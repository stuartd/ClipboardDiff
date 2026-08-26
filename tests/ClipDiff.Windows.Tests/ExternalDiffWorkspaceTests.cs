using ClipDiff.Windows.ExternalDiff;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ClipDiff.Windows.Tests;

[TestClass]
public sealed class ExternalDiffWorkspaceTests
{
    [TestMethod]
    public void CreatesNamedReadOnlyFilesWithExactUnicodeClipboardText()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var workspace = new ExternalDiffWorkspace(root);

            var files = workspace.Create("previous\r\n秘密\t", "current\n😀");

            Assert.AreEqual("Previous clipboard.txt", Path.GetFileName(files.PreviousPath));
            Assert.AreEqual("Current clipboard.txt", Path.GetFileName(files.CurrentPath));
            Assert.AreEqual("previous\r\n秘密\t", File.ReadAllText(files.PreviousPath));
            Assert.AreEqual("current\n😀", File.ReadAllText(files.CurrentPath));
            Assert.IsTrue(File.GetAttributes(files.PreviousPath).HasFlag(FileAttributes.ReadOnly));
            Assert.IsTrue(File.GetAttributes(files.CurrentPath).HasFlag(FileAttributes.ReadOnly));
        }
        finally
        {
            ExternalDiffWorkspace.TryDelete(root);
        }
    }

    [TestMethod]
    public void EachComparisonUsesAUniqueDirectory()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var workspace = new ExternalDiffWorkspace(root);

            var first = workspace.Create("one", "two");
            var second = workspace.Create("one", "two");

            Assert.AreNotEqual(first.DirectoryPath, second.DirectoryPath);
        }
        finally
        {
            ExternalDiffWorkspace.TryDelete(root);
        }
    }

    [TestMethod]
    public void FileBackedValuesUseTheirSourceFileNames()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var workspace = new ExternalDiffWorkspace(root);

            var files = workspace.Create("old", "new", "same name.cs", "same name.cs");

            Assert.AreEqual("same name.cs", Path.GetFileName(files.PreviousPath));
            Assert.AreEqual("same name.cs", Path.GetFileName(files.CurrentPath));
            Assert.AreEqual("Previous", Path.GetFileName(Path.GetDirectoryName(files.PreviousPath)));
            Assert.AreEqual("Current", Path.GetFileName(Path.GetDirectoryName(files.CurrentPath)));
        }
        finally
        {
            ExternalDiffWorkspace.TryDelete(root);
        }
    }

    [TestMethod]
    public void CleanupRemovesReadOnlyActiveAndStaleComparisonDirectories()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var workspace = new ExternalDiffWorkspace(root);
            var files = workspace.Create("secret one", "secret two");

            workspace.CleanupStaleDirectories();

            Assert.IsFalse(Directory.Exists(files.DirectoryPath));
            Assert.IsTrue(Directory.Exists(root));
        }
        finally
        {
            ExternalDiffWorkspace.TryDelete(root);
        }
    }

    private static string CreateTemporaryDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ClipDiff.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }
}
