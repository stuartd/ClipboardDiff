using ClipDiff.Windows.Hotkeys;
using ClipDiff.Windows.Settings;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ClipDiff.Windows.Tests;

[TestClass]
public sealed class ClipDiffSettingsStoreTests
{
    [TestMethod]
    public void SavesAndLoadsOnlyAllowedApplicationPreferences()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var path = Path.Combine(directory, "settings.json");
            var store = new ClipDiffSettingsStore(path);
            var settings = new ClipDiffSettings(
                @"C:\Tools\Diff.exe",
                true,
                new HotKeyGesture(HotKeyModifiers.Control | HotKeyModifiers.Shift, 0x36));

            Assert.IsTrue(store.TrySave(settings));

            Assert.AreEqual(settings, store.Load());
            var serialized = File.ReadAllText(path);
            StringAssert.Contains(serialized, "SelectedExecutablePath");
            StringAssert.Contains(serialized, "PlaintextWarningAcknowledged");
            StringAssert.Contains(serialized, "HotKey");
            StringAssert.Contains(serialized, "Modifiers");
            StringAssert.Contains(serialized, "VirtualKey");
            Assert.IsFalse(serialized.Contains("DisplayText", StringComparison.Ordinal));
            Assert.IsFalse(serialized.Contains("IsValid", StringComparison.Ordinal));
            Assert.IsFalse(serialized.Contains("clipboard", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [TestMethod]
    public void MissingOrMalformedSettingsReturnSafeDefaults()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var path = Path.Combine(directory, "settings.json");
            var store = new ClipDiffSettingsStore(path);
            Assert.AreEqual(new ClipDiffSettings(), store.Load());

            File.WriteAllText(path, "not json");
            Assert.AreEqual(new ClipDiffSettings(), store.Load());
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [TestMethod]
    public void ExistingSettingsWithoutAHotKeyUseTheDefaultShortcut()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var path = Path.Combine(directory, "settings.json");
            File.WriteAllText(
                path,
                "{\"SelectedExecutablePath\":null,\"PlaintextWarningAcknowledged\":true}");

            var settings = new ClipDiffSettingsStore(path).Load();

            Assert.IsTrue(settings.PlaintextWarningAcknowledged);
            Assert.AreEqual(HotKeyGesture.Default, HotKeyGesture.Normalize(settings.HotKey));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    private static string CreateTemporaryDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ClipDiff.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }
}
