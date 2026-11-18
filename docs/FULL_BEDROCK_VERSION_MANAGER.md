# Full Bedrock Version Manager

## Goal

Extend the existing BedrockLauncher implementation so a user can select a Minecraft for Windows / Bedrock release, download its exact package, install or activate it, and launch the real game. Preserve the existing UWP and GDK pipelines; do not replace them with a mock installer.

## User workflow

1. Refresh the version catalogue.
2. Search and filter versions by release channel, package type, architecture, and availability.
3. Select a version and inspect its exact package identity and source.
4. Download the exact package into that version's cache.
5. Validate the downloaded payload before installation.
6. Back up or isolate save data before changing the registered Windows package.
7. Register/activate the selected package using the existing UWP or GDK path.
8. Validate the registered package identity and launch Minecraft.
9. Reuse cached package files when switching back to a previously downloaded version.

## Reality and platform constraints

- This repository targets Minecraft for Windows on Windows, not the Android Bedrock APK.
- “Any version” means every version for which a valid, compatible package and a permitted download/install path are available. The launcher must never fabricate a download URL or silently substitute a different version.
- Windows package identity and package-family registration constrain side-by-side installs. Multiple package archives can be cached, but switching the active package may require replacing the currently registered package in that family.
- UWP and GDK are distinct package pipelines. A version must stay bound to its recorded package type and exact package identity.
- Some older releases may be unavailable, architecture-incompatible, blocked by package dependencies, or require Microsoft account entitlements. Those versions must be marked unavailable with a clear reason rather than shown as installable.
- Save data must be protected before package removal/replacement. Never claim worlds are safe unless the backup/restore operation has succeeded.

## Existing implementation to preserve

- VersionDownloader and VersionManager version catalogues.
- PackageHandler UWP install/launch flow.
- GdkLaunchPipeline and PackageHandler.Gdk.cs exact-package GDK flow.
- Per-version package cache and installation profiles.
- Existing package identity validation and save-data safeguards.

## Phase 1 implementation notes

The first catalogue foundation is now committed on `feature/full-bedrock-version-manager`.

Completed in this phase:
- Version entries are tagged at load time with their source: community catalogue, Microsoft Store, GdkLinks manifest, or persisted GDK catalogue. The existing source-specific JSON layouts remain backward compatible.
- Local validation distinguishes catalogue presence from a listed exact GDK download resource. It checks the version, update identifier, release channel, package type, architecture, GDK package family, GDK architecture, package-version mapping, and exact URL/package identity.
- UWP entries are described as catalogued only; the Microsoft Store package link is still resolved by the existing Store pipeline when a download is requested.
- Catalogue writes use a temporary file before replacement, and a malformed or unexpectedly empty community refresh does not replace a populated cache.
- The Store refresh no longer deletes its cached catalogue before collecting refreshed results.
- Regression tests cover source tagging, UWP/GDK coexistence, duplicates, unavailable GDK mappings, exact package resources, and unsupported architectures.

Verification note: tests have been added, but a successful Windows build/test run has not yet been observed for this branch. A catalogue resource being listed is not proof that the remote download succeeds; the actual package must still be downloaded and validated.

## Implementation milestones

### M1 — Catalogue and availability
- Audit UWP, Store, and GDK catalogue loading.
- Add explicit availability/source metadata and validation.
- Keep cached catalogue data when refresh fails.
- Add tests for duplicate versions, UWP/GDK coexistence, missing package identities, and unsupported architectures.

### Phase 2 implementation notes

The first version-library slice is committed on the feature branch.

Implemented:
- The Versions page now lists catalogued versions, rather than filtering the list to only versions with local playable files. Imported local versions remain visible; synthetic latest-channel shortcut rows are not shown in the library.
- Search matches the version/display name, architecture, package type, and catalogue/update identifier.
- Existing Release/Beta/Preview and UWP/GDK filters are retained. Added Cached only and Show unavailable filters.
- The current PC architecture is passed to catalogue validation. Other architectures are marked unsupported for this PC and can be revealed with Show unavailable.
- Each row displays availability/cache status and catalogue source. Unavailable entries are not offered a Cache action.
- Cache uses the existing real package flows: UWP downloads and extracts through the Store pipeline; GDK downloads the exact catalogued MSIXVC identity into its per-version cache without registering or launching it.
- Cache state is refreshed in the UI after the operation. Repair remains available for cached/ready versions.
- The downloader now tolerates a missing current-architecture Release or Preview row instead of throwing while constructing the latest-channel shortcuts.

Important limits:
- A UWP row marked Catalogued still has its Microsoft Store download link resolved at download time; catalogue presence is not proof that the Store will provide the package.
- A GDK row with a listed resource is not proof that the CDN is reachable. The package is checked locally for an obvious text/error response and Windows performs package validation during registration.
- Phase 2 now includes resumable direct HTTP downloads where the server provides a stable validator. A trusted package signature/hash manifest and a dedicated version-details screen are still outstanding.
- A successful Windows CI build/test run has not yet been observed for the current branch.


#### Resumable direct-download slice

- The shared direct HTTP downloader used by both UWP Store-resolved packages and GDK package URLs now retains interrupted bytes in a temporary `.download` file.
- It resumes only when the URL fingerprint matches and a strong ETag or Last-Modified validator is available; the original URL (which may contain a temporary token) is not written to the sidecar.
- Resume requests use `Range` and `If-Range`. A valid `206` must begin at the exact saved offset. If the server returns a full `200`, an invalid range, a changed validator, or `416`, the downloader safely restarts instead of appending mismatched bytes.
- The final cache destination is replaced only after the known response length matches. This is transfer-completeness validation, not a cryptographic authenticity check; a trusted SHA-256/signature catalogue remains future work.
- Regression tests cover interrupted continuation, a server returning a full response to a range request, and safe restart when the server provides no stable validator.

### M2 — Version library and downloads
- Present all known versions with search and filters.
- Show cached/downloadable/unavailable states.
- Verify payload size and package identity before promoting a download into the cache.
- Resume or safely restart interrupted downloads where supported.

### Phase 3 first integration slice

The Versions library now has **Add to Play** and **Install** actions for catalogue entries whose package can be attempted or is already cached.

- **Add to Play** creates a profile installation for the exact selected version UUID, or selects the existing matching installation instead of creating duplicates. It selects that installation and returns the user to the Bedrock Play screen.
- **Install** creates/selects the same profile entry, then calls the existing UWP `InstallPackage` or GDK `InstallGdkPackage` pipeline with that installation's save-data directory. It does not launch Minecraft.
- The UWP route still downloads/extracts as needed, checks the package manifest, confirms package-family replacement when necessary, registers the package, and redirects save data.
- The GDK route still checks Gaming Services, downloads the exact required MSIXVC package, confirms/removes an occupying package when needed, deploys through Windows, validates the registered package, records the installation, and prepares save data.
- A successful install returns the user to the Play screen with the selected installation. GDK install now returns an explicit success result, so a handled failure is not treated as a successful installation.
- The existing Play button remains responsible for actual launch. The existing package-family replacement confirmation and save-data safeguards are not bypassed.
- Uncached unavailable entries and unsupported architectures do not expose these actions. A cached package can still be used after its remote source disappears, but Windows must validate it during installation.

The UI integration still needs a successful Windows build and end-to-end testing before it can be called verified.

### Phase 3 install reliability slice

- The GDK package pipeline now exposes an explicit `InstallAsync` operation. It ensures the exact required package is registered and validated, then prepares the selected installation's save-data redirect. It does not activate Minecraft.
- The Windows GDK `InstallGdkPackage` wrapper calls this install-only operation and reports an explicit success/failure result to the library UI.
- Regression tests cover exact-package install-only behaviour, reuse of an already registered exact package, save-data preparation order, save-data failure, declined package replacement, and rejection of a package Windows registered with the wrong identity. Install-only tests assert that no activation call occurs.
- UWP package extraction now takes place in a unique staging directory. The staging manifest must identify the selected release/preview family, a four-part package version, and the exact catalogue architecture before the staged folder can replace the existing version folder.
- The existing version directory is moved aside and restored if promotion fails. Failed or cancelled extraction removes only the staging directory, not the previous version's content.
- The UWP manifest identity validator has six focused tests and is included in the pipeline regression test project.

Windows limitation: these tests exercise platform-independent package-selection and manifest-validation logic. Actual Windows Appx registration, GDK Gaming Services deployment, and world-data migration still require Windows end-to-end tests.

### M3 — Real install, switch, and play
- Route each selected version through its matching existing pipeline.
- Require confirmation before replacing a registered package.
- Back up save data and verify restoration.
- Validate exact installed package identity before launching.

### M4 — Reliability and release
- Add Windows integration tests for install, switch, relaunch from cache, cancel, failed download, and failed registration.
- Build and run the solution on a supported Windows runner.
- Publish only after tests pass; report any untested platform-specific behaviour clearly.

## Acceptance criteria

- Selecting a version never launches a different version as a fallback.
- A cached version can be selected again without downloading its package again.
- A failed or cancelled operation does not mark a version as installed.
- A package replacement is never silent.
- World/save data is backed up and verified before a destructive package change.
- The UI distinguishes installed, cached, downloadable, unsupported, and unavailable versions.
- Play is enabled only when the exact selected package is validated and ready.


### Phase 3 save-data removal guard and CI runtime fix

- Added a shared `SaveDataBackupMover` used by the actual Windows package-removal guard. Non-empty `com.mojang` data is moved atomically to a unique backup directory outside the package's data tree and on the same volume.
- The mover rejects a backup destination inside the package folder and refuses cross-volume moves. It never falls back to a recursive copy that could be interrupted halfway through.
- If save-data inspection, link detachment, or backup creation fails, package removal now fails closed with a visible package-manager error. The currently registered package is not removed. A reparse point with an unresolved target is also treated as unsafe.
- The package-removal progress state is reset even when backup protection aborts before Windows is asked to remove anything.
- Regression tests cover moving world files intact, rejecting a backup destination inside the package tree, preserving the original folder when backup creation fails, and skipping unnecessary backups for empty folders.
- Both Windows CI workflows now provision the .NET 10 Windows Desktop runtime used by the current PostSharp build host; the launcher still targets .NET 8. This addresses the observed runner error where PostSharp could not locate `Microsoft.WindowsDesktop.App 10.0.15`.

Verification note: these changes have been committed, but their Windows CI run has not yet been observed to pass. The tests validate the filesystem mover on the CI runner; they do not replace an end-to-end test against real Windows package removal, Appx/GDK registration, or Minecraft world migration.


### Phase 3 recovery import reliability

- The existing Settings → Backup Save Data recovery flow now uses a transactional copier rather than writing directly into the final installation folder.
- Recovery first inventories real files and directories, ignores reparse points rather than following them, and copies into a unique staging folder beside the intended installation data directory.
- It checks copied file counts and lengths, confirms the source file list/folder structure did not change during the copy, and only then promotes the staging folder with a same-volume directory move.
- A pre-existing destination is never overwritten. A failed/cancelled copy removes its staging folder and leaves the source backup untouched; no Recovery Data installation is created until promotion succeeds.
- Recovery logs the imported file count, folder count, and copied bytes. The original saved data remains in place so the user can retain it as a backup.
- Regression tests cover successful folder/file preservation, empty directories, cancellation cleanup, source mutation during copy, destination collision, and overlapping source/destination paths.

Verification note: the earlier Windows build and pipeline tests passed on commit `0b65eef`. This recovery-copy slice is newer and is awaiting its own Windows CI build/test run. The copier performs structural and byte-length verification; it is not a cryptographic authenticity check for the original world files.


### Phase 3 SHA-256 recovery integrity

- Recovery copies now hash the exact source bytes with SHA-256 while streaming them into the staging folder. Source files are opened with read-only sharing during the copy so concurrent writers cannot modify the active file stream.
- The staged file is independently hashed and compared with the source-stream digest. Before promotion, the complete staged tree is enumerated again and each staged file's SHA-256 is checked a second time, detecting same-size changes to the staging area.
- The copier returns a deterministic SHA-256 manifest fingerprint over sorted relative file paths and their per-file SHA-256 hashes. The recovery handler writes this fingerprint to the diagnostic log together with file count, folder count, and copied bytes.
- Added tests for repeatable fingerprints and same-size staged-file tampering. A tampered staging copy must fail and be removed without modifying the original save data.
- This digest is an integrity fingerprint for the recovery operation, not a signed authenticity claim: it does not prove that the original world data is malware-free or came from Microsoft.
