using System.Buffers.Binary;
using System.Text;
using ClipDiff.Stress;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ClipDiff.Stress.Tests;

[TestClass]
public sealed class StressWorkloadTests
{
	[TestMethod]
	[DataRow(false)]
	[DataRow(true)]
	public void GeneratedTextHasRequestedFileSizeAndDistinctComparisonValues(bool singleLine)
	{
		var previous = StressWorkload.CreateText(256, 'A', singleLine);
		var current = StressWorkload.CreateText(256, 'B', singleLine);
		Assert.AreEqual(256 * 1024, Encoding.UTF8.GetByteCount(previous));
		Assert.AreEqual(previous.Length, current.Length);
		Assert.AreNotEqual(previous, current);
		Assert.AreEqual(singleLine, !previous.Contains('\n'));
		Assert.DoesNotContain('\0', previous);
	}

	[TestMethod]
	[DataRow(1)]
	[DataRow(2)]
	public void FileDropUsesNativeUnicodeHeaderAndCompleteNullTerminatedPaths(int count)
	{
		string[] paths = [@"C:\Stress test\previous ü.txt", @"C:\Stress test\current 中.txt"];
		var item = StressWorkload.Files(paths[..count]);
		Assert.AreEqual(15u, item.Format);
		var pathOffset = BinaryPrimitives.ReadInt32LittleEndian(item.Bytes);
		Assert.AreEqual(20, pathOffset);
		Assert.AreEqual(1, BinaryPrimitives.ReadInt32LittleEndian(item.Bytes.AsSpan(16)));
		CollectionAssert.AreEqual(new byte[12], item.Bytes[4..16]);
		var names = Encoding.Unicode.GetString(item.Bytes.AsSpan(pathOffset));
		Assert.EndsWith("\0\0", names);
		CollectionAssert.AreEqual(paths[..count], names[..^2].Split('\0'));
	}

	[TestMethod]
	public void MixedWorkloadIncludesDuplicatesSingleFilesPairsAndQuotedAndUnquotedPaths()
	{
		const string previousPath = @"C:\Test files\previous.txt";
		const string currentPath = @"C:\Test files\current.txt";
		var workload = StressWorkload.Create(StressMode.Mixed, "old", "new", previousPath, currentPath);
		var text = workload.Where(item => item.Format == StressWorkload.UnicodeText)
			.Select(item => Encoding.Unicode.GetString(item.Bytes).TrimEnd('\0')).ToArray();
		Assert.AreEqual("old", text[0]);
		Assert.AreEqual("old", text[1]);
		CollectionAssert.Contains(text, "new");
		CollectionAssert.Contains(text, previousPath);
		CollectionAssert.Contains(text, $"\"{previousPath}\"\r\n\"{currentPath}\"");
		CollectionAssert.Contains(text, $"{currentPath}\n{previousPath}\n");
		var fileCounts = workload.Where(item => item.Format == StressWorkload.FileDrop)
			.Select(item => Encoding.Unicode.GetString(item.Bytes.AsSpan(20))[..^2].Split('\0').Length)
			.ToArray();
		CollectionAssert.AreEquivalent(new[] { 1, 1, 2, 2 }, fileCounts);
	}

	[TestMethod]
	[DataRow("--size-kib", "16385")]
	[DataRow("--size-kib", "0")]
	[DataRow("--copies", "-1")]
	[DataRow("--delay-ms", "0")]
	[DataRow("--copies", "not-a-number")]
	[DataRow("--mode", "100")]
	[DataRow("--unknown", "1")]
	public void RejectsInvalidOrUnboundedWorkloads(string option, string value)
	{
		Assert.ThrowsExactly<ArgumentException>(() => StressOptions.Parse([option, value]));
	}

	[TestMethod]
	public void OptionsAcceptLargestSupportedFileAndRejectMissingValues()
	{
		var options = StressOptions.Parse(["--mode", "files", "--size-kib", "16384", "--single-line", "--pid", "123"]);
		Assert.AreEqual(StressMode.Files, options.Mode);
		Assert.AreEqual(16384, options.SizeKiB);
		Assert.IsTrue(options.SingleLine);
		Assert.AreEqual(123, options.ProcessId);
		Assert.ThrowsExactly<ArgumentException>(() => StressOptions.Parse(["--copies"]));
	}
}
