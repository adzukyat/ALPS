#!/usr/bin/env bash
# Shared by the test scripts, which source it.

ROOT_DIR="${ROOT_DIR:-$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)}"
PROJECT_PATH="${PROJECT_PATH:-${ROOT_DIR}/TestProject~}"
RESULTS_DIR="${RESULTS_DIR:-${PROJECT_PATH}/TestResults~}"

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

  local candidate
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

  echo "Unity 2022.3.22f1 was not found. Set UNITY_EXECUTABLE." >&2
  return 1
}

# run_unity_tests <project> <assembly> <results xml> <editor log> [more Unity arguments]
# Returns Unity's exit code.
run_unity_tests() {
  local project="$1"
  local assembly="$2"
  local results_xml="$3"
  local editor_log="$4"
  shift 4

  # A run that dies before it writes results must not leave the last run's to be read.
  rm -f "${results_xml}"

  # No -nographics: the show is evaluated on the GPU, so the tests need a graphics device.
  # No -quit: with Unity 2022.3 Test Framework it can preempt -runTests and exit before
  # writing a fresh test result XML.
  # Burst comes with the VRChat SDK and nothing under test uses it, but starting its
  # compiler takes about four seconds of every start and every entry into play mode.
  "${UNITY_EXECUTABLE}" \
    -batchmode \
    -projectPath "${project}" \
    -runTests \
    -testPlatform EditMode \
    -assemblyNames "${assembly}" \
    -testResults "${results_xml}" \
    -logFile "${editor_log}" \
    --burst-disable-compilation \
    "$@"
}

# The value of an attribute of the results' test-run element, or nothing.
results_attribute() {
  local results_xml="$1"
  local attribute="$2"
  [[ -f "${results_xml}" ]] || return 0
  grep -o '<test-run [^>]*>' "${results_xml}" | head -n 1 \
    | sed -n "s/.* ${attribute}=\"\\([^\"]*\\)\".*/\\1/p"
}

# The full names of the test cases that failed, one per line.
failed_tests() {
  local results_xml="$1"
  [[ -f "${results_xml}" ]] || return 0
  grep -o '<test-case [^>]*result="Failed"[^>]*>' "${results_xml}" \
    | sed -n 's/.* fullname="\([^"]*\)".*/\1/p' \
    | sed -e 's/&quot;/"/g' -e 's/&lt;/</g' -e 's/&gt;/>/g' -e 's/&amp;/\&/g'
}
