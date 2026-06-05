using System.Diagnostics;
using Core.Logging;

namespace Core.Shell;

public sealed record ShellResult(int ExitCode, string StandardOutput, string StandardError)
{
    public bool Success => ExitCode == 0;
    public string CombinedOutput => string.IsNullOrWhiteSpace(StandardError)
        ? StandardOutput.Trim()
        : $"{StandardOutput}\n{StandardError}".Trim();
}

public static class ShellRunner
{
    public static async Task<ShellResult> RunAsync(string fileName, string arguments = "", TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linkedCts.CancelAfter(timeout ?? TimeSpan.FromSeconds(10));

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        try
        {
            process.Start();
            var stdoutTask = process.StandardOutput.ReadToEndAsync(linkedCts.Token);
            var stderrTask = process.StandardError.ReadToEndAsync(linkedCts.Token);
            await process.WaitForExitAsync(linkedCts.Token).ConfigureAwait(false);
            return new ShellResult(process.ExitCode, await stdoutTask.ConfigureAwait(false), await stderrTask.ConfigureAwait(false));
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            Logger.Warning($"Command timed out: {fileName} {arguments}", "Shell");
            return new ShellResult(124, string.Empty, "Command timed out.");
        }
        catch (Exception ex)
        {
            Logger.Error($"Command failed: {fileName} {arguments}: {ex.Message}", "Shell");
            return new ShellResult(1, string.Empty, ex.Message);
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch
        {
        }
    }
}
