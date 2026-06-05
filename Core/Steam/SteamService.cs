using System.Diagnostics;
using Core.Logging;
using Core.Shell;

namespace Core.Steam;

public sealed class SteamService
{
    public async Task<ShellResult> LaunchSteamLinkAsync(
        CancellationToken cancellationToken = default)
    {
        Logger.Info(
            "Launching Steam Link session",
            "Steam");

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var process = new Process();

            process.StartInfo = new ProcessStartInfo
            {
                FileName = "startx",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            process.Start();

            Logger.Info(
                $"Steam Link session started with PID {process.Id}",
                "Steam");

            await process.WaitForExitAsync(cancellationToken);

            var stdout =
                await process.StandardOutput.ReadToEndAsync();

            var stderr =
                await process.StandardError.ReadToEndAsync();

            Logger.Info(
                $"Steam Link session ended with code {process.ExitCode}",
                "Steam");

            return new ShellResult(
                process.ExitCode,
                stdout,
                stderr);
        }
        catch (OperationCanceledException)
        {
            Logger.Warning(
                "Steam Link launch canceled",
                "Steam");

            return new ShellResult(
                125,
                string.Empty,
                "Launch canceled.");
        }
        catch (Exception ex)
        {
            Logger.Error(
                $"Steam Link launch failed: {ex.Message}",
                "Steam");

            return new ShellResult(
                1,
                string.Empty,
                ex.Message);
        }
    }
}