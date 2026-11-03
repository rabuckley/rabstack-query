#!/usr/bin/env bash
# Checks out the TanStack Query commit pinned in eng/tanstack.ref into
# .upstream/, limited to the query-core and react-query sources and tests.
set -euo pipefail
cd "$(dirname "$0")/.."

pin="$(grep -v '^#' eng/tanstack.ref | tr -d '[:space:]')"
# Reuse the pinned checkout so offline sessions do not need a fetch.
[[ "$(git -C .upstream rev-parse HEAD 2>/dev/null)" == "$pin" ]] && exit 0

git init -q .upstream
git -C .upstream sparse-checkout set packages/query-core/src packages/react-query/src
git -C .upstream fetch -q --depth=1 --filter=blob:none https://github.com/TanStack/query.git "$pin"
git -C .upstream -c advice.detachedHead=false checkout -q "$pin"
echo "Checked out TanStack Query $pin into .upstream/."
