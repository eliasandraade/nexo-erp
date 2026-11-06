import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { OrderDto } from "../types";
import { printKitchen, printReceipt } from "./print";

const PAYLOAD = `<img src=x onerror="alert(1)"><script>alert(2)</script>"'&`;

function order(overrides: Partial<OrderDto> = {}): OrderDto {
  return {
    orderNumber: 12,
    tableNumber: null,
    openedAt: "2026-11-06T12:00:00Z",
    partySize: 2,
    itemsSubtotal: 50,
    couvertAmount: 0,
    serviceFeeAmount: 0,
    deliveryFeeAmount: 0,
    discountAmount: 0,
    total: 50,
    items: [
      {
        status: "Pending",
        quantity: 1,
        productName: PAYLOAD,
        notes: PAYLOAD,
        total: 50,
        modifiers: [{ labelSnapshot: PAYLOAD, priceSnapshot: 1 }],
      },
    ],
    ...overrides,
  } as unknown as OrderDto;
}

/** What the print window received, parsed the way the browser parses it. */
function printedDocument(print: () => void): Document {
  let written = "";
  const popup = {
    document: { write: (html: string) => { written += html; }, close: vi.fn() },
    focus: vi.fn(),
    print: vi.fn(),
    addEventListener: vi.fn(),
    opener: {} as unknown,
  };
  vi.spyOn(window, "open").mockReturnValue(popup as unknown as Window);
  print();
  expect(written).not.toBe("");
  return new DOMParser().parseFromString(written, "text/html");
}

describe.each([
  ["kitchen ticket", printKitchen],
  ["customer receipt", printReceipt],
])("%s", (_name, print) => {
  beforeEach(() => {
    vi.useFakeTimers();
  });

  afterEach(() => {
    vi.useRealTimers();
    vi.restoreAllMocks();
  });

  it("shows what the order says as text and never as markup", () => {
    const doc = printedDocument(() => print(order()));

    expect(doc.querySelector("img")).toBeNull();
    expect(doc.querySelector("script")).toBeNull();
    expect(doc.querySelector("[onerror]")).toBeNull();
    expect(doc.body.textContent).toContain(PAYLOAD);
  });

  it("escapes a table name too, in the body and in the title", () => {
    const doc = printedDocument(() => print(order({ tableNumber: PAYLOAD as unknown as OrderDto["tableNumber"] })));

    expect(doc.querySelector("img")).toBeNull();
    expect(doc.querySelector("script")).toBeNull();
    expect(doc.title).toContain(PAYLOAD);
  });

  it("allows no script in the print document, whatever gets past the escaping", () => {
    const doc = printedDocument(() => print(order()));

    const policy = doc.querySelector('meta[http-equiv="Content-Security-Policy"]')?.getAttribute("content") ?? "";
    expect(policy).toContain("default-src 'none'");
    expect(policy).not.toContain("script-src");
    expect(policy).toContain("style-src 'unsafe-inline'");
  });

  it("is still the same ticket for an ordinary order", () => {
    const doc = printedDocument(() =>
      print(order({
        items: [{
          status: "Pending", quantity: 2, productName: "Moqueca", notes: "Sem coentro", total: 80,
          modifiers: [{ labelSnapshot: "Arroz", priceSnapshot: 0 }],
        }] as unknown as OrderDto["items"],
      })),
    );

    expect(doc.body.textContent).toContain("2x");
    expect(doc.body.textContent).toContain("Moqueca");
    expect(doc.body.textContent).toContain("Arroz");
    expect(doc.querySelector("style")?.textContent).toContain("80mm");
  });
});

describe("print window", () => {
  beforeEach(() => {
    vi.useFakeTimers();
  });

  afterEach(() => {
    vi.useRealTimers();
    vi.restoreAllMocks();
  });

  it("gives the document no way back to the page that opened it", () => {
    const popup = {
      document: { write: vi.fn(), close: vi.fn() },
      focus: vi.fn(),
      print: vi.fn(),
      addEventListener: vi.fn(),
      opener: {} as unknown,
    };
    vi.spyOn(window, "open").mockReturnValue(popup as unknown as Window);

    printKitchen(order());

    expect(popup.opener).toBeNull();
  });

  it("the kitchen ticket prints the notes", () => {
    const doc = printedDocument(() => printKitchen(order()));

    expect(doc.querySelector("em")?.textContent).toContain(PAYLOAD);
  });
});
