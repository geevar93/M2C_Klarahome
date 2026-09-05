#!/usr/bin/env bash
#
# Runs the Klara Home quality gates - the same ones CI runs, in the same order.
#
#   ./tools/ci.sh                       every gate except `package`
#   ./tools/ci.sh format test           only those two
#   ./tools/ci.sh package               build both images and scan them with Trivy
#   ./tools/ci.sh --coverage-minimum 80 raise the line-coverage floor for this run
#   ./tools/ci.sh --skip-integration    accept the gap when there is no Docker daemon
#
# This is a thin wrapper around tools/ci.ps1, NOT a second implementation. The gates involve
# reading TRX counters and cobertura line rates, and two hand-written parsers of the same XML would
# eventually disagree about whether the build passes - the one failure a quality gate must never
# have. PowerShell 7 is the common denominator: it ships with the Windows dev setup and is
# preinstalled on every GitHub-hosted runner.
#
set -euo pipefail

if ! command -v pwsh >/dev/null 2>&1; then
    cat >&2 <<'EOF'
error: pwsh (PowerShell 7+) is not on PATH.

  Windows : it is already installed; open a new shell, or use tools\ci.ps1 directly
  Linux   : https://learn.microsoft.com/powershell/scripting/install/install-ubuntu
  macOS   : brew install --cask powershell
EOF
    exit 127
fi

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

# Git Bash and MSYS hand out POSIX paths (/c/work/...) that a native Windows pwsh cannot resolve.
# cygpath exists precisely on the shells that do this, so its presence is the test.
if command -v cygpath >/dev/null 2>&1; then
    script_dir="$(cygpath -w "$script_dir")"
fi

stages=()
args=()

while [ $# -gt 0 ]; do
    case "$1" in
        --coverage-minimum)
            [ $# -ge 2 ] || { echo "error: --coverage-minimum needs a value" >&2; exit 2; }
            args+=(-CoverageMinimum "$2")
            shift 2
            ;;
        --skip-integration|--skip-integration-tests)
            args+=(-SkipIntegrationTests)
            shift
            ;;
        -h|--help)
            sed -n '2,15p' "${BASH_SOURCE[0]}" | sed 's/^# \{0,1\}//'
            exit 0
            ;;
        -*)
            echo "error: unknown option $1" >&2
            exit 2
            ;;
        *)
            stages+=("$1")
            shift
            ;;
    esac
done

if [ ${#stages[@]} -gt 0 ]; then
    # -File would pass the list as one string; -Command lets PowerShell parse it as an array.
    joined="$(IFS=,; echo "${stages[*]}")"
    args+=(-Stage "$joined")
fi

exec pwsh -NoProfile -NoLogo -Command "& '$script_dir/ci.ps1' ${args[*]}"
