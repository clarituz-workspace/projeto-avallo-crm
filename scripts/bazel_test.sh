#!/usr/bin/env bash
set -euo pipefail

cd "${BUILD_WORKSPACE_DIRECTORY}"

dotnet test Avallo.slnx -c Release --nologo
