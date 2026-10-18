# Clipboard stress tool

This standalone C# console tool exercises the running ClipDiff instance on a Windows
desktop. It requires the .NET 10 SDK to build/run. It does not change the application,
its settings, or its release package. No extra runtime packages are used.

It temporarily overwrites the Windows clipboard with generated data. It never reads
or saves the original clipboard. File modes create only two synthetic UTF-8 fixtures
in a unique `ClipDiff-stress-*` temporary directory and attempt to delete that directory
on completion or Ctrl+C. A forcibly terminated tool can leave those synthetic files.
File paths on the clipboard stop referring to existing files after fixture cleanup;
ClipDiff's accepted in-memory values remain available.

Run commands from the repository root with ClipDiff already running. Select the
built-in viewer before testing the UI if you want to avoid external-viewer launches.

Start with ordinary capture activity, leaving the viewer closed:

```powershell
dotnet run --project tools/ClipDiff.Stress -- --mode mixed --copies 1000 --size-kib 256 --delay-ms 200
```

Then increase file-read pressure, keeping each file below the 16 MiB conversion limit:

```powershell
dotnet run --project tools/ClipDiff.Stress -- --mode files --copies 2000 --size-kib 8192 --delay-ms 100
```

To test cancellation/supersession, reduce `--delay-ms` to `10`. This deliberately
allows newer copies to supersede pending reads, so not every write will be captured.
For a slower run that lets individual reads finish, use `--delay-ms 500` or `1000`.

To exercise diff calculation and WPF rendering, invoke the configured shortcut or
tray Show Diff manually during a run or its cooldown. Switch views, scroll, close
and reopen the window. Repeat the same workload with the viewer closed and open
to distinguish capture activity from presentation activity. `--single-line` creates
a long unbroken line instead of many short lines, providing a separate wrapping test.
The tool never sends hotkeys, opens viewers automatically, or changes clipboard
content after copying stops. More than two files and files above 16 MiB mostly
exercise ignore/fallback paths rather than text conversion.

Every five seconds, standard output reports CSV counters for the **ClipDiff target
process**, including private memory, working set, handle count, thread count, GDI/USER
object counts, and cumulative CPU seconds. `Attempts`/`Writes` count clipboard
operations, not confirmed captures; `Skipped` means the clipboard remained locked
through ten short open attempts. An unavailable GUI count is blank. The tool follows
one process identity and stops if it exits; it does not follow a restarted instance.
Use `--pid <id>` to select a particular process, including a development launch.

The default 30-second cooldown performs no more writes and continues sampling.
Compare several runs of the same size for sustained growth in private memory or
handles/GUI objects. A high reading or memory that stays allocated after a run is
not by itself proof of a leak. ClipDiff intentionally retains its latest captures and
the last displayed diff, and .NET can retain committed memory for reuse.

To save counters, build once and redirect only the tool's output:

```powershell
dotnet build tools/ClipDiff.Stress --configuration Release
dotnet tools/ClipDiff.Stress/bin/Release/net10.0/ClipDiff.Stress.dll --mode mixed > stress-counters.csv
```

The CSV contains process counters only. Status messages go to standard error. No
clipboard values, previews, diffs, source paths or dumps are collected. Run `--help`
for size, timing, count and process options. Ctrl+C stops a run.

Portable workload/argument tests (including native clipboard packet layout):

```powershell
dotnet test tests/ClipDiff.Stress.Tests
```

Actual clipboard delivery and GUI counters require a Windows desktop; the portable
tests do not verify those integrations or reproduce the reported crash.
