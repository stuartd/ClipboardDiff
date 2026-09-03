using ClipDiff.Windows.Hotkeys;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ClipDiff.Windows.Tests;

[TestClass]
public sealed class HotKeyGestureTests
{
    [TestMethod]
    public void DefaultShortcutIsCtrlAltD()
    {
        Assert.IsTrue(HotKeyGesture.Default.IsValid);
        Assert.AreEqual("Ctrl+Alt+D", HotKeyGesture.Default.DisplayText);
    }

    [TestMethod]
    public void FormatsTopRowNumpadAndFunctionKeys()
    {
        Assert.AreEqual(
            "Ctrl+Alt+6",
            new HotKeyGesture(HotKeyModifiers.Control | HotKeyModifiers.Alt, 0x36).DisplayText);
        Assert.AreEqual(
            "Ctrl+Shift+Num 6",
            new HotKeyGesture(HotKeyModifiers.Control | HotKeyModifiers.Shift, 0x66).DisplayText);
        Assert.AreEqual(
            "Alt+F10",
            new HotKeyGesture(HotKeyModifiers.Alt, 0x79).DisplayText);
    }

    [TestMethod]
    public void RejectsUnsafeOrIncompleteShortcuts()
    {
        Assert.IsFalse(new HotKeyGesture(HotKeyModifiers.None, 0x44).IsValid);
        Assert.IsFalse(new HotKeyGesture(HotKeyModifiers.Shift, 0x44).IsValid);
        Assert.IsFalse(new HotKeyGesture(HotKeyModifiers.Control, 0x11).IsValid);
        Assert.IsFalse(new HotKeyGesture(HotKeyModifiers.Alt, 0x73).IsValid);
        Assert.IsFalse(new HotKeyGesture(HotKeyModifiers.Windows, 0x44).IsValid);
    }

    [TestMethod]
    public void InvalidPersistedShortcutFallsBackToDefault()
    {
        var invalid = new HotKeyGesture(HotKeyModifiers.None, 0);

        Assert.AreEqual(HotKeyGesture.Default, HotKeyGesture.Normalize(invalid));
        Assert.AreEqual(HotKeyGesture.Default, HotKeyGesture.Normalize(null));
    }
}
