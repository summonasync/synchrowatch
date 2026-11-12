#!/usr/bin/env bash
set -euo pipefail

VERSION="${1:?usage: release-syncrowatch.sh <version>}"
if [[ ! "${VERSION}" =~ ^[A-Za-z0-9._-]+$ ]]; then
    echo "FAIL: version '${VERSION}' may only contain letters, digits, dot, underscore, dash" >&2
    exit 1
fi
CONFIG="${SYNCROWATCH_CONFIG:-Release}"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"
PROJECT="${REPO_ROOT}/src/Syncrowatch.App/Syncrowatch.App.csproj"
DIST="${REPO_ROOT}/dist/syncrowatch-${VERSION}"

if ! command -v dotnet >/dev/null 2>&1; then
    echo "FAIL: dotnet SDK not found on PATH" >&2
    exit 1
fi

cd "${REPO_ROOT}"

rm -rf "${DIST}"
mkdir -p "${DIST}/linux-x64" "${DIST}/win-x64"

echo "==> syncrowatch release ${VERSION} (${CONFIG}, self-contained single-file, built from Ubuntu)"
echo "==> linux-x64"
dotnet publish "${PROJECT}" \
    -c "${CONFIG}" \
    -r linux-x64 \
    --self-contained true \
    -p:PublishSingleFile=true \
    -p:IncludeNativeLibrariesForSelfExtract=true \
    -o "${DIST}/linux-x64"

echo "==> win-x64"
dotnet publish "${PROJECT}" \
    -c "${CONFIG}" \
    -r win-x64 \
    --self-contained true \
    -p:PublishSingleFile=true \
    -p:IncludeNativeLibrariesForSelfExtract=true \
    -o "${DIST}/win-x64"

LINUX_BIN="${DIST}/linux-x64/syncrowatch"
WIN_BIN="${DIST}/win-x64/syncrowatch.exe"

echo "==> verifying artifacts"
for artifact in "${LINUX_BIN}" "${WIN_BIN}"; do
    if [[ ! -f "${artifact}" ]]; then
        echo "FAIL: expected artifact missing: ${artifact}" >&2
        exit 1
    fi
done

echo "==> artifacts"
printf '  %-72s %10s\n' "${LINUX_BIN}" "$(du -h "${LINUX_BIN}" | cut -f1)"
printf '  %-72s %10s\n' "${WIN_BIN}" "$(du -h "${WIN_BIN}" | cut -f1)"
echo "==> release folder ready: ${DIST}"
