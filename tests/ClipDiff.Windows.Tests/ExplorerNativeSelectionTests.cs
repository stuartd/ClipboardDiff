using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using ClipDiff.Windows.Explorer;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ClipDiff.Windows.Tests;

[TestClass]
public sealed class ExplorerNativeSelectionTests
{
    [TestMethod]
    public void RealShellSelectionUsesCorrectCountAndAttributeInterfacesOnWindows()
    {
        if (OperatingSystem.IsWindows())
        {
            VerifyNativeSelections();
        }
        else
        {
            Assert.Inconclusive("Native Shell selection queries require Windows.");
        }
    }

    [SupportedOSPlatform("windows")]
    private static void VerifyNativeSelections()
    {
        var initializeResult = CoInitializeEx(0, 0);
        Assert.IsTrue(initializeResult >= 0 || initializeResult == unchecked((int)0x80010106));
        var directory = Path.Combine(Path.GetTempPath(), $"ClipDiff.ShellTests.{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(directory);
            string[] files = ["old file.txt", "new 雪.txt", "third.txt"];
            var paths = files.Select(file => Path.Combine(directory, file)).ToArray();
            foreach (var path in paths)
            {
                File.WriteAllText(path, string.Empty);
            }

            AssertSelection([paths[0]], ExplorerCommandState.Hidden);
            AssertSelection([paths[0], paths[1]], ExplorerCommandState.Enabled);
            AssertSelection(paths, ExplorerCommandState.Hidden);
            AssertSelection([paths[0], directory], ExplorerCommandState.Hidden);
            AssertSelection([directory, paths[1]], ExplorerCommandState.Hidden);
        }
        finally
        {
            try
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, recursive: true);
                }
            }
            finally
            {
                if (initializeResult >= 0)
                {
                    CoUninitialize();
                }
            }
        }
    }

    [SupportedOSPlatform("windows")]
    private static void AssertSelection(string[] paths, uint expectedState)
    {
        var itemIds = new nint[paths.Length];
        IShellItemArray? selection = null;
        try
        {
            for (var index = 0; index < paths.Length; index++)
            {
                Marshal.ThrowExceptionForHR(SHParseDisplayName(paths[index], 0, out itemIds[index], 0, out _));
            }

            Marshal.ThrowExceptionForHR(SHCreateShellItemArrayFromIDLists((uint)itemIds.Length, itemIds, out selection));
            var target = new ExplorerDropTarget(_ => Assert.Fail("A menu query must not compare files."), () => true);

            Assert.AreEqual(0, target.GetState(selection, okToBeSlow: false, out var state));
            Assert.AreEqual(expectedState, state);
        }
        finally
        {
            if (selection is not null)
            {
                Marshal.ReleaseComObject(selection);
            }

            foreach (var itemId in itemIds)
            {
                Marshal.FreeCoTaskMem(itemId);
            }
        }
    }

    [DllImport("ole32.dll")]
    private static extern int CoInitializeEx(nint reserved, uint coInit);

    [DllImport("ole32.dll")]
    private static extern void CoUninitialize();

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int SHParseDisplayName(string name, nint bindContext, out nint itemId, uint mask, out uint attributes);

    [DllImport("shell32.dll")]
    private static extern int SHCreateShellItemArrayFromIDLists(
        uint count,
        [In, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] nint[] itemIds,
        out IShellItemArray selection);
}
