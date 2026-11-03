#!/usr/bin/env bash
cd "$(dirname "$0")/.."

if git grep -nIE '[A-Za-z]\.tsx?:[0-9]' -- . ':!eng'; then
    echo "Cite TanStack by symbol or test name, not line number (see AGENTS.md)." >&2
    exit 1
fi
