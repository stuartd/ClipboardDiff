# ClipDiff for Windows — implementation specification

## 1. Product summary

ClipDiff is a small, native Windows notification-area utility for comparing the last two text values captured from copied Unicode text or copied files.

The intended workflow is:

1. Copy the older text or file.
1. Copy the newer text or file.
1. Press `Ctrl+Alt+D` (or the user-selected replacement), or select **Show Diff** from the notification-area menu.
1. Read the difference in a native Windows window.
1. Optionally copy a readable unified diff back to the clipboard.

Alternatively, copy exactly two files in one operation to use them immediately as the older/newer comparison pair.

After one value has been captured, the user may instead right-click a second file in Explorer and select **Compare with current ClipDiff capture**. This must behave as though that file had been copied as the next value and **Show Diff** had then been invoked, without modifying the Windows clipboard.

The user may also select exactly two files in Explorer and choose **Compare two selected files with ClipDiff**. This converts the two files directly into the previous/current comparison pair and opens the selected viewer without first copying either file or modifying the Windows clipboard.

This is a personal local utility. Keep the implementation small, inspectable, dependency-light, and privacy-conscious.

The application should not depend on Ditto or any other clipboard manager. Earlier versions of this workflow used the last two values from Ditto’s database; this application replaces that hack by listening to the Windows clipboard directly.

## 2. Repository and product identity

This must be a new repository, separate from the existing macOS project.

Suggested names:

- Repository: `WindowsClipboardDiff` or `ClipDiff.Windows`
- Product name: `ClipDiff`
- Executable: `ClipDiff.exe`
- Default namespace: `ClipDiff`

The implementation must be self-contained. Do not assume access to the macOS repository or copy source files from it.

## 3. Technology choices

Use:

- C#
- .NET 10
- WPF for the diff window
- Windows Forms `NotifyIcon` for the notification-area icon and native context menu
- Win32 APIs through small, explicit P/Invoke wrappers
- PowerShell for local build/release scripts

The Windows application project should target:

```
<TargetFramework>net10.0-windows10.0.17763.0</TargetFramework>
<UseWPF>true</UseWPF>
<UseWindowsForms>true</UseWindowsForms>
<EnableWindowsTargeting>true</EnableWindowsTargeting>
```

`EnableWindowsTargeting` allows the project to be at least restored and compiled from a non-Windows development machine where supported. The application cannot be run or meaningfully smoke-tested on macOS.

The pure core project should target ordinary `net10.0`, so its tests can run on macOS.

Do not add runtime third-party dependencies unless there is a compelling reason. The Windows and .NET libraries are sufficient.

Microsoft-owned test packages are acceptable as development-only dependencies. Prefer MSTest for the unit-test project.

## 4. Supported environment

Primary targets:

- Windows 10, version 1809 or newer
- Windows 11
- Windows Server 2022 with Desktop Experience, on a best-effort basis

Windows Server Core is not a target because the application requires an interactive desktop, notification area, clipboard, hotkey message loop, and WPF window.

Initial release architecture:

- `win-x64`

Arm64 can be added later but is not required for the first version.

## 5. Core product constraints

The application must:

- Capture text values only, including the copied-file-to-text conversion and explicit Explorer-file workflow defined in section 6.4.
- Keep captured text in memory only unless the user explicitly selects an external diff viewer. That approved workflow may use the temporary plaintext handoff defined in section 16.5.
- Retain at most two accepted clipboard values.
- Accept consecutive copies of identical text as separate entries.
- Ignore unsupported non-text clipboard changes; handle copied file lists as defined in section 6.4.
- Ignore privacy-marked clipboard items.
- Avoid capturing its own **Copy diff** output.
- Continue working if global hotkey registration fails.
- Lose all in-memory captured text when the application exits and attempt to delete all external-diff temporary files.
- Use one notification-area icon and one reusable diff window.

The application must not:

- Write captured clipboard values to disk except for the explicitly selected, warned, short-lived external-diff handoff in section 16.5.
- Use a database.
- Read Ditto’s database.
- Upload or synchronize captured content.
- Log clipboard content.
- Add analytics, telemetry, or crash-reporting services.
- Add accounts or cloud services.
- Add a clipboard-history browser.
- Retain more than the two values needed for the comparison.
- Add settings or onboarding beyond the external-viewer executable path, warning acknowledgement, and global keyboard shortcut, or add a complex preferences interface.
- Add document-management features or file access beyond the copied-file conversion and explicit Explorer actions in section 6.4.
- Use a terminal window or embedded web UI for the diff.
- Require an installer for the initial personal release.

## 6. Clipboard history semantics

Maintain an ordered in-memory list with newest first:

```
entries[0] = current clipboard entry
entries[1] = previous clipboard entry
```

Each accepted entry contains:

```
public sealed record ClipboardEntry(
    Guid Id,
    string Text,
    DateTimeOffset CapturedAt,
    string? SourceFileName,
    string? SourceFilePath
);
```

`SourceFileName` is the basename for a value obtained from a copied or directly selected file, and `SourceFilePath` is its normalized full path. Both are null for ordinary clipboard text. The full path may exist only in the same two-entry in-memory history as the captured value, solely to disambiguate equal basenames. Never persist or log it.

### 6.1 Initial startup

When ClipDiff starts:

- Record the current clipboard sequence number.
- Do not capture whatever was already on the clipboard.
- Begin listening for future clipboard changes.
- Show the status **Waiting for copied text**.

This avoids unexpectedly importing a value that predates application startup.

Consequently, the user must copy two values after ClipDiff starts. Running ClipDiff at sign-in can be documented as an optional setup step, but no startup-settings UI is required.

### 6.2 Accepted text

A clipboard change is accepted when all of the following are true:

- Monitoring is enabled.
- The change is not ClipDiff’s own write.
- The clipboard can be inspected successfully.
- No supported privacy/exclusion marker is present.
- Unicode plain text is present, or a copied file list can be converted under section 6.4.
- The text is not the empty string.
- The clipboard sequence is a new observation, even if its text equals the current accepted entry.

Whitespace-only text is valid and must be captured. Only a truly empty string is rejected.

When accepted:

1. Create a new `ClipboardEntry`.
1. Insert it at index zero.
1. Remove entries beyond index one.
1. Clear any nonfatal UI error.
1. Update tray status and preview information.

### 6.3 Consecutive identical copies

If copied text is exactly equal to the current entry:

- Add a new entry for the new copy occurrence.
- Move the formerly current entry to the previous position.
- Evict entries beyond the two-item limit.
- Allow the two identical entries to produce a comparison whose summary is No differences.

Comparison is ordinal and case-sensitive.

### 6.4 Non-text changes

If the clipboard contains an image, rich content without a plain-text representation, an unusable file list, or another unsupported non-text value:

- Ignore the change.
- Do not clear existing history.
- Do not convert it to text.
- Do not treat an ordinary non-text change as an explicit clipboard clear.

If a clipboard object contains both rich content and Unicode text, capturing its Unicode text is acceptable unless privacy markers prohibit it.

If CF_HDROP contains one copied file, inspect privacy markers before obtaining the path, release the clipboard before file I/O, and convert the file to one text value. Known binary executable/package extensions, binary-looking content, directories, empty files, missing or unreadable files, and files larger than 16 MiB contribute the filename with a parenthetical reason appended: (binary file), (directory), (empty file), (file not found), (file unreadable), or (file too large), as appropriate. Otherwise decode and capture the complete text contents. Support UTF-8, BOM-marked UTF-16 and UTF-32, common BOM-less UTF-16, and Windows-1252. Text scripts such as BAT, CMD, and PowerShell files therefore contribute their contents, while EXE, COM, DLL, MSI, and similar binary types contribute their filename followed by (binary file).

Every file-backed value must retain its source filename separately from its converted text. Show that filename in the notification-area current/previous item and in every diff presentation, even when the file was successfully decoded and its contents form the comparison value. If both compared files have the same basename but different paths, add parent directories from right to left until each label has the shortest path suffix that distinguishes it, for example `branch-a/src/settings.json` and `branch-b/src/settings.json`. Do not add path context when the basenames already differ or both entries refer to the same path.

If exactly two files are copied together, convert each independently using the single-file rules above and atomically replace the comparison pair. The first path in CF_HDROP order is the previous value and the second is the current value. Ignore copies containing more than two files; never create a comparison value from a file list. Prefer CF_HDROP handling over incidental Unicode text exposed for the same copied-file item. File contents and paths must not be persisted or logged, and a newer clipboard sequence must supersede an in-progress file read.

Treat Explorer **Copy as path** text as the equivalent file copy when the Unicode text consists solely of quoted, absolute Windows file-system paths, one per line. One or two paths use the same conversion and ordering rules as CF_HDROP; more than two are ignored. This deliberately prioritizes file comparison over the unusual case of comparing quoted path strings. Unquoted paths, relative paths, and paths embedded in other text remain ordinary clipboard text. Privacy markers must still be inspected before reading the text, and the clipboard must be released before file I/O.

While monitoring is active and at least one captured entry exists, register a per-user Explorer context-menu command named **Compare with current ClipDiff capture** for individual files. When the current capture is file-backed, append ` — <filename>` to the command label so Explorer identifies the comparison source. Invoking it must convert the directly selected file with the same single-file rules, insert its result as the new current entry, move the former current entry to previous, evict any older entry, and immediately invoke the ordinary **Show Diff** workflow. It must not modify the Windows clipboard or its sequence baseline, and the direct entry must not be eligible for the recent clipboard-clear heuristic. If monitoring is paused or there is no current entry, do not read the selected file or alter history.

While monitoring is active, also register a per-user Explorer context-menu command named **Compare two selected files with ClipDiff**. It must accept exactly two directly selected files without requiring an existing capture, convert both independently using the same single-file rules, atomically replace the history with the resulting pair, and immediately invoke the ordinary **Show Diff** workflow. The first path in Explorer's supplied selection order is previous and the second is current. Neither direct entry is eligible for the recent clipboard-clear heuristic. If monitoring is paused, or the invocation does not contain exactly two usable file-system paths, do not read a selected file or alter history.

The individual-file command may pass its selected path on its process command line and through same-user local IPC to the existing ClipDiff instance. The two-file command must instead receive the complete selection through an out-of-process Shell drop-target/COM data object so neither selected path appears on a command line. Do not pass copied file paths or contents, decoded text, or a diff on a command line. Do not persist or log any selected file path. The individual-file command does not support multiple selections. Because the classic Shell `Player` selection model has no exact-two visibility rule, the two-file command may be visible for another selection count, but such an invocation must be rejected without file I/O.

### 6.5 Empty clipboard changes

If the clipboard is explicitly empty, or contains an empty Unicode text value:

- Do not add an entry.
- Apply the recent-clear privacy heuristic if appropriate.
- Otherwise leave existing history unchanged.

### 6.6 Monitoring pause

The notification-area menu must contain a checked **Monitor Clipboard** item.

When monitoring is disabled:

- Leave existing entries untouched.
- Ignore incoming clipboard changes.
- Show status **Monitoring paused**.

When monitoring is re-enabled:

- Record the clipboard’s current sequence number.
- Do not capture values copied while monitoring was paused.
- Resume from the next clipboard change.

### 6.7 Clear Captured Text

The **Clear Captured Text** command must:

- Remove both in-memory entries.
- Remove the active diff document.
- Return the status to **Waiting for copied text**.
- Not clear or alter the Windows clipboard.
- Not close the application.

Disable the command when there are no entries.

## 7. Sensitive clipboard content

Windows does not provide a universal “this text is a password” flag. ClipDiff must therefore implement best-effort privacy handling.

The application that places a value on the clipboard can attach advisory Windows clipboard formats. ClipDiff must inspect these before reading the text.

### 7.1 Privacy formats to honour

Register and inspect these named clipboard formats:

```
ExcludeClipboardContentFromMonitorProcessing
CanIncludeInClipboardHistory
CanUploadToCloudClipboard
```

Rules:

- If `ExcludeClipboardContentFromMonitorProcessing` is present, ignore the entire clipboard item.
- If `CanIncludeInClipboardHistory` contains a serialized DWORD value of zero, ignore the item.
- If `CanUploadToCloudClipboard` contains a serialized DWORD value of zero, conservatively ignore the item as well.

The final rule is intentionally stricter than strictly necessary. If an application says its clipboard content must not be uploaded, treat that as evidence that it may be sensitive.

The equivalent modern Windows API property, `ClipboardContentOptions.IsAllowedInHistory = false`, should normally materialize through the relevant clipboard-history marker. The implementation should operate on the underlying clipboard formats so it also works with Win32 producers.

### 7.2 Inspect before reading

The order is important:

```
WM_CLIPBOARDUPDATE
  → open and inspect clipboard formats
  → check privacy markers
  → if excluded, stop without reading text or file paths
  → otherwise read CF_UNICODETEXT, or obtain CF_HDROP paths and release the clipboard before file I/O
  → apply history policy
```

Do not read text or file paths first and then decide whether to retain them.

If ClipDiff cannot inspect the privacy markers because the clipboard is temporarily locked:

- Retry the entire inspection.
- Do not fall back to a higher-level text-only read that bypasses marker inspection.
- If all retries fail, ignore that update rather than risk reading an item whose privacy status was not checked.

This is a fail-closed policy.

### 7.3 Clipboard retry policy

Windows clipboard access can temporarily fail because another process still owns or is writing it.

Use short asynchronous retries such as:

```
10 ms
25 ms
50 ms
100 ms
200 ms
```

Requirements:

- Do not block the WPF UI thread using `Thread.Sleep`.
- Serialize clipboard reads so several update messages cannot race.
- Cancel or supersede stale retries if a newer clipboard sequence appears.
- Verify that the clipboard sequence remains stable while inspecting and reading.
- Never include clipboard content in an exception message or log.

### 7.4 Recent clipboard-clear heuristic

Some password managers copy an unmarked password and then clear the clipboard automatically.

Implement this best-effort heuristic:

- If an accepted text item is immediately followed by an explicit clipboard clear within 60 seconds, remove the current captured entry.
- “Immediately followed” means there was no intervening unrelated clipboard item.
- An explicit clear means:
    - the clipboard contains no formats; or
    - Unicode text is present but is the empty string.
- Copying an image or another non-text item is not considered a clear.
- A privacy-marked item followed by a clear must not remove an older ordinary entry, because the marked item was never captured.
- ClipDiff’s own clipboard write resets clear eligibility.

The 60-second interval is a recommended concrete default. Keep it as a named constant so it can be adjusted easily in source without introducing settings UI.

If removing the latest entry leaves only one entry, do not attempt to resurrect an older evicted value. Retaining a hidden third item would undermine the two-item privacy model.

### 7.5 Limitations to document

The README must say clearly:

- Passwords without standard privacy markers are indistinguishable from ordinary text.
- The automatic-clear heuristic is only best effort.
- Strings managed by .NET cannot be guaranteed to be securely zeroed in memory.
- Operating-system paging, process dumps, other clipboard monitors, and Windows’ own clipboard history are outside ClipDiff’s control.
- ClipDiff never intentionally persists or transmits captured text.

Do not attempt unreliable password detection based on:

- text length;
- punctuation;
- entropy;
- the apparent source process;
- whether the value resembles a token;
- hard-coded password-manager process names.

A personal source denylist can be considered later if genuinely needed, but it is not part of the first version.

## 8. ClipDiff’s own clipboard writes

The **Copy diff** command writes the currently displayed unified diff to the Windows clipboard.

Requirements:

- The output must remain pasteable as ordinary Unicode text.
- It must not become a new ClipDiff history entry.
- Attach all three privacy/exclusion formats:
    - `ExcludeClipboardContentFromMonitorProcessing`
    - `CanIncludeInClipboardHistory` with DWORD zero
    - `CanUploadToCloudClipboard` with DWORD zero
- Record the resulting clipboard sequence number and suppress the corresponding update event.

Write all formats as one clipboard operation.

If a WPF `DataObject` does not produce the exact raw registered-format data required by Windows, implement the write with a small native clipboard helper. Validate the resulting formats with `EnumClipboardFormats`/`GetClipboardData` during testing.

## 9. Windows event integration

### 9.1 Clipboard monitoring

Use the event-driven Windows clipboard listener:

- Create or obtain an HWND owned by the application.
- Call `AddClipboardFormatListener`.
- Handle `WM_CLIPBOARDUPDATE`.
- Call `RemoveClipboardFormatListener` during disposal.

A hidden `HwndSource`, hidden native message window, or the WPF application window handle can be used. Prefer a dedicated small message source because the diff window may not yet exist and may be hidden.

Use `GetClipboardSequenceNumber` to:

- establish the startup baseline;
- deduplicate update processing;
- suppress ClipDiff’s own write;
- detect races while inspecting.

Do not poll the clipboard on a timer unless a specific Windows defect forces a fallback.

### 9.2 Global hotkey

Register this default:

```
Ctrl+Alt+D
```

Use Win32:

- `RegisterHotKey`
- `UnregisterHotKey`
- `WM_HOTKEY`

Default modifiers:

```
MOD_CONTROL | MOD_ALT | MOD_NOREPEAT
```

Default key:

```
D
```

For a configured replacement, map the stored Ctrl, Alt, and optional Shift flags to the corresponding `RegisterHotKey` modifiers, add `MOD_NOREPEAT`, and use the stored virtual-key code.

If registration succeeds:

- The shortcut invokes **Show Diff** globally.
- Holding the keys must not repeatedly reopen or recompute the window.

The notification-area menu must include **Keyboard shortcut...**, which opens a small recorder window. The window shows the configured shortcut, focuses a capture box, and lets the user press a replacement combination before choosing **Save**. A shortcut must contain Ctrl or Alt, may also contain Shift, and must include one non-modifier key. Windows-key combinations and `Alt+F4` are not supported. Provide **Reset to Ctrl+Alt+D** and **Cancel** commands.

Keep the existing registered shortcut active while testing a replacement. Only switch and persist the replacement after `RegisterHotKey` succeeds. If Windows rejects it because it is reserved or already owned, leave the recorder open with concise feedback and keep the previous shortcut active. If settings persistence fails after registration, attempt to restore the previous shortcut and report the save failure. The current shortcut may change during the process lifetime without restarting ClipDiff.

Persist only the shortcut's modifier flags and virtual-key code in the existing settings file. Missing or invalid persisted shortcut data falls back to `Ctrl+Alt+D`. If the configured shortcut cannot be registered at startup, preserve the configured value and expose **Shortcut unavailable** as described below rather than silently choosing another combination.

If registration fails because another application owns the combination:

- Continue running.
- The notification-area **Show Diff** command must still work.
- Expose a restrained status such as **Shortcut unavailable** in the tray menu.
- Do not terminate.
- Do not show a modal startup error.

Unregister the hotkey during disposal.

### 9.3 Single instance

Use a named per-session mutex, for example:

```
Local\ClipDiff
```

If a second instance starts:

- Exit the second instance cleanly.
- Do not create another notification-area icon.
- If it was launched by the Explorer comparison command, forward the selected path to the first instance over same-user, per-session local IPC before exiting.
- Bringing the first instance forward is optional and not required for the initial version.

### 9.4 Explorer context menu

Use ordinary per-user file-shell verbs below `HKCU\Software\Classes`; do not require administrator access, an installer, or an in-process Explorer extension. Register the individual-file verb only while monitoring is active and a current entry exists. Register the two-file verb while monitoring is active, using an out-of-process local COM drop target so Explorer supplies the selection as one data object. Remove the individual verb when captured text is cleared, and remove both verbs when monitoring is paused or ClipDiff exits. Remove owned stale verb and COM-server registrations at the next start. The registry values may contain only the executable command, label, icon, selection policy, COM class identifier, and local-server command—never captured text, previews, selected paths, or diffs.

On Windows 11 the classic verbs may appear below **Show more options**. Forward individual-file invocations to the existing per-session instance through a local pipe restricted to the current user; deliver two-file invocations directly through the registered COM class factory. A failure to register a verb or deliver a command is nonfatal and must not impair clipboard monitoring or tray operation.

## 10. Status and presentation models

Define pure core models along these lines:

```
public enum DiffKind
{
    Equal,
    Inserted,
    Removed,
    Changed
}

public sealed record DiffRow(
    Guid Id,
    int? OldLineNumber,
    int? NewLineNumber,
    string? OldText,
    string? NewText,
    DiffKind Kind
);

public sealed record DiffSummary(
    int Inserted,
    int Removed,
    int Changed,
    int Unchanged
);

public sealed record DiffSideLabels(
    string Previous,
    string Current
);

public sealed record DiffDocument(
    Guid Id,
    ClipboardEntry Previous,
    ClipboardEntry Current,
    IReadOnlyList<DiffRow> Rows,
    DiffSummary Summary,
    DateTimeOffset CreatedAt,
    DiffSideLabels Labels
);

public enum DiffViewMode
{
    SideBySide,
    Unified
}
```

Resolve `DiffSideLabels` while creating the document, then omit `SourceFilePath` from the document's entry snapshots. The active history is the only component that needs the original paths.

Status strings:

```
0 entries: Waiting for copied text
1 entry:  Copy one more text value
2 entries: Ready to diff
paused:    Monitoring paused
```

Summary labels must use this order:

1. changed
1. added
1. removed

Examples:

```
No differences
1 changed line
2 added lines
1 changed line, 15 added lines, 2 removed lines
```

Do not include unchanged lines in the human summary.

## 11. Clipboard-entry preview

The notification-area menu should show lightweight previews of the current and previous entries.

Preview generation:

1. Convert CRLF, LF, CR, and tabs to spaces.
1. Trim leading and trailing whitespace.
1. If nothing remains, display `Blank text`.
1. Otherwise limit the preview to 120 user-visible characters.
1. Append `...` when truncated.

Display:

```
Current: <preview or None>
Previous: <preview or None>
```

For file-backed entries, prefix the preview with the filename:

```
Current: <filename> — <preview>
Previous: <filename> — <preview>
```

When both filenames match but their paths differ, replace each filename with the shortest unique path suffix defined in section 6.4.

If practical, also show:

```
<n> lines, <n> characters
```

Do not let rich preview UI turn the tray menu into a custom settings window. Disabled native menu items are sufficient.

## 12. Text splitting and line semantics

Normalize line endings before diffing:

```
CRLF → LF
CR   → LF
```

Then split on LF while preserving empty components, including a final empty line.

Examples:

```
"a"       → ["a"]
"a\nb"    → ["a", "b"]
"a\n"     → ["a", ""]
"\n"      → ["", ""]
"a\r\nb"  → ["a", "b"]
"a\rb"    → ["a", "b"]
```

An empty clipboard string is never captured, so an accepted entry will always produce at least one line.

Preserving the final empty component is important because:

```
"a"
```

and:

```
"a\n"
```

must be distinguishable.

Tabs remain tabs in stored text and copied diff output. The UI may render each tab as four spaces for predictable alignment.

## 13. Diff algorithm

Implement a deterministic line-based sequence diff in the pure core project.

Preferred algorithm:

- Myers diff, or another efficient shortest-edit-script implementation
- No runtime diff library dependency

Avoid a naive unbounded `O(n × m)` full matrix if it would behave badly for large clipboard values.

### 13.1 Row generation

Start with the edit script between old lines and new lines.

For equal lines, generate:

```
OldLineNumber = old index + 1
NewLineNumber = new index + 1
OldText       = old line
NewText       = new line
Kind          = Equal
```

For each contiguous edit block before the next equal line:

1. Collect removed old lines.
1. Collect inserted new lines.
1. Pair removed and inserted lines by position.
1. Each pair becomes one `Changed` row.
1. Unpaired removed lines become `Removed` rows.
1. Unpaired inserted lines become `Inserted` rows.

Examples:

One removed and one inserted:

```
old: bravo
new: bravo changed
```

becomes one `Changed` row rather than a separate removed row and inserted row.

Two removed and one inserted:

```
Changed
Removed
```

One removed and two inserted:

```
Changed
Inserted
```

Line numbers are always one-based and refer to the original side’s line positions.

### 13.2 Summary counting

Count generated rows:

- `Equal` increments unchanged.
- `Inserted` increments inserted.
- `Removed` increments removed.
- `Changed` increments changed once.

A `Changed` row does not also increment inserted and removed.

### 13.3 No intraline diff

The first version does not need character-level highlighting within a changed line.

Changed rows are highlighted at the line level only.

## 14. Copyable unified diff

Generate a compact readable diff, not necessarily a complete patch format.

For ordinary clipboard text, the first two lines are exactly:

```
--- Previous clipboard
+++ Current clipboard
```

For a file-backed side, append ` — <file label>` to that side's header. The file label is normally the filename, or the shortest unique path suffix when both filenames collide. This makes the source identity part of the built-in unified view and the copied diff output.

For each row:

```
Equal:
"  " + old text

Removed:
"- " + old text

Inserted:
"+ " + new text

Changed:
"- " + old text
"+ " + new text
```

Example:

```
--- Previous clipboard
+++ Current clipboard
  same
- old
+ new
```

Rules:

- Join output lines using `\n` for deterministic output.
- Do not append an extra final newline.
- Preserve tabs and other line content.
- Do not add timestamps.
- Do not add hunk headers.
- Do not escape text.
- A changed row produces two unified lines.
- Blank lines still receive their appropriate prefix.

## 15. Notification-area interface

ClipDiff should normally have no visible main window.

Use a notification-area icon with tooltip:

```
ClipDiff
```

Suggested menu:

```
Ready to diff                         [disabled status]
Current: <preview>                   [disabled]
Previous: <preview>                  [disabled]
──────────────────────────────────
Show Diff (Ctrl+Alt+D)
Diff viewer: Built-in               [submenu]
Keyboard shortcut...
Monitor Clipboard                    [checked/unchecked]
Clear Captured Text
──────────────────────────────────
About ClipDiff
Quit ClipDiff
```

Behaviour:

- **Show Diff** is disabled until two entries are available.
- **Diff viewer** selects the built-in viewer, a detected external program, or an executable chosen by the user.
- **Keyboard shortcut...** opens the shortcut recorder without changing the active shortcut unless Save succeeds.
- **Monitor Clipboard** toggles monitoring.
- **Clear Captured Text** is disabled when history is empty.
- **About ClipDiff** opens a reusable native window showing the application icon, the product version and short source commit hash, Stuart Dunkeld, `stuartd.dev`, and the source repository link.
- **Quit ClipDiff** removes the tray icon, unregisters native listeners/hotkeys, clears references to captured text, and shuts down.
- Double-clicking the tray icon may invoke **Show Diff** when available.
- The ordinary native right-click context-menu behaviour is sufficient.
- An exact macOS-style popover is not required.

Show the active configured shortcut in the **Show Diff** label, using readable notation such as `Ctrl+Alt+6`. If hotkey registration failed, replace the Show Diff label with:

```
Show Diff (shortcut unavailable)
```

Avoid balloon notifications except where they materially help. No routine clipboard notifications are needed.

## 16. Diff window

Use one reusable WPF window titled:

```
ClipDiff
```

Recommended initial dimensions:

```
Width:     1120
Height:     720
MinWidth:   760
MinHeight:  480
```

Behaviour:

- Create lazily on the first successful **Show Diff**.
- Reuse the same window afterwards.
- If minimized, restore it.
- Bring it to the foreground.
- Closing the window hides it; it must not quit the tray application.
- Application shutdown is explicit through **Quit ClipDiff**.
- Use `ShutdownMode.OnExplicitShutdown`.
- Do not automatically update an already displayed diff merely because another clipboard value is copied.
- Invoking **Show Diff** again must recompute from the latest two entries and update/raise the existing window.

If the global shortcut is pressed before two entries exist:

- Play a restrained system beep or briefly update tray status.
- Do not show a modal dialog.
- Do not open an empty window.

### 16.1 Header

The top header contains:

- Title: `Clipboard Diff`
- Summary label
- View selector:
    - `Side by Side`
    - `Unified`
- **Copy diff** button
- **Clear captured text** button

The view defaults to **Side by Side** each application launch. It may remain at the user’s selected mode during the current process lifetime. It does not need to be persisted.

### 16.2 Side-by-side mode

Layout:

```
┌──────── Previous ────────┬──────── Current ─────────┐
│ line no. │ old text      │ line no. │ new text      │
│          │               │          │               │
└──────────────────────────┴───────────────────────────┘
```

When a side is file-backed, its heading must include the file label, for example `Previous clipboard — old.cs`. Matching filenames from different paths use the shortest unique suffixes defined in section 6.4.

Requirements:

- Old content on the left.
- New content on the right.
- One shared vertical row layout so both sides remain aligned.
- One vertical scroll position for the complete comparison.
- One-based line numbers.
- Monospaced text.
- Long lines wrap.
- Row height expands to fit the taller side.
- Missing text on one side leaves an empty cell.
- Thin row separators.
- Text should be selectable where reasonably possible.
- **Copy diff** remains the reliable way to copy the entire comparison.

Backgrounds:

- `Removed`: pale red on old side, transparent on new side.
- `Inserted`: transparent on old side, pale green on new side.
- `Changed`: pale red on old side, pale green on new side.
- `Equal`: transparent on both sides.

Use system/theme resources where practical. Ensure foreground text remains readable in both light and dark Windows themes.

### 16.3 Unified mode

Display the exact unified lines described earlier.

Backgrounds:

- headers: control/background tint;
- removed lines: pale red;
- inserted lines: pale green;
- unchanged lines: normal background.

Changed rows appear as one red removed line followed by one green inserted line.

Use monospaced text and allow selection.

### 16.4 Empty state

Ordinarily, the window is not shown until a diff is available. If its data is cleared while it remains open, show:

```
No Diff

Copy two text values, then use Show Diff.
```

### 16.5 External diff viewers

The built-in viewer is the default. The notification-area **Diff viewer** submenu must offer the built-in viewer, supported external programs detected through Windows App Paths, PATH, or normal installation locations, and **Choose program...** for manual executable selection. Detection must not select an external program automatically. Remember the selected executable path between runs.

When two entries are ready, **Show Diff** must launch the selected external program with the previous value on the left and the current value on the right. If there is no selection, the executable is unavailable, the privacy warning is cancelled, temporary-file creation fails, or process launch fails, open the built-in viewer instead. Do not discard either captured entry.

Known command profiles must be provided for SourceGear DiffMerge, WinMerge, Meld, KDiff3, Beyond Compare, Araxis Merge, Visual Studio Code, Visual Studio, TortoiseGitMerge, TortoiseMerge, P4Merge, and ExamDiff Pro. Use each program's supported read-only, separate-instance, wait, and side-title options where available. A manually chosen unknown executable receives the previous and current file paths as two positional arguments. Construct arguments as separate process arguments rather than shell-concatenated text so spaces and special characters in paths remain safe. Clipboard contents must never appear in the command line.

Before the first external comparison for a user profile, show one modal privacy warning. It must state that the clipboard may contain passwords, tokens, or other secrets; external comparison requires temporary plaintext files; a crash or power loss can leave them behind; and the selected program may retain its own copies. The user may cancel. Cancelling must create no files and must open the built-in viewer. Remember only successful acknowledgement, so the warning is normally shown once.

After acknowledgement, create a unique per-comparison directory below `%LOCALAPPDATA%\ClipDiff\Temp`. Write ordinary clipboard values exactly as UTF-8 text to `Previous clipboard.txt` and `Current clipboard.txt`. For a file-backed value, use its source filename within a `Previous` or `Current` child directory so external viewers that expose only basenames still show the filename. For viewers that support side titles, use the same collision-disambiguated file labels as the built-in viewer. Do not reproduce original parent directories in the temporary workspace. Mark both files read-only and temporary. The files are the only permitted disk persistence of captured clipboard values. Do not reuse a directory between comparisons.

Track the process returned by the launch. Attempt to delete the comparison directory after that process exits, allowing a short grace period for handoff; when ClipDiff exits; and on ClipDiff's next startup to remove stale directories. Cleanup is best effort because crashes, power loss, open file handles, and programs that delegate to another process cannot be controlled. Normalize read-only attributes before deletion. Never log a temporary path together with clipboard text.

Persist application preferences in `%LOCALAPPDATA%\ClipDiff\settings.json`. The file may contain only the selected executable path, the one-time-warning acknowledgement, and the global-shortcut modifier/virtual-key values. It must never contain clipboard text, previews, diffs, temporary-file contents, or selected-file paths. Returning to the built-in viewer clears the executable selection but need not reset the acknowledgement.

## 17. Suggested solution structure

```
ClipDiff.Windows/
├── ClipDiff.Windows.sln
├── README.md
├── SPEC.md
├── LICENSE
├── .gitignore
├── Directory.Build.props
├── src/
│   ├── ClipDiff.Core/
│   │   ├── ClipDiff.Core.csproj
│   │   ├── ClipboardEntry.cs
│   │   ├── ClipboardHistory.cs
│   │   ├── ClipboardModels.cs
│   │   ├── DiffEngine.cs
│   │   ├── DiffModels.cs
│   │   └── TextLines.cs
│   └── ClipDiff.Windows/
│       ├── ClipDiff.Windows.csproj
│       ├── App.xaml
│       ├── App.xaml.cs
│       ├── AppController.cs
│       ├── Assets/
│       │   └── ClipDiff.ico
│       ├── Clipboard/
│       │   ├── ClipboardMonitor.cs
│       │   ├── ClipboardObservation.cs
│       │   ├── ClipboardPrivacyInspector.cs
│       │   ├── ClipboardWriter.cs
│       │   └── NativeClipboard.cs
│       ├── Hotkeys/
│       │   └── GlobalHotKey.cs
│       ├── Native/
│       │   ├── NativeMethods.cs
│       │   └── NativeMessageWindow.cs
│       ├── Tray/
│       │   └── TrayIconController.cs
│       ├── ViewModels/
│       │   └── DiffWindowViewModel.cs
│       └── Views/
│           ├── DiffWindow.xaml
│           └── DiffWindow.xaml.cs
├── tests/
│   └── ClipDiff.Core.Tests/
│       ├── ClipDiff.Core.Tests.csproj
│       ├── ClipboardHistoryTests.cs
│       ├── DiffEngineTests.cs
│       └── TextLinesTests.cs
└── scripts/
    └── create-local-release.ps1
```

Exact filenames can vary, but preserve the separation:

```
Windows clipboard/Win32 I/O
        ↓
application controller
        ↓
pure history and diff core
        ↓
WPF/tray presentation
```

Do not put clipboard policy or diff logic directly in XAML code-behind.

## 18. Core/application separation

```
ClipDiff.Core
```

Must contain no WPF, WinForms, Win32, or clipboard access.

Responsibilities:

- Clipboard entries
- Two-entry history policy
- Pause/status semantics
- Line splitting
- Diff calculation
- Summary labels
- Copyable unified diff
- Preview formatting where useful
- Recent-clear state transitions if designed as pure observations

This project must be unit-testable on macOS.

```
ClipDiff.Windows
```

Responsibilities:

- Notification-area icon
- WPF window
- Native message loop
- Clipboard listener
- Privacy-marker inspection
- Clipboard read retries
- Clipboard writes
- Own-write suppression
- Global hotkey
- Single-instance mutex
- External-diff discovery, command profiles, warned temporary-file handoff, minimal preference storage, and best-effort cleanup
- Explorer context-menu registration, same-user command forwarding, and selected-file conversion
- UI-thread coordination

All clipboard and UI work should be coordinated through the WPF dispatcher/STA thread.

### Controller

Use a small application controller rather than a large framework.

Responsibilities:

- Own `ClipboardHistory`.
- Subscribe to clipboard observations.
- Expose current/previous entries.
- Compute active diff when requested.
- Manage current view mode.
- Coordinate tray menu state.
- Select and launch the configured diff viewer, falling back to the built-in viewer on failure.
- Coordinate diff-window lifetime.
- Record nonfatal status such as failed hotkey registration.
- Ensure cleanup occurs exactly once.

A third-party MVVM toolkit is unnecessary. Implement `INotifyPropertyChanged` directly if needed.

## 19. Native-resource lifecycle

Every native registration must have matching cleanup:

```
AddClipboardFormatListener
  → RemoveClipboardFormatListener

RegisterHotKey
  → UnregisterHotKey

NotifyIcon.Visible = true
  → Visible = false, Dispose()

HwndSource/native message window
  → Dispose()

Named Mutex
  → Release/Dispose()
```

Cleanup should run on:

- normal **Quit ClipDiff**;
- WPF application exit;
- controller disposal.

Avoid finalizer-only cleanup.

## 20. Error handling

User-facing errors should remain restrained.

Handle:

- Clipboard temporarily locked: asynchronous retry.
- Clipboard read exhausted retries: ignore update.
- Hotkey unavailable: tray status, menu remains functional.
- Copy diff failure: beep or concise status.
- No two entries: disable menu command; hotkey gives a nonmodal indication.
- Native listener registration failure: show a concise tray status and keep the app open if manual retry is possible.

Never:

- Show clipboard text in an exception.
- Log clipboard text.
- Include previews in diagnostic output.
- Crash merely because the clipboard is temporarily unavailable.
- Show repeated modal dialogs from background clipboard changes.

## 21. Testing requirements

### 21.1 Clipboard history tests

At minimum:

1. Captures the last two text copy occurrences.
1. Orders newest as current and older as previous.
1. Evicts values beyond two.
1. Accepts consecutive identical copies as separate entries.
1. Ignores empty text without clearing history.
1. Accepts whitespace-only text.
1. Ignores non-text observations without clearing history.
1. Paused monitoring leaves history untouched.
1. Resuming establishes a new sequence baseline.
1. Clear removes both entries.
1. Status strings are correct.
1. Recent explicit clear removes the latest eligible entry.
1. Clear outside the 60-second window does not remove it.
1. An intervening non-text or sensitive item prevents an unrelated clear from removing older history, according to the finalized state model.
1. A second identical copy followed by immediate clear removes only the latest entry.
1. Own clipboard writes never enter history.

Copied-file tests must cover privacy inspection before paths, CF_HDROP preference over incidental text, Explorer **Copy as path** recognition for quoted absolute paths without reclassifying ordinary path-like text, BAT contents, PE and other known binary executable filenames, binary content with a misleading extension, supported encodings, exact-two-file previous/current pairing and independent conversion, ignoring copies containing more than two files, and reason-labelled filename fallback for directories, empty, missing, unreadable, oversized, and binary files.

They must also cover retaining the source filename and memory-only full path independently from decoded text; choosing the shortest unique suffix for equal basenames on different paths; leaving different basenames and identical paths uncluttered; showing the resolved labels in current/previous previews and built-in diff headers; and passing resolved labels and source basenames to external viewers.

Explorer-command tests must cover exact single-file argument parsing, paths containing spaces and Unicode, command quoting, filename-aware command labels that exclude the full source path, direct insertion as current with the former current moved to previous, exact-two-file validation and ordering, direct-pair atomic replacement, unchanged clipboard sequence state, paused-monitoring rejection, and direct-entry immunity from the recent clipboard-clear heuristic. The two-file COM server command must contain no selected-file placeholder or path.

### 21.2 Line-splitting tests

Include:

- LF
- CRLF
- CR
- blank internal lines
- trailing newline
- multiple trailing newlines
- text containing only a newline
- tabs
- Unicode text

### 21.3 Diff-engine tests

Include:

- all lines equal;
- one changed line;
- one inserted line;
- one removed line;
- changed plus inserted;
- changed plus removed;
- more removals than insertions in one edit block;
- more insertions than removals;
- blank-line changes;
- trailing-newline difference;
- CRLF versus LF normalization;
- repeated identical lines;
- completely unrelated texts;
- empty line on one side;
- tabs preserved in copy output;
- Unicode;
- summary count and label ordering;
- unified diff headers and markers;
- no trailing newline in copied output.

Representative example:

```
Previous:
alpha
bravo
charlie

Current:
alpha
bravo changed
charlie
delta
```

Expected summary:

```
unchanged = 2
changed   = 1
inserted  = 1
removed   = 0

"1 changed line, 1 added line"
```

Expected changed row:

```
old line number = 2
new line number = 2
old text = "bravo"
new text = "bravo changed"
kind = Changed
```

Expected inserted row:

```
old line number = null
new line number = 4
new text = "delta"
kind = Inserted
```

### 21.4 Privacy inspector tests

Make privacy inspection testable behind a fake native clipboard-data abstraction.

Include:

- exclude marker present;
- `CanIncludeInClipboardHistory = 0`;
- `CanIncludeInClipboardHistory = 1`;
- `CanUploadToCloudClipboard = 0`;
- `CanUploadToCloudClipboard = 1`;
- malformed DWORD data;
- no privacy formats;
- privacy marker plus text;
- marker inspection failure;
- text must not be requested when exclusion is already known.

Malformed privacy data should be handled conservatively if exclusion cannot be established safely. Document the selected behaviour.

### 21.5 External diff tests

At minimum:

The catalog contains every supported known program.

Known executable names match case-insensitively, including Windows paths when tests run on macOS.

Each command profile produces the expected switches, labels, and previous/current argument order.

Paths containing spaces remain separate arguments.

An unknown executable receives two positional paths.

Settings round-trip the path and acknowledgement, while missing or malformed settings use built-in-safe defaults and serialized settings contain no clipboard content.

Shortcut tests must cover the default `Ctrl+Alt+D`, readable display text, top-row versus numeric-keypad digits, modifier requirements, rejected modifier-only/Windows-key/`Alt+F4` combinations, invalid persisted values falling back to the default, and settings round-trip without disturbing external-viewer preferences.

Temporary workspaces use unique directories, preserve exact Unicode and line endings, mark files read-only, and remove stale read-only directories.

### 21.6 Windows integration/manual tests

On Windows:

1. Start ClipDiff.
1. Confirm only one notification-area icon appears.
1. Confirm existing pre-start clipboard text is not captured.
1. Copy one text value.
1. Confirm status says **Copy one more text value**.
1. Copy a different value.
1. Confirm status says **Ready to diff**.
1. Press `Ctrl+Alt+D`.
1. Verify the diff window appears.
1. Check side-by-side alignment.
1. Check unified view.
1. Copy the diff.
1. Paste into Notepad and verify contents.
1. Confirm copied diff did not become a ClipDiff history entry.
1. Inspect the clipboard formats and verify exclusion markers.
1. Copy identical text twice and confirm Show Diff reports No differences.
1. Copy an image and confirm history does not change.
1. Pause monitoring, copy text, resume, and confirm paused text was not captured.
1. Clear captured text and confirm the Windows clipboard itself remains unchanged.
1. Close the diff window and confirm ClipDiff remains in the tray.
1. Quit ClipDiff and restart it; confirm no previous text returns.
1. Run a second instance and confirm it exits cleanly.
1. Deliberately occupy `Ctrl+Alt+D` with another program and verify menu operation still works.
1. Open **Keyboard shortcut...**, press `Ctrl+Alt+6`, save it, and confirm the tray label changes and the new combination opens the diff while the old combination no longer does.
1. Attempt to save a combination already owned by another application and confirm the recorder stays open, explains the conflict, and the previous ClipDiff shortcut still works.
1. Reset the shortcut to `Ctrl+Alt+D`, restart ClipDiff, and confirm the saved combination is restored. Confirm missing or invalid shortcut settings fall back safely to the default.
1. Test a privacy-marked sample clipboard item and confirm its text is never requested/captured.
1. Test an unmarked value followed by an immediate clipboard clear and confirm the latest captured item is removed.
1. Test under Remote Desktop if Windows Server 2022 is the intended machine.
1. Confirm the built-in viewer is the default and detected supported programs appear in the Diff viewer submenu without being auto-selected.
1. Choose an external program, invoke Show Diff, and confirm the privacy warning appears before any temporary file is written.
1. Cancel the warning and confirm the built-in viewer opens and no external-diff temporary directory is created.
1. Accept the warning and confirm it is not repeated on later comparisons in the same user profile.
1. Verify each available supported viewer receives the exact previous and current text on the intended sides with readable labels and read-only behavior where supported.
1. Confirm the per-comparison directory and its two read-only files are removed after the launched process exits and when ClipDiff exits.
1. Leave a simulated stale comparison directory and confirm the next ClipDiff start removes it.
1. Remove or rename the selected executable and confirm Show Diff falls back to the built-in viewer.
1. Inspect settings.json and confirm it contains only the selected path, acknowledgement, and shortcut codes, never captured text or previews.
1. Copy one file, then right-click a second file and choose **Compare with current ClipDiff capture**; confirm the second file becomes current, the copied file becomes previous, and the configured viewer opens immediately without changing the clipboard.
1. Confirm the Explorer command is single-file only, uses the same binary/encoding/fallback rules, disappears after pause, clear, and quit, and is under **Show more options** on Windows 11 when not shown in the primary menu.
1. With no captured value required, select exactly two files in Explorer and choose **Compare two selected files with ClipDiff**; confirm the first Explorer-supplied path becomes previous, the second becomes current, and the configured viewer opens immediately without changing the clipboard.
1. Confirm the two-file command uses the same binary/encoding/fallback rules, rejects other selection counts without reading files, remains available after clearing captured text, disappears while monitoring is paused and on quit, and is under **Show more options** on Windows 11 when not shown in the primary menu.
1. Compare two files with the same basename from different directories and confirm the tray, built-in views, copied diff, and supported external-viewer titles use the shortest unique path suffixes.
1. Start ClipDiff after a simulated abnormal exit and confirm it removes an owned stale Explorer registration when there is no captured entry.

## 22. Release build

Provide:

```
scripts/create-local-release.ps1
```

It should:

1. Run core tests.
1. Publish a Release build.
1. Place output in a gitignored `releases/` directory.
1. Optionally launch the resulting executable.

Suggested publish properties:

```
<RuntimeIdentifier>win-x64</RuntimeIdentifier>
<SelfContained>true</SelfContained>
<PublishSingleFile>true</PublishSingleFile>
<PublishTrimmed>false</PublishTrimmed>
<IncludeNativeLibrariesForSelfExtract>true</IncludeNativeLibrariesForSelfExtract>
```

Do not enable trimming for the initial WPF build.

A typical command is:

```
dotnet publish src/ClipDiff.Windows/ClipDiff.Windows.csproj `
  --configuration Release `
  --runtime win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:PublishTrimmed=false
```

The first release can be an unsigned personal executable. Document that Windows SmartScreen may warn about an unsigned binary.

Not required initially:

- MSI
- MSIX
- Microsoft Store submission
- code signing
- automatic updates
- notarization equivalent
- enterprise deployment machinery

## 23. Development from macOS

Because the new repository may initially be created on a Mac:

- Put all pure behavior in `ClipDiff.Core`.
- Run core tests on macOS with:

```
dotnet test
```

- Set `EnableWindowsTargeting=true` for Windows project compilation.
- Do not claim the application works merely because it compiles on macOS.
- Use one of these for Windows verification:
    - the Windows Server 2022 machine with Desktop Experience;
    - a Windows VM;
    - a physical Windows machine;
    - a GitHub Actions Windows runner.

A simple CI workflow should run on `windows-latest`:

```
dotnet restore
dotnet test
dotnet build --configuration Release
```

The native clipboard, hotkey, tray, and WPF behaviour still require manual Windows testing.

## 24. Optional start-at-sign-in documentation

ClipDiff cannot recover clipboard values copied before it started because clipboard content is not persisted.

The README may explain how the user can place a shortcut in:

```
shell:startup
```

Do not add a **Start with Windows** settings screen in the first version.

## 25. README content

The README should explain:

- What ClipDiff does.
- The two-copy workflow.
- How copied files are converted to text or filenames, including limits and privacy behavior.
- How both Explorer comparison actions behave, when each is registered, and their Windows 11 placement.
- `Ctrl+Alt+D`.
- How to record, validate, reset, and persist a replacement global shortcut.
- Notification-area commands.
- The memory-only built-in privacy model and the explicit temporary-plaintext exception for external viewers.
- Supported external viewers, fallback behavior, one-time warning, temporary-file locations, cleanup limits, and minimal stored preference data.
- Password/privacy-marker limitations.
- How automatic clipboard clearing is handled.
- How to build and test.
- How to publish a local release.
- Windows requirements.
- Best-effort Windows Server 2022 status.
- Why there is no Ditto dependency.
- That in-memory captured content disappears on exit and external temporary-file cleanup is best effort.

## 26. Acceptance criteria

The first release is complete when all of these are true:

- The app starts without a conventional main window.
- A notification-area icon appears.
- It captures two future text values from copied Unicode text or the section 6.4 copied-file conversion, including identical consecutive copies and the exact-two-file pair workflow.
- After one captured value, the Explorer command can make a selected second file current and immediately invoke the configured viewer without changing the clipboard.
- Without an existing captured value, the two-file Explorer command can directly replace the comparison pair and immediately invoke the configured viewer without changing the clipboard.
- It stores those values only in memory unless the user explicitly selects and acknowledges the external-viewer temporary-plaintext workflow.
- It accepts duplicate text copies, converts copied files according to section 6.4, and ignores empty text and unsupported non-text changes correctly.
- It honours all three agreed Windows privacy formats.
- It removes a recently captured item when the clipboard is immediately auto-cleared.
- `Ctrl+Alt+D` opens the diff.
- A replacement Ctrl/Alt shortcut can be recorded from the tray, validated before replacing the active shortcut, persisted, and reset to `Ctrl+Alt+D`.
- The menu command still works if hotkey registration fails.
- The built-in viewer is the default; supported external viewers can be selected or browsed for and the selection is remembered.
- External comparison shows the one-time secret-aware warning before writing and falls back to the built-in viewer on cancellation or failure.
- External comparison uses unique read-only temporary files and attempts cleanup after process exit, application exit, and next startup.
- Persisted application settings never contain clipboard content.
- Side-by-side mode is readable and correctly aligned.
- Unified mode is readable.
- File-backed sides show their basenames, using shortest unique path suffixes only when equal basenames need disambiguation.
- Summary counts are correct.
- **Copy diff** produces the agreed output.
- Copied diff output is excluded from history/cloud processing and is not recaptured.
- **Clear Captured Text** resets state without changing the Windows clipboard.
- Closing the diff window leaves the tray app running.
- Quitting releases native resources and loses all captured content.
- Core unit tests pass.
- A self-contained `win-x64` Release build can be produced.
- The application has no dependency on Ditto, a database, a web service, or the macOS repository.

## 27. Suggested implementation order

1. Create solution, core project, Windows project, and test project.
1. Implement text splitting and models.
1. Implement and thoroughly test the diff engine.
1. Implement and test two-entry clipboard history.
1. Implement native clipboard format inspection and privacy policy.
1. Implement clipboard update listener and retry handling.
1. Implement own-write-safe clipboard writer.
1. Implement the application controller.
1. Implement notification-area menu.
1. Implement global hotkey.
1. Implement single-instance handling.
1. Implement side-by-side WPF view.
1. Implement unified WPF view.
1. Add release script and Windows CI.
1. Perform the complete Windows manual smoke test.
1. Test on Windows Server 2022 with Desktop Experience if that is the intended host.

## 28. Guiding principle

When considering additional features, preserve this product shape:

Copy old text or file, copy new text or file, press `Ctrl+Alt+D`, inspect or copy the diff.

The narrowly scoped external-viewer preference and temporary-file handoff in section 16.5 are explicit exceptions. If another proposed feature introduces persistence, accounts, a history browser, dependencies, background infrastructure, or significant configuration, it is probably outside the scope of ClipDiff.
