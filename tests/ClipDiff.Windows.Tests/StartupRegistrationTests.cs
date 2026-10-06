using ClipDiff.Windows.Startup;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Win32;

namespace ClipDiff.Windows.Tests;

[TestClass]
public sealed class StartupRegistrationTests
{
	private readonly string registryPath = @"Software\ClipDiff.Tests\" + Guid.NewGuid().ToString("N");

	[TestCleanup]
	public void Cleanup() => Registry.CurrentUser.DeleteSubKeyTree(registryPath, throwOnMissingSubKey: false);

	[TestMethod]
	public void EnableQuotesExecutableAndDisablePreservesOtherValues()
	{
		var registration = new StartupRegistration(registryPath, @"C:\Tools with spaces\日本語\ClipDiff.exe");
		Assert.IsTrue(registration.TryRead(out var registered));
		Assert.IsFalse(registered);
		Assert.IsTrue(registration.TrySetEnabled(false));
		Assert.IsTrue(registration.TrySetEnabled(true));
		Assert.IsTrue(registration.TryRead(out registered));
		Assert.IsTrue(registered);

		using var key = Registry.CurrentUser.OpenSubKey(registryPath, writable: true)!;
		Assert.AreEqual("\"C:\\Tools with spaces\\日本語\\ClipDiff.exe\"", key.GetValue("ClipDiff"));
		key.SetValue("AnotherApp", "untouched");
		Assert.IsTrue(registration.TrySetEnabled(false));
		Assert.IsTrue(registration.TrySetEnabled(false));
		Assert.IsTrue(registration.TryRead(out registered));
		Assert.IsFalse(registered);
		Assert.AreEqual("untouched", key.GetValue("AnotherApp"));
	}

	[TestMethod]
	public void EnablingFromNewLocationReplacesOnlyClipDiffCommand()
	{
		Assert.IsTrue(new StartupRegistration(registryPath, @"C:\Old\ClipDiff.exe").TrySetEnabled(true));
		var registration = new StartupRegistration(registryPath, @"C:\New version\ClipDiff.exe");
		Assert.IsTrue(registration.TryRead(out var registered));
		Assert.IsTrue(registered);
		Assert.IsTrue(registration.TrySetEnabled(true));

		using var key = Registry.CurrentUser.OpenSubKey(registryPath)!;
		Assert.AreEqual("\"C:\\New version\\ClipDiff.exe\"", key.GetValue("ClipDiff"));
	}

	[TestMethod]
	public void ReadReflectsExternalRemoval()
	{
		var registration = new StartupRegistration(registryPath, @"C:\Tools\ClipDiff.exe");
		Assert.IsTrue(registration.TrySetEnabled(true));

		using var key = Registry.CurrentUser.OpenSubKey(registryPath, writable: true)!;
		key.DeleteValue("ClipDiff");
		Assert.IsTrue(registration.TryRead(out var registered));
		Assert.IsFalse(registered);
	}

	[TestMethod]
	public void InvalidExecutableDoesNotReplaceExistingRegistration()
	{
		Assert.IsTrue(new StartupRegistration(registryPath, @"C:\Tools\ClipDiff.exe").TrySetEnabled(true));

		foreach (var path in new[] { "", "ClipDiff.exe", "C:\\bad\"path.exe", "C:\\" + new string('x', 260) + ".exe" })
		{
			Assert.IsFalse(new StartupRegistration(registryPath, path).TrySetEnabled(true));
		}

		using var key = Registry.CurrentUser.OpenSubKey(registryPath)!;
		Assert.AreEqual("\"C:\\Tools\\ClipDiff.exe\"", key.GetValue("ClipDiff"));
	}
}
