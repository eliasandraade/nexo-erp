import { useState, type FormEvent } from "react";
import type { PortalActions, UploadProgress } from "./api/gateway";
import type { SignedMedia } from "./media";
import { actionError, PortalHttpError } from "./api/http-gateway";
import { date, percent, safeUrl, type Portal } from "./types";
const status: Record<string, string> = {
  Pending: "Aguardando decisão",
  Approved: "Aprovado",
  Rejected: "Recusado",
  Open: "Aberto",
  Triaged: "Em análise",
  Scheduled: "Agendado",
  InProgress: "Em atendimento",
  Resolved: "Resolvido",
  Closed: "Encerrado",
  Delivered: "Entregue",
  InspectionDone: "Vistoria realizada",
  Passed: "Aprovada",
  PassedWithRemarks: "Aprovada com pendências",
  Failed: "Com pendências",
  Planned: "Agendada",
  Ok: "Conforme",
  NotOk: "Pendente",
  NotApplicable: "Não se aplica",
};
const label = (s: string) => status[s] ?? "Estado informado pela construtora";
const amount = (n: number) =>
  n.toLocaleString("pt-BR", { style: "currency", currency: "BRL" });
export function ProgressDetails({ portal: p }: { portal: Portal }) {
  const series = p.curves
    ? [
        { name: "Realizado", data: p.curves.actual, color: "#34594d" },
        { name: "Planejado", data: p.curves.planned, color: "#687483" },
        { name: "Previsão", data: p.curves.forecast, color: "#99651f" },
      ].filter((s) => s.data?.length)
    : [];
  const dates = series
    .flatMap((s) => s.data!.map((d) => Date.parse(d.date)))
    .filter(Number.isFinite);
  const min = Math.min(...dates),
    max = Math.max(...dates);
  return (
    <>
      {!!series.length && (
        <section className="mb-section">
          <h2>O avanço ao longo do tempo</h2>
          <p>Planejado, realizado e previsão publicada pela construtora.</p>
          <svg
            viewBox="0 0 600 180"
            className="mb-curves"
            role="img"
            aria-label="Curvas de progresso. Valores disponíveis na tabela abaixo."
          >
            {[0, 50, 100].map((v) => (
              <g key={v}>
                <line
                  x1="35"
                  x2="590"
                  y1={155 - v * 1.3}
                  y2={155 - v * 1.3}
                  stroke="#d6ddd8"
                />
                <text x="0" y={159 - v * 1.3} fontSize="11">
                  {v}%
                </text>
              </g>
            ))}
            {series.map((s) => (
              <polyline
                key={s.name}
                fill="none"
                stroke={s.color}
                strokeWidth="3"
                strokeDasharray={s.name === "Previsão" ? "5 4" : undefined}
                points={s
                  .data!.filter(
                    (d) =>
                      Number.isFinite(Date.parse(d.date)) &&
                      Number.isFinite(d.percent),
                  )
                  .map(
                    (d) =>
                      `${35 + ((Date.parse(d.date) - min) / Math.max(1, max - min)) * 555},${155 - d.percent * 1.3}`,
                  )
                  .join(" ")}
              />
            ))}
          </svg>
          <p>
            {series.map((s) => (
              <span key={s.name} style={{ color: s.color, marginRight: 20 }}>
                {s.name}
              </span>
            ))}
          </p>
          <details>
            <summary>Consultar valores por data</summary>
            <div className="mb-table-wrap">
              <table>
                <caption>Registros publicados de progresso</caption>
                <thead>
                  <tr>
                    <th>Curva</th>
                    <th>Data</th>
                    <th>Progresso</th>
                  </tr>
                </thead>
                <tbody>
                  {series.flatMap((s) =>
                    s.data!.map((d, i) => (
                      <tr key={s.name + i}>
                        <td>{s.name}</td>
                        <td>{date(d.date)}</td>
                        <td>{percent(d.percent)}</td>
                      </tr>
                    )),
                  )}
                </tbody>
              </table>
            </div>
          </details>
        </section>
      )}
      {p.schedule && (
        <section className="mb-section">
          <h2>O que vem pela frente</h2>
          <ol className="mb-stages">
            {p.schedule.map((s) => (
              <li key={s.order}>
                <div>
                  <h3>{s.name}</h3>
                  <p>
                    Início: {date(s.start)} · Fim planejado: {date(s.end)}
                  </p>
                </div>
              </li>
            ))}
          </ol>
          {!p.schedule.length && <p>O cronograma ainda não foi publicado.</p>}
        </section>
      )}
      {p.measurements && (
        <section className="mb-section">
          <h2>Avanços medidos</h2>
          {p.measurements.map((m) => (
            <article key={m.number}>
              <h3>Medição {m.number}</h3>
              <p>
                {date(m.start)} a {date(m.end)}
              </p>
              {m.items.map((i, n) => (
                <p key={n}>
                  {i.name}: {percent(i.previous)} → {percent(i.measured)}
                </p>
              ))}
              {m.amount !== null && <p>{amount(m.amount)}</p>}
            </article>
          ))}
          {!p.measurements.length && <p>Nenhuma medição publicada.</p>}
        </section>
      )}
    </>
  );
}
export function PortalOperations({
  portal: p,
  actions,
  media,
}: {
  portal: Portal;
  actions: PortalActions;
  media?: SignedMedia;
}) {
  // One action at a time; its outcome is shown next to the form that sent it, and only after the server answered.
  const [busy, setBusy] = useState(""),
    [progress, setProgress] = useState(""),
    [notice, setNotice] = useState<{
      form: string;
      text: string;
      error: boolean;
    }>();
  const submit = async (
    key: string,
    e: FormEvent<HTMLFormElement>,
    done: string,
    work: (data: FormData, onProgress: UploadProgress) => Promise<void>,
  ) => {
    e.preventDefault();
    if (busy) return;
    const form = e.currentTarget;
    const data = new FormData(form);
    setBusy(key);
    setProgress("");
    setNotice(undefined);
    try {
      await work(data, (sent, total) =>
        setProgress(
          sent < total ? `Enviando foto ${sent + 1} de ${total}…` : "",
        ),
      );
      setNotice({ form: key, text: done, error: false });
      form.reset();
    } catch (error) {
      if (error instanceof PortalHttpError && error.code === "partial_upload")
        form.reset();
      setNotice({ form: key, text: actionError(error), error: true });
    } finally {
      setBusy("");
      setProgress("");
    }
  };
  const outcome = (key: string) =>
    busy === key && progress ? (
      <p role="status">{progress}</p>
    ) : notice?.form === key ? (
      <p role={notice.error ? "alert" : "status"}>{notice.text}</p>
    ) : null;
  const text = (d: FormData, k: string) => String(d.get(k) ?? "");
  const files = (d: FormData) =>
    d.getAll("files").filter((f): f is File => f instanceof File && f.size > 0);
  const signer = (
    <>
      <label>
        <span>Seu nome completo</span>
        <input name="signer" required maxLength={200} autoComplete="name" />
      </label>
      <label>
        <span>Comentário (opcional)</span>
        <textarea name="comment" maxLength={2000} />
      </label>
    </>
  );
  return (
    <>
      {p.changeOrders && (
        <section className="mb-section">
          <h2>Ajustes combinados</h2>
          {!p.changeOrders.length && <p>Nenhum aditivo publicado.</p>}
          {p.changeOrders.map((o) => (
            <article className="mb-decision" key={o.id}>
              <p className="mb-eyebrow">
                Aditivo {o.number} · {label(o.status)}
              </p>
              <h3>{o.title}</h3>
              <p>{o.description}</p>
              <ul>
                {o.items.map((i, n) => (
                  <li key={n}>
                    {i.name}: {i.quantity} {i.unit}
                  </li>
                ))}
              </ul>
              {o.cost !== null && <p>Impacto no contrato: {amount(o.cost)}</p>}
              <p>
                Impacto no prazo:{" "}
                {o.days === 0 ? "Sem alteração" : `${o.days} dias`}
              </p>
              {o.canDecide && actions.changeOrder && (
                <form
                  onSubmit={(e) =>
                    void submit(
                      `order-${o.id}`,
                      e,
                      "Decisão registrada.",
                      (d) =>
                        actions.changeOrder!(
                          o.id,
                          text(d, "decision") as "approve" | "reject",
                          text(d, "signer"),
                          text(d, "comment"),
                        ),
                    )
                  }
                >
                  {signer}
                  <label>
                    <span>Decisão</span>
                    <select name="decision" required defaultValue="">
                      <option value="" disabled>
                        Selecione
                      </option>
                      <option value="approve">Aprovar aditivo</option>
                      <option value="reject">Recusar aditivo</option>
                    </select>
                  </label>
                  <label className="mb-check">
                    <input type="checkbox" required />
                    <span>Li os itens e os impactos informados e confirmo esta
                    decisão.</span>
                  </label>
                  <button type="submit" disabled={!!busy}>
                    {busy === `order-${o.id}`
                      ? "Registrando…"
                      : "Registrar decisão do aditivo"}
                  </button>
                </form>
              )}
              {outcome(`order-${o.id}`)}
            </article>
          ))}
        </section>
      )}
      {p.handover && (
        <section className="mb-section">
          <h2>A entrega do seu imóvel</h2>
          <p>
            {label(p.handover.status)} ·{" "}
            {date(p.handover.delivered ?? p.handover.scheduled ?? null)}
          </p>
          <p>
            {p.handover.keys
              ? "Chaves entregues"
              : "Entrega de chaves ainda não registrada"}
          </p>
          {p.handover.inspection && (
            <>
              <h3>Vistoria: {label(p.handover.inspection.status)}</h3>
              <ul>
                {p.handover.inspection.items.map((i, n) => (
                  <li key={n}>
                    {i.text}: {label(i.result)}
                  </li>
                ))}
              </ul>
            </>
          )}
          {p.handover.documents.map((d, i) => (
            <p key={i}>
              {safeUrl(d.url) ? (
                <a
                  href={safeUrl(d.url)}
                  target="_blank"
                  rel="noreferrer"
                  onClick={media?.guardLink}
                >
                  {d.name} ↗
                </a>
              ) : (
                `${d.name}: arquivo indisponível`
              )}
            </p>
          ))}
          {p.handover.accepted ? (
            <p>Aceite registrado em {date(p.handover.accepted)}.</p>
          ) : (
            p.handover.canAccept &&
            actions.acceptHandover && (
              <form
                onSubmit={(e) =>
                  void submit("handover", e, "Aceite registrado.", (d) =>
                    actions.acceptHandover!(
                      text(d, "signer"),
                      text(d, "comment"),
                    ),
                  )
                }
              >
                {signer}
                <label className="mb-check">
                  <input type="checkbox" required />
                  <span>Revisei a vistoria e confirmo o aceite da entrega.</span>
                </label>
                <button type="submit" disabled={!!busy}>
                  {busy === "handover"
                    ? "Registrando…"
                    : "Registrar aceite da entrega"}
                </button>
              </form>
            )
          )}
          {outcome("handover")}
        </section>
      )}
      {p.sections?.includes("postConstruction") && (
        <section className="mb-section">
          <h2>Cuidado que continua</h2>
          {p.warranties.map((w) => (
            <p key={w.id}>
              {w.name} · Garantia até {date(w.until)}
            </p>
          ))}
          {p.warrantyNotice && <p>{p.warrantyNotice}</p>}
          {p.assistance && (
            <>
              <h3>Assistência</h3>
              {p.assistance.tickets.map((t) => (
                <article className="mb-decision" key={t.id}>
                  <h4>
                    Chamado {t.number}: {t.title}
                  </h4>
                  <p>{label(t.status)}</p>
                  <p>{t.description}</p>
                  {t.scheduled && <p>Atendimento: {date(t.scheduled)}</p>}
                  {t.resolution && <p>{t.resolution}</p>}
                  {t.timeline && (
                    <details>
                      <summary>Histórico do atendimento</summary>
                      {t.timeline.map((s, i) => (
                        <p key={i}>
                          {date(s.at)} · {label(s.status)}
                        </p>
                      ))}
                    </details>
                  )}
                  <div className="mb-gallery">
                    {t.photos?.map((url, i) => (
                      <a
                        key={i}
                        href={safeUrl(url)}
                        target="_blank"
                        rel="noreferrer"
                      >
                        <img
                          src={safeUrl(url)}
                          alt={`Evidência ${i + 1} do chamado ${t.number}`}
                          loading="lazy"
                          onError={media?.onError}
                        />
                      </a>
                    ))}
                  </div>
                  {t.canAddPhotos && actions.uploadEvidence && (
                    <form
                      onSubmit={(e) =>
                        void submit(
                          `photos-${t.id}`,
                          e,
                          "Fotos recebidas pela construtora.",
                          (d, onProgress) =>
                            actions.uploadEvidence!(t.id, files(d), onProgress),
                        )
                      }
                    >
                      <label>
                        <span>Adicionar fotos ao chamado</span>
                        <input
                          type="file"
                          name="files"
                          accept="image/jpeg,image/png,image/webp"
                          multiple
                          required
                        />
                      </label>
                      <p>
                        JPEG, PNG ou WebP. Até 8 MB por foto, 6 fotos por
                        chamado.
                      </p>
                      <button type="submit" disabled={!!busy}>
                        {busy === `photos-${t.id}`
                          ? "Enviando…"
                          : "Enviar fotos"}
                      </button>
                    </form>
                  )}
                  {outcome(`photos-${t.id}`)}
                  {t.rating !== undefined && (
                    <p>Sua avaliação: {t.rating} de 5</p>
                  )}
                  {t.canGiveFeedback && actions.feedback && (
                    <form
                      onSubmit={(e) =>
                        void submit(
                          `rating-${t.id}`,
                          e,
                          "Avaliação enviada.",
                          (d) =>
                            actions.feedback!(
                              t.id,
                              Number(d.get("rating")),
                              text(d, "comment"),
                            ),
                        )
                      }
                    >
                      <label>
                        <span>Avalie o atendimento</span>
                        <select name="rating" defaultValue="" required>
                          <option value="" disabled>
                            Selecione uma nota
                          </option>
                          {[1, 2, 3, 4, 5].map((n) => (
                            <option key={n} value={n}>
                              {n} de 5
                            </option>
                          ))}
                        </select>
                      </label>
                      <label>
                        <span>Comentário</span>
                        <textarea name="comment" maxLength={2000} />
                      </label>
                      <button type="submit" disabled={!!busy}>
                        {busy === `rating-${t.id}`
                          ? "Enviando…"
                          : "Enviar avaliação"}
                      </button>
                    </form>
                  )}
                  {outcome(`rating-${t.id}`)}
                </article>
              ))}
              {p.assistance.canOpen && actions.requestAssistance && (
                <form
                  onSubmit={(e) =>
                    void submit(
                      "assistance",
                      e,
                      "Chamado aberto. Ele aparece acima com o número do protocolo.",
                      (d, onProgress) =>
                        actions.requestAssistance!(
                          {
                            category: text(d, "category"),
                            title: text(d, "title"),
                            description: text(d, "description"),
                            location: text(d, "location"),
                            files: files(d),
                          },
                          onProgress,
                        ),
                    )
                  }
                >
                  <h3>Solicitar atendimento</h3>
                  <label>
                    <span>Categoria</span>
                    <select name="category" required>
                      {p.assistance.categories.map((c) => (
                        <option key={c}>{c}</option>
                      ))}
                    </select>
                  </label>
                  <label>
                    <span>Título do chamado</span>
                    <input name="title" required maxLength={200} />
                  </label>
                  <label>
                    <span>Como podemos ajudar?</span>
                    <textarea name="description" required maxLength={4000} />
                  </label>
                  <label>
                    <span>Local no imóvel</span>
                    <input name="location" maxLength={200} />
                  </label>
                  <label>
                    <span>Fotos (opcional)</span>
                    <input
                      name="files"
                      type="file"
                      multiple
                      accept="image/jpeg,image/png,image/webp"
                    />
                  </label>
                  <p>Até 6 fotos JPEG, PNG ou WebP, de até 8 MB cada.</p>
                  <button type="submit" disabled={!!busy}>
                    {busy === "assistance"
                      ? "Enviando…"
                      : "Solicitar atendimento"}
                  </button>
                  {outcome("assistance")}
                </form>
              )}
            </>
          )}
        </section>
      )}
    </>
  );
}
