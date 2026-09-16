using ClipDiff.Windows.Native;

namespace ClipDiff.Windows.Hotkeys;

internal sealed class GlobalHotKey : IDisposable
{
    private const int PrimaryHotKeyId = 0x4344;
    private const int SecondaryHotKeyId = 0x4345;
    private readonly IHotKeyBackend _backend;
    private int _hotKeyId = PrimaryHotKeyId;
    private bool _disposed;

    public GlobalHotKey(IHotKeyBackend backend, HotKeyGesture gesture)
    {
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));
        Gesture = HotKeyGesture.Normalize(gesture);
        _backend.MessageReceived += OnMessageReceived;
        IsRegistered = TryRegister(_hotKeyId, Gesture);
    }

    public event EventHandler? Pressed;

    public HotKeyGesture Gesture { get; private set; }

    public bool IsRegistered { get; private set; }

    public bool TryChange(HotKeyGesture gesture)
    {
        ArgumentNullException.ThrowIfNull(gesture);

        if (_disposed || !gesture.IsValid)
        {
            return false;
        }

        if (IsRegistered && gesture == Gesture)
        {
            return true;
        }

        var replacementId = _hotKeyId == PrimaryHotKeyId
            ? SecondaryHotKeyId
            : PrimaryHotKeyId;
        if (!TryRegister(replacementId, gesture))
        {
            return false;
        }

        if (IsRegistered && !_backend.Unregister(_hotKeyId))
        {
            _backend.Unregister(replacementId);
            return false;
        }

        _hotKeyId = replacementId;
        Gesture = gesture;
        IsRegistered = true;
        return true;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _backend.MessageReceived -= OnMessageReceived;
        if (IsRegistered)
        {
            _backend.Unregister(_hotKeyId);
            IsRegistered = false;
        }
    }

    private void OnMessageReceived(object? sender, NativeMessageEventArgs args)
    {
        if (args.Message != NativeMethods.WmHotKey || args.WParam != _hotKeyId)
        {
            return;
        }

        args.Handled = true;
        // IDs alternate during replacement. A queued message can refer to an
        // earlier gesture that used the same ID, so check the payload as well.
        var modifiers = (uint)((long)args.LParam & 0xFFFF);
        var virtualKey = (uint)(((long)args.LParam >> 16) & 0xFFFF);
        if (_disposed || !IsRegistered || modifiers != (uint)Gesture.Modifiers || virtualKey != Gesture.VirtualKey)
        {
            return;
        }

        Pressed?.Invoke(this, EventArgs.Empty);
    }

    private bool TryRegister(int id, HotKeyGesture gesture) =>
        _backend.Register(
            id,
            (uint)gesture.Modifiers | NativeMethods.ModNoRepeat,
            gesture.VirtualKey);
}

internal interface IHotKeyBackend
{
    event EventHandler<NativeMessageEventArgs>? MessageReceived;
    bool Register(int id, uint modifiers, uint virtualKey);
    bool Unregister(int id);
}
