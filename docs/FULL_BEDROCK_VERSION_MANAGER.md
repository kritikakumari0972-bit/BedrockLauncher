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
- Phase 2 does not yet implement download resumption, a full package signature/hash manifest, or a dedicated version-details screen.
- A successful Windows CI build/test run has not yet been observed for the current branch.

### M2 — Version library and downloads
- Present all known versions with search and filters.
- Show cached/downloadable/unavailable states.
- Verify payload size and package identity before promoting a download into the cache.
- Resume or safely restart interrupted downloads where supported.

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