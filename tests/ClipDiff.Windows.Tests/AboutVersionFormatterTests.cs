using ClipDiff.Windows.Views;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ClipDiff.Windows.Tests;

[TestClass]
public sealed class AboutVersionFormatterTests
{
    [TestMethod]
    public void IncludesShortCommitFromSdkInformationalVersion()
    {
        var result = AboutVersionFormatter.Format(
            "1.0+753aab75f36e959cf8dfa6d72a2175110c9ed608",
            new Version(1, 0, 0, 0));

        Assert.AreEqual("1.0 (753aab7)", result);
    }

    [TestMethod]
    public void FindsCommitAfterExistingBuildMetadata()
    {
        var result = AboutVersionFormatter.Format(
            "1.0+local.753aab75f36e959cf8dfa6d72a2175110c9ed608",
            new Version(1, 0, 0, 0));

        Assert.AreEqual("1.0 (753aab7)", result);
    }

    [TestMethod]
    public void OmitsNonCommitBuildMetadata()
    {
        var result = AboutVersionFormatter.Format("1.0+preview", new Version(1, 0, 0, 0));

        Assert.AreEqual("1.0", result);
    }

    [TestMethod]
    public void FallsBackToTwoPartAssemblyVersion()
    {
        var result = AboutVersionFormatter.Format(null, new Version(1, 0, 0, 0));

        Assert.AreEqual("1.0", result);
    }
}
