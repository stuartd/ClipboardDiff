# Releasing ClipDiff

The release contract is the ZIP, not a `bin` or raw `dotnet publish` directory.
Every ClipDiff Windows x64 release contains exactly:

```text
ClipDiff.exe
ClipDiff.ShellExtension.dll
```

Both files go in the same directory. The executable is the self-contained .NET
application; the DLL is the native two-file Explorer context-menu handler. Do
not ship either file on its own.

## Build a release locally

On Windows, from a normal non-administrator PowerShell in the repository root:

```powershell
.\scripts\create-local-release.ps1
```

The script requires the .NET 10 SDK and Visual Studio C++ build tools with a
Windows SDK. It builds and tests the native extension, runs the .NET tests,
publishes the self-contained application in a clean staging directory, and
validates the final folder and ZIP. Output is:

```text
releases\ClipDiff-<version>-win-x64\
    ClipDiff.exe
    ClipDiff.ShellExtension.dll
releases\ClipDiff-<version>-win-x64.zip
```

The version defaults to the `Version` property in `Directory.Build.props`. A
specific version can be supplied when reproducing a tagged build:

```powershell
.\scripts\create-local-release.ps1 -Version 1.2.3
```

`-SkipNativeTests` skips only the native Shell integration tests; it still
builds and packages the DLL and runs the .NET tests. Do not use that switch for
a published release.

## Publish on GitHub

1. Merge the release changes into `main` and confirm **Windows build** succeeds.
2. Choose a semantic version such as `1.2.3`.
3. Create and push an annotated tag for that exact `main` commit:

   ```powershell
   git tag -a v1.2.3 -m "ClipDiff v1.2.3"
   git push origin v1.2.3
   ```

The **Release** workflow validates the tag, runs the complete release script,
and creates a GitHub release with generated notes and
`ClipDiff-1.2.3-win-x64.zip` attached. It will not publish if any build, test, or
payload validation fails.

## Install or update the portable build

Extract the whole ZIP into a directory owned by the current user and run
`ClipDiff.exe`. Do not move the DLL elsewhere or run `regsvr32`; ClipDiff owns
its per-user Explorer registration.

For an update, quit ClipDiff and extract the new version into a new directory.
This avoids overwriting a DLL that Explorer may still have loaded. Update any
startup shortcut to the new executable. The old directory can be removed after
Explorer releases its DLL, which may require restarting Explorer or signing
out.

Release binaries are currently unsigned, so Windows SmartScreen may warn on the
first launch.
