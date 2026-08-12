# ClipDiff for Windows

ClipDiff is a small Windows notification-area utility that compares the last two distinct plain-text values copied after it starts. It listens to the Windows clipboard directly; it does not use Ditto, a database, or a web service.

## Use

1. Start `ClipDiff.exe`. It opens in the notification area without a conventional main window.
2. Copy the older text.
3. Copy the newer text.
4. Press `Ctrl+Alt+D`, or right-click the ClipDiff icon and choose **Show Diff**.

The reusable diff window opens in **Side by Side** mode. It can switch to **Unified**, copy the unified difference as ordinary Unicode text, or clear the captured values. Closing the window hides it; **Quit ClipDiff** in the notification-area menu exits the application.

The menu also lets you pause/resume monitoring and shows short previews of the current and previous entries. Resuming starts from the clipboard's then-current sequence and does not import text copied while paused. If another application owns `Ctrl+Alt+D`, ClipDiff continues to work through its notification-area menu and displays **Shortcut unavailable**.

## Privacy model and limitations

ClipDiff captures only future, non-empty Unicode text and retains at most two accepted values in process memory. Consecutive duplicates and non-text clipboard changes do not alter those two entries. **Clear Captured Text** removes the in-memory entries and active diff without changing the Windows clipboard. All captured content disappears when ClipDiff exits. ClipDiff never intentionally persists, logs, uploads, or transmits captured clipboard text.

Before reading text, ClipDiff inspects and honours these advisory clipboard formats:

- `ExcludeClipboardContentFromMonitorProcessing`
- `CanIncludeInClipboardHistory` when its DWORD is zero
- `CanUploadToCloudClipboard` when its DWORD is zero

Malformed or unreadable privacy markers are excluded conservatively. **Copy diff** writes its Unicode text and all three exclusion formats in one native clipboard operation, and ClipDiff suppresses the resulting clipboard update.

Some password managers clear an unmarked value shortly after copying it. If an accepted value (or a duplicate of the current value) is immediately followed by an explicit clipboard clear within 60 seconds, ClipDiff removes that current entry. An intervening unrelated, sensitive, or failed clipboard observation cancels this eligibility. This is only a best-effort heuristic.

Important limitations:

- Passwords without standard privacy markers are indistinguishable from ordinary text.
- The automatic-clear heuristic cannot identify every sensitive value or clearing pattern.
- .NET strings cannot be guaranteed to be securely zeroed in memory.
- Operating-system paging, process dumps, other clipboard monitors, and Windows clipboard history are outside ClipDiff's control.

ClipDiff does not guess whether text is sensitive from its length, punctuation, entropy, apparent source process, or token-like appearance.

## Requirements

- Windows 10 version 1809 or newer, or Windows 11
- Windows Server 2022 with Desktop Experience on a best-effort basis
- An interactive desktop session with a clipboard and notification area

Windows Server Core is not supported. The initial release target is `win-x64`.

## Build and test

Install the .NET 10 SDK, then run:

```powershell
dotnet restore ClipDiff.Windows.sln
dotnet test ClipDiff.Windows.sln
dotnet build ClipDiff.Windows.sln --configuration Release
```

The pure `ClipDiff.Core` project and both policy test assemblies target ordinary `net10.0`, so they can run on macOS:

```bash
dotnet test tests/ClipDiff.Core.Tests/ClipDiff.Core.Tests.csproj
dotnet test tests/ClipDiff.Windows.Tests/ClipDiff.Windows.Tests.csproj
```

The Windows project has `EnableWindowsTargeting=true`, which permits cross-compilation where Microsoft targeting packs are available. A successful macOS compile is not a functional Windows test. Clipboard formats, notification-area behaviour, global hotkeys, WPF presentation, and native cleanup still require Windows verification.

## Local release

On Windows PowerShell:

```powershell
./scripts/create-local-release.ps1
```

The script runs tests and publishes a self-contained, single-file, untrimmed `win-x64` build to the gitignored `releases/win-x64` directory. Pass `-Launch` to start it after publishing. The personal build is unsigned, so Windows SmartScreen may warn before first launch.

No installer, automatic updater, or code signing is included in the initial release.

## Windows verification checklist

On a Windows desktop, verify:

- only one notification-area icon appears and a second process exits cleanly;
- clipboard text that existed before startup is not captured;
- two future distinct copies produce **Ready to diff** and `Ctrl+Alt+D` opens the window;
- side-by-side rows remain aligned, unified output is exact, and long lines wrap;
- **Copy diff** pastes into Notepad, has all three exclusion formats, and is not recaptured;
- duplicates and images leave history unchanged;
- pausing skips copied text and resuming establishes a new baseline;
- clearing captured text does not modify the Windows clipboard;
- closing the diff window leaves the tray application running;
- occupying `Ctrl+Alt+D` leaves **Show Diff** usable from the menu;
- a privacy-marked item is not read, and a recent unmarked value followed by a clear is removed;
- quitting removes the icon, unregisters native resources, and loses all captured content.

Run the same checks through Remote Desktop if Windows Server 2022 is the intended host.

## Optional start at sign-in

ClipDiff cannot recover values copied before it starts because it does not persist clipboard content. If desired, create a shortcut to `ClipDiff.exe` in the folder opened by `shell:startup`.
