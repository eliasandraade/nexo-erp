using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Nexo.Api.Middleware;
using Nexo.Application.Common.Observability;
using NSubstitute;

namespace Nexo.UnitTests.Observability;

/// <summary>
/// Kestrel refuses a request while MVC reads its body, and the exception carries the status it chose. The client sent too much, or stopped in the middle: nothing failed here,
/// so it is no 500, no error log, no error metric and no line in error_events.
/// </summary>
public class BadRequestFromTheServerTests
{
    private readonly IErrorTracker _errors = Substitute.For<IErrorTracker>();

    private sealed class CountingLogger : ILogger<ExceptionHandlingMiddleware>
    {
        public int Errors { get; private set; }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel >= LogLevel.Error) Errors++;
        }
    }

    private async Task<(HttpContext Context, CountingLogger Logger)> RunAsync(Exception thrown)
    {
        var logger = new CountingLogger();
        var context = new DefaultHttpContext { Response = { Body = new MemoryStream() } };
        await new ExceptionHandlingMiddleware(_ => throw thrown, logger, _errors).InvokeAsync(context);
        return (context, logger);
    }

    [Theory]
    [InlineData(StatusCodes.Status413PayloadTooLarge)]
    [InlineData(StatusCodes.Status400BadRequest)]
    [InlineData(StatusCodes.Status408RequestTimeout)]
    public async Task The_status_of_the_server_is_the_answer(int status)
    {
        var (context, _) = await RunAsync(new BadHttpRequestException("refused by the server", status));

        context.Response.StatusCode.Should().Be(status);
    }

    [Fact]
    public async Task It_is_no_error_of_ours()
    {
        var (_, logger) = await RunAsync(new BadHttpRequestException("too large", StatusCodes.Status413PayloadTooLarge));

        logger.Errors.Should().Be(0);
        await _errors.DidNotReceiveWithAnyArgs().TrackAsync(default!, default);
    }

    [Fact]
    public async Task Any_other_exception_is_still_a_500_and_an_error_event()
    {
        var (context, logger) = await RunAsync(new InvalidOperationException("a bug"));

        context.Response.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
        logger.Errors.Should().Be(1);
        await _errors.ReceivedWithAnyArgs(1).TrackAsync(default!, default);
    }
}
