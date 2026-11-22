#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="${ROOT_DIR:-$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)}"

is_importable_path() {
  local rel="$1"
  local part
  IFS='/' read -ra parts <<< "${rel}"
  for part in "${parts[@]}"; do
    if [[ -z "${part}" ]]; then
      continue
    fi

    if [[ "${part}" == .* ]]; then
      return 1
    fi

    if [[ "${part}" == *~ ]]; then
      return 1
    fi
  done

  return 0
}

missing_meta_file="$(mktemp)"
ignored_meta_file="$(mktemp)"
cleanup() {
  rm -f "${missing_meta_file}" "${ignored_meta_file}"
}
trap cleanup EXIT

while IFS= read -r -d '' path; do
  rel="${path#${ROOT_DIR}/}"

  if ! is_importable_path "${rel}"; then
    continue
  fi

  if [[ "${rel}" == *.meta ]]; then
    continue
  fi

  if [[ ! -e "${path}.meta" ]]; then
    printf '%s\n' "${rel}" >> "${missing_meta_file}"
  fi
done < <(
  find "${ROOT_DIR}" -mindepth 1 \
    \( -name '.*' -o -name '*~' \) -prune -o \
    \( -type f -o -type d \) -print0
)

if command -v git >/dev/null 2>&1 && git -C "${ROOT_DIR}" rev-parse --is-inside-work-tree >/dev/null 2>&1; then
  meta_paths=()
  while IFS= read -r -d '' meta_path; do
    rel="${meta_path#${ROOT_DIR}/}"

    if ! is_importable_path "${rel}"; then
      continue
    fi

    meta_paths+=("${rel}")
  done < <(
    find "${ROOT_DIR}" -mindepth 1 \
      \( -name '.*' -o -name '*~' \) -prune -o \
      \( -name '*.meta' -type f \) -print0
  )

  # One git process for every path: starting one per file takes most of a minute on Windows.
  # check-ignore prints the ignored paths and exits with 1 when there are none.
  if (( ${#meta_paths[@]} > 0 )); then
    printf '%s\0' "${meta_paths[@]}" \
      | git -C "${ROOT_DIR}" check-ignore -z --stdin \
      | tr '\0' '\n' >> "${ignored_meta_file}" || true
  fi
fi

if [[ -s "${missing_meta_file}" || -s "${ignored_meta_file}" ]]; then
  if [[ -s "${missing_meta_file}" ]]; then
    echo "[package-metadata] Missing .meta files:" >&2
    sed 's/^/  /' "${missing_meta_file}" >&2
  fi

  if [[ -s "${ignored_meta_file}" ]]; then
    echo "[package-metadata] .meta files ignored by git:" >&2
    sed 's/^/  /' "${ignored_meta_file}" >&2
  fi

  exit 1
fi

echo "[package-metadata] All importable package files have .meta files."
