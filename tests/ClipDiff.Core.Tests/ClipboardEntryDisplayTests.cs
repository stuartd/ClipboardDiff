using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ClipDiff.Core.Tests;

[TestClass]
public sealed class ClipboardEntryDisplayTests
{
    private static readonly DateTimeOffset CapturedAt = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void DifferentFileNamesNeedNoPathContext()
    {
        var labels = ClipboardEntryDisplay.ResolveFileLabels(
            Entry("before.cs", @"C:\repo\src\before.cs"),
            Entry("after.cs", @"C:\repo\src\after.cs"));

        Assert.AreEqual("before.cs", labels.Previous);
        Assert.AreEqual("after.cs", labels.Current);
    }

    [TestMethod]
    public void MatchingFileNamesUseTheShortestUniquePathSuffix()
    {
        var labels = ClipboardEntryDisplay.ResolveFileLabels(
            Entry("settings.json", @"C:\worktrees\branch-a\project\src\settings.json"),
            Entry("settings.json", @"C:\worktrees\branch-b\project\src\settings.json"));

        Assert.AreEqual("branch-a/project/src/settings.json", labels.Previous);
        Assert.AreEqual("branch-b/project/src/settings.json", labels.Current);
    }

    [TestMethod]
    public void MatchingFileNamesInDifferentImmediateParentsUseOnlyThoseParents()
    {
        var labels = ClipboardEntryDisplay.ResolveFileLabels(
            Entry("app.cs", "/repo/old/app.cs"),
            Entry("app.cs", "/repo/new/app.cs"));

        Assert.AreEqual("old/app.cs", labels.Previous);
        Assert.AreEqual("new/app.cs", labels.Current);
    }

    [TestMethod]
    public void SameFilePathDoesNotAddRedundantContext()
    {
        var labels = ClipboardEntryDisplay.ResolveFileLabels(
            Entry("app.cs", @"C:\repo\app.cs"),
            Entry("app.cs", @"C:\repo\app.cs"));

        Assert.AreEqual("app.cs", labels.Previous);
        Assert.AreEqual("app.cs", labels.Current);
    }

    private static ClipboardEntry Entry(string fileName, string filePath) =>
        new(Guid.NewGuid(), "contents", CapturedAt, fileName, filePath);
}
