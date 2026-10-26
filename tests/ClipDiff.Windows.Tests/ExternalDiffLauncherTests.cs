using ClipDiff.Windows.ExternalDiff;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ClipDiff.Windows.Tests;

[TestClass]
public sealed class ExternalDiffLauncherTests
{
	[TestMethod]
	public async Task RapidlyExitingViewersReleaseAllWorkspacesAndOwnershipLeases()
	{
		var root = CreateTemporaryDirectory();

		try
		{
			using var launcher = new ExternalDiffLauncher(new ExternalDiffWorkspace(root));
			var choice = ExitingViewer();

			for (var index = 0; index < 32; index++)
			{
				Assert.IsTrue(launcher.TryLaunch(choice, Entry("synthetic older"), Entry("synthetic newer")));
			}

			using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));

			while (Directory.EnumerateFileSystemEntries(root).Any())
			{
				await Task.Delay(25, timeout.Token);
			}

			Assert.IsEmpty(Directory.GetFileSystemEntries(root));
		}
		finally
		{
			ExternalDiffWorkspace.TryDelete(root);
		}
	}

	[TestMethod]
	public void FailedProcessStartReleasesItsWorkspaceAndLease()
	{
		var root = CreateTemporaryDirectory();

		try
		{
			var executable = Path.Combine(root, "not-an-executable.exe");
			File.WriteAllText(executable, "synthetic invalid executable");
			using var launcher = new ExternalDiffLauncher(new ExternalDiffWorkspace(Path.Combine(root, "workspaces")));
			var choice = ExitingViewer() with { ExecutablePath = executable };

			Assert.IsFalse(launcher.TryLaunch(choice, Entry("synthetic older"), Entry("synthetic newer")));
			Assert.IsEmpty(Directory.GetFileSystemEntries(Path.Combine(root, "workspaces")));
		}
		finally
		{
			ExternalDiffWorkspace.TryDelete(root);
		}
	}

	[TestMethod]
	public void DisposedLauncherDoesNotStartViewerOrCreateFiles()
	{
		var root = CreateTemporaryDirectory();

		try
		{
			using var launcher = new ExternalDiffLauncher(new ExternalDiffWorkspace(root));
			launcher.Dispose();
			launcher.Dispose();

			Assert.IsFalse(launcher.TryLaunch(ExitingViewer(), Entry("synthetic older"), Entry("synthetic newer")));
			Assert.IsEmpty(Directory.GetFileSystemEntries(root));
		}
		finally
		{
			ExternalDiffWorkspace.TryDelete(root);
		}
	}

	private static ExternalDiffToolChoice ExitingViewer()
	{
		// The test child ignores the synthetic comparison paths and exits immediately,
		// exercising the race between Process.Exited and listener registration.
		var executable = OperatingSystem.IsWindows()
			? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe")
			: "/bin/true";
		var tool = new ExternalDiffTool("test", "Synthetic exiting viewer", [],
			(_, _, _, _) => OperatingSystem.IsWindows() ? ["/d", "/c", "exit", "0"] : []);
		return new(tool, executable);
	}

	private static ClipboardEntry Entry(string text) => new(Guid.NewGuid(), text, DateTimeOffset.UtcNow);

	private static string CreateTemporaryDirectory()
	{
		var root = Path.Combine(Path.GetTempPath(), "ClipDiff.LauncherTests", Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(root);
		return root;
	}
}
