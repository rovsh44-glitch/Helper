using Helper.Runtime.WebResearch.Normalization;

namespace Helper.Runtime.Tests;

public sealed class DocumentSourceNormalizationPolicyTests
{
    [Fact]
    public void Normalize_ConvertsGitHubBlobPdf_ToRawContentUrl()
    {
        var policy = new DocumentSourceNormalizationPolicy();

        var result = policy.Normalize(new Uri("https://github.com/MoonshotAI/Attention-Residuals/blob/master/Attention_Residuals.pdf"));

        Assert.True(result.WasNormalized);
        Assert.Equal("github_blob_document", result.SourceKind);
        Assert.Equal("https://raw.githubusercontent.com/MoonshotAI/Attention-Residuals/master/Attention_Residuals.pdf", result.EffectiveUri.AbsoluteUri);
    }

    [Fact]
    public void Normalize_ConvertsGitLabBlobPdf_ToRawContentUrl()
    {
        var policy = new DocumentSourceNormalizationPolicy();

        var result = policy.Normalize(new Uri("https://gitlab.com/example/research/-/blob/main/paper.pdf"));

        Assert.True(result.WasNormalized);
        Assert.Equal("gitlab_blob_document", result.SourceKind);
        Assert.Equal("https://gitlab.com/example/research/-/raw/main/paper.pdf", result.EffectiveUri.AbsoluteUri);
    }

    [Fact]
    public void Normalize_ConvertsArxivAbstract_ToPdfUrl()
    {
        var policy = new DocumentSourceNormalizationPolicy();

        var result = policy.Normalize(new Uri("https://arxiv.org/abs/2603.15031"));

        Assert.True(result.WasNormalized);
        Assert.Equal("arxiv_pdf", result.SourceKind);
        Assert.Equal("https://arxiv.org/pdf/2603.15031.pdf", result.EffectiveUri.AbsoluteUri);
    }

    [Fact]
    public void Normalize_LeavesDirectPdfUrlUntouched()
    {
        var policy = new DocumentSourceNormalizationPolicy();

        var result = policy.Normalize(new Uri("https://example.org/papers/attention-residuals.pdf"));

        Assert.False(result.WasNormalized);
        Assert.Equal("direct_document", result.SourceKind);
        Assert.Equal("https://example.org/papers/attention-residuals.pdf", result.EffectiveUri.AbsoluteUri);
    }
}

