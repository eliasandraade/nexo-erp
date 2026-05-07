using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Nexo.Application.Common.Interfaces;
using Nexo.Application.Modules.Interpreter;
using Nexo.Application.Modules.Interpreter.Interfaces;
using Nexo.Domain.Exceptions;
using Nexo.Domain.Modules.Interpreter;
using NSubstitute;

namespace Nexo.UnitTests.Interpreter;

public class AnalyzeMovementUseCaseTests
{
    // ── Mocks ─────────────────────────────────────────────────────────────────

    private readonly IAnalyzerSelector              _selector   = Substitute.For<IAnalyzerSelector>();
    private readonly IDocumentAnalyzer              _analyzer   = Substitute.For<IDocumentAnalyzer>();
    private readonly IInterpretationService         _interp     = Substitute.For<IInterpretationService>();
    private readonly IMovementMemoryService         _memory     = Substitute.For<IMovementMemoryService>();
    private readonly IDescriptionNormalizer         _normalizer = Substitute.For<IDescriptionNormalizer>();
    private readonly IFinancialMovementRepository   _movRepo    = Substitute.For<IFinancialMovementRepository>();
    private readonly IExtractionResultRepository    _extRepo    = Substitute.For<IExtractionResultRepository>();
    private readonly IInterpretationSuggestionRepository _sugRepo = Substitute.For<IInterpretationSuggestionRepository>();
    private readonly IMovementAttachmentRepository  _attRepo    = Substitute.For<IMovementAttachmentRepository>();
    private readonly IUnitOfWork                    _uow        = F.UnitOfWork();
    private readonly ICurrentTenant                 _tenant     = F.CurrentTenant();
    private readonly ICurrentUser                   _user       = F.CurrentUser();

    private AnalyzeMovementUseCase BuildSut() => new(
        _selector, _interp, _memory, _normalizer,
        _movRepo, _extRepo, _sugRepo, _attRepo,
        _uow, _tenant, _user,
        NullLogger<AnalyzeMovementUseCase>.Instance);

    // ── Arrange helpers ───────────────────────────────────────────────────────

    private void SetupHappyPath(string? text = "Pagamento R$ 100,00")
    {
        _selector.Select(Arg.Any<InputSourceType>(), Arg.Any<Guid>(), Arg.Any<AnalyzerProvider?>())
                 .Returns(_analyzer);

        _analyzer.AnalyzeAsync(Arg.Any<AnalysisInput>(), Arg.Any<CancellationToken>())
                 .Returns(F.BuildAnalysisOutput(100m, DateOnly.FromDateTime(DateTime.UtcNow)));

        _memory.GetCompactContextAsync(F.TenantId, F.UserId, Arg.Any<CancellationToken>())
               .Returns("{}");

        _interp.SuggestAsync(Arg.Any<AnalysisOutput>(), Arg.Any<string>(),
                             Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
               .Returns(F.BuildInterpretationOutput());

        _normalizer.Normalize(Arg.Any<string>(), Arg.Any<Guid>())
                   .Returns("pagamento");
    }

    // ── Guard: text or attachment required ───────────────────────────────────

    [Fact]
    public async Task Execute_WithoutTextOrAttachment_ThrowsDomainException()
    {
        var sut = BuildSut();
        var cmd = new AnalyzeMovementCommand(
            F.TenantId, F.UserId,
            Text: null, AttachmentId: null,
            InputSource: InputSourceType.Text);

        await sut.Invoking(s => s.ExecuteAsync(cmd))
                 .Should().ThrowAsync<DomainException>();
    }

    // ── Happy path: text input ────────────────────────────────────────────────

    [Fact]
    public async Task Execute_WithTextInput_CreatesDraftAndReturnsResponse()
    {
        SetupHappyPath();
        var sut = BuildSut();
        var cmd = new AnalyzeMovementCommand(
            F.TenantId, F.UserId,
            Text: "Pagamento R$ 100,00", AttachmentId: null,
            InputSource: InputSourceType.Text);

        var response = await sut.ExecuteAsync(cmd);

        response.DraftId.Should().NotBe(Guid.Empty);
        response.Extraction.Should().NotBeNull();
        response.Suggestion.Should().NotBeNull();
    }

    [Fact]
    public async Task Execute_WithTextInput_PersistsMovementExtractionSuggestion()
    {
        SetupHappyPath();
        var sut = BuildSut();
        var cmd = new AnalyzeMovementCommand(
            F.TenantId, F.UserId,
            Text: "Pagamento R$ 100,00", AttachmentId: null,
            InputSource: InputSourceType.Text);

        await sut.ExecuteAsync(cmd);

        await _movRepo.Received(1).AddAsync(Arg.Any<FinancialMovement>(), Arg.Any<CancellationToken>());
        await _extRepo.Received(1).AddAsync(Arg.Any<ExtractionResult>(), Arg.Any<CancellationToken>());
        await _sugRepo.Received(1).AddAsync(Arg.Any<InterpretationSuggestion>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Execute_ExtractionContainsFieldStatus_FromAnalysisOutput()
    {
        SetupHappyPath();
        var sut = BuildSut();
        var cmd = new AnalyzeMovementCommand(
            F.TenantId, F.UserId, "R$ 500,00", null, InputSourceType.Text);

        var response = await sut.ExecuteAsync(cmd);

        response.Extraction.Amount.Status.Should().NotBe("RequiresInput");
        response.Extraction.Amount.Value.Should().Be(100m);
    }

    [Fact]
    public async Task Execute_CallsNormalizerWithTenantId()
    {
        SetupHappyPath();
        var sut = BuildSut();
        var cmd = new AnalyzeMovementCommand(
            F.TenantId, F.UserId, "Pagamento fornecedor", null, InputSourceType.Text);

        await sut.ExecuteAsync(cmd);

        _normalizer.Received(1).Normalize(Arg.Any<string>(), F.TenantId);
    }

    [Fact]
    public async Task Execute_CallsMemoryServiceForContext()
    {
        SetupHappyPath();
        var sut = BuildSut();
        var cmd = new AnalyzeMovementCommand(
            F.TenantId, F.UserId, "texto", null, InputSourceType.Text);

        await sut.ExecuteAsync(cmd);

        await _memory.Received(1).GetCompactContextAsync(F.TenantId, F.UserId, Arg.Any<CancellationToken>());
    }

    // ── Attachment input ──────────────────────────────────────────────────────

    [Fact]
    public async Task Execute_WithAttachmentId_LooksUpStorageKey()
    {
        SetupHappyPath();
        var attachmentId = Guid.NewGuid();
        var attachment   = MovementAttachment.CreatePending(F.TenantId, "receipt.pdf", "application/pdf", "key/123.pdf", 1024);

        _attRepo.GetByIdAsync(attachmentId, Arg.Any<CancellationToken>())
                .Returns(attachment);

        var sut = BuildSut();
        var cmd = new AnalyzeMovementCommand(
            F.TenantId, F.UserId, null, attachmentId, InputSourceType.File);

        var response = await sut.ExecuteAsync(cmd);

        response.DraftId.Should().NotBe(Guid.Empty);
        await _attRepo.Received(1).GetByIdAsync(attachmentId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Execute_AttachmentNotFound_ThrowsNotFoundException()
    {
        _selector.Select(Arg.Any<InputSourceType>(), Arg.Any<Guid>(), Arg.Any<AnalyzerProvider?>())
                 .Returns(_analyzer);

        var missingId = Guid.NewGuid();
        _attRepo.GetByIdAsync(missingId, Arg.Any<CancellationToken>())
                .Returns((MovementAttachment?)null);

        var sut = BuildSut();
        var cmd = new AnalyzeMovementCommand(
            F.TenantId, F.UserId, null, missingId, InputSourceType.File);

        await sut.Invoking(s => s.ExecuteAsync(cmd))
                 .Should().ThrowAsync<NotFoundException>();
    }

    // ── Analyzer selection ─────────────────────────────────────────────────────

    [Fact]
    public async Task Execute_SelectsAnalyzerForInputSource()
    {
        SetupHappyPath();
        var sut = BuildSut();
        var cmd = new AnalyzeMovementCommand(
            F.TenantId, F.UserId, "texto", null, InputSourceType.Text);

        await sut.ExecuteAsync(cmd);

        _selector.Received(1).Select(InputSourceType.Text, F.TenantId, null);
    }

    // ── Response shape ────────────────────────────────────────────────────────

    [Fact]
    public async Task Execute_ResponseContainsSuggestionSources()
    {
        SetupHappyPath();
        var sut = BuildSut();
        var cmd = new AnalyzeMovementCommand(
            F.TenantId, F.UserId, "texto", null, InputSourceType.Text);

        var response = await sut.ExecuteAsync(cmd);

        response.Suggestion.Direction.Source.Should().NotBeNullOrWhiteSpace();
        response.Suggestion.Nature.Source.Should().NotBeNullOrWhiteSpace();
    }
}
