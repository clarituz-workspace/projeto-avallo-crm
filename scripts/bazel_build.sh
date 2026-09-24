#!/usr/bin/env bash
set -euo pipefail

cd "${BUILD_WORKSPACE_DIRECTORY}"

dotnet build Avallo.Web/Avallo.Web.csproj -c Release --nologo
