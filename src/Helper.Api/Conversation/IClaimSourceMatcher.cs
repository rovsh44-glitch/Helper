namespace Helper.Api.Conversation;

public interface IClaimSourceMatcher
{
    ClaimSourceMatch Match(string claim, IReadOnlyList<string> sources, int fallbackSeed = 0);
}

