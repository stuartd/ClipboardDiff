using ClipDiff.Windows.Native;

namespace ClipDiff.Windows.Hotkeys;

internal sealed class GlobalHotKey : IDisposable
{
    private const int PrimaryHotKeyId = 0x4344;
    private const int SecondaryHotKeyId = 0x4345;
    private readonly NativeMessageWindow _messageWindow;
    private int _hotKeyId = PrimaryHotKeyId;
    private bool _disposed;

    public GlobalHotKey(NativeMessageWindow messageWindow, HotKeyGesture gesture)
    {
        _messageWindow = messageWindow ?? throw new ArgumentNullException(nameof(messageWindow));
        Gesture = HotKeyGesture.Normalize(gesture);
        _messageWindow.MessageReceived += OnMessageReceived;
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

        if (IsRegistered && !NativeMethods.UnregisterHotKey(_messageWindow.Handle, _hotKeyId))
        {
            NativeMethods.UnregisterHotKey(_messageWindow.Handle, replacementId);
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
        _messageWindow.MessageReceived -= OnMessageReceived;
        if (IsRegistered)
        {
            NativeMethods.UnregisterHotKey(_messageWindow.Handle, _hotKeyId);
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
        Pressed?.Invoke(this, EventArgs.Empty);
    }

    private bool TryRegister(int id, HotKeyGesture gesture) =>
        NativeMethods.RegisterHotKey(
            _messageWindow.Handle,
            id,
            (uint)gesture.Modifiers | NativeMethods.ModNoRepeat,
            gesture.VirtualKey);
}
