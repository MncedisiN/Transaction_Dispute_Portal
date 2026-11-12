#!/bin/sh
set -eu
cd "$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)"

open_browser=true
case "${1:-}" in
    --background) open_browser=false ;;
    '') ;;
    *) printf '%s\n' 'Run sh start-portal.sh to start the portal, or add --background to leave the browser closed.'; exit 1 ;;
esac

printf '%s\n' 'Starting Dispute Portal...'
command -v docker >/dev/null 2>&1 || {
    printf '%s\n' 'Install Docker, then run Start Portal again.'
    exit 1
}
system=$(uname)
if ! docker info >/dev/null 2>&1; then
    if [ "$system" = Darwin ]; then
        printf '%s\n' 'Starting Docker Desktop. This can take a few minutes...'
        open -g -a Docker
        attempts=0
        until docker info >/dev/null 2>&1; do
            attempts=$((attempts + 1))
            if [ "$attempts" -ge 80 ]; then
                printf '%s\n' 'Docker is still not ready. Open Docker Desktop, follow its instructions, then run Start Portal again.'
                exit 1
            fi
            sleep 3
        done
    else
        printf '%s\n' 'Start Docker, then run Start Portal again.'
        exit 1
    fi
fi
if [ "$(docker info --format '{{.OSType}}')" != linux ]; then
    printf '%s\n' 'Switch Docker to Linux containers, then run Start Portal again.'
    exit 1
fi

if [ -z "${PORTAL_LAN_IP:-}" ]; then
    if [ "$system" = Darwin ]; then
        interface=$(route -n get default | awk '/interface:/ {print $2}')
        PORTAL_LAN_IP=$(ipconfig getifaddr "$interface" 2>/dev/null || true)
    else
        PORTAL_LAN_IP=$(ip -4 route get 1.1.1.1 | awk '{for (i=1;i<=NF;i++) if ($i=="src") {print $(i+1); exit}}')
    fi
fi
if ! printf '%s\n' "$PORTAL_LAN_IP" | awk -F. '
    NF != 4 { exit 1 }
    { for (i = 1; i <= 4; i++) if ($i !~ /^[0-9]+$/ || $i > 255) exit 1 }
    { exit !($1 == 10 || ($1 == 172 && $2 >= 16 && $2 <= 31) || ($1 == 192 && $2 == 168)) }
'; then
    printf '%s\n' 'Connect this computer to your private Wi-Fi or Ethernet, then try again.'
    exit 1
fi
export PORTAL_LAN_IP
export PORTAL_HTTPS_PORT="${PORTAL_HTTPS_PORT:-5443}"
export PORTAL_CERTIFICATE_PORT="${PORTAL_CERTIFICATE_PORT:-8081}"
for port in "$PORTAL_HTTPS_PORT" "$PORTAL_CERTIFICATE_PORT"; do
    case "$port" in
        ''|*[!0-9]*) printf '%s\n' 'Use port numbers between 1024 and 65535.'; exit 1 ;;
    esac
    if [ "$port" -lt 1024 ] || [ "$port" -gt 65535 ]; then
        printf '%s\n' 'Use port numbers between 1024 and 65535.'
        exit 1
    fi
done
if [ "$PORTAL_HTTPS_PORT" -eq "$PORTAL_CERTIFICATE_PORT" ]; then
    printf '%s\n' 'Use different ports for the portal and phone setup.'
    exit 1
fi
export PORTAL_HTTPS_BIND="$PORTAL_LAN_IP:$PORTAL_HTTPS_PORT"
export PORTAL_SETUP_BIND="$PORTAL_LAN_IP:$PORTAL_CERTIFICATE_PORT"
export PORTAL_PUBLIC_URL="https://$PORTAL_LAN_IP:$PORTAL_HTTPS_PORT"

printf 'Starting the services for %s ...\n' "$PORTAL_PUBLIC_URL"
docker compose -f compose.yaml up -d --wait --wait-timeout 300
docker compose -f compose.yaml up --no-build --no-deps --force-recreate -d --wait --wait-timeout 180 api web
mkdir -p .local-run/https
docker compose -f compose.yaml cp bootstrap:/settings/https/authority.cer .local-run/https/authority.cer
docker compose -f compose.yaml cp bootstrap:/settings/https/fingerprint.txt .local-run/https/fingerprint.txt

printf '\nOpen on this computer: %s\n' "$PORTAL_PUBLIC_URL"
printf 'Open on your phone: %s\n' "$PORTAL_PUBLIC_URL"
printf 'Set up a new phone: http://%s:%s\n' "$PORTAL_LAN_IP" "$PORTAL_CERTIFICATE_PORT"
printf 'Certificate SHA-256: '
cat .local-run/https/fingerprint.txt
printf '\n%s\n' 'Keep your phone on the same Wi-Fi as this computer.'
printf '%s\n' 'For a new phone, open the setup address and follow the instructions.'
printf '%s\n' 'Trust .local-run/https/authority.cer on this computer using its certificate settings.'
printf 'If your firewall blocks phone access, allow TCP %s and %s from your local network.\n' "$PORTAL_HTTPS_PORT" "$PORTAL_CERTIFICATE_PORT"
printf '%s\n' 'Dispute Portal is ready. You can close this window; the services will keep running.'
if [ "$open_browser" = true ]; then
    if [ "$system" = Darwin ]; then
        open "$PORTAL_PUBLIC_URL" || true
    elif command -v xdg-open >/dev/null 2>&1; then
        xdg-open "$PORTAL_PUBLIC_URL" >/dev/null 2>&1 || true
    fi
fi
