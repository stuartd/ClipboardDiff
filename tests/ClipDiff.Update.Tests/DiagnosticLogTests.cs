using ClipDiff.Windows.Diagnostics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ClipDiff.Update.Tests;

[TestClass]
public sealed class DiagnosticLogTests
{
	[TestMethod]
	public async Task RollsOverToOneBackupAndContinuesWriting()
	{
		var directory = Directory.CreateTempSubdirectory();
		var path = Path.Combine(directory.FullName, "ClipDiff.log");
		var backupPath = path + ".previous";
		var fullLog = new string('x', 256 * 1024);

		try
		{
			await File.WriteAllTextAsync(path, fullLog);
			await File.WriteAllTextAsync(backupPath, "old backup");

			var log = new DiagnosticLog(path);
			log.Write(DiagnosticEvent.Starting);
			log.Write(DiagnosticEvent.Stopped);
			await log.CompleteAsync();

			Assert.AreEqual(fullLog, await File.ReadAllTextAsync(backupPath));
			var lines = await File.ReadAllLinesAsync(path);
			Assert.HasCount(2, lines);
			StringAssert.EndsWith(lines[0], "Starting");
			StringAssert.EndsWith(lines[1], "Stopped");
		}
		finally
		{
			directory.Delete(recursive: true);
		}
	}
}
