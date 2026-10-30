import type * as D from "./generated/portal-types";
import { adaptPortal, adaptState, type PortalResources } from "./adapter";
import type { Access, Portal, TwinSnapshot } from "../types";
import type { PortalActions, PortalGateway } from "./gateway";

export class PortalHttpError extends Error {
  constructor(
    public status: number,
    public code: string,
    message: string,
    public retryAt?: number,
    public teaser?: D.PublicBuildPortalTeaserDto | null,
  ) {
    super(message);
  }
}
export function actionError(error: unknown): string {
  return error instanceof PortalHttpError
    ? `${error.message}${error.status === 409 ? " Atualize o registro antes de continuar." : ""}${error.retryAt ? ` Tente após ${new Date(error.retryAt).toLocaleTimeString("pt-BR")}.` : ""}`
    : "Não foi possível concluir. Confira sua conexão e tente novamente.";
}
function accessError(e: unknown): Access {
  if (!(e instanceof PortalHttpError)) return { kind: "unavailable" };
  const kind =
    e.status === 404
      ? "not-found"
      : e.status === 429
        ? "rate-limited"
        : e.code === "portal_password_required"
          ? "private"
          : e.code === "portal_password_invalid"
            ? "wrong-password"
            : e.status === 401
              ? "expired"
              : "unavailable";
  return {
    kind,
    retryAt: e.retryAt ? new Date(e.retryAt).toISOString() : undefined,
    publicIdentity: e.teaser
      ? {
          company: e.teaser.name,
          name: e.teaser.title ?? "",
          logo: e.teaser.logoUrl ?? undefined,
        }
      : undefined,
  };
}
export function portalApiRoot(
  base = import.meta.env.VITE_API_BASE_URL ?? "/api",
): string {
  return `${base.replace(/\/+$/, "").replace(/\/api$/, "")}/api/public/build`;
}

export function createHttpGateway(
  options: { base?: string; fetch?: typeof fetch } = {},
): PortalGateway {
  const requestFetch = options.fetch ?? globalThis.fetch.bind(globalThis);
  let key = "",
    token = "",
    expiresAt = 0,
    blockedUntil = 0,
    generation = 0;
  let bootstrap: D.PublicBuildBootstrapDto | undefined;
  let resources: PortalResources = {};
  let errors: NonNullable<Portal["sectionErrors"]> = [];
  const listeners = new Set<(event: Access | "changed") => void>();
  const etags = new Map<string, { etag: string; snapshot: TwinSnapshot }>();
  const emit = (event: Access | "changed") =>
    listeners.forEach((f) => f(event));
  const reset = () => {
    token = "";
    expiresAt = 0;
    blockedUntil = 0;
    bootstrap = undefined;
    resources = {};
    errors = [];
    etags.clear();
    generation++;
  };
  const path = (tenant: string, project: string) => {
    const next = `${encodeURIComponent(tenant)}/${encodeURIComponent(project)}`;
    if (next !== key) {
      reset();
      key = next;
    }
    return `${portalApiRoot(options.base)}/${next}`;
  };
  const raw = async (
    base: string,
    suffix: string,
    init: RequestInit = {},
    etag?: string,
  ) => {
    if (blockedUntil > Date.now())
      throw new PortalHttpError(
        429,
        "rate_limited",
        "Aguarde antes de tentar novamente.",
        blockedUntil,
      );
    if (token && expiresAt <= Date.now()) {
      token = "";
      etags.clear();
      const e = new PortalHttpError(
        401,
        "portal_session_expired",
        "Seu acesso expirou. Informe a senha novamente.",
      );
      emit(accessError(e));
      throw e;
    }
    const requestGeneration = generation;
    const headers = new Headers({ Accept: "application/json" });
    if (token) headers.set("X-Build-Portal-Session", token);
    if (init.body && !(init.body instanceof FormData))
      headers.set("Content-Type", "application/json");
    if (etag) headers.set("If-None-Match", etag);
    const response = await requestFetch(base + suffix, {
      ...init,
      headers,
      credentials: "omit",
      cache: "no-store",
      redirect: "error",
    });
    if (requestGeneration !== generation)
      throw new DOMException("Portal changed", "AbortError");
    if (response.status === 304) return response;
    if (!response.ok) {
      const body = (await response
        .json()
        .catch(() => ({}))) as Partial<D.PublicBuildErrorDto>;
      const retry = response.headers.get("Retry-After");
      const retryTime =
        retry && Number.isFinite(Number(retry))
          ? Date.now() + Number(retry) * 1000
          : retry
            ? Date.parse(retry)
            : Date.now() + (body.retryAfterSeconds ?? 60) * 1000;
      if (response.status === 429)
        blockedUntil = Number.isFinite(retryTime)
          ? retryTime
          : Date.now() + 60000;
      const e = new PortalHttpError(
        response.status,
        body.code ?? "portal_error",
        body.error || "Não foi possível concluir esta solicitação.",
        response.status === 429 ? blockedUntil : undefined,
        body.portal,
      );
      if (response.status === 401) {
        token = "";
        expiresAt = 0;
        etags.clear();
        resources = {};
        bootstrap = undefined;
        emit(accessError(e));
      }
      if (response.status === 404 && body.code === "portal_not_found") {
        reset();
        emit({ kind: "not-found" });
      }
      throw e;
    }
    return response;
  };
  async function get<T>(
    base: string,
    suffix: string,
    signal?: AbortSignal,
  ): Promise<T> {
    return (await raw(base, suffix, { signal })).json() as Promise<T>;
  }
  async function write<T>(
    base: string,
    suffix: string,
    body: unknown,
  ): Promise<T> {
    return (
      await raw(base, suffix, {
        method: "POST",
        body: body instanceof FormData ? body : JSON.stringify(body),
      })
    ).json() as Promise<T>;
  }
  const load: PortalGateway["load"] = async (tenant, project, signal) => {
    const base = path(tenant, project);
    try {
      const b = await get<D.PublicBuildBootstrapDto>(base, "", signal);
      if (b.contractVersion.split(".")[0] !== "1" || !Array.isArray(b.sections))
        throw Error("Unsupported portal contract");
      const next: PortalResources = {};
      const failures: typeof errors = [];
      const tasks: [keyof PortalResources, string, string][] = [
        ["progress", "overallProgress", "/progress"],
        ["photos", "photos", "/photos?take=24"],
        ["orders", "changeOrders", "/change-orders"],
        ["selections", "selections", "/selections"],
        ["measurements", "measurements", "/measurements"],
        ["handover", "postConstruction", "/handover"],
        ["warranty", "postConstruction", "/warranty"],
        ["assistance", "postConstruction", "/assistance"],
        ["twin", "model3d", "/twin"],
        ["captures", "realityCapture", "/reality-captures?take=24"],
        ["documents", "documents", "/documents"],
      ];
      if (
        ["clientUpdates", "dailyLogs", "stageProgress", "photos"].some((s) =>
          b.sections.includes(s),
        )
      )
        tasks.push(["timeline", "timeline", "/timeline?take=20"]);
      await Promise.all(
        tasks
          .filter(
            ([, section]) =>
              section === "timeline" || b.sections.includes(section),
          )
          .map(async ([name, section, url]) => {
            try {
              Object.assign(next, { [name]: await get(base, url, signal) });
            } catch (e) {
              if (
                signal.aborted ||
                (e instanceof PortalHttpError &&
                  (e.status === 401 || e.code === "portal_not_found"))
              )
                throw e;
              if (
                e instanceof PortalHttpError &&
                e.status === 404 &&
                e.code === "portal_item_not_found" &&
                name === "handover"
              )
                return;
              failures.push({ section, message: actionError(e) });
            }
          }),
      );
      if (signal.aborted) throw new DOMException("Aborted", "AbortError");
      bootstrap = b;
      resources = next;
      errors = failures;
      return { kind: "ready", portal: adaptPortal(b, next, failures) };
    } catch (e) {
      if (signal.aborted) throw e;
      return accessError(e);
    }
  };
  const notifyMutation = async (work: () => Promise<unknown>) => {
    try {
      await work();
      emit("changed");
    } catch (e) {
      if (e instanceof PortalHttpError && e.status === 409) emit("changed");
      throw e;
    }
  };
  const validateFiles = (files: File[]) => {
    if (files.length > 6)
      throw new PortalHttpError(
        422,
        "assistance_photo_limit",
        "Envie no máximo 6 fotos por chamado.",
      );
    for (const file of files) {
      if (file.size > 8 * 1024 * 1024)
        throw new PortalHttpError(
          413,
          "file_too_large",
          "Cada foto deve ter até 8 MB.",
        );
      if (!["image/jpeg", "image/png", "image/webp"].includes(file.type))
        throw new PortalHttpError(
          422,
          "invalid_file_type",
          "Envie fotos JPEG, PNG ou WebP.",
        );
    }
  };
  const actions = (tenant: string, project: string): PortalActions => {
    const base = path(tenant, project);
    const upload = async (id: string, files: File[]) => {
      validateFiles(files);
      for (const file of files) {
        const form = new FormData();
        form.append("file", file);
        await write(base, `/assistance/${encodeURIComponent(id)}/photos`, form);
      }
    };
    return {
      decide: (id, option) =>
        notifyMutation(() =>
          write<D.PublicBuildSelectionDto>(
            base,
            `/selections/${encodeURIComponent(id)}/choice`,
            { optionId: option } satisfies D.PublicBuildSelectionChoiceRequest,
          ),
        ),
      changeOrder: (id, decision, signerName, comment) =>
        notifyMutation(() =>
          write(base, `/change-orders/${encodeURIComponent(id)}/decision`, {
            decision,
            signerName,
            comment,
          } satisfies D.PublicBuildChangeOrderDecisionRequest),
        ),
      acceptHandover: (signerName, comment) =>
        notifyMutation(() =>
          write(base, "/handover/accept", {
            signerName,
            comment,
          } satisfies D.PublicBuildHandoverAcceptRequest),
        ),
      requestAssistance: (input) =>
        notifyMutation(async () => {
          validateFiles(input.files);
          const ticket = await write<D.PublicBuildAssistanceDto>(
            base,
            "/assistance",
            {
              category: input.category,
              title: input.title,
              description: input.description,
              location: input.location,
            } satisfies D.PublicBuildAssistanceOpenRequest,
          );
          try {
            await upload(ticket.id, input.files);
          } catch (e) {
            emit("changed");
            throw new PortalHttpError(
              e instanceof PortalHttpError ? e.status : 503,
              "partial_upload",
              `Chamado ${ticket.number} criado. Não abra outro: envie as fotos pelo chamado. ${actionError(e)}`,
            );
          }
        }),
      uploadEvidence: (id, files) =>
        notifyMutation(async () => {
          try {
            await upload(id, files);
          } catch (e) {
            emit("changed");
            throw e;
          }
        }),
      feedback: (id, rating, comment) =>
        notifyMutation(() =>
          write(base, `/assistance/${encodeURIComponent(id)}/feedback`, {
            rating,
            comment,
          } satisfies D.PublicBuildAssistanceFeedbackRequest),
        ),
      twinState: async (date, signal) => {
        const cached = etags.get(date);
        const response = await raw(
          base,
          `/twin/state?date=${encodeURIComponent(date)}`,
          { signal },
          cached?.etag,
        );
        if (response.status === 304 && cached) return cached.snapshot;
        const snapshot = adaptState(
          (await response.json()) as D.PublicBuildTwinStateDto,
        );
        const etag = response.headers.get("ETag");
        if (etag) etags.set(date, { etag, snapshot });
        return snapshot;
      },
      more: async (section) => {
        if (!bootstrap) return;
        if (section === "timeline" && resources.timeline?.nextBefore) {
          const next = await get<D.PublicBuildTimelineDto>(
            base,
            `/timeline?take=20&before=${encodeURIComponent(resources.timeline.nextBefore)}`,
          );
          resources.timeline = {
            ...next,
            items: [...resources.timeline.items, ...next.items],
          };
        }
        if (section === "photos" && resources.photos) {
          const next = await get<D.PublicBuildPhotosDto>(
            base,
            `/photos?take=24&skip=${resources.photos.items.length}`,
          );
          resources.photos = {
            ...next,
            items: [...resources.photos.items, ...next.items],
          };
        }
        if (section === "captures" && resources.captures) {
          const next = await get<D.PublicBuildRealityCapturesDto>(
            base,
            `/reality-captures?take=24&skip=${resources.captures.items.length}`,
          );
          resources.captures = {
            ...next,
            items: [...resources.captures.items, ...next.items],
          };
        }
        emit({
          kind: "ready",
          portal: adaptPortal(bootstrap, resources, errors),
        });
      },
      refresh: async () => {
        emit("changed");
      },
      logout: async () => {
        try {
          await raw(base, "/session", { method: "DELETE" });
        } finally {
          reset();
          emit({ kind: "private" });
        }
      },
    };
  };
  return {
    load,
    actions,
    unlock: async (tenant, project, password, signal) => {
      const base = path(tenant, project);
      token = "";
      expiresAt = 0;
      try {
        const session = (await (
          await raw(base, "/session", {
            method: "POST",
            body: JSON.stringify({
              password,
            } satisfies D.CreatePublicBuildSessionRequest),
            signal,
          })
        ).json()) as D.PublicBuildSessionDto;
        if (signal.aborted) throw new DOMException("Aborted", "AbortError");
        token = session.token;
        expiresAt = Date.parse(session.expiresAt);
        return load(tenant, project, signal);
      } catch (e) {
        if (signal.aborted) throw e;
        return accessError(e);
      }
    },
    subscribe: (listener) => {
      listeners.add(listener);
      return () => {
        listeners.delete(listener);
      };
    },
    dispose: () => {
      reset();
      listeners.clear();
    },
  };
}
