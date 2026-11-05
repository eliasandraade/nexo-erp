"""Testes do simulador de webhook: python -m unittest nexo-backend/tools/test_stripe_webhook_sim.py"""

import hashlib
import hmac
import http.server
import json
import os
import pathlib
import sys
import threading
import unittest
from unittest import mock

sys.path.insert(0, str(pathlib.Path(__file__).parent))

import stripe_webhook_sim as sim  # noqa: E402

SECRET = "whsec_segredo_dos_testes"
PLAIN_HTTP = "http"  # as URLs sem TLS abaixo são o que o script precisa recusar


class UrlGuard(unittest.TestCase):
    def test_production_hosts_are_refused_in_every_spelling(self):
        for url in (
            "https://api.erp.orken.com.br/api/billing/webhook",
            "https://erp.orken.com.br/api/billing/webhook",
            "https://www.orken.com.br/api/billing/webhook",
            "https://API.ERP.ORKEN.COM.BR/api/billing/webhook",
            "https://api.erp.orken.com.br./api/billing/webhook",
            "https://api.erp.orken.com.br:443/api/billing/webhook",
            f"{PLAIN_HTTP}://api.erp.orken.com.br/api/billing/webhook",
            "  https://api.erp.orken.com.br/api/billing/webhook  ",
            "https://x.api.erp.orken.com.br/api/billing/webhook",
            "https://localhost@api.erp.orken.com.br/api/billing/webhook",
            "https://localhost\\@api.erp.orken.com.br/api/billing/webhook",
            "https://api%2Eerp.orken.com.br/api/billing/webhook",
        ):
            with self.subTest(url=url), self.assertRaises(sim.Refused):
                sim.check_url(url)

    def test_other_schemes_and_broken_urls_are_refused(self):
        for url in ("ftp://localhost/x", "localhost:5199/x", "https:///x", f"{PLAIN_HTTP}://exemplo.test/x", f"{PLAIN_HTTP}://[::1/x"):
            with self.subTest(url=url), self.assertRaises(sim.Refused):
                sim.check_url(url)

    def test_staging_and_local_hosts_are_accepted(self):
        for url in (
            "http://localhost:5199/api/billing/webhook",
            "http://127.0.0.1:5199/api/billing/webhook",
            "https://api.erp.staging.orken.com.br/api/billing/webhook",
            "https://nexo-erp-staging.up.railway.app/api/billing/webhook",
        ):
            with self.subTest(url=url):
                self.assertEqual(sim.check_url(url), url)


class SecretGuard(unittest.TestCase):
    def test_only_a_webhook_secret_is_accepted(self):
        self.assertEqual(sim.check_secret(SECRET), SECRET)
        for value in (None, "", "sk_test_x", "rk_test_x", "pk_test_x", "sk_" + "live_x"):
            with self.subTest(value=value), self.assertRaises(sim.Refused) as refused:
                sim.check_secret(value)
            if value:
                self.assertNotIn(value, str(refused.exception))


class Signature(unittest.TestCase):
    def test_matches_the_scheme_stripe_documents(self):
        expected = hmac.new(SECRET.encode(), b"1700000000.{}", hashlib.sha256).hexdigest()
        self.assertEqual(sim.sign("{}", SECRET, 1700000000), f"t=1700000000,v1={expected}")


class Events(unittest.TestCase):
    def test_every_type_builds_an_event_with_the_fields_the_backend_reads(self):
        for event_type in sim.EVENT_TYPES:
            with self.subTest(event_type=event_type):
                event = json.loads(sim.build_event(event_type, subscription="sub_1", tenant_id="t", now=1700000000))
                self.assertEqual(event["type"], event_type)
                self.assertFalse(event["livemode"])
                self.assertTrue(event["id"].startswith("evt_"))

        checkout = json.loads(sim.build_event("checkout.session.completed", subscription="sub_1", tenant_id="t", module="build"))
        self.assertEqual(checkout["data"]["object"]["metadata"], {"tenantId": "t", "moduleKey": "build"})
        self.assertEqual(checkout["data"]["object"]["subscription"], "sub_1")

        invoice = json.loads(sim.build_event("invoice.payment_failed", subscription="sub_1"))
        self.assertEqual(invoice["data"]["object"]["parent"]["subscription_details"]["subscription"], "sub_1")

        updated = json.loads(sim.build_event("customer.subscription.updated", subscription="sub_1", status="past_due", cancel_at_period_end=True))
        self.assertEqual(updated["data"]["object"]["status"], "past_due")
        self.assertTrue(updated["data"]["object"]["cancel_at_period_end"])

    def test_a_checkout_without_tenant_has_no_metadata(self):
        checkout = json.loads(sim.build_event("checkout.session.completed"))
        self.assertEqual(checkout["data"]["object"]["metadata"], {})


class _Receiver(http.server.BaseHTTPRequestHandler):
    received: list = []
    status = 200

    def do_POST(self):  # noqa: N802
        body = self.rfile.read(int(self.headers["Content-Length"]))
        type(self).received.append((self.headers["Stripe-Signature"], body.decode()))
        self.send_response(type(self).status)
        if type(self).status == 302:
            self.send_header("Location", "https://api.erp.orken.com.br/api/billing/webhook")
        self.end_headers()

    def log_message(self, *args):
        pass  # sem log de cada pedido na saída dos testes


class Delivery(unittest.TestCase):
    def setUp(self):
        _Receiver.received = []
        _Receiver.status = 200
        self.server = http.server.ThreadingHTTPServer(("127.0.0.1", 0), _Receiver)
        threading.Thread(target=self.server.serve_forever, daemon=True).start()
        self.addCleanup(self.server.server_close)
        self.addCleanup(self.server.shutdown)
        self.url = f"http://127.0.0.1:{self.server.server_address[1]}/api/billing/webhook"

    def run_cli(self, *args):
        with mock.patch.dict(os.environ, {sim.SECRET_ENV: SECRET}), mock.patch("builtins.print"):
            return sim.main(["--url", self.url, *args])

    def test_the_signed_body_is_what_arrives(self):
        self.assertEqual(self.run_cli("--event", "customer.subscription.deleted", "--expect", "200"), 0)
        signature, body = _Receiver.received[0]
        timestamp = signature.split(",")[0][2:]
        self.assertEqual(signature, sim.sign(body, SECRET, int(timestamp)))

    def test_repeat_and_concurrent_send_the_same_event(self):
        self.assertEqual(self.run_cli("--event", "invoice.paid", "--repeat", "2", "--concurrent", "3"), 0)
        ids = {json.loads(body)["id"] for _, body in _Receiver.received}
        self.assertEqual(len(_Receiver.received), 6)
        self.assertEqual(len(ids), 1)

    def test_all_sends_one_of_each_type(self):
        self.assertEqual(self.run_cli("--all"), 0)
        self.assertEqual([json.loads(body)["type"] for _, body in _Receiver.received], list(sim.EVENT_TYPES))

    def test_a_bad_signature_does_not_use_the_real_secret(self):
        self.run_cli("--event", "invoice.paid", "--bad-signature")
        signature, body = _Receiver.received[0]
        timestamp = int(signature.split(",")[0][2:])
        self.assertNotEqual(signature, sim.sign(body, SECRET, timestamp))

    def test_an_unexpected_status_fails_the_run(self):
        _Receiver.status = 500
        self.assertEqual(self.run_cli("--event", "invoice.paid", "--expect", "200"), 1)

    def test_a_redirect_is_not_followed(self):
        _Receiver.status = 302
        self.assertEqual(self.run_cli("--event", "invoice.paid", "--expect", "302"), 0)
        self.assertEqual(len(_Receiver.received), 1)

    def test_production_refuses_before_sending_anything(self):
        with mock.patch.dict(os.environ, {sim.SECRET_ENV: SECRET}), mock.patch.object(sim, "deliver") as deliver, mock.patch("sys.stderr"):
            code = sim.main(["--url", "https://api.erp.orken.com.br/api/billing/webhook", "--all"])
        self.assertEqual(code, 2)
        deliver.assert_not_called()


if __name__ == "__main__":
    unittest.main()
