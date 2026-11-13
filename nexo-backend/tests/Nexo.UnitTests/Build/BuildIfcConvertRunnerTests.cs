using System.Diagnostics;
using FluentAssertions;
using Nexo.Infrastructure.Modules.Build;

namespace Nexo.UnitTests.Build;

/// <summary>
/// O disparo do IfcConvert (argumentos fixos, tempo limite rígido) com um executável de mentira — um script que registra os
/// argumentos, falha ou fica parado: código de saída diferente de zero é erro, e passar do tempo mata o processo.
/// </summary>
public sealed class BuildIfcConvertRunnerTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "orken-ifcconvert-test-" + Guid.NewGuid().ToString("N"));

    public BuildIfcConvertRunnerTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); }
        catch (IOException) { /* a killed process may still hold the folder for a moment */ }
        catch (UnauthorizedAccessException) { }
    }

    /// <summary>"args" writes the received arguments, one per line, to the output path; "fail" exits 7; "hang" sleeps 30 s.</summary>
    private string Script(string behaviour)
    {
        if (OperatingSystem.IsWindows())
        {
            var cmd = Path.Combine(_dir, behaviour + ".cmd");
            File.WriteAllText(cmd, behaviour switch
            {
                "args" => "@(echo %~1& echo %~2& echo %~3& echo %~4) > \"%~4\"\r\n@exit /b 0\r\n",
                "fail" => "@echo erro de leitura 1>&2\r\n@exit /b 7\r\n",
                _ => "@ping -n 30 127.0.0.1 >nul\r\n@exit /b 0\r\n",
            });
            return cmd;
        }
        var sh = Path.Combine(_dir, behaviour + ".sh");
        File.WriteAllText(sh, behaviour switch
        {
            "args" => "#!/bin/sh\nprintf '%s\\n' \"$1\" \"$2\" \"$3\" \"$4\" > \"$4\"\n",
            "fail" => "#!/bin/sh\necho 'erro de leitura' >&2\nexit 7\n",
            _ => "#!/bin/sh\nsleep 30\n",
        });
        File.SetUnixFileMode(sh, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return sh;
    }

    [Fact]
    public async Task Passes_only_the_fixed_arguments_and_the_two_paths()
    {
        var input = Path.Combine(_dir, "input.ifc");
        var output = Path.Combine(_dir, "output.glb");
        await File.WriteAllTextAsync(input, "ISO-10303-21;");

        await BuildModelProcessor.RunIfcConvertAsync(Script("args"), input, output, TimeSpan.FromSeconds(30), CancellationToken.None);

        (await File.ReadAllLinesAsync(output)).Select(l => l.Trim()).Should().Equal("--use-element-guids", "-y", input, output);
    }

    [Fact]
    public async Task A_non_zero_exit_code_is_an_error()
    {
        var input = Path.Combine(_dir, "input.ifc");
        await File.WriteAllTextAsync(input, "ISO-10303-21;");

        var act = () => BuildModelProcessor.RunIfcConvertAsync(Script("fail"), input, Path.Combine(_dir, "output.glb"),
            TimeSpan.FromSeconds(30), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("IfcConvert exited with 7.");
    }

    [Fact]
    public async Task Passing_the_time_limit_kills_the_converter()
    {
        var input = Path.Combine(_dir, "input.ifc");
        await File.WriteAllTextAsync(input, "ISO-10303-21;");
        var watch = Stopwatch.StartNew();

        var act = () => BuildModelProcessor.RunIfcConvertAsync(Script("hang"), input, Path.Combine(_dir, "output.glb"),
            TimeSpan.FromMilliseconds(500), CancellationToken.None);

        await act.Should().ThrowAsync<OperationCanceledException>();
        watch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(20), "the converter is stopped at the limit, not awaited");
    }

    [Fact]
    public async Task The_callers_cancellation_also_stops_the_converter()
    {
        var input = Path.Combine(_dir, "input.ifc");
        await File.WriteAllTextAsync(input, "ISO-10303-21;");
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));

        var act = () => BuildModelProcessor.RunIfcConvertAsync(Script("hang"), input, Path.Combine(_dir, "output.glb"),
            TimeSpan.FromMinutes(5), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task A_missing_executable_does_not_start()
    {
        var act = () => BuildModelProcessor.RunIfcConvertAsync(Path.Combine(_dir, "nao-existe"), Path.Combine(_dir, "input.ifc"),
            Path.Combine(_dir, "output.glb"), TimeSpan.FromSeconds(5), CancellationToken.None);

        await act.Should().ThrowAsync<System.ComponentModel.Win32Exception>();
    }
}
