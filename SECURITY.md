# FreeAppLock v8.13 security model

FreeAppLock discourages casual access to windows within an existing Windows session. It is not Windows access control and does not prevent a person with control of that session from ending the utility, changing user-owned files, reading application data, renaming executables, or using other application surfaces.

Passwords use per-password random 16-byte salts and PBKDF2-SHA256 (310,000 iterations, 32-byte hashes), with fixed-time verification. This protects against simple password recovery from hashes; it does not prevent deletion or replacement of settings. Admin and app passwords allow any nonempty length, including one character. Existing passwords remain compatible.

Rules match process names. Approvals are keyed by HWND, checked against PID and name during scans, and invalidated on observed destruction events. Event delivery and polling are best effort. Owned dialogs are inspected too, but applications may refuse or ignore minimize requests. Status describes observed window state, not secure isolation.

Settings and attempt logs are stored in local application data and are editable by the same Windows user. Admin cooldowns are persisted; wall-clock changes and file edits can bypass them. `settings.json.bak` contains the preceding saved settings and may contain prior password hashes and retry counters. Do not automatically roll back a backup after password or policy changes.

Default app lockout preserves the target process and applies a 60-second in-memory per-window cooldown. The separate admin-verified opt-in termination setting can destroy unsaved work across every window in a target process. It remains off by default.

Release binaries are unsigned. There is no privileged service, driver, pre-launch enforcement, screenshot blocking, tamper protection or installer. Use separate Windows accounts and lock Windows to protect sensitive information. Elevated/hosted apps, previews, accessibility surfaces, and Windows session ending require additional compatibility checks.

## v8.12 browser handling

Grouping is a presentation change and does not authorize every process in the group. Recognized browser-owned dialogs share an approval only along visible/minimized owner links within the same PID; independent primary windows remain separate. Browser policy never selects the process-termination branch for recognized names, even if termination is enabled for other apps. Unknown/renamed browsers are subject to the ordinary optional termination setting; keep that setting off for them.

Initial dialog activation uses standard foreground activation, a short foreground-responsiveness probe and a temporary input-thread attachment only when needed, always detached in finally. It does not modify global focus-lock settings. Foreground restrictions, secure desktops and higher-integrity apps remain compatibility limits.
