#!/usr/bin/env bash
set -euo pipefail

cd "${BUILD_WORKSPACE_DIRECTORY}"

OUT="${BUILD_WORKSPACE_DIRECTORY}/bazel-bin/publish"
rm -rf "${OUT}"

dotnet publish Avallo.Web/Avallo.Web.csproj \
  -c Release \
  -o "${OUT}" \
  --nologo \
  /p:UseSharedCompilation=false
