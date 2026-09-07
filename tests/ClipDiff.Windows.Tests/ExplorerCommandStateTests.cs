using System.Runtime.InteropServices;
using ClipDiff.Windows.Explorer;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ClipDiff.Windows.Tests;

[TestClass]
public sealed class ExplorerCommandStateTests
{
    private const uint FileSystem = 0x40000000;
    private const uint Folder = 0x20000000;
    private const uint Stream = 0x00400000;
    private const int EFail = unchecked((int)0x80004005);

    [TestMethod]
    [DataRow(0, ExplorerCommandState.Hidden)]
    [DataRow(1, ExplorerCommandState.Hidden)]
    [DataRow(2, ExplorerCommandState.Enabled)]
    [DataRow(3, ExplorerCommandState.Hidden)]
    [DataRow(100, ExplorerCommandState.Hidden)]
    public void MenuIsVisibleOnlyForExactlyTwoFiles(int count, uint expectedState)
    {
        var selection = new FakeShellItemArray(Enumerable.Repeat(FileSystem, count).ToArray());
        var target = CreateTarget();

        Assert.AreEqual(0, target.GetState(selection, okToBeSlow: false, out var state));

        Assert.AreEqual(expectedState, state);
        Assert.AreEqual(count == 2 ? 2 : 0, selection.AttributeQueries);
    }

    [TestMethod]
    public void NoSelectionIsHidden()
    {
        CreateTarget().GetState(null, okToBeSlow: false, out var state);

        Assert.AreEqual(ExplorerCommandState.Hidden, state);
    }

    [TestMethod]
    [DataRow(FileSystem, FileSystem | Folder)]
    [DataRow(FileSystem | Folder, FileSystem)]
    [DataRow(FileSystem | Folder, FileSystem | Folder)]
    [DataRow(FileSystem, 0u)]
    [DataRow(0u, FileSystem)]
    [DataRow(FileSystem | Folder | Stream, FileSystem | Folder)]
    public void FoldersAndVirtualItemsAreHidden(uint firstAttributes, uint secondAttributes)
    {
        var selection = new FakeShellItemArray(firstAttributes, secondAttributes);

        CreateTarget().GetState(selection, okToBeSlow: true, out var state);

        Assert.AreEqual(ExplorerCommandState.Hidden, state);
    }

    [TestMethod]
    public void ArchiveFilesWithShellFolderAttributesAreStillFiles()
    {
        var selection = new FakeShellItemArray(FileSystem | Folder | Stream, FileSystem | Stream);

        CreateTarget().GetState(selection, okToBeSlow: false, out var state);

        Assert.AreEqual(ExplorerCommandState.Enabled, state);
    }

    [TestMethod]
    public void ExistingHandlerReflectsPauseAndResumeWithoutInspectingPausedSelection()
    {
        var canCompare = true;
        var target = new ExplorerDropTarget(_ => Assert.Fail("State queries must not invoke a comparison."), () => canCompare);
        var selection = new FakeShellItemArray(FileSystem, FileSystem);
        target.GetState(selection, okToBeSlow: false, out var state);
        Assert.AreEqual(ExplorerCommandState.Enabled, state);

        canCompare = false;
        var pausedSelection = new FakeShellItemArray(FileSystem, FileSystem);
        target.GetState(pausedSelection, okToBeSlow: false, out state);
        Assert.AreEqual(ExplorerCommandState.Hidden, state);
        Assert.AreEqual(0, pausedSelection.CountQueries);
        Assert.AreEqual(0, pausedSelection.AttributeQueries);

        canCompare = true;
        target.GetState(selection, okToBeSlow: false, out state);
        Assert.AreEqual(ExplorerCommandState.Enabled, state);
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void FailedSelectionInspectionIsHidden(bool failCount)
    {
        var selection = new FakeShellItemArray(FileSystem, FileSystem)
        {
            CountResult = failCount ? EFail : 0,
            AttributesResult = failCount ? null : EFail
        };

        Assert.AreEqual(0, CreateTarget().GetState(selection, okToBeSlow: false, out var state));

        Assert.AreEqual(ExplorerCommandState.Hidden, state);
    }

    [TestMethod]
    public void UnavailableShellObjectIsHidden()
    {
        var selection = new FakeShellItemArray(FileSystem, FileSystem)
        {
            CountException = new COMException("Selection unavailable", EFail)
        };

        Assert.AreEqual(0, CreateTarget().GetState(selection, okToBeSlow: false, out var state));

        Assert.AreEqual(ExplorerCommandState.Hidden, state);
    }

    [TestMethod]
    public void ClassFactoryExposesBothStateAndDropInterfacesOnWindows()
    {
        if (OperatingSystem.IsWindows())
        {
            var factory = new ExplorerDropTargetClassFactory(_ => { }, () => true);
            var unknownId = new Guid("00000000-0000-0000-C000-000000000046");
            Assert.AreEqual(0, factory.CreateInstance(0, ref unknownId, out var instance));
            try
            {
                foreach (var interfaceId in new[] { typeof(IExplorerCommandState).GUID, typeof(IExplorerDropTarget).GUID })
                {
                    Assert.AreEqual(0, Marshal.QueryInterface(instance, in interfaceId, out var pointer));
                    Marshal.Release(pointer);
                }
            }
            finally
            {
                Marshal.Release(instance);
            }
        }
        else
        {
            Assert.Inconclusive("Native COM interface exposure requires Windows.");
        }
    }

    private static ExplorerDropTarget CreateTarget() =>
        new(_ => Assert.Fail("State queries must not invoke a comparison."), () => true);

    private sealed class FakeShellItemArray(params uint[] itemAttributes) : IShellItemArray
    {
        public int CountResult { get; init; }
        public int? AttributesResult { get; init; }
        public Exception? CountException { get; init; }
        public int CountQueries { get; private set; }
        public int AttributeQueries { get; private set; }

        public int GetCount(out uint count)
        {
            CountQueries++;
            if (CountException is not null)
            {
                throw CountException;
            }

            count = (uint)itemAttributes.Length;
            return CountResult;
        }

        public int GetAttributes(uint flags, uint mask, out uint attributes)
        {
            AttributeQueries++;
            attributes = flags switch
            {
                1 => itemAttributes.Aggregate(uint.MaxValue, (result, value) => result & value) & mask,
                2 => itemAttributes.Aggregate(0u, (result, value) => result | value) & mask,
                _ => throw new AssertFailedException("Unexpected attribute combination policy.")
            };
            // S_FALSE is a successful result when, for example, neither file is a folder.
            return AttributesResult ?? (attributes == mask ? 0 : 1);
        }

        public int BindToHandler(nint context, in Guid handler, in Guid iid, out nint result) => throw UnexpectedRead();
        public int GetPropertyStore(uint flags, in Guid iid, out nint result) => throw UnexpectedRead();
        public int GetPropertyDescriptionList(nint key, in Guid iid, out nint result) => throw UnexpectedRead();
        public int GetItemAt(uint index, out nint item) => throw UnexpectedRead();
        public int EnumItems(out nint items) => throw UnexpectedRead();

        private static AssertFailedException UnexpectedRead() =>
            new("Menu visibility must use only the selection count and attributes, without obtaining paths or contents.");
    }
}
