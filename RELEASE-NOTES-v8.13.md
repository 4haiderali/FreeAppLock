# FreeAppLock 8.13.0 - flexible password length

Removes the eight-character requirement for both admin and app passwords in first-run setup and password changes. Any nonempty password is accepted, including one character. Confirmation is still required, and empty passwords are rejected. Updated every setup/change description and removed the configurable minimum-length validator so these flows use the same rule.

Password hashing, verification, retry policies, browser grouping/window behavior, focus, settings format, icon and installer upgrade identity are retained. Existing passwords are preserved; use Settings to change them if desired.

Validation: Release build, 17 core test groups and 16 Windows integration groups passed. The added test exercises all four setup/change dialog titles, checking empty rejection, mismatched confirmation rejection and one-character acceptance. Real settings were not changed by the tests.
