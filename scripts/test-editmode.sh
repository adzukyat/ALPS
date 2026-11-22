#!/usr/bin/env bash
set -euo pipefail

source "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/unity-test-common.sh"

RESULTS_XML="${RESULTS_DIR}/editmode-results.xml"
EDITOR_LOG="${RESULTS_DIR}/editor.log"

mkdir -p "${RESULTS_DIR}"

"${ROOT_DIR}/scripts/check-package-metadata.sh"
"${ROOT_DIR}/scripts/bootstrap-test-project.sh"

UNITY_EXECUTABLE="$(find_unity)"
echo "[unity-local-test] Using Unity: ${UNITY_EXECUTABLE}"
echo "[unity-local-test] Project: ${PROJECT_PATH}"
echo "[unity-local-test] Results: ${RESULTS_XML}"

# Arguments go on to Unity, so a filter picks tests: test-editmode.sh -testFilter "Name"
set +e
run_unity_tests "${PROJECT_PATH}" AdzukiSoft.ALPS.EditorTests "${RESULTS_XML}" "${EDITOR_LOG}" "$@"
exit_code=$?
set -e

echo "[unity-local-test] Unity exited with code ${exit_code}"
exit "${exit_code}"
