using Helper.Runtime.Knowledge;

namespace Helper.Runtime.Tests;

public sealed class StructuredParserUtilitiesTests
{
    [Fact]
    public void SelectBestTextExtraction_PrefersCandidateWithHealthySpacing()
    {
        var degraded = string.Concat(Enumerable.Repeat("CompressedAcademicToken", 24));
        var healthy = "Compressed Academic Token with healthy spacing across the extracted text for a page of an academic book.";

        var selected = StructuredParserUtilities.SelectBestTextExtraction(degraded, healthy);

        Assert.Equal(healthy, selected);
    }

    [Fact]
    public void LooksLikeDegradedTextExtraction_FlagsCollapsedText()
    {
        var degraded = string.Concat(Enumerable.Repeat("VeryLongCollapsedTokenWithoutSpacing", 10));

        Assert.True(StructuredParserUtilities.LooksLikeDegradedTextExtraction(degraded));
        Assert.False(StructuredParserUtilities.LooksLikeDegradedTextExtraction("This page has normal spacing and readable extracted prose throughout the paragraph."));
    }
}

