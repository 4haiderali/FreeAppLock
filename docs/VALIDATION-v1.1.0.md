# FreeAppLock v1.1.0 validation

Feature implementation and verification: 13 November 2026. Release audit/publication: 16 November 2026.

## App and policy checks

- Release app and native-host builds succeeded with zero warnings/errors.
- 28 core regression groups passed, including old-settings migration, independent per-app timers, exact session boundaries, minimize behavior, persistence rollback, profile/window/connection isolation, cooldown, closed-window identity cleanup, message bounds and an isolated current-user pipe round trip.
- 3 extension tests passed: only selected-profile windows are changed; original tabs/window state are preserved; disconnect releases only companion-minimized windows; event bursts are coalesced rather than building an unbounded sync queue.
- 20 Windows UI scenarios passed, including saved closed-app/offline-profile rules, selection retention, rule-editor validation and actual native per-app session/minimize behavior. In the final run, two native keyboard-injection assertions were explicitly skipped because another foreground application prevented focus acquisition. Approval/isolation portions still ran. An earlier full UI run with actual keystrokes had passed before the foreground environment changed. No claim is made that input can be forced away from an active remote-desktop client.
- Real settings-file digests remained unchanged during the automated fixture tests.

## Real browser integration

A renamed, isolated Brave runtime and fresh disposable browser data were used so the existing protected Brave process and real profiles were not targeted. Two profiles shared that browser runtime. The allowlisted extension connected through the actual native messaging executable to a desktop fixture with in-memory rules. A separate test pipe prevented access to the installed app's live rules.

Verified: profile IDs and window sets were distinct; only the selected Work profile minimized; the other profile stayed free; the desktop password prompt granted one window; original tab IDs were unchanged after unlock; the maximum session limit relocked the selected window without deleting tabs. Test native-host registrations were removed afterward.

A reused disposable profile run did not converge after extension code changes. The final successful run used fresh data. Reload the unpacked extension after an upgrade; cached service workers must not be assumed to reload automatically from replaced files.

## Installer

An isolated registered v1.0.1 install upgraded in place to v1.1.0. Verified the app, native host, allowlisted host manifest, companion files and setup guide were installed; internal upgrade version is 8.15.0.0. Uninstall removed the upgraded files and registration. The real settings file was unchanged. Test mode substitutes work directories and dedicated registry/mutex identifiers; it does not test production UAC or machine native-host registration.

## Scope

Companion support is for regular Brave/Chrome/Edge windows with the extension enabled and connected. Private/incognito windows and disabled/removed extensions are outside that scope. Browser messages cannot enable desktop rules or receive password material. Same-user tampering remains possible. No store listing or automatic extension installation is included. Actual Windows logoff/shutdown and arbitrary third-party app compatibility remain separate checks. See BROWSER-PROFILES.md and SECURITY.md.
