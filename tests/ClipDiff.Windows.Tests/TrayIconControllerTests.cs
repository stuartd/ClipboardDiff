using System.Reflection;
using ClipDiff.Windows.ExternalDiff;
using ClipDiff.Windows.Tray;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Forms = System.Windows.Forms;

namespace ClipDiff.Windows.Tests;

[TestClass]
public sealed class TrayIconControllerTests
{
	[STATestMethod]
	public void ReplacingViewerChoicesDisposesEveryPreviousMenuItem()
	{
		var choice = new ExternalDiffToolChoice(
			ExternalDiffToolCatalog.MatchExecutable("WinMergeU.exe"), @"C:\Synthetic\WinMergeU.exe");
		using var controller = new TrayIconController([choice], null);
		// Keep the old item identities so the test verifies disposal, rather than
		// merely observing that they disappeared from the visible menu.
		var field = typeof(TrayIconController).GetField("diffViewerItem", BindingFlags.Instance | BindingFlags.NonPublic)!;
		var viewer = (Forms.ToolStripMenuItem)field.GetValue(controller)!;

		for (var replacement = 0; replacement < 100; replacement++)
		{
			var previous = viewer.DropDownItems.Cast<Forms.ToolStripItem>().ToArray();
			var disposed = 0;

			foreach (var item in previous)
			{
				item.Disposed += (_, _) => disposed++;
			}

			controller.SetDiffTools([choice], replacement % 2 == 0 ? choice.ExecutablePath : null);

			Assert.AreEqual(previous.Length, disposed);
			Assert.IsFalse(viewer.DropDownItems.Cast<Forms.ToolStripItem>().Any(previous.Contains));
		}
	}
}
