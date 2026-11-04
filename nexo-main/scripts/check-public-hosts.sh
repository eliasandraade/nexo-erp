#!/usr/bin/env bash
# Route matrix of the production server (Caddyfile) by Host header, against a running Caddy over a built dist
# (docs/engineering/public-hosts.md). The root of www goes to the Orken portal (307, query string kept); every client
# path on www, the whole erp host and any other host (Railway's healthcheck) are served by the SPA as before.
#   scripts/check-public-hosts.sh http://localhost:8099
set -u
BASE="${1:-http://localhost:8080}"
WWW="www.orken.com.br"
ERP="erp.orken.com.br"
fail=0

# check <description> <host> <path> <expected status> [expected Location] [extra curl args...]
check() {
  local what="$1" host="$2" path="$3" want="$4" loc="${5:-}"
  shift 5 2>/dev/null || shift $#
  local out code got
  out=$(curl -s -o /dev/null -w '%{http_code} %{redirect_url}' -H "Host: $host" "$@" "$BASE$path")
  code="${out%% *}"
  got="${out#* }"
  if [[ "$code" == "$want" && ( -z "$loc" || "$got" == "$loc" ) ]]; then
    echo "ok   $what"
  else
    echo "FAIL $what: $code ${got:-(no Location)}"
    fail=1
  fi
}

# The institutional root moves; nothing else on www does.
check "www / -> Orken portal" "$WWW" "/" 307 "https://app.orken.com.br/"
check "www / keeps the query string" "$WWW" "/?utm_source=jornal&b=1" 307 "https://app.orken.com.br/?utm_source=jornal&b=1"
check "www HEAD / -> Orken portal" "$WWW" "/" 307 "https://app.orken.com.br/" -I
check "www POST / is not redirected (405 as before)" "$WWW" "/" 405 "" -X POST
check "www Build portal /{company}/{project}" "$WWW" "/solutie/shoppingeusebio" 200
check "www menu /{slug}" "$WWW" "/cantina-da-ana" 200
check "www legacy menu /menu/{slug}" "$WWW" "/menu/cantina-da-ana" 200
check "www booking /agendar/{slug}" "$WWW" "/agendar/barbearia" 200
check "www booking management /agendar/{slug}/reserva/{token}" "$WWW" "/agendar/barbearia/reserva/abc123" 200
check "www order tracking /rastrear/{token}" "$WWW" "/rastrear/abc123" 200
check "www sign-in /login" "$WWW" "/login" 200
check "www password reset link" "$WWW" "/redefinir-senha?token=abc" 200
check "www e-mail confirmation link" "$WWW" "/verify-email?token=abc" 200
check "www /index.html is not the root" "$WWW" "/index.html" 200
check "www robots.txt" "$WWW" "/robots.txt" 200

# erp and any other host keep the SPA at the root.
check "erp /" "$ERP" "/" 200
check "erp / with a query string" "$ERP" "/?x=1" 200
check "erp /login" "$ERP" "/login" 200
check "Railway healthcheck host /" "healthcheck.railway.app" "/" 200

# Security headers also on the redirect, assets still cached for good.
headers=$(curl -s -o /dev/null -D - -H "Host: $WWW" "$BASE/" | tr -d '\r')
for h in "Strict-Transport-Security: max-age=31536000; includeSubDomains" "X-Content-Type-Options: nosniff" "X-Frame-Options: DENY" \
  "Content-Security-Policy: frame-ancestors 'none'" "Referrer-Policy: strict-origin-when-cross-origin" \
  "Permissions-Policy: camera=(), microphone=(), usb=(), geolocation=(self)"; do
  if [[ "${headers,,}" == *"${h,,}"* ]]; then echo "ok   www / redirect sends $h"; else echo "FAIL www / redirect without $h"; fail=1; fi
done
asset=$(curl -s -H "Host: $ERP" "$BASE/" | grep -oE '/assets/[^"]+\.js' | head -n 1)
cache=$(curl -s -o /dev/null -D - -H "Host: $WWW" "$BASE$asset" | tr -d '\r')
if [[ -n "$asset" && "${cache,,}" == *"cache-control: public, max-age=31536000, immutable"* ]]; then
  echo "ok   www $asset cached as immutable"
else
  echo "FAIL www asset ${asset:-(none found)} not served as immutable"
  fail=1
fi

[[ "$fail" == 0 ]] && echo "all checks passed"
exit "$fail"
