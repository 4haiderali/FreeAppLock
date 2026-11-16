# FreeAppLock v1.1.0

- Per-application rules: away delay, maximum unlocked session (including while focused), and immediate locking on minimize. Each timer can be disabled independently. Existing rules inherit their previous global away delay and minimize behavior until edited.
- **Locked apps** tab lists every saved app/profile rule, including closed apps and disconnected profiles. Add an executable even when it is not running; edit timing, lock now, or remove protection.
- **Browser profiles** tab and optional companion for Brave, Chrome and Edge. Register profiles explicitly, protect only selected ones, and keep authorization per browser window. The companion uses no tab-content permissions and does not close tabs. Private/incognito windows are outside its scope.
- Native messaging bridge uses a stable allowlisted extension identity and a current-user/session-scoped named pipe. It never lets a browser message change desktop rules or receive password material.
- Profile-only mode can remove an existing app-wide browser rule after admin verification, so other profiles remain free. Disconnected profiles are labeled unavailable; offline editing cannot silently switch off the app-wide rule.
- Settings schema, passwords, icon, install location and installer identity are retained. Internal Windows file version advances to 8.15.0.0; public product version is 1.1.0.

See BROWSER-PROFILES.md for per-profile setup and docs/VALIDATION-v1.1.0.md for verification scope. The companion is distributed unpacked; no store listing or automatic browser installation is performed.
