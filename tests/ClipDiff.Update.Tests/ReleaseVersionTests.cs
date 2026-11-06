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
	public void UsesSemanticVersionOrdering(string older, string newer)
	{
		Assert.IsLessThan(0, UpdateCheckTests.Parse(older).CompareTo(UpdateCheckTests.Parse(newer)));
		Assert.IsGreaterThan(0, UpdateCheckTests.Parse(newer).CompareTo(UpdateCheckTests.Parse(older)));
	}

	[TestMethod]
	public void IgnoresBuildMetadataAndAcceptsReleaseTagPrefix()
	{
		Assert.AreEqual(0, UpdateCheckTests.Parse("v1.2.3+abc").CompareTo(UpdateCheckTests.Parse("1.2.3+def")));
	}

	[TestMethod]
	[DataRow("1.2")]
	[DataRow("01.2.3")]
	[DataRow("1.2.3-01")]
	[DataRow("1.2.3-preview..1")]
	[DataRow("1.2.3\n")]
	[DataRow("https://example.com")]
	public void RejectsMalformedVersions(string text)
	{
		Assert.IsFalse(ReleaseVersion.TryParse(text, out _));
	}
}
