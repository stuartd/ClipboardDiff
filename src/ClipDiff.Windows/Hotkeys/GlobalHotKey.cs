using ClipDiff.Windows.Native;

namespace ClipDiff.Windows.Hotkeys;

internal sealed class GlobalHotKey : IDisposable
{
    private const int HotKeyId = 0x4344;
    private readonly NativeMessageWindow _messageWindow;
    private bool _disposed;

    public GlobalHotKey(NativeMessageWindow messageWindow)
    {
        _messageWindow = messageWindow ?? throw new ArgumentNullException(nameof(messageWindow));
        _messageWindow.MessageReceived += OnMessageReceived;
        IsRegistered = NativeMethods.RegisterHotKey(
            _messageWindow.Handle,
            HotKeyId,
            NativeMethods.ModControl | NativeMethods.ModAlt | NativeMethods.ModNoRepeat,
            NativeMethods.VirtualKeyD);
    }

    public event EventHandler? Pressed;

    public bool IsRegistered { get; }

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
            NativeMethods.UnregisterHotKey(_messageWindow.Handle, HotKeyId);
        }
    }

    private void OnMessageReceived(object? sender, NativeMessageEventArgs args)
    {
        if (args.Message != NativeMethods.WmHotKey || args.WParam != HotKeyId)
        {
            return;
        }

        args.Handled = true;
        Pressed?.Invoke(this, EventArgs.Empty);
    }
}
