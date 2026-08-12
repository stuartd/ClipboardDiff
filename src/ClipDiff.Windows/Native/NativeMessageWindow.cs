using System.Windows.Interop;

namespace ClipDiff.Windows.Native;

internal sealed class NativeMessageWindow : IDisposable
{
    private readonly HwndSource _source;
    private bool _disposed;

    public NativeMessageWindow()
    {
        var parameters = new HwndSourceParameters("ClipDiff.MessageWindow")
        {
            ParentWindow = NativeMethods.HwndMessage,
            Width = 0,
            Height = 0,
            WindowStyle = 0
        };
        _source = new HwndSource(parameters);
        _source.AddHook(WindowProcedure);
    }

    public event EventHandler<NativeMessageEventArgs>? MessageReceived;

    public nint Handle => _source.Handle;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _source.RemoveHook(WindowProcedure);
        _source.Dispose();
    }

    private nint WindowProcedure(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        var args = new NativeMessageEventArgs(message, wParam, lParam);
        MessageReceived?.Invoke(this, args);
        handled = args.Handled;
        return nint.Zero;
    }
}

internal sealed class NativeMessageEventArgs(int message, nint wParam, nint lParam) : EventArgs
{
    public int Message { get; } = message;

    public nint WParam { get; } = wParam;

    public nint LParam { get; } = lParam;

    public bool Handled { get; set; }
}
