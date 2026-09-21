#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="${ROOT_DIR:-$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)}"
PROJECT_PATH="${PROJECT_PATH:-${ROOT_DIR}/TestProject~}"
RESULTS_DIR="${RESULTS_DIR:-${PROJECT_PATH}/TestResults~}"
RESULTS_XML="${RESULTS_DIR}/editmode-results.xml"
EDITOR_LOG="${RESULTS_DIR}/editor.log"

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
echo "[unity-local-test] Using Unity: ${UNITY_EXECUTABLE}"
echo "[unity-local-test] Project: ${PROJECT_PATH}"
echo "[unity-local-test] Results: ${RESULTS_XML}"

set +e
"${UNITY_EXECUTABLE}" \
  -batchmode \
  -nographics \
  -projectPath "${PROJECT_PATH}" \
  -runTests \
  -testPlatform EditMode \
  -assemblyNames AdzukiSoft.ALPS.EditorTests \
  -testResults "${RESULTS_XML}" \
  -logFile "${EDITOR_LOG}"
# Do not add -quit here: with Unity 2022.3 Test Framework it can preempt
# -runTests and exit before writing a fresh test result XML.
exit_code=$?
set -e

echo "[unity-local-test] Unity exited with code ${exit_code}"
exit "${exit_code}"
