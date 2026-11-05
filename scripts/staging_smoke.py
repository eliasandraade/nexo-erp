#!/usr/bin/env python3
"""Smoke test of the ERP staging environment (docs/engineering/staging-e-promocao.md).

Exercises sign-in, sessions, the ERP core, Build, tenant isolation and the frontend bundle against a deployed staging.
It writes synthetic records there, so it refuses any production host. Prints checks and status codes, never tokens or
passwords.

    STAGING_API=https://<backend> STAGING_WEB=https://<main> STAGING_ADMIN_PASSWORD=<Seed__AdminPassword> \
        python3 scripts/staging_smoke.py
"""
import json
import os
import re
import sys
import time
import urllib.error
import urllib.parse
import urllib.request

PRODUCTION_HOSTS = {"api.erp.orken.com.br", "erp.orken.com.br", "www.orken.com.br", "api.orken.com.br", "app.orken.com.br"}
# Fixed password of a synthetic tenant from DataSeeder; it exists only outside production.
SECOND_TENANT = ("clara.boutique", "boutique@123")
TWO_STORES = ("ana.norte", "ana@123")
ME = "/api/auth/me"
REFRESH = "/api/auth/refresh"
BUILD_PROJECTS = "/api/v1/build/projects"


def base_url(name):
    value = os.environ.get(name, "").rstrip("/")
    host = urllib.parse.urlparse(value).hostname
    if not value.startswith("https://") or not host:
        sys.exit(f"{name} must be an https URL")
    if host in PRODUCTION_HOSTS:
        sys.exit(f"Refused: {host} is a production host")
    return value


API = base_url("STAGING_API")
WEB = base_url("STAGING_WEB")
ADMIN_PASSWORD = os.environ.get("STAGING_ADMIN_PASSWORD", "")
if not ADMIN_PASSWORD:
    sys.exit("STAGING_ADMIN_PASSWORD is required")

failures = []


def check(name, passed, detail=""):
    print(("OK   " if passed else "FAIL ") + name + (f" ({detail})" if detail else ""))
    if not passed:
        failures.append(name)
    return passed


def call(method, url, token=None, body=None):
    """Returns (status, parsed JSON or text)."""
    headers = {"User-Agent": "orken-staging-smoke", "Accept": "application/json"}
    data = None
    if body is not None:
        data = json.dumps(body).encode()
        headers["Content-Type"] = "application/json"
    if token:
        headers["Authorization"] = "Bearer " + token
    request = urllib.request.Request(url, data=data, headers=headers, method=method)
    try:
        with urllib.request.urlopen(request, timeout=60) as response:
            status, raw = response.status, response.read()
    except urllib.error.HTTPError as error:
        status, raw = error.code, error.read()
    text = raw.decode("utf-8", "replace")
    try:
        return status, json.loads(text)
    except ValueError:
        return status, text


def api(method, path, token=None, body=None):
    return call(method, API + path, token, body)


def login(user, password):
    status, payload = api("POST", "/api/auth/login", body={"login": user, "password": password})
    return payload if status == 200 and isinstance(payload, dict) and payload.get("accessToken") else None


# ── Service ──────────────────────────────────────────────────────────────────
status, _ = api("GET", "/health")
check("API /health", status == 200, str(status))
status, _ = api("GET", "/swagger/v1/swagger.json")
check("Swagger answers, so this is not a production environment", status == 200, str(status))
status, _ = api("POST", "/api/billing/webhook", body={})
check("Stripe webhook off while there is no test key", status == 404, str(status))
status, _ = api("GET", "/api/orken/v1/operations")
check("Cockpit contract refuses an anonymous call", status in (401, 404), str(status))

# ── Sign-in and sessions ─────────────────────────────────────────────────────
status, _ = api("POST", "/api/auth/login", body={"login": "admin", "password": "wrong-password-for-the-smoke-test"})
check("Wrong password refused", status == 401, str(status))

admin = login("admin", ADMIN_PASSWORD)
if not check("Admin signs in", admin is not None):
    sys.exit("Cannot go on without the admin session")
token = admin["accessToken"]
status, _ = api("GET", ME, token)
check("Session answers /me", status == 200, str(status))
status, _ = api("GET", ME)
check("No token, no /me", status == 401, str(status))

first_refresh = current_refresh = admin["refreshToken"]
status, rotated = api("POST", REFRESH, body={"refreshToken": first_refresh})
if check("Refresh rotates", status == 200 and isinstance(rotated, dict) and bool(rotated.get("refreshToken")), str(status)):
    token, current_refresh = rotated["accessToken"], rotated["refreshToken"]
    check("Refresh hands a new token", current_refresh != first_refresh)
    status, replay = api("POST", REFRESH, body={"refreshToken": first_refresh})
    code = replay.get("code") if isinstance(replay, dict) else None
    check("Spent token inside the grace gets 409 and no tokens",
          status == 409 and code == "refresh_already_rotated" and "accessToken" not in replay, f"{status} {code}")
    status, _ = api("GET", ME, token)
    check("Session survives the spent token", status == 200, str(status))

two_stores = login(*TWO_STORES)
if check("User with two stores signs in", two_stores is not None):
    stores = two_stores["session"]["storeIds"]
    other = [s for s in stores if s != two_stores["session"].get("storeId")]
    if check("That user really has another store", bool(other), str(len(stores))):
        status, _ = api("POST", "/api/auth/switch-store", two_stores["accessToken"], {"storeId": other[0]})
        check("Switching store without the refresh token is refused", status in (400, 401, 403), str(status))
        status, switched = api("POST", "/api/auth/switch-store", two_stores["accessToken"],
                               {"storeId": other[0], "refreshToken": two_stores["refreshToken"]})
        check("Switching store with the refresh token works", status == 200, str(status))
        if status == 200:
            status, again = api("POST", REFRESH, body={"refreshToken": two_stores["refreshToken"]})
            check("The switch spent the old refresh token", status == 409 and "accessToken" not in again, str(status))

# ── ERP core ─────────────────────────────────────────────────────────────────
stamp = str(int(time.time()))
status, product = api("POST", "/api/products", token,
                      {"code": "SMOKE-" + stamp, "name": "Produto sintético " + stamp, "unit": "Un", "salePrice": 10.5, "costPrice": 4})
product_id = product.get("id") if isinstance(product, dict) else None
check("ERP creates a product", status in (200, 201) and bool(product_id), str(status))
status, _ = api("GET", "/api/products", token)
check("ERP lists products", status == 200, str(status))
status, _ = api("GET", "/api/customers", token)
check("ERP lists customers", status == 200, str(status))

# ── Build ────────────────────────────────────────────────────────────────────
status, project = api("POST", BUILD_PROJECTS, token,
                      {"name": "Obra sintética " + stamp, "clientName": "Cliente sintético", "type": "House"})
project_id = project.get("id") if isinstance(project, dict) else None
check("Build creates a project", status in (200, 201) and bool(project_id), str(status))
status, _ = api("GET", BUILD_PROJECTS, token)
check("Build lists projects", status == 200, str(status))
if project_id:
    status, _ = api("GET", f"{BUILD_PROJECTS}/{project_id}/details", token)
    check("Build opens the project", status == 200, str(status))

# ── Tenant isolation ─────────────────────────────────────────────────────────
other_tenant = login(*SECOND_TENANT)
if check("Second tenant signs in", other_tenant is not None):
    other_token = other_tenant["accessToken"]
    status, _ = api("GET", BUILD_PROJECTS, other_token)
    check("Tenant without Build is refused by the module gate", status == 403, str(status))
    if product_id:
        status, _ = api("GET", f"/api/products/{product_id}", other_token)
        check("Another tenant's product is invisible", status in (403, 404), str(status))

# ── Sign-out ─────────────────────────────────────────────────────────────────
status, _ = api("POST", "/api/auth/logout", token, {"refreshToken": current_refresh})
check("Logout answers", status in (200, 204), str(status))
status, _ = api("POST", REFRESH, body={"refreshToken": current_refresh})
check("Refresh after logout is refused", status == 401, str(status))

# ── Frontend ─────────────────────────────────────────────────────────────────
status, html = call("GET", WEB + "/")
check("Frontend answers", status == 200, str(status))
assets = re.findall(r'(?:src|href)="(/assets/[^"]+\.js)"', html if isinstance(html, str) else "")
check("Frontend lists its bundles", bool(assets), str(len(assets)))
api_host = urllib.parse.urlparse(API).hostname
bundle_text = ""
for asset in assets:
    _, content = call("GET", WEB + asset)
    bundle_text += content if isinstance(content, str) else json.dumps(content)
check("Bundle calls the staging API", api_host in bundle_text)
check("Bundle never calls the production API", "api.erp.orken.com.br" not in bundle_text)

print()
if failures:
    print(f"{len(failures)} check(s) failed: " + "; ".join(failures))
    sys.exit(1)
print("All checks passed.")
