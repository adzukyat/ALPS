#!/usr/bin/env bash
set -euo pipefail

source "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/unity-test-common.sh"

RESULTS_XML="${RESULTS_DIR}/editor-ui-results.xml"
EDITOR_LOG="${RESULTS_DIR}/editor-ui.log"

mkdir -p "${RESULTS_DIR}"

"${ROOT_DIR}/scripts/check-package-metadata.sh"
"${ROOT_DIR}/scripts/bootstrap-test-project.sh"

UNITY_EXECUTABLE="$(find_unity)"
echo "[unity-editor-ui-test] Using Unity: ${UNITY_EXECUTABLE}"
echo "[unity-editor-ui-test] Project: ${PROJECT_PATH}"
echo "[unity-editor-ui-test] Results: ${RESULTS_XML}"

# Arguments go on to Unity, so a filter picks tests: test-editor-ui.sh -testFilter "Name"
set +e
run_unity_tests "${PROJECT_PATH}" AdzukiSoft.ALPS.EditorUiTests "${RESULTS_XML}" "${EDITOR_LOG}" "$@"
exit_code=$?
set -e

echo "[unity-editor-ui-test] Unity exited with code ${exit_code}"
exit "${exit_code}"
