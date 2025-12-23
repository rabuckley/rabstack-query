#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
WORK_DIR=""

cleanup() { [[ -n "$WORK_DIR" ]] && rm -rf "$WORK_DIR"; }
trap cleanup EXIT

# -- Colors (disabled when piped) --

if [[ -t 1 ]]; then
    RED=$'\033[31m' YELLOW=$'\033[33m' GREEN=$'\033[32m' CYAN=$'\033[36m'
    BOLD=$'\033[1m' DIM=$'\033[2m' RESET=$'\033[0m'
else
    RED='' YELLOW='' GREEN='' CYAN='' BOLD='' DIM='' RESET=''
fi

die() { printf '%serror:%s %s\n' "$RED" "$RESET" "$*" >&2; exit 1; }

# -- Helpers --

# Convert HH:MM:SS.FFFFFFF (TimeSpan) to integer milliseconds.
duration_to_ms() {
    awk -F'[:.]' '{
        h=$1+0; m=$2+0; s=$3+0; f=$4+0
        flen = length($4)
        if (flen > 3) f = int(f / 10^(flen-3))
        else while (flen++ < 3) f *= 10
        print (h*3600 + m*60 + s)*1000 + f
    }' <<< "$1"
}

# Pretty-print a millisecond value.
format_duration() {
    local ms=$1
    if (( ms >= 60000 )); then
        printf '%dm%ds' $((ms / 60000)) $(( (ms % 60000) / 1000 ))
    elif (( ms >= 1000 )); then
        awk "BEGIN { printf \"%.1fs\", $ms / 1000 }"
    else
        printf '%dms' "$ms"
    fi
}

# Parse a timeout string (30s, 2m, 1h) to milliseconds.
parse_timeout_to_ms() {
    local num unit
    num="${1//[!0-9]/}"
    unit="${1//[0-9]/}"
    case "$unit" in
        s|"") echo $(( num * 1000 )) ;;
        m)    echo $(( num * 60000 )) ;;
        h)    echo $(( num * 3600000 )) ;;
        *)    echo $(( num * 1000 )) ;;
    esac
}

# Delete stale TRX output directories so we only pick up fresh results.
clean_trx() {
    rm -rf "$REPO_ROOT"/test/*/TestResults
}

# Parse all .trx files under a directory into tab-separated rows:
#   test_name \t duration_ms \t outcome
parse_trx() {
    local dir="$1"
    local trx_file

    while IFS= read -r -d '' trx_file; do
        # Strip XML namespaces, then extract UnitTestResult attributes.
        sed 's/ xmlns="[^"]*"//g; s/ xmlns:[a-zA-Z]*="[^"]*"//g' "$trx_file" \
        | grep -o '<UnitTestResult [^>]*>' \
        | while IFS= read -r line; do
            local name duration outcome ms
            name=$(sed -n 's/.*testName="\([^"]*\)".*/\1/p' <<< "$line")
            duration=$(sed -n 's/.*duration="\([^"]*\)".*/\1/p' <<< "$line")
            outcome=$(sed -n 's/.*outcome="\([^"]*\)".*/\1/p' <<< "$line")
            [[ -z "$name" ]] && continue
            ms=$(duration_to_ms "${duration:-0:00:00}")
            printf '%s\t%s\t%s\n' "$name" "$ms" "$outcome"
        done
    done < <(find "$dir" -name '*.trx' -type f -print0 2>/dev/null)
}

# Run dotnet test with TRX reporting into a clean results directory.
# Extra arguments are forwarded to dotnet test.
# When no --project is specified, runs each test project individually
# to avoid building non-test projects (e.g. MAUI example apps).
run_with_trx() {
    local results_dir="$1"; shift
    clean_trx

    # If the user specified --project, run exactly what they asked for.
    local has_project=false
    for arg in "$@"; do
        [[ "$arg" == "--project" ]] && has_project=true
    done

    local rc=0
    if [[ "$has_project" == "true" ]]; then
        (cd "$REPO_ROOT" && dotnet test --report-trx --results-directory "$results_dir" "$@") || rc=$?
    else
        # Discover test projects and run each one.
        while IFS= read -r proj; do
            (cd "$REPO_ROOT" && dotnet test --report-trx --results-directory "$results_dir" \
                --project "$proj" "$@") || rc=$?
        done < <(find "$REPO_ROOT/test" -name '*.csproj' -type f)
    fi

    return $rc
}

# -- Subcommands --

cmd_slow() {
    local threshold=500
    local extra_args=()

    while [[ $# -gt 0 ]]; do
        case "$1" in
            --threshold) threshold="$2"; shift 2 ;;
            --)          shift; extra_args=("$@"); break ;;
            *)           extra_args+=("$1"); shift ;;
        esac
    done

    printf '%sRunning tests to collect timing data...%s\n\n' "$BOLD" "$RESET"

    local results_dir="$WORK_DIR/trx"
    run_with_trx "$results_dir" "${extra_args[@]+"${extra_args[@]}"}" || true

    local results="$WORK_DIR/results.tsv"
    parse_trx "$results_dir" | sort -t$'\t' -k2 -rn > "$results"

    local total slow_count
    total=$(wc -l < "$results" | tr -d ' ')
    slow_count=$(awk -F'\t' -v t="$threshold" '$2 >= t' "$results" | wc -l | tr -d ' ')

    printf '\n%sTest Duration Report%s (threshold: %sms)\n' "$BOLD" "$RESET" "$threshold"
    printf '%.0s=' {1..60}; printf '\n'

    if [[ "$total" -eq 0 ]]; then
        printf '%sNo test results found. Is --report-trx working?%s\n' "$YELLOW" "$RESET"
        return
    fi

    while IFS=$'\t' read -r name ms outcome; do
        local dur color
        dur=$(format_duration "$ms")

        if (( ms >= threshold )); then
            color="$RED"
        elif (( ms >= threshold / 2 )); then
            color="$YELLOW"
        else
            color="$GREEN"
        fi

        local status=""
        [[ "$outcome" != "Passed" ]] && status=" ${DIM}[$outcome]${RESET}"

        printf '  %s%8s%s  %s%s\n' "$color" "$dur" "$RESET" "$name" "$status"
    done < "$results"

    printf '\n%sSummary:%s %d tests, %s%d slow%s (>=%sms)\n' \
        "$BOLD" "$RESET" "$total" "$RED" "$slow_count" "$RESET" "$threshold"
}

cmd_hanging() {
    local timeout="30s"
    local extra_args=()

    while [[ $# -gt 0 ]]; do
        case "$1" in
            --timeout) timeout="$2"; shift 2 ;;
            --)        shift; extra_args=("$@"); break ;;
            *)         extra_args+=("$1"); shift ;;
        esac
    done

    local timeout_ms
    timeout_ms=$(parse_timeout_to_ms "$timeout")

    printf '%sRunning tests with %s timeout + hang dump...%s\n\n' "$BOLD" "$timeout" "$RESET"

    local results_dir="$WORK_DIR/trx"
    run_with_trx "$results_dir" \
        --timeout "$timeout" \
        --hangdump --hangdump-timeout "$timeout" \
        "${extra_args[@]+"${extra_args[@]}"}" || true

    local results="$WORK_DIR/results.tsv"
    parse_trx "$results_dir" > "$results"

    # Tests that didn't pass or fail normally (timed out, cancelled, etc.)
    local hung="$WORK_DIR/hung.tsv"
    awk -F'\t' '$3 != "Passed" && $3 != "Failed"' "$results" > "$hung"

    # Tests that used >80% of the timeout budget
    local near_limit="$WORK_DIR/near_limit.tsv"
    awk -F'\t' -v limit="$(( timeout_ms * 80 / 100 ))" \
        '($3 == "Passed" || $3 == "Failed") && $2 >= limit' "$results" \
        | sort -t$'\t' -k2 -rn > "$near_limit"

    printf '\n%sHang Detection Report%s (timeout: %s)\n' "$BOLD" "$RESET" "$timeout"
    printf '%.0s=' {1..60}; printf '\n'

    local hung_count near_count total
    hung_count=$(wc -l < "$hung" | tr -d ' ')
    near_count=$(wc -l < "$near_limit" | tr -d ' ')
    total=$(wc -l < "$results" | tr -d ' ')

    if (( hung_count > 0 )); then
        printf '\n%s%sTimed out / incomplete:%s\n' "$RED" "$BOLD" "$RESET"
        while IFS=$'\t' read -r name ms outcome; do
            printf '  %s%-12s%s  %s  %s(%s)%s\n' \
                "$RED" "$outcome" "$RESET" "$name" "$DIM" "$(format_duration "$ms")" "$RESET"
        done < "$hung"
    fi

    if (( near_count > 0 )); then
        printf '\n%s%sNear timeout (>80%%):%s\n' "$YELLOW" "$BOLD" "$RESET"
        while IFS=$'\t' read -r name ms outcome; do
            local pct=$(( ms * 100 / timeout_ms ))
            printf '  %s%8s%s (%d%%)  %s\n' \
                "$YELLOW" "$(format_duration "$ms")" "$RESET" "$pct" "$name"
        done < "$near_limit"
    fi

    if (( hung_count == 0 && near_count == 0 )); then
        printf '\n%sAll %d tests completed well within the timeout.%s\n' "$GREEN" "$total" "$RESET"
    else
        printf '\n%sSummary:%s %d tests, %s%d hung%s, %s%d near limit%s\n' \
            "$BOLD" "$RESET" "$total" "$RED" "$hung_count" "$RESET" "$YELLOW" "$near_count" "$RESET"
    fi

    # Point to any dump files that were generated.
    local dumps
    dumps=$(find "$WORK_DIR" "$REPO_ROOT/test" -name '*.dmp' -type f 2>/dev/null || true)
    if [[ -n "$dumps" ]]; then
        printf '\n%sDump files:%s\n' "$BOLD" "$RESET"
        while IFS= read -r f; do
            printf '  %s\n' "$f"
        done <<< "$dumps"
    fi
}

cmd_flaky() {
    local runs=5
    local extra_args=()

    while [[ $# -gt 0 ]]; do
        case "$1" in
            --runs) runs="$2"; shift 2 ;;
            --)     shift; extra_args=("$@"); break ;;
            *)      extra_args+=("$1"); shift ;;
        esac
    done

    printf '%sRunning tests %d times to detect flaky tests...%s\n\n' "$BOLD" "$runs" "$RESET"

    local all_results="$WORK_DIR/all_results.tsv"
    : > "$all_results"

    for (( i = 1; i <= runs; i++ )); do
        printf '%sRun %d/%d%s ' "$DIM" "$i" "$runs" "$RESET"

        local run_dir="$WORK_DIR/run_$i"
        clean_trx

        # Build only on first run.
        local build_flag=()
        (( i > 1 )) && build_flag=(--no-build)

        # Reuse run_with_trx but capture output to a log file.
        local rc=0
        run_with_trx "$run_dir" \
            "${build_flag[@]+"${build_flag[@]}"}" \
            "${extra_args[@]+"${extra_args[@]}"}" > "$WORK_DIR/run_${i}_log.txt" 2>&1 || rc=$?

        # Parse this run's TRX, tagging each row with the run number.
        while IFS=$'\t' read -r name ms outcome; do
            printf '%s\t%s\t%d\n' "$name" "$outcome" "$i" >> "$all_results"
        done < <(parse_trx "$run_dir")

        # Show a one-line summary for this run.
        local passed failed
        passed=$(awk -F'\t' -v r="$i" '$3 == r && $2 == "Passed"' "$all_results" | wc -l | tr -d ' ')
        failed=$(awk -F'\t' -v r="$i" '$3 == r && $2 != "Passed"' "$all_results" | wc -l | tr -d ' ')

        if (( failed > 0 )); then
            printf '%s%d passed%s, %s%d failed%s (exit %d)\n' "$GREEN" "$passed" "$RESET" "$RED" "$failed" "$RESET" "$rc"
        else
            printf '%s%d passed%s (exit %d)\n' "$GREEN" "$passed" "$RESET" "$rc"
        fi
    done

    printf '\n%sFlaky Test Report%s (%d runs)\n' "$BOLD" "$RESET" "$runs"
    printf '%.0s=' {1..60}; printf '\n'

    # Find tests with mixed outcomes across runs.
    local flaky="$WORK_DIR/flaky.tsv"
    awk -F'\t' '{
        key = $1
        outcomes[key] = outcomes[key] " " $2
        counts[key]++
        if ($2 == "Passed") passed[key]++
    } END {
        for (k in outcomes) {
            p = (k in passed) ? passed[k] : 0
            total = counts[k]
            if (p > 0 && p < total) {
                printf "%s\t%d\t%d\t%.0f\n", k, p, total, (p/total)*100
            }
        }
    }' "$all_results" | sort -t$'\t' -k4 -n > "$flaky"

    local flaky_count
    flaky_count=$(wc -l < "$flaky" | tr -d ' ')

    if (( flaky_count > 0 )); then
        printf '\n%s%sFlaky tests detected:%s\n\n' "$RED" "$BOLD" "$RESET"
        while IFS=$'\t' read -r name p total pct; do
            local color
            if (( pct < 50 )); then color="$RED"
            elif (( pct < 80 )); then color="$YELLOW"
            else color="$CYAN"
            fi

            # Show which runs failed.
            local fail_runs
            fail_runs=$(awk -F'\t' -v n="$name" '$1 == n && $2 != "Passed" { printf "%s ", $3 }' "$all_results")
            printf '  %s%3d%% pass%s (%d/%d)  %s\n' "$color" "$pct" "$RESET" "$p" "$total" "$name"
            printf '  %s  failed in runs: %s%s\n' "$DIM" "$fail_runs" "$RESET"
        done < "$flaky"
    else
        local unique_tests
        unique_tests=$(awk -F'\t' '!seen[$1]++' "$all_results" | wc -l | tr -d ' ')
        printf '\n%sNo flaky tests detected across %d tests in %d runs.%s\n' \
            "$GREEN" "$unique_tests" "$runs" "$RESET"
    fi

    # Report tests that failed every single run (not flaky, just broken).
    local consistent="$WORK_DIR/consistent.tsv"
    awk -F'\t' '{
        key = $1; counts[key]++
        if ($2 != "Passed") failed[key]++
    } END {
        for (k in failed) if (failed[k] == counts[k]) print k
    }' "$all_results" | sort > "$consistent"

    local fail_count
    fail_count=$(wc -l < "$consistent" | tr -d ' ')

    if (( fail_count > 0 )); then
        printf '\n%s%sConsistently failing (%d/%d runs):%s\n' "$RED" "$BOLD" "$runs" "$runs" "$RESET"
        while IFS= read -r name; do
            printf '  %sx%s %s\n' "$RED" "$RESET" "$name"
        done < "$consistent"
    fi

    printf '\n%sSummary:%s %s%d flaky%s, %s%d always failing%s\n' \
        "$BOLD" "$RESET" "$RED" "$flaky_count" "$RESET" "$RED" "$fail_count" "$RESET"

    if (( flaky_count > 0 )); then
        printf '\n%sPer-run logs in: %s%s\n' "$DIM" "$WORK_DIR" "$RESET"
    fi
}

# -- Usage --

usage() {
    cat <<EOF
Usage: $(basename "$0") <command> [options] [-- dotnet test args...]

Diagnose hanging, slow, and flaky tests using MTP v2 extensions.

Commands:
  slow     [--threshold MS]     Rank all tests by duration (default: 500ms)
  hanging  [--timeout DURATION] Find tests that don't complete (default: 30s)
  flaky    [--runs N]           Run tests N times, find inconsistent results (default: 5)

Extra arguments after -- are forwarded to dotnet test. For example:
  $(basename "$0") slow -- --project test/RabstackQuery.Tests/RabstackQuery.Tests.csproj
  $(basename "$0") flaky --runs 3 -- --filter-method "*Observer*"

Prerequisites (already configured for this project):
  - Microsoft.Testing.Extensions.TrxReport   (enables --report-trx)
  - Microsoft.Testing.Extensions.HangDump    (enables --hangdump)
  - xunit.runner.json longRunningTestSeconds  (real-time slow test warnings)
EOF
    exit 1
}

# -- Main --

WORK_DIR=$(mktemp -d)
[[ $# -eq 0 ]] && usage

cmd="$1"; shift
case "$cmd" in
    slow)        cmd_slow "$@" ;;
    hanging)     cmd_hanging "$@" ;;
    flaky)       cmd_flaky "$@" ;;
    -h|--help|help) usage ;;
    *)           die "unknown command: $cmd" ;;
esac
