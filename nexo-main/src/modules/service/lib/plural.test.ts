import { describe, expect, it } from "vitest";
import { pluralPt } from "./plural";

describe("pluralPt", () => {
  it.each([
    ["Agendamento", "Agendamentos"],
    ["Ordem de serviço", "Ordens de serviço"],
    ["Ordem", "Ordens"],
    ["Comanda", "Comandas"],
    ["Sessão", "Sessões"],
    ["Avaliação", "Avaliações"],
    ["Profissional", "Profissionais"],
    ["Personal", "Personais"],
    ["Professor", "Professores"],
    ["Instrutor", "Instrutores"],
    ["Tutor", "Tutores"],
    ["Matrícula", "Matrículas"],
    ["consulta", "consultas"],
    ["ordem de serviço", "ordens de serviço"],
  ])("%s → %s", (one, many) => expect(pluralPt(one)).toBe(many));
});
