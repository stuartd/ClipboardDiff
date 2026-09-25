using System.Windows.Interop;

namespace ClipDiff.Windows.Native;

internal sealed class NativeMessageWindow : IDisposable
{
    private readonly HwndSource source;
    private bool disposed;

    public NativeMessageWindow()
    {
        var parameters = new HwndSourceParameters("ClipDiff.MessageWindow")
        {
            ParentWindow = NativeMethods.HwndMessage,
            Width = 0,
            Height = 0,
            WindowStyle = 0
        };
        source = new HwndSource(parameters);
        source.AddHook(WindowProcedure);
    }

    public event EventHandler<NativeMessageEventArgs>? MessageReceived;

    public nint Handle => source.Handle;

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        source.RemoveHook(WindowProcedure);
        source.Dispose();
    }

    private nint WindowProcedure(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        var args = new NativeMessageEventArgs(message, wParam, lParam);
        MessageReceived?.Invoke(this, args);
        handled = args.Handled;
        return nint.Zero;
    }
}
