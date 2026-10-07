using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ClipDiff.Windows.Tests;

[TestClass]
public sealed class ApplicationCommandLineTests
{
	[TestMethod]
	public void ExecutableCommandQuotesSpacesAndUnicode()
	{
		var command = ApplicationCommandLine.Build(@"C:\Tools with spaces\日本語\ClipDiff.exe", null);
		Assert.AreEqual("\"C:\\Tools with spaces\\日本語\\ClipDiff.exe\"", command);
	}

	[TestMethod]
	[DataRow(@"C:\Program Files\dotnet\dotnet.exe")]
	[DataRow(@"C:\Program Files\dotnet\DOTNET.EXE")]
	[DataRow(@"/usr/local/share/dotnet/dotnet")]
	public void HostedCommandIncludesQuotedEntryAssembly(string processPath)
	{
		var command = ApplicationCommandLine.Build(processPath, @"C:\ClipDiff build\日本語\ClipDiff.dll");
		Assert.AreEqual($"\"{processPath}\" \"C:\\ClipDiff build\\日本語\\ClipDiff.dll\"", command);
	}

	[TestMethod]
	[DataRow(null)]
	[DataRow("")]
	[DataRow("ClipDiff.dll")]
	[DataRow("C:\\bad\"path\\ClipDiff.dll")]
	public void HostedCommandRejectsMissingOrInvalidEntryAssembly(string? entryAssemblyPath)
	{
		Assert.IsFalse(ApplicationCommandLine.TryBuild(@"C:\Program Files\dotnet\dotnet.exe", entryAssemblyPath, out _));
	}

	[TestMethod]
	[DataRow(null)]
	[DataRow("")]
	[DataRow("ClipDiff.exe")]
	[DataRow("C:\\bad\"path.exe")]
	[DataRow("C:\\bad\0path.exe")]
	public void CommandRejectsInvalidExecutable(string? processPath)
	{
		Assert.IsFalse(ApplicationCommandLine.TryBuild(processPath, null, out _));
	}

	[TestMethod]
	public void DirectExecutableDoesNotIncludeAnEntryAssemblyArgument()
	{
		var command = ApplicationCommandLine.Build(@"C:\ClipDiff\ClipDiff.exe", @"C:\ClipDiff\ClipDiff.dll");
		Assert.AreEqual("\"C:\\ClipDiff\\ClipDiff.exe\"", command);
	}
}
