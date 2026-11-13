# FreeAppLock v1.0.1

Initial GitHub release of the Windows privacy utility. Public app/product version is 1.0.1, displayed alongside FreeAppLock in the main header, title bar and tray tooltip. Retains the established padlock icon, grouped app rows, independent browser-window unlocks, safe browser lockouts, password focus fixes, and any-nonempty-length admin/app passwords.

This installer upgrades the earlier locally distributed v8.13 build in place and preserves its passwords, rules and startup choices. Quit the running utility through its authenticated Quit action before running setup. No uninstall is needed.

Windows file version 8.14.0.0 is retained as a monotonically increasing internal upgrade identifier so the public version reset does not cause old v8.13 files to be treated as newer. The installer exposes public version 1.0.1 and records the internal upgrade identifier separately. No automatic update service is included.
