namespace ClipDiff.Windows.Native;

internal sealed class NativeMessageEventArgs(int message, nint wParam, nint lParam) : EventArgs
{
    public int Message { get; } = message;

    public nint WParam { get; } = wParam;

    public nint LParam { get; } = lParam;

    public bool Handled { get; set; }
}
