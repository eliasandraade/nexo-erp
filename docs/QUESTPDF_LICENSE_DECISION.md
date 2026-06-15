# QuestPDF License Decision

**Date:** 2026-06-15
**Decision:** Use QuestPDF Community License

## License Used

QuestPDF Community License (free tier)

## Reason

QuestPDF Community License is free for organizations with annual gross revenue under $1M USD.
Orken is currently below this threshold, making the community license applicable.

## Library

- Package: `QuestPDF`
- Version: `2026.6.0`
- NuGet: https://www.nuget.org/packages/QuestPDF

## Conditions

The Community License allows:
- Commercial use
- Full feature set
- No time limit

## Upgrade Condition

A Andrade Systems / Orken deverá migrar para uma licença paga do QuestPDF assim que a receita
bruta anual da organização **ultrapassar US$1M USD**.

**Obrigações:**
- Ao atingir o limite, não é permitido continuar usando a Community License
- A migração deve ser feita antes de continuar operando em produção com QuestPDF
- Consultar a **pricing page oficial vigente no momento da contratação** para verificar os planos e
  valores atuais — os preços não devem ser tratados como fixos neste documento

**Ação requerida ao ultrapassar o limite:**
1. Acessar https://www.questpdf.com/license/ para verificar os planos disponíveis e preços atuais
2. Adquirir a licença adequada ao porte da organização naquele momento
3. Atualizar `QuestPDF.Settings.License` em `QuestPdfRenderer.cs` para o tipo correspondente
4. Atualizar este documento com a nova licença, data e valor contratado

## License Configuration in Code

The license type is set in application startup via:
```csharp
// nexo-backend/src/Nexo.Infrastructure/Integrations/Pdf/QuestPdfRenderer.cs
QuestPDF.Settings.License = LicenseType.Community;
```

This is called once at DI registration time.

## References

- Official license page: https://www.questpdf.com/license/
- Community license FAQ: https://www.questpdf.com/license/configuration.html
- QuestPDF GitHub: https://github.com/QuestPDF/QuestPDF
