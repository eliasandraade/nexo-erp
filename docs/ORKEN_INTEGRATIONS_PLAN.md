# Orken — Plano de Integrações Externas

> **Para agentes agentic:** Use `superpowers:subagent-driven-development` para implementar fase a fase. Cada fase é independente e produz software testável.

**Objetivo:** Criar uma camada central e padronizada de integrações externas no Orken, cobrindo dados brasileiros, pagamentos, comunicação, fiscal, armazenamento e APIs operacionais.

**Contexto de execução:** .NET 8 / Clean Architecture (Domain → Application → Infrastructure → Api). Sem infraestrutura de integração existente — começa do zero.

---

## 1. Resumo Executivo

### Integrações que fazem sentido agora

| # | API | Motivo |
|---|-----|--------|
| 1 | BrasilAPI + ViaCEP | Autofill de CEP/CNPJ é UX crítica no onboarding |
| 2 | Resend (já existe) | Email transacional já parcialmente implementado |
| 3 | Cloudflare R2 | Storage de imagens de produto e logos — necessidade imediata |
| 4 | Open Food Facts | Cadastro de produto por barcode acelera adoção no restaurante/varejo |
| 5 | Stripe | Billing SaaS — Orken não tem como cobrar tenants sem isso |
| 6 | Mercado Pago | Pix + cartão para PDV e restaurante — mercado brasileiro |
| 7 | Open-Meteo | Clima no diário de obra do Orken Build — baixo custo, alto valor |
| 8 | PDF (QuestPDF) | Orçamentos, recibos, fechamento de caixa — necessidade imediata |
| 9 | WhatsApp Business | Confirmação de pedido no Orken Menu — diferencial competitivo |
| 10 | NFe.io / Focus NFe | Fiscal — sem NF-e/NFC-e o PDV não é viável para maioria dos tenants |

### Integrações descartadas ou adiadas

| # | API | Motivo |
|---|-----|--------|
| 1 | ReceitaWS | BrasilAPI já retorna dados CNPJ suficientes; ReceitaWS como fallback só se necessário |
| 2 | Disify / EVA (email validation) | Prioridade baixa; rate limit em cadastro é suficiente por ora |
| 3 | Numverify / Veriphone | Validação de telefone pode ser regex + E.164; API externa é over-engineering agora |
| 4 | AfterShip | Tracking de envio — só relevante após módulo Store/E-commerce |
| 5 | Postmon | Superseded por BrasilAPI + ViaCEP |
| 6 | CloudConvert | Nenhum caso de uso imediato justifica o custo |
| 7 | Botd | Rate limit no backend resolve 80% do problema antes de precisar disso |
| 8 | VirusTotal / Safe Browsing | Sem upload de arquivos por terceiros ainda |
| 9 | Banco Central Open Data | Dashboards macroeconômicos são P3+ |
| 10 | Exchangerate / Fixer | Operações multi-moeda não estão no roadmap imediato |

### Ordem recomendada de implementação

```
Fase 1 → Infraestrutura base (HttpClientFactory, Polly, feature flags, logging)
Fase 2 → Cadastro inteligente (BrasilAPI, ViaCEP)
Fase 3 → Storage (Cloudflare R2)
Fase 4 → Produtos (Open Food Facts + barcode)
Fase 5 → Documentos (QuestPDF)
Fase 6 → Clima (Open-Meteo)
Fase 7 → Billing (Stripe + Mercado Pago)
Fase 8 → Orken Menu avançado (WhatsApp Business)
Fase 9 → iFood Partner API
Fase 10 → Fiscal (NFe.io / Focus NFe)
```

---

## 2. Arquitetura Proposta

### 2.1 Organização de pastas

```
Nexo.Application/
  Integrations/
    Contracts/                        ← interfaces públicas (IXxxProvider)
      ICepLookupProvider.cs
      ICnpjLookupProvider.cs
      IBarcodeProductLookupProvider.cs
      IStorageProvider.cs
      IPdfRenderer.cs
      IWeatherProvider.cs
      IBillingProvider.cs
      IPaymentProvider.cs
      IWhatsAppProvider.cs
      IFiscalDocumentProvider.cs
    DTOs/                             ← DTOs internos, independentes de provider
      CepLookupResult.cs
      CnpjLookupResult.cs
      ProductLookupResult.cs
      StorageUploadResult.cs
      PdfRenderRequest.cs
      PdfRenderResult.cs
      WeatherSnapshot.cs
      BillingCheckoutResult.cs
      PaymentIntentResult.cs
      WhatsAppMessageResult.cs
      FiscalDocumentResult.cs
    Options/                          ← classes Options para IOptions<T>
      BrasilApiOptions.cs
      ViaCepOptions.cs
      OpenFoodFactsOptions.cs
      CloudflareR2Options.cs
      StripeOptions.cs
      MercadoPagoOptions.cs
      WhatsAppOptions.cs
      FiscalOptions.cs
      OpenMeteoOptions.cs

Nexo.Infrastructure/
  Integrations/
    BrasilApi/
      BrasilApiCepProvider.cs
      BrasilApiCnpjProvider.cs
      BrasilApiBankProvider.cs
      BrasilApiHolidayProvider.cs
    ViaCep/
      ViaCepProvider.cs
    Composite/
      CompositeCepLookupProvider.cs   ← tenta BrasilAPI → ViaCEP
      CompositeCnpjLookupProvider.cs  ← BrasilAPI → ReceitaWS
    OpenFoodFacts/
      OpenFoodFactsProvider.cs
    Storage/
      CloudflareR2Provider.cs
    Pdf/
      QuestPdfRenderer.cs
    Weather/
      OpenMeteoProvider.cs
    Billing/
      StripeProvider.cs
    Payment/
      MercadoPagoProvider.cs
    WhatsApp/
      MetaWhatsAppProvider.cs
    Fiscal/
      NFeIoProvider.cs
    Common/
      IntegrationHttpClientHandler.cs ← logging + correlation id
      IntegrationResiliencePipeline.cs ← Polly: retry + timeout + circuit breaker
    DependencyInjection.cs            ← extensão AddIntegrations()

Nexo.Api/
  Controllers/
    Integrations/
      CepLookupController.cs
      CnpjLookupController.cs
      BarcodeProductController.cs
      StorageController.cs
      WeatherController.cs
      BillingWebhookController.cs
      PaymentWebhookController.cs
      WhatsAppWebhookController.cs
      FiscalWebhookController.cs
```

### 2.2 Interfaces principais

```csharp
// Application/Integrations/Contracts/ICepLookupProvider.cs
public interface ICepLookupProvider
{
    Task<CepLookupResult?> LookupAsync(string cep, CancellationToken ct = default);
}

// Application/Integrations/Contracts/ICnpjLookupProvider.cs
public interface ICnpjLookupProvider
{
    Task<CnpjLookupResult?> LookupAsync(string cnpj, CancellationToken ct = default);
}

// Application/Integrations/Contracts/IBarcodeProductLookupProvider.cs
public interface IBarcodeProductLookupProvider
{
    Task<ProductLookupResult?> LookupAsync(string barcode, CancellationToken ct = default);
}

// Application/Integrations/Contracts/IStorageProvider.cs
public interface IStorageProvider
{
    Task<StorageUploadResult> UploadAsync(StorageUploadRequest request, CancellationToken ct = default);
    Task DeleteAsync(string objectKey, CancellationToken ct = default);
    string GetPublicUrl(string objectKey);
}

// Application/Integrations/Contracts/IPdfRenderer.cs
public interface IPdfRenderer
{
    Task<PdfRenderResult> RenderAsync(PdfRenderRequest request, CancellationToken ct = default);
}

// Application/Integrations/Contracts/IWeatherProvider.cs
public interface IWeatherProvider
{
    Task<WeatherSnapshot?> GetCurrentAsync(double lat, double lon, CancellationToken ct = default);
    Task<WeatherSnapshot?> GetHistoricalAsync(double lat, double lon, DateOnly date, CancellationToken ct = default);
}

// Application/Integrations/Contracts/IBillingProvider.cs
public interface IBillingProvider
{
    Task<BillingCheckoutResult> CreateSubscriptionCheckoutAsync(BillingCheckoutRequest request, CancellationToken ct = default);
    Task<BillingPortalResult> CreatePortalSessionAsync(string customerId, CancellationToken ct = default);
    Task HandleWebhookAsync(string payload, string signature, CancellationToken ct = default);
}

// Application/Integrations/Contracts/IPaymentProvider.cs
public interface IPaymentProvider
{
    Task<PaymentIntentResult> CreatePaymentIntentAsync(PaymentIntentRequest request, CancellationToken ct = default);
    Task HandleWebhookAsync(string payload, string signature, CancellationToken ct = default);
}

// Application/Integrations/Contracts/IWhatsAppProvider.cs
public interface IWhatsAppProvider
{
    Task<WhatsAppMessageResult> SendTemplateMessageAsync(WhatsAppTemplateRequest request, CancellationToken ct = default);
}

// Application/Integrations/Contracts/IFiscalDocumentProvider.cs
public interface IFiscalDocumentProvider
{
    Task<FiscalDocumentResult> IssueDanfeAsync(FiscalDocumentRequest request, CancellationToken ct = default);
    Task<FiscalDocumentResult> IssueNfceAsync(FiscalDocumentRequest request, CancellationToken ct = default);
    Task<FiscalDocumentStatus> GetStatusAsync(string documentId, CancellationToken ct = default);
}
```

### 2.3 DTOs internos

```csharp
// Todos os DTOs ficam em Application/Integrations/DTOs/
// São agnósticos ao provider — o adapter mapeia o response da API para estes tipos

public record CepLookupResult(
    string Cep,
    string Street,
    string Neighborhood,
    string City,
    string State,
    string? IbgeCode,
    string Provider          // "BrasilApi" | "ViaCep" | "Postmon"
);

public record CnpjLookupResult(
    string Cnpj,
    string CompanyName,
    string? TradeName,
    string? Status,          // "ATIVA" | "BAIXADA" | etc.
    string? ActivityCode,
    string? ActivityDescription,
    CepLookupResult? Address,
    string Provider
);

public record ProductLookupResult(
    string Barcode,
    string Name,
    string? Brand,
    string? ImageUrl,
    string? Category,
    string? Quantity,
    string? Unit,
    string SourceProvider,
    double? Confidence        // 0.0–1.0; null = desconhecido
);

public record WeatherSnapshot(
    double Latitude,
    double Longitude,
    string? City,
    DateOnly Date,
    double TemperatureCelsius,
    double? PrecipitationMm,
    string? Condition,
    double? HumidityPercent,
    double? WindSpeedKmh,
    string Provider
);

public record StorageUploadRequest(
    Stream Content,
    string FileName,
    string ContentType,
    string? Folder = null     // ex: "products", "logos", "receipts"
);

public record StorageUploadResult(
    string ObjectKey,
    string PublicUrl,
    long SizeBytes
);

public record PdfRenderRequest(
    string TemplateType,      // "Receipt" | "Budget" | "CashClose" | "DailyLog"
    object Data,
    string? OutputFileName = null
);

public record PdfRenderResult(
    byte[] Bytes,
    string FileName,
    long SizeBytes
);
```

### 2.4 Resiliência: Polly

Cada integração HTTP deve ter um pipeline Polly configurável via options:

```csharp
// Infrastructure/Integrations/Common/IntegrationResiliencePipeline.cs
// Configuração padrão por integração:
//   - Timeout: 10s (ajustável por options)
//   - Retry: 3 tentativas, backoff exponencial (1s, 2s, 4s), apenas em 429/503/504
//   - Circuit breaker: 5 falhas em 30s → abre por 60s
//   - Não aplicar retry em 401/403/404/400 (são erros definitivos)
```

Pacote necessário: `Microsoft.Extensions.Http.Resilience` (integra Polly com `HttpClientFactory`).

### 2.5 Feature Flags

Padrão já existente via `InterpreterFeatureFlags`. Criar `IntegrationFeatureFlags`:

```csharp
// Application/Integrations/Options/IntegrationFeatureFlags.cs
public class IntegrationFeatureFlags
{
    public bool BrasilApiEnabled { get; init; }
    public bool ViaCepEnabled { get; init; }
    public bool OpenFoodFactsEnabled { get; init; }
    public bool StorageEnabled { get; init; }
    public bool PdfEnabled { get; init; }
    public bool WeatherEnabled { get; init; }
    public bool StripeEnabled { get; init; }
    public bool MercadoPagoEnabled { get; init; }
    public bool WhatsAppEnabled { get; init; }
    public bool FiscalEnabled { get; init; }
}
```

### 2.6 Logging e Auditoria

Cada chamada externa deve logar:
- `[Integration] {Provider} {Method} started — Tenant: {tenantId}`
- `[Integration] {Provider} {Method} succeeded in {ms}ms`
- `[Integration] {Provider} {Method} failed after {ms}ms — Status: {status}, Error: {error}`

**Nunca logar:** API keys, tokens, dados pessoais completos (CPF/CNPJ truncados), payloads de webhook.

### 2.7 Cache

Usar `ICacheService` já existente (Redis/NoOp). Chaves de cache por domínio:

```
integration:cep:{normalized_cep}               TTL: 30 dias
integration:cnpj:{normalized_cnpj}             TTL: 7 dias
integration:barcode:{barcode}                  TTL: 30 dias
integration:weather:current:{lat}:{lon}        TTL: 30 min
integration:weather:history:{lat}:{lon}:{date} TTL: permanente (histórico não muda)
integration:holiday:{year}:{country}           TTL: 1 ano
integration:bank:{code}                        TTL: 30 dias
```

---

## 3. Matriz de Integrações

| API | Categoria | Auth | Módulo(s) Impactado(s) | Prioridade | Cache | Limite/Rate | Implementar? |
|-----|-----------|------|------------------------|------------|-------|-------------|--------------|
| BrasilAPI | Dados BR | Nenhuma | Clientes, Fornecedores, Tenant, Build | P0 | CEP 30d, CNPJ 7d | Sem limite declarado | ✅ Fase 2 |
| ViaCEP | Dados BR | Nenhuma | Clientes, Fornecedores, Delivery | P0 | 30 dias | Sem limite declarado | ✅ Fase 2 (fallback) |
| ReceitaWS | Dados BR | Nenhuma | Fornecedores, Clientes PJ | P1 | 7–30 dias | ~3 req/min gratuito | ⏳ Fase 2 (fallback CNPJ) |
| Open Food Facts | Produtos | Nenhuma | Produtos, Estoque, PDV | P0 | 30 dias | Sem limite declarado | ✅ Fase 4 |
| QuestPDF | PDF | N/A (lib local) | Caixa, Vendas, Build, Restaurante | P1 | N/A | N/A | ✅ Fase 5 |
| Cloudflare R2 | Storage | API Key | Produtos, Logos, Build, Restaurante | P0 | N/A | Pago por uso | ✅ Fase 3 |
| Open-Meteo | Clima | Nenhuma | Orken Build | P1 | Atual 30min, Hist permanente | Sem limite (não-comercial) | ✅ Fase 6 |
| HG Weather | Clima BR | API Key | Orken Build (fallback) | P2 | 30 min | 10k req/mês free | ⏳ Fase 6 (fallback) |
| Stripe | Billing | API Key Secret | Platform, Billing, Tenant | P0 | N/A | Sem limite prático | ✅ Fase 7 |
| Mercado Pago | Pagamento | Access Token | PDV, Restaurante, Portal | P0 | N/A | Sem limite prático | ✅ Fase 7 |
| WhatsApp Business (Meta) | Comunicação | Bearer Token | Orken Menu, Delivery | P1 | N/A | 1k conv gratuitas/mês | ✅ Fase 8 |
| iFood Partner API | Delivery | OAuth2 | Orken Menu | P1 | N/A | Via contrato | ⏳ Fase 9 |
| NFe.io / Focus NFe | Fiscal | API Key | PDV, Restaurante, Vendas | P1 | N/A | Pago por emissão | ✅ Fase 10 |
| Spedy | Fiscal | API Key | PDV alternativo | P2 | N/A | Pago por emissão | ⏳ Alternativa fiscal |
| Resend | Email | API Key | Auth, Usuários, Platform | P0 | N/A | 100 emails/dia free | ✅ Já implementado |
| Disify | Email Validation | Nenhuma | Registro, Portal | P3 | 7 dias | Sem limite declarado | ❌ Adiar |
| Numverify / Veriphone | Phone Validation | API Key | Clientes, Portal | P3 | 7 dias | Pago | ❌ Adiar |
| AfterShip | Tracking | API Key | Vendas, Delivery | P3 | N/A | Pago | ❌ Adiar |
| Correios | Tracking BR | Usuário/senha | Vendas, Delivery | P2 | N/A | Instável | ❌ Adiar |
| CloudConvert | Conversão | API Key | Documentos | P3 | N/A | Pago | ❌ Adiar |
| Botd | Anti-bot | API Key | Portal, Registro | P3 | N/A | Pago | ❌ Adiar |
| Boleto.Cloud | Boleto | API Key | Financeiro | P2 | N/A | Pago | ⏳ Após Pix/Stripe |
| Banco Central | Dados financeiros | Nenhuma | Dashboard, Platform | P3 | 1 dia | Sem limite | ❌ Adiar |
| Frankfurter | Câmbio | Nenhuma | Financeiro multi-moeda | P3 | 1 dia | Sem limite | ❌ Adiar |

---

## 4. Fases de Implementação

### Fase 1 — Infraestrutura Base de Integrações

**Objetivo:** Criar a plataforma técnica que todas as integrações futuras usarão. Nenhuma integração concreta ainda.

**Entregáveis:**
- Estrutura de pastas `Application/Integrations/` e `Infrastructure/Integrations/`
- `IntegrationResiliencePipeline` com Polly (timeout, retry, circuit breaker)
- `IntegrationHttpClientHandler` (logging de correlation id, duração, status)
- `IntegrationFeatureFlags` com leitura via `IOptions<T>`
- `AddIntegrations()` extensão em `Infrastructure/Integrations/DependencyInjection.cs`
- Testes unitários do pipeline de resiliência

**Pacotes NuGet a adicionar:**
```xml
<!-- Nexo.Infrastructure.csproj -->
<PackageReference Include="Microsoft.Extensions.Http.Resilience" Version="8.*" />
<PackageReference Include="QuestPDF" Version="2024.*" />
<PackageReference Include="AWSSDK.S3" Version="3.*" />          <!-- R2 é S3-compatible -->
```

**Variáveis de ambiente mínimas desta fase:**
```env
Integrations__Enabled=true
```

---

### Fase 2 — Cadastro Inteligente (BrasilAPI + ViaCEP)

**Objetivo:** Autofill de CEP e CNPJ no cadastro de clientes, fornecedores e tenant.

**Integrações:**
- `BrasilApiCepProvider` → `GET https://brasilapi.com.br/api/cep/v2/{cep}`
- `BrasilApiCnpjProvider` → `GET https://brasilapi.com.br/api/cnpj/v1/{cnpj}`
- `ViaCepProvider` → `GET https://viacep.com.br/ws/{cep}/json/` (fallback CEP)
- `CompositeCepLookupProvider` → BrasilAPI → ViaCEP → null
- `CompositeCnpjLookupProvider` → BrasilAPI → ReceitaWS (se configurado) → null

**Endpoints no Nexo.Api:**
```
GET /api/integrations/cep/{cep}          → CepLookupResult
GET /api/integrations/cnpj/{cnpj}        → CnpjLookupResult
```

**Regras:**
- Autenticação obrigatória (tenant deve estar logado)
- Rate limit: máx 20 req/min por tenant via middleware existente
- Falha silenciosa: se ambos providers falharem, retornar 204 (não 500) — usuário preenche manualmente
- Cache via `ICacheService`

**Frontend (onde aparece):**
- Cadastro de Cliente → campo CEP com botão "Buscar"
- Cadastro de Fornecedor → campo CNPJ com botão "Preencher dados"
- Configurações da Empresa → campo CNPJ + CEP
- Onboarding do Tenant → mesmo fluxo

**Variáveis de ambiente:**
```env
Integrations__BrasilApi__Enabled=true
Integrations__BrasilApi__BaseUrl=https://brasilapi.com.br/api
Integrations__BrasilApi__TimeoutSeconds=8
Integrations__ViaCep__Enabled=true
Integrations__ViaCep__BaseUrl=https://viacep.com.br/ws
Integrations__ViaCep__TimeoutSeconds=6
```

---

### Fase 3 — Storage (Cloudflare R2)

**Objetivo:** Upload e servir imagens de produto, logos de tenant, comprovantes do Build.

**Integrações:**
- `CloudflareR2Provider` implementando `IStorageProvider`
- Usa AWS SDK for .NET (R2 é S3-compatible com endpoint customizado)

**Endpoints no Nexo.Api:**
```
POST /api/integrations/storage/upload       → StorageUploadResult
DELETE /api/integrations/storage/{key}
```

**Regras:**
- Validar content-type (apenas imagens e PDFs)
- Limite de tamanho: 10MB por arquivo (configurável)
- Pasta por contexto: `products/{tenantId}/`, `logos/{tenantId}/`, `build/{tenantId}/`
- Nunca expor credentials para o frontend — o upload sempre passa pelo backend
- Public URL via Custom Domain do R2 (não expor endpoint interno)

**Variáveis de ambiente:**
```env
Integrations__Storage__Provider=R2
Integrations__Storage__R2__AccountId=
Integrations__Storage__R2__AccessKeyId=
Integrations__Storage__R2__SecretAccessKey=
Integrations__Storage__R2__BucketName=orken-assets
Integrations__Storage__R2__PublicUrl=https://assets.orken.com.br
Integrations__Storage__R2__MaxFileSizeMb=10
```

---

### Fase 4 — Produtos por Código de Barras (Open Food Facts)

**Objetivo:** Acelerar cadastro de produto no PDV, restaurante e varejo via barcode.

**Integrações:**
- `OpenFoodFactsProvider` implementando `IBarcodeProductLookupProvider`
- `GET https://world.openfoodfacts.org/api/v0/product/{barcode}.json`

**Endpoint no Nexo.Api:**
```
GET /api/integrations/barcode/{barcode}    → ProductLookupResult
```

**Regras:**
- **Nunca criar produto automaticamente** — sempre retornar sugestão para confirmação do usuário
- Campos confiáveis: `name`, `barcode`, `brand`, `imageUrl`
- Campos não confiáveis: `category`, `unit` — apresentar como sugestão editável
- Salvar `sourceProvider` no audit log quando usado para criar/atualizar produto
- Cache por barcode: 30 dias

**Frontend:**
- Campo de barcode no cadastro de produto com ícone de câmera/scanner
- Retornar card de "Sugestão encontrada" com campos pré-preenchidos
- Usuário confirma ou edita antes de salvar

**Variáveis de ambiente:**
```env
Integrations__OpenFoodFacts__Enabled=true
Integrations__OpenFoodFacts__BaseUrl=https://world.openfoodfacts.org/api/v0
Integrations__OpenFoodFacts__TimeoutSeconds=10
```

---

### Fase 5 — Documentos PDF (QuestPDF)

**Objetivo:** Gerar PDFs de recibos, orçamentos, fechamento de caixa e diários de obra.

**Integrações:**
- `QuestPdfRenderer` implementando `IPdfRenderer` (biblioteca local, sem chamada HTTP)
- Templates: `ReceiptTemplate`, `BudgetTemplate`, `CashCloseTemplate`, `DailyLogTemplate`

**Endpoints no Nexo.Api:**
```
POST /api/integrations/pdf/receipt/{saleId}
POST /api/integrations/pdf/budget/{budgetId}
POST /api/integrations/pdf/cash-close/{cashId}
POST /api/integrations/pdf/daily-log/{logId}
```

**Regras:**
- Resposta `application/pdf` com header `Content-Disposition: attachment`
- Gerar em background para relatórios grandes; stream imediato para recibos
- Não armazenar PDF gerado no banco — gerar sob demanda (exceto se tenant pedir arquivo)
- Incluir logo do tenant (buscar de `IStorageProvider`)

**Licença QuestPDF:**
- Community License: gratuita para receita anual < $1M USD. Documentar decisão.

**Variáveis de ambiente:**
```env
Integrations__Pdf__Provider=QuestPdf
Integrations__Pdf__Enabled=true
```

---

### Fase 6 — Clima para Orken Build (Open-Meteo)

**Objetivo:** Registrar clima automaticamente no diário de obra. Justificar atrasos por chuva.

**Integrações:**
- `OpenMeteoProvider` implementando `IWeatherProvider`
- `GET https://api.open-meteo.com/v1/forecast?latitude={lat}&longitude={lon}&daily=...`
- `GET https://archive-api.open-meteo.com/v1/archive?...` (histórico)

**Endpoint no Nexo.Api:**
```
GET /api/integrations/weather/current?lat={lat}&lon={lon}
GET /api/integrations/weather/history?lat={lat}&lon={lon}&date={date}
```

**Regras:**
- Open-Meteo é gratuito para uso não-comercial; verificar termos para SaaS comercial
- Se usar comercialmente: plano pago ($0/mês grátis até 10k req/dia na API basic)
- Snapshot histórico deve ser **persistido no banco** ao ser registrado no diário de obra — não reconsultar API para data passada
- Campos mínimos: temperatura, precipitação, condição textual

**Variáveis de ambiente:**
```env
Integrations__Weather__Provider=OpenMeteo
Integrations__Weather__Enabled=true
Integrations__Weather__OpenMeteo__BaseUrl=https://api.open-meteo.com/v1
Integrations__Weather__OpenMeteo__TimeoutSeconds=8
```

---

### Fase 7 — Billing e Pagamentos (Stripe + Mercado Pago)

**Objetivo:** Cobrar tenants via Stripe (SaaS) e processar pagamentos do PDV/restaurante via Mercado Pago (Pix + cartão).

#### 7a — Stripe (Billing SaaS)

**Integrações:**
- `StripeProvider` implementando `IBillingProvider`
- Pacote: `Stripe.net`
- Fluxo: Checkout Session → Webhook → `tenant_subscriptions` table

**Webhooks esperados:**
- `checkout.session.completed` → ativar plano
- `customer.subscription.updated` → atualizar plano
- `customer.subscription.deleted` → desativar/trial
- `invoice.payment_failed` → notificar admin

**Variáveis de ambiente:**
```env
Integrations__Payments__Stripe__Enabled=true
Integrations__Payments__Stripe__SecretKey=sk_live_...
Integrations__Payments__Stripe__WebhookSecret=whsec_...
Integrations__Payments__Stripe__PriceIdMonthly=price_...
Integrations__Payments__Stripe__PriceIdYearly=price_...
```

#### 7b — Mercado Pago (PDV + Restaurante)

**Integrações:**
- `MercadoPagoProvider` implementando `IPaymentProvider`
- Pacote: `MercadoPago` (SDK oficial)
- Fluxo: Payment Intent → QR Code Pix ou Link → Webhook → `payment_transactions` table

**Webhooks esperados:**
- `payment.updated` → atualizar status do pedido/venda
- `payment.created` → registrar transação

**Regras:**
- Nunca expor `access_token` ao frontend
- Gerar QR Code no backend, retornar base64 para o frontend
- Cada tenant pode ter seu próprio `access_token` (sub-merchant) → armazenar criptografado em `tenant_integration_settings`

**Variáveis de ambiente:**
```env
Integrations__Payments__MercadoPago__Enabled=true
Integrations__Payments__MercadoPago__AccessToken=APP_USR-...
Integrations__Payments__MercadoPago__WebhookSecret=
```

---

### Fase 8 — WhatsApp Business (Meta Cloud API)

**Objetivo:** Enviar confirmações de pedido, status de entrega e notificações de preparo via WhatsApp.

**Integrações:**
- `MetaWhatsAppProvider` implementando `IWhatsAppProvider`
- `POST https://graph.facebook.com/v19.0/{phone_number_id}/messages`

**Templates necessários (devem ser aprovados pela Meta antes de usar em produção):**
- `order_confirmed` — confirmação de pedido delivery
- `order_ready` — pedido pronto para retirada
- `order_out_for_delivery` — saiu para entrega

**Regras:**
- Templates devem ser gerenciados via Meta Business → aprovação pode levar até 48h
- Nunca enviar mensagem de texto livre para número novo (violação de política)
- Apenas enviar para número que optou por receber (campo `opt_in_whatsapp` no cliente)
- Log de cada envio: número (truncado), template, status, timestamp
- Webhook de status: entregue/lido/falhou → atualizar `whatsapp_message_logs`

**Variáveis de ambiente:**
```env
Integrations__WhatsApp__Enabled=true
Integrations__WhatsApp__AccessToken=EAABz...
Integrations__WhatsApp__PhoneNumberId=
Integrations__WhatsApp__BusinessAccountId=
Integrations__WhatsApp__WebhookVerifyToken=
```

---

### Fase 9 — iFood Partner API

**Objetivo:** Receber pedidos do iFood automaticamente no Orken Menu.

**Arquitetura obrigatória (não negociável):**
```
iFood → POST /api/integrations/ifood/webhook
      → criar RestDeliveryOrder (status: Pendente)
      → operador aceita no Orken Menu
      → criar RestOrder
      → KDS recebe evento via SignalR
```

**Regras:**
- iFood **nunca** cria `RestOrder` diretamente — sempre entra como `RestDeliveryOrder`
- Polling de eventos iFood via `GET /order/v1.0/events:polling` (long polling)
- OAuth2: `client_credentials` com renovação automática de token
- Necessita contrato comercial com iFood — não é API pública

**Pré-requisitos antes de implementar:**
1. Contrato com iFood Partner Program assinado
2. Aprovação de `merchantId` e credenciais
3. Ambiente de sandbox testado

**Variáveis de ambiente:**
```env
Integrations__IFood__Enabled=true
Integrations__IFood__ClientId=
Integrations__IFood__ClientSecret=
Integrations__IFood__MerchantId=
Integrations__IFood__BaseUrl=https://merchant-api.ifood.com.br
```

---

### Fase 10 — Fiscal (NFe.io / Focus NFe)

**Objetivo:** Emitir NFC-e no PDV e NF-e em vendas B2B via provider fiscal plugável.

**Integrações:**
- `NFeIoProvider` implementando `IFiscalDocumentProvider`
- `FocusNFeProvider` como alternativa (mesmo interface)

**Fluxo:**
```
Venda/PDV → IFiscalDocumentProvider.IssueNfceAsync(request)
           → cria fiscal_document_requests (status: Pending)
           → chama API do provider
           → atualiza status: Issued | Failed
           → retorna DANFE URL ou XML
           → imprime/envia para cliente
```

**Regras:**
- Orken **não** emite nota diretamente — sempre delega ao provider
- Nunca bloquear venda se fiscal falhar — criar flag `fiscal_pending` na venda
- Retentar fiscal em background (job periódico)
- Armazenar XML e chave de acesso da nota
- Dados fiscais do tenant (CNPJ, IE, regime) devem estar configurados antes de habilitar

**Pré-requisitos antes de implementar:**
1. Conta ativa no NFe.io ou Focus NFe
2. Certificado digital A1 do tenant carregado
3. Ambiente de homologação testado com SEFAZ

**Variáveis de ambiente:**
```env
Integrations__Fiscal__Provider=NFeIo
Integrations__Fiscal__Enabled=true
Integrations__Fiscal__NFeIo__ApiKey=
Integrations__Fiscal__NFeIo__BaseUrl=https://api.nfe.io
Integrations__Fiscal__FocusNFe__Token=
Integrations__Fiscal__FocusNFe__BaseUrl=https://homologacao.focusnfe.com.br
```

---

## 5. Modelo de Configuração Completo

```env
# ── Flags globais ─────────────────────────────────────────────────────────────
Integrations__Enabled=true

# ── Dados Brasileiros ─────────────────────────────────────────────────────────
Integrations__BrasilApi__Enabled=true
Integrations__BrasilApi__BaseUrl=https://brasilapi.com.br/api
Integrations__BrasilApi__TimeoutSeconds=8

Integrations__ViaCep__Enabled=true
Integrations__ViaCep__BaseUrl=https://viacep.com.br/ws
Integrations__ViaCep__TimeoutSeconds=6

Integrations__ReceitaWs__Enabled=false
Integrations__ReceitaWs__BaseUrl=https://www.receitaws.com.br/v1
Integrations__ReceitaWs__TimeoutSeconds=10

# ── Produtos ──────────────────────────────────────────────────────────────────
Integrations__OpenFoodFacts__Enabled=true
Integrations__OpenFoodFacts__BaseUrl=https://world.openfoodfacts.org/api/v0
Integrations__OpenFoodFacts__TimeoutSeconds=10

# ── Storage ───────────────────────────────────────────────────────────────────
Integrations__Storage__Provider=R2
Integrations__Storage__R2__AccountId=
Integrations__Storage__R2__AccessKeyId=
Integrations__Storage__R2__SecretAccessKey=
Integrations__Storage__R2__BucketName=orken-assets
Integrations__Storage__R2__PublicUrl=https://assets.orken.com.br
Integrations__Storage__R2__MaxFileSizeMb=10

# ── PDF ───────────────────────────────────────────────────────────────────────
Integrations__Pdf__Provider=QuestPdf
Integrations__Pdf__Enabled=true

# ── Clima ─────────────────────────────────────────────────────────────────────
Integrations__Weather__Provider=OpenMeteo
Integrations__Weather__Enabled=true
Integrations__Weather__OpenMeteo__BaseUrl=https://api.open-meteo.com/v1
Integrations__Weather__OpenMeteo__TimeoutSeconds=8

# ── Email ─────────────────────────────────────────────────────────────────────
Resend__ApiKey=re_...

# ── Billing SaaS ──────────────────────────────────────────────────────────────
Integrations__Payments__Stripe__Enabled=true
Integrations__Payments__Stripe__SecretKey=sk_live_...
Integrations__Payments__Stripe__WebhookSecret=whsec_...
Integrations__Payments__Stripe__PriceIdMonthly=price_...
Integrations__Payments__Stripe__PriceIdYearly=price_...

# ── Pagamentos PDV/Restaurante ────────────────────────────────────────────────
Integrations__Payments__MercadoPago__Enabled=true
Integrations__Payments__MercadoPago__AccessToken=APP_USR-...
Integrations__Payments__MercadoPago__WebhookSecret=

# ── WhatsApp Business ─────────────────────────────────────────────────────────
Integrations__WhatsApp__Enabled=false
Integrations__WhatsApp__AccessToken=
Integrations__WhatsApp__PhoneNumberId=
Integrations__WhatsApp__BusinessAccountId=
Integrations__WhatsApp__WebhookVerifyToken=

# ── iFood ─────────────────────────────────────────────────────────────────────
Integrations__IFood__Enabled=false
Integrations__IFood__ClientId=
Integrations__IFood__ClientSecret=
Integrations__IFood__MerchantId=
Integrations__IFood__BaseUrl=https://merchant-api.ifood.com.br

# ── Fiscal ────────────────────────────────────────────────────────────────────
Integrations__Fiscal__Enabled=false
Integrations__Fiscal__Provider=NFeIo
Integrations__Fiscal__NFeIo__ApiKey=
Integrations__Fiscal__NFeIo__BaseUrl=https://api.nfe.io
Integrations__Fiscal__FocusNFe__Token=
Integrations__Fiscal__FocusNFe__BaseUrl=https://homologacao.focusnfe.com.br
```

> **Regra de ouro:** Qualquer chave com valor sensível (tokens, secrets, API keys) vai **somente** em variáveis de ambiente ou Secret Manager. Nunca em `appsettings.json` commitado.

---

## 6. Banco de Dados

### 6.1 Tabelas necessárias (com justificativa)

#### `external_integration_logs`
**Justificativa:** Auditoria de todas as chamadas externas. Essencial para debugging, SLA, cobrança.
```sql
CREATE TABLE nexo.external_integration_logs (
    id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id       UUID NOT NULL REFERENCES nexo.tenants(id),
    provider        VARCHAR(64) NOT NULL,   -- "BrasilApi", "Stripe", "WhatsApp"
    operation       VARCHAR(128) NOT NULL,  -- "CepLookup", "CreatePayment"
    request_id      VARCHAR(64),            -- correlation id
    status_code     INT,
    duration_ms     INT,
    success         BOOLEAN NOT NULL,
    error_message   TEXT,
    created_at      TIMESTAMPTZ NOT NULL DEFAULT now()
);
CREATE INDEX ON nexo.external_integration_logs (tenant_id, provider, created_at);
```

#### `tenant_integration_settings`
**Justificativa:** Cada tenant pode ter suas próprias credenciais (Mercado Pago, WhatsApp, Fiscal). Armazenadas criptografadas.
```sql
CREATE TABLE nexo.tenant_integration_settings (
    id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id       UUID NOT NULL REFERENCES nexo.tenants(id),
    integration_key VARCHAR(64) NOT NULL,   -- "mercadopago", "whatsapp", "fiscal"
    config_json     TEXT NOT NULL,           -- AES-256 encrypted JSON blob
    enabled         BOOLEAN NOT NULL DEFAULT false,
    created_at      TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at      TIMESTAMPTZ NOT NULL DEFAULT now(),
    UNIQUE (tenant_id, integration_key)
);
```

#### `product_external_sources`
**Justificativa:** Rastrear qual produto foi criado/atualizado com dados de API externa (Open Food Facts, ANVISA futura).
```sql
CREATE TABLE nexo.product_external_sources (
    id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id       UUID NOT NULL REFERENCES nexo.tenants(id),
    product_id      UUID NOT NULL REFERENCES nexo.products(id),
    barcode         VARCHAR(32),
    source_provider VARCHAR(64) NOT NULL,   -- "OpenFoodFacts"
    source_data     JSONB,                  -- raw data usado no cadastro
    created_at      TIMESTAMPTZ NOT NULL DEFAULT now()
);
```

#### `fiscal_document_requests`
**Justificativa:** NF-e/NFC-e têm estados assíncronos (pending → issued → authorized). Necessita persistência.
```sql
CREATE TABLE nexo.fiscal_document_requests (
    id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id       UUID NOT NULL REFERENCES nexo.tenants(id),
    sale_id         UUID REFERENCES nexo.sales(id),
    document_type   VARCHAR(16) NOT NULL,   -- "NFCe" | "NFe"
    provider        VARCHAR(32) NOT NULL,   -- "NFeIo" | "FocusNFe"
    status          VARCHAR(32) NOT NULL DEFAULT 'Pending',
    provider_ref_id VARCHAR(128),           -- ID no provider externo
    access_key      VARCHAR(64),            -- chave de acesso NFe
    xml_url         TEXT,
    danfe_url       TEXT,
    error_message   TEXT,
    attempts        INT NOT NULL DEFAULT 0,
    last_attempt_at TIMESTAMPTZ,
    issued_at       TIMESTAMPTZ,
    created_at      TIMESTAMPTZ NOT NULL DEFAULT now()
);
```

#### `whatsapp_message_logs`
**Justificativa:** Log de envios WhatsApp para compliance (opt-in/opt-out), debugging e custo.
```sql
CREATE TABLE nexo.whatsapp_message_logs (
    id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id       UUID NOT NULL REFERENCES nexo.tenants(id),
    phone_last4     CHAR(4) NOT NULL,       -- nunca salvar número completo
    template_name   VARCHAR(128) NOT NULL,
    status          VARCHAR(32) NOT NULL,   -- "Sent" | "Delivered" | "Read" | "Failed"
    provider_msg_id VARCHAR(128),
    error_message   TEXT,
    sent_at         TIMESTAMPTZ NOT NULL DEFAULT now()
);
```

### 6.2 Tabelas descartadas

- `external_lookup_cache` — **descartada**: Redis já faz esse papel via `ICacheService`.
- `delivery_channel_connections` — **adiada**: implementar junto com Fase 9 (iFood).
- `customer_external_sources` — **adiada**: baixa necessidade agora; endereço preenchido via CEP não requer auditoria persistida.

---

## 7. Segurança

### 7.1 Credenciais

- Todas as API keys e tokens ficam **exclusivamente** em variáveis de ambiente ou Secret Manager (Railway/AWS Secrets Manager)
- Credenciais de tenant (Mercado Pago, WhatsApp, Fiscal) ficam em `tenant_integration_settings.config_json` criptografado com AES-256-GCM, chave derivada de `INTEGRATION_ENCRYPTION_KEY` env var
- **Nunca** expor qualquer credential em log, response de API, ou frontend

### 7.2 Frontend

- Nenhuma chamada de API externa parte do frontend — tudo via endpoints internos do Nexo.Api
- Nenhum token/key retornado ao cliente
- Respostas de lookup (CEP/CNPJ) são sanitizadas — sem dados extras da API externa

### 7.3 Webhooks

- Todos os webhooks devem verificar assinatura HMAC antes de processar (Stripe: `Stripe-Signature`, Meta: `X-Hub-Signature-256`, Mercado Pago: `x-signature`)
- Retornar `200 OK` imediatamente e processar em background — evitar timeout de webhook
- Idempotência: verificar `event_id` para não processar duplicados

### 7.4 Rate Limiting por Tenant

- Lookups de CEP/CNPJ: máx 20 req/min por tenant
- Barcode lookup: máx 30 req/min por tenant
- Upload de storage: máx 50 req/min por tenant
- Implementar via middleware existente ou Redis counter

### 7.5 Timeout Obrigatório

Nenhuma chamada externa sem timeout. Padrões:
- APIs de cadastro (CEP, CNPJ, barcode): 8–10s
- Storage upload: 30s
- PDF generation (local): 15s
- WhatsApp / Fiscal: 15s
- Stripe / Mercado Pago: 20s

### 7.6 Circuit Breaker

Polly circuit breaker para cada provider externo:
- Abre após 5 falhas em 30 segundos
- Fica aberto por 60 segundos
- Log quando abre/fecha: `[CircuitBreaker] {provider} OPEN after {n} failures`

### 7.7 Isolamento Multi-Tenant

- Todo log de integração inclui `tenant_id`
- Credenciais de tenant nunca vazam entre tenants (validar `tenant_id` em toda busca de `tenant_integration_settings`)
- Storage: objetos organizados por `/{tenantId}/` — validar no upload

---

## 8. UX — Mapeamento por Módulo

| Módulo | Integração | UX Element | Comportamento |
|--------|-----------|------------|---------------|
| Clientes | CEP Lookup | Campo CEP + botão "Buscar endereço" | Autofill de rua, bairro, cidade, estado |
| Fornecedores | CNPJ Lookup | Campo CNPJ + botão "Preencher dados" | Autofill de razão social, endereço, atividade |
| Config. Empresa | CEP + CNPJ | Ambos na tela de configurações do tenant | Mesmo fluxo |
| Onboarding | CEP + CNPJ | Wizard de setup inicial | Preencher dados da empresa automaticamente |
| Produtos | Barcode | Campo barcode + botão câmera | Abrir câmera (mobile) ou digitar; exibir card de sugestão |
| PDV | Barcode | Leitor USB + campo de busca | Buscar produto por barcode com lookup automático |
| Orken Build | Clima | Automático no diário de obra | Ao criar diário de obra, buscar clima e pré-preencher |
| Caixa | PDF | Botão "Gerar fechamento PDF" | Download ou impressão |
| Vendas | PDF | Botão "Gerar recibo PDF" | Idem |
| Build | PDF | Botão "Exportar orçamento PDF" | Idem |
| Restaurante | PDF | Botão "Imprimir comanda" | Idem |
| Produtos/Tenant | Storage | Upload de imagem | Drag-and-drop ou seleção; preview antes de salvar |
| Platform | Stripe | Botão "Assinar plano" | Redireciona para Stripe Checkout |
| PDV/Restaurante | Mercado Pago | Botão "Cobrar via Pix" | QR Code + polling de status |
| Restaurante | WhatsApp | Automático ao confirmar pedido delivery | Enviar template após operador aceitar pedido |
| Restaurante | iFood | Badge "iFood" no pedido | Pedidos entram como delivery orders; fluxo igual ao portal |
| PDV/Vendas | Fiscal | Botão "Emitir NF-e" / "Emitir NFC-e" | Modal de confirmação → emitir → exibir DANFE |

### 8.1 Padrões de UX para falhas de integração

- **CEP/CNPJ não encontrado:** Toast informativo "Dados não encontrados. Preencha manualmente." — nunca bloquear formulário.
- **Barcode não encontrado:** Card "Produto não encontrado nesta base. Cadastre manualmente." com campos vazios pré-abertos.
- **PDF falhou:** Toast de erro + opção "Tentar novamente". Nunca mostrar stack trace.
- **Pix timeout:** Modal "Aguardando confirmação de pagamento..." com polling de 30s; opção de cancelar.
- **WhatsApp falhou:** Pedido segue normalmente; log interno de falha; tentar reenviar via job.
- **Fiscal falhou:** Venda é registrada; badge "Fiscal pendente" na venda; retentar em background.

---

## 9. Riscos e Decisões Pendentes

### 9.1 BrasilAPI / ViaCEP
| Item | Detalhe |
|------|---------|
| Custo | Gratuito |
| Rate limit | Não declarado; usar cache agressivo para evitar problemas |
| Estabilidade | BrasilAPI é bem mantida; ViaCEP é estável há anos |
| Risco | Sem SLA garantido — mas em cache de 30 dias o impacto é mínimo |
| Ação | Implementar composite com fallback — baixo risco |

### 9.2 Open Food Facts
| Item | Detalhe |
|------|---------|
| Custo | Gratuito (Creative Commons) |
| Rate limit | Não declarado oficialmente; respeitar 1 req/s para ser cidadão |
| Estabilidade | Boa, mas coverage no Brasil é limitada |
| Risco | Dados de categoria/unidade são ruidosos — nunca auto-aplicar |
| Ação | Implementar com confiança parcial; deixar usuário confirmar sempre |

### 9.3 QuestPDF
| Item | Detalhe |
|------|---------|
| Custo | Gratuito até $1M ARR; depois licença comercial ($999/ano) |
| Rate limit | N/A (local) |
| Risco | Mudança de licença em 2023 causou controvérsia; verificar termos atuais |
| Ação | **Decisão pendente:** confirmar licença comercial aceitável antes de implementar. Alternativa: Scriban + Puppeteer |

### 9.4 Cloudflare R2
| Item | Detalhe |
|------|---------|
| Custo | $0.015/GB storage + sem egress fee (diferencial vs S3) |
| Rate limit | 1M operações de classe A/mês grátis; depois $4.50/M |
| Risco | Baixo; S3-compatible facilita migração |
| Ação | Confirmar criação de bucket e custom domain antes de implementar |

### 9.5 Open-Meteo
| Item | Detalhe |
|------|---------|
| Custo | Gratuito para não-comercial; plano pago para uso comercial (verificar) |
| Rate limit | 10k req/dia free; plano pago para mais |
| Risco | **Verificar termos:** Orken é SaaS comercial — pode precisar de plano pago |
| Ação | **Decisão pendente:** checar se API gratuita permite uso em SaaS comercial |

### 9.6 Stripe
| Item | Detalhe |
|------|---------|
| Custo | 2.9% + $0.30 por transação; sem mensalidade |
| Rate limit | Sem limite prático |
| Risco | Requer conta Stripe aprovada; pode exigir verificação KYC da empresa |
| Ação | Criar conta Stripe e validar identidade antes de implementar |

### 9.7 Mercado Pago
| Item | Detalhe |
|------|---------|
| Custo | ~3.99% por transação Pix; varia por plano |
| Rate limit | Sem limite prático |
| Risco | Modelo sub-merchant (cada tenant com próprio MP) pode exigir OAuth por tenant |
| Ação | **Decisão pendente:** modelo plataforma (Orken como marketplace MP) vs. cada tenant com conta própria |

### 9.8 WhatsApp Business API
| Item | Detalhe |
|------|---------|
| Custo | 1k conversas de serviço gratuitas/mês; depois ~$0.0688 por conversa (BR) |
| Rate limit | Por template e número |
| Risco | Templates precisam de aprovação Meta (24–48h); política de mensagens rígida |
| Ação | **Pré-requisito:** criar conta Meta Business, verificar empresa, criar e aprovar templates ANTES de implementar |

### 9.9 iFood Partner API
| Item | Detalhe |
|------|---------|
| Custo | Comissão por pedido (negociada no contrato) |
| Rate limit | Via contrato |
| Risco | **Alto:** API não é pública, exige contrato formal com iFood; pode levar semanas |
| Ação | Iniciar processo de parceria iFood agora se quiser implementar em 3–6 meses |

### 9.10 NFe.io / Focus NFe
| Item | Detalhe |
|------|---------|
| Custo | Por emissão (~R$0,10–R$0,50/documento, depende de plano) |
| Rate limit | Via contrato |
| Risco | **Alto complexidade:** requer certificado digital A1 do tenant, homologação na SEFAZ, regime tributário correto |
| Ação | **Pré-requisitos críticos:** homologar ANTES de qualquer tenant entrar em produção; erro fiscal tem consequência legal |

---

## 10. Recomendação Final

### ✅ Implementar agora (Fases 1–6)

1. **Fase 1 — Infraestrutura Base:** fundação para tudo; sem ela não tem integração segura
2. **BrasilAPI + ViaCEP (Fase 2):** impacto imediato em UX de cadastro, custo zero
3. **Cloudflare R2 (Fase 3):** storage é necessidade básica para imagens de produto e logos
4. **Open Food Facts (Fase 4):** acelera cadastro de produto no restaurante e PDV
5. **QuestPDF (Fase 5):** recibos e orçamentos em PDF são esperados por qualquer PME
6. **Open-Meteo (Fase 6):** Orken Build é diferencial; clima no diário de obra é feature única

### ⏳ Implementar no médio prazo (Fases 7–8)

7. **Stripe + Mercado Pago (Fase 7):** essencial para monetização, mas exige conta aprovada e testes extensivos
8. **WhatsApp Business (Fase 8):** diferencial para Orken Menu, mas depende de aprovação Meta

### 🔒 Só implementar após pré-requisitos externos (Fases 9–10)

9. **iFood (Fase 9):** só após contrato firmado com iFood
10. **Fiscal (Fase 10):** só após homologação na SEFAZ e certificado digital do tenant

### ❌ Não implementar agora

- Disify / email validation — rate limit é suficiente
- Numverify / phone validation — regex E.164 resolve 95% dos casos
- AfterShip — sem módulo Store ainda
- CloudConvert — sem caso de uso
- Botd — rate limit resolve
- Banco Central / Exchangerate — nenhum módulo consome isso

### ⚠️ Decisões pendentes que bloqueiam implementação

| Decisão | Impacto | Quem decide |
|---------|---------|-------------|
| QuestPDF: confirmar licença comercial aceitável | Bloqueia Fase 5 | Elias |
| Open-Meteo: verificar termos para SaaS comercial | Bloqueia Fase 6 | Elias |
| Mercado Pago: modelo plataforma vs. conta por tenant | Bloqueia Fase 7b | Elias |
| Stripe: criar conta e verificar identidade | Bloqueia Fase 7a | Elias |
| Meta Business: criar conta e aprovar templates | Bloqueia Fase 8 | Elias |
| iFood: iniciar processo de parceria | Bloqueia Fase 9 | Elias |
| NFe.io: conta + certificado + homologação SEFAZ | Bloqueia Fase 10 | Elias |

---

*Documento gerado em 2026-06-14. Revisar antes da implementação de cada fase.*
