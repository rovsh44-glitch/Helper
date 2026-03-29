namespace Helper.Api.Conversation;

public interface IClaimExtractionService
{
    IReadOnlyList<ExtractedClaim> Extract(string text);
}

