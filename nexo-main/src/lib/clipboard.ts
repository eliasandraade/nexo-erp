/**
 * Copies text. Resolves `false` when the browser refuses (permission, no focus, in-app browsers,
 * no clipboard API at all) and never rejects, so the caller can always tell the user what happened.
 */
export async function copyToClipboard(text: string): Promise<boolean> {
  try {
    await navigator.clipboard.writeText(text);
    return true;
  } catch {
    return false;
  }
}
