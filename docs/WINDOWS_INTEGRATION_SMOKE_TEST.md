# Windows Integration Smoke Test — Bedrock Version Manager

This checklist covers Windows behavior that cannot be proven by the platform-independent regression test project alone. Run it on a disposable Windows 10/11 test account with a supported architecture. Do not use a valuable Minecraft world as test data.

## Preconditions

- Record Windows edition/build, launcher commit, CPU architecture, and installed Minecraft package family/version.
- Confirm the test account can install the selected Store package. For GDK versions, confirm Gaming Services is installed and functional.
- Create a disposable test world and export/copy it before testing package replacement.
- Keep the launcher logs for each case. Record the selected Minecraft version and exact package identity.

## Version Library and cache

1. Open Settings → Versions. Confirm search and Release/Beta/Preview, UWP/GDK, Cached only, and Show unavailable filters work.
2. Open Details for both a UWP and GDK catalogue row. Confirm the displayed version, channel, package type, architecture, source, availability reason, and package identity match the selected row.
3. Cache a downloadable UWP version. Confirm the final extracted folder appears only after extraction and manifest validation succeeds.
4. Cache a GDK version. Confirm the MSIXVC filename matches the exact required package full name and the local SHA-256 receipt is created.
5. Close and reopen the launcher, then inspect GDK version details. Confirm the cache is reused and its local receipt is checked before reuse.
6. In a disposable test copy only, change a byte in a same-length cached GDK file. Confirm the next cache/install attempt rejects the receipt and downloads the package again. Restore the real cache from a clean download.
7. Cancel a download. Confirm no final cache file or installed state is created from an incomplete transfer. Where a stable ETag/Last-Modified value is available, retry and confirm the partial transfer resumes; otherwise it safely restarts.

## UWP install, switch, and launch

1. Install a cached UWP version. Confirm Windows registers the selected package family and exact manifest version from the launcher-owned version folder.
2. Confirm the launcher does not report success if the registered package identity or install location differs from the selected version.
3. Confirm save data is redirected only after exact registration validation. Open the game and verify it sees the disposable test world.
4. Add a second UWP version and switch to it. Confirm replacement requires explicit approval and save data is preserved before the previous package is removed.
5. Decline a replacement prompt. Confirm the currently registered package remains intact and the selected version is not marked installed/ready.
6. Switch back to the first cached version. Confirm it is installed from local files without a network download and only that selected version launches.
7. Simulate an unavailable download source in a test environment. Confirm the launcher reports failure and never silently installs or launches another version.

## GDK install, switch, and launch

1. Install a cached GDK package with Gaming Services. Confirm the registered package full name exactly equals the selected required identity.
2. Confirm Install does not activate Minecraft. The Play action must be the operation that launches the selected package.
3. Switch between two cached GDK versions. Confirm the launcher asks before removing an occupying package and does not activate a different registered version.
4. Test a deliberately invalid or truncated MSIXVC copy in a disposable cache. Confirm Windows deployment rejects it, the launcher removes the rejected package file and its local receipt, and a later retry downloads it again.
5. Confirm the correct per-installation save-data directory is linked and the disposable world remains readable after switching.

## Recovery and failure cases

1. Start a package replacement with a non-empty com.mojang directory. Confirm the launcher moves it to a backup outside the Windows package data tree before removal.
2. Force a backup destination failure in a disposable test account. Confirm package removal is blocked and original save data remains in place.
3. Import a recovery backup. Confirm the staging folder is promoted only after all file hashes and the aggregate manifest have been verified; the source backup remains intact.
4. Cancel recovery during copy or introduce a same-size staged-file change in a test harness. Confirm no final Recovery Data installation is created and the source backup remains unchanged.
5. Force a registration failure. Confirm install does not report success, does not redirect save data to an unregistered package, and does not launch another version as fallback.

## Result recording

For every case, record PASS / FAIL / NOT RUN, launcher commit, Windows build, exact version/package identity, relevant log excerpt, and any world-data comparison result. Do not call the release ready until all required cases pass on a real Windows machine.