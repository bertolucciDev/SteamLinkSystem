using Core.Logging;

namespace Core.Controllers;

public static class InputMapper
{
    private const int AnalogThreshold = 20000;

    public static NavigationAction MapButton(ushort code, ButtonState state)
    {
        if (state == ButtonState.Released)
            return NavigationAction.None;

        var action = code switch
        {
            // Linux input-event-codes.h: BTN_A/BTN_SOUTH, common Xbox A, PlayStation Cross.
            256 or 288 or 304 when state == ButtonState.Pressed => NavigationAction.Select,
            // BTN_B/BTN_EAST, common Xbox B, PlayStation Circle.
            257 or 289 or 305 when state == ButtonState.Pressed => NavigationAction.Back,
            // Start/options/home-ish controls. Keep Home distinct from Select.
            315 when state == ButtonState.Pressed => NavigationAction.Home,
            // Select/share remains Back to support returning without a B/O button.
            314 when state == ButtonState.Pressed => NavigationAction.Back,
            // D-pad key codes.
            544 => NavigationAction.Up,
            545 => NavigationAction.Down,
            546 => NavigationAction.Left,
            547 => NavigationAction.Right,
            _ => NavigationAction.None
        };

        if (action != NavigationAction.None)
            Logger.Debug($"Mapped button code {code} ({state}) to {action}", "Controllers");

        return action;
    }

    public static NavigationAction MapAxis(ushort code, int value)
    {
        var action = code switch
        {
            // ABS_HAT0X / ABS_HAT0Y for D-pad hats.
            16 when value < 0 => NavigationAction.Left,
            16 when value > 0 => NavigationAction.Right,
            17 when value < 0 => NavigationAction.Up,
            17 when value > 0 => NavigationAction.Down,
            // Left analog stick.
            0 when value < -AnalogThreshold => NavigationAction.Left,
            0 when value > AnalogThreshold => NavigationAction.Right,
            1 when value < -AnalogThreshold => NavigationAction.Up,
            1 when value > AnalogThreshold => NavigationAction.Down,
            _ => NavigationAction.None
        };

        if (action != NavigationAction.None)
            Logger.Debug($"Mapped axis code {code} value {value} to {action}", "Controllers");

        return action;
    }
}
