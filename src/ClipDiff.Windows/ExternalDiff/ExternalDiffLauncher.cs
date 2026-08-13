using System.Diagnostics;
using System.IO;

namespace ClipDiff.Windows.ExternalDiff;

internal sealed class ExternalDiffLauncher : IDisposable
{
    private static readonly TimeSpan ProcessExitCleanupDelay = TimeSpan.FromSeconds(3);
    private readonly ExternalDiffWorkspace _workspace;
    private readonly object _gate = new();
    private readonly Dictionary<Process, ActiveComparison> _activeComparisons = [];
    private bool _disposed;

    public ExternalDiffLauncher(ExternalDiffWorkspace? workspace = null)
    {
        _workspace = workspace ?? new ExternalDiffWorkspace();
        _workspace.CleanupStaleDirectories();
    }

    public bool TryLaunch(ExternalDiffToolChoice choice, ClipboardEntry previous, ClipboardEntry current)
    {
        ArgumentNullException.ThrowIfNull(choice);
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(current);

        if (_disposed || !File.Exists(choice.ExecutablePath))
        {
            return false;
        }

        ExternalDiffFiles? files = null;
        Process? process = null;
        ActiveComparison? comparison = null;
        try
        {
            files = _workspace.Create(previous.Text, current.Text);
            var startInfo = new ProcessStartInfo
            {
                FileName = choice.ExecutablePath,
                UseShellExecute = false,
                WorkingDirectory = files.DirectoryPath
            };
            foreach (var argument in choice.Tool.BuildArguments(files.PreviousPath, files.CurrentPath))
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
            lock (_gate)
            {
                if (!_disposed)
                {
                    _activeComparisons.Add(process, comparison);
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
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            comparisons = _activeComparisons.Values.ToList();
            _activeComparisons.Clear();
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
        lock (_gate)
        {
            _activeComparisons.TryGetValue(process, out comparison);
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

        lock (_gate)
        {
            _activeComparisons.Remove(comparison.Process);
        }

        comparison.Process.Exited -= OnProcessExited;
        comparison.Process.Dispose();
        ExternalDiffWorkspace.TryDelete(comparison.DirectoryPath);
    }

    private sealed class ActiveComparison(string directoryPath, Process process)
    {
        private int _cleanupScheduled;
        private int _cleanupCompleted;

        public string DirectoryPath { get; } = directoryPath;

        public Process Process { get; } = process;

        public bool TryScheduleCleanup() => Interlocked.Exchange(ref _cleanupScheduled, 1) == 0;

        public bool TryCompleteCleanup() => Interlocked.Exchange(ref _cleanupCompleted, 1) == 0;
    }
}
