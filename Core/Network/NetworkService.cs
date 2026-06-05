using Core.Shell;

namespace Core.Network;

public sealed class NetworkService
{
    public Task<ShellResult> GetIpStatusAsync(CancellationToken cancellationToken = default) =>
        ShellRunner.RunAsync("ip", "addr", TimeSpan.FromSeconds(5), cancellationToken);

    public Task<ShellResult> TestConnectivityAsync(CancellationToken cancellationToken = default) =>
        ShellRunner.RunAsync("ping", "-c 1 -W 2 1.1.1.1", TimeSpan.FromSeconds(4), cancellationToken);
}
