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

When Orken's annual gross revenue **reaches or exceeds $1M USD**, the license must be upgraded to
one of the paid tiers:
- Professional License ($299/year per developer) — for organizations up to $5M revenue
- Enterprise License ($599/year per developer) — for organizations above $5M revenue

Action required at that point: contact QuestPDF at https://www.questpdf.com/license/ and update
the license type in `QuestPdfRenderer` from `LicenseType.Community` to the appropriate tier.

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
