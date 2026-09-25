using System.Diagnostics;
using System.IO;

namespace ClipDiff.Windows.ExternalDiff;

internal sealed class ExternalDiffLauncher : IDisposable
{
    private static readonly TimeSpan ProcessExitCleanupDelay = TimeSpan.FromSeconds(3);
    private readonly ExternalDiffWorkspace workspace;
    private readonly Lock gate = new();
    private readonly Dictionary<Process, ActiveComparison> activeComparisons = [];
    private bool disposed;

    public ExternalDiffLauncher(ExternalDiffWorkspace? workspace = null)
    {
        this.workspace = workspace ?? new ExternalDiffWorkspace();
        this.workspace.CleanupStaleDirectories();
    }

    public bool TryLaunch(ExternalDiffToolChoice choice, ClipboardEntry previous, ClipboardEntry current)
    {
        ArgumentNullException.ThrowIfNull(choice);
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(current);

        if (disposed || !File.Exists(choice.ExecutablePath))
        {
            return false;
        }

        ExternalDiffFiles? files = null;
        Process? process = null;
        ActiveComparison? comparison = null;
        try
        {
            files = workspace.Create(
                previous.Text,
                current.Text,
                previous.SourceFileName,
                current.SourceFileName);
            var startInfo = new ProcessStartInfo
            {
                FileName = choice.ExecutablePath,
                UseShellExecute = false,
                WorkingDirectory = files.DirectoryPath
            };
            var labels = DiffFormatting.Labels(previous, current);
            foreach (var argument in choice.Tool.BuildArguments(
                         files.PreviousPath,
                         files.CurrentPath,
                         labels.Previous,
                         labels.Current))
            {
                startInfo.ArgumentList.Add(argument);
            }

            process = Process.Start(startInfo);
            if (process is null)
            {
                ExternalDiffWorkspace.TryDelete(files.DirectoryPath);
                return false;
            }

            comparison = new ActiveComparison(files.DirectoryPath, process);
            process.EnableRaisingEvents = true;
            process.Exited += OnProcessExited;

            var registered = false;
            lock (gate)
            {
                if (!disposed)
                {
                    activeComparisons.Add(process, comparison);
                    registered = true;
                }
            }

            if (!registered)
            {
                CleanupComparison(comparison);
                return false;
            }

            if (process.HasExited)
            {
                ScheduleCleanup(comparison);
            }

            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            if (comparison is not null)
            {
                CleanupComparison(comparison);
            }
            else
            {
                process?.Dispose();
                if (files is not null)
                {
                    ExternalDiffWorkspace.TryDelete(files.DirectoryPath);
                }
            }

            return false;
        }
    }

    public void Dispose()
    {
        List<ActiveComparison> comparisons;
        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            comparisons = [.. activeComparisons.Values];
            activeComparisons.Clear();
        }

        foreach (var comparison in comparisons)
        {
            CleanupComparison(comparison);
        }
    }

    private void OnProcessExited(object? sender, EventArgs args)
    {
        if (sender is not Process process)
        {
            return;
        }

        ActiveComparison? comparison;
        lock (gate)
        {
            activeComparisons.TryGetValue(process, out comparison);
        }

        if (comparison is not null)
        {
            ScheduleCleanup(comparison);
        }
    }

    private async void ScheduleCleanup(ActiveComparison comparison)
    {
        if (!comparison.TryScheduleCleanup())
        {
            return;
        }

        await Task.Delay(ProcessExitCleanupDelay).ConfigureAwait(false);
        CleanupComparison(comparison);
    }

    private void CleanupComparison(ActiveComparison comparison)
    {
        if (!comparison.TryCompleteCleanup())
        {
            return;
        }

        lock (gate)
        {
            activeComparisons.Remove(comparison.Process);
        }

        comparison.Process.Exited -= OnProcessExited;
        comparison.Process.Dispose();
        ExternalDiffWorkspace.TryDelete(comparison.DirectoryPath);
    }

    private sealed class ActiveComparison(string directoryPath, Process process)
    {
        private int cleanupScheduled;
        private int cleanupCompleted;

        public string DirectoryPath { get; } = directoryPath;

        public Process Process { get; } = process;

        public bool TryScheduleCleanup() => Interlocked.Exchange(ref cleanupScheduled, 1) == 0;

        public bool TryCompleteCleanup() => Interlocked.Exchange(ref cleanupCompleted, 1) == 0;
    }
}
