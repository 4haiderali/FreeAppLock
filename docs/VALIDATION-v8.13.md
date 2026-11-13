# FreeAppLock 8.13 validation - 4 November 2026

Change: both admin and app password setup/change accept any nonempty password. The configurable minimum-length argument and eight-character call sites were removed. Blank passwords remain rejected, and confirmation matching remains required.

Verification: Release build with zero warnings/errors; 17 core and 16 Windows integration groups passed. Added a modal-dialog regression for Create admin password, Create app password, Change admin password and Change app password, verifying blank rejection, mismatched confirmation rejection and acceptance of a single character. The existing grouping, browser boundary, native focus, restoration, hashing and persistence checks remain passing. The test script verifies the real settings-file digest remains unchanged.

Self-contained x64 publication and installer compilation passed for the delivered build. Installer identity/upgrade behavior and settings schema are unchanged from v8.12. No real passwords were changed and no production installation was performed.
