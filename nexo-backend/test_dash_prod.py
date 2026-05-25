import urllib.request, json, time, sys, subprocess

base = "https://backend-production-b2bc.up.railway.app"

# Read password from Railway variables to avoid shell encoding issues
result = subprocess.run(
    [r"C:\Users\Elias\AppData\Roaming\npm\railway.cmd", "variables", "--json"],
    capture_output=True, text=True, encoding="utf-8",
    cwd=r"C:\Users\Elias\Documents\NexoERP\nexo-backend"
)
if result.returncode != 0:
    print("Could not read Railway variables:", result.stderr)
    sys.exit(1)

vars_data = json.loads(result.stdout)
raw_password = vars_data.get("Seed__AdminPassword", "")
# Railway CLI double-encodes non-ASCII chars (Â£ instead of £).
# len=24 raw → len=22 after fix. encode('latin-1') maps U+00C2→0xC2, U+00A3→0xA3;
# decode('utf-8') interprets 0xC2 0xA3 as £ (U+00A3) — the actual server-stored value.
try:
    password = raw_password.encode('latin-1').decode('utf-8')
except (UnicodeEncodeError, UnicodeDecodeError):
    password = raw_password
print(f"[DEBUG] raw len={len(raw_password)} corrected len={len(password)}")

# Login
print(f"\n[1] Login as admin@nexo.local ...")
t0 = time.time()
# Admin user login name is "admin" (not the email address)
body = json.dumps({"login": "admin", "password": password}).encode("utf-8")
req = urllib.request.Request(
    f"{base}/api/auth/login",
    data=body,
    headers={"Content-Type": "application/json"},
)
try:
    with urllib.request.urlopen(req, timeout=15) as resp:
        data = json.loads(resp.read())
        token = data.get("accessToken", "")
        elapsed = round(time.time() - t0, 3)
        print(f"    Login OK in {elapsed}s, token len={len(token)}")
except urllib.error.HTTPError as e:
    elapsed = round(time.time() - t0, 3)
    body_text = e.read().decode()[:300]
    print(f"    Login HTTP {e.code} in {elapsed}s: {body_text}")
    sys.exit(1)
except Exception as e:
    elapsed = round(time.time() - t0, 3)
    print(f"    Login FAILED in {elapsed}s: {e}")
    sys.exit(1)

# Dashboard
print(f"\n[2] GET /api/dashboard/summary ...")
t0 = time.time()
req2 = urllib.request.Request(
    f"{base}/api/dashboard/summary",
    headers={"Authorization": f"Bearer {token}"},
)
try:
    with urllib.request.urlopen(req2, timeout=30) as resp:
        data = json.loads(resp.read())
        elapsed = round(time.time() - t0, 3)
        print(f"    200 OK in {elapsed}s")
        print(f"    totalSales={data.get('totalSales')} totalRevenue={data.get('totalRevenue')}")
        print(f"    topProducts={len(data.get('topProducts', []))} topSellers={len(data.get('topSellers', []))}")
        print(f"    salesByDay={len(data.get('salesByDay', []))} stockAlerts={len(data.get('stockAlerts', []))}")
        print(f"    zeroStockCount={data.get('zeroStockCount')} lowStockCount={data.get('lowStockCount')}")
        print(f"    hasOpenCashSession={data.get('hasOpenCashSession')}")
except urllib.error.HTTPError as e:
    elapsed = round(time.time() - t0, 3)
    print(f"    FAIL HTTP {e.code} in {elapsed}s: {e.read().decode()[:300]}")
except Exception as e:
    elapsed = round(time.time() - t0, 3)
    print(f"    ERROR in {elapsed}s: {e}")
