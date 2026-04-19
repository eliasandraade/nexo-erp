/**
 * Impressão operacional — restaurante
 *
 * Abre uma nova janela com o HTML gerado, aciona window.print() e fecha
 * automaticamente após o diálogo de impressão ser dispensado.
 *
 * Layout otimizado para papel térmico 80 mm, mas funciona em qualquer
 * impressora padrão (A4, etc.) — o operador ajusta escala no diálogo.
 */

import type { OrderDto } from "../types";

// ─── Shared styles ────────────────────────────────────────────────────────────

const BASE_STYLE = `
  * { margin: 0; padding: 0; box-sizing: border-box; }

  /*
   * @page size hint: tells the print dialog to pre-select 80mm thermal paper.
   * Most thermal printer drivers (Epson TM, Bematech, Elgin, Daruma, etc.)
   * expose a "80mm x Roll" or similar page size — the browser will pick it
   * automatically when this declaration is present, instead of defaulting to A4.
   * The operator only needs to select the correct printer once.
   */
  @page {
    size: 80mm auto;
    margin: 2mm 3mm;
  }

  body {
    font-family: 'Courier New', Courier, monospace;
    font-size: 13px;
    line-height: 1.4;
    color: #000;
    background: #fff;
    width: 80mm;
    max-width: 100%;
    padding: 4px 6px 16px;
    /* Prevent any colored background from bleeding into print */
    -webkit-print-color-adjust: exact;
    print-color-adjust: exact;
  }
  h1  { font-size: 15px; font-weight: bold; text-align: center; text-transform: uppercase; letter-spacing: 1px; }
  h2  { font-size: 13px; font-weight: bold; text-align: center; }
  .sep-solid  { border-top: 1px solid  #000; margin: 5px 0; }
  .sep-dashed { border-top: 1px dashed #000; margin: 5px 0; }
  .center { text-align: center; }
  .right  { text-align: right; }
  .bold   { font-weight: bold; }
  .small  { font-size: 11px; }
  .indent { padding-left: 10px; }
  .row { display: flex; justify-content: space-between; }

  /* Screen preview — shows 80mm column centered */
  @media screen {
    body {
      margin: 12px auto;
      border: 1px dashed #ccc;
      min-height: 200px;
    }
  }
`;

function wrapHtml(title: string, body: string): string {
  return `<!doctype html>
<html lang="pt-BR">
<head>
  <meta charset="UTF-8">
  <title>${title}</title>
  <style>${BASE_STYLE}</style>
</head>
<body>${body}</body>
</html>`;
}

function openPrint(html: string) {
  const win = window.open("", "_blank", "width=420,height=700,scrollbars=yes");
  if (!win) {
    alert("Permita pop-ups para este site para usar a impressão.");
    return;
  }
  win.document.write(html);
  win.document.close();
  win.focus();
  // Small delay so browser has time to render fonts before printing
  setTimeout(() => {
    win.print();
    win.addEventListener("afterprint", () => win.close());
  }, 250);
}

function fmtDate(iso: string): string {
  return new Date(iso).toLocaleString("pt-BR", {
    day: "2-digit", month: "2-digit", year: "numeric",
    hour: "2-digit", minute: "2-digit",
  });
}

function fmtMoney(v: number): string {
  return `R$ ${v.toFixed(2)}`;
}

// ─── Kitchen ticket ───────────────────────────────────────────────────────────

/**
 * Imprime um ticket de cozinha com todos os itens activos (não cancelados,
 * não entregues) — o operador decide quando acionar.
 */
export function printKitchen(order: OrderDto): void {
  const activeItems = order.items.filter(
    i => i.status !== "Cancelled" && i.status !== "Delivered"
  );

  if (activeItems.length === 0) {
    alert("Não há itens para imprimir na cozinha.");
    return;
  }

  const tableLabel = order.tableNumber
    ? `Mesa ${order.tableNumber}`
    : `Comanda #${order.orderNumber}`;

  const itemsHtml = activeItems.map(item => {
    const mods = item.modifiers.length > 0
      ? item.modifiers
          .map(m => `<div class="indent small">+ ${m.labelSnapshot}</div>`)
          .join("")
      : "";
    const notes = item.notes
      ? `<div class="indent small"><em>Obs: ${item.notes}</em></div>`
      : "";
    return `
      <div style="margin-bottom:8px">
        <div class="bold">${item.quantity}x  ${item.productName}</div>
        ${mods}${notes}
      </div>`;
  }).join("");

  const body = `
    <h1>*** COZINHA ***</h1>
    <div class="sep-solid"></div>
    <div class="row">
      <span class="bold">${tableLabel}</span>
      <span>#${order.orderNumber}</span>
    </div>
    <div class="small center" style="margin-bottom:4px">${fmtDate(order.openedAt)}</div>
    <div class="sep-dashed"></div>
    ${itemsHtml}
    <div class="sep-solid"></div>
    <div class="center small">${activeItems.length} item(ns)</div>
  `;

  openPrint(wrapHtml(`Cozinha — ${tableLabel}`, body));
}

// ─── Customer receipt ─────────────────────────────────────────────────────────

/**
 * Imprime o resumo da comanda para o cliente: itens, subtotal, couvert,
 * taxa de serviço e total.
 */
export function printReceipt(order: OrderDto): void {
  const billItems = order.items.filter(i => i.status !== "Cancelled");

  const tableLabel = order.tableNumber
    ? `Mesa ${order.tableNumber}`
    : `Comanda #${order.orderNumber}`;

  const itemsHtml = billItems.map(item => {
    const mods = item.modifiers.length > 0
      ? item.modifiers
          .map(m => `<div class="indent small">+ ${m.labelSnapshot}${m.priceSnapshot > 0 ? `  ${fmtMoney(m.priceSnapshot)}` : ""}</div>`)
          .join("")
      : "";
    return `
      <div class="row" style="margin-bottom:3px">
        <span>${item.quantity}x ${item.productName}</span>
        <span>${fmtMoney(item.total)}</span>
      </div>
      ${mods}`;
  }).join("");

  const couvertRow = order.couvertAmount > 0
    ? `<div class="row"><span>Couvert${order.partySize ? ` (${order.partySize} pess.)` : ""}</span><span>${fmtMoney(order.couvertAmount)}</span></div>`
    : "";

  const serviceFeeRow = order.serviceFeeAmount > 0
    ? `<div class="row"><span>Taxa de serviço</span><span>${fmtMoney(order.serviceFeeAmount)}</span></div>`
    : "";

  // total: prefer stored value, fall back to sum
  const total = order.total > 0
    ? order.total
    : order.itemsSubtotal + order.couvertAmount + order.serviceFeeAmount;

  const body = `
    <h1>Orken</h1>
    <div class="center small" style="margin-bottom:2px">Resumo da Comanda</div>
    <div class="sep-solid"></div>
    <div class="row">
      <span class="bold">${tableLabel}</span>
      <span class="bold">#${order.orderNumber}</span>
    </div>
    ${order.partySize ? `<div class="small">${order.partySize} pessoa(s)</div>` : ""}
    <div class="small">${fmtDate(order.openedAt)}</div>
    <div class="sep-dashed"></div>
    ${itemsHtml}
    <div class="sep-dashed"></div>
    <div class="row"><span>Subtotal</span><span>${fmtMoney(order.itemsSubtotal)}</span></div>
    ${couvertRow}
    ${serviceFeeRow}
    <div class="sep-solid"></div>
    <div class="row bold" style="font-size:15px">
      <span>TOTAL</span>
      <span>${fmtMoney(total)}</span>
    </div>
    <div class="sep-dashed" style="margin-top:10px"></div>
    <div class="center small">Obrigado pela visita!</div>
  `;

  openPrint(wrapHtml(`Comanda ${tableLabel}`, body));
}
