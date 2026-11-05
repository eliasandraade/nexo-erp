#!/usr/bin/env python3
"""Envia eventos sintéticos do Stripe a um webhook do ERP, assinados localmente.

Não fala com o Stripe e não usa chave do Stripe: só o segredo de assinatura do webhook (whsec_...), lido de
STRIPE_WEBHOOK_SECRET. Recusa os hosts de produção. Roteiro completo: docs/engineering/stripe-homologacao.md.

    export STRIPE_WEBHOOK_SECRET=whsec_...
    python nexo-backend/tools/stripe_webhook_sim.py --url http://localhost:5199/api/billing/webhook --list
    python nexo-backend/tools/stripe_webhook_sim.py --url URL --event customer.subscription.updated --subscription sub_x
    python nexo-backend/tools/stripe_webhook_sim.py --url URL --event customer.subscription.deleted --concurrent 5
"""

from __future__ import annotations

import argparse
import concurrent.futures
import hashlib
import hmac
import json
import os
import re
import sys
import time
import urllib.error
import urllib.parse
import urllib.request
import uuid

SECRET_ENV = "STRIPE_WEBHOOK_SECRET"

# Hosts servidos pela produção. www é o mesmo serviço do ERP; os demais são da Orken Platform e nunca recebem este webhook.
PRODUCTION_HOSTS = frozenset(
    {
        "api.erp.orken.com.br",
        "erp.orken.com.br",
        "www.orken.com.br",
        "orken.com.br",
        "app.orken.com.br",
        "api.orken.com.br",
        "account.orken.com.br",
    }
)
LOCAL_HOSTS = frozenset({"localhost", "127.0.0.1", "::1"})
HOST_PATTERN = re.compile(r"^[a-z0-9.:-]+$")

# Tipos tratados por StripeWebhookService (DispatchAsync). invoice.paid não tem efeito: serve para ver o registro.
EVENT_TYPES = (
    "checkout.session.completed",
    "customer.subscription.updated",
    "customer.subscription.deleted",
    "invoice.payment_failed",
    "invoice.paid",
)
API_VERSION = "2026-05-27.dahlia"


class Refused(Exception):
    """A configuração pedida não pode ser usada; a mensagem nunca contém o segredo."""


def check_url(url: str) -> str:
    """Devolve a URL se ela pode receber eventos sintéticos; recusa produção e formas ambíguas de URL."""
    try:
        parts = urllib.parse.urlsplit(url.strip())
        host = (parts.hostname or "").rstrip(".").lower()
        _ = parts.port  # uma porta fora do intervalo só aparece aqui
    except ValueError as error:
        raise Refused(f"URL inválida: {error}") from error

    if parts.scheme not in ("http", "https"):
        raise Refused("A URL precisa usar HTTP ou HTTPS.")
    if not host or not HOST_PATTERN.match(host):
        raise Refused("A URL não tem um host válido.")
    if parts.username or parts.password or "\\" in url:
        raise Refused("A URL não pode ter usuário, senha ou barra invertida.")
    if host in PRODUCTION_HOSTS or host.endswith(".api.erp.orken.com.br"):
        raise Refused(f"{host} é produção. Este script só envia para staging ou para a máquina local.")
    if parts.scheme == "http" and host not in LOCAL_HOSTS:
        raise Refused("HTTP sem TLS só para a máquina local. Use HTTPS em staging.")
    return url.strip()


def check_secret(secret: str | None) -> str:
    """Só aceita um segredo de webhook; uma chave de API aqui é erro de quem está rodando."""
    if not secret:
        raise Refused(f"Defina {SECRET_ENV} com o segredo de assinatura do webhook de teste (whsec_...).")
    if not secret.startswith("whsec_"):
        raise Refused(f"{SECRET_ENV} precisa ser um segredo de webhook (whsec_...). Este script não usa chave do Stripe.")
    return secret


def sign(payload: str, secret: str, timestamp: int | None = None) -> str:
    """Cabeçalho Stripe-Signature: HMAC-SHA256 de "{timestamp}.{corpo}" com o segredo inteiro, como o Stripe assina."""
    moment = int(time.time()) if timestamp is None else timestamp
    digest = hmac.new(secret.encode("utf-8"), f"{moment}.{payload}".encode("utf-8"), hashlib.sha256).hexdigest()
    return f"t={moment},v1={digest}"


def subscription_object(subscription: str, status: str, cancel_at_period_end: bool, now: int) -> dict:
    return {
        "id": subscription,
        "object": "subscription",
        "status": status,
        "cancel_at_period_end": cancel_at_period_end,
        "items": {
            "object": "list",
            "data": [
                {
                    "id": "si_sintetico",
                    "object": "subscription_item",
                    "current_period_start": now,
                    "current_period_end": now + 30 * 24 * 3600,
                    "price": {"id": "price_sintetico", "object": "price", "recurring": {"interval": "month"}},
                }
            ],
        },
    }


def invoice_object(subscription: str) -> dict:
    return {
        "id": f"in_{uuid.uuid4().hex}",
        "object": "invoice",
        "parent": {"type": "subscription_details", "subscription_details": {"subscription": subscription}},
    }


def checkout_object(subscription: str, tenant_id: str | None, module: str) -> dict:
    metadata = {"tenantId": tenant_id, "moduleKey": module} if tenant_id else {}
    return {
        "id": f"cs_{uuid.uuid4().hex}",
        "object": "checkout.session",
        "customer": "cus_sintetico",
        "subscription": subscription,
        "metadata": metadata,
    }


def build_event(
    event_type: str,
    subscription: str = "sub_sintetico",
    status: str = "active",
    cancel_at_period_end: bool = False,
    tenant_id: str | None = None,
    module: str = "build",
    event_id: str | None = None,
    now: int | None = None,
) -> str:
    """Corpo do evento, com a forma da versão da API da biblioteca do backend (Stripe.net 52). Sempre livemode=false."""
    moment = int(time.time()) if now is None else now
    if event_type == "checkout.session.completed":
        data = checkout_object(subscription, tenant_id, module)
    elif event_type.startswith("customer.subscription."):
        data = subscription_object(subscription, status, cancel_at_period_end, moment)
    elif event_type.startswith("invoice."):
        data = invoice_object(subscription)
    else:
        raise Refused(f"Tipo de evento desconhecido: {event_type}. Tipos: {', '.join(EVENT_TYPES)}.")
    return json.dumps(
        {
            "id": event_id or f"evt_sintetico_{uuid.uuid4().hex}",
            "object": "event",
            "api_version": API_VERSION,
            "created": moment,
            "livemode": False,
            "pending_webhooks": 1,
            "request": {"id": None, "idempotency_key": None},
            "type": event_type,
            "data": {"object": data},
        },
        separators=(",", ":"),
    )


class _NoRedirect(urllib.request.HTTPRedirectHandler):
    """Um redirecionamento poderia levar o evento assinado a outro host, sem passar pela checagem da URL."""

    def redirect_request(self, *args, **kwargs):
        return None


def deliver(url: str, body: str, signature: str, timeout: float) -> int:
    request = urllib.request.Request(
        url,
        data=body.encode("utf-8"),
        method="POST",
        headers={"Content-Type": "application/json", "Stripe-Signature": signature, "User-Agent": "orken-stripe-webhook-sim"},
    )
    opener = urllib.request.build_opener(_NoRedirect())
    try:
        with opener.open(request, timeout=timeout) as response:
            return response.status
    except urllib.error.HTTPError as error:
        return error.code


def parse_args(argv: list[str]) -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Eventos sintéticos do Stripe, assinados localmente, para staging ou máquina local.")
    parser.add_argument("--url", default=os.environ.get("STRIPE_WEBHOOK_URL"), help="URL do webhook (ou STRIPE_WEBHOOK_URL).")
    parser.add_argument("--event", choices=EVENT_TYPES, help="Tipo do evento.")
    parser.add_argument("--all", action="store_true", help="Envia um evento de cada tipo.")
    parser.add_argument("--list", action="store_true", help="Lista os tipos e termina, sem enviar nada.")
    parser.add_argument("--subscription", default="sub_sintetico", help="Id da assinatura (sub_...).")
    parser.add_argument("--status", default="active", help="Status no corpo de customer.subscription.*.")
    parser.add_argument("--cancel-at-period-end", action="store_true", help="cancel_at_period_end no corpo do evento.")
    parser.add_argument("--tenant-id", help="Metadado tenantId do checkout. Sem ele, o checkout é aceito e não tem efeito.")
    parser.add_argument("--module", default="build", help="Metadado moduleKey do checkout.")
    parser.add_argument("--event-id", help="Reaproveita um id de evento, para testar entrega repetida.")
    parser.add_argument("--repeat", type=int, default=1, help="Entrega o mesmo evento N vezes, em sequência.")
    parser.add_argument("--concurrent", type=int, default=1, help="Entrega o mesmo evento N vezes, ao mesmo tempo.")
    parser.add_argument("--bad-signature", action="store_true", help="Assina com outro segredo (o esperado é 400).")
    parser.add_argument("--stale-signature", action="store_true", help="Assina com horário de 10 minutos atrás (o esperado é 400).")
    parser.add_argument("--expect", type=int, help="Termina com erro se alguma resposta for diferente deste status.")
    parser.add_argument("--timeout", type=float, default=30.0, help="Tempo limite de cada entrega, em segundos.")
    return parser.parse_args(argv)


def delivery_target(args: argparse.Namespace) -> tuple[str, str]:
    """Valida o pedido antes de qualquer envio; devolve a URL e o segredo."""
    if not args.url:
        raise Refused("Informe --url (ou STRIPE_WEBHOOK_URL).")
    url = check_url(args.url)
    secret = check_secret(os.environ.get(SECRET_ENV))
    if not args.all and not args.event:
        raise Refused("Informe --event ou --all.")
    if args.repeat < 1 or args.concurrent < 1:
        raise Refused("--repeat e --concurrent precisam ser pelo menos 1.")
    return url, secret


def deliver_event(args: argparse.Namespace, url: str, body: str, signing_secret: str, timestamp: int | None) -> bool:
    """Entrega o evento --repeat vezes, --concurrent de cada vez. False se algum status fugiu de --expect."""
    event = json.loads(body)
    as_expected = True
    for _ in range(args.repeat):
        signature = sign(body, signing_secret, timestamp)
        with concurrent.futures.ThreadPoolExecutor(max_workers=args.concurrent) as pool:
            deliveries = [pool.submit(deliver, url, body, signature, args.timeout) for _ in range(args.concurrent)]
            statuses = [delivery.result() for delivery in deliveries]
        print(f"{event['type']} {event['id']} -> {' '.join(str(s) for s in statuses)}")
        if args.expect is not None and any(s != args.expect for s in statuses):
            as_expected = False
    return as_expected


def run(args: argparse.Namespace) -> int:
    if args.list:
        print("\n".join(EVENT_TYPES))
        return 0

    url, secret = delivery_target(args)
    signing_secret = "whsec_outro_segredo_de_teste" if args.bad_signature else secret
    timestamp = int(time.time()) - 600 if args.stale_signature else None

    results = [
        deliver_event(
            args,
            url,
            build_event(
                event_type,
                subscription=args.subscription,
                status=args.status,
                cancel_at_period_end=args.cancel_at_period_end,
                tenant_id=args.tenant_id,
                module=args.module,
                event_id=args.event_id,
            ),
            signing_secret,
            timestamp,
        )
        for event_type in (EVENT_TYPES if args.all else (args.event,))
    ]
    return 0 if all(results) else 1


def main(argv: list[str]) -> int:
    try:
        return run(parse_args(argv))
    except Refused as refused:
        print(f"Recusado: {refused}", file=sys.stderr)
        return 2
    except urllib.error.URLError as error:
        print(f"Falha ao conectar: {error.reason}", file=sys.stderr)
        return 3


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
