# Package Sources, Entitlements, and Official Free Trial

## What is free

BedrockLauncher is a free launcher project. It does not make the commercial Minecraft for Windows game free, grant a Microsoft Store entitlement, or provide a licence. A user must use an account that is eligible to install and run the selected package.

There is one legitimate no-cost play route for Windows: Mojang's official, time-limited Minecraft Bedrock free trial.

- Official page: https://www.minecraft.net/en-us/free-trial
- The official page lists a Bedrock download for Windows 10/11 and asks users to sign in with a Microsoft account.
- Mojang describes the trial as the core Survival experience and says it provides approximately 100 minutes of in-game time (about five in-game days; duration may vary by device).
- The official FAQ says offline play is supported, online servers are disabled, and LAN multiplayer is allowed.

The Versions page now has an **Official Free Trial** button. It opens the official Minecraft trial page in the default browser. The Microsoft/Minecraft flow handles the actual trial download, sign-in, installation, and entitlement checks. This button is not a hidden package mirror and does not claim that the trial is an unlimited full-game licence.

## Real package-source routing

The version source manager is the existing `VersionManager` in `modules/BedrockLauncher.UpdateProcessor/Handlers/VersionManager.cs`. It keeps discovery metadata separate from the actual package transfer:

| Package path | Catalogue/source metadata | Actual package transfer |
| --- | --- | --- |
| UWP / Microsoft Store | Curated version metadata plus optional Microsoft Store/Windows Update discovery | Resolves the selected update identity through the existing StoreNetwork path when the user downloads; then uses the resumable HTTP downloader |
| GDK | GdkLinks discovery metadata plus a persisted exact Minecraft-to-package identity | Downloads only URLs whose package filename matches the exact required GDK identity; no UWP fallback |
| Official free trial | Mojang's official free-trial page | Browser hand-off to the official Windows 10/11 download/install flow; not imported as an arbitrary historical package |

### Important source and licence rules

1. Catalogue presence is not proof that a package can still be downloaded. The remote source can expire, become unavailable, or reject the request.
2. A download completing is not proof of entitlement or successful Windows deployment. Windows package registration and the Microsoft account/store flow remain authoritative.
3. Every cached package remains tied to its exact package type, identity, architecture, and version. Never substitute a nearby release when the requested package is missing.
4. Community catalogue metadata is not the same thing as an official Microsoft publisher signature. Local SHA-256 receipts detect later cache changes; they do not prove the original download was published by Microsoft.
5. The launcher must not bundle copyrighted game files, distribute account tokens, bypass Store/Xbox ownership checks, or advertise paid releases as freely playable.
6. If a version cannot be sourced or installed legitimately, show it as unavailable and explain why.

## How a user gets the official free trial

1. Open **Versions → Official Free Trial**.
2. On the official Minecraft page, choose **Minecraft Bedrock Edition → Windows 10/11**.
3. Sign in with the Microsoft account requested by the official flow and follow its install instructions.
4. Launch the installed trial through the official Windows/Minecraft entry point. The trial's own restrictions apply.

The launcher button only opens the official page. It does not silently download a trial package, alter Store licensing, or promise that the trial appears as a historical version in the version cache. Deeper trial-package detection can be added only after it is tested against the actual official trial package on Windows.

## Remaining implementation phases

### M3 — Windows install, switch, and play verification
- Complete real Windows tests with an eligible Microsoft Store account and actual UWP package.
- Test GDK only on a Windows machine with the required Gaming Services runtime and an eligible package.
- Verify exact version registration, relaunch from cache, profile selection, package-family replacement prompts, and world-data preservation.
- Confirm that failed registration never enables Play.

### M4 — Reliability and recovery
- Exercise cancellation, interrupted transfer, expired URLs, offline refresh, insufficient disk space, corrupt package, failed registration, and rollback.
- Add Windows integration coverage for the Appx/GDK deployment boundaries that hosted unit tests cannot emulate.
- Keep the manual Windows smoke checklist current and require it before a release.

### M5 — Package Source Manager hardening
- Show the resolved source and last successful/failed resolution time for each version.
- Add provider health diagnostics without treating a failed HEAD request as proof that a download URL is dead.
- Add trusted publisher/signature verification where the upstream package format exposes verifiable publisher identity.
- Keep source allow-lists, exact package-identity checks, provenance, and clear unavailable reasons.

### M6 — Trial and account-state experience
- Test official trial installation and launch on supported Windows builds.
- Detect an already-installed official trial only through supported Windows package APIs; never infer ownership from a cached archive.
- Clearly distinguish trial, owned Store install, cached package, and unavailable package in the UI.
- Keep the trial entry as an official hand-off unless Windows testing proves a supported direct integration.

### M7 — Release readiness
- Finish a clean Windows build, regression suite, and manual smoke run.
- Publish a signed launcher installer and checksums, document supported Windows versions, and provide backup/rollback guidance.
- Publish a release only with a truthful compatibility matrix and no unverified "all versions" or "free full game" claims.
