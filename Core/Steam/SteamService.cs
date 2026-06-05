using System.Diagnostics;
using Core.Logging;
using Core.Shell;

namespace Core.Steam;

public sealed class SteamService
{
    private const string SteamLinkFlatpakId = "com.valvesoftware.SteamLink";

    public Task<ShellResult> LaunchSteamLinkAsync(CancellationToken cancellationToken = default)
    {
        Logger.Info("Launching Steam Link Flatpak", "Steam");

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "flatpak",
                    Arguments = $"run {SteamLinkFlatpakId}",
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            process.Dispose();
            return Task.FromResult(new ShellResult(0, "Steam Link Flatpak launch requested.", string.Empty));
        }
        catch (OperationCanceledException)
        {
            Logger.Warning("Steam Link Flatpak launch canceled", "Steam");
            return Task.FromResult(new ShellResult(125, string.Empty, "Launch canceled."));
        }
        catch (Exception ex)
        {
            Logger.Error($"Steam Link Flatpak launch failed: {ex.Message}", "Steam");
            return Task.FromResult(new ShellResult(1, string.Empty, ex.Message));
        }
    }
}
