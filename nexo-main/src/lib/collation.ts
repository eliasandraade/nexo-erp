/** Alphabetical order for names in Portuguese: the default sort puts "Água" after "Zona" and "São" after "Siqueira". */
export function comparePtBr(a: string, b: string): number {
  return a.localeCompare(b, "pt-BR");
}
