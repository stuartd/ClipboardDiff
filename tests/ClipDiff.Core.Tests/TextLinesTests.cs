using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ClipDiff.Core.Tests;

[TestClass]
public sealed class TextLinesTests
{
    [TestMethod]
    [DataRow("a", new[] { "a" })]
    [DataRow("a\nb", new[] { "a", "b" })]
    [DataRow("a\r\nb", new[] { "a", "b" })]
    [DataRow("a\rb", new[] { "a", "b" })]
    [DataRow("a\n\nb", new[] { "a", "", "b" })]
    [DataRow("a\n", new[] { "a", "" })]
    [DataRow("a\n\n", new[] { "a", "", "" })]
    [DataRow("\n", new[] { "", "" })]
    [DataRow("\tvalue", new[] { "\tvalue" })]
    [DataRow("café 日本語 😀", new[] { "café 日本語 😀" })]
    public void SplitNormalizesLineEndingsAndPreservesEmptyComponents(string text, string[] expected)
    {
        CollectionAssert.AreEqual(expected, TextLines.Split(text));
    }

    [TestMethod]
    public void PreviewFlattensLineEndingsAndTabs()
    {
        Assert.AreEqual("alpha  bravo charlie", TextLines.Preview("\r\nalpha\t\tbravo\rcharlie\n"));
    }

    [TestMethod]
    public void PreviewNamesWhitespaceOnlyText()
    {
        Assert.AreEqual("Blank text", TextLines.Preview(" \t\r\n "));
    }

    [TestMethod]
    public void PreviewLimitsUserVisibleCharactersWithoutSplittingEmoji()
    {
        var text = string.Concat(Enumerable.Repeat("😀", 121));
        var result = TextLines.Preview(text);

        Assert.IsTrue(result.EndsWith("...", StringComparison.Ordinal));
        Assert.AreEqual(string.Concat(Enumerable.Repeat("😀", 120)) + "...", result);
    }
}
