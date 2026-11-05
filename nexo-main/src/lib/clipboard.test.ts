import { describe, it, expect, vi, afterEach } from "vitest";
import { copyToClipboard } from "./clipboard";

function stubClipboard(value: unknown) {
  Object.defineProperty(navigator, "clipboard", { value, configurable: true });
}

afterEach(() => stubClipboard(undefined));

describe("copyToClipboard", () => {
  it("resolves true when the browser copied", async () => {
    const writeText = vi.fn().mockResolvedValue(undefined);
    stubClipboard({ writeText });
    await expect(copyToClipboard("abc")).resolves.toBe(true);
    expect(writeText).toHaveBeenCalledWith("abc");
  });

  it("resolves false when the browser refuses", async () => {
    stubClipboard({ writeText: vi.fn().mockRejectedValue(new DOMException("denied", "NotAllowedError")) });
    await expect(copyToClipboard("abc")).resolves.toBe(false);
  });

  it("resolves false where the clipboard API does not exist", async () => {
    stubClipboard(undefined);
    await expect(copyToClipboard("abc")).resolves.toBe(false);
  });
});
