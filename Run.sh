#!/usr/bin/env bash
# Bash arrays preserve native argv without eval.
set -Eeuo pipefail
ROOT="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)"
BUILD="$ROOT/Build"
source "$ROOT/Scripts/Runner.sh"
source "$ROOT/Scripts/Suite.sh"
main "$@"
