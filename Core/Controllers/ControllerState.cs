namespace Core.Controllers;

public sealed class ControllerState
{
    private readonly object _sync = new();
    private readonly Dictionary<ushort, ButtonState> _buttonStates = new();
    private readonly Dictionary<ushort, int> _axisStates = new();

    public ControllerState(ControllerDevice device)
    {
        DeviceName = device.Name;
        DevicePath = device.DevicePath;
        Backend = device.Backend;
        Connected = true;
        LastInputUtc = DateTime.UtcNow;
    }

    public string DeviceName { get; }
    public string DevicePath { get; }
    public string Backend { get; }
    public bool Connected { get; private set; }
    public DateTime LastInputUtc { get; private set; }

    public IReadOnlyDictionary<ushort, ButtonState> ButtonStates
    {
        get
        {
            lock (_sync)
                return new Dictionary<ushort, ButtonState>(_buttonStates);
        }
    }

    public IReadOnlyDictionary<ushort, int> AxisStates
    {
        get
        {
            lock (_sync)
                return new Dictionary<ushort, int>(_axisStates);
        }
    }

    public void UpdateButton(ushort code, ButtonState state)
    {
        lock (_sync)
        {
            _buttonStates[code] = state;
            LastInputUtc = DateTime.UtcNow;
        }
    }

    public void UpdateAxis(ushort code, int value)
    {
        lock (_sync)
        {
            _axisStates[code] = value;
            LastInputUtc = DateTime.UtcNow;
        }
    }

    public void MarkDisconnected()
    {
        lock (_sync)
            Connected = false;
    }
}
