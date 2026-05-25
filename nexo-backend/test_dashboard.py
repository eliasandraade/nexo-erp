import urllib.request
import json
import os
import time

base = "http://localhost:8080"
admin_pass = os.environ.get("Seed__AdminPassword", "")

print(f"[1] Login as admin@nexo.local ...")
t0 = time.time()
body = json.dumps({"login": "admin@nexo.local", "password": admin_pass}).encode("utf-8")
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
except Exception as e:
    elapsed = round(time.time() - t0, 3)
    print(f"    Login FAILED in {elapsed}s: {e}")
    exit(1)

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
        print(f"    totalSales={data.get('totalSales')}, totalRevenue={data.get('totalRevenue')}")
        print(f"    topProducts={len(data.get('topProducts', []))}, topSellers={len(data.get('topSellers', []))}")
        print(f"    salesByDay={len(data.get('salesByDay', []))}, stockAlerts={len(data.get('stockAlerts', []))}")
except urllib.error.HTTPError as e:
    elapsed = round(time.time() - t0, 3)
    print(f"    HTTP {e.code} in {elapsed}s: {e.read().decode()}")
except Exception as e:
    elapsed = round(time.time() - t0, 3)
    print(f"    ERROR in {elapsed}s: {e}")
