set -eu
cd "$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)"
if [ -z "${PORTAL_LAN_IP:-}" ]; then
    if [ "$(uname)" = Darwin ]; then
        interface=$(route -n get default | awk '/interface:/ {print $2}')
        PORTAL_LAN_IP=$(ipconfig getifaddr "$interface")
    else
        PORTAL_LAN_IP=$(ip -4 route get 1.1.1.1 | awk '{for (i=1;i<=NF;i++) if ($i=="src") {print $(i+1); exit}}')
    fi
fi
test -n "$PORTAL_LAN_IP" || { printf '%s\n' 'Connect to Wi-Fi or Ethernet first.'; exit 1; }
export PORTAL_LAN_IP
export PORTAL_HTTPS_PORT="${PORTAL_HTTPS_PORT:-8443}"
export PORTAL_HTTPS_BIND="$PORTAL_LAN_IP:$PORTAL_HTTPS_PORT"
export PORTAL_SETUP_BIND="$PORTAL_LAN_IP:8081"
export PORTAL_PUBLIC_URL="https://$PORTAL_LAN_IP:$PORTAL_HTTPS_PORT"
docker compose up --build -d --wait --wait-timeout 300
docker compose up --no-build --no-deps --force-recreate -d --wait --wait-timeout 180 api web
mkdir -p .local-run/https
docker compose cp bootstrap:/settings/https/authority.cer .local-run/https/authority.cer
printf 'Phone certificate setup: http://%s:8081\nPhone portal: https://%s:%s\n' "$PORTAL_LAN_IP" "$PORTAL_LAN_IP" "$PORTAL_HTTPS_PORT"
docker compose run --rm --no-deps --entrypoint cat web /run/https/fingerprint.txt
printf '\n%s\n' 'Trust the local CA once on each device. Allow TCP 8081 and the HTTPS port through your local firewall for the private LAN only.'
