using ClipDiff.Windows.Explorer;
using Microsoft.Win32;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ClipDiff.Windows.Tests;

[TestClass]
public sealed class ExplorerRegistrationValueTests
{
	[TestMethod]
	[DataRow(null)]
	[DataRow("Icon")]
	public void UnchangedRegistrationDoesNotRequestAnotherShellNotification(string? valueName)
	{
		WithKey(key =>
		{
			Assert.IsTrue(ExplorerContextMenuRegistration.SetStringValue(key, valueName, "synthetic registration"));

			for (var update = 0; update < 1_000; update++)
			{
				Assert.IsFalse(ExplorerContextMenuRegistration.SetStringValue(key, valueName, "synthetic registration"));
			}

			Assert.IsTrue(ExplorerContextMenuRegistration.SetStringValue(key, valueName, "changed registration"));
			Assert.AreEqual("changed registration", key.GetValue(valueName));
			Assert.IsFalse(ExplorerContextMenuRegistration.SetStringValue(key, valueName, "changed registration"));
		});
	}

	[TestMethod]
	public void RegistrationStillRepairsDeletedValuesAndIncorrectRegistryTypes()
	{
		WithKey(key =>
		{
			key.SetValue("Icon", "unchanged text", RegistryValueKind.ExpandString);
			Assert.IsTrue(ExplorerContextMenuRegistration.SetStringValue(key, "Icon", "unchanged text"));
			Assert.AreEqual(RegistryValueKind.String, key.GetValueKind("Icon"));
			key.DeleteValue("Icon");
			Assert.IsTrue(ExplorerContextMenuRegistration.SetStringValue(key, "Icon", "unchanged text"));
		});
	}

	private static void WithKey(Action<RegistryKey> run)
	{
		var path = @"Software\ClipDiff.Tests\" + Guid.NewGuid().ToString("N");

		try
		{
			using var key = Registry.CurrentUser.CreateSubKey(path);
			run(key);
		}
		finally
		{
			Registry.CurrentUser.DeleteSubKeyTree(path, throwOnMissingSubKey: false);
		}
	}
}
