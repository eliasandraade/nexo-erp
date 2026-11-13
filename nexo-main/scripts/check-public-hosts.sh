#!/usr/bin/env bash
# Route matrix of the production server (Caddyfile) by Host header, against a running Caddy over a built dist
# (docs/engineering/public-hosts.md). The root of www goes to the Orken portal and the root of erp to the ERP's
# sign-in page (307, query string kept); every other path on both hosts, and the root of any other host (Railway's
# healthcheck), are served by the SPA as before.
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

# The root of erp is the ERP's entry; its other paths and any other host's root stay on the SPA.
check "erp / -> sign-in page" "$ERP" "/" 307 "$BASE/login"
check "erp / keeps the query string" "$ERP" "/?origem=portal&x=1" 307 "$BASE/login?origem=portal&x=1"
check "erp HEAD / -> sign-in page" "$ERP" "/" 307 "$BASE/login" -I
check "erp POST / is not redirected (405 as before)" "$ERP" "/" 405 "" -X POST
check "erp /login" "$ERP" "/login" 200
check "erp /login with its notice" "$ERP" "/login?motivo=sessao-encerrada" 200
check "erp /dashboard" "$ERP" "/dashboard" 200
check "erp /workspaces" "$ERP" "/workspaces" 200
check "erp menu /{slug}" "$ERP" "/cantina-da-ana" 200
check "erp booking /agendar/{slug}" "$ERP" "/agendar/barbearia" 200
check "erp order tracking /rastrear/{token}" "$ERP" "/rastrear/abc123" 200
check "erp Build portal /{company}/{project}" "$ERP" "/solutie/shoppingeusebio" 200
check "Railway healthcheck host /" "healthcheck.railway.app" "/" 200

# No redirect loop: each root ends in one hop, on a page this server serves or on the portal.
end=$(curl -s -o /dev/null -L --max-redirs 5 -w '%{http_code} %{num_redirects} %{url_effective}' -H "Host: $ERP" "$BASE/?x=1")
if [[ "$end" == "200 1 $BASE/login?x=1" ]]; then echo "ok   erp / ends on /login?x=1 in one hop"; else echo "FAIL erp / chain: $end"; fail=1; fi

# Security headers also on the redirect, assets still cached for good.
for host in "$WWW" "$ERP"; do
  headers=$(curl -s -o /dev/null -D - -H "Host: $host" "$BASE/" | tr -d '\r')
  for h in "Strict-Transport-Security: max-age=31536000; includeSubDomains" "X-Content-Type-Options: nosniff" "X-Frame-Options: DENY" \
    "Content-Security-Policy: frame-ancestors 'none'" "Referrer-Policy: strict-origin-when-cross-origin" \
    "Permissions-Policy: camera=(), microphone=(), usb=(), geolocation=(self)"; do
    if [[ "${headers,,}" == *"${h,,}"* ]]; then echo "ok   $host / redirect sends $h"; else echo "FAIL $host / redirect without $h"; fail=1; fi
  done
done

# Content-Security-Policy (docs/security/csp.md). The enforced header stays exactly what it was: the full policy is report-only,
# it cannot block anything, and it reports to the production API when this server gets no environment of its own.
for host in "$WWW" "$ERP"; do
  for path in "/login" "/agendar/barbearia" "/solutie/shoppingeusebio"; do
    headers=$(curl -s -o /dev/null -D - -H "Host: $host" "$BASE$path" | tr -d '\r')
    enforced=$(grep -i '^content-security-policy:' <<<"$headers")
    if [[ "${enforced,,}" == "content-security-policy: frame-ancestors 'none'" ]]; then
      echo "ok   $host $path enforces only frame-ancestors"
    else
      echo "FAIL $host $path enforced policy: ${enforced:-(none)}"
      fail=1
    fi
    report_only=$(grep -i '^content-security-policy-report-only:' <<<"$headers")
    for part in "default-src 'none'" "script-src 'self' 'wasm-unsafe-eval';" "object-src blob:;" "frame-src blob:;" "worker-src 'self';" "frame-ancestors 'none'" \
      "connect-src 'self' blob: https://api.erp.orken.com.br wss://api.erp.orken.com.br " "report-uri https://api.erp.orken.com.br/api/csp-reports"; do
      if [[ "$report_only" == *"$part"* ]]; then echo "ok   $host $path report-only has $part"; else echo "FAIL $host $path report-only without $part"; fail=1; fi
    done
    for banned in "unsafe-eval'" "script-src 'unsafe-inline'" "script-src 'self' 'unsafe-inline'" " * " " https: "; do
      if [[ "${report_only//wasm-unsafe-eval/}" == *"$banned"* ]]; then echo "FAIL $host $path report-only has $banned"; fail=1; fi
    done
  done
done

asset=$(curl -s -H "Host: $ERP" "$BASE/login" | grep -oE '/assets/[^"]+\.js' | head -n 1)
cache=$(curl -s -o /dev/null -D - -H "Host: $WWW" "$BASE$asset" | tr -d '\r')
if [[ -n "$asset" && "${cache,,}" == *"cache-control: public, max-age=31536000, immutable"* ]]; then
  echo "ok   www $asset cached as immutable"
else
  echo "FAIL www asset ${asset:-(none found)} not served as immutable"
  fail=1
fi

[[ "$fail" == 0 ]] && echo "all checks passed"
exit "$fail"
