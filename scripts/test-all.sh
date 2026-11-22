#!/usr/bin/env bash
set -euo pipefail

source "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/unity-test-common.sh"

# Runs every test, as several Unity instances at once. Starting Unity takes most of the time
# a run needs and no start can be shared, so the test classes are dealt out to ALPS_TEST_JOBS
# instances by how long each took the last time, and a run takes one start plus the slowest
# share. More instances than the processor has room for slow every start down, so the
# default follows the number of threads.
#
# Unity opens a project once, so each instance gets a worker: a copy of the package and the
# test project with a Library of its own, under WORKERS_DIR, brought up to date before every
# run. Nothing is shared between them, so no instance sees what the tests of another write,
# and nothing is written to the test project or the package themselves, which may be open in
# Unity meanwhile. A worker's Library is about 2 GB and is copied from the test project's
# the first time.
WORKERS_DIR="${ALPS_TEST_WORKERS_DIR:-${ROOT_DIR}/.cache/test-workers}"
TIMINGS="${ALPS_TEST_TIMINGS:-${ROOT_DIR}/.cache/test-timings.tsv}"
ASSEMBLIES="AdzukiSoft.ALPS.EditorTests;AdzukiSoft.ALPS.EditorUiTests"
PROJECT_NAME="$(basename "${PROJECT_PATH}")"

# What UdonSharp compiles into a project. A worker keeps its own, or the copy of the test
# project's, which may be older, would replace what the worker just compiled.
COMPILED=(SerializedUdonPrograms SerializedUdonPrograms.meta UdonSharp UdonSharp.meta)

default_jobs() {
  local threads
  threads="$(nproc 2>/dev/null || sysctl -n hw.ncpu 2>/dev/null || echo 4)"
  local jobs=$(( threads * 3 / 8 ))
  if (( jobs < 1 )); then
    jobs=1
  elif (( jobs > 8 )); then
    jobs=8
  fi

  echo "${jobs}"
}

JOBS="${ALPS_TEST_JOBS:-$(default_jobs)}"

# A worker rebuilds the layout the test project refers to the package by, so a test project
# somewhere else is run where it is, by one instance.
USE_WORKERS=1
if [[ "$(dirname "${PROJECT_PATH}")" != "${ROOT_DIR}" ]]; then
  USE_WORKERS=0
  JOBS=1
fi

# The names of the classes in the test folders, one per line. Every test is in one of them,
# and a name that is no test class matches nothing.
test_classes() {
  find "${ROOT_DIR}/Tests" -name '*.cs' -print0 \
    | xargs -0 grep -hoE '(^|[[:space:]])class[[:space:]]+[A-Za-z_][A-Za-z0-9_]*' \
    | awk '{ print $NF }' | sort -u
}

# Deals the classes on stdin out to <jobs> shares, the slowest first and each to the share
# that is the shortest so far, and prints "share class" lines. A class with no time yet
# counts as a second.
deal_classes() {
  local jobs="$1"
  local timings="$2"
  awk -v jobs="${jobs}" -v timings="${timings}" '
    BEGIN {
      while ((getline line < timings) > 0) {
        split(line, field, "\t")
        seconds[field[1]] = field[2] + 0
      }
    }
    {
      class[NR] = $1
      weight[NR] = ($1 in seconds) ? seconds[$1] : 1
    }
    END {
      for (i = 1; i <= NR; i++) {
        taken[i] = 0
      }
      for (n = 1; n <= NR; n++) {
        best = 0
        for (i = 1; i <= NR; i++) {
          if (!taken[i] && (best == 0 || weight[i] > weight[best])) {
            best = i
          }
        }
        taken[best] = 1
        share = 1
        for (j = 2; j <= jobs; j++) {
          if (load[j] + 0 < load[share] + 0) {
            share = j
          }
        }
        load[share] += weight[best]
        print share, class[best]
      }
    }'
}

# Writes how long each class took, from the results of a run, for the next run to deal by.
# A class takes far longer where an instance does something for the first time (compiling
# the shaders of a bundle, say), and a deal that follows every such time moves the classes
# to other workers, which then start from nothing again. So a time moves half the way to
# what was measured, and the deal settles.
write_timings() {
  local timings="$1"
  shift
  mkdir -p "$(dirname "${timings}")"
  cat "$@" \
    | grep -o '<test-case [^>]*>' \
    | sed -n 's/.* classname="\([^"]*\)".* duration="\([^"]*\)".*/\1 \2/p' \
    | awk -v timings="${timings}" '
        BEGIN {
          while ((getline line < timings) > 0) {
            split(line, field, "\t")
            before[field[1]] = field[2] + 0
          }
        }
        { n = split($1, part, "."); seconds[part[n]] += $2 }
        END {
          for (name in seconds) {
            value = (name in before) ? (before[name] + seconds[name]) / 2 : seconds[name]
            printf "%s\t%.2f\n", name, value
          }
        }' \
    | sort > "${timings}.new"
  mv "${timings}.new" "${timings}"
}

# mirror_dir <source> <destination> [name...]
# Makes <destination> the same as <source>, copying only what changed and keeping the
# times, so Unity imports nothing again. The names, directly inside, are left as they are
# on both sides.
mirror_dir() {
  local source="$1"
  local destination="$2"
  shift 2
  mkdir -p "${destination}"

  local name
  if command -v rsync >/dev/null 2>&1; then
    local excludes=()
    for name in "$@"; do
      excludes+=("--exclude=/${name}")
    done

    rsync -a --delete ${excludes[@]+"${excludes[@]}"} "${source}/" "${destination}/"
  elif command -v robocopy >/dev/null 2>&1 && command -v cygpath >/dev/null 2>&1; then
    # Git Bash has no rsync. Robocopy leaves a path alone that is named on the side it is on,
    # whether it is a file or a directory.
    local excludes=()
    for name in "$@"; do
      excludes+=("$(cygpath -w "${source}/${name}")" "$(cygpath -w "${destination}/${name}")")
    done

    if (( ${#excludes[@]} > 0 )); then
      excludes=(/XD "${excludes[@]}" /XF "${excludes[@]}")
    fi

    # Without the two variables Git Bash rewrites /MIR as a path.
    local code=0
    MSYS_NO_PATHCONV=1 MSYS2_ARG_CONV_EXCL='*' robocopy \
      "$(cygpath -w "${source}")" "$(cygpath -w "${destination}")" \
      /MIR /MT:16 /R:2 /W:1 /NFL /NDL /NJH /NJS /NP ${excludes[@]+"${excludes[@]}"} >/dev/null || code=$?
    # Robocopy counts what it copied in its exit code, and fails from 8 on.
    (( code < 8 ))
  else
    local entry kept
    for entry in "${destination}"/* "${source}"/*; do
      [[ -e "${entry}" ]] || continue
      kept=0
      for name in "$@"; do
        if [[ "${entry##*/}" == "${name}" ]]; then
          kept=1
        fi
      done

      if (( kept )); then
        continue
      fi

      if [[ "${entry}" == "${destination}/"* ]]; then
        rm -rf "${entry}"
      else
        cp -Rp "${entry}" "${destination}/"
      fi
    done
  fi
}

prepare_worker() {
  local worker_root="$1"
  local worker_project="${worker_root}/${PROJECT_NAME}"
  local entry name

  # The package: everything Unity imports, which leaves out dot folders and folders ending in ~.
  mkdir -p "${worker_root}"
  for entry in "${ROOT_DIR}"/*; do
    name="${entry##*/}"
    if [[ "${name}" == *~ ]]; then
      continue
    fi

    if [[ -d "${entry}" ]]; then
      mirror_dir "${entry}" "${worker_root}/${name}"
    else
      cp -p "${entry}" "${worker_root}/${name}"
    fi
  done

  for entry in "${worker_root}"/*; do
    name="${entry##*/}"
    if [[ "${name}" != *~ && ! -e "${ROOT_DIR}/${name}" ]]; then
      rm -rf "${entry}"
    fi
  done

  # The test project, without what Unity makes for itself. A new worker starts from what
  # the test project has compiled, and keeps its own from then on.
  mkdir -p "${worker_project}"
  if [[ -d "${worker_project}/Assets" ]]; then
    mirror_dir "${PROJECT_PATH}/Assets" "${worker_project}/Assets" "${COMPILED[@]}"
  else
    mirror_dir "${PROJECT_PATH}/Assets" "${worker_project}/Assets"
  fi

  mirror_dir "${PROJECT_PATH}/Packages" "${worker_project}/Packages"
  mirror_dir "${PROJECT_PATH}/ProjectSettings" "${worker_project}/ProjectSettings"

  if [[ ! -d "${worker_project}/Library" && -d "${PROJECT_PATH}/Library" ]]; then
    if [[ -e "${PROJECT_PATH}/Temp/UnityLockfile" ]]; then
      echo "[test-all] ${PROJECT_NAME} looks open in Unity, so worker ${worker_root##*/} imports everything itself this once."
    else
      echo "[test-all] Copying the Library for worker ${worker_root##*/}, this once."
      rm -rf "${worker_project}/Library.copying"
      mirror_dir "${PROJECT_PATH}/Library" "${worker_project}/Library.copying"
      mv "${worker_project}/Library.copying" "${worker_project}/Library"
    fi
  fi

  # Tests write what they render and measure beside the project.
  rm -rf "${worker_project}/TestResults~" "${worker_project}/layout-dump.tsv" "${worker_project}/layout-assets"
  mkdir -p "${worker_project}/TestResults~"
}

# Brings what a worker's tests wrote beside its project to where the test project has it.
collect_worker() {
  local worker_project="$1"
  cp -Rp "${worker_project}/TestResults~/." "${RESULTS_DIR}/"

  if [[ -f "${worker_project}/layout-dump.tsv" ]]; then
    cp -p "${worker_project}/layout-dump.tsv" "${PROJECT_PATH}/layout-dump.tsv"
  fi

  if [[ -d "${worker_project}/layout-assets" ]]; then
    mirror_dir "${worker_project}/layout-assets" "${PROJECT_PATH}/layout-assets"
  fi
}

# Prints an instance's line of the summary and returns whether it passed.
report() {
  local name="$1"
  local results_xml="$2"
  local editor_log="$3"
  local exit_code="$4"
  local seconds="$5"
  local total failed

  total="$(results_attribute "${results_xml}" total)"
  failed="$(results_attribute "${results_xml}" failed)"
  if [[ -z "${total}" ]]; then
    echo "[test-all] ${name}: no results after ${seconds}s (Unity exited with code ${exit_code}), see ${editor_log}"
    return 1
  fi

  echo "[test-all] ${name}: $(results_attribute "${results_xml}" passed) passed, ${failed} failed, $(results_attribute "${results_xml}" skipped) skipped, ${seconds}s"
  failed_tests "${results_xml}" | sed 's/^/[test-all]   failed: /'
  [[ "${exit_code}" == "0" && "${failed}" == "0" ]]
}

mkdir -p "${RESULTS_DIR}"
rm -f "${RESULTS_DIR}"/tests-*-results.xml "${RESULTS_DIR}"/tests-*.log

"${ROOT_DIR}/scripts/check-package-metadata.sh"
"${ROOT_DIR}/scripts/bootstrap-test-project.sh"

UNITY_EXECUTABLE="$(find_unity)"
echo "[test-all] Using Unity: ${UNITY_EXECUTABLE}"
echo "[test-all] Results: ${RESULTS_DIR}"

started=${SECONDS}

# A filter per instance: the tests whose full name is one of its classes and then a member.
# One instance alone runs everything and needs none.
filters=("")
if (( JOBS > 1 )); then
  filters=()
  while read -r share class; do
    index=$(( share - 1 ))
    filters[${index}]="${filters[${index}]:-}${filters[${index}]:+|}${class}"
  done < <(test_classes | deal_classes "${JOBS}" "${TIMINGS}")

  for (( i = 0; i < ${#filters[@]}; i++ )); do
    filters[${i}]="^([A-Za-z0-9_]+\\.)*(${filters[${i}]})([.+<(]|\$)"
  done
fi

count=${#filters[@]}
projects=()
results=()
logs=()
for (( i = 0; i < count; i++ )); do
  results+=("${RESULTS_DIR}/tests-$(( i + 1 ))-results.xml")
  logs+=("${RESULTS_DIR}/tests-$(( i + 1 )).log")
  if (( USE_WORKERS )); then
    projects+=("${WORKERS_DIR}/$(( i + 1 ))/${PROJECT_NAME}")
  else
    projects+=("${PROJECT_PATH}")
  fi
done

if (( USE_WORKERS )); then
  # The workers are brought up to date before any Unity starts, at once.
  pids=()
  for (( i = 0; i < count; i++ )); do
    prepare_worker "${WORKERS_DIR}/$(( i + 1 ))" &
    pids+=($!)
  done

  for pid in "${pids[@]}"; do
    wait "${pid}"
  done

  echo "[test-all] Workers ready after $(( SECONDS - started ))s, starting ${count} Unity instances."
fi

launched=${SECONDS}
pids=()
for (( i = 0; i < count; i++ )); do
  (
    exit_code=0
    if [[ -n "${filters[$i]}" ]]; then
      run_unity_tests "${projects[$i]}" "${ASSEMBLIES}" "${results[$i]}" "${logs[$i]}" \
        -testFilter "${filters[$i]}" || exit_code=$?
    else
      run_unity_tests "${projects[$i]}" "${ASSEMBLIES}" "${results[$i]}" "${logs[$i]}" || exit_code=$?
    fi

    echo "$(( SECONDS - launched ))" > "${results[$i]}.seconds"
    if (( USE_WORKERS )); then
      collect_worker "${projects[$i]}"
    fi

    exit "${exit_code}"
  ) &
  pids+=($!)
done

status=0
complete=1
for (( i = 0; i < count; i++ )); do
  exit_code=0
  wait "${pids[$i]}" || exit_code=$?
  seconds="$(cat "${results[$i]}.seconds" 2>/dev/null || echo "?")"
  rm -f "${results[$i]}.seconds"
  report "tests $(( i + 1 ))/${count}" "${results[$i]}" "${logs[$i]}" "${exit_code}" "${seconds}" || status=1
  if [[ ! -f "${results[$i]}" ]]; then
    complete=0
  fi
done

if [[ "${complete}" == "1" ]]; then
  write_timings "${TIMINGS}" "${results[@]}"
fi

if [[ "${status}" == "0" ]]; then
  echo "[test-all] All tests passed in $(( SECONDS - started ))s."
else
  echo "[test-all] Tests failed after $(( SECONDS - started ))s."
fi

exit "${status}"
