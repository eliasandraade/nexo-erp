import type { PortalActions } from "./api/gateway";
import { lazy, Suspense, useState, type CSSProperties } from "react";
import { type Portal, type Photo, date, percent, safeUrl } from "./types";
import {
  Dialog,
  DialogContent,
  DialogTitle,
  DialogDescription,
} from "@/components/ui/dialog";
const Panorama = lazy(() => import("./Panorama"));
const TwinViewer = lazy(() => import("./TwinViewer"));
const labels = {
  waiting: "Próximo",
  active: "Em andamento",
  complete: "Concluído",
  delayed: "Atenção ao prazo",
  blocked: "Aguardando liberação",
};
const money = (value: number | null) =>
  value === null
    ? "Não informado"
    : value.toLocaleString("pt-BR", { style: "currency", currency: "BRL" });
export function PortalView({
  portal: p,
  actions = {},
}: {
  portal: Portal;
  actions?: PortalActions;
}) {
  const [compare, setCompare] = useState("");
  const [photoDate, setPhotoDate] = useState("");
  const [zone, setZone] = useState("");
  const [visit, setVisit] = useState(false);
  const [photo, setPhoto] = useState<Photo>();
  const [filter, setFilter] = useState("");
  const [viewer, setViewer] = useState(false);
  const [decision, setDecision] = useState<{ id: string; option: string }>();
  const [busy, setBusy] = useState(false);
  const [notice, setNotice] = useState("");
  const visiblePhotos = p.photos.filter(item => (!filter || item.stage === filter) && (!photoDate || item.date.startsWith(photoDate)) && (!zone || item.zone === zone));
  const chosen = p.decisions.find((item) => item.id === decision?.id);
  const option = chosen?.options.find((item) => item.id === decision?.option);
  const accent = /^#[0-9a-f]{6}$/i.test(p.color ?? "") ? p.color : "#34594d";
  return (
    <div
      className="my-build"
      style={{ "--build-brand": accent, "--build-secondary": /^#[0-9a-f]{6}$/i.test(p.secondaryColor??'')?p.secondaryColor:accent } as CSSProperties}
    >
      <a className="skip-link" href="#my-build-main">
        Pular para a obra
      </a>
      <header className="mb-header">
        <div>
          {safeUrl(p.logo) && <img src={safeUrl(p.logo)} alt="" />}
          <span>{p.company}</span>
        </div>
        <span>{p.lifecycle === "handover" ? "Meu Imóvel" : "Minha Obra"}</span>
      </header>
      <nav className="mb-nav" aria-label="Sua obra">
        <a href="#overview">Visão geral</a>
        <a href="#stages">Etapas</a>
        <a href="#updates">Atualizações</a>
        <a href="#photos">Fotos</a>
        {p.documents.length > 0 && <a href="#documents">Documentos</a>}
        {p.finance && <a href="#finance">Financeiro</a>}
      </nav>
      <main id="my-build-main">
        <section className="mb-hero" id="overview">
          <div className="mb-hero-copy">
            <p className="mb-eyebrow">
              {p.lifecycle === "building"
                ? "Acompanhe cada avanço"
                : "Uma nova etapa começa"}
            </p>
            <h1>{p.name}</h1>
            <p className="mb-lead">
              {p.lifecycle === "building"
                ? "Sua obra, mais perto de você."
                : p.lifecycle === "completed"
                  ? "Obra concluída."
                  : "Bem-vindo ao seu imóvel."}
            </p>
            <div className="mb-progress">
              <span>{percent(p.progress)}</span>
              <p>da obra concluída</p>
              {percent(p.progress) !== "Não informado" && (
                <progress
                  aria-label="Progresso da obra"
                  value={p.progress!}
                  max={100}
                />
              )}
            </div>
            <dl className="mb-facts">
              <div>
                <dt>Previsão atual de entrega</dt>
                <dd>{date(p.delivery)}</dd>
              </div>
              <div>
                <dt>Última atualização</dt>
                <dd>{date(p.updatedAt)}</dd>
              </div>
            </dl>
          </div>
          {safeUrl(p.cover) ? (
            <figure>
              <img
                src={safeUrl(p.cover)}
                alt={`Vista da obra ${p.name}`}
                fetchPriority="high"
              />
              <figcaption>Imagem publicada pela {p.company}</figcaption>
            </figure>
          ) : (
            <div className="mb-no-cover">
              <span>O próximo capítulo está em construção.</span>
              <p>As imagens da obra aparecerão aqui quando forem publicadas.</p>
            </div>
          )}
        </section>
        {p.weekly && (
          <section className="mb-week">
            <p className="mb-eyebrow">Esta semana na sua obra</p>
            <p>{p.weekly.body}</p>
            <small>
              Publicado pela {p.company} em {date(p.weekly.publishedAt)}
            </small>
          </section>
        )}
        {p.forecast && (
          <section className="mb-section">
            <h2>Previsão da obra</h2>
            <p>{p.forecast}</p>
            {p.planned !== null && (
              <p>
                Planejado: {percent(p.planned)} · Realizado:{" "}
                {percent(p.progress)}
              </p>
            )}
            <small>
              Previsão informada pela construtora, sujeita a atualização.
            </small>
          </section>
        )}
        <section id="stages" className="mb-section">
          <p className="mb-eyebrow">Do primeiro passo à entrega</p>
          <h2>O caminho da sua obra</h2>
          {!p.stages.length && (
            <p>
              O cronograma será exibido quando a construtora publicar as etapas.
            </p>
          )}
          <ol className="mb-stages">
            {p.stages.map((stage, index) => (
              <li key={stage.id}>
                <span className="mb-step">
                  {stage.status === "complete"
                    ? "✓"
                    : String(index + 1).padStart(2, "0")}
                </span>
                <div>
                  <span className="mb-eyebrow">{labels[stage.status]}</span>
                  <h3>{stage.name}</h3>
                  {stage.date && <small>{date(stage.date)}</small>}
                </div>
                <strong>{percent(stage.progress)}</strong>
              </li>
            ))}
          </ol>
        </section>
        {p.model && (
          <section className="mb-section">
            <p className="mb-eyebrow">Outro ponto de vista</p>
            <h2>Veja a construção acontecer</h2>
            {p.model.status === "ready" ? (
              <>
                <p>
                  Explore o projeto e os registros de execução publicados pela
                  construtora.
                </p>
                {!viewer ? (
                  <button onClick={() => setViewer(true)}>
                    Explorar a obra em 3D
                  </button>
                ) : (
                  <Suspense
                    fallback={<p role="status">Preparando a visualização…</p>}
                  >
                    <TwinViewer model={p.model} />
                  </Suspense>
                )}
              </>
            ) : (
              <p>
                {p.model.status === "processing"
                  ? "A visualização está sendo preparada. Enquanto isso, acompanhe as etapas e fotos."
                  : "O modelo não está disponível. As etapas e fotos continuam disponíveis abaixo."}
              </p>
            )}
          </section>
        )}
        {!!p.captures?.length && (
          <section className="mb-section">
            <h2>Visitar minha obra</h2>
            {!visit ? (
              <button onClick={() => setVisit(true)}>Abrir visita 360°</button>
            ) : (
              <Suspense fallback={<p role="status">Abrindo visita…</p>}>
                <Panorama captures={p.captures} />
              </Suspense>
            )}
          </section>
        )}
        {!!p.weather?.length && (
          <section className="mb-section">
            <h2>Clima na obra</h2>
            {p.weather.map((day) => (
              <article key={day.date}>
                <time>{date(day.date)}</time>
                <p>{day.description}</p>
                {day.impact && <p>{day.impact}</p>}
              </article>
            ))}
          </section>
        )}
        <section id="updates" className="mb-section">
          <h2>Histórias de uma obra em movimento</h2>
          {!p.updates.length && (
            <p>A primeira atualização ainda não foi publicada.</p>
          )}
          <ol className="mb-updates">
            {p.updates.map((update) => (
              <li key={update.id}>
                <time>{date(update.publishedAt)}</time>
                <article id={"update-"+update.id}>
                  <p className="mb-eyebrow">
                    {update.planned ? "Previsto" : "Publicado pela construtora"}
                  </p>
                  <h3>{update.title}</h3>
                  <p>{update.body}</p>
                </article>
              </li>
            ))}
          </ol>
        </section>
        <section id="photos" className="mb-section">
          <h2>Como está ficando</h2>
          {!p.photos.length ? (
            <p>
              Assim que a construtora publicar fotos, você poderá acompanhar
              cada detalhe aqui.
            </p>
          ) : (
            <>
              <label>
                Filtrar por etapa
                <select
                  value={filter}
                  onChange={(e) => setFilter(e.target.value)}
                >
                  <option value="">Todas as etapas</option>
                  {[
                    ...new Set(
                      p.photos.map((item) => item.stage).filter(Boolean),
                    ),
                  ].map((stage) => (
                    <option key={stage}>{stage}</option>
                  ))}
                </select>
              </label>
              <label>
                Data das imagens
                <input
                  type="date"
                  value={photoDate}
                  onChange={(e) => setPhotoDate(e.target.value)}
                />
              </label>
              <label>
                Ambiente
                <select value={zone} onChange={(e) => setZone(e.target.value)}>
                  <option value="">Todos os ambientes</option>
                  {[
                    ...new Set(
                      p.photos.map((item) => item.zone).filter(Boolean),
                    ),
                  ].map((value) => (
                    <option key={value}>{value}</option>
                  ))}
                </select>
              </label>
              {visiblePhotos.length===0&&<p role="status">Nenhuma imagem nesta seleção. Altere a data, etapa ou ambiente.</p>}<div className="mb-gallery">
                {p.photos
                  .filter(
                    (item) =>
                      (!filter || item.stage === filter) &&
                      (!photoDate || item.date.startsWith(photoDate)) &&
                      (!zone || item.zone === zone),
                  )
                  .map((item) => (
                    <button
                      key={item.id}
                      onClick={() => {
                        setCompare("");
                        setPhoto(item);
                      }}
                    >
                      {item.kind === "video" && !item.thumbnail ? (
                        <span>▶ Vídeo da obra</span>
                      ) : (
                        <img
                          loading="lazy"
                          src={safeUrl(item.thumbnail) || safeUrl(item.url)}
                          alt={item.caption}
                        />
                      )}
                      <span>{item.caption}</span>
                      <small>{date(item.date)}</small>
                    </button>
                  ))}
              </div>
            </>
          )}
        </section>
        {!!p.decisions.length && (
          <section className="mb-section">
            <h2>Sua escolha faz parte</h2>
            {p.decisions.map((item) => (
              <article className="mb-decision" key={item.id}>
                <p className="mb-eyebrow">
                  {item.status === "confirmed"
                    ? "Escolha registrada"
                    : `Sua decisão até ${date(item.due)}`}
                </p>
                <h3>{item.title}</h3>
                {item.status === "confirmed" ? (
                  <p>
                    {item.options.find((o) => o.id === item.selected)?.name ??
                      "Escolha confirmada pela construtora"}
                  </p>
                ) : (
                  <>
                    {item.options.map((o) => (
                      <div className="mb-option" key={o.id}>
                        <div>
                          <h4>{o.name}</h4>
                          <p>{o.description}</p>
                          <p>
                            {o.extra === 0
                              ? "Incluso"
                              : `Valor adicional: ${money(o.extra)}`}{" "}
                            ·{" "}
                            {o.days === null
                              ? "Prazo a confirmar"
                              : o.days === 0
                                ? "Sem alteração de prazo"
                                : `${o.days} dias adicionais`}
                          </p>
                        </div>
                        {actions.decide && (
                          <button
                            onClick={() => {
                              setNotice("");
                              setDecision({ id: item.id, option: o.id });
                            }}
                          >
                            Escolher {o.name}
                          </button>
                        )}
                      </div>
                    ))}
                    {!actions.decide && (
                      <p>
                        A confirmação online ainda não está disponível. Consulte
                        a construtora.
                      </p>
                    )}
                  </>
                )}
              </article>
            ))}
          </section>
        )}
        {p.finance && (
          <section id="finance" className="mb-section">
            <h2>Investimento na sua obra</h2>
            <dl className="mb-facts">
              <div>
                <dt>Orçamento</dt>
                <dd>{money(p.finance.budget)}</dd>
              </div>
              <div>
                <dt>Executado</dt>
                <dd>{money(p.finance.executed)}</dd>
              </div>
            </dl>
            {p.finance.level !== "summary" &&
              p.finance.categories.map((c) => (
                <p key={c.name}>
                  {c.name} <strong>{money(c.amount)}</strong>
                </p>
              ))}
            {p.finance.level === "full" &&
              p.finance.transactions.map((t) => (
                <article className="mb-option" key={t.id}>
                  <div>
                    <time>{date(t.date)}</time>
                    <h3>{t.description}</h3>
                  </div>
                  <strong>{money(t.amount)}</strong>
                  {safeUrl(t.receipt) && (
                    <a
                      href={safeUrl(t.receipt)}
                      target="_blank"
                      rel="noreferrer"
                    >
                      Comprovante
                    </a>
                  )}
                </article>
              ))}
          </section>
        )}
        {!!p.documents.length && (
          <section id="documents" className="mb-section">
            <h2>Tudo no seu lugar</h2>
            {p.documents.map((doc) => (
              <article className="mb-option" key={doc.id}>
                <div>
                  <small>{doc.category}</small>
                  <h3>{doc.name}</h3>
                </div>
                {safeUrl(doc.url) && (
                  <a href={safeUrl(doc.url)} target="_blank" rel="noreferrer">
                    Abrir documento ↗
                  </a>
                )}
              </article>
            ))}
          </section>
        )}
        {p.lifecycle === "handover" && (
          <section className="mb-section">
            <h2>Cuidado que continua</h2>
            {p.warranties.map((w) => (
              <p key={w.id}>
                {w.name} · Garantia até {date(w.until)}
              </p>
            ))}
            {p.assistance && (
              <>
                <h3>Assistência</h3>
                {p.assistance.tickets.map((t) => (
                  <p key={t.id}>
                    {t.title} · {t.status}
                  </p>
                ))}
                {actions.requestAssistance && (
                  <form
                    onSubmit={async (e) => {
                      e.preventDefault();
                      const form = e.currentTarget;
                      const data = new FormData(form);
                      setBusy(true);
                      setNotice("");
                      try {
                        await actions.requestAssistance!({
                          category: String(data.get("category")),
                          description: String(data.get("description")),
                          availability: String(data.get("availability")),
                          files: data
                            .getAll("files")
                            .filter(
                              (file): file is File =>
                                file instanceof File && file.size > 0,
                            ),
                        });
                        setNotice(
                          "Solicitação recebida. A construtora dará continuidade ao atendimento.",
                        );
                        form.reset();
                      } catch {
                        setNotice(
                          "Não foi possível enviar. Seus dados foram mantidos para tentar novamente.",
                        );
                      } finally {
                        setBusy(false);
                      }
                    }}
                  >
                    <label>
                      Categoria
                      <select name="category" required>
                        {p.assistance.categories.map((c) => (
                          <option key={c}>{c}</option>
                        ))}
                      </select>
                    </label>
                    <label>
                      Como podemos ajudar?
                      <textarea name="description" required maxLength={4000} />
                    </label>
                    <label>
                      Disponibilidade
                      <input name="availability" required />
                    </label>
                    <label>
                      Fotos ou vídeos
                      <input
                        name="files"
                        type="file"
                        multiple
                        accept="image/*,video/*"
                      />
                    </label>
                    <button disabled={busy}>
                      {busy ? "Enviando…" : "Solicitar atendimento"}
                    </button>
                  </form>
                )}
              </>
            )}
          </section>
        )}
        {notice && (
          <p role="status" className="mb-section">
            {notice}
          </p>
        )}
      </main>
      <footer className="mb-footer">
        <span>{p.company}</span>
        {safeUrl(p.contact) && (
          <a href={safeUrl(p.contact)}>Falar com a construtora</a>
        )}
        <small>Powered by Orken</small>
      </footer>
      <Dialog
        open={!!photo}
        onOpenChange={(open) => {
          if (!open) setPhoto(undefined);
        }}
      >
        <DialogContent className="mb-dialog max-w-5xl">
          <DialogTitle>{photo?.caption}</DialogTitle>
          <DialogDescription>{date(photo?.date ?? null)}</DialogDescription>
          {photo && (
            <>
              {photo.kind === "video" ? (
                <video
                  controls
                  preload="metadata"
                  className="max-h-[65dvh] w-full"
                  src={safeUrl(photo.url)}
                  aria-label={photo.caption}
                />
              ) : (
                <>
                  <label>
                    Comparar com
                    <select
                      value={compare}
                      onChange={(e) => setCompare(e.target.value)}
                    >
                      <option value="">Sem comparação</option>
                      {p.photos
                        .filter(
                          (item) =>
                            item.id !== photo.id && item.kind !== "video",
                        )
                        .map((item) => (
                          <option key={item.id} value={item.id}>
                            {date(item.date)} · {item.caption}
                          </option>
                        ))}
                    </select>
                  </label>
                  <div
                    className={
                      compare ? "grid grid-cols-1 gap-4 sm:grid-cols-2" : ""
                    }
                  >
                    <img
                      className="max-h-[65dvh] w-full object-contain"
                      src={safeUrl(photo.url)}
                      alt={photo.caption}
                    />
                    {compare && (
                      <img
                        className="max-h-[65dvh] w-full object-contain"
                        src={safeUrl(
                          p.photos.find((item) => item.id === compare)?.url,
                        )}
                        alt={
                          p.photos.find((item) => item.id === compare)?.caption
                        }
                      />
                    )}
                  </div>
                </>
              )}
            </>
          )}
        </DialogContent>
      </Dialog>
      <Dialog
        open={!!decision}
        onOpenChange={(open) => {
          if (!open && !busy) setDecision(undefined);
        }}
      >
        <DialogContent className="mb-dialog">
          <DialogTitle>Confirmar sua escolha</DialogTitle>
          <DialogDescription>
            Confira o valor e o prazo antes de confirmar. A escolha será enviada
            à construtora.
          </DialogDescription>
          <h3>{option?.name}</h3>
          <p>Adicional: {money(option?.extra ?? null)}</p>
          <p>
            Prazo:{" "}
            {option?.days === null
              ? "A confirmar"
              : `${option?.days} dias adicionais`}
          </p>
          <button className="mb-confirm"
            disabled={busy}
            onClick={async () => {
              if (!chosen || !option || !actions.decide) return;
              setBusy(true);
              try {
                await actions.decide(chosen.id, option.id, chosen.version);
                setDecision(undefined);
                setNotice(
                  "Escolha enviada. Aguarde a atualização do registro pela construtora.",
                );
              } catch {
                setNotice("Não foi possível confirmar. Tente novamente.");
              } finally {
                setBusy(false);
              }
            }}
          >
            {busy ? "Confirmando…" : "Confirmar escolha e impactos"}
          </button>
          {notice && <p role="alert">{notice}</p>}
        </DialogContent>
      </Dialog>
    </div>
  );
}
