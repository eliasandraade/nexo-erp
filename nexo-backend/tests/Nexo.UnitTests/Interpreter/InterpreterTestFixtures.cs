using Nexo.Application.Features.Financial;
using Nexo.Domain.Entities;
using Nexo.Domain.Enums;
using Nexo.Application.Common.Interfaces;
using Nexo.Application.Modules.Interpreter.Interfaces;
using Nexo.Domain.Modules.Interpreter;
using NSubstitute;

namespace Nexo.UnitTests.Interpreter;

/// <summary>
/// Shared fixtures and builder helpers used across Interpreter use case tests.
/// </summary>
internal static class F
{
    internal static readonly Guid TenantId = Guid.Parse("11111111-0000-0000-0000-000000000001");
    internal static readonly Guid UserId   = Guid.Parse("22222222-0000-0000-0000-000000000002");
    internal static readonly Guid MovId    = Guid.Parse("33333333-0000-0000-0000-000000000003");
    internal static readonly Guid SugId    = Guid.Parse("44444444-0000-0000-0000-000000000004");

    /// <summary>The livro posting with a financeiro that has the default accounts and no prior lançamentos.</summary>
    internal static VerticalLedgerPostingService Ledger(IFinancialRepository? financial = null)
    {
        var fin = financial ?? Substitute.For<IFinancialRepository>();
        fin.GetDefaultByTypeAsync(Arg.Any<FinancialAccountType>(), Arg.Any<CancellationToken>())
            .Returns(ci => FinancialAccount.Create(TenantId, "x", "Conta", ci.Arg<FinancialAccountType>()));
        fin.GetTransactionsByReferenceAsync(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<FinancialTransaction>());
        var store = Substitute.For<ICurrentStore>();
        return new VerticalLedgerPostingService(fin, CurrentTenant(), store, CurrentUser());
    }

    // ── Domain entity builders ────────────────────────────────────────────────

    internal static FinancialMovement BuildDraftMovement(
        Guid?   movementId = null,
        decimal amount     = 100m,
        string  description = "Test movement")
    {
        var m = FinancialMovement.CreateDraft(
            tenantId:              TenantId,
            createdBy:             UserId,
            direction:             MovementDirection.Out,
            nature:                MovementNature.Expense,
            amount:                amount,
            date:                  DateOnly.FromDateTime(DateTime.UtcNow),
            description:           description,
            normalizedDescription: description.ToLowerInvariant(),
            contextType:           FinancialContextType.Obra,
            contextId:             null,
            categoryId:            null,
            accountId:             null);

        return m;
    }

    internal static InterpretationSuggestion BuildSuggestion(Guid movementId)
        => InterpretationSuggestion.Create(
            tenantId:      TenantId,
            movementId:    movementId,
            direction:     MovementDirection.Out,     directionSource: SuggestionSource.RuleEngine,
            nature:        MovementNature.Expense,    natureSource:    SuggestionSource.RuleEngine,
            categoryId:    null,                      categorySource:  SuggestionSource.RuleEngine,
            contextType:   null,                      contextId:       null,
            contextSource: SuggestionSource.RuleEngine,
            accountId:     null,                      accountSource:   SuggestionSource.RuleEngine);

    internal static ExtractionResult BuildExtraction(Guid movementId) =>
        ExtractionResult.Create(
            TenantId, movementId, InputSourceType.Text, "raw text",
            BuildAnalysisOutput(100m, DateOnly.FromDateTime(DateTime.UtcNow)));

    internal static AnalysisOutput BuildAnalysisOutput(decimal? amount = null, DateOnly? date = null) =>
        new(
            Amount:              ExtractedField<decimal?>.From(amount, amount.HasValue ? 0.92f : 0f, AnalyzerProvider.RuleBased),
            Date:                ExtractedField<DateOnly?>.From(date, date.HasValue ? 0.92f : 0f, AnalyzerProvider.RuleBased),
            Payee:               ExtractedField<string?>.Unknown(),
            Account:             ExtractedField<string?>.Unknown(),
            RawProviderResponse: "{}",
            Prompt:              new PromptMetadata("rule-based", "1.0.0", "abc123"));

    internal static InterpretationOutput BuildInterpretationOutput() =>
        new(
            Direction:    MovementDirection.Out,    DirectionSource: SuggestionSource.RuleEngine,
            Nature:       MovementNature.Expense,   NatureSource:    SuggestionSource.RuleEngine,
            CategoryId:   null,                     CategorySource:  SuggestionSource.RuleEngine,
            ContextType:  null,                     ContextId:       null,
            ContextSource: SuggestionSource.RuleEngine,
            AccountId:    null,                     AccountSource:   SuggestionSource.RuleEngine);

    // ── Mock infrastructure ────────────────────────────────────────────────────

    internal static ICurrentTenant CurrentTenant()
    {
        var t = Substitute.For<ICurrentTenant>();
        t.Id.Returns(TenantId);
        t.IsResolved.Returns(true);
        return t;
    }

    internal static ICurrentUser CurrentUser()
    {
        var u = Substitute.For<ICurrentUser>();
        u.UserId.Returns(UserId);
        u.TenantId.Returns(TenantId);
        return u;
    }

    /// <summary>
    /// Configures IUnitOfWork to execute the Func immediately (no real transaction).
    /// This allows use case transaction bodies to run inside unit tests.
    /// </summary>
    internal static IUnitOfWork UnitOfWork()
    {
        var uow = Substitute.For<IUnitOfWork>();
        uow.ExecuteInTransactionAsync(
                Arg.Any<Func<CancellationToken, Task>>(),
                Arg.Any<CancellationToken>())
           .Returns(callInfo =>
                callInfo.Arg<Func<CancellationToken, Task>>()(CancellationToken.None));
        return uow;
    }
}
