# Controller input access

SteamLinkSystem reads controller input through SDL2 GameController first and Linux evdev (`/dev/input/event*`) as a fallback so the TUI can remain terminal-native and avoid desktop GUI dependencies.

## Required user groups

The application should not run as root. Grant the service user access to input, audio, video, and render devices instead:

```bash
sudo usermod -aG input,audio,video,render steamlinkclient
```

Log out and back in after changing groups so the new permissions are applied.

## Backend notes

- Primary backend: SDL2 GameController API (`libSDL2-2.0.so.0`) with an independent event loop and watchdog.
- Fallback backend: Linux evdev remains active for devices exposing readable joystick/gamepad event nodes.
- Recovery behavior: after a fullscreen takeover such as Steam Link, SDL controllers are closed, the GameController subsystem is quit/reinitialized, and controllers are scanned/opened again automatically.
- Expected devices: Xbox, DualShock, DualSense, Steam, and generic gamepads exposing SDL GameController mappings or joystick/gamepad event nodes.

## Failure behavior

Permission failures, unsupported devices, disconnects, and read failures are logged and must not crash the TUI. Keyboard navigation remains available as a fallback.
