# NEXO — Development Guide

Nexo is a desktop ERP system for small and medium businesses developed by Andrade Systems.

Slogan:
"Gestão inteligente para empresas reais."

The goal of Nexo is to provide a centralized system for operational control and business insights.

---

# Architecture Principles

Nexo follows a modular architecture.

Each module is isolated and organized as:

modules/moduleName

components  
pages  
services  
types  
data  

Services act as the data access layer.

Currently services use mock data.

Later they will connect to a .NET backend.

---

# Tech Stack

Frontend:

React  
TypeScript  
Vite  
TailwindCSS  
shadcn/ui  
Lucide Icons  
React Router  
TanStack Query  

Future:

Electron desktop wrapper  
.NET backend  

---

# Product Philosophy

Nexo is designed to be:

• fast
• reliable
• simple
• operational

It must replace multiple fragmented tools used by small businesses.

The interface should always prioritize operational clarity.

---

# Core Modules

Dashboard  
Products  
Inventory  
Customers  
Users & Permissions  
Cash  
POS  
Reports  
Insights  
Audit  

---

## Planning First

For critical modules such as Cash (Caixa) and POS (PDV), always begin in planning mode before coding.

First:
- inspect the repository
- understand the existing architecture
- identify reuse opportunities
- identify risky coupling or weak abstractions
- ask only high-value clarifying questions if necessary

If AskUserQuestionTool is available, use it during planning for unresolved product or architecture decisions.

Do not jump directly into implementation for sensitive transactional modules.

---

## POS Architecture Guidelines

POS is the most critical operational module in Nexo.

It must be:
- fast
- keyboard-friendly
- barcode-scanner-friendly
- simple under pressure
- modular internally

Avoid building POS as one large UI component.

Split responsibilities conceptually into:
- session/screen state
- cart logic
- product lookup
- checkout/payment flow
- transaction completion

The POS MVP should include:
- product scan/search
- cart operations
- totals
- discounts
- payment method selection
- finalize sale
- success/reset flow

Do not overbuild advanced fiscal or TEF features yet.

---

## Cash Architecture Guidelines

Cash is a sensitive operational module and must emphasize:
- clarity
- accountability
- session-based control
- divergence visibility
- auditability

Cash must support:
- opening
- movements
- sangria
- suprimento
- closing
- expected vs counted balance

Keep it practical and operational, not accounting-heavy.

---

# User Roles

Diretoria  
Gerente  
Vendedor  
Estoquista  

Permissions are managed through role presets.

---

# Critical Modules

Cash (Caixa)  
POS (PDV)

These modules must be treated carefully because they handle financial transactions.

---

# Inventory Rules

Inventory supports:

adjustments  
transfers  
movements  
alerts  

Stock changes must generate movement records.

---

# Cash Rules

Cash sessions include:

opening  
movements  
withdrawals  
reinforcements  
closing  
difference detection  

All operations must generate logs.

---

# POS Rules

The POS must support:

barcode scanning  
cart management  
fast checkout  
multiple payment methods  

Sales must update:

inventory  
cash  
sales history  

---

# Coding Guidelines

Keep code simple and maintainable.

Avoid unnecessary abstractions.

Reuse shared components.

Follow the module architecture strictly.

Services must encapsulate data access.

UI components must not directly manipulate data sources.

---

# Refactoring

Keep the repository clean and production-ready.

---

# Future Backend

The backend will be implemented in .NET.

Service methods should be written in a way that allows easy API integration later.

---

# Goal

Build a reliable, fast, and scalable ERP for small and medium businesses.

---

## Design Context (Impeccable)

> Loaded from `.impeccable.md`. Keep in sync when updated.

**Product:** Orken — sistema operacional empresarial modular, multi-tenant, para negócios reais brasileiros. Não é ERP corporativo. É a camada de controle entre o caos da operação e a decisão do dono.

**Users:** donos de restaurantes, bares, lojas e negócios locais. Operam sob pressão, com pouco tempo, sem equipe de TI. Job: "quero saber se estou lucrando sem montar planilha."

**Brand personality:** moderno, inteligente, eficiente. Voz direta, operacional, sem jargão. Verbos de ação, não substantivos abstratos.

**References:** Stripe (hierarquia, copy precisa), Loom (produto mostrado em uso), Notion (modularidade simples).

**Anti-references:** SAP/Totvs (frio, burocrático), SaaS genérico americano (hero genérico, social proof irrelevante), startup hype (buzzword de IA sem substância).

**Aesthetic:** dark mode definitivo (navy/indigo). Tipografia dominante (Inter bold). Espaço generoso. Cor indigo reservada para ação. Screenshots reais do produto como evidência.

**Design Principles:**
1. Operacional primeiro — mostrar o que muda na operação, não o que o sistema é
2. Inteligência visível — automação como resultado tangível, não promessa de feature
3. Premium acessível — visual sofisticado, linguagem de pessoa real
4. Hierarquia sem ansiedade — Core em 10s, módulos verticais como "tem mais se precisar"
5. Evidência, não abstração — screenshots, números reais, fluxos concretos

**Modules:** Core (PDV, estoque, caixa) = destaque principal. Orken Menu (restaurante/delivery) e Orken Build (obras/despesas) = seções próprias mas secundárias.

**AI/Intelligence:** diferencial principal. Fluxo: texto curto → OCR → interpretação → sugestão → confirmação. Narrativa: "Pagou? Já está registrado." Não usar as palavras "IA", "machine learning". Mostrar o resultado.