using ClipDiff.Windows.Native;

namespace ClipDiff.Windows.Clipboard;

internal sealed class ClipboardMonitor : IDisposable
{
    private static readonly TimeSpan[] RetryDelays =
    [
        TimeSpan.FromMilliseconds(10),
        TimeSpan.FromMilliseconds(25),
        TimeSpan.FromMilliseconds(50),
        TimeSpan.FromMilliseconds(100),
        TimeSpan.FromMilliseconds(200)
    ];

    private readonly NativeMessageWindow messageWindow;
    private readonly NativeClipboard nativeClipboard;
    private readonly ClipboardPrivacyInspector inspector;
    private readonly CopiedFileTextReader copiedFileTextReader = new();
    private readonly SemaphoreSlim readGate = new(1, 1);
    private CancellationTokenSource? pendingRead;
    private uint baselineSequence;
    private uint latestRequestedSequence;
    private uint? ownWriteSequence;
    private bool enabled = true;
    private bool disposed;

    public ClipboardMonitor(NativeMessageWindow messageWindow)
    {
        this.messageWindow = messageWindow ?? throw new ArgumentNullException(nameof(messageWindow));
        nativeClipboard = new NativeClipboard();
        var formats = new ClipboardFormatIds(
            RegisterFormat("ExcludeClipboardContentFromMonitorProcessing"),
            RegisterFormat("CanIncludeInClipboardHistory"),
            RegisterFormat("CanUploadToCloudClipboard"));
        inspector = new ClipboardPrivacyInspector(nativeClipboard, formats);

        baselineSequence = NativeMethods.GetClipboardSequenceNumber();
        latestRequestedSequence = baselineSequence;
        this.messageWindow.MessageReceived += OnMessageReceived;
        IsRegistered = NativeMethods.AddClipboardFormatListener(this.messageWindow.Handle);
    }

    public event EventHandler<ClipboardObservation>? ObservationReceived;

    public bool IsRegistered { get; }

    public uint BaselineSequence => baselineSequence;

    public void Pause()
    {
        enabled = false;
        CancelPendingRead();
    }

    public uint Resume()
    {
        CancelPendingRead();
        baselineSequence = NativeMethods.GetClipboardSequenceNumber();
        latestRequestedSequence = baselineSequence;
        ownWriteSequence = null;
        enabled = true;
        return baselineSequence;
    }

    public void SuppressOwnWrite(uint sequenceNumber)
    {
        ownWriteSequence = sequenceNumber;
        baselineSequence = sequenceNumber;
        latestRequestedSequence = sequenceNumber;
        CancelPendingRead();
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        enabled = false;
        CancelPendingRead();
        messageWindow.MessageReceived -= OnMessageReceived;
        if (IsRegistered)
        {
            NativeMethods.RemoveClipboardFormatListener(messageWindow.Handle);
        }

        readGate.Dispose();
    }

    private static uint RegisterFormat(string name)
    {
        var format = NativeMethods.RegisterClipboardFormat(name);
        return format != 0
            ? format
            : throw new InvalidOperationException("A required clipboard format could not be registered.");
    }

    private void OnMessageReceived(object? sender, NativeMessageEventArgs args)
    {
        if (args.Message != NativeMethods.WmClipboardUpdate || !enabled || disposed)
        {
            return;
        }

        args.Handled = true;
        var sequence = NativeMethods.GetClipboardSequenceNumber();
        if (sequence == baselineSequence || sequence == latestRequestedSequence)
        {
            return;
        }

        if (ownWriteSequence == sequence)
        {
            baselineSequence = sequence;
            latestRequestedSequence = sequence;
            ownWriteSequence = null;
            return;
        }

        if (latestRequestedSequence != baselineSequence)
        {
            // A newer update superseded an item that could not be inspected. Surface only
            // the failure state so history cannot mistake a later clear as immediately
            // following an older accepted value.
            baselineSequence = latestRequestedSequence;
            ObservationReceived?.Invoke(
                this,
                ClipboardObservation.InspectionFailed(latestRequestedSequence, DateTimeOffset.Now));
        }

        latestRequestedSequence = sequence;
        CancelPendingRead();
        pendingRead = new CancellationTokenSource();
        _ = ProcessSequenceAsync(sequence, pendingRead.Token);
    }

    private async Task ProcessSequenceAsync(uint expectedSequence, CancellationToken cancellationToken)
    {
        try
        {
            await readGate.WaitAsync(cancellationToken);
            try
            {
                for (var attempt = 0; attempt <= RetryDelays.Length; attempt++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!enabled || NativeMethods.GetClipboardSequenceNumber() != expectedSequence)
                    {
                        return;
                    }

                    ClipboardInspection? inspection = null;
                    if (nativeClipboard.TryOpen(messageWindow.Handle))
                    {
                        try
                        {
                            inspection = inspector.Inspect(expectedSequence, DateTimeOffset.Now);
                        }
                        finally
                        {
                            nativeClipboard.Close();
                        }
                    }

                    var observation = inspection switch
                    {
                        ClipboardInspection.Completed completed => completed.Observation,
                        ClipboardInspection.CopiedFiles copiedFiles => await CreateFileObservationAsync(
                            copiedFiles,
                            cancellationToken),
                        _ => null
                    };

                    if (observation is not null &&
                        observation.Kind != ClipboardObservationKind.InspectionFailed &&
                        NativeMethods.GetClipboardSequenceNumber() == expectedSequence)
                    {
                        baselineSequence = expectedSequence;
                        ObservationReceived?.Invoke(this, observation);
                        return;
                    }

                    if (attempt < RetryDelays.Length)
                    {
                        await Task.Delay(RetryDelays[attempt], cancellationToken);
                    }
                }

                if (enabled && NativeMethods.GetClipboardSequenceNumber() == expectedSequence)
                {
                    baselineSequence = expectedSequence;
                    ObservationReceived?.Invoke(
                        this,
                        ClipboardObservation.InspectionFailed(expectedSequence, DateTimeOffset.Now));
                }
            }
            finally
            {
                readGate.Release();
            }
        }
        catch (OperationCanceledException)
        {
            // A newer clipboard sequence superseded this read.
        }
        catch (ObjectDisposedException) when (disposed)
        {
        }
    }

    private async ValueTask<ClipboardObservation> CreateFileObservationAsync(
        ClipboardInspection.CopiedFiles copiedFiles,
        CancellationToken cancellationToken)
    {
        var readTask = Task.Run(
            async () => await copiedFileTextReader.ReadValuesAsync(copiedFiles.FilePaths, cancellationToken),
            CancellationToken.None);
        var values = await readTask.WaitAsync(cancellationToken);
        return values.Count switch
        {
            1 => ClipboardObservation.TextValue(
                copiedFiles.SequenceNumber,
                copiedFiles.ObservedAt,
                values[0].Text,
                values[0].FileName,
                values[0].FilePath),
            2 => ClipboardObservation.TextPair(
                copiedFiles.SequenceNumber,
                copiedFiles.ObservedAt,
                values[0].Text,
                values[1].Text,
                values[0].FileName,
                values[1].FileName,
                values[0].FilePath,
                values[1].FilePath),
            _ => ClipboardObservation.NonText(copiedFiles.SequenceNumber, copiedFiles.ObservedAt)
        };
    }

    private void CancelPendingRead()
    {
        var pending = pendingRead;
        pendingRead = null;
        if (pending is null)
        {
            return;
        }

        pending.Cancel();
        pending.Dispose();
    }
}
