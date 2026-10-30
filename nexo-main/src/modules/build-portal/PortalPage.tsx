import { useCallback, useEffect, useRef, useState } from "react";
import { useParams } from "react-router-dom";
import { type Access, safeUrl } from "./types";
import { type PortalGateway, unavailableGateway } from "./api/gateway";
import { PortalView } from "./PortalView";
import "./portal.css";

const messages: Record<Exclude<Access["kind"], "ready">, string> = {
  private: "Acompanhe a evolução da sua obra.",
  expired: "Seu acesso expirou. Informe a senha novamente.",
  "wrong-password": "Não foi possível acessar com essa senha.",
  "rate-limited": "Muitas tentativas. Aguarde antes de tentar novamente.",
  unpublished: "Este acompanhamento ainda não está publicado.",
  "not-found": "Não encontramos este acompanhamento.",
  unavailable: "O acompanhamento está indisponível neste momento.",
};
export default function PortalPage({
  gateway = unavailableGateway,
}: {
  gateway?: PortalGateway;
}) {
  const { companySlug = "", projectSlug = "" } = useParams();
  // Key destroys all state synchronously when tenant/project changes.
  return (
    <PortalSession
      key={`${companySlug}/${projectSlug}`}
      tenant={companySlug}
      project={projectSlug}
      gateway={gateway}
    />
  );
}
export function PortalSession({
  tenant,
  project,
  gateway,
}: {
  tenant: string;
  project: string;
  gateway: PortalGateway;
}) {
  const [access, setAccess] = useState<Access>();
  const [busy, setBusy] = useState(false);
  const [password, setPassword] = useState("");
  const request = useRef<AbortController>();
  const sequence = useRef(0);
  useEffect(() => {
    const originalTitle = document.title;
    let robots = document.querySelector<HTMLMetaElement>('meta[name="robots"]');
    const previous = robots?.content;
    const created = !robots;
    if (!robots) {
      robots = document.createElement("meta");
      robots.name = "robots";
      document.head.append(robots);
    }
    robots.content = "noindex, nofollow, noarchive";
    document.title = "Minha Obra";
    return () => {
      request.current?.abort();
      document.title = originalTitle;
      if (created) robots.remove();
      else robots.content = previous ?? "";
    };
  }, []);
  const run = useCallback(
    async (secret?: string) => {
      request.current?.abort();
      const controller = new AbortController();
      request.current = controller;
      const id = ++sequence.current;
      setBusy(true);
      setAccess(undefined);
      try {
        const result =
          secret === undefined
            ? await gateway.load(tenant, project, controller.signal)
            : await gateway.unlock(tenant, project, secret, controller.signal);
        if (!controller.signal.aborted && id === sequence.current)
          setAccess(result);
      } catch {
        if (!controller.signal.aborted && id === sequence.current)
          setAccess({ kind: "unavailable" });
      } finally {
        if (!controller.signal.aborted && id === sequence.current) {
          setBusy(false);
          setPassword("");
        }
      }
    },
    [gateway, tenant, project],
  );
  useEffect(() => {
    void run();
    return () => request.current?.abort();
  }, [run]); // Session key isolates projects.
  useEffect(() => {
    if (access?.kind !== "ready") {document.title='Minha Obra';const robots=document.querySelector<HTMLMetaElement>('meta[name="robots"]');if(robots)robots.content='noindex, nofollow, noarchive';return;}
    document.title = access.portal.name + " · " + access.portal.company;
    const robots = document.querySelector<HTMLMetaElement>(
      'meta[name="robots"]',
    );
    if (robots)
      robots.content =
        access.portal.visibility === "public" &&
        access.portal.indexable === true
          ? "index, follow"
          : "noindex, nofollow, noarchive";
  }, [access]);
  if (access?.kind === "ready")
    return (
      <PortalView
        portal={access.portal}
        actions={gateway.actions?.(tenant, project)}
      />
    );
  const locked =
    access && ["private", "expired", "wrong-password"].includes(access.kind);
  return (
    <main className="my-build mb-access">
      <div>
        {access?.publicIdentity && <div>{safeUrl(access.publicIdentity.logo)&&<img width="64" height="64" src={safeUrl(access.publicIdentity.logo)} alt=""/>}<p>{access.publicIdentity.company}</p></div>}
        <p className="mb-eyebrow">Minha Obra</p>
        <h1>{access?.publicIdentity?.name || 'Mais perto de cada conquista.'}</h1>
        {!access ? (
          <p role="status">Abrindo seu acompanhamento…</p>
        ) : (
          <p role="status">{messages[access.kind]}</p>
        )}
        {locked && (
          <form
            onSubmit={(event) => {
              event.preventDefault();
              if (password.trim()) void run(password);
            }}
          >
            <label htmlFor="portal-password">Senha de acesso</label>
            <input
              id="portal-password"
              autoComplete="current-password"
              type="password"
              required
              value={password}
              onChange={(event) => setPassword(event.target.value)}
            />
            <button disabled={busy || !password.trim()}>
              Acessar minha obra
            </button>
          </form>
        )}
        {(access?.kind === "unavailable" ||
          access?.kind === "rate-limited") && (
          <button onClick={() => void run()} disabled={busy}>
            Tentar novamente
          </button>
        )}
        <small>Powered by Orken</small>
      </div>
    </main>
  );
}
