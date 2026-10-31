# Orken Build — Previsão de entrega e clima por atividade (Wave 6)

Como o Orken Build prevê a entrega de uma obra e avisa sobre o clima, sem IA e sem inventar números. Rotas em
[`internal-api.md`](internal-api.md) (# Wave 6). A explicação em linguagem natural continua sendo a da Wave 5
(`POST /projects/{id}/risk/explain`, ver [`ai.md`](ai.md)) — nenhuma chamada nova a LLM.

## 1. Princípios

1. **Determinística.** Mesmos dados, mesma data → mesma previsão. Motor puro `BuildForecastEngine`
   (`Nexo.Application/Modules/Build`), metodologia **`productivity/1`**, versão 1. Testado em
   `BuildForecastEngineTests`.
2. **Previsão não é promessa.** Toda previsão traz confiança (0..1), os fatores que a explicam, a metodologia e a versão.
3. **Riscos não movem datas.** Aditivos pendentes e clima aparecem como fatores; só o avanço real e o plano movem datas.
4. **Histórico guardado.** Cada previsão armazenada (`bld_forecasts`) é imutável: dá para responder "o que previmos no dia X".
5. **Liga/desliga por empresa.** `BuildTenantSettings.BuildPredictEnabled` (padrão ligado, alterado só pela plataforma).
   Desligado → `403 build_predict_disabled` nas rotas de previsão, o job não grava e o progresso volta ao `weighted-stages/1`.

## 2. Calendário de trabalho

| Item | Regra | Configuração |
|---|---|---|
| Dias úteis | segunda a sábado (domingo nunca, por padrão) | `Build:Forecast:WorkingDays` (`Mon,Tue,…`, `seg,ter,…` ou `0..6`, 0 = domingo) |
| Feriados nacionais | tabela estática `BrazilNationalHolidays` — fixos (1/1, 21/4, 1/5, 7/9, 12/10, 2/11, 15/11, 20/11, 25/12) para qualquer ano + Sexta-feira Santa 2026 (3/4), 2027 (26/3) e 2028 (14/4) | `Build:Forecast:ExtraHolidays` (lista `yyyy-MM-dd`, soma à tabela) |
| Janela de produtividade | últimos 21 dias corridos | `Build:Forecast:WindowDays` (3–180) |

Sem API externa. Carnaval e Corpus Christi são ponto facultativo (não entram); feriados estaduais/municipais também não —
use `ExtraHolidays`. **Manutenção:** acrescentar a Sexta-feira Santa de cada novo ano na tabela (2029: 30/3).

## 3. Metodologia `productivity/1`

Por etapa, na ordem (`order`, depois nome):

1. **Concluída** (status `Completed` ou 100%) → fim = fim real (ou fim planejado). Confiança 1.
2. **Produtividade** (Δ% por dia útil) de uma etapa iniciada (% > 0):
   - **janela** — avanço entre o início da janela (ou o início real, se depois) e hoje ÷ dias úteis desse intervalo,
     quando ao menos um evento de avanço (`bld_stage_progress_events`) dentro da janela mudou o %;
   - **geral** — % atual ÷ dias úteis desde o início real (sem eventos recentes);
   - **planejada** — 100 ÷ dias úteis do plano (sem dados; fator `no_productivity_data`).
3. **Fim de etapa iniciada** = hoje + ⌈(100 − %) ÷ produtividade⌉ dias úteis (pulando domingos e feriados).
4. **Etapa não iniciada**: mantém a duração planejada em dias úteis e começa no início planejado — nunca antes de hoje —
   e, quando o início planejado não é depois do fim previsto da **etapa anterior (pela ordem)**, no dia útil seguinte a
   esse fim (**atraso herdado**, fator `inherited_delay`). Sem datas planejadas e sem avanço → sem previsão (`no_forecast`).
5. **Entrega prevista** = o maior fim previsto. Atraso = previsão − fim planejado (dias corridos); a rota também devolve
   `daysLateVsContract` = previsão − término contratado da obra (`expectedEnd`).

Limites: no máximo ~2 500 dias úteis por cálculo (ritmo quase nulo não trava o motor). Etapas em paralelo não são
modeladas (a sequência usa só a etapa anterior); a produtividade baixa de uma etapa não altera a duração das próximas.

### Confiança (0..1)

`confiança = Σ(peso × confiança da etapa) ÷ Σ peso` sobre as etapas não concluídas (todas concluídas → 1; sem etapas ou
sem previsão → 0). Confiança da etapa = base do método × (0,6 + 0,4 × min(1, eventos ÷ 5)) × 1 ÷ (1 + CV), com base
**janela 0,9**, **geral 0,65**, **planejada 0,5** (não iniciada) / **0,35** (iniciada sem histórico), e CV = coeficiente
de variação dos ritmos entre eventos consecutivos (menos de dois intervalos → sem penalidade).

### Fatores

| `kind` | Quando | Severidade |
|---|---|---|
| `delivery_after_plan` | entrega prevista depois do fim planejado | médio; alto > 30 dias |
| `low_productivity` | ritmo observado < 80% do planejado | baixo; médio se a etapa atrasa; alto > 15 dias |
| `inherited_delay` | etapa empurrada pela anterior | baixo; médio > 7; alto > 30 dias |
| `pending_change_order` | aditivo `Draft`/`SentToClient` com impacto no prazo — **não aplicado** | baixo; médio > 7 dias |
| `weather_risk` | dia(s) com risco climático para etapa sensível em campo (§4) — **não aplicado** | baixo; médio com risco alto |
| `no_productivity_data` | etapa iniciada sem eventos (usa o ritmo planejado) | baixo |
| `no_forecast` | etapa sem datas e sem avanço | baixo |

## 4. Clima por atividade

- Fonte: Open-Meteo (`/v1/forecast`, gratuito, sem chave): `precipitation_sum`, `precipitation_probability_max`,
  `wind_speed_10m_max` (km/h), `temperature_2m_max/min`, `weathercode`, hoje + 7 dias, fuso `America/Sao_Paulo`. Cache de
  1 h por coordenada (serviço de cache da API), timeout/retry/circuit breaker do cliente HTTP da integração, e 15 s no
  máximo por consulta. Ligado só com `Integrations:Features:WeatherEnabled`.
- **Etapa sensível**: `BuildStage.WeatherSensitive` (`true`/`false`, definido pela construtora em
  `PUT /stages/{id}/weather-sensitivity`); `null` → palavras-chave no nome, sem acento/maiúsculas: concretagem, concreto,
  fundação, cobertura, telhado, impermeabilização, pintura externa, fachada, terraplenagem (lista substituível em
  `Build:Weather:SensitiveKeywords`).
- **Em campo** num dia: etapa em andamento (de hoje até o fim previsto/planejado; atrasada → em aberto) ou não iniciada
  entre o início e o fim previstos (ou planejados).
- **Limiares** (`BuildWeatherRules`):

| Condição | Regra | Nível |
|---|---|---|
| Chuva forte | ≥ 10 mm **e** probabilidade ≥ 70% | alto |
| Chuva | ≥ 10 mm com probabilidade menor/desconhecida, ou ≥ 2 mm com probabilidade ≥ 60% | médio |
| Vento forte | rajada máxima ≥ 50 km/h | alto |
| Vento | ≥ 40 km/h | médio |

- Mensagem: "Chuva forte prevista (15 mm, probabilidade 85%) — etapa Concretagem da laje. Previsão, pode mudar." — sempre
  "previsão", nunca certeza. O risco entra na previsão como fator `weather_risk`, **sem deslocar datas** (decisão: o clima
  previsto muda várias vezes por dia e a construtora decide se remarca).
- Sem coordenadas → `200` com `available: false`, `reason: "no_coordinates"` (também `weather_disabled`,
  `provider_unavailable`). O clima do bootstrap do portal (v1.0) não mudou.

## 5. Onde a previsão aparece

- `GET/POST /projects/{id}/forecast`, `GET …/forecast/history` (rotas da Wave 6).
- **Progresso da Wave 1** (`GET /projects/{id}/progress`, retratos diários, curvas, portal): com BuildPredict ligado, as
  datas previstas por etapa, a entrega prevista e o % previsto passam a vir do `productivity/1`
  (`methodology: "weighted-stages/1+productivity/1"`); planejado e real não mudam. Desligado, ou quando o motor não chega a
  uma entrega (nenhuma etapa com previsão) → `weighted-stages/1` como antes (fallback).
- Risco de prazo (Wave 5, `forecast_after_contract`) e painel geral (`daysLate`) usam a mesma entrega prevista.
- Job diário (`BuildProgressSnapshotJob`, de hora em hora, idempotente): depois dos retratos, grava uma previsão
  `Scheduled` por obra ativa (planejamento, andamento, pausada) das empresas com BuildPredict ligado que ainda não tenha
  previsão no dia.

## 6. Limitações conhecidas

- Sem dependências entre etapas além da ordem; sem recursos/equipes; sem curva S por etapa (ritmo linear).
- Feriados estaduais/municipais e paradas da obra (chuva já ocorrida, greve) não entram no calendário; registre o avanço
  real e a previsão se ajusta.
- A confiança é heurística (volume e regularidade dos dados), não um intervalo estatístico.
- Clima só para os próximos 7 dias e só como aviso.
