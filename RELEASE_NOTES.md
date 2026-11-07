# v1.6.1 — Install once, stay up to date

Download **TOMA-TOMA-Windows-Setup-x64.msi**, run the Windows installation wizard, then use the desktop/Start Menu shortcut. Do not download Source code.zip to install the app.

- New per-user MSI installer, shortcuts and Windows uninstall entry.
- Startup and periodic update checks. When a verified newer stable release is detected, update or exit before using the old application.
- Official GitHub-only installer download with SHA-256 and size verification; no silent installation.
- Themes, lyric cache and timing preferences stay separate from program files.
- Existing 1.5.x users must install this MSI once; those older binaries have no updater yet.

Network failure permits offline use only if the app has not already learned of a newer required release. The installer is unsigned. This release contains Windows code and installer only; Android is unchanged.

## Validation
75 executable self-tests pass. MSI schema/database and payload integrity were checked. Interactive install/uninstall and a real upgrade could not be confirmed in the automated environment (Windows Installer 1619/1622); please report installation issues with your Windows version.
