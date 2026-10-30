import type * as D from "./generated/portal-types";
import type { Phase, Portal, TwinModel, TwinSnapshot } from "../types";
export interface PortalResources {
  progress?: D.PublicBuildProgressDto;
  timeline?: D.PublicBuildTimelineDto;
  photos?: D.PublicBuildPhotosDto;
  orders?: D.PublicBuildChangeOrdersDto;
  selections?: D.PublicBuildSelectionsDto;
  measurements?: D.PublicBuildMeasurementsDto;
  handover?: D.PublicBuildHandoverDto;
  warranty?: D.PublicBuildWarrantyDto;
  assistance?: D.PublicBuildAssistanceListDto;
  twin?: D.PublicBuildTwinDto;
  captures?: D.PublicBuildRealityCapturesDto;
  documents?: D.PublicBuildDocumentsDto;
}
const phases: Record<string, Phase> = {
  Pending: "waiting",
  NotStarted: "waiting",
  InProgress: "active",
  Completed: "complete",
  Delayed: "delayed",
  Blocked: "blocked",
  NeedsReview: "review",
};
export const phase = (s: string): Phase => phases[s] ?? "unknown";
export const nullableNumber = (n: number | null | undefined) =>
  typeof n === "number" && Number.isFinite(n) ? n : null;
export function adaptState(s: D.PublicBuildTwinStateDto): TwinSnapshot {
  const flatten = (states: Record<string, string[]>) =>
    Object.fromEntries(
      Object.entries(states).flatMap(([status, ids]) =>
        ids.map((id) => [id, phase(status)]),
      ),
    );
  return {
    id: s.date,
    date: s.date,
    states: flatten(s.states),
    zoneStates: flatten(s.zoneStates),
  };
}
export function adaptTwin(d: D.PublicBuildTwinDto): TwinModel {
  const snapshot = adaptState(d.state);
  return {
    status:
      d.viewerUrl ||
      (d.geometrySource === "SimplifiedRepresentation" &&
        d.zones.some(
          (z) =>
            z.simplifiedBox &&
            [
              z.simplifiedBox.width,
              z.simplifiedBox.height,
              z.simplifiedBox.depth,
            ].every((n) => Number.isFinite(n) && n > 0),
        )) ||
      (d.geometrySource === "AssistedMapping" && d.zones.length > 0)
        ? "ready"
        : "unavailable",
    url: d.viewerUrl ?? undefined,
    geometrySource: d.geometrySource ?? undefined,
    disclaimer: d.disclaimer,
    minDate: d.minDate ?? undefined,
    maxDate: d.maxDate,
    unbound: d.state.unbound,
    zoneStates: snapshot.zoneStates,
    zones: d.zones.map((z) => ({
      id: z.id,
      name: z.name,
      kind: z.kind,
      node: z.externalId ?? undefined,
      parentId: z.parentId ?? undefined,
      box: z.simplifiedBox ?? undefined,
    })),
    // externalId is the published GLTF node binding. Never infer from diary or name.
    elements: Object.entries(snapshot.states).map(([id, status]) => ({
      id,
      node: id,
      name: id,
      floor: "",
      stage: "",
      status,
    })),
    snapshots: [snapshot],
  };
}
export function adaptPortal(
  b: D.PublicBuildBootstrapDto,
  r: PortalResources,
  errors: Portal["sectionErrors"] = [],
): Portal {
  const has = (s: string) => b.sections.includes(s);
  const progress = has("overallProgress")
    ? (r.progress ?? b.progress)
    : undefined;
  const latest = has("clientUpdates") ? (b.latestUpdates ?? []) : [];
  const timeline = (r.timeline?.items ?? []).filter(
    (t) =>
      ({
        update: has("clientUpdates"),
        daily_log: has("dailyLogs"),
        stage_progress: has("stageProgress"),
        stage_completed: has("stageProgress"),
        photo: has("photos"),
      })[t.type],
  );
  const summary = latest.find((u) => u.kind === "WeeklyDigest");
  const expires: number[] = [];
  const collect = (value: unknown) => {
    if (!value || typeof value !== "object") return;
    for (const [key, item] of Object.entries(value)) {
      if (
        /expire(s)?at$/i.test(key) &&
        typeof item === "string" &&
        Number.isFinite(Date.parse(item))
      )
        expires.push(Date.parse(item));
      else if (typeof item === "object") collect(item);
    }
  };
  collect(r);
  const updates = timeline.length
    ? timeline.map((t, i) => ({
        id: `${t.type}-${t.occurredAt}-${i}`,
        title:
          t.title ??
          t.stageName ??
          (t.type === "daily_log"
            ? "Diário da obra"
            : t.type === "photo"
              ? "Imagem publicada"
              : "Avanço da obra"),
        body:
          t.text ??
          t.caption ??
          (t.percent == null
            ? ""
            : `Avanço registrado: ${t.percent.toLocaleString("pt-BR")}%`),
        publishedAt: t.occurredAt,
        planned: false,
      }))
    : latest.map((u, i) => ({
        id: `update-${i}`,
        title: u.title,
        body: u.body,
        publishedAt: u.publishedAt,
        planned: false,
      }));
  const p: Portal = {
    sections: [...b.sections],
    sectionErrors: errors,
    refreshAt: new Date(
      Math.max(
        Date.now() + 5000,
        Math.min(Date.now() + 14 * 60000, ...expires) - 30000,
      ),
    ).toISOString(),
    visibility: b.portal.access === "Public" ? "public" : "private",
    indexable: false,
    title: b.portal.title ?? undefined,
    welcome: b.portal.welcomeMessage ?? undefined,
    company: b.company.name,
    name: b.project.name,
    logo: b.portal.logoUrl ?? b.company.logoUrl ?? undefined,
    cover: b.portal.coverUrl ?? undefined,
    color: b.portal.primaryColor ?? undefined,
    secondaryColor: b.portal.accentColor ?? undefined,
    contact:
      has("contact") && b.contact?.whatsApp
        ? `https://wa.me/${b.contact.whatsApp.replace(/\D/g, "")}`
        : undefined,
    progress: nullableNumber(progress?.actualPercent),
    planned: nullableNumber(progress?.plannedPercent),
    updatedAt: progress?.asOf ?? latest[0]?.publishedAt ?? null,
    delivery: has("forecastDelivery")
      ? (progress?.forecastDelivery ?? null)
      : null,
    lifecycle:
      b.phase === "delivered"
        ? "handover"
        : b.project.status === "Completed"
          ? "completed"
          : "building",
    forecast: null,
    curves:
      has("overallProgress") && r.progress
        ? {
            actual: r.progress.curves.actual,
            planned: r.progress.curves.planned ?? undefined,
            forecast: has("forecastDelivery")
              ? (r.progress.curves.forecast ?? undefined)
              : undefined,
          }
        : undefined,
    schedule:
      has("schedule") && b.schedule
        ? b.schedule.stages.map((s) => ({
            name: s.name,
            order: s.order,
            start: s.plannedStart ?? null,
            end: s.plannedEnd ?? null,
          }))
        : undefined,
    stages: has("stageProgress")
      ? (b.stages ?? []).map((s) => ({
          id: `stage-${s.order}`,
          name: s.name,
          status: phase(s.status),
          progress: nullableNumber(s.actualPercent),
        }))
      : [],
    weekly: summary
      ? { body: summary.body, publishedAt: summary.publishedAt }
      : null,
    updates,
    photos: has("photos")
      ? (r.photos?.items ?? []).map((f, i) => ({
          id: `photo-${i}`,
          url: f.url ?? "",
          caption: f.caption ?? "Foto da obra",
          date: f.date,
        }))
      : [],
    documents: has("documents")
      ? (r.documents?.items ?? []).map((d, i) => ({
          id: `document-${i}`,
          name: d.title,
          category: d.category,
          url: d.url ?? "",
        }))
      : [],
    finance:
      has("financialSummary") && b.financialSummary
        ? {
            level: "summary",
            budget: nullableNumber(b.financialSummary.approvedBudget),
            executed: nullableNumber(b.financialSummary.realizedTotal),
            categories: [],
            transactions: [],
          }
        : null,
    decisions: has("selections")
      ? (r.selections?.items ?? []).map((s) => ({
          id: s.id,
          version: "",
          title: s.title,
          description: s.description ?? undefined,
          due: s.deadline ?? "",
          status: s.chosenOptionId ? "confirmed" : "pending",
          statusLabel:
            s.status === "Submitted"
              ? "Enviada, aguardando a construtora"
              : s.status === "Confirmed"
                ? "Confirmada pela construtora"
                : s.deadlinePassed
                  ? "Prazo encerrado"
                  : "Aguardando sua escolha",
          canChoose: s.canChoose,
          selected: s.chosenOptionId ?? undefined,
          options: s.options.map((o) => ({
            id: o.id,
            name: o.name,
            description: o.description ?? "",
            extra: nullableNumber(o.priceDelta),
            days: nullableNumber(o.leadTimeDays),
            leadTime: true,
            image: o.imageUrl ?? undefined,
          })),
        }))
      : [],
    warranties: has("postConstruction")
      ? (r.warranty?.items ?? b.property?.warrantyTerms ?? []).map((w, i) => ({
          id: `warranty-${i}`,
          name: w.system,
          until: w.expiresAt,
        }))
      : [],
    assistance:
      has("postConstruction") && r.assistance
        ? {
            canOpen: r.assistance.canOpen,
            categories: r.assistance.categories,
            tickets: r.assistance.items.map((t) => ({
              id: t.id,
              number: t.number,
              title: t.title,
              status: t.status,
              description: t.description,
              canAddPhotos: t.canAddPhotos,
              canGiveFeedback: t.canGiveFeedback,
              photos: t.photoUrls,
              rating: t.rating ?? undefined,
              resolution: t.resolutionNotes ?? t.rejectionReason ?? undefined,
              scheduled: t.scheduledFor ?? undefined,
              timeline: t.timeline,
            })),
          }
        : null,
    changeOrders:
      has("changeOrders") && r.orders
        ? r.orders.items.map((o) => ({
            id: o.id,
            number: o.number,
            title: o.title,
            description: o.description ?? undefined,
            status: o.status,
            canDecide: o.canDecide,
            cost: has("financialSummary") ? nullableNumber(o.costImpact) : null,
            days: o.scheduleImpactDays,
            items: o.items.map((i) => ({
              name: i.name,
              quantity: i.quantity,
              unit: i.unit,
            })),
          }))
        : undefined,
    measurements:
      has("measurements") && r.measurements
        ? r.measurements.items.map((m) => ({
            number: m.number,
            start: m.periodStart,
            end: m.periodEnd,
            amount: has("financialSummary")
              ? nullableNumber(m.totalAmount)
              : null,
            items: m.items.map((i) => ({
              name: i.stageName,
              previous: i.previousPercent,
              measured: i.measuredPercent,
            })),
          }))
        : undefined,
    weather:
      has("weather") && b.weather
        ? [
            {
              date: b.weather.date,
              description: `${b.weather.description}. ${b.weather.temperatureMin}–${b.weather.temperatureMax} °C. Chuva: ${b.weather.precipitationMm} mm.`,
              impact: `${b.weather.notice} Fonte: ${b.weather.source}.`,
            },
          ]
        : undefined,
    model: has("model3d") && r.twin ? adaptTwin(r.twin) : undefined,
    warrantyNotice: has("postConstruction") ? r.warranty?.notice : undefined,
    pagination: {
      timeline: r.timeline?.nextBefore ?? undefined,
      photos:
        r.photos && r.photos.items.length < r.photos.total
          ? r.photos.items.length
          : undefined,
      captures:
        r.captures && r.captures.items.length < r.captures.total
          ? r.captures.items.length
          : undefined,
    },
  };
  if (has("postConstruction") && r.handover) {
    const h = r.handover;
    p.handover = {
      status: h.status,
      scheduled: h.scheduledFor ?? undefined,
      delivered: h.deliveredAt ?? undefined,
      accepted: h.acceptedAt ?? undefined,
      canAccept: h.canAccept,
      keys: h.keysDelivered,
      documents: h.documents.map((d) => ({
        name: d.name,
        url: d.url ?? undefined,
      })),
      inspection: h.walkthrough
        ? { status: h.walkthrough.status, items: h.walkthrough.items }
        : undefined,
    };
  }
  if (has("realityCapture") && r.captures) {
    p.captures = r.captures.items
      .filter((c) => c.type === "Photo360" && c.url)
      .map((c, i) => ({
        id: `capture-${i}`,
        name: c.title,
        date: c.capturedAt,
        floor: c.zoneName ?? "",
        url: c.url!,
        hotspots: [],
      }));
    r.captures.items.forEach((c, i) => {
      if (
        ["Photo", "Video", "Drone"].includes(c.type) &&
        (c.contentType.startsWith("image/") ||
          c.contentType.startsWith("video/"))
      )
        p.photos.push({
          id: `capture-media-${i}`,
          caption: c.title,
          date: c.capturedAt,
          url: c.url ?? "",
          kind: c.contentType.startsWith("video/") ? "video" : "image",
          stage: c.stageName ?? undefined,
          zone: c.zoneName ?? undefined,
        });
    });
    p.downloads = r.captures.items
      .filter((c) => c.type === "PointCloud")
      .map((c) => ({
        name: c.title,
        date: c.capturedAt,
        url: c.url ?? undefined,
      }));
  }
  return p;
}
