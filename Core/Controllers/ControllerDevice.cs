namespace Core.Controllers;

public sealed record ControllerDevice(
    string Name,
    string DevicePath,
    string Backend,
    bool IsLikelyController);
