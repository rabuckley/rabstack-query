#!/usr/bin/env bash
#   (no arguments)   Fail on stale citations, stale ledger rows or an outdated summary.
#   --update         Regenerate the summary.
#   --list           Print the tests nobody has reviewed yet.
#   --since <sha>    Print the test keys added and removed since an older upstream commit.
set -euo pipefail
cd "$(dirname "$0")/.."

eng/fetch-tanstack.sh
exec dotnet run eng/upstream-tests.cs -- "$@"
