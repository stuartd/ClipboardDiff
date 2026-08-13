using ClipDiff.Windows.ExternalDiff;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ClipDiff.Windows.Tests;

[TestClass]
public sealed class ExternalDiffToolCatalogTests
{
    private const string Previous = @"C:\Temp Folder\Previous clipboard.txt";
    private const string Current = @"C:\Temp Folder\Current clipboard.txt";

    [TestMethod]
    public void CatalogSupportsCommonDeveloperDiffPrograms()
    {
        CollectionAssert.AreEquivalent(
            new[]
            {
                "diffmerge", "winmerge", "meld", "kdiff3", "beyond-compare", "araxis",
                "vscode", "visual-studio", "tortoisegitmerge", "tortoisemerge", "p4merge", "examdiff"
            },
            ExternalDiffToolCatalog.Tools.Select(tool => tool.Id).ToArray());
    }

    [TestMethod]
    [DataRow(@"C:\Tools\sgdm.exe", "diffmerge")]
    [DataRow(@"C:\Tools\WinMergeU.exe", "winmerge")]
    [DataRow(@"C:\Tools\Meld.exe", "meld")]
    [DataRow(@"C:\Tools\kdiff3.exe", "kdiff3")]
    [DataRow(@"C:\Tools\BComp.exe", "beyond-compare")]
    [DataRow(@"C:\Araxis\ConsoleCompare.exe", "araxis")]
    [DataRow(@"C:\Tools\Code.exe", "vscode")]
    [DataRow(@"C:\Tools\devenv.exe", "visual-studio")]
    [DataRow(@"C:\Tools\TortoiseGitMerge.exe", "tortoisegitmerge")]
    [DataRow(@"C:\Tools\TortoiseMerge.exe", "tortoisemerge")]
    [DataRow(@"C:\Tools\p4merge.exe", "p4merge")]
    [DataRow(@"C:\Tools\ExamDiff.exe", "examdiff")]
    public void MatchesKnownExecutableNames(string executablePath, string expectedId)
    {
        Assert.AreEqual(expectedId, ExternalDiffToolCatalog.MatchExecutable(executablePath).Id);
    }

    [TestMethod]
    public void AmbiguousCompareExecutableIsAraxisOnlyWhenPathIdentifiesAraxis()
    {
        Assert.AreEqual("araxis", ExternalDiffToolCatalog.MatchExecutable(@"C:\Program Files\Araxis\Compare.exe").Id);
        Assert.AreEqual("custom", ExternalDiffToolCatalog.MatchExecutable(@"C:\Other\Compare.exe").Id);
    }

    [TestMethod]
    public void CustomProgramReceivesTwoSeparatePositionalPaths()
    {
        var tool = ExternalDiffToolCatalog.MatchExecutable(@"C:\Tools\MyDiff.exe");

        CollectionAssert.AreEqual(new[] { Previous, Current }, tool.BuildArguments(Previous, Current).ToArray());
    }

    [TestMethod]
    public void WinMergeUsesReadOnlySeparateInstanceAndTitles()
    {
        var arguments = Arguments("winmerge");

        CollectionAssert.AreEqual(
            new[]
            {
                "/e", "/u", "/s-", "/wl", "/wr",
                "/dl", "Previous clipboard", "/dr", "Current clipboard", Previous, Current
            },
            arguments);
    }

    [TestMethod]
    public void DiffMergeUsesPanelTitlesAndReadOnlyCurrentSide()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                "-caption=ClipDiff", "-t1=Previous clipboard", "-t2=Current clipboard",
                "-ro2", Previous, Current
            },
            Arguments("diffmerge"));
    }

    [TestMethod]
    public void BeyondCompareUsesSoloReadOnlyComparison()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                "/solo", "/readonly", "/lefttitle=Previous clipboard", "/righttitle=Current clipboard",
                Previous, Current
            },
            Arguments("beyond-compare"));
    }

    [TestMethod]
    public void AraxisWaitsForReadOnlyTwoWayComparison()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                "/wait", "/readOnly", "/2", "/title1:Previous clipboard", "/title2:Current clipboard",
                Previous, Current
            },
            Arguments("araxis"));
    }

    [TestMethod]
    public void VisualStudioCodeUsesDiffAndWait()
    {
        CollectionAssert.AreEqual(
            new[] { "--diff", "--wait", Previous, Current },
            Arguments("vscode"));
    }

    [TestMethod]
    public void KDiff3AndVisualStudioUseNamedSides()
    {
        CollectionAssert.AreEqual(
            new[] { "--L1", "Previous clipboard", "--L2", "Current clipboard", Previous, Current },
            Arguments("kdiff3"));
        CollectionAssert.AreEqual(
            new[] { "/Diff", Previous, Current, "Previous clipboard", "Current clipboard" },
            Arguments("visual-studio"));
    }

    [TestMethod]
    public void TortoiseToolsUseReadOnlyNamedSides()
    {
        var expected = new[]
        {
            "/readonly", $"/base:{Previous}", $"/mine:{Current}",
            "/basename:Previous clipboard", "/minename:Current clipboard"
        };

        CollectionAssert.AreEqual(expected, Arguments("tortoisegitmerge"));
        CollectionAssert.AreEqual(expected, Arguments("tortoisemerge"));
    }

    [TestMethod]
    public void PositionalToolsKeepPathsAsSeparateArguments()
    {
        CollectionAssert.AreEqual(new[] { Previous, Current }, Arguments("meld"));
        CollectionAssert.AreEqual(new[] { Previous, Current }, Arguments("p4merge"));
    }

    [TestMethod]
    public void ExamDiffUsesPositionalPathsAndDisplayNames()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                Previous, Current,
                "--left_display_name:Previous clipboard",
                "--right_display_name:Current clipboard"
            },
            Arguments("examdiff"));
    }

    private static string[] Arguments(string id) =>
        ExternalDiffToolCatalog.Tools.Single(tool => tool.Id == id).BuildArguments(Previous, Current).ToArray();
}
