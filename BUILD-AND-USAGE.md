# FreeAppLock 1.1.0

Run FreeAppLock-1.1.0-Setup-x64.exe normally and accept the Windows administrator prompt. Close any running older FreeAppLock first using its authenticated Quit action.

The installer defaults to C:\Program Files\FreeAppLock on a standard C-drive Windows installation. It creates Start menu and desktop shortcuts, enables login startup for all users, and offers to launch the app at the end. Desktop shortcut and login startup tasks are checked by default and can be deselected. The .NET runtime is included. No separate SDK/runtime installation is required.

Create your admin and app passwords on first launch. Existing local settings are retained. At subsequent login, the app starts in the tray after password setup is complete. Open the tray icon or a shortcut to show the app. Both admin and app passwords can be any nonempty length, including one character.

Each Windows account has its own passwords, rules and startup override. Settings > Launch at Windows login controls the current account. Disabling startup causes the machine login invocation to exit without monitoring. Previously disabled accounts stay disabled after upgrades until re-enabled. A matching installed startup registration also removes the current user's old portable startup entry.

Uninstall through Windows Settings > Apps or the Start menu uninstaller. Program files, installer-created shortcuts and machine startup registration are removed. Passwords, protection rules, logs and per-user preferences in local application data/registry are deliberately retained. Quit FreeAppLock before upgrading or uninstalling; the installer does not forcibly terminate it.

This is an unsigned x64 Windows 10/11 installer. Run it normally, rather than pre-launching it as administrator, so the app can be started as the original interactive user. An installation approved with credentials for a different Windows administrator still installs shared Program Files/shortcuts/startup; account settings belong to the user who runs the app.

FreeAppLock is reactive privacy assistance, not OS-level access control. It can be ended or reconfigured by someone controlling the Windows session. Elevated/hosted app compatibility, previews and arbitrary third-party surfaces are not guaranteed. See SECURITY.md.

## Build and tests

Install .NET 8 SDK and Inno Setup 6, then run build-and-test.ps1. This executes 17 core and 17 Windows integration groups using disposable data/windows. Tests verify the real settings file is unchanged. Installer/build-installer.ps1 publishes the self-contained executable and compiles the installer. Generated bin/obj/publish/dist folders are not source artifacts.

## Updating an existing installation

Quit FreeAppLock through its authenticated Quit action and run the newer installer. Do not uninstall first. Setup uses the same application ID, keeps the registered install location and selected tasks, replaces versioned application files, and refreshes the shortcuts. Passwords, rules, logs and account startup preferences remain in their existing locations. A newer registered version blocks a lower-version installer built from this template. Same-version reinstall is permitted for repair. Old installer releases without downgrade checks cannot gain those checks retroactively.

This provides manual in-place updates. There is no background download server or automatic updater. Future releases must retain the production AppId and bump FreeAppLock.csproj Version; Installer/build-installer.ps1 supplies that version to the compiler. Keep the machine install mode and settings schema compatible, or implement an explicit migration.

## Icon

The same cyan padlock on a navy tile is embedded in the executable and used for shortcuts, app/dialog windows, taskbar, tray, app header, setup and uninstall display. Assets/AppLock.ico contains nine resolutions. Assets/AppLock.svg is editable; Assets/Build-Icon.ps1 reproduces the supplied bitmap/ICO using built-in Windows drawing tools. Settings/rules never depend on the icon files.

## Application list and browser windows

Brave, Chrome, Edge and other multi-process apps appear once in the list. The Processes column shows a count; hover for PIDs. Background helpers are included in that count but cannot make an actual locked browser window appear as separate Rule set rows. A group may show Mixed if some windows are authorized and others are locked.

Passwords authorize a browser window and its same-process owned dialogs. Tabs inside that window stay together; switching tabs or changing the window title does not require a fresh password. A separate browser window has its own approval. The configured relock delay still applies when you switch to a different application or minimize the browser. When several browser windows exist, activate the one you want rather than using the list's Unlock button to choose arbitrarily.

Recognized browsers always stay running on failed-password lockout, including when termination is enabled for other apps. There is a per-window retry cooldown. FreeAppLock does not send browser close-tab/window commands or alter browser session/profile files. Core/BrowserPolicy.cs lists supported browser process names. Custom/renamed browsers are not automatically recognized; keep process termination off for them.

The password prompt activates and focuses its input after rendering, including tray/minimized startup. Windows foreground rules and elevated/secure desktop transitions can still limit activation; test your normal browser workflow after upgrading.

## Public version and existing installations

The public release is v1.1.0, shown beside the app name in its header, title bar and tray tooltip. It can update the earlier locally distributed v8.13 installation without uninstalling. The Windows file/upgrade version remains monotonically increasing (8.15.0.0 for this release) to preserve that upgrade path; the product/app version is 1.1.0. Future releases must increase both the public Version/AssemblyVersion and the internal FileVersion. Keep the production installer AppId stable. The installer records the internal upgrade version in a separate machine installer key, independently of its public DisplayVersion.

For new per-app timers and profile setup, see README.md and BROWSER-PROFILES.md.
