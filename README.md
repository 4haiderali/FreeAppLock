<p align="center"><img src="Assets/AppLock.png" width="112" alt="FreeAppLock icon"></p>

# FreeAppLock

A Windows desktop privacy utility that asks for a password before restoring protected application windows. Built with C#, WPF and .NET 8.

**[Download the Windows x64 installer](https://github.com/4haiderali/FreeAppLock/releases/latest)** · [Build and usage guide](BUILD-AND-USAGE.md) · [Security model](SECURITY.md)

## Features

- Protect applications with a separate app-unlock password; use an admin password for settings and rule changes.
- Show one application row for browsers and other apps with many helper processes.
- Unlock browser windows independently. Tabs stay within their window, and switching tabs does not require another unlock.
- Keep recognized browsers running after failed password attempts, with a retry cooldown.
- Relock after switching away, with a configurable delay.
- Run from the system tray and optionally start automatically at Windows login.
- Install into Program Files with desktop and Start menu shortcuts; update by running a newer installer over the existing installation.
- Accept any nonempty admin/app password, including a single character.

FreeAppLock provides reactive privacy assistance inside an existing Windows session. It minimizes unauthorized windows after detecting them; it does not provide Windows account isolation, tamper resistance or pre-launch blocking. Someone controlling the session can stop the utility or edit its local settings. See [SECURITY.md](SECURITY.md) for details.

## Install

1. Download `FreeAppLock-1.0.1-Setup-x64.exe` from the [v1.0.1 release](https://github.com/4haiderali/FreeAppLock/releases/tag/v1.0.1).
2. Quit any running older FreeAppLock through its authenticated Quit action.
3. Run setup and accept the Windows administrator prompt. The .NET runtime is included.
4. Create your admin and app passwords on first launch, then select applications to protect.

The desktop shortcut and login-startup options are enabled by default for a new install. Login launches into the tray after setup is complete. Each Windows account has its own settings. The installer is unsigned.

For updates, quit FreeAppLock and run the newer installer. No uninstall is needed; existing passwords, rules and startup preferences are retained. Uninstalling preserves local settings and logs.

## Build

Requires Windows and the .NET 8 SDK:

```powershell
dotnet build .\FreeAppLock.csproj -c Release
```

Run the core regression suite without UI interaction:

```powershell
dotnet run --project .\Tests\FreeAppLock.CoreTests.csproj -c Release
```

Run the full Windows verification suite on an interactive desktop:

```powershell
.\build-and-test.ps1
```

The Windows tests create disposable windows/processes and send test keystrokes to their password dialogs. Let them finish without interacting with the test windows. The script checks that the real settings file remains unchanged.

To build the self-contained executable and installer, install Inno Setup 6, then run:

```powershell
.\Installer\build-installer.ps1
```

The installer is written to `dist/`. Keep the production installer AppId stable for upgrades and increase `Version` in `FreeAppLock.csproj` for future releases.

## Project layout

| Path | Purpose |
|---|---|
| `Core/` | Settings, password hashing, retry policies, process support and grouping |
| `Views/` | Password and settings dialogs |
| `MainWindow.xaml.cs` | Application list and window monitoring |
| `Assets/` | Shared icon assets and icon build script |
| `Installer/` | Inno Setup source and build script |
| `Tests/` | Core regressions and disposable Windows integration fixtures |

Version 1.0.1 passed 17 core and 17 Windows integration test groups in local verification. Those checks do not guarantee compatibility with every third-party application or Windows session transition. See the [validation report](docs/VALIDATION-v1.0.1.md).

## License

An open-source license has not been selected. Source publication does not itself grant a general license to reuse or redistribute the project. Contact the repository owner about intended use.

## Public version and existing installations

The public release is v1.0.1, shown beside the app name in its header, title bar and tray tooltip. It can update the earlier locally distributed v8.13 installation without uninstalling. The Windows file/upgrade version remains monotonically increasing (8.14.0.0 for this release) to preserve that upgrade path; the product/app version is 1.0.1. Future releases must increase both the public Version/AssemblyVersion and the internal FileVersion. Keep the production installer AppId stable. The installer records the internal upgrade version in a separate machine installer key, independently of its public DisplayVersion.
