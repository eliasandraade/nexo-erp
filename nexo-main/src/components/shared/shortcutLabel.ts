/** Apple keyboards label the shortcut with ⌘; everyone else presses Ctrl. */
export function shortcutLabel(platform = typeof navigator === "undefined" ? "" : navigator.platform): string {
  return /mac|iphone|ipad|ipod/i.test(platform) ? "⌘ K" : "Ctrl K";
}
