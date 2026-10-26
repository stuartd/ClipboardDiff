# Long-running tray hang: ownership and blocking-call audit

Audited baseline: `1f47957a9e63a98bd369d8348e23eabca72a548d`, matching
[Actions run 39242061366](https://github.com/stuartd/ClipboardDiff/actions/runs/39242061366).
The reported symptom was an unresponsive notification-area icon/menu after several
days on a work laptop. The process had not been restarted. No stack or resource
sample from that process was available during this review, so the cause of that
particular incident remains unconfirmed.

## Findings and changes

| Finding | Impact and correction |
| --- | --- |
| Clipboard inspection called `GetClipboardData` on the UI thread. | This API can ask another application to render delayed clipboard data and wait for it. The async capture method did not prevent this: its semaphore await often completed synchronously, and inspection happened before its next await. Inspection now runs on a worker. Opening, inspecting, and closing the clipboard stay on that same worker, and the monitor holds its existing gate until the native call returns. Cancellation rejects the result without abandoning an open clipboard or starting overlapping native reads. Privacy inspection still precedes retrieving text or paths. |
| Explorer registration reported a change on every refresh. | `UpdatePresentation` runs after clipboard observations and when opening the tray menu. The previous code rewrote identical association values and reported successful pair registration as a change, causing repeated `SHCNE_ASSOCCHANGED` calls from the UI thread. Registration now compares both the value and registry type, writes only changes, and distinguishes pair readiness from an actual change. Deleted/changed values still get repaired, and cross-session reconciliation remains active. This is avoidable shell work, rather than evidence of a C++ memory leak. |
| Rebuilding the viewer submenu cleared items without disposing them. | `ToolStripItemCollection.Clear` removes items but does not dispose them. Selecting/changing a viewer repeatedly could leave removed items awaiting GC and unmanaged resource cleanup. Each old item is now disposed before its replacement is created. This is an operation-specific disposal defect; merely leaving the app idle does not exercise this path. |
| About-window links discarded `Process.Start`'s returned `Process`. | The wrapper is now disposed. This closes owned process resources without terminating the browser. This only occurs when clicking About links and is unlikely to explain an idle hang. |

Microsoft documents the rendering callback in
[Clipboard Operations](https://learn.microsoft.com/windows/win32/dataxchg/clipboard-operations#delayed-rendering).
Moving the read protects the UI even when a producer blocks. It cannot cancel
an in-progress Win32 call or prove that delayed rendering caused this incident.

## Native C++ ownership

The DLL is loaded into **Explorer**, not the ClipDiff process. I found no unmatched
allocation, COM reference, event handle, or menu ownership in its production paths.
That is a source-review result, not a Windows soak-test result.

| Native path | Ownership and release |
| --- | --- |
| `IsClipDiffReady` | Each successful `OpenEventW` is followed by a zero-time wait and `CloseHandle`. No persistent per-query handle. |
| `IsFilePair` | Shell item arrays and individual items are held in `ComPtr`; early failures unwind both. The loop is bounded to two items. The method requests attributes, not display names or file contents. |
| `ContextMenu::Initialize` | Releases the previous selection before accepting another. Retains at most one data object per handler. |
| `ContextMenu::InvokeCommand` | A valid invocation swaps selection into a local `ComPtr`. Paused/unready/invalid selection, activation failure, drag-enter rejection, and drop completion all release it. An unknown verb deliberately leaves the still-usable menu selection intact. |
| Handler destruction | Resets selection and decrements the module reference. All successful `QueryInterface` calls take a matching reference; final `Release` deletes the handler. |
| Class factory | Creation and failed-interface paths release temporary objects. Factory lifetime and balanced `LockServer` calls are reflected in `DllCanUnloadNow`. |
| Menu items | `InsertMenuItemW` inserts a text item into the caller's menu. The Shell owns that `HMENU`; the extension must not destroy it. No extension-owned menu bitmap, brush, window, timer, or worker thread exists. |

Added Windows native regression checks for 10,000 factory/handler lifetimes,
module unload readiness, process-handle counts, and 1,000 selection replacements.
A reference-counted wrapper around the real Shell data object checks release on
reinitialization, handler destruction, failed invocation, and successful invocation.

The native calls that can still wait are Shell metadata queries, COM activation,
and the calls to the out-of-process drop target. A blocked ClipDiff UI can therefore
also block an Explorer invocation. The ready event indicates that the server was
registered; it is not a UI-health heartbeat. Retaining a handler while its client
still owns it, or Explorer keeping a DLL loaded, is not by itself a leak.

## Managed ownership ledger

| Component | Result |
| --- | --- |
| Application instance mutex | Held for process lifetime; released/disposed on exit. A second normal launch exits without repairing or foregrounding an existing hung instance. |
| Hidden message window | Removes its hook and disposes `HwndSource`. Monitor and hotkey callbacks unsubscribe before window disposal. |
| Clipboard listener | Unregisters on disposal. Retry delays are finite and only run after updates; there is no periodic multi-day timer. |
| Clipboard reads | `CloseClipboard` and `GlobalUnlock` are in `finally` paths. Clipboard-owned global memory is correctly borrowed, not freed. The UI blocking defect is fixed as described above. |
| Protected clipboard writes | Failed `GlobalAlloc`/`GlobalLock`/`SetClipboardData` paths free untransferred memory. Successful `SetClipboardData` transfers ownership to Windows; freeing it there would be wrong. Clipboard closure is unconditional. |
| Read cancellation/semaphore | Superseded cancellation sources are cancelled/disposed. At most one native inspection owns the monitor's gate. Shutdown may finish before an already-running native call returns; its worker still closes the clipboard. |
| Copied files | Streams are `await using`, including empty, oversized, decoding failure, and cancellation exits. |
| Shortcut COM objects | A locally created ShellLink wrapper is final-released in `finally`. No `Resolve`, launch, or mutation is used. |
| Explorer COM server | Revokes class registration, releases owned IUnknown pointers, and balances successful COM initialization. Created targets release temporary IUnknown pointers. |
| Incoming COM drop data | Every successfully obtained `STGMEDIUM` reaches `ReleaseStgMedium`, including invalid counts/types and path failures. Incoming RCWs are managed by the interop runtime; force-releasing a potentially shared incoming wrapper would be unsafe. |
| Explorer pipe server/client | Streams, message deadlines, listener shutdown source, and slot semaphore have cleanup paths. Pending receipts are bounded to four; old completed tasks are removed. Handler completion is not awaited while accepting the next message. |
| Shared registration | Registry keys use `using`; owner/gate leases are released, with deletion on close. Lock acquisition has a 500 ms deadline, not an indefinite wait. Stale-owner reconciliation is tested. |
| External viewer processes | Exit events are detached; process wrappers and workspaces are disposed by an interlocked, one-shot cleanup. Failed starts and disposal have cleanup paths. Delayed cleanup retains a completed comparison for three seconds intentionally. |
| External temporary files | Exclusive owner leases protect other sessions. Files are made writable before best-effort deletion; cleanup releases the lease. Windows may prevent deleting a file still held by a viewer. |
| Tray and drawing | Owned icon, notification icon, menu, and per-paint pens are disposed. The shared system-icon fallback is correctly not disposed. Replaced menu-item disposal is fixed. |
| WPF windows and documents | One diff window and one About window are reused intentionally; final shutdown closes them. Undo is disabled in diff text boxes. The active document is replaced/cleared; there is no application collection of past documents. |
| History/comparison tasks | History retains two entries. The last displayed diff and its comparison input can intentionally outlive those history entries. Generation checks reject superseded results; cancellation sources are scoped to each comparison. |
| Settings/startup/tool discovery | Registry and file APIs close their resources. No periodic registry scan or growing cache of viewer discoveries is present. |

## Remaining risks to distinguish with the hung process

- File probes, file opens, and ShellLink loading run off the UI thread, but a
  synchronous operation that ignores cancellation can outlive its request.
  Repeated inaccessible network paths can accumulate blocked workers until the
  operating system returns. This is distinct from a missing stream disposal;
  many thread-pool stacks in those calls would support this explanation.
- The UI still performs local registration/settings work, external-viewer launch
  and temporary-file creation, and explicit clipboard writes. Storage, antivirus,
  Shell callbacks, or unavailable installation paths can delay those operations.
- Very large single-line comparisons can spend substantial time creating and
  measuring WPF rich-text documents. Core search budgets do not bound WPF layout
  cost. This needs a Windows UI workload or stack to diagnose.
- A hidden modal dialog, a stale icon after process exit, and an Explorer hang
  are different cases. Process presence, wait chains, and stacks help separate them.

These are remaining code paths, not additional confirmed causes. There is no
uptime or weekend-specific trigger in the resource ownership reviewed here.

## Validation

- 96 core tests and 13 stress-workload tests passed, with no skips or failures.
- 215 portable application tests passed in a local linked-source harness at
  `artifacts/audit/PortableAudit.Tests.csproj`. This runs the actual portable
  production classes and existing/new tests under `net10.0`, without loading WPF
  or substituting native API stubs. The blocked-renderer test uses a controlled
  blocking delegate; it is not a real Windows clipboard integration test.
- The Release managed solution, including the Windows test assembly, compiled
  with zero warnings/errors.
- The native DLL and native test executable were cross-compiled with MinGW as
  an additional C++ check. This is not the supported MSVC release build, and
  neither native binary was executed on Linux.
- The hang-inspection PowerShell script parses successfully. Its Windows
  process-inspection behavior has not been executed on this machine.

The new Windows registry, tray-menu, and native lifetime tests still need to run
on Windows. Use the existing workflow or, from a normal Windows PowerShell with
ClipDiff closed:

```powershell
./scripts/build-shell-extension.ps1 -Test
dotnet test ClipDiff.Windows.sln --configuration Release
dotnet test tests/ClipDiff.Stress.Tests --configuration Release
```

Then test the tray while a synthetic producer delays rendering, check ordinary
clipboard capture/privacy behavior, repeat viewer selection, and exercise the
actual Explorer menu. No Windows functional-readiness claim is made here.

## Inspect the existing hang before restarting

Run on the affected laptop:

```powershell
./scripts/inspect-hang.ps1
```

This only reports process/thread identifiers, CPU/memory/handle/GUI counts and
wait states. It does not read the clipboard, fetch text or file paths, collect
window titles or command lines, write a dump, or restart anything. Supply
`-ClipDiffProcessId <id>` for a dotnet-hosted development launch. `Responding`
alone is not conclusive for a tray app.

Use Task Manager's Details tab to **Analyze wait chain** for that PID. If the
Microsoft `dotnet-stack` tool is already available, the script prints its
`report --process-id` command; the report contains managed method stacks without
locals or clipboard values. If collection stalls, cancel it. Native symbols may
be needed to identify a specific underlying Win32 wait.

Do not collect a process/heap dump for this review: it could persist captured
clipboard values. Start with the read-only inspection; the existing stress tool
overwrites the clipboard with synthetic data and is for subsequent patched-build
testing, after the original hang evidence has been captured.
