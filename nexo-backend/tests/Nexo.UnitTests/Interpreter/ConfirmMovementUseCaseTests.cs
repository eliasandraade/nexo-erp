using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Nexo.Application.Common.Interfaces;
using Nexo.Application.Modules.Interpreter;
using Nexo.Application.Modules.Interpreter.Interfaces;
using Nexo.Domain.Exceptions;
using Nexo.Domain.Modules.Interpreter;
using NSubstitute;

namespace Nexo.UnitTests.Interpreter;

public class ConfirmMovementUseCaseTests
{
    // ── Mocks ─────────────────────────────────────────────────────────────────

    private readonly IFinancialMovementRepository        _movRepo  = Substitute.For<IFinancialMovementRepository>();
    private readonly IInterpretationSuggestionRepository _sugRepo  = Substitute.For<IInterpretationSuggestionRepository>();
    private readonly IUserCorrectionRepository           _corRepo  = Substitute.For<IUserCorrectionRepository>();
    private readonly IMovementAuditLogRepository         _audRepo  = Substitute.For<IMovementAuditLogRepository>();
    private readonly IDescriptionNormalizer              _norm     = Substitute.For<IDescriptionNormalizer>();
    private readonly IMovementMemoryService              _memory   = Substitute.For<IMovementMemoryService>();
    private readonly IUnitOfWork                         _uow      = F.UnitOfWork();
    private readonly ICurrentTenant                      _tenant   = F.CurrentTenant();
    private readonly ICurrentUser                        _user     = F.CurrentUser();

    private ConfirmMovementUseCase BuildSut() => new(
        _movRepo, _sugRepo, _corRepo, _audRepo,
        _norm, _memory, _uow, _tenant, _user,
        NullLogger<ConfirmMovementUseCase>.Instance);

    // ── Helpers ───────────────────────────────────────────────────────────────

    private ConfirmMovementCommand BuildCommand(
        Guid                  draftId,
        Guid                  suggestionId,
        MovementDirection     direction   = MovementDirection.Out,
        MovementNature        nature      = MovementNature.Expense,
        FinancialContextType  contextType = FinancialContextType.Obra,
        Guid?                 categoryId  = null,
        Guid?                 contextId   = null)
        => new(
            TenantId:            F.TenantId,
            UserId:              F.UserId,
            DraftId:             draftId,
            Amount:              150m,
            Date:                new DateOnly(2026, 5, 7),
            Description:         "Pagamento teste",
            Direction:           direction,
            Nature:              nature,
            CategoryId:          categoryId,
            ContextType:         contextType,
            ContextId:           contextId,
            AccountId:           null,
            OriginalSuggestionId: suggestionId);

    // ── Guard: draft not found ────────────────────────────────────────────────

    [Fact]
    public async Task Execute_DraftNotFound_ThrowsNotFoundException()
    {
        _movRepo.GetDraftByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
                .Returns((FinancialMovement?)null);

        var sut = BuildSut();

        await sut.Invoking(s => s.ExecuteAsync(BuildCommand(Guid.NewGuid(), Guid.NewGuid())))
                 .Should().ThrowAsync<NotFoundException>()
                 .WithMessage("*Draft movement*");
    }

    // ── Guard: suggestion not found ───────────────────────────────────────────

    [Fact]
    public async Task Execute_SuggestionNotFound_ThrowsNotFoundException()
    {
        var draft = F.BuildDraftMovement();
        _movRepo.GetDraftByIdAsync(draft.Id, Arg.Any<CancellationToken>())
                .Returns(draft);
        _sugRepo.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
                .Returns((InterpretationSuggestion?)null);
        _norm.Normalize(Arg.Any<string>(), Arg.Any<Guid>()).Returns("normalized");

        var sut = BuildSut();

        await sut.Invoking(s => s.ExecuteAsync(BuildCommand(draft.Id, Guid.NewGuid())))
                 .Should().ThrowAsync<NotFoundException>()
                 .WithMessage("*InterpretationSuggestion*");
    }

    // ── Happy path: no corrections ───────────────────────────────────────────

    [Fact]
    public async Task Execute_NoCorrectionNeeded_SuggestionMarkedAccepted()
    {
        var draft      = F.BuildDraftMovement();
        var suggestion = F.BuildSuggestion(draft.Id);

        _movRepo.GetDraftByIdAsync(draft.Id, Arg.Any<CancellationToken>()).Returns(draft);
        _sugRepo.GetByIdAsync(suggestion.Id, Arg.Any<CancellationToken>()).Returns(suggestion);
        _norm.Normalize(Arg.Any<string>(), Arg.Any<Guid>()).Returns("pagamento teste");

        var sut = BuildSut();
        // Command matches the suggestion exactly (no corrections expected)
        var cmd = BuildCommand(draft.Id, suggestion.Id,
            direction: suggestion.SuggestedDirection,
            nature:    suggestion.SuggestedNature);

        var response = await sut.ExecuteAsync(cmd);

        response.Status.Should().Be("Confirmed");
        response.Corrections.Should().BeEmpty();
        suggestion.WasAccepted.Should().BeTrue();
    }

    // ── Happy path: with corrections ─────────────────────────────────────────

    [Fact]
    public async Task Execute_UserChangedDirection_RecordsCorrection()
    {
        var draft      = F.BuildDraftMovement();
        var suggestion = F.BuildSuggestion(draft.Id); // Direction = Out

        _movRepo.GetDraftByIdAsync(draft.Id, Arg.Any<CancellationToken>()).Returns(draft);
        _sugRepo.GetByIdAsync(suggestion.Id, Arg.Any<CancellationToken>()).Returns(suggestion);
        _norm.Normalize(Arg.Any<string>(), Arg.Any<Guid>()).Returns("pagamento teste");

        var sut = BuildSut();
        // User changes direction from Out → In
        var cmd = BuildCommand(draft.Id, suggestion.Id, direction: MovementDirection.In);

        var response = await sut.ExecuteAsync(cmd);

        response.Corrections.Should().ContainSingle(c => c.Field == "Direction");
        response.Corrections[0].Original.Should().Be("Out");
        response.Corrections[0].Corrected.Should().Be("In");
    }

    [Fact]
    public async Task Execute_UserChangedNature_RecordsCorrection()
    {
        var draft      = F.BuildDraftMovement();
        var suggestion = F.BuildSuggestion(draft.Id); // Nature = Expense

        _movRepo.GetDraftByIdAsync(draft.Id, Arg.Any<CancellationToken>()).Returns(draft);
        _sugRepo.GetByIdAsync(suggestion.Id, Arg.Any<CancellationToken>()).Returns(suggestion);
        _norm.Normalize(Arg.Any<string>(), Arg.Any<Guid>()).Returns("pagamento teste");

        var sut = BuildSut();
        var cmd = BuildCommand(draft.Id, suggestion.Id, nature: MovementNature.Transfer);

        var response = await sut.ExecuteAsync(cmd);

        response.Corrections.Should().ContainSingle(c => c.Field == "Nature");
    }

    [Fact]
    public async Task Execute_WithCorrections_SuggestionMarkedRejected()
    {
        var draft      = F.BuildDraftMovement();
        var suggestion = F.BuildSuggestion(draft.Id);

        _movRepo.GetDraftByIdAsync(draft.Id, Arg.Any<CancellationToken>()).Returns(draft);
        _sugRepo.GetByIdAsync(suggestion.Id, Arg.Any<CancellationToken>()).Returns(suggestion);
        _norm.Normalize(Arg.Any<string>(), Arg.Any<Guid>()).Returns("pagamento");

        var sut = BuildSut();
        var cmd = BuildCommand(draft.Id, suggestion.Id, direction: MovementDirection.In);

        await sut.ExecuteAsync(cmd);

        suggestion.WasAccepted.Should().BeFalse();
    }

    [Fact]
    public async Task Execute_WithCorrections_PersistsThemToRepository()
    {
        var draft      = F.BuildDraftMovement();
        var suggestion = F.BuildSuggestion(draft.Id);

        _movRepo.GetDraftByIdAsync(draft.Id, Arg.Any<CancellationToken>()).Returns(draft);
        _sugRepo.GetByIdAsync(suggestion.Id, Arg.Any<CancellationToken>()).Returns(suggestion);
        _norm.Normalize(Arg.Any<string>(), Arg.Any<Guid>()).Returns("pagamento");

        var sut = BuildSut();
        var cmd = BuildCommand(draft.Id, suggestion.Id, direction: MovementDirection.In);

        await sut.ExecuteAsync(cmd);

        await _corRepo.Received(1).AddRangeAsync(
            Arg.Is<IEnumerable<UserCorrection>>(list => list.Any()),
            Arg.Any<CancellationToken>());
    }

    // ── Audit log ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Execute_AlwaysCreatesAuditLog()
    {
        var draft      = F.BuildDraftMovement();
        var suggestion = F.BuildSuggestion(draft.Id);

        _movRepo.GetDraftByIdAsync(draft.Id, Arg.Any<CancellationToken>()).Returns(draft);
        _sugRepo.GetByIdAsync(suggestion.Id, Arg.Any<CancellationToken>()).Returns(suggestion);
        _norm.Normalize(Arg.Any<string>(), Arg.Any<Guid>()).Returns("pagamento");

        var sut = BuildSut();
        await sut.ExecuteAsync(BuildCommand(draft.Id, suggestion.Id));

        await _audRepo.Received(1).AddAsync(
            Arg.Is<MovementAuditLog>(l => l.Action == "Confirmed"),
            Arg.Any<CancellationToken>());
    }

    // ── Status transition ─────────────────────────────────────────────────────

    [Fact]
    public async Task Execute_MovementChangesFromDraftToConfirmed()
    {
        var draft      = F.BuildDraftMovement();
        var suggestion = F.BuildSuggestion(draft.Id);

        draft.Status.Should().Be(MovementStatus.Draft);

        _movRepo.GetDraftByIdAsync(draft.Id, Arg.Any<CancellationToken>()).Returns(draft);
        _sugRepo.GetByIdAsync(suggestion.Id, Arg.Any<CancellationToken>()).Returns(suggestion);
        _norm.Normalize(Arg.Any<string>(), Arg.Any<Guid>()).Returns("pagamento");

        var sut = BuildSut();
        await sut.ExecuteAsync(BuildCommand(draft.Id, suggestion.Id));

        draft.Status.Should().Be(MovementStatus.Confirmed);
    }

    // ── Memory rebuild ────────────────────────────────────────────────────────

    [Fact]
    public async Task Execute_TriggersMemoryRebuildAfterCommit()
    {
        var draft      = F.BuildDraftMovement();
        var suggestion = F.BuildSuggestion(draft.Id);

        _movRepo.GetDraftByIdAsync(draft.Id, Arg.Any<CancellationToken>()).Returns(draft);
        _sugRepo.GetByIdAsync(suggestion.Id, Arg.Any<CancellationToken>()).Returns(suggestion);
        _norm.Normalize(Arg.Any<string>(), Arg.Any<Guid>()).Returns("pagamento");

        // Ensure the fire-and-forget doesn't throw
        _memory.RebuildProfileAsync(Arg.Any<Guid>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
               .Returns(Task.CompletedTask);

        var sut = BuildSut();
        await sut.ExecuteAsync(BuildCommand(draft.Id, suggestion.Id));

        // Small delay to let fire-and-forget task start
        await Task.Delay(10);

        await _memory.Received().RebuildProfileAsync(F.TenantId, Arg.Any<Guid?>(), Arg.Any<CancellationToken>());
    }
}
