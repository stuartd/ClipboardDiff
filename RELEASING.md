# Releasing ClipDiff

The release contract is the Inno Setup installer and two portable ZIPs, not a
`bin` or raw `dotnet publish` directory. The recommended download is
`ClipDiff-<version>-win-x64-Setup.exe`, which bundles .NET and installs per-user
without elevation under `%LOCALAPPDATA%\Programs\ClipDiff`. Every ClipDiff Windows x64 package contains exactly:

```text
ClipDiff.exe
ClipDiff.ShellExtension.<sha256>.dll
```

Both files go in the same directory. The executable is the .NET application;
the DLL is the native two-file Explorer context-menu handler. Do
not ship either file on its own.

## Build a release locally

On Windows, from a normal non-administrator PowerShell in the repository root:

```powershell
.\scripts\create-local-release.ps1
```

The script requires the .NET 10 SDK and Visual Studio C++ build tools with a
Windows SDK, plus Inno Setup 6.3+ in its default `Inno Setup 6` location or
with `ISCC.exe` on PATH. It builds and tests the native extension, runs the .NET tests,
publishes both application variants in separate clean staging directories, and
validates both final folders and ZIPs, then builds the self-contained installer
from the validated payload before exposing any release assets. Output is:

```text
releases\ClipDiff-<version>-win-x64-Setup.exe

releases\ClipDiff-<version>-win-x64-self-contained\
    ClipDiff.exe
    ClipDiff.ShellExtension.<sha256>.dll
releases\ClipDiff-<version>-win-x64-self-contained.zip

releases\ClipDiff-<version>-win-x64-net10\
    ClipDiff.exe
    ClipDiff.ShellExtension.<sha256>.dll
releases\ClipDiff-<version>-win-x64-net10.zip
```

The self-contained package bundles .NET. The smaller `net10` package requires
the x64 .NET 10 Desktop Runtime and checks for `Microsoft.WindowsDesktop.App
10.x` when documenting compatibility; the base .NET runtime alone is not
sufficient for this WPF application.

The version defaults to the `Version` property in `Directory.Build.props`. A
specific version can be supplied when reproducing a tagged build:

```powershell
.\scripts\create-local-release.ps1 -Version 1.2.3
```

`-SkipNativeTests` skips only the native Shell integration tests; it still
builds and packages the DLL and runs the .NET tests. Do not use that switch for
a published release.

## Publish on GitHub

1. Merge the release changes into `main` and confirm **Windows build and release** succeeds.
2. Choose a semantic version such as `1.2.3`.
3. Create and push an annotated tag for that exact `main` commit:

   ```powershell
   git tag -a v1.2.3 -m "ClipDiff v1.2.3"
   git push origin v1.2.3
   ```

The **Windows build and release** workflow is the sole tag-release workflow.
It shares tag validation and package naming with the release script, checks regular
and prerelease version propagation on ordinary builds, and verifies published
executable metadata. Tags such as `v1.2.3-preview.1` preserve the prerelease suffix
in the product version and both ZIP names. The workflow validates the tag, runs the complete release script,
runs the installer smoke test, and creates a GitHub release with the
`ClipDiff-1.2.3-win-x64-Setup.exe` installer and both
`ClipDiff-1.2.3-win-x64-self-contained.zip` and
`ClipDiff-1.2.3-win-x64-net10.zip` attached. It will not publish if any build,
test, or payload validation fails.

## Installer verification and extension lifecycle

Follow [the Windows update testing checklist](docs/windows-update-testing.md).
CI runs `scripts/test-installer.ps1`, which installs the app, starts it, holds
native DLL images open, upgrades with unchanged and changed extension binaries,
checks registration, and uninstalls without a restart. This checks Windows image
locks; real Explorer menus and COM caching still require interactive testing.
Ordinary Actions builds expose the installer as `ClipDiff-win-x64-Setup`.

Setup signals `Local\ClipDiff.ShutdownForUpdate` and waits for the running app to
exit before replacing its EXE. It preserves preferences and an existing startup
opt-in, then offers relaunch. Pre-installer portable builds require a one-time
manual quit; their folders are not deleted.

SHA-256 filenames are independent of app versions. Reproducible native linking
lets unchanged binaries reuse their name. Installers never overwrite an existing
hashed DLL; the app registers the current path and attempts to clean obsolete,
unlocked DLLs on launch, skipping other sessions' claims. Preserve the native
COM IDs/interfaces, readiness event and delivery behavior so an older loaded
handler remains compatible during the transition. Never restart Explorer or
request a reboot for cleanup. Uninstall leaves loaded DLLs behind, retains
preferences, and removes only owned registrations pointing into its installation.
Development builds continue to use `ClipDiff.ShellExtension.dll`.

Automatic checks use GitHub's `releases/latest` API, which excludes drafts and
prereleases. Download opens that specific release page; no automatic downloading
or installation occurs.

## Install or update the portable build

Extract one whole ZIP into a directory owned by the current user and run
`ClipDiff.exe`. Use the `net10` ZIP only when `dotnet --list-runtimes` contains
an x64 `Microsoft.WindowsDesktop.App 10.x` entry. Do not move the DLL elsewhere
or run `regsvr32`; ClipDiff owns its per-user Explorer registration.

For a portable update, quit ClipDiff and extract the entire new ZIP. Different
extensions have different filenames, so leave a loaded older DLL in place. The
new app registers its matching DLL. Cleanup is best effort on later launches.
Use Setup for the recommended update path.

Release binaries are currently unsigned, so Windows SmartScreen may warn on the
first launch.
