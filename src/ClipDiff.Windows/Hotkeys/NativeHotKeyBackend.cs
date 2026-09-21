using ClipDiff.Windows.Native;

namespace ClipDiff.Windows.Hotkeys;

internal sealed class NativeHotKeyBackend(NativeMessageWindow messageWindow) : IHotKeyBackend
{
    public event EventHandler<NativeMessageEventArgs>? MessageReceived
    {
        add => messageWindow.MessageReceived += value;
        remove => messageWindow.MessageReceived -= value;
    }

    public bool Register(int id, uint modifiers, uint virtualKey) =>
        NativeMethods.RegisterHotKey(messageWindow.Handle, id, modifiers, virtualKey);

    public bool Unregister(int id) => NativeMethods.UnregisterHotKey(messageWindow.Handle, id);
}
