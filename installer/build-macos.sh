#!/usr/bin/env bash
set -euo pipefail

VERSION="${1:-1.0.0}"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"
PROJECT="${ROOT}/src/LazyLinuxSSH.Desktop/LazyLinuxSSH.Desktop.csproj"
ICON_PNG="${ROOT}/src/LazyLinuxSSH.Desktop/Assets/LazyLinuxSSH.png"
PLIST_TEMPLATE="${SCRIPT_DIR}/macos/Info.plist.template"
INSTALLER_OUT="${ROOT}/artifacts/installer"

if [[ "$(uname -s)" != "Darwin" ]]; then
  echo "Build the macOS packages on a Mac with .NET 8 SDK." >&2
  exit 1
fi

if ! command -v dotnet >/dev/null 2>&1; then
  echo "dotnet was not found. Install .NET 8 SDK: https://dotnet.microsoft.com/download/dotnet/8.0" >&2
  exit 1
fi

if [[ ! -f "${PROJECT}" ]]; then
  echo "Project not found: ${PROJECT}" >&2
  exit 1
fi

if [[ ! -f "${PLIST_TEMPLATE}" ]]; then
  echo "Info.plist template not found: ${PLIST_TEMPLATE}" >&2
  exit 1
fi

mkdir -p "${INSTALLER_OUT}"

make_icns() {
  local png="$1"
  local icns="$2"
  local iconset
  iconset="$(mktemp -d)/AppIcon.iconset"
  mkdir -p "${iconset}"

  if command -v sips >/dev/null 2>&1; then
    for size in 16 32 64 128 256 512; do
      sips -z "${size}" "${size}" "${png}" --out "${iconset}/icon_${size}x${size}.png" >/dev/null
      local double=$((size * 2))
      if [[ "${double}" -le 1024 ]]; then
        sips -z "${double}" "${double}" "${png}" --out "${iconset}/icon_${size}x${size}@2x.png" >/dev/null
      fi
    done
    iconutil -c icns "${iconset}" -o "${icns}"
  else
    echo "sips/iconutil unavailable; copying PNG as fallback icon resource." >&2
    cp "${png}" "${icns%.icns}.png"
    return 1
  fi
  rm -rf "$(dirname "${iconset}")"
  return 0
}

build_runtime() {
  local rid="$1"
  local publish_dir="${ROOT}/artifacts/publish/${rid}"
  local app_dir="${ROOT}/artifacts/app/${rid}/LazyLinuxSSH.app"
  local dmg_path="${INSTALLER_OUT}/LazyLinuxSSH-${VERSION}-${rid}.dmg"
  local stage_dir
  stage_dir="$(mktemp -d)/dmg"

  echo "[${rid}] Cleaning publish directory..."
  rm -rf "${publish_dir}" "${app_dir}"
  mkdir -p "${publish_dir}" "$(dirname "${app_dir}")"

  echo "[${rid}] Restoring NuGet packages..."
  dotnet restore "${PROJECT}" --runtime "${rid}" --nologo

  echo "[${rid}] Publishing LazyLinuxSSH ${VERSION}..."
  dotnet publish "${PROJECT}" \
    --configuration Release \
    --runtime "${rid}" \
    --self-contained true \
    --no-restore \
    --nologo \
    -p:Version="${VERSION}" \
    -p:PublishSingleFile=false \
    -p:PublishTrimmed=false \
    -p:DebugType=None \
    -p:DebugSymbols=false \
    --output "${publish_dir}"

  local published_bin
  if [[ -x "${publish_dir}/LazyLinuxSSH.Desktop" ]]; then
    published_bin="${publish_dir}/LazyLinuxSSH.Desktop"
  elif [[ -x "${publish_dir}/LazyLinuxSSH" ]]; then
    published_bin="${publish_dir}/LazyLinuxSSH"
  else
    echo "Published executable not found in ${publish_dir}" >&2
    exit 1
  fi

  echo "[${rid}] Assembling .app bundle..."
  mkdir -p "${app_dir}/Contents/MacOS" "${app_dir}/Contents/Resources"
  sed "s/__VERSION__/${VERSION}/g" "${PLIST_TEMPLATE}" > "${app_dir}/Contents/Info.plist"
  printf 'APPL????' > "${app_dir}/Contents/PkgInfo"

  # Copy published payload next to the launcher name expected by Info.plist.
  cp -R "${publish_dir}/." "${app_dir}/Contents/MacOS/"
  if [[ "$(basename "${published_bin}")" != "LazyLinuxSSH" ]]; then
    cp "${published_bin}" "${app_dir}/Contents/MacOS/LazyLinuxSSH"
  fi
  chmod +x "${app_dir}/Contents/MacOS/LazyLinuxSSH"

  if [[ -f "${ICON_PNG}" ]]; then
    if make_icns "${ICON_PNG}" "${app_dir}/Contents/Resources/AppIcon.icns"; then
      :
    elif [[ -f "${app_dir}/Contents/Resources/AppIcon.png" ]]; then
      :
    else
      cp "${ICON_PNG}" "${app_dir}/Contents/Resources/AppIcon.png"
    fi
  fi

  echo "[${rid}] Creating DMG..."
  rm -f "${dmg_path}"
  mkdir -p "${stage_dir}"
  cp -R "${app_dir}" "${stage_dir}/LazyLinuxSSH.app"
  ln -s /Applications "${stage_dir}/Applications"
  hdiutil create \
    -volname "LazyLinuxSSH ${VERSION}" \
    -srcfolder "${stage_dir}" \
    -ov -format UDZO \
    "${dmg_path}" >/dev/null
  rm -rf "$(dirname "${stage_dir}")"

  echo "[${rid}] Success: ${dmg_path}"
}

build_runtime "osx-arm64"
build_runtime "osx-x64"

echo ""
echo "Done."
echo "  ${INSTALLER_OUT}/LazyLinuxSSH-${VERSION}-osx-arm64.dmg   (Apple Silicon)"
echo "  ${INSTALLER_OUT}/LazyLinuxSSH-${VERSION}-osx-x64.dmg     (Intel)"
echo "Unsigned build: first launch via Right-click → Open."
