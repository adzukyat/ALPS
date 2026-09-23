#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="${ROOT_DIR:-$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)}"
PROJECT_PATH="${PROJECT_PATH:-${ROOT_DIR}/TestProject~}"
PACKAGES_DIR="${PROJECT_PATH}/Packages"
CACHE_DIR="${ROOT_DIR}/.cache/test-project-packages"

mkdir -p "${PACKAGES_DIR}" "${CACHE_DIR}"

download_package() {
  local name="$1"
  local url="$2"
  local sha256="$3"
  # Optional path of the package folder inside the archive, for repository archives.
  local package_path="${4:-}"
  local zip_path="${CACHE_DIR}/${name}-${sha256:0:12}.zip"
  local tmp_dir="${CACHE_DIR}/${name}.tmp"
  local target_dir="${PACKAGES_DIR}/${name}"
  local source_marker="${target_dir}/.alps-bootstrap-source"

  if [[ -f "${target_dir}/package.json" && -f "${source_marker}" && "$(cat "${source_marker}")" == "${sha256}" ]]; then
    return 0
  fi

  # Older harnesses installed packages without a marker. Keep those when the source is unchanged.
  if [[ -f "${target_dir}/package.json" && ! -f "${source_marker}" && -z "${package_path}" ]]; then
    echo "${sha256}" > "${source_marker}"
    return 0
  fi

  echo "[bootstrap] Installing ${name}"
  if [[ ! -f "${zip_path}" ]]; then
    curl -fL "${url}" -o "${zip_path}"
  fi

  if command -v sha256sum >/dev/null 2>&1; then
    echo "${sha256}  ${zip_path}" | sha256sum -c -
  elif command -v shasum >/dev/null 2>&1; then
    echo "${sha256}  ${zip_path}" | shasum -a 256 -c -
  else
    echo "[bootstrap] sha256 tool not found; skipping checksum for ${name}" >&2
  fi

  rm -rf "${tmp_dir}" "${target_dir}"
  mkdir -p "${tmp_dir}"
  if command -v unzip >/dev/null 2>&1; then
    unzip -q "${zip_path}" -d "${tmp_dir}"
  elif command -v python3 >/dev/null 2>&1; then
    python3 -m zipfile -e "${zip_path}" "${tmp_dir}"
  else
    echo "Neither unzip nor python3 is available to extract ${zip_path}" >&2
    exit 1
  fi

  local extracted
  if [[ -n "${package_path}" ]]; then
    extracted="$(find "${tmp_dir}" -path "*/${package_path}/package.json" -type f -print -quit)"
  else
    extracted="$(find "${tmp_dir}" -maxdepth 3 -type f -name package.json -print -quit)"
  fi

  if [[ -z "${extracted}" ]]; then
    echo "No package.json found in ${zip_path}" >&2
    exit 1
  fi

  mv "$(dirname "${extracted}")" "${target_dir}"
  echo "${sha256}" > "${source_marker}"
  rm -rf "${tmp_dir}"
}

patch_udonsharp_batchmode_guard() {
  local file="${PACKAGES_DIR}/com.vrchat.worlds/Integrations/UdonSharp/Editor/UdonSharpEditorManager.cs"
  local marker="test harness: skip GUI-only inspector Harmony patch in batchmode."

  if [[ ! -f "${file}" ]] || grep -q "${marker}" "${file}"; then
    return 0
  fi

  if ! command -v perl >/dev/null 2>&1; then
    echo "[bootstrap] perl is required to patch ${file}" >&2
    exit 1
  fi

  perl -0pi -e 's/(private static void PatchInspectorTitleIfNeeded\(\)\s*\{\s*)if \(_inspectorTitlePatched\) return;/${1}\/\/ ALPS test harness: skip GUI-only inspector Harmony patch in batchmode.\n            if (Application.isBatchMode) return;\n            if (_inspectorTitlePatched) return;/s' "${file}"

  if ! grep -q "${marker}" "${file}"; then
    echo "[bootstrap] Failed to patch UdonSharp batchmode guard in ${file}" >&2
    exit 1
  fi
}

patch_udonsharp_runtime_watcher_batchmode_guard() {
  local file="${PACKAGES_DIR}/com.vrchat.worlds/Integrations/UdonSharp/Editor/UdonSharpRuntimeLogWatcher.cs"
  local marker="test harness: skip VRChat runtime log watcher in batchmode."

  if [[ ! -f "${file}" ]] || grep -q "${marker}" "${file}"; then
    return 0
  fi

  if ! command -v perl >/dev/null 2>&1; then
    echo "[bootstrap] perl is required to patch ${file}" >&2
    exit 1
  fi

  perl -0pi -e 's/(public static void InitLogWatcher\(\)\s*\{\s*)EditorApplication\.update \+= OnEditorUpdate;/${1}\/\/ ALPS test harness: skip VRChat runtime log watcher in batchmode.\n            if (Application.isBatchMode) return;\n\n            EditorApplication.update += OnEditorUpdate;/s' "${file}"

  if ! grep -q "${marker}" "${file}"; then
    echo "[bootstrap] Failed to patch UdonSharp runtime watcher batchmode guard in ${file}" >&2
    exit 1
  fi
}

download_package \
  "com.vrchat.base" \
  "https://github.com/vrchat/packages/releases/download/3.10.2/com.vrchat.base-3.10.2.zip" \
  "e4268b7677baedc50f15e22c5d7d73c8d173d39fa49d78821b3c23e1e9c6555e"

download_package \
  "com.vrchat.worlds" \
  "https://github.com/vrchat/packages/releases/download/3.10.2/com.vrchat.worlds-3.10.2.zip" \
  "438b26dd4873f26a2825993d34e142b8540c4ec6e94cdeacab39b16ab53e081a"

download_package \
  "com.llealloo.audiolink" \
  "https://github.com/llealloo/audiolink/releases/download/3.1.2/com.llealloo.audiolink-3.1.2.zip" \
  "f52f2fe04b7c6b86e79468ffa70e1e8fa1726a5c618f782f8e6d62e02da7c236"

# Stock VRSL 2.8.4, released under the v2.8.1 tag.
download_package \
  "com.acchosen.vr-stage-lighting" \
  "https://github.com/AcChosen/VR-Stage-Lighting/releases/download/v2.8.1/com.acchosen.vr-stage-lighting.zip" \
  "7b2e7c3ae697d398a494aab022425f539ac2238ff87f01f27c7281b4af50529a"

patch_udonsharp_batchmode_guard
patch_udonsharp_runtime_watcher_batchmode_guard
