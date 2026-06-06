using Core.Logging;
using Core.Shell;

namespace Core.Power;

public sealed class PowerService
{
    public Task<ShellResult> PowerOffAsync(CancellationToken cancellationToken = default)
    {
        Logger.Info("Poweroff requested", "Power");
        return ShellRunner.RunAsync("sudo systemctl", "poweroff", TimeSpan.FromSeconds(3), cancellationToken);
    }

    public Task<ShellResult> RebootAsync(CancellationToken cancellationToken = default)
    {
        Logger.Info("Reboot requested", "Power");
        return ShellRunner.RunAsync("sudo systemctl", "reboot", TimeSpan.FromSeconds(3), cancellationToken);
    }
}
