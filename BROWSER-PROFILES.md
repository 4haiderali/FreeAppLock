# Browser-profile protection

Supported companion browsers: Brave, Google Chrome and Microsoft Edge on Windows, based on Chromium 120 or newer. The extension controls regular windows in the profile where it is installed. It does not request tab URLs, history or page content. Private/incognito windows are not covered by this companion.

## Connect a profile

1. Install FreeAppLock 1.1.0 and open it.
2. In the browser profile you want to protect, open `brave://extensions`, `chrome://extensions` or `edge://extensions`.
3. Enable Developer mode, choose **Load unpacked**, and select `C:\Program Files\FreeAppLock\BrowserExtension` (or that folder under your chosen install location).
4. Open the **FreeAppLock Profile Companion** extension popup. Enter a profile name such as Work, select the correct browser, then choose **Register / update profile name**.
5. In the desktop app, open **Browser profiles**, select the discovered profile, and choose **Protect / edit profile**. Set its timers and confirm with your admin password.
6. Keep **Use profile-only mode** checked if you want other profiles to stay free. It removes the browser's previous app-wide rule, while retaining any other explicitly protected profile rules. This switch requires a live companion connection; offline rules can still have their timers edited.

Repeat only in profiles you want the desktop app to recognize. Registration does not itself enable protection; the desktop admin-approved rule does. Each profile has a separate randomly generated identity stored in its extension storage. Passwords stay in the desktop app and are never sent to the companion.

## Behavior

- Passwords authorize an individual browser window. Tabs remain together, and separate windows keep separate approvals.
- You can configure an away delay, a maximum unlocked session and an immediate-minimize lock for each registered profile.
- The desktop app labels a saved profile **Extension offline** when it is not receiving snapshots. This does not establish whether the browser is closed or the companion is disabled.
- Connection loss, extension removal or closing FreeAppLock makes profile protection unavailable. The companion releases only windows it minimized; it does not restore windows you minimized yourself. The popup badge shows an unavailable state. This is reactive privacy assistance, not tamper resistance.
- The native host exits when the desktop app closes, allowing upgrades to replace it. The companion backs off before reconnecting. After opening the app again, open the extension popup to retry immediately or wait up to 30 seconds.
- Quit FreeAppLock before upgrading; keep the extension in its installed folder so later installers update its files. Reload the extension after an app/companion upgrade if the browser still has an older service worker loaded.

## Why an extension?

Multiple browser profiles can share a process. Process IDs alone cannot reliably associate windows with profiles. The companion uses the browser's profile-local window API and a registered native messaging host instead of guessing from a PID, title or email address.

## Scope

No Chrome Web Store listing is created by this release; the supplied companion is loaded unpacked per profile. Browser policies may prohibit developer-mode extensions. The app-wide browser rule remains available if you do not want a companion or need broader native window coverage. Never treat a green badge as OS-level isolation.
