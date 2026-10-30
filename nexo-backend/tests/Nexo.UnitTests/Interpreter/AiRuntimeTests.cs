using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Nexo.Application.Common.Interfaces;
using Nexo.Application.Modules.Interpreter.Interfaces;
using Nexo.Domain.Modules.Interpreter;
using Nexo.Infrastructure.Modules.Interpreter;
using Nexo.Infrastructure.Security;

namespace Nexo.UnitTests.Interpreter;

public sealed class SecretProtectorTests
{
    private static readonly string Master = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    private const string Secret = "sk-ant-api03-a-secret-that-must-never-be-readable-1234";
    private const string Purpose = "ai-provider/abc/api-key";

    private static SecretProtector With(string? master, string? previous = null)
        => new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Secrets:MasterKey"] = master, ["Secrets:PreviousMasterKeys"] = previous,
        }).Build(), NullLogger<SecretProtector>.Instance);

    [Fact]
    public void Round_trips_and_the_envelope_never_contains_the_plaintext()
    {
        var sut = With(Master);
        var envelope = sut.Protect(Secret, Purpose);

        envelope.Should().StartWith("s1.").And.NotContain(Secret).And.NotContain(Convert.ToBase64String(Encoding.UTF8.GetBytes(Secret)));
        sut.IsEnvelope(envelope).Should().BeTrue();
        sut.Unprotect(envelope, Purpose).Should().Be(Secret);
    }

    [Fact]
    public void Every_encryption_uses_a_fresh_nonce()
    {
        var sut = With(Master);
        sut.Protect(Secret, Purpose).Should().NotBe(sut.Protect(Secret, Purpose));
    }

    [Fact]
    public void Another_purpose_a_tampered_envelope_or_another_master_key_never_opens_it()
    {
        var sut = With(Master);
        var envelope = sut.Protect(Secret, Purpose);

        sut.Unprotect(envelope, "ai-provider/other/api-key").Should().BeNull("an envelope copied to another row does not open");
        var tampered = envelope[..^4] + (envelope[^4] == 'A' ? "B" : "A") + envelope[^3..];
        sut.Unprotect(tampered, Purpose).Should().BeNull();
        With(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))).Unprotect(envelope, Purpose).Should().BeNull();
        sut.Unprotect("not an envelope", Purpose).Should().BeNull();
    }

    [Fact]
    public void Without_a_master_key_nothing_is_encrypted_with_a_fallback()
    {
        var sut = With(null);

        sut.IsAvailable.Should().BeFalse();
        sut.Invoking(s => s.Protect(Secret, Purpose)).Should().Throw<SecretProtectionUnavailableException>();
        With("not-base64-of-32-bytes").IsAvailable.Should().BeFalse("a malformed master key is ignored, never used");
    }

    [Fact]
    public void A_rotated_master_key_still_opens_old_envelopes_and_flags_them_for_re_sealing()
    {
        var oldEnvelope = With(Master).Protect(Secret, Purpose);
        var rotated = With(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)), previous: Master);

        rotated.Unprotect(oldEnvelope, Purpose).Should().Be(Secret);
        rotated.NeedsRotation(oldEnvelope).Should().BeTrue();
        rotated.NeedsRotation(rotated.Protect(Secret, Purpose)).Should().BeFalse();
    }

    [Fact]
    public void Legacy_base64_keys_decode_in_memory_and_garbage_does_not()
    {
        AiProviderRegistry.DecodeLegacy(Convert.ToBase64String(Encoding.UTF8.GetBytes(Secret))).Should().Be(Secret);
        AiProviderRegistry.DecodeLegacy("%%%not-base64").Should().BeNull();
        AiProviderRegistry.DecodeLegacy(Convert.ToBase64String(Encoding.UTF8.GetBytes("has spaces in it"))).Should().BeNull();
    }
}

public sealed class ClaudeExtractionTests
{
    [Fact]
    public void Implausible_values_become_unknown_with_zero_confidence_and_confidences_are_clamped()
    {
        var fields = ClaudeExtraction.Parse("""
            {"amount":-50,"amount_confidence":0.99,"date":"1899-01-01","date_confidence":0.9,
             "payee":"Fornecedor\u0007 X","payee_confidence":7,"account":"","account_confidence":-2}
            """);

        fields.Amount.Should().BeNull();
        fields.AmountConfidence.Should().Be(0);
        fields.Date.Should().BeNull();
        fields.Payee.Should().Be("Fornecedor X", "control characters are stripped");
        fields.PayeeConfidence.Should().Be(1);
        fields.Account.Should().BeNull();
        fields.AccountConfidence.Should().Be(0);
    }

    [Fact]
    public void Amounts_are_rounded_to_cents_and_huge_ones_rejected()
    {
        ClaudeExtraction.Parse("""{"amount":10.005,"amount_confidence":0.9,"date":null,"date_confidence":0,"payee":null,"payee_confidence":0,"account":null,"account_confidence":0}""")
            .Amount.Should().Be(10.01m);
        ClaudeExtraction.Parse("""{"amount":5000000000,"amount_confidence":0.9,"date":null,"date_confidence":0,"payee":null,"payee_confidence":0,"account":null,"account_confidence":0}""")
            .Amount.Should().BeNull();
    }

    [Fact]
    public void Wrong_types_are_an_invalid_response()
    {
        var act = () => ClaudeExtraction.Parse("""{"amount":1,"amount_confidence":1,"date":20261003,"date_confidence":1,"payee":null,"payee_confidence":0,"account":null,"account_confidence":0}""");
        act.Should().Throw<AiProviderCallException>().Which.Failure.Should().Be(AiCallFailure.InvalidResponse);
    }
}

public sealed class AnalysisRunnerTests
{
    private static readonly AnalyzerConfig ClaudeConfig = new(AnalyzerProvider.Claude, "claude-haiku-5-5", "k",
        new ActivePrompt("extraction", "1", "h", "p"), 0, 0);
    private static readonly AnalysisInput Input = new(InputSourceType.Text, "R$ 10,00", null, Guid.NewGuid(), Guid.NewGuid());

    private static AnalysisOutput Output(AnalyzerProvider provider) => new(
        ExtractedField<decimal?>.From(10m, 0.95f, provider), ExtractedField<DateOnly?>.Unknown(), ExtractedField<string?>.Unknown(),
        ExtractedField<string?>.Unknown(), "{}", PromptMetadata.None, Provider: provider);

    private static IDocumentAnalyzer Analyzer(AnalyzerProvider provider, Exception? failure = null)
    {
        var a = Substitute.For<IDocumentAnalyzer>();
        a.Provider.Returns(provider);
        if (failure is null) a.AnalyzeAsync(Arg.Any<AnalysisInput>(), Arg.Any<AnalyzerConfig?>(), Arg.Any<CancellationToken>()).Returns(Output(provider));
        else a.AnalyzeAsync(Arg.Any<AnalysisInput>(), Arg.Any<AnalyzerConfig?>(), Arg.Any<CancellationToken>()).ThrowsAsync(failure);
        return a;
    }

    private static (AnalysisRunner Runner, ITelemetryWriter Telemetry) Runner(AnalyzerPlan plan, IAiUsageGuard? usage = null)
    {
        var selector = Substitute.For<IAnalyzerSelector>();
        selector.PlanAsync(Arg.Any<InputSourceType>(), Arg.Any<Guid>(), Arg.Any<AnalyzerProvider?>(), Arg.Any<CancellationToken>()).Returns(plan);
        var telemetry = Substitute.For<ITelemetryWriter>();
        return (new AnalysisRunner(selector, telemetry, usage ?? Substitute.For<IAiUsageGuard>(), NullLogger<AnalysisRunner>.Instance), telemetry);
    }

    [Fact]
    public async Task A_paid_call_reserves_first_settles_on_success_and_a_reached_limit_falls_back_to_the_free_reader()
    {
        var usage = Substitute.For<IAiUsageGuard>();
        var reservation = Guid.NewGuid();
        usage.ReserveAsync(Arg.Any<Guid>(), Arg.Any<AnalyzerConfig>(), Arg.Any<long>(), Arg.Any<long>(), Arg.Any<CancellationToken>()).Returns(reservation);
        var (runner, _) = Runner(new AnalyzerPlan([new AnalyzerStep(Analyzer(AnalyzerProvider.Claude), ClaudeConfig)], Forced: false), usage);

        await runner.RunAsync(Input, null, "Analyze");

        await usage.Received(1).ReserveAsync(Input.TenantId, ClaudeConfig, Arg.Any<long>(), Arg.Any<long>(), Arg.Any<CancellationToken>());
        await usage.Received(1).SettleAsync(reservation, Arg.Any<long>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>());

        var limited = Substitute.For<IAiUsageGuard>();
        limited.ReserveAsync(Arg.Any<Guid>(), Arg.Any<AnalyzerConfig>(), Arg.Any<long>(), Arg.Any<long>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new AiProviderCallException(AnalyzerProvider.Claude, AiCallFailure.LimitReached, "limite"));
        var claude = Analyzer(AnalyzerProvider.Claude);
        var (fallback, _) = Runner(new AnalyzerPlan([new AnalyzerStep(claude, ClaudeConfig), new AnalyzerStep(Analyzer(AnalyzerProvider.RuleBased), null)], Forced: false), limited);

        var run = await fallback.RunAsync(Input, null, "Analyze");

        run.Provider.Should().Be(AnalyzerProvider.RuleBased);
        run.Attempts[0].FailureReason.Should().Be("limit_reached");
        await claude.DidNotReceiveWithAnyArgs().AnalyzeAsync(default!, default, default);
    }

    [Fact]
    public async Task A_failed_paid_call_releases_its_reservation()
    {
        var usage = Substitute.For<IAiUsageGuard>();
        var reservation = Guid.NewGuid();
        usage.ReserveAsync(Arg.Any<Guid>(), Arg.Any<AnalyzerConfig>(), Arg.Any<long>(), Arg.Any<long>(), Arg.Any<CancellationToken>()).Returns(reservation);
        var (runner, _) = Runner(new AnalyzerPlan([new AnalyzerStep(Analyzer(AnalyzerProvider.Claude, Failure(AiCallFailure.Timeout)), ClaudeConfig)], Forced: true), usage);

        await ((Func<Task>)(() => runner.RunAsync(Input, AnalyzerProvider.Claude, "Playground"))).Should().ThrowAsync<AiProviderCallException>();
        await usage.Received(1).ReleaseAsync(reservation, Arg.Any<CancellationToken>());
    }

    private static AiProviderCallException Failure(AiCallFailure f) => new(AnalyzerProvider.Claude, f, "falhou");

    [Fact]
    public async Task Automatic_mode_falls_back_to_the_next_usable_provider_and_says_so()
    {
        var (runner, telemetry) = Runner(new AnalyzerPlan([
            new AnalyzerStep(Analyzer(AnalyzerProvider.Claude, Failure(AiCallFailure.RateLimited)), ClaudeConfig),
            new AnalyzerStep(Analyzer(AnalyzerProvider.RuleBased), null)], Forced: false));

        var run = await runner.RunAsync(Input, null, "Analyze");

        run.Provider.Should().Be(AnalyzerProvider.RuleBased);
        run.FallbackUsed.Should().BeTrue();
        run.FallbackFrom.Should().Be(AnalyzerProvider.Claude);
        run.Attempts.Should().HaveCount(2);
        run.Attempts[0].FailureReason.Should().Be("rate_limited");
        await telemetry.Received(1).WriteAsync(Arg.Is<TelemetryEntry>(e => !e.Success && e.Provider == "Claude" && e.ModelId == "claude-haiku-5-5"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_forced_provider_failure_is_returned_never_hidden_behind_another_provider()
    {
        var ruleBased = Analyzer(AnalyzerProvider.RuleBased);
        var (runner, _) = Runner(new AnalyzerPlan([
            new AnalyzerStep(Analyzer(AnalyzerProvider.Claude, Failure(AiCallFailure.InvalidCredentials)), ClaudeConfig)], Forced: true));

        var act = () => runner.RunAsync(Input, AnalyzerProvider.Claude, "Playground");

        (await act.Should().ThrowAsync<AiProviderCallException>()).Which.Failure.Should().Be(AiCallFailure.InvalidCredentials);
        await ruleBased.DidNotReceiveWithAnyArgs().AnalyzeAsync(default!, default, default);
    }

    [Fact]
    public async Task When_every_provider_fails_the_last_failure_is_returned()
    {
        var (runner, _) = Runner(new AnalyzerPlan([
            new AnalyzerStep(Analyzer(AnalyzerProvider.Claude, Failure(AiCallFailure.Timeout)), ClaudeConfig)], Forced: false));

        (await ((Func<Task>)(() => runner.RunAsync(Input, null, "Analyze"))).Should().ThrowAsync<AiProviderCallException>())
            .Which.Failure.Should().Be(AiCallFailure.Timeout);
    }

    [Fact]
    public async Task A_bug_in_an_analyzer_is_not_swallowed_as_a_fallback()
    {
        var (runner, _) = Runner(new AnalyzerPlan([
            new AnalyzerStep(Analyzer(AnalyzerProvider.Claude, new NullReferenceException()), ClaudeConfig),
            new AnalyzerStep(Analyzer(AnalyzerProvider.RuleBased), null)], Forced: false));

        await ((Func<Task>)(() => runner.RunAsync(Input, null, "Analyze"))).Should().ThrowAsync<NullReferenceException>();
    }
}

public sealed class AnalyzerSelectorTests
{
    private static AiProviderState State(AnalyzerProvider p, AiProviderStatus status, bool isDefault, int priority)
        => new(Guid.NewGuid(), p, p.ToString(), status, "", true, p != AnalyzerProvider.RuleBased, true, status != AiProviderStatus.Disabled,
            isDefault, priority, null, false, null, null, null, 0, 0, null, null, null);

    private static IDocumentAnalyzer Analyzer(AnalyzerProvider p)
    {
        var a = Substitute.For<IDocumentAnalyzer>();
        a.Provider.Returns(p);
        return a;
    }

    [Fact]
    public async Task Automatic_order_is_default_first_then_priority_and_skips_unavailable_providers()
    {
        var registry = Substitute.For<IAiProviderRegistry>();
        registry.GetStatesAsync(Arg.Any<CancellationToken>()).Returns(new List<AiProviderState>
        {
            State(AnalyzerProvider.Claude, AiProviderStatus.Available, isDefault: true, priority: 2),
            State(AnalyzerProvider.RuleBased, AiProviderStatus.Available, isDefault: false, priority: 1),
            State(AnalyzerProvider.OpenAI, AiProviderStatus.ImplementationMissing, isDefault: false, priority: 3),
        });
        var sut = new AnalyzerSelectorService([Analyzer(AnalyzerProvider.RuleBased), Analyzer(AnalyzerProvider.Claude)], registry);

        var plan = await sut.PlanAsync(InputSourceType.Text, Guid.NewGuid());

        plan.Forced.Should().BeFalse();
        plan.Steps.Select(s => s.Provider).Should().Equal(AnalyzerProvider.Claude, AnalyzerProvider.RuleBased);
    }

    [Fact]
    public async Task Nothing_usable_or_a_forced_unavailable_provider_is_a_controlled_error_not_a_500()
    {
        var registry = Substitute.For<IAiProviderRegistry>();
        registry.GetStatesAsync(Arg.Any<CancellationToken>()).Returns(new List<AiProviderState>
        {
            State(AnalyzerProvider.RuleBased, AiProviderStatus.Disabled, isDefault: true, priority: 1),
        });
        registry.ResolveAsync(AnalyzerProvider.OpenAI, Arg.Any<CancellationToken>())
            .ThrowsAsync(new AiProviderUnavailableException("não disponível", AnalyzerProvider.OpenAI, AiProviderStatus.ImplementationMissing));
        var sut = new AnalyzerSelectorService([Analyzer(AnalyzerProvider.RuleBased)], registry);

        await ((Func<Task>)(() => sut.PlanAsync(InputSourceType.Text, Guid.NewGuid()))).Should().ThrowAsync<AiProviderUnavailableException>();
        (await ((Func<Task>)(() => sut.PlanAsync(InputSourceType.Text, Guid.NewGuid(), AnalyzerProvider.OpenAI))).Should()
            .ThrowAsync<AiProviderUnavailableException>()).Which.Code.Should().Be("ai_provider_unavailable");
    }
}
