namespace ClipDiff.Windows.Hotkeys;

internal static class HotKeyChangeTransaction
{
    public static HotKeyChangeResult TrySave(GlobalHotKey hotKey, HotKeyGesture gesture, Func<bool> save)
    {
        ArgumentNullException.ThrowIfNull(hotKey);
        ArgumentNullException.ThrowIfNull(save);
        var previousGesture = hotKey.Gesture;
        if (!hotKey.TryChange(gesture))
        {
            return HotKeyChangeResult.Unavailable;
        }

        if (save())
        {
            return HotKeyChangeResult.Success;
        }

        return hotKey.TryChange(previousGesture)
            ? HotKeyChangeResult.SaveFailed
            : HotKeyChangeResult.SaveFailedAndRestoreFailed;
    }
}
