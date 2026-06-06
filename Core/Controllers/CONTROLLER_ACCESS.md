# Controller input access

SteamLinkSystem reads Linux controller input through evdev (`/dev/input/event*`) so the TUI can remain terminal-native and avoid desktop GUI dependencies.

## Required user groups

The application should not run as root. Grant the service user access to input, audio, video, and render devices instead:

```bash
sudo usermod -aG input,audio,video,render steamlinkclient
```

Log out and back in after changing groups so the new permissions are applied.

## Backend notes

- Current backend: Linux evdev.
- Future backend: SDL2 can be added behind `ControllerManager` without changing TUI menu code.
- Expected devices: Xbox, DualShock, DualSense, Steam, and generic gamepads exposing joystick/gamepad event nodes.

## Failure behavior

Permission failures, unsupported devices, disconnects, and read failures are logged and must not crash the TUI. Keyboard navigation remains available as a fallback.
