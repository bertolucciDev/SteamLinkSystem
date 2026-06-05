using Core.Logging;
using Core.Shell;

namespace Core.Steam;

public sealed class SteamService
{
    public Task<ShellResult> LaunchSteamAsync(CancellationToken cancellationToken = default)
    {
        Logger.Info("Launching Steam", "Steam");
        return ShellRunner.RunAsync("steam", string.Empty, TimeSpan.FromSeconds(5), cancellationToken);
    }

    public Task<ShellResult> LaunchBigPictureAsync(CancellationToken cancellationToken = default)
    {
        Logger.Info("Launching Steam Big Picture", "Steam");
        return ShellRunner.RunAsync("steam", "-bigpicture", TimeSpan.FromSeconds(5), cancellationToken);
    }
}
