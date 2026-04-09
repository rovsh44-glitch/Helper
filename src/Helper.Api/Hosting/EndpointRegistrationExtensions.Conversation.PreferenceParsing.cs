#nullable enable

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Helper.Api.Conversation;
using Microsoft.AspNetCore.Http;

namespace Helper.Api.Hosting;

public static partial class EndpointRegistrationExtensions
{
    private sealed record ConversationPreferenceUpdate(
        ConversationPreferenceDto Preferences,
        IReadOnlySet<string> PresentFields);

    private static async Task<ConversationPreferenceUpdate> ReadConversationPreferenceUpdateAsync(HttpRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        using var document = await JsonDocument.ParseAsync(request.Body, cancellationToken: ct);
        var root = document.RootElement;
        var preferences = JsonSerializer.Deserialize<ConversationPreferenceDto>(root.GetRawText())
            ?? new ConversationPreferenceDto(null, null, null);
        var presentFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (root.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in root.EnumerateObject())
            {
                presentFields.Add(property.Name);
            }
        }

        return new ConversationPreferenceUpdate(preferences, presentFields);
    }
}
