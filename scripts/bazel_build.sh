#!/usr/bin/env bash
set -euo pipefail

if [ -n "${BUILD_WORKSPACE_DIRECTORY:-}" ]; then
  WORKSPACE_DIR="${BUILD_WORKSPACE_DIRECTORY}"
elif [ -n "${TEST_SRCDIR:-}" ] && [ -n "${TEST_WORKSPACE:-}" ]; then
  WORKSPACE_DIR="${TEST_SRCDIR}/${TEST_WORKSPACE}"
else
  echo "Nao foi possivel determinar o diretorio do workspace" >&2
  exit 1
fi

cd "${WORKSPACE_DIR}"

dotnet build Avallo.Web/Avallo.Web.csproj -c Release --nologo
