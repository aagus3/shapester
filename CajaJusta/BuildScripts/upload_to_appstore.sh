#!/bin/bash
# Unity Build Automation post-build script (runs on Unity's Mac after the iOS build).
# Uploads the generated .ipa to App Store Connect, where it shows up in TestFlight.
#
# Configure these ENVIRONMENT VARIABLES in the Build Automation iOS target (never commit them):
#   ASC_KEY_ID           App Store Connect API Key ID            (e.g. ABC123DEFG)
#   ASC_ISSUER_ID        Issuer ID shown on the API keys page    (a UUID)
#   ASC_KEY_P8_BASE64    The AuthKey_XXXX.p8 file, base64-encoded (single line)
# Build Automation provides UNITY_PLAYER_PATH = path to the built .ipa.

set -u
echo "== Shapester: uploading IPA to App Store Connect"

missing=0
for v in ASC_KEY_ID ASC_ISSUER_ID ASC_KEY_P8_BASE64; do
  if [ -z "${!v:-}" ]; then echo "ERROR: environment variable $v is not set"; missing=1; fi
done
if [ $missing -ne 0 ]; then
  echo "Skipping upload (credentials missing). The IPA is still available for download."
  exit 1
fi

IPA="${UNITY_PLAYER_PATH:-}"
if [ -z "$IPA" ] || [ ! -f "$IPA" ]; then
  # fallback: look for an .ipa next to the build output
  IPA=$(find "${UNITY_PLAYER_PATH:-.}" .. -maxdepth 3 -name "*.ipa" 2>/dev/null | head -1)
fi
if [ -z "$IPA" ] || [ ! -f "$IPA" ]; then
  echo "ERROR: could not find the .ipa (UNITY_PLAYER_PATH='${UNITY_PLAYER_PATH:-}')"
  exit 1
fi
echo "IPA: $IPA"

# altool looks for the key as ~/private_keys/AuthKey_<KEY_ID>.p8
mkdir -p ~/private_keys
KEY_FILE=~/private_keys/AuthKey_${ASC_KEY_ID}.p8
printf '%s' "$ASC_KEY_P8_BASE64" | base64 --decode > "$KEY_FILE" 2>/dev/null \
  || printf '%s' "$ASC_KEY_P8_BASE64" | base64 -D > "$KEY_FILE"
chmod 600 "$KEY_FILE"
if ! grep -q "BEGIN PRIVATE KEY" "$KEY_FILE"; then
  echo "ERROR: ASC_KEY_P8_BASE64 did not decode to a valid .p8 key"
  rm -f "$KEY_FILE"
  exit 1
fi

if xcrun altool --upload-app --type ios -f "$IPA" --apiKey "$ASC_KEY_ID" --apiIssuer "$ASC_ISSUER_ID"; then
  echo "== Upload finished OK. The build will appear in App Store Connect > TestFlight in a few minutes (after Apple's processing)."
  rm -f "$KEY_FILE"
  exit 0
else
  echo "== Upload FAILED (see the altool output above)."
  rm -f "$KEY_FILE"
  exit 1
fi
