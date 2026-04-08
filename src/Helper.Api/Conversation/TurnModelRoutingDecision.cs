using Helper.Api.Backend.ModelGateway;

namespace Helper.Api.Conversation;

public sealed record TurnModelRoutingDecision(
    string PreferredModel,
    HelperModelClass ModelClass,
    string RouteKey,
    IReadOnlyList<string> Reasons);
