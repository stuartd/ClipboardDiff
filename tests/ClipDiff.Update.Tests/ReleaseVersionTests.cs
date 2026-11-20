using ClipDiff.Windows.Updates;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ClipDiff.Update.Tests;

[TestClass]
public sealed class ReleaseVersionTests
{
	[TestMethod]
	[DataRow("1.2.0", "1.10.0")]
	[DataRow("1.0.0-preview.2", "1.0.0-preview.10")]
	[DataRow("1.0.0-preview.10", "1.0.0")]
	[DataRow("1.0.0-1", "1.0.0-alpha")]
	[DataRow("1.0.0-alpha", "1.0.0-alpha.1")]
	[DataRow("1.0.0", "2.0.0-preview.1")]
	[DataRow("1.0.0", "999999999999999999999999.0.0")]
	[DataRow("0.9.1120", "v0.9.1120.2")]
	[DataRow("0.9.1120.2", "0.9.1120.10")]
	[DataRow("0.9.1120.10", "0.9.1121")]
	[DataRow("0.9.1120", "0.9.1120.2-preview.1")]
	[DataRow("0.9.1120.2-preview.1", "0.9.1120.2")]
	public void OrdersReleaseVersions(string older, string newer)
	{
		Assert.IsLessThan(0, UpdateCheckTests.Parse(older).CompareTo(UpdateCheckTests.Parse(newer)));
		Assert.IsGreaterThan(0, UpdateCheckTests.Parse(newer).CompareTo(UpdateCheckTests.Parse(older)));
	}

	[TestMethod]
	[DataRow("v1.2.3+abc", "1.2.3+def")]
	[DataRow("v1.2.3.4+abc", "1.2.3.4+def")]
	[DataRow("1.2.3", "1.2.3.0")]
	public void IgnoresBuildMetadataAndTreatsMissingRevisionAsZero(string left, string right)
	{
		Assert.AreEqual(0, UpdateCheckTests.Parse(left).CompareTo(UpdateCheckTests.Parse(right)));
	}

	[TestMethod]
	[DataRow("1.2")]
	[DataRow("01.2.3")]
	[DataRow("1.2.3.04")]
	[DataRow("1.2.3.4.5")]
	[DataRow("1.2.3-01")]
	[DataRow("1.2.3-preview..1")]
	[DataRow("1.2.3\n")]
	[DataRow("https://example.com")]
	public void RejectsMalformedVersions(string text)
	{
		Assert.IsFalse(ReleaseVersion.TryParse(text, out _));
	}
}
