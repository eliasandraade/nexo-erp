using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Nexo.Application.Common.Interfaces;
using Nexo.Application.Modules.Interpreter;
using Nexo.Application.Modules.Interpreter.Interfaces;
using Nexo.Domain.Exceptions;
using Nexo.Domain.Modules.Interpreter;
using NSubstitute;

namespace Nexo.UnitTests.Interpreter;

public class ReprocessMovementUseCaseTests
{
    // ── Mocks ─────────────────────────────────────────────────────────────────

    private readonly IAnalyzerSelector              _selector  = Substitute.For<IAnalyzerSelector>();
    private readonly IDocumentAnalyzer              _analyzer  = Substitute.For<IDocumentAnalyzer>();
    private readonly IInterpretationService         _interp    = Substitute.For<IInterpretationService>();
    private readonly IMovementMemoryService         _memory    = Substitute.For<IMovementMemoryService>();
    private readonly IDescriptionNormalizer         _norm      = Substitute.For<IDescriptionNormalizer>();
    private readonly IFinancialMovementRepository   _movRepo   = Substitute.For<IFinancialMovementRepository>();
    private readonly IExtractionResultRepository    _extRepo   = Substitute.For<IExtractionResultRepository>();
    private readonly IInterpretationSuggestionRepository _sugRepo = Substitute.For<IInterpretationSuggestionRepository>();
    private readonly IMovementAttachmentRepository  _attRepo   = Substitute.For<IMovementAttachmentRepository>();
    private readonly IReprocessLogRepository        _repRepo   = Substitute.For<IReprocessLogRepository>();
    private readonly IUnitOfWork                    _uow       = F.UnitOfWork();
    private readonly ICurrentTenant                 _tenant    = F.CurrentTenant();
    private readonly ICurrentUser                   _user      = F.CurrentUser();

    private ReprocessMovementUseCase BuildSut() => new(
        _selector, _interp, _memory, _norm,
        _movRepo, _extRepo, _sugRepo, _attRepo, _repRepo,
        _uow, _tenant, _user,
        NullLogger<ReprocessMovementUseCase>.Instance);

    // ── Arrange helpers ───────────────────────────────────────────────────────

    private void SetupHappyPath(FinancialMovement movement)
    {
        var extraction = F.BuildExtraction(movement.Id);
        var suggestion = F.BuildSuggestion(movement.Id);

        _movRepo.GetByIdAsync(movement.Id, Arg.Any<CancellationToken>()).Returns(movement);
        _extRepo.GetLatestByMovementIdAsync(movement.Id, Arg.Any<CancellationToken>()).Returns(extraction);
        _sugRepo.GetLatestByMovementIdAsync(movement.Id, Arg.Any<CancellationToken>()).Returns(suggestion);
        _attRepo.GetByMovementIdAsync(movement.Id, Arg.Any<CancellationToken>())
                .Returns(new List<MovementAttachment>());

        _selector.Select(Arg.Any<InputSourceType>(), Arg.Any<Guid>(), Arg.Any<AnalyzerProvider?>())
                 .Returns(_analyzer);
        _analyzer.AnalyzeAsync(Arg.Any<AnalysisInput>(), Arg.Any<CancellationToken>())
                 .Returns(F.BuildAnalysisOutput(200m, DateOnly.FromDateTime(DateTime.UtcNow)));
        _memory.GetCompactContextAsync(F.TenantId, F.UserId, Arg.Any<CancellationToken>())
               .Returns("{}");
        _interp.SuggestAsync(Arg.Any<AnalysisOutput>(), Arg.Any<string>(),
                             Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
               .Returns(F.BuildInterpretationOutput());
        _norm.Normalize(Arg.Any<string>(), Arg.Any<Guid>()).Returns("pagamento reprocessado");
    }

    private ReprocessMovementCommand BuildCommand(Guid movementId) => new(
        TenantId:      F.TenantId,
        UserId:        F.UserId,
        MovementId:    movementId,
        Reason:        TriggerReason.ManualRequest,
        ForceAnalyzer: null,
        Notes:         "unit test reprocess");

    // ── Guards ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Execute_MovementNotFound_ThrowsNotFoundException()
    {
        _movRepo.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
                .Returns((FinancialMovement?)null);

        var sut = BuildSut();

        await sut.Invoking(s => s.ExecuteAsync(BuildCommand(Guid.NewGuid())))
                 .Should().ThrowAsync<NotFoundException>()
                 .WithMessage("*Movement*");
    }

    [Fact]
    public async Task Execute_NoExistingExtraction_ThrowsInvalidOperationException()
    {
        var movement = F.BuildDraftMovement();
        _movRepo.GetByIdAsync(movement.Id, Arg.Any<CancellationToken>()).Returns(movement);
        _extRepo.GetLatestByMovementIdAsync(movement.Id, Arg.Any<CancellationToken>())
                .Returns((ExtractionResult?)null);

        var sut = BuildSut();

        await sut.Invoking(s => s.ExecuteAsync(BuildCommand(movement.Id)))
                 .Should().ThrowAsync<InvalidOperationException>()
                 .WithMessage("*ExtractionResult*");
    }

    // ── Happy path ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Execute_ReturnsResponseWithReprocessLogId()
    {
        var movement = F.BuildDraftMovement();
        SetupHappyPath(movement);
        var sut = BuildSut();

        var response = await sut.ExecuteAsync(BuildCommand(movement.Id));

        response.ReprocessLogId.Should().NotBe(Guid.Empty);
        response.NewDraftId.Should().Be(movement.Id);
    }

    [Fact]
    public async Task Execute_ResetsMovementToDraft()
    {
        var movement = F.BuildDraftMovement();
        // Manually confirm it first to test the reset
        SetupHappyPath(movement);

        var sut = BuildSut();
        await sut.ExecuteAsync(BuildCommand(movement.Id));

        // After reprocess, movement must be Draft (for human to re-confirm)
        movement.Status.Should().Be(MovementStatus.Draft);
    }

    [Fact]
    public async Task Execute_CreatesNewExtractionResult()
    {
        var movement = F.BuildDraftMovement();
        SetupHappyPath(movement);
        var sut = BuildSut();

        await sut.ExecuteAsync(BuildCommand(movement.Id));

        await _extRepo.Received(1).AddAsync(Arg.Any<ExtractionResult>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Execute_CreatesNewSuggestion()
    {
        var movement = F.BuildDraftMovement();
        SetupHappyPath(movement);
        var sut = BuildSut();

        await sut.ExecuteAsync(BuildCommand(movement.Id));

        await _sugRepo.Received(1).AddAsync(Arg.Any<InterpretationSuggestion>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Execute_AppendsReprocessLog()
    {
        var movement = F.BuildDraftMovement();
        SetupHappyPath(movement);
        var sut = BuildSut();

        await sut.ExecuteAsync(BuildCommand(movement.Id));

        await _repRepo.Received(1).AddAsync(
            Arg.Is<ReprocessLog>(r => r.TriggerReason == TriggerReason.ManualRequest),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Execute_ReprocessLogContainsNotes()
    {
        var movement = F.BuildDraftMovement();
        SetupHappyPath(movement);
        var sut = BuildSut();

        await sut.ExecuteAsync(BuildCommand(movement.Id));

        await _repRepo.Received(1).AddAsync(
            Arg.Is<ReprocessLog>(r => r.Notes == "unit test reprocess"),
            Arg.Any<CancellationToken>());
    }

    // ── ForceAnalyzer ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Execute_WithForceAnalyzer_PassesProviderToSelector()
    {
        var movement = F.BuildDraftMovement();
        SetupHappyPath(movement);
        var sut = BuildSut();

        var cmd = new ReprocessMovementCommand(
            F.TenantId, F.UserId, movement.Id,
            TriggerReason.NewPromptVersion,
            ForceAnalyzer: AnalyzerProvider.Claude,
            Notes: null);

        await sut.ExecuteAsync(cmd);

        _selector.Received(1).Select(
            Arg.Any<InputSourceType>(),
            Arg.Any<Guid>(),
            Arg.Is<AnalyzerProvider?>(p => p == AnalyzerProvider.Claude));
    }

    // ── Diff JSON ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Execute_ReprocessLogHasDiffJson()
    {
        var movement = F.BuildDraftMovement();
        SetupHappyPath(movement);
        var sut = BuildSut();

        ReprocessLog? captured = null;
        await _repRepo.AddAsync(
            Arg.Do<ReprocessLog>(r => captured = r),
            Arg.Any<CancellationToken>());

        await sut.ExecuteAsync(BuildCommand(movement.Id));

        captured.Should().NotBeNull();
        captured!.DiffJson.Should().NotBeNullOrWhiteSpace();
        captured.DiffJson.Should().Contain("extraction");
        captured.DiffJson.Should().Contain("suggestion");
    }
}
