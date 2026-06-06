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
            var deviceLines = states.Count == 0
                ? "No controller event devices are currently readable. Ensure the service user belongs to input,audio,video,render and that controllers expose /dev/input/event* nodes."
                : string.Join(Environment.NewLine, states.Select(state => $"- {state.DeviceName} ({state.DevicePath}) [{state.Backend}] connected={state.Connected} lastInput={state.LastInputUtc:u}"));

            return "Controller navigation subsystem: Linux evdev backend active; SDL2 backend can be added behind ControllerManager later." + Environment.NewLine +
                   $"Detected/readable devices: {_manager.ConnectedDeviceCount}" + Environment.NewLine +
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
