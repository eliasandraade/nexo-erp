import { fireEvent, screen } from "@testing-library/react";

/**
 * O jsdom não implementa captura de ponteiro, scrollIntoView nem ResizeObserver, que o Select e o
 * DropdownMenu do Radix usam ao abrir e posicionar. Sem isto, abrir um deles no teste lança erro.
 */
export function installRadixPolyfills() {
  const proto = Element.prototype as unknown as Record<string, unknown>;
  proto.hasPointerCapture ??= () => false;
  proto.setPointerCapture ??= () => {};
  proto.releasePointerCapture ??= () => {};
  proto.scrollIntoView ??= () => {};
  globalThis.ResizeObserver ??= class { observe() {} unobserve() {} disconnect() {} } as unknown as typeof ResizeObserver;
}

/** Abre um Select do Radix pelo gatilho e escolhe a opção pelo texto, como a pessoa faz. */
export async function pickOption(trigger: HTMLElement, name: string | RegExp) {
  fireEvent.click(trigger);
  fireEvent.click(await screen.findByRole("option", { name }));
}

/** Abre um DropdownMenu do Radix pelo teclado (o jsdom não dispara pointerdown com botão). */
export function openMenu(trigger: HTMLElement) {
  fireEvent.keyDown(trigger, { key: "Enter" });
}

/** Ativa uma aba do Radix: a troca acontece no mousedown com o botão principal. */
export function selectTab(tab: HTMLElement) {
  fireEvent.mouseDown(tab, { button: 0 });
}
