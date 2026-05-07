# Orken Menu — Arquitetura Oficial

> **Versão:** pós-implementação Delivery Hub + Portal Público  
> **Data:** 2026-04-26  
> **Status:** Fonte de verdade. O código é o árbitro final.

---

## 1. O que é o Orken Menu

O Orken Menu é o módulo `restaurante` do NexoERP. Gerencia a operação completa de restaurantes e bares: salão, cozinha, entrega e portal público do cliente.

O sistema usa uma arquitetura de **inbox multicanal**: todo pedido externo (portal próprio, iFood futuro, telefone, WhatsApp, balcão) entra como `RestDeliveryOrder`. O operador triaja, aceita, e só então o pedido entra na operação interna como `RestOrder`.

Nenhum canal externo cria `RestOrder` diretamente. Nunca.

---

## 2. Fluxo Central

```
Cliente (Portal / iFood / WhatsApp / Telefone / Balcão)
    │
    ▼
RestDeliveryOrder          ← inbox multicanal; status de entrega
    │
    │  Operador aceita
    ▼
RestOrder                  ← operação interna; preparo + pagamento
    │
    ▼
Cozinha (KDS)
    │
    ▼
Entrega / Retirada / Mesa
```

Pedidos de mesa não passam pelo `RestDeliveryOrder`. O garçom abre um `RestOrder` diretamente via `POST /api/restaurante/orders`.

---

## 3. Componentes do Sistema

### 3.1 Delivery Hub

Painel do operador para gerenciar pedidos externos.

- Entrada: qualquer canal externo
- Visualização: todos os `RestDeliveryOrder` com filtros por status e canal
- Ações disponíveis: aceitar, rejeitar, cancelar, atribuir entregador, avançar status
- Rota frontend: `/restaurante/delivery`
- Endpoint raiz: `GET /api/restaurante/delivery-orders`

### 3.2 Portal Público

Portal web do restaurante para clientes fazerem pedidos online.

- URL: `/menu/{slug}` — slug globalmente único na tabela `stores`
- Rastreamento: `/rastrear/{token}` — token opaco de 32 chars hex
- Sem autenticação em nenhum endpoint público
- Pedido criado via `POST /api/public/orders` → gera `RestDeliveryOrder` com `Channel=Portal`
- Cardápio: `GET /api/public/menu/{slug}` — apenas produtos com `IsMenuVisible=true`

**Flags de controle do portal** (em `FoodServiceSettings`):
- `AcceptingOrders` — liga/desliga recebimento de novos pedidos sem remover o slug
- `DeliveryEnabled` — habilita/desabilita modalidade entrega
- `TakeawayEnabled` — habilita/desabilita modalidade retirada

### 3.3 Operação do Restaurante

Gestão de salão e comandas pelos garçons.

- Mesas organizadas em áreas (`RestArea` → `RestTable`)
- Garçom abre comanda: `POST /api/restaurante/orders` → `RestOrder` com `OrderType=DineIn`
- Itens adicionados com snapshot de preço e grupos de modificadores
- Fechamento: `POST /orders/{id}/close` → gera `Sale` em Draft (CORE)
- Pagamento: `POST /orders/{id}/pay` → confirma `Sale`, deduz ingredientes via `StockMovement RecipeOutput`, libera mesa

### 3.4 Cozinha (KDS)

Kitchen Display System em tempo real.

- Rota frontend: `/restaurante/cozinha`
- Exibe itens de `RestOrder` com status `Pending` ou `Preparing`
- Fluxo de item: `Pending → Preparing → Ready → Delivered`
- Quando todos os itens ativos de um `RestOrder` atingem `Ready` → `RestOrder.Status = Ready`
- `RestOrder.Ready` sincroniza `RestDeliveryOrder.Status = ReadyForPickup` via `IDeliveryOrderSyncService`
- WebSocket para atualizações em tempo real (`useKitchenSocket`)

---

## 4. Modelo de Dados

### 4.1 `RestDeliveryOrder`

Representa qualquer pedido externo. Contém a perspectiva do cliente e da entrega.

| Campo | Tipo | Descrição |
|---|---|---|
| `Id` | Guid | PK |
| `TenantId` | Guid | Tenant |
| `StoreId` | Guid | Loja |
| `OrderNumber` | int | Sequencial por loja |
| `TrackingToken` | string | 32 chars hex, URL-safe, público |
| `Channel` | `DeliveryChannel` | Origem do pedido |
| `OrderType` | `DeliveryOrderType` | `Delivery` ou `Takeaway` |
| `Status` | `DeliveryOrderStatus` | Estado atual |
| `CustomerName` | string | Nome do cliente |
| `CustomerPhone` | string | Telefone normalizado (só dígitos) |
| `CustomerEmail` | string? | Email opcional |
| `CustomerId` | Guid? | Link a cliente cadastrado (opcional) |
| `DeliveryAddressJson` | jsonb? | Endereço de entrega (objeto JSON) |
| `DeliveryFee` | decimal | Taxa de entrega; 0 para Takeaway |
| `EstimatedMinutes` | int? | Tempo estimado de entrega |
| `RiderName` | string? | Nome do entregador |
| `RiderPhone` | string? | Telefone do entregador |
| `RestOrderId` | Guid? | **Link para `RestOrder`; null até AcceptAsync** |
| `Notes` | string? | Observações |
| `ItemsSubtotal` | decimal | Soma dos itens (calculado) |
| `Total` | decimal | `ItemsSubtotal + DeliveryFee` |
| `ExternalOrderId` | string? | ID no sistema externo (iFood etc.) |
| `RawPayload` | string? | Payload bruto do canal externo |
| `ReceivedAt` | DateTime | Timestamp de chegada |
| `AcceptedAt` | DateTime? | Timestamp de aceite |
| `ReadyAt` | DateTime? | Timestamp de pronto |
| `DispatchedAt` | DateTime? | Timestamp de saída para entrega |
| `DeliveredAt` | DateTime? | Timestamp de entrega/retirada |
| `CancelledAt` | DateTime? | Timestamp de cancelamento |

**`DeliveryChannel`:**
```
Portal | IFood | Rappi | Anotaai | WhatsApp | PhoneCall | InPerson | Other
```

**`DeliveryOrderType`:** `Delivery | Takeaway`

**`DeliveryOrderStatus` + máquina de estados:**
```
Received ──────► Accepted ──► InPreparation ──► ReadyForPickup ──► OutForDelivery ──► Delivered
    │                │                                  │                               ▲
    │                │                                  └───────────────────────────────┘
    ▼                ▼
 Rejected        Cancelled

Qualquer estado (exceto Delivered | Rejected | Cancelled) → Cancelled
OutForDelivery é exclusivo de pedidos Delivery (nunca Takeaway)
SetDelivered requer ReadyForPickup ou OutForDelivery como estado anterior
```

### 4.2 `RestOrder`

Representa a comanda interna. Contém itens, preparo e pagamento. Criado exclusivamente via `AcceptAsync`.

| Campo | Tipo | Descrição |
|---|---|---|
| `Id` | Guid | PK |
| `TenantId` | Guid | Tenant |
| `StoreId` | Guid | Loja |
| `OrderNumber` | int | Sequencial |
| `Status` | `RestOrderStatus` | Estado atual |
| `OrderType` | `RestOrderType` | `DineIn | Counter | Takeaway` |
| `TableId` | Guid? | Mesa (null para Counter/Takeaway) |
| `WaiterId` | Guid | Usuário que abriu |
| `CustomerId` | Guid? | Cliente (opcional) |
| `SaleId` | Guid? | Link para `Sale` do CORE (gerado em Close) |
| `CouvertAmount` | decimal | Couvert aplicado |
| `ServiceFeeAmount` | decimal | Taxa de serviço |
| `Notes` | string? | Observações |
| `OpenedAt` | DateTime | Abertura |
| `ClosedAt` | DateTime? | Fechamento |
| `CancelledAt` | DateTime? | Cancelamento |
| `ItemsSubtotal` | decimal | Soma de itens ativos |
| `Total` | decimal | `ItemsSubtotal + Couvert + ServiceFee` |

**Relacionamento com `RestDeliveryOrder`:**
- `RestDeliveryOrder.RestOrderId` aponta para `RestOrder.Id`
- `RestOrder` não tem FK para `RestDeliveryOrder`
- A relação é unidirecional e estabelecida no momento do `AcceptAsync`

**`RestOrderStatus` + máquina de estados:**
```
Open → InPreparation → Ready → Closed → Paid
 └────────────────────────────► Cancelled
 
Closed e Paid são terminais para cancelamento (não pode cancelar depois de fechar)
SetReady: quando todos os itens ativos estão Ready
```

### 4.3 `RestOrderItem`

Itens da comanda interna. Têm status independente de preparo.

**`RestOrderItemStatus` + máquina de estados:**
```
Pending → Preparing → Ready → Delivered
   └────────────────────────► Cancelled (exceto Delivered)
```

| Campo | Tipo | Descrição |
|---|---|---|
| `ProductId` | Guid | Produto |
| `ProductName` | string | Snapshot do nome |
| `UnitPrice` | decimal | Snapshot do preço |
| `Quantity` | decimal | Quantidade |
| `Total` | decimal | `Qty × UnitPrice + modifiers` |
| `Notes` | string? | Observação do item |
| `Status` | `RestOrderItemStatus` | Estado de preparo |
| `SentToKitchenAt` | DateTime? | Enviado para cozinha |
| `PreparedAt` | DateTime? | Preparado |
| `DeliveredAt` | DateTime? | Entregue na mesa |
| `Modifiers` | list | Modificadores aplicados (snapshot) |

### 4.4 `FoodServiceSettings`

Configurações operacionais e do portal por loja. Uma por loja, criada com defaults.

**Configurações operacionais:**

| Campo | Tipo | Padrão |
|---|---|---|
| `StoreType` | string | `"restaurant"` |
| `CouvertEnabled` | bool | `false` |
| `CouvertPricePerPerson` | decimal? | — |
| `CouvertAutomatic` | bool | `false` |
| `ServiceFeeEnabled` | bool | `false` |
| `ServiceFeePercent` | decimal? | — |
| `OrderTypesEnabled` | string | `"DineIn,Counter,Takeaway"` (CSV) |

**Configurações do portal:**

| Campo | Tipo | Padrão |
|---|---|---|
| `DisplayName` | string? | — |
| `LogoUrl` | string? | — |
| `CoverImageUrl` | string? | — |
| `Description` | string? | — |
| `WhatsAppPhone` | string? | — |
| `BusinessHoursJson` | string? | Array JSON: `{dayOfWeek, isOpen, openTime, closeTime}` |
| `AcceptingOrders` | bool | `true` |
| `DeliveryEnabled` | bool | `true` |
| `TakeawayEnabled` | bool | `true` |

### 4.5 `Store.PublicSlug`

- Tipo: `string?`
- `null` = portal desabilitado para esta loja
- Globalmente único na tabela `stores`
- Normalizado: lowercase, sem acentos, apenas `[a-z0-9-]`, hifens simples
- Definido via `PATCH /api/stores/{id}/public-slug`
- Acesso público: `/menu/{slug}` e `/api/public/menu/{slug}`

---

## 5. Fluxos Operacionais

### 5.1 Pedido via Portal Público

```
1. Cliente acessa /menu/{slug}
   GET /api/public/menu/{slug}
   → PublicMenuDto (cardápio + flags do portal)

2. Cliente monta carrinho + preenche dados

3. Cliente confirma pedido
   POST /api/public/orders
   Body: { publicSlug, orderType, customerName, customerPhone,
           deliveryAddressJson?, items[], notes? }
   Validações (no backend, antes de criar):
     - store existe por PublicSlug
     - FoodServiceSettings.AcceptingOrders = true
     - se Delivery: DeliveryEnabled = true
     - se Takeaway: TakeawayEnabled = true
     - todos os produtos são IsMenuVisible = true
     - grupos de modificadores obrigatórios preenchidos
     - maxSelections respeitado
   → RestDeliveryOrder criado (Status=Received, Channel=Portal)
   → Response: { id, orderNumber, trackingToken, status, total }

4. Cliente rastreia pedido
   GET /api/public/orders/{trackingToken}
   → { orderNumber, status, statusLabel, estimatedMinutes, orderType }
   Polling automático a cada 30s até status terminal (Delivered | Rejected | Cancelled)
```

### 5.2 Pedido Manual (Operador)

```
1. Operador acessa Delivery Hub
2. Cria pedido manual via ManualOrderSheet
   POST /api/restaurante/delivery-orders/manual
   Body: { orderType, channel, customerName, customerPhone, ... }
   → RestDeliveryOrder (Status=Received, Channel=PhoneCall|WhatsApp|InPerson)
```

### 5.3 Pedido de Mesa (Garçom)

```
1. Garçom acessa /restaurante (FloorPage)
2. Seleciona mesa disponível
3. Abre comanda (OpenOrderSheet)
   POST /api/restaurante/orders
   Body: { tableId, partySize, orderType=DineIn }
   → RestOrder (Status=Open), Mesa → Occupied
   → Couvert aplicado automaticamente se CouvertAutomatic=true

4. Adiciona itens
   POST /api/restaurante/orders/{id}/items
   Body: { productId, quantity, modifiers[], notes? }
   → Snapshot de preço no momento da adição

5. [Opcional] Cancela item
   DELETE /api/restaurante/orders/{id}/items/{itemId}
```

### 5.4 Aceite e Criação de RestOrder (Delivery Hub)

```
1. Operador vê RestDeliveryOrder com Status=Received no Delivery Hub
2. Clica em Aceitar
   POST /api/restaurante/delivery-orders/{id}/accept
   Body: { estimatedMinutes? }
   
   No backend (AcceptAsync):
   - RestDeliveryOrder.Status → Accepted
   - RestOrder criado com OrderType=Takeaway (delivery orders têm OrderType próprio)
   - Items do RestDeliveryOrder copiados para RestOrder (snapshot de preço/nome)
   - RestDeliveryOrder.RestOrderId = RestOrder.Id
   
   → Response: DeliveryOrderDto com restOrderId preenchido

3. [Alternativa] Rejeitar
   POST /api/restaurante/delivery-orders/{id}/reject
   Body: { reason }
   → RestDeliveryOrder.Status → Rejected (RestOrder NUNCA criado)
```

### 5.5 Fluxo de Cozinha (KDS)

```
1. Cozinheiro acessa /restaurante/cozinha (KitchenPage)
2. Vê itens pendentes de todos os RestOrders ativos

3. Inicia preparo de item
   PATCH /api/restaurante/orders/{id}/items/{itemId}/status
   Body: { status: "Preparing" }
   → RestOrderItem.Status → Preparing
   → Se todos os itens ativos ≠ Pending: RestOrder.Status → InPreparation
   → Se RestOrder veio de AcceptAsync: DeliveryOrder.Status → InPreparation (via sync)

4. Conclui item
   PATCH /api/restaurante/orders/{id}/items/{itemId}/status
   Body: { status: "Ready" }
   → RestOrderItem.Status → Ready
   → Se todos os itens ativos = Ready: RestOrder.Status → Ready
   → Se RestOrder veio de AcceptAsync: DeliveryOrder.Status → ReadyForPickup (via sync)
```

### 5.6 Sincronização `RestOrder` → `RestDeliveryOrder`

A sincronização é **unidirecional**: mudanças no `RestOrder` propagam para o `RestDeliveryOrder` via `IDeliveryOrderSyncService`.

```
RestOrder.Status      →  DeliveryOrder.Status
─────────────────────────────────────────────
InPreparation         →  InPreparation
Ready                 →  ReadyForPickup
Cancelled             →  Cancelled
```

`RestDeliveryOrder` **não** propaga mudanças de volta ao `RestOrder`.

### 5.7 Fluxo de Entrega (após ReadyForPickup)

```
Delivery:
  POST /api/restaurante/delivery-orders/{id}/rider
  Body: { name, phone }   ← atribuir entregador

  PATCH /api/restaurante/delivery-orders/{id}/status
  Body: { status: "OutForDelivery" }
  → DeliveryOrder.Status → OutForDelivery (somente Delivery)

  PATCH /api/restaurante/delivery-orders/{id}/status
  Body: { status: "Delivered" }
  → DeliveryOrder.Status → Delivered

Takeaway (retirada):
  PATCH /api/restaurante/delivery-orders/{id}/status
  Body: { status: "Delivered" }
  → DeliveryOrder.Status → Delivered (direto de ReadyForPickup)
```

### 5.8 Fechamento e Pagamento de Mesa

```
1. Garçom solicita conta
   POST /api/restaurante/orders/{id}/close
   → RestOrder.Status → Closed
   → Sale criado em Draft com itens snapshot
   → Mesa permanece Occupied (cliente ainda está)

2. Garçom confirma pagamento
   POST /api/restaurante/orders/{id}/pay
   Body: { paymentMethod, amount }
   → SaleService.ConfirmAsync → Sale.Status → Paid
   → StockMovement RecipeOutput para ingredientes
   → RestOrder.Status → Paid
   → Mesa → Available
```

---

## 6. API — Referência de Endpoints

### Endpoints Públicos (sem autenticação)

| Método | Rota | Descrição |
|---|---|---|
| `GET` | `/api/public/menu/{slug}` | Cardápio público do restaurante |
| `GET` | `/api/public/orders/{token}` | Rastreamento de pedido por token |
| `POST` | `/api/public/orders` | Criar pedido via portal |

### Delivery Hub (requer autenticação + módulo `restaurante`)

| Método | Rota | Descrição |
|---|---|---|
| `GET` | `/api/restaurante/delivery-orders` | Listar pedidos externos (filtros: status, channel, date) |
| `GET` | `/api/restaurante/delivery-orders/{id}` | Detalhe do pedido |
| `POST` | `/api/restaurante/delivery-orders` | Criar pedido genérico (integrações) |
| `POST` | `/api/restaurante/delivery-orders/manual` | Criar pedido manual (operador) |
| `POST` | `/api/restaurante/delivery-orders/{id}/accept` | Aceitar → cria RestOrder |
| `POST` | `/api/restaurante/delivery-orders/{id}/reject` | Rejeitar |
| `PATCH` | `/api/restaurante/delivery-orders/{id}/status` | Avançar status (OutForDelivery, Delivered) |
| `POST` | `/api/restaurante/delivery-orders/{id}/rider` | Atribuir entregador |
| `POST` | `/api/restaurante/delivery-orders/{id}/cancel` | Cancelar |

### Operação (requer autenticação + módulo `restaurante`)

| Método | Rota | Descrição |
|---|---|---|
| `GET` | `/api/restaurante/orders` | Listar comandas |
| `GET` | `/api/restaurante/orders/{id}` | Detalhe da comanda |
| `POST` | `/api/restaurante/orders` | Abrir comanda (mesa → Occupied) |
| `POST` | `/api/restaurante/orders/{id}/items` | Adicionar item |
| `PATCH` | `/api/restaurante/orders/{id}/items/{itemId}/status` | Status do item (Preparing, Ready, Delivered) |
| `DELETE` | `/api/restaurante/orders/{id}/items/{itemId}` | Cancelar item |
| `POST` | `/api/restaurante/orders/{id}/close` | Fechar comanda (gera Sale Draft) |
| `POST` | `/api/restaurante/orders/{id}/pay` | Pagar (confirma Sale, libera mesa) |
| `POST` | `/api/restaurante/orders/{id}/cancel` | Cancelar comanda |

### Configuração e Estrutura

| Método | Rota | Descrição |
|---|---|---|
| `GET/PUT` | `/api/restaurante/settings` | Configurações operacionais |
| `PUT` | `/api/restaurante/settings/portal` | Configurações do portal |
| `GET/POST/PUT` | `/api/restaurante/areas` | Áreas do salão |
| `GET/POST/PUT/PATCH` | `/api/restaurante/tables` | Mesas |
| `GET/{id}/orders` | `/api/restaurante/tables/{id}/orders` | Comandas da mesa |
| `GET/POST/PUT` | `/api/restaurante/modifier-groups` | Grupos de modificadores |
| `GET/POST/PUT/DELETE` | `/api/restaurante/recipe-cards` | Fichas técnicas |
| `GET` | `/api/restaurante/reports/summary` | Relatório do restaurante |
| `PATCH` | `/api/stores/{id}/public-slug` | Definir slug do portal |

---

## 7. Frontend — Mapa de Rotas e Estado de Conexão

| Rota | Componente | Estado |
|---|---|---|
| `/restaurante` | `FloorPage` | ✅ conectado ao backend |
| `/restaurante/mesa/:tableId` | `OrderPage` | ✅ conectado ao backend |
| `/restaurante/comanda/:orderId` | `OrderPage` | ✅ conectado ao backend |
| `/restaurante/delivery` | `DeliveryPage` | ✅ conectado ao backend |
| `/restaurante/cozinha` | `KitchenPage` | ✅ conectado ao backend |
| `/restaurante/configurar` | `RestauranteSetupPage` | ✅ conectado ao backend |
| `/restaurante/relatorios` | `RelatoriosPage` | ✅ conectado ao backend |
| `/menu/:slug` | `PortalMenuPage` | ✅ conectado ao backend (público) |
| `/rastrear/:token` | `PortalTrackingPage` | ✅ conectado ao backend (público) |

---

## 8. Cobertura de Testes

### `DeliveryPortalFlowTests` (14 testes E2E) — ✅ todos passando

| Teste | O que valida |
|---|---|
| `PublicMenu_Returns200_WithVisibleProducts` | Cardápio público retorna produtos visíveis |
| `PublicMenu_InvisibleProduct_DoesNotAppear` | `IsMenuVisible=false` exclui produto |
| `PublicMenu_InvalidSlug_Returns404` | Slug inexistente retorna 404 |
| `PublicMenu_ClosedRestaurant_AcceptingOrdersFalse` | Flag `AcceptingOrders=false` refletida |
| `CreatePortalOrder_Delivery_CreatesDeliveryOrder` | Delivery cria `RestDeliveryOrder`, não `RestOrder` |
| `CreatePortalOrder_Takeaway_CreatesDeliveryOrder_NotRestOrder` | Takeaway idem |
| `CreatePortalOrder_WithRequiredModifier_Accepted` | Modificador obrigatório preenchido → aceito |
| `AcceptDeliveryOrder_CreatesRestOrder` | `AcceptAsync` cria `RestOrder` |
| `AcceptDeliveryOrder_LinkedRestOrder_AppearsInKitchen` | `RestOrder` aceito aparece no KDS |
| `FullStatusFlow_DeliveryOrder_Takeaway` | Fluxo completo: Received → Accepted → (kitchen: Preparing→Ready) → ReadyForPickup → Delivered |
| `Tracking_Returns_StatusLabel_WithoutSensitiveFields` | Tracking público não expõe dados sensíveis |
| `CreatePortalOrder_WhenNotAcceptingOrders_IsRejected` | `AcceptingOrders=false` → 422 |
| `CreatePortalOrder_DeliveryDisabled_DeliveryOrderIsRejected` | `DeliveryEnabled=false` → 422 |
| `CreatePortalOrder_TakeawayDisabled_TakeawayOrderIsRejected` | `TakeawayEnabled=false` → 422 |

### `RestauranteFlowTests` — ✅ passando

Cobre: ciclo de vida de mesas, integração com CORE (Sale), fichas técnicas + dedução de estoque, prevenção de comanda duplicada (SELECT FOR UPDATE + partial index).

---

## 9. Decisões Arquiteturais (Fechadas)

### `RestDeliveryOrder` como inbox obrigatório

Todo canal externo passa pelo inbox. Garante auditabilidade completa, permite rejeição sem poluir a operação, e isola o operador das particularidades de cada canal. Não há exceção.

### `RestOrder` nasce exclusivamente via `AcceptAsync`

`AcceptAsync` é o único ponto de criação. Isso garante que a cozinha nunca vê pedidos não triados e que o operador tem controle total sobre o que entra na operação.

### Sincronização unidirecional

`RestOrder → RestDeliveryOrder` via `SyncFromRestOrderAsync`. A direção inversa não existe. O cliente rastreia via `RestDeliveryOrder`, o operador gerencia via `RestOrder`. Os dois evoluem de forma independente, comunicando-se apenas nesta direção.

### Preços no `RestDeliveryOrder` são confiáveis

O backend recalcula todos os preços a partir do catálogo. O cliente nunca envia valores monetários que o backend confia. Modificadores são validados contra o catálogo, não contra o que o cliente enviou.

### `deliveryAddressJson` é `jsonb`

O campo armazena um objeto JSON com os campos do endereço. Strings planas são rejeitadas pelo PostgreSQL. O frontend serializa o endereço como objeto JSON antes de enviar.

### Taxa de entrega fixa em 0

Não há configuração de taxa de entrega por loja ainda. O campo existe e o backend loga um warning. Implementar `FoodServiceSettings.DeliveryFee` é uma evolução planejada, não uma omissão.

### Portal público sem autenticação

Endpoints `GET /api/public/menu/{slug}`, `GET /api/public/orders/{token}`, `POST /api/public/orders` não requerem JWT. O `IgnoreQueryFilters()` é usado explicitamente nestas queries para contornar os EF query filters de tenant/store. Isso é intencional.

---

## 10. Roadmap

### Concluído

- ✅ Módulo restaurante: mesas, áreas, comandas, KDS, fechamento, pagamento
- ✅ Fichas técnicas com dedução de estoque via `RecipeOutput`
- ✅ Delivery Hub: inbox multicanal, aceite, rejeição, atribuição de entregador
- ✅ Portal público: cardápio, carrinho, pedido, rastreamento
- ✅ Sincronização bidirecional RestOrder ↔ DeliveryOrder (KDS → status de entrega)
- ✅ Grupos de modificadores com validação de obrigatoriedade e `maxSelections`
- ✅ Flags de controle do portal (`AcceptingOrders`, `DeliveryEnabled`, `TakeawayEnabled`)
- ✅ 14 testes E2E do portal + testes E2E do restaurante

### Próximo (Varejo — Fase 2 PDV)

- [ ] PDV: busca/scan de produto, carrinho, resolução de preço, desconto, pagamento, finalização

### Backlog do Restaurante

**Hardening operacional:**
- [ ] Taxa de entrega configurável por loja (`FoodServiceSettings.DeliveryFee`)
- [ ] Horário de funcionamento com validação automática (`BusinessHoursJson` já existe, falta enforcement)
- [ ] Cancelamento de item de `RestDeliveryOrder` (partial cancel)
- [ ] Notificação por WhatsApp ao cliente (status changes)

**Integrações externas:**
- [ ] Webhook iFood (channel `IFood` já existe no enum, falta o receiver)
- [ ] Webhook Rappi
- [ ] Webhook AnotaAí

**Experiência do portal:**
- [ ] Estimativa de tempo configurável por tipo de pedido
- [ ] Histórico de pedidos do cliente (requer autenticação no portal ou link por CPF/telefone)
- [ ] Avaliação pós-entrega

**Pagamento online:**
- [ ] Integração com gateway de pagamento para pedidos do portal
- [ ] Pix gerado automaticamente no checkout

---

## 11. Diagrama de Dependência entre Entidades

```
Store
  ├── FoodServiceSettings       (1:1 por store)
  ├── RestArea[]                (áreas do salão)
  │     └── RestTable[]         (mesas)
  │           └── RestOrder     (comanda aberta)
  ├── RestOrder[]               (todas as comandas)
  │     └── RestOrderItem[]     (itens com status de preparo)
  │           └── RestOrderItemModifier[]
  └── RestDeliveryOrder[]       (inbox de pedidos externos)
        └── RestDeliveryOrderItem[]
              └── RestDeliveryOrderItemModifier[]

Product
  ├── ModifierGroup[]           (grupos de modificadores)
  │     └── Modifier[]          (opções)
  └── RecipeCard               (ficha técnica)
        └── RecipeIngredient[]  (ingredientes)

RestOrder ──────────────────────► Sale (CORE)
                 SaleId FK

RestDeliveryOrder ──────────────► RestOrder
                  RestOrderId FK (null até AcceptAsync)
```

---

*Este documento é gerado a partir do estado real do código. Qualquer divergência entre este documento e o código — o código vence.*
