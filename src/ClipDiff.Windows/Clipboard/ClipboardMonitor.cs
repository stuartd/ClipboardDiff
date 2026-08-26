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

    private readonly NativeMessageWindow _messageWindow;
    private readonly NativeClipboard _nativeClipboard;
    private readonly ClipboardPrivacyInspector _inspector;
    private readonly CopiedFileTextReader _copiedFileTextReader = new();
    private readonly SemaphoreSlim _readGate = new(1, 1);
    private CancellationTokenSource? _pendingRead;
    private uint _baselineSequence;
    private uint _latestRequestedSequence;
    private uint? _ownWriteSequence;
    private bool _enabled = true;
    private bool _disposed;

    public ClipboardMonitor(NativeMessageWindow messageWindow)
    {
        _messageWindow = messageWindow ?? throw new ArgumentNullException(nameof(messageWindow));
        _nativeClipboard = new NativeClipboard();
        var formats = new ClipboardFormatIds(
            RegisterFormat("ExcludeClipboardContentFromMonitorProcessing"),
            RegisterFormat("CanIncludeInClipboardHistory"),
            RegisterFormat("CanUploadToCloudClipboard"));
        _inspector = new ClipboardPrivacyInspector(_nativeClipboard, formats);

        _baselineSequence = NativeMethods.GetClipboardSequenceNumber();
        _latestRequestedSequence = _baselineSequence;
        _messageWindow.MessageReceived += OnMessageReceived;
        IsRegistered = NativeMethods.AddClipboardFormatListener(_messageWindow.Handle);
    }

    public event EventHandler<ClipboardObservation>? ObservationReceived;

    public bool IsRegistered { get; }

    public uint BaselineSequence => _baselineSequence;

    public void Pause()
    {
        _enabled = false;
        CancelPendingRead();
    }

    public uint Resume()
    {
        CancelPendingRead();
        _baselineSequence = NativeMethods.GetClipboardSequenceNumber();
        _latestRequestedSequence = _baselineSequence;
        _ownWriteSequence = null;
        _enabled = true;
        return _baselineSequence;
    }

    public void SuppressOwnWrite(uint sequenceNumber)
    {
        _ownWriteSequence = sequenceNumber;
        _baselineSequence = sequenceNumber;
        _latestRequestedSequence = sequenceNumber;
        CancelPendingRead();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _enabled = false;
        CancelPendingRead();
        _messageWindow.MessageReceived -= OnMessageReceived;
        if (IsRegistered)
        {
            NativeMethods.RemoveClipboardFormatListener(_messageWindow.Handle);
        }

        _readGate.Dispose();
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
        if (args.Message != NativeMethods.WmClipboardUpdate || !_enabled || _disposed)
        {
            return;
        }

        args.Handled = true;
        var sequence = NativeMethods.GetClipboardSequenceNumber();
        if (sequence == _baselineSequence || sequence == _latestRequestedSequence)
        {
            return;
        }

        if (_ownWriteSequence == sequence)
        {
            _baselineSequence = sequence;
            _latestRequestedSequence = sequence;
            _ownWriteSequence = null;
            return;
        }

        if (_latestRequestedSequence != _baselineSequence)
        {
            // A newer update superseded an item that could not be inspected. Surface only
            // the failure state so history cannot mistake a later clear as immediately
            // following an older accepted value.
            _baselineSequence = _latestRequestedSequence;
            ObservationReceived?.Invoke(
                this,
                ClipboardObservation.InspectionFailed(_latestRequestedSequence, DateTimeOffset.Now));
        }

        _latestRequestedSequence = sequence;
        CancelPendingRead();
        _pendingRead = new CancellationTokenSource();
        _ = ProcessSequenceAsync(sequence, _pendingRead.Token);
    }

    private async Task ProcessSequenceAsync(uint expectedSequence, CancellationToken cancellationToken)
    {
        try
        {
            await _readGate.WaitAsync(cancellationToken);
            try
            {
                for (var attempt = 0; attempt <= RetryDelays.Length; attempt++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!_enabled || NativeMethods.GetClipboardSequenceNumber() != expectedSequence)
                    {
                        return;
                    }

                    ClipboardInspection? inspection = null;
                    if (_nativeClipboard.TryOpen(_messageWindow.Handle))
                    {
                        try
                        {
                            inspection = _inspector.Inspect(expectedSequence, DateTimeOffset.Now);
                        }
                        finally
                        {
                            _nativeClipboard.Close();
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
                        _baselineSequence = expectedSequence;
                        ObservationReceived?.Invoke(this, observation);
                        return;
                    }

                    if (attempt < RetryDelays.Length)
                    {
                        await Task.Delay(RetryDelays[attempt], cancellationToken);
                    }
                }

                if (_enabled && NativeMethods.GetClipboardSequenceNumber() == expectedSequence)
                {
                    _baselineSequence = expectedSequence;
                    ObservationReceived?.Invoke(
                        this,
                        ClipboardObservation.InspectionFailed(expectedSequence, DateTimeOffset.Now));
                }
            }
            finally
            {
                _readGate.Release();
            }
        }
        catch (OperationCanceledException)
        {
            // A newer clipboard sequence superseded this read.
        }
        catch (ObjectDisposedException) when (_disposed)
        {
        }
    }

    private async ValueTask<ClipboardObservation> CreateFileObservationAsync(
        ClipboardInspection.CopiedFiles copiedFiles,
        CancellationToken cancellationToken)
    {
        var readTask = Task.Run(
            async () => await _copiedFileTextReader.ReadValuesAsync(copiedFiles.FilePaths, cancellationToken),
            CancellationToken.None);
        var values = await readTask.WaitAsync(cancellationToken);
        return values.Count switch
        {
            1 => ClipboardObservation.TextValue(
                copiedFiles.SequenceNumber,
                copiedFiles.ObservedAt,
                values[0].Text,
                values[0].FileName),
            2 => ClipboardObservation.TextPair(
                copiedFiles.SequenceNumber,
                copiedFiles.ObservedAt,
                values[0].Text,
                values[1].Text,
                values[0].FileName,
                values[1].FileName),
            _ => ClipboardObservation.NonText(copiedFiles.SequenceNumber, copiedFiles.ObservedAt)
        };
    }

    private void CancelPendingRead()
    {
        var pending = _pendingRead;
        _pendingRead = null;
        if (pending is null)
        {
            return;
        }

        pending.Cancel();
        pending.Dispose();
    }
}
