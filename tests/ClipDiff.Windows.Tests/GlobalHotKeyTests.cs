using ClipDiff.Windows.Hotkeys;
using ClipDiff.Windows.Native;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ClipDiff.Windows.Tests;

[TestClass]
public sealed class GlobalHotKeyTests
{
    private static readonly HotKeyGesture Custom = new(HotKeyModifiers.Control | HotKeyModifiers.Shift, 0x36);

    [TestMethod]
    public void StartupFailurePreservesConfiguredGestureAndCanBeRetried()
    {
        var backend = new FakeHotKeyBackend { RejectRegistration = true };
        using var hotKey = new GlobalHotKey(backend, Custom);
        Assert.IsFalse(hotKey.IsRegistered);
        Assert.AreEqual(Custom, hotKey.Gesture);
        backend.RejectRegistration = false;
        Assert.IsTrue(hotKey.TryChange(Custom));
        Assert.IsTrue(hotKey.IsRegistered);
        Assert.AreEqual(Custom, hotKey.Gesture);
    }

    [TestMethod]
    public void InvalidStartupGestureFallsBackBeforeRegistration()
    {
        var backend = new FakeHotKeyBackend();
        using var hotKey = new GlobalHotKey(backend, new(HotKeyModifiers.None, 0));
        Assert.AreEqual(HotKeyGesture.Default, hotKey.Gesture);
        Assert.AreEqual(HotKeyGesture.Default, backend.Registrations.Single().Value);
        Assert.IsTrue(backend.LastModifiers.HasValue);
        Assert.AreEqual((uint)HotKeyGesture.Default.Modifiers | NativeMethods.ModNoRepeat, backend.LastModifiers!.Value);
    }

    [TestMethod]
    public void FailedReplacementKeepsCurrentRegistration()
    {
        var backend = new FakeHotKeyBackend();
        using var hotKey = new GlobalHotKey(backend, HotKeyGesture.Default);
        backend.RejectRegistration = true;
        Assert.IsFalse(hotKey.TryChange(Custom));
        Assert.AreEqual(HotKeyGesture.Default, hotKey.Gesture);
        Assert.IsTrue(hotKey.IsRegistered);
        CollectionAssert.AreEqual(new[] { "register", "register" }, backend.Calls);
    }

    [TestMethod]
    public void ReplacementIsRegisteredBeforeOldShortcutIsReleased()
    {
        var backend = new FakeHotKeyBackend();
        using var hotKey = new GlobalHotKey(backend, HotKeyGesture.Default);
        Assert.IsTrue(hotKey.TryChange(Custom));
        Assert.AreEqual(Custom, hotKey.Gesture);
        Assert.AreEqual(Custom, backend.Registrations.Single().Value);
        CollectionAssert.AreEqual(new[] { "register", "register", "unregister" }, backend.Calls);
    }

    [TestMethod]
    public void FailedOldUnregistrationRemovesReplacementAndPreservesOldShortcut()
    {
        var backend = new FakeHotKeyBackend();
        using var hotKey = new GlobalHotKey(backend, HotKeyGesture.Default);
        backend.FailNextUnregister = true;
        Assert.IsFalse(hotKey.TryChange(Custom));
        Assert.AreEqual(HotKeyGesture.Default, hotKey.Gesture);
        Assert.AreEqual(HotKeyGesture.Default, backend.Registrations.Single().Value);
    }

    [TestMethod]
    public void SavingSameShortcutDoesNotConflictWithItself()
    {
        var backend = new FakeHotKeyBackend();
        using var hotKey = new GlobalHotKey(backend, Custom);
        backend.RejectRegistration = true;
        Assert.IsTrue(hotKey.TryChange(Custom));
        Assert.AreEqual(1, backend.Calls.Count);
    }

    [TestMethod]
    public void QueuedMessageForReusedIdDoesNotTriggerNewShortcut()
    {
        var backend = new FakeHotKeyBackend();
        using var hotKey = new GlobalHotKey(backend, HotKeyGesture.Default);
        var oldId = backend.Registrations.Single().Key;
        var presses = 0;
        hotKey.Pressed += (_, _) => presses++;
        Assert.IsTrue(hotKey.TryChange(Custom));
        var newest = new HotKeyGesture(HotKeyModifiers.Alt, 0x47);
        Assert.IsTrue(hotKey.TryChange(newest));
        Assert.AreEqual(oldId, backend.Registrations.Single().Key);
        backend.Raise(oldId, HotKeyGesture.Default);
        Assert.AreEqual(0, presses);
        backend.Raise(oldId, newest);
        Assert.AreEqual(1, presses);
    }

    [TestMethod]
    public void UnregisteredAndDisposedHotkeysCannotFire()
    {
        var backend = new FakeHotKeyBackend { RejectRegistration = true };
        var hotKey = new GlobalHotKey(backend, Custom);
        var presses = 0;
        hotKey.Pressed += (_, _) => presses++;
        backend.Raise(backend.LastId, Custom);
        Assert.AreEqual(0, presses);
        backend.RejectRegistration = false;
        Assert.IsTrue(hotKey.TryChange(Custom));
        hotKey.Dispose();
        hotKey.Dispose();
        backend.Raise(backend.LastId, Custom);
        Assert.AreEqual(0, presses);
        Assert.IsFalse(hotKey.IsRegistered);
        Assert.IsFalse(hotKey.TryChange(Custom));
        Assert.IsEmpty(backend.Registrations);
    }

    [TestMethod]
    public void ConflictingShortcutDoesNotReachSettingsSave()
    {
        var backend = new FakeHotKeyBackend();
        using var hotKey = new GlobalHotKey(backend, HotKeyGesture.Default);
        backend.RejectRegistration = true;
        var saved = false;
        var result = HotKeyChangeTransaction.TrySave(hotKey, Custom, () => { saved = true; return true; });
        Assert.AreEqual(HotKeyChangeResult.Unavailable, result);
        Assert.IsFalse(saved);
        Assert.AreEqual(HotKeyGesture.Default, hotKey.Gesture);
    }

    [TestMethod]
    public void SaveFailureRestoresPreviousShortcut()
    {
        var backend = new FakeHotKeyBackend();
        using var hotKey = new GlobalHotKey(backend, HotKeyGesture.Default);
        var result = HotKeyChangeTransaction.TrySave(hotKey, Custom, () => false);
        Assert.AreEqual(HotKeyChangeResult.SaveFailed, result);
        Assert.AreEqual(HotKeyGesture.Default, hotKey.Gesture);
        Assert.AreEqual(HotKeyGesture.Default, backend.Registrations.Single().Value);
    }

    [TestMethod]
    public void FailedRollbackReportsTheReplacementIsStillActive()
    {
        var backend = new FakeHotKeyBackend();
        using var hotKey = new GlobalHotKey(backend, HotKeyGesture.Default);
        var result = HotKeyChangeTransaction.TrySave(hotKey, Custom, () =>
        {
            backend.RejectRegistration = true;
            return false;
        });
        Assert.AreEqual(HotKeyChangeResult.SaveFailedAndRestoreFailed, result);
        Assert.AreEqual(Custom, hotKey.Gesture);
        Assert.IsTrue(hotKey.IsRegistered);
    }

    [TestMethod]
    public void ResetToDefaultRegistersBeforeSaving()
    {
        var backend = new FakeHotKeyBackend();
        using var hotKey = new GlobalHotKey(backend, Custom);
        var result = HotKeyChangeTransaction.TrySave(hotKey, HotKeyGesture.Default, () =>
        {
            Assert.AreEqual(HotKeyGesture.Default, hotKey.Gesture);
            Assert.AreEqual(HotKeyGesture.Default, backend.Registrations.Single().Value);
            return true;
        });
        Assert.AreEqual(HotKeyChangeResult.Success, result);
    }
}

internal sealed class FakeHotKeyBackend : IHotKeyBackend
{
    public event EventHandler<NativeMessageEventArgs>? MessageReceived;
    public bool RejectRegistration { get; set; }
    public bool FailNextUnregister { get; set; }
    public Dictionary<int, HotKeyGesture> Registrations { get; } = [];
    public List<string> Calls { get; } = [];
    public uint? LastModifiers { get; private set; }
    public int LastId { get; private set; }

    public bool Register(int id, uint modifiers, uint virtualKey)
    {
        Calls.Add("register");
        LastId = id;
        LastModifiers = modifiers;
        if (RejectRegistration)
		{
			return false;
		}

		Registrations.Add(id, new((HotKeyModifiers)(modifiers & ~NativeMethods.ModNoRepeat), virtualKey));
        return true;
    }

    public bool Unregister(int id)
    {
        Calls.Add("unregister");
        if (FailNextUnregister)
        {
            FailNextUnregister = false;
            return false;
        }
        return Registrations.Remove(id);
    }

    public void Raise(int id, HotKeyGesture gesture) => MessageReceived?.Invoke(
        this, new(NativeMethods.WmHotKey, id, (nint)((gesture.VirtualKey << 16) | (uint)gesture.Modifiers)));
}
