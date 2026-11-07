# Release and updater contract

1. Increment `<Version>` in the Windows csproj using three numeric components (for example 1.6.1). Keep the WiX `UpgradeCode` and component GUIDs stable for upgrades.
2. Build/publish and run the EXE self-tests. Build MSI with `./build-installer.ps1`. Test install, upgrade and uninstall on Windows before recommending broad distribution.
3. Publish a Git tag `v<Version>` from the matching source commit.
4. Create a **draft** GitHub release and upload **TOMA-TOMA-Windows-Setup-x64.msi**. Keep that asset name stable; the repository is fixed in UpdateClient.cs.
5. Verify the server asset is uploaded with a `sha256:` digest and a valid size. Upload SHA256SUMS and release notes. Publish the release as stable/latest only after assets are ready.
6. Validate `--update-probe <output.json> <older-version> --download`: it must detect the new release, download and verify it without executing the MSI. Run with the current version and verify no update is required.

The updater trusts authenticated HTTPS GitHub release metadata for this specific repository; it does not provide an independent code-signing identity. It rejects unknown download/redirect hosts, missing digests, oversize or tampered downloads, prereleases and draft releases. No API key is shipped. The MSI is currently unsigned.

Startup checks have a seven-second timeout. If no newer validated release has ever been learned, network failures permit offline use. Once a newer version is cached, that requirement remains active offline. Closing/canceling the update gate exits the old app; canceling MSI installation does not satisfy the requirement. Already released 1.5.x binaries cannot be remotely changed to adopt this policy.

The updater is a distribution policy in an MIT-licensed open-source client, not DRM. A fork can change it. It does not prevent running arbitrary old source builds, uninstalling, or turning off network connectivity before a newer release has been learned.

For regression testing, `--update-preview` displays the real update gate while simulating a 1.5.0 client and using isolated preview cache. It never opens the normal player UI. `--self-test` and `--showcase` remain isolated developer modes.
