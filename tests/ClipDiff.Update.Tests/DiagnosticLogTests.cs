using ClipDiff.Windows.Diagnostics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ClipDiff.Update.Tests;

[TestClass]
public sealed class DiagnosticLogTests
{
	[TestMethod]
	public async Task RecordsExceptionMessageStackAndInnerExceptionOnOneLine()
	{
		var directory = Directory.CreateTempSubdirectory();
		var path = Path.Combine(directory.FullName, "ClipDiff.log");
		var log = new DiagnosticLog(path);

		try
		{
			try
			{
				throw new InvalidOperationException("Launch failed\r\nMore detail", new IOException("Access failed"));
			}
			catch (InvalidOperationException exception)
			{
				log.Write(DiagnosticEvent.ExternalDiffFailed, exception: exception);
			}

			await log.CompleteAsync();
			var lines = await File.ReadAllLinesAsync(path);
			Assert.HasCount(1, lines);
			StringAssert.Contains(lines[0], "ExternalDiffFailed exception_code=0x");
			StringAssert.Contains(lines[0], "System.InvalidOperationException: Launch failed\\r\\nMore detail");
			StringAssert.Contains(lines[0], "System.IO.IOException: Access failed");
			StringAssert.Contains(lines[0], nameof(RecordsExceptionMessageStackAndInnerExceptionOnOneLine));
		}
		finally
		{
			await log.CompleteAsync();
			directory.Delete(recursive: true);
		}
	}

	[TestMethod]
	public async Task LargeExceptionStaysWithinRotationLimit()
	{
		var directory = Directory.CreateTempSubdirectory();
		var path = Path.Combine(directory.FullName, "ClipDiff.log");
		var log = new DiagnosticLog(path);

		try
		{
			log.Write(DiagnosticEvent.UnhandledException, exception: new Exception(new string('界', 300000)));
			log.Write(DiagnosticEvent.Stopped);
			await log.CompleteAsync();
			Assert.IsTrue(new FileInfo(path).Length <= 256 * 1024);
			var text = await File.ReadAllTextAsync(path);
			StringAssert.Contains(text, "[truncated]");
			StringAssert.Contains(text, "Stopped");
		}
		finally
		{
			await log.CompleteAsync();
			directory.Delete(recursive: true);
		}
	}

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
