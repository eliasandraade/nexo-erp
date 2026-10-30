import { useState, type FormEvent } from "react";
import type { PortalActions } from "./api/gateway";
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
}: {
  portal: Portal;
  actions: PortalActions;
}) {
  const [busy, setBusy] = useState(false),
    [notice, setNotice] = useState("");
  const submit = async (
    e: FormEvent<HTMLFormElement>,
    work: (data: FormData) => Promise<void>,
  ) => {
    e.preventDefault();
    if (busy) return;
    const form = e.currentTarget;
    const data = new FormData(form);
    setBusy(true);
    setNotice("");
    try {
      await work(data);
      setNotice("Solicitação registrada. O registro está sendo atualizado.");
      form.reset();
    } catch (error) {
      if (error instanceof PortalHttpError && error.code === "partial_upload")
        form.reset();
      setNotice(actionError(error));
    } finally {
      setBusy(false);
    }
  };
  const text = (d: FormData, k: string) => String(d.get(k) ?? "");
  const files = (d: FormData) =>
    d.getAll("files").filter((f): f is File => f instanceof File && f.size > 0);
  const signer = (
    <>
      <label>
        Seu nome completo
        <input name="signer" required maxLength={200} autoComplete="name" />
      </label>
      <label>
        Comentário (opcional)
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
                    void submit(e, (d) =>
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
                    Decisão
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
                    Li os itens e os impactos informados e confirmo esta
                    decisão.
                  </label>
                  <button disabled={busy}>Registrar decisão do aditivo</button>
                </form>
              )}
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
                <a href={safeUrl(d.url)} target="_blank" rel="noreferrer">
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
                  void submit(e, (d) =>
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
                  Revisei a vistoria e confirmo o aceite da entrega.
                </label>
                <button disabled={busy}>Registrar aceite da entrega</button>
              </form>
            )
          )}
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
                        />
                      </a>
                    ))}
                  </div>
                  {t.canAddPhotos && actions.uploadEvidence && (
                    <form
                      onSubmit={(e) =>
                        void submit(e, (d) =>
                          actions.uploadEvidence!(t.id, files(d)),
                        )
                      }
                    >
                      <label>
                        Adicionar fotos ao chamado
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
                      <button disabled={busy}>Enviar fotos</button>
                    </form>
                  )}
                  {t.rating !== undefined && (
                    <p>Sua avaliação: {t.rating} de 5</p>
                  )}
                  {t.canGiveFeedback && actions.feedback && (
                    <form
                      onSubmit={(e) =>
                        void submit(e, (d) =>
                          actions.feedback!(
                            t.id,
                            Number(d.get("rating")),
                            text(d, "comment"),
                          ),
                        )
                      }
                    >
                      <label>
                        Avalie o atendimento
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
                        Comentário
                        <textarea name="comment" maxLength={2000} />
                      </label>
                      <button disabled={busy}>Enviar avaliação</button>
                    </form>
                  )}
                </article>
              ))}
              {p.assistance.canOpen && actions.requestAssistance && (
                <form
                  onSubmit={(e) =>
                    void submit(e, (d) =>
                      actions.requestAssistance!({
                        category: text(d, "category"),
                        title: text(d, "title"),
                        description: text(d, "description"),
                        location: text(d, "location"),
                        files: files(d),
                      }),
                    )
                  }
                >
                  <h3>Solicitar atendimento</h3>
                  <label>
                    Categoria
                    <select name="category" required>
                      {p.assistance.categories.map((c) => (
                        <option key={c}>{c}</option>
                      ))}
                    </select>
                  </label>
                  <label>
                    Título do chamado
                    <input name="title" required maxLength={200} />
                  </label>
                  <label>
                    Como podemos ajudar?
                    <textarea name="description" required maxLength={4000} />
                  </label>
                  <label>
                    Local no imóvel
                    <input name="location" maxLength={200} />
                  </label>
                  <label>
                    Fotos (opcional)
                    <input
                      name="files"
                      type="file"
                      multiple
                      accept="image/jpeg,image/png,image/webp"
                    />
                  </label>
                  <p>Até 6 fotos JPEG, PNG ou WebP, de até 8 MB cada.</p>
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
    </>
  );
}
