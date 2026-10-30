// DEV ONLY. Synthetic records for browser QA, never a production fallback.
import { PortalSession } from "../PortalPage";
import type { Portal } from "../types";
import type { PortalGateway } from "../api/gateway";
const portal: Portal = {
  company: "Construtora de teste",
  name: "Residência de revisão",
  progress: 67,
  planned: 71,
  updatedAt: "2026-10-30",
  delivery: "2027-08-18",
  lifecycle: "building",
  forecast: "Previsão informada pela construtora para revisão da interface.",
  stages: [
    { id: "1", name: "Fundação", status: "complete", progress: 100 },
    { id: "2", name: "Estrutura", status: "active", progress: 67 },
    { id: "3", name: "Acabamentos", status: "waiting", progress: null },
  ],
  updates: [
    {
      id: "u1",
      title: "Estrutura do térreo concluída",
      body: "Registro sintético para revisão local. Nenhum dado de uma obra real.",
      publishedAt: "2026-10-29",
      planned: false,
    },
  ],
  weekly: {
    body: "Esta é uma publicação sintética para avaliar hierarquia e leitura. A construtora publicará aqui o resumo real da semana.",
    publishedAt: "2026-10-30",
  },
  photos: [],
  documents: [],
  finance: null,
  decisions: [
    {
      id: "d1",
      version: "1",
      title: "Piso da área social",
      due: "2026-11-14",
      status: "pending",
      options: [
        {
          id: "a",
          name: "Opção de teste A",
          description: "Especificação para revisão",
          extra: 0,
          days: 0,
        },
        {
          id: "b",
          name: "Opção de teste B",
          description: "Especificação alternativa",
          extra: 2840,
          days: 3,
        },
      ],
    },
  ],
  warranties: [],
  assistance: null,
};
const scenario = new URLSearchParams(location.search).get("scenario");
if (scenario === "handover") {
  portal.lifecycle = "handover";
  portal.progress = 100;
  portal.warranties = [
    { id: "w", name: "Impermeabilização", until: "2030-01-01" },
  ];
  portal.assistance = { categories: ["Instalações"], tickets: [] };
}
if (scenario === "model")
  portal.model = {
    status: "ready",
    url: "https://fixtures.invalid/model.glb",
    elements: [
      {
        id: "wall",
        node: "Wall",
        name: "Parede de teste",
        floor: "Térreo",
        stage: "Estrutura",
        status: "complete",
      },
    ],
    snapshots: [
      { id: "past", date: "2026-10-01", states: { wall: "waiting" } },
    ],
  };
const gateway: PortalGateway = {
  load: async () =>
    scenario === "private"
      ? { kind: "private" }
      : scenario === "missing"
        ? { kind: "not-found" }
        : { kind: "ready", portal },
  unlock: async (_t, _p, password) =>
    password === "review-only"
      ? { kind: "ready", portal }
      : { kind: "wrong-password" },
  actions: () => ({
    decide: async () => {},
    requestAssistance: async () => {},
  }),
};
export default function Review() {
  return (
    <>
      <div style={{ padding: 8, background: "#ffe6a2", color: "#382c12" }}>
        REVISÃO LOCAL · DADOS SINTÉTICOS · NÃO É UMA OBRA REAL
      </div>
      <PortalSession tenant="review" project="review" gateway={gateway} />
    </>
  );
}
