# Orken Build — Gêmeo digital (Wave 4)

Como o Orken Build liga o modelo da obra ao andamento real, sem inventar geometria. Rotas: [`internal-api.md`](internal-api.md)
(# Wave 4) e, para o portal do cliente, [`client-portal-contract.md`](client-portal-contract.md) (v1.3, §2.19–§2.22).

## 1. Princípios

1. **Três camadas separadas.**
   - **Fonte geométrica:** o arquivo do modelo (privado) e o seu derivado de visualização (GLB, privado). Só o que veio
     do projetista. O Orken nunca desenha, completa ou "corrige" geometria.
   - **Verdade operacional:** etapas, cronograma, diário, medições (Waves 1–3) e os eventos de estado de elemento.
   - **Interpretação:** vínculos etapa ↔ elemento/zona/categoria (`source` `Manual`; `AiSuggested`/`AiConfirmed` chegam na
     Wave 5, sempre com confirmação humana).
2. **O arquivo bruto nunca sai.** IFC/RVT/DWG/PDF de projeto ficam em `private/tenants/{t}/build/models/…`. O portal só
   recebe a URL assinada (~15 min) do GLB de visualização e mapas de estado por id externo.
3. **Sem vínculo não é avanço.** Elemento sem etapa fica neutro (`unbound`), nunca "0%" nem "concluído".
4. **Representação simplificada é rotulada.** Caixas só existem se a construtora digitou as medidas; o aviso
   "Representação simplificada; não corresponde ao projeto arquitetônico" acompanha sempre.

## 2. Pipeline

```
upload (multipart) ──► primeiros bytes ──► recusa (422, nada gravado)        zip, ifcXML, DXF binário, desconhecido, glTF com URI externa
                                     ├──► rejeição registrada (Rejected)       RVT, DWG — nada armazenado, mensagem de exportação
                                     └──► SHA-256 + armazenamento privado ──► versão Queued + job Parse
                                                                               (PDF/imagem: Ready na hora)
BuildModelProcessingJobRunner (a cada 10 s; UPDATE … FOR UPDATE SKIP LOCKED … RETURNING)
   Parse ─► IFC: xBIM (zonas + elementos + tipo + material + quantidades)
         ─► GLB/glTF: nós com malha = elementos; nós de topo com filhos = zonas
         ─► DXF: nomes de camadas
         ─► uma transação (elementos em lotes de 1000) ─► Ready + versão atual do modelo
         ─► IFC com geometria + IfcConvert configurado ─► job Convert ─► viewer.glb (ConvertedServer)
   sem IfcConvert ─► o visualizador interno converte no navegador (web-ifc) e envia o GLB:
                     PUT /models/versions/{id}/viewer (≥ 50% dos nós = GlobalIds da versão) ─► ConvertedBrowser
```

Falhas: arquivo ilegível/esquema não suportado → `Failed` na hora, com mensagem; outros erros → nova tentativa (até 3, trava
que expira, tempo limite por job). `POST /models/versions/{id}/reprocess` recoloca uma versão `Failed` na fila.

## 3. Formatos

| Formato | Detecção (bytes) | Resultado | Geometria |
|---|---|---|---|
| IFC (STEP) IFC2X3/IFC4 | `ISO-10303-21;` | elementos + hierarquia + quantidades | `NativeBim` (sem nenhuma geometria → só hierarquia, `AssistedMapping`) |
| GLB (glTF 2.0 binário) | `glTF` + versão 2 | elementos dos nós; o próprio arquivo é o visualizador | `Converted3D` |
| glTF JSON | `{` + `asset.version` 2.x | idem; só com buffers/imagens embutidos (`data:`) | `Converted3D` |
| DXF ASCII | `0` / `SECTION` | nomes das camadas; zonas desenhadas pelo usuário | `AssistedMapping` |
| PDF, PNG, JPEG, WebP | `%PDF-`, PNG, `FF D8 FF`, `RIFF…WEBP` | folha para zonas (contorno 0..1) | `AssistedMapping` |
| RVT | OLE `D0 CF 11 E0 A1 B1 1A E1` | **rejeitado** (registrado, não armazenado) | — |
| DWG | `AC10…` | **rejeitado** (registrado, não armazenado) | — |
| IFCZIP / ZIP | `PK 03 04` | recusado: descompacte e envie o `.ifc` | — |
| ifcXML | `<?xml` | recusado: exporte IFC STEP | — |
| DXF binário | `AutoCAD Binary DXF` | recusado: salve como ASCII | — |
| glTF com `uri` externa | JSON | recusado (`gltf_external_uri`): o servidor não busca arquivos externos | — |

**Por que RVT/DWG não:** são formatos proprietários (Autodesk) sem leitor aberto confiável; a conversão exige serviço
externo pago (Autodesk Platform Services ou similar), fora do escopo. Mensagem ao usuário: "Arquivos .rvt/.dwg precisam ser
exportados para IFC (Revit: Arquivo → Exportar → IFC; AutoCAD/Civil: exportar IFC) ou GLB. Conversão automática exige
serviço externo, não incluído."

**IFC — o que é lido** (xBIM Essentials 6, `Xbim.IO.MemoryModel`, código gerenciado, sem motor de geometria nem
dependência nativa — roda no Linux; licença CDDL-1.0): `IfcSite`/`IfcBuilding`/`IfcBuildingStorey`/`IfcSpace` (pai por
`IfcRelAggregates`, cota do pavimento) e todo `IfcElement` exceto aberturas (`IfcFeatureElementSubtraction`), com GlobalId
(id externo, único por versão), classe (`IfcWallStandardCase` → `IfcWall`), nome, tipo (`IfcRelDefinesByType`), 1º material
(`IfcRelAssociatesMaterial`), zona (`IfcRelContainedInSpatialStructure`, subindo pela agregação para peças como lances de
escada) e quantidades de `Qto_*` (`netVolume`, `netArea`, `length`, `count`; bruto como reserva; ≤ 2 KB por elemento).
Limite: `Build:Models:MaxElements` (200 000).

## 4. Estados da fonte geométrica

| `geometrySource` | Quando | Aviso mostrado ao cliente |
|---|---|---|
| `NativeBim` | IFC com geometria | Modelo BIM do projeto; cores = andamento registrado; não substitui vistoria |
| `Converted3D` | GLB/glTF enviado | Modelo 3D fornecido pela construtora; idem |
| `AssistedMapping` | PDF/imagem/DXF (ou IFC sem geometria) | Mapeamento sobre a planta (2D), não é modelo 3D |
| `SimplifiedRepresentation` | sem modelo, caixas digitadas | Representação simplificada; não corresponde ao projeto arquitetônico |

A obra usa a **versão principal**: a versão atual de um modelo, preferindo 3D com visualizador, Arquitetura primeiro, depois o
modelo mais antigo.

## 5. Regras de estado (4D)

Estado de um elemento numa data `d` (`BuildTwinStateEngine`, puro e testado):

1. **Evento explícito** (`bld_element_progress_events`, só inclusão): o último com data efetiva ≤ `d` (empate: o registrado por
   último). Vale pelo id externo em qualquer versão da obra (GlobalId é estável entre versões).
2. **Derivado da etapa vinculada**, precedência **Elemento > Zona** (a do elemento ou a ancestral mais próxima) **> Categoria**
   (categoria com versão nula vale para todas as versões). Várias etapas no mesmo nível → a de **fim previsto mais tarde**
   (com data vence sem data; empate pelo id). Estado da etapa em `d`:
   - % em `d` = último `BuildStageProgressEvent` com data efetiva ≤ `d` (histórico da Wave 1); etapa sem histórico algum →
     % atual a partir de hoje e 0 antes (o passado não é inventado);
   - 100% → `Completed`; status atual `Blocked` (o status não tem histórico: só de hoje em diante) → `Blocked`; fim previsto
     < `d` → `Delayed`; % > 0 → `InProgress`; senão `NotStarted`.
3. Sem vínculo e sem evento → `null` (contado em `unbound`, desenhado neutro).

Zonas recebem o estado da zona vinculada mais próxima (própria ou ancestral) — é o que pinta as caixas da representação
simplificada. Eventos explícitos **não** mexem no % da etapa nem nos retratos diários (`bld_progress_snapshots`). NC por
elemento (Wave 3) não entra nesta regra.

## 6. Preparação para 5D

`GET /projects/{id}/twin/cost?date=`: por etapa, orçado (orçamento de referência da Wave 1/2) e realizado até a data
(lançamentos confirmados de saída da obra com etapa), elementos vinculados (pela regra acima na data) e soma das
quantidades do IFC → custo por elemento e por m³/m². É **rateio por quantidade** — base para o 5D, não é orçamento por
elemento nem medição.

## 7. Captura da realidade

Foto, 360°, vídeo, drone e nuvem de pontos (`.las`/`.e57`, guardada sem processamento), privadas
(`private/tenants/{t}/build/reality/{projectId}/…`), identificadas pelos primeiros bytes (JPEG/PNG/WebP, `ftyp` MP4/MOV,
EBML WebM, `LASF`, `ASTM-E57`), limites por família (25/200/500 MB, configuráveis), ligadas a etapa/zona/elemento e
visíveis ao cliente só quando marcadas (flag `realityCapture` do portal). O envio passa pela API (sem URL pré-assinada de
PUT direto ao R2 por enquanto).

## 8. Configuração

| Variável (ambiente: `__` no lugar de `:`) | Padrão | Uso |
|---|---|---|
| `Build:Models:MaxUploadMb` | 150 | limite do modelo; o corpo/multipart só é ampliado nas rotas de upload de modelo |
| `Build:Models:MaxElements` | 200000 | elementos por versão |
| `Build:Models:ViewerMatchMinRatio` | 0.5 | fração mínima de nós do GLB do navegador que precisam ser GlobalIds da versão |
| `Build:Models:JobTimeoutMinutes` | 15 | tempo limite por job (a trava dura +5 min) |
| `Build:Models:PollSeconds` | 10 | intervalo do worker |
| `Build:Models:IfcConvertPath` | vazio | caminho do binário IfcConvert; vazio = sem conversão no servidor |
| `Build:Models:IfcConvertTimeoutSeconds` | 300 | tempo limite da conversão |
| `Build:RealityCapture:MaxImageMb` / `MaxVideoMb` / `MaxPointCloudMb` | 25 / 200 / 500 | limites da captura |

Recurso por empresa: `BuildTenantSettings.Build3DEnabled` (padrão ligado), alterado só pela plataforma
(`PUT /api/platform/tenants/{id}/build-settings`, auditado). Desligado → `403 build_3d_disabled` nos uploads/processamento.

### IfcConvert (opcional)

Sem ele tudo funciona: a hierarquia, os elementos e o estado vêm do IFC, e o visualizador interno gera o GLB no navegador.
Para converter no servidor:

1. Baixe o IfcConvert do IfcOpenShell (release 0.8.x, Linux x64) de <https://github.com/IfcOpenShell/IfcOpenShell/releases>
   e inclua o binário na imagem (ex.: `/opt/ifcopenshell/IfcConvert`, `chmod +x`).
2. Defina `Build__Models__IfcConvertPath=/opt/ifcopenshell/IfcConvert` (e, se quiser, `Build__Models__IfcConvertTimeoutSeconds=600`).
3. O worker roda `IfcConvert --use-element-guids -y input.ifc output.glb` (argumentos fixos — nada do usuário entra na linha
   de comando) numa pasta temporária apagada ao fim, com tempo limite; o GLB vira `viewer.glb` (`ConvertedServer`). Falhou →
   a versão continua `Ready` e a mensagem orienta a conversão no navegador.

## 9. Custos

Nenhum custo obrigatório: leitura de IFC/GLB/DXF é local (xBIM, gerenciado), a fila é uma tabela do próprio Postgres e o
worker roda no processo da API. Custos variáveis: armazenamento no R2 (modelos até 150 MB, vídeos e nuvens de pontos) e,
se ligado, CPU/memória do IfcConvert e do xBIM para modelos grandes (o modelo inteiro é lido em memória — dimensione a
instância ou limite `MaxUploadMb`). Nenhuma chamada a serviço externo.
