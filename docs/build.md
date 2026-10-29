# Building

Use .NET SDK 10.0.301 with the Android workload, JDK 21, Python 3.9 or newer,
Android platform 36 and build-tools 36.0.0. Builds run on Windows.
Set `JAVA_HOME` and `ANDROID_HOME` to your JDK and Android SDK directories.

```powershell
dotnet workload install android --skip-manifest-update
python tools/prepare_dependencies.py
$env:SOURCE_DATE_EPOCH = git show -s --format=%ct HEAD
pwsh scripts/build.ps1
python tools/verify_release.py artifacts/Minkyu-unsigned.apk
```

The output is `artifacts/Minkyu-unsigned.apk`. Sign a local build with your own key
to install it. A different key can't update the published app.

## Source layout

- `src/Minkyu.Android`: setup, APK preparation, song library and content provider.
- `src/CustomAlbums.Core`: MDM validation, chart timing and persistent imports.
- `src/CustomAlbums.Android`: game integration and adapted upstream chart conversion.
- `src/MuseDashUnlockDLC`: the separate local unlock mod, bundled by default.
- `src/LoaderPatcher`, `src/LoaderFixes`, `src/LoaderFixes.Android`: loader compatibility fixes.
- `references/LemonLoaderInstaller`: the AXML and APK signing code used by the app.

The build downloads the dependencies pinned in `dependencies.lock.json`, compiles
the mods and loader fixes, applies six patches to the upstream loader, and packages
them with the manager. No prepared game APK is a build input.

The checked-in Android reference bundle contains declarations and throwing stubs.
LemonLoader generates working wrappers on the phone. To refresh the bundle from
wrappers for the supported game:

```powershell
dotnet run --project tools/ReferenceAssemblies -c Release -- path/to/Il2CppAssemblies new-reference.zip
```

Review the output before replacing the bundle and updating its hash in the lock file.
Don't substitute PC wrappers or commit game files and charts.

## Tests and tools

```powershell
dotnet run --project tests/CustomAlbums.Core.Tests -c Release
dotnet run --project tests/CustomAlbums.MinkyuBridge.Tests -c Release
dotnet run --project tests/Minkyu.Library.Tests -c Release
dotnet run --project tests/Minkyu.ApkPreparation.Tests -c Release
```

These host tests cover chart timing, archive validation, imports, deletion,
transactional library synchronization and OBB verification. Android calls are
stubbed; they don't verify setup or gameplay on a phone.

APK preparation tests also cover package/version rejection, binary manifests,
truncated signer input and saved-key reuse. To include a full host preparation
with your own supported APK after building the payload:

```powershell
$env:MINKYU_TEST_GAME_APK = 'path/to/game.apk'
dotnet run --project tests/Minkyu.ApkPreparation.Tests -c Release -- artifacts/apk-tests.json dependencies/loader
```

To test local charts as well:

```powershell
dotnet run --project tests/CustomAlbums.Core.Tests -c Release -- path/to/charts artifacts/parser-tests.json
```

The CLI helps inspect a rejected archive without starting the game:

```powershell
dotnet run --project src/CustomAlbums.Cli -c Release -- inspect path/to/song.mdm
dotnet run --project src/CustomAlbums.Cli -c Release -- scan path/to/charts
dotnet run --project src/CustomAlbums.Cli -c Release -- import path/to/song.mdm path/to/library
```

CLI imports copy the source. The Android file picker moves it when the provider
allows deletion, after saving and verifying the imported copy.

## Game compatibility

Setup requires `com.prpr.musedash` 6.7.0, version code 626, and checks the ARM64 game
library and metadata hashes. Other builds, split APKs and existing loaders are
rejected. An untouched Play Store installation has not been verified.

Keep the early JNISharp OBB copy and the later Unity bridge: Unity needs the OBB
before game scripts run, and the bridge handles later synchronization and recovery.
A process-only verification receipt avoids hashing the same OBB twice. The native
value-copy and achievement hooks work around ARM64 interop ABI problems.

MuseDashUnlockDLC depends on Harmony, LemonLoader and the generated game wrappers. Its
`MinkyuLocalUnlock/Enabled` MelonPreferences entry defaults to `true`. It returns
true from selected official song, difficulty, role, elfin, skin and VIP availability
checks. It also blocks non-GET requests whose URLs contain `leaderboard`,
`high-score` or `statistics`. It doesn't write purchase data or supply resources.
Custom-song availability and score/achievement isolation are separate and remain
active when MuseDashUnlockDLC is disabled.

Before a release, check setup, repeated launches, import and source-file removal,
deletion, original and custom songs, touch input, results and return to selection
on a device. Check MuseDashUnlockDLC separately. Host tests don't cover native hooks,
JNI permissions, installation, audio synchronization or release trimming on a phone.

## Releases

Increase `ApplicationDisplayVersion` and `ApplicationVersion` in the manager
project. Update `.github/release-notes.md`; this is the single source for the next
release description. Keep it about installation and player-visible changes.

Push the matching new version tag (for example, `minkyu-0.1.0`). Actions tests and builds the source at that tag,
checks the APK payload, then signs it in a separate tag-only job. The workflow
creates a draft prerelease with `Minkyu.apk`, `SHA256SUMS`, `build.json` and a build
attestation. Review that draft and publish it with:

```sh
gh release edit minkyu-X.Y.Z --repo ashleyyyww/Minkyu --draft=false
```

Branches and pull requests have a read-only token and no signing step. The release
job uses the unsigned APK and mod DLLs from the same workflow run. Never reuse a
tag for another APK or replace an old APK with one built from a different commit.

Signing requires all three encrypted Actions secrets:
`MINKYU_RELEASE_KEYSTORE_BASE64`, `MINKYU_RELEASE_PASSWORD` and
`MINKYU_LEGACY_KEYSTORE_BASE64`. Keep both keys and `build-support/minkyu.lineage`.
The lineage preserves migration from the earlier test certificate. Signing stops
if a secret or the lineage is missing, or if the output has a different release
certificate. Keys must never be committed.

The manager has a separate private key for preparing the game. Uninstalling Minkyu
removes that key and its library. A new installation cannot update a game signed
by the previous installation.

To verify a published APK:

```sh
gh attestation verify Minkyu.apk --repo ashleyyyww/Minkyu
```

The attestation identifies the source commit and workflow. Byte-for-byte APK
reproducibility across machines has not been established.
