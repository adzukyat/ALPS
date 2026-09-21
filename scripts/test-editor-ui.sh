#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="${ROOT_DIR:-$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)}"
PROJECT_PATH="${PROJECT_PATH:-${ROOT_DIR}/TestProject~}"
RESULTS_DIR="${RESULTS_DIR:-${PROJECT_PATH}/TestResults~}"
RESULTS_XML="${RESULTS_DIR}/editor-ui-results.xml"
EDITOR_LOG="${RESULTS_DIR}/editor-ui.log"

mkdir -p "${RESULTS_DIR}"

"${ROOT_DIR}/scripts/check-package-metadata.sh"
"${ROOT_DIR}/scripts/bootstrap-test-project.sh"

find_unity() {
  if [[ -n "${UNITY_EXECUTABLE:-}" ]]; then
    if [[ -x "${UNITY_EXECUTABLE}" ]]; then
      printf '%s\n' "${UNITY_EXECUTABLE}"
      return 0
    fi

    echo "UNITY_EXECUTABLE is set but is not executable: ${UNITY_EXECUTABLE}" >&2
    return 1
  fi

  local candidates=(
    "unity-editor"
    "/Applications/Unity/Hub/Editor/2022.3.22f1/Unity.app/Contents/MacOS/Unity"
    "/c/Program Files/Unity/Hub/Editor/2022.3.22f1/Editor/Unity.exe"
    "/mnt/c/Program Files/Unity/Hub/Editor/2022.3.22f1/Editor/Unity.exe"
    "C:/Program Files/Unity/Hub/Editor/2022.3.22f1/Editor/Unity.exe"
  )

  for candidate in "${candidates[@]}"; do
    if command -v "${candidate}" >/dev/null 2>&1; then
      command -v "${candidate}"
      return 0
    fi

    if [[ -x "${candidate}" ]]; then
      printf '%s\n' "${candidate}"
      return 0
    fi
  done

  return 1
}

UNITY_EXECUTABLE="$(find_unity)"
echo "[unity-editor-ui-test] Using Unity: ${UNITY_EXECUTABLE}"
echo "[unity-editor-ui-test] Project: ${PROJECT_PATH}"
echo "[unity-editor-ui-test] Results: ${RESULTS_XML}"

set +e
"${UNITY_EXECUTABLE}" \
  -batchmode \
  -projectPath "${PROJECT_PATH}" \
  -runTests \
  -testPlatform EditMode \
  -assemblyNames AdzukiSoft.ALPS.EditorUiTests \
  -testResults "${RESULTS_XML}" \
  -logFile "${EDITOR_LOG}"
exit_code=$?
set -e

echo "[unity-editor-ui-test] Unity exited with code ${exit_code}"
exit "${exit_code}"
