using System.Diagnostics;
using System.Text;

namespace Publisher;

public sealed record ProcessResult(int ExitCode, string StdOut, string StdErr);

public static class Shell
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(10);

    public static Task<ProcessResult> CaptureAsync(string file, IReadOnlyList<string> args, string? workDir,
        IReadOnlyDictionary<string, string>? env, TimeSpan? timeout, CancellationToken ct)
        => RunCoreAsync(file, args, workDir, env, timeout ?? DefaultTimeout, echo: false, display: null, ct);

    public static Task<ProcessResult> RunShellAsync(string command, string workDir, TimeSpan timeout,
        bool echo, string? display, CancellationToken ct)
    {
        if (OperatingSystem.IsWindows())
            return RunCoreAsync("cmd.exe", ["/d", "/s", "/c", command], workDir, null, timeout, echo, display ?? command, ct);
        return RunCoreAsync("/bin/sh", ["-c", command], workDir, null, timeout, echo, display ?? command, ct);
    }

    private static async Task<ProcessResult> RunCoreAsync(string file, IReadOnlyList<string> args, string? workDir,
        IReadOnlyDictionary<string, string>? env, TimeSpan timeout, bool echo, string? display, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = file,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        foreach (var arg in args) psi.ArgumentList.Add(arg);
        if (!string.IsNullOrWhiteSpace(workDir)) psi.WorkingDirectory = workDir;
        if (env is not null)
        {
            foreach (var pair in env) psi.Environment[pair.Key] = pair.Value;
        }

        if (echo) Ui.Command(display is { Length: > 0 } ? display : file);

        using var process = new Process { StartInfo = psi };
        if (!process.Start()) throw new ToolException($"اجرای دستور ممکن نشد: {file}");

        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        var outTask = PumpAsync(process.StandardOutput, stdout, echo, isError: false);
        var errTask = PumpAsync(process.StandardError, stderr, echo, isError: true);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        try
        {
            await process.WaitForExitAsync(cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            try { await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false); } catch (InvalidOperationException) { }
            await Task.WhenAll(outTask, errTask).ConfigureAwait(false);
            if (ct.IsCancellationRequested) throw;
            throw new ToolException($"مهلت اجرای دستور تمام شد ({timeout.TotalMinutes:0} دقیقه): {display ?? file}");
        }

        await Task.WhenAll(outTask, errTask).ConfigureAwait(false);
        return new ProcessResult(process.ExitCode, stdout.ToString(), stderr.ToString());
    }

    private static async Task PumpAsync(StreamReader reader, StringBuilder sink, bool echo, bool isError)
    {
        string? line;
        while ((line = await reader.ReadLineAsync().ConfigureAwait(false)) is not null)
        {
            sink.AppendLine(line);
            if (echo) Ui.Stream(line, isError);
        }
    }
}
