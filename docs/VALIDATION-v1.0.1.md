# FreeAppLock v1.0.1 validation

Verified on 13 November 2026. Release build passed with zero warnings/errors. All 17 core and 17 Windows integration groups passed, including native password-input focus and the new public-version label in the main header, title bar and tray tooltip. The test script verified the real settings-file digest remained unchanged.

Public app/product/installer version is 1.0.1. Native Windows file/internal upgrade version is 8.14.0.0 so this release can replace earlier local v8.13 installations. The production AppId is unchanged. A separate machine installer key tracks the internal upgrade sequence; Windows Apps displays public version 1.0.1. Future releases must increase both the public version and the internal file version.

The version-reset installer is tested with a disposable registered v8.13 installation in a work directory, using isolated shortcuts, registry paths and installer mutex. Production Program Files/UAC and actual Windows login remain separate compatibility checks. Browser and focus checks use disposable fixtures; no live browser profile is changed.

Isolated installer checks passed: legacy v8.13 custom-directory installation upgraded to product v1.0.1/file build 8.14.0.0; both public and internal metadata were verified. Same-version repair succeeded, an older internal build was rejected, and uninstall cleaned the isolated registration/files. Real user settings remained unchanged.
