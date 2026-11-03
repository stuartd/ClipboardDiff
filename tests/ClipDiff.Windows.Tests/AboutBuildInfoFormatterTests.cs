using ClipDiff.Windows.Views;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ClipDiff.Windows.Tests;

[TestClass]
public sealed class AboutBuildInfoFormatterTests
{
	[TestMethod]
	[DataRow("1.2.3+753aab75f36e959cf8dfa6d72a2175110c9ed608", "1.2.3")]
	[DataRow("2.3.4-preview.1+local.753aab75f36e959cf8dfa6d72a2175110c9ed608", "2.3.4-preview.1")]
	[DataRow("1.2.3", "1.2.3")]
	[DataRow("1.2.3-preview.1", "1.2.3-preview.1")]
	[DataRow(null, "Unknown")]
	[DataRow("", "Unknown")]
	public void ReleaseBuildShowsOnlyVersion(string? informationalVersion, string expectedVersion)
	{
		var result = AboutBuildInfoFormatter.Format(informationalVersion, "2026-10-19T10:43:44.0000000Z", true);

		Assert.AreEqual($"Version: {expectedVersion}", result);
	}

	[TestMethod]
	[DataRow("1.0+753aab75f36e959cf8dfa6d72a2175110c9ed608")]
	[DataRow("2.3.4-preview.1+local.753aab75f36e959cf8dfa6d72a2175110c9ed608")]
	public void DevelopmentBuildShowsCommitAndBuildDate(string informationalVersion)
	{
		var result = AboutBuildInfoFormatter.Format(informationalVersion, "2026-10-19T10:43:44.0000000Z", false);

		Assert.AreEqual("Last commit: 753aab7\nBuild date: 2026-10-19 10:43 UTC", result);
	}

	[TestMethod]
	[DataRow(null)]
	[DataRow("")]
	[DataRow("1.0")]
	[DataRow("1.0+preview")]
	public void MissingCommitIsExplicit(string? informationalVersion)
	{
		var result = AboutBuildInfoFormatter.Format(informationalVersion, "2026-10-19T10:43:44Z", false);

		Assert.AreEqual("Last commit: Unknown\nBuild date: 2026-10-19 10:43 UTC", result);
	}

	[TestMethod]
	[DataRow(null)]
	[DataRow("")]
	[DataRow("invalid")]
	public void MissingOrInvalidBuildDateIsExplicit(string? buildDate)
	{
		var result = AboutBuildInfoFormatter.Format("1.0+753aab75f36e959cf8dfa6d72a2175110c9ed608", buildDate, false);

		Assert.AreEqual("Last commit: 753aab7\nBuild date: Unknown", result);
	}

	[TestMethod]
	public void NormalizesBuildDateToUtc()
	{
		var result = AboutBuildInfoFormatter.Format(null, "2026-10-19T11:43:44+01:00", false);

		Assert.AreEqual("Last commit: Unknown\nBuild date: 2026-10-19 10:43 UTC", result);
	}

	[TestMethod]
	public void BuildContainsTimestampMetadata()
	{
		var metadata = typeof(AboutBuildInfoFormatter).Assembly
			.GetCustomAttributes(typeof(System.Reflection.AssemblyMetadataAttribute), false)
			.Cast<System.Reflection.AssemblyMetadataAttribute>()
			.Single(attribute => attribute.Key == "BuildDateUtc");

		Assert.IsTrue(DateTimeOffset.TryParse(metadata.Value, System.Globalization.CultureInfo.InvariantCulture,
			System.Globalization.DateTimeStyles.AssumeUniversal, out _));
	}
}
