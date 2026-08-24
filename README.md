# ClipDiff for Windows

ClipDiff is a Windows notification-area utility that compares the last two text values captured after it starts. A value can come from copied Unicode text or from a file copied in Explorer. 

ClipDiff listens to the Windows clipboard directly. No data is ever uploaded.

## Use

1. Start `ClipDiff.exe`. It opens in the notification area
2. Copy the older text or file.
3. Copy the newer text or file.
4. Press `Ctrl+Alt+D`, or right-click the ClipDiff icon and choose **Show Diff**.
5. You can if you wish copy two files at the same time - `code.txt` and `code_old.txt`- and diff those

The built-in viewer is the default. Its reusable window opens in 'Side by Side' mode but can switch to 'Unified', copy the unified difference as ordinary Unicode text, or clear the captured values. Closing the window hides it; **Quit ClipDiff** in the notification-area menu exits the application.

The menu's **Diff viewer** submenu lists supported programs found on the machine, provides **Choose program...** for another executable, and lets you return to the built-in viewer. The selection is remembered. The menu also lets you pause/resume monitoring and shows short previews of the current and previous entries. Resuming starts from the clipboard's then-current sequence and does not import text copied while paused. If another application owns `Ctrl+Alt+D`, ClipDiff continues to work through its notification-area menu and displays **Shortcut unavailable**.

## Copied files

When Explorer places files on the clipboard, ClipDiff checks the same privacy markers used for ordinary text before obtaining any file paths. A single copied file is converted as follows:

- `.bat`, `.cmd`, `.ps1`, and other files whose bytes look like text contribute their full decoded contents. UTF-8, BOM-marked UTF-16/UTF-32, common BOM-less UTF-16, and Windows-1252 text are supported.
- Known binary executable/package types such as `.exe`, `.com`, `.dll`, and `.msi` contribute only the filename.
- Other binary-looking, empty, missing, unreadable, directory, or larger-than-16-MiB entries also contribute only the filename.

The decision uses both safe binary-extension handling and content inspection, so a binary renamed to `.txt` still falls back to its filename.

When exactly two files are copied together, ClipDiff converts each one independently and immediately uses them as the comparison pair: the first clipboard path is **Previous** and the second is **Current**.

Copies containing more than two files are ignored; ClipDiff never creates a comparison value from a file list.

File contents and paths are never written by this workflow; the resulting value or pair follows the same two-entry, in-memory history policy as copied text.

## External diff viewers

ClipDiff has command-line profiles for these developer tools:

- SourceGear DiffMerge
- WinMerge
- Meld
- KDiff3
- Beyond Compare
- Araxis Merge
- Visual Studio Code
- Visual Studio
- TortoiseGitMerge
- TortoiseMerge
- P4Merge
- ExamDiff Pro

ClipDiff checks Windows App Paths, `PATH`, and the programs' usual install locations. A program selected with **Choose program...** is matched to a known profile by executable name; an unknown executable receives the previous and current file paths as two positional arguments. If no external viewer is selected, its executable is no longer available, or it cannot be started, **Show Diff** falls back to the built-in viewer.

## Privacy model and limitations

ClipDiff captures only future, non-empty text values (including the copied-file conversion above) and retains at most two accepted values in process memory. Consecutive copies of identical text are accepted as separate entries, allowing ClipDiff to report **No differences**. Unsupported non-text clipboard changes do not alter the entries. **Clear Captured Text** removes the in-memory entries and active diff without changing the Windows clipboard. ClipDiff never logs, uploads, or transmits captured text or file contents.

The built-in viewer keeps captured text in memory only. An external program cannot compare in-memory strings directly, so selecting an external viewer creates an explicit exception: after a one-time warning is accepted, ClipDiff writes the two values as UTF-8 plaintext files named `Previous clipboard.txt` and `Current clipboard.txt` in a unique directory below `%LOCALAPPDATA%\ClipDiff\Temp`. The files are marked read-only. ClipDiff attempts to remove that directory after the launched process exits, when ClipDiff exits, and during its next startup.

This cleanup is best effort. A crash, power loss, open file handle, or external viewer that hands work to another process can leave files behind, and the selected program may cache or retain its own copy outside ClipDiff's control. Do not select an external viewer when this disk exposure is unacceptable. Cancelling the warning opens the built-in viewer without writing the files.

ClipDiff stores only the selected executable path and the one-time-warning acknowledgement in `%LOCALAPPDATA%\ClipDiff\settings.json`; clipboard text and previews are never stored there. Rebuilding or replacing the executable does not reset the acknowledgement. To retest the notice, close ClipDiff and set `PlaintextWarningAcknowledged` to `false` in that file; the selected program can remain unchanged. With the built-in viewer, normal exit loses all captured content. With an external viewer, normal exit also attempts to remove every temporary comparison directory.

Before reading text, ClipDiff inspects and honours these advisory [clipboard formats](https://learn.microsoft.com/en-us/windows/win32/dataxchg/clipboard-formats):

- `ExcludeClipboardContentFromMonitorProcessing`
- `CanIncludeInClipboardHistory` when its DWORD is zero
- `CanUploadToCloudClipboard` when its DWORD is zero

Malformed or unreadable privacy markers are excluded conservatively. **Copy diff** writes its Unicode text and all three exclusion formats in one native clipboard operation, and ClipDiff suppresses the resulting clipboard update.

Some password managers clear an unmarked value shortly after copying it. If an accepted value is immediately followed by an explicit clipboard clear within 60 seconds, ClipDiff removes that current entry. An intervening unrelated, sensitive, or failed clipboard observation cancels this eligibility. **This is only a best-effort heuristic and does not come with a guarantee.**

Important limitations:

- Copying a text-like file intentionally causes ClipDiff to read that file from disk and retain its decoded contents in process memory.
- Passwords without standard privacy markers are indistinguishable from ordinary text.
- The automatic-clear heuristic cannot identify every sensitive value or clearing pattern.
- .NET strings cannot be guaranteed to be securely zeroed in memory.
- Operating-system paging, process dumps, other clipboard monitors, and Windows clipboard history are outside ClipDiff's control.
- Temporary plaintext and any copies retained by a selected external diff program are outside the memory-only guarantee described above.

ClipDiff does not guess whether text is sensitive from its length, punctuation, entropy, apparent source process, or token-like appearance.

## Requirements

- Windows 10 version 1809 or newer, or Windows 11
- Windows Server 2022 with Desktop Experience
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
- two future text copies, including identical text, or one exact-two-file copy produce **Ready to diff** and `Ctrl+Alt+D` opens the window;
- side-by-side rows remain aligned, unified output is exact, and long lines wrap;
- the built-in viewer is selected by default, detected external programs appear under **Diff viewer**, and **Choose program...** accepts another executable;
- selecting an external viewer shows the privacy warning once; cancelling uses the built-in viewer without creating plaintext files;
- each supported installed viewer receives the previous/current sides in the right order and with the documented labels and read-only switches where supported;
- external comparison files appear only below `%LOCALAPPDATA%\ClipDiff\Temp`, contain the exact text, and are removed after the launched process exits, on ClipDiff exit, or on the next start;
- removing or renaming the selected viewer executable causes **Show Diff** to fall back to the built-in viewer;
- `%LOCALAPPDATA%\ClipDiff\settings.json` contains only the executable preference and warning acknowledgement, never clipboard content;
- **Copy diff** pastes into Notepad, has all three exclusion formats, and is not recaptured;
- two identical copies produce a diff reporting **No differences**, while images leave history unchanged;
- a copied `.bat` contributes its full text, a copied `.exe` contributes only its filename, and a renamed binary is still treated as binary;
- exactly two copied files become the previous/current comparison pair in clipboard order, while copies of more than two files are ignored; missing, unreadable, empty, directory, oversized, and binary entries fall back to a filename;
- pausing skips copied text and resuming establishes a new baseline;
- clearing captured text does not modify the Windows clipboard;
- closing the diff window leaves the tray application running;
- occupying `Ctrl+Alt+D` leaves **Show Diff** usable from the menu;
- a privacy-marked item is not read, and a recent unmarked value followed by a clear is removed;
- quitting removes the icon, unregisters native resources, and loses all captured content.

Run the same checks through Remote Desktop if Windows Server 2022 is the intended host.

## Optional start at sign-in

ClipDiff cannot recover values copied before it starts because it does not persist clipboard content. If desired, create a shortcut to `ClipDiff.exe` in the folder opened by `shell:startup`.
