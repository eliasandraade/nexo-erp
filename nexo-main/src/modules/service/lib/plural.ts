/**
 * Portuguese plural of the preset nouns ("Ordem de serviço" → "Ordens de serviço",
 * "Sessão" → "Sessões", "Profissional" → "Profissionais"). Compound nouns pluralise the head
 * word only. Covers the endings the presets use; anything else just takes an "s".
 */
export function pluralPt(noun: string): string {
  const space = noun.indexOf(" ");
  const head = space === -1 ? noun : noun.slice(0, space);
  const tail = space === -1 ? "" : noun.slice(space);
  return pluralWord(head) + tail;
}

function pluralWord(word: string): string {
  const w = word.toLowerCase();
  if (w.endsWith("ão")) return word.slice(0, -2) + "ões";
  if (w.endsWith("m")) return word.slice(0, -1) + "ns";
  if (w.endsWith("al") || w.endsWith("el") || w.endsWith("ol") || w.endsWith("ul")) return word.slice(0, -1) + "is";
  if (w.endsWith("r") || w.endsWith("z")) return word + "es";
  if (w.endsWith("s")) return word;
  return word + "s";
}
