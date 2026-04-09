#nullable enable

using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace Helper.Api.Hosting;

public static partial class EndpointRegistrationExtensions
{
    private static Task<ConversationPreferencePayloadReader.Update> ReadConversationPreferenceUpdateAsync(HttpRequest request, CancellationToken ct)
        => ConversationPreferencePayloadReader.ReadAsync(request, ct);
}
