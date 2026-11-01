#!/bin/sh
# Writes the SPA runtime config from environment variables at container start,
# so the same image can be promoted between environments.
set -eu

json_escape() { printf '%s' "$1" | sed 's/\\/\\\\/g; s/"/\\"/g'; }

cat > /usr/share/nginx/html/config.js <<EOF
window.__APP_CONFIG__ = {
  entraClientId: "$(json_escape "${ENTRA_CLIENT_ID:-}")",
  entraAuthority: "$(json_escape "${ENTRA_AUTHORITY:-https://login.microsoftonline.com/organizations}")",
  apiScope: "$(json_escape "${API_SCOPE:-}")",
  apiBaseUrl: "$(json_escape "${API_BASE_URL:-/api}")"
};
EOF
echo "app-config: wrote /usr/share/nginx/html/config.js"
