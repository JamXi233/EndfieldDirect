# EndfieldDirect

English | [中文说明](README.md)

Launches Arknights: Endfield directly, without opening the official launcher.

Unofficial project, not affiliated with Hypergryph. Please read the Risks section before use.

## Features

- Starts Endfield directly, without opening the official launcher.
- Hides the launcher's windows and removes its tray icon.
- `-sc` creates a shortcut with the game's icon and stores the remaining launch options in it.
- Can be added to Steam as a non-Steam game. The Steam overlay, F12 screenshots and third-party performance-monitor overlays were tested and work in the author's environment.
- Sets the game's text and voice language through launch options.
- A single exe of about 35 KB, no installer.

## Usage

1. Put `EndfieldDirect.exe` in the official launcher's folder, next to `Launcher.exe`. If the launcher is not found, a dialog explains what to do.
2. Run it and accept the UAC prompt.

### Creating a shortcut

```text
EndfieldDirect.exe -sc -wait=5
```

This creates a shortcut named "Endfield Direct" on the desktop. It uses the game's icon and launches with `-wait=5`.

`-sc` can be written in three ways:

| Form | Result |
|---|---|
| `-sc` | Put it on the desktop |
| `-sc folder-path` | Put it in the given folder |
| `-sc=path\name.lnk` | Give the full path and file name |

Every argument on the command line except `-sc` is stored in the shortcut. The shortcut points to the exe's current location, so recreate it after moving the exe.

### Using it with Steam

Choose "Add a Non-Steam Game" and select `EndfieldDirect.exe`. Put any arguments in the entry's Launch Options.

Normally `-hold` is not needed: Steam shows the game as running and tracks playtime (tested in the author's environment). Add `-hold` only if Steam's running status or playtime does not work, so the program waits until the game exits.

## Options

| Option | Description |
|---|---|
| `-sc` | Create a shortcut with the game's icon and store the other arguments in it; forms above |
| `-wait=N` | Seconds to wait after the game window appears before cleaning up. Default `3`; `0` usually fails |
| `-timeout=N` | Maximum seconds to wait for the game window. Default `60` |
| `-hold` | Close the launcher only after the game exits |
| `-dry-run` | Only print what would be done, without launching (with `-sc`, no shortcut is created) |
| `-v` / `-verbose` | Detailed log with timestamps |
| `-lang=zh` / `-lang=en` | Set the UI language; follows the system language by default |
| `-language_text_change=N` | Writes the text-language registry value |
| `-language_audio=N` | Writes the voice-language registry value |
| `-launcher_xxx=value` | Overrides a launcher argument; defaults are `-launcher_lang=zh-cn -launcher_sub_channel=1` |
| anything else | Passed through to the game, e.g. `-force-d3d11` |

Language settings work by editing the existing values under `HKCU\Software\Hypergryph\Endfield`, so run the game once through the official launcher first so those values exist.

## How it works

1. Starts the official `Launcher.exe` in the background and waits for it to bring up `Games.exe`.
2. Starts `Endfield.exe` with `Games.exe` as its parent process, so the game believes it was started by the official launcher.
3. Uses a window event hook to hide the launcher's windows the moment they appear, and unregisters its tray icon.
4. Once the game window has appeared and the grace period has passed, asks the launcher to exit normally and force-kills it only on timeout.

## Risks

- Parent-process spoofing bypasses the game's check that it must be started by the official launcher. This may violate the game's terms of service and may stop working after a game update. Whether to use it, and any consequences, are your own decision and responsibility.
- The program only changes how the launcher and the game are started. It does not modify game files or inject code into the game process.
- It requires administrator rights and reads the taskbar's notification-area data to unregister the launcher's tray icon.
- It only closes `Games.exe` and `Launcher.exe` located inside the launcher's install folder, and leaves other programs alone.

## Build from source

Windows 10/11 x64. No extra tools are needed; the .NET Framework 4 compiler that ships with Windows is enough:

```powershell
.\build.ps1
```

## License

MIT License. See the LICENSE file in the repository root.
