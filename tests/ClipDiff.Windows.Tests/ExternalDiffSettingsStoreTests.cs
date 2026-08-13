using ClipDiff.Windows.ExternalDiff;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ClipDiff.Windows.Tests;

[TestClass]
public sealed class ExternalDiffSettingsStoreTests
{
    [TestMethod]
    public void SavesAndLoadsOnlyExternalToolPreferenceAndWarningAcknowledgement()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var path = Path.Combine(directory, "settings.json");
            var store = new ExternalDiffSettingsStore(path);
            var settings = new ExternalDiffSettings(@"C:\Tools\Diff.exe", true);

            Assert.IsTrue(store.TrySave(settings));

            Assert.AreEqual(settings, store.Load());
            var serialized = File.ReadAllText(path);
            StringAssert.Contains(serialized, "SelectedExecutablePath");
            StringAssert.Contains(serialized, "PlaintextWarningAcknowledged");
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
            var store = new ExternalDiffSettingsStore(path);
            Assert.AreEqual(new ExternalDiffSettings(), store.Load());

            File.WriteAllText(path, "not json");
            Assert.AreEqual(new ExternalDiffSettings(), store.Load());
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
