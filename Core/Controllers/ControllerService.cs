using Core.Logging;

namespace Core.Controllers;

public sealed class ControllerService : IDisposable
{
    private readonly ControllerManager _manager = new();
    private bool _started;

    public event Action<NavigationAction>? NavigationReceived;

    public string Status
    {
        get
        {
            var states = _manager.States;
            var sdlLines = _manager.SdlControllerSummaries;
            var evdevLines = states.Select(state => $"- {state.DeviceName} ({state.DevicePath}) [{state.Backend}] connected={state.Connected} lastInput={state.LastInputUtc:u}");
            var allDeviceLines = sdlLines.Concat(evdevLines).ToArray();
            var deviceLines = allDeviceLines.Length == 0
                ? "No controller devices are currently readable. Ensure SDL2 is installed or the service user belongs to input,audio,video,render for evdev fallback."
                : string.Join(Environment.NewLine, allDeviceLines);

            return "Controller navigation subsystem: SDL2 GameController primary backend with Linux evdev fallback." + Environment.NewLine +
                   $"Detected/readable devices: {_manager.ConnectedDeviceCount}" + Environment.NewLine +
                   $"SDL2 available: {_manager.IsSdlAvailable}" + Environment.NewLine +
                   "Supported defaults: D-pad/left stick navigate, A/Cross selects, B/Circle backs out, Start opens Home." + Environment.NewLine +
                   "Linux access: sudo usermod -aG input,audio,video,render steamlinkclient" + Environment.NewLine +
                   deviceLines;
        }
    }

    public void Start()
    {
        if (_started)
            return;

        _started = true;
        _manager.NavigationReceived += action => NavigationReceived?.Invoke(action);
        _manager.Start();
        Logger.Info("Controller service initialized", "Controllers");
    }

    public bool TryReadNavigation(out NavigationAction action)
    {
        Start();
        if (!_manager.TryReadNavigation(out action))
            return false;

        return true;
    }

    public async Task<NavigationAction> ReadNavigationAsync(CancellationToken cancellationToken = default)
    {
        Start();
        return await _manager.ReadNavigationAsync(cancellationToken).ConfigureAwait(false);
    }

    public void Dispose() => _manager.Dispose();
}
