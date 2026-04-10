#nullable enable

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Helper.Api.Conversation;
using Microsoft.AspNetCore.Http;

namespace Helper.Api.Hosting;

internal static class ConversationPreferencePayloadReader
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    internal sealed record Update(
        ConversationPreferenceDto Preferences,
        IReadOnlySet<string> PresentFields);

    public static async Task<Update> ReadAsync(HttpRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        using var document = await JsonDocument.ParseAsync(request.Body, cancellationToken: ct);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("Preferences payload must be a JSON object.");
        }

        var preferences = JsonSerializer.Deserialize<ConversationPreferenceDto>(root.GetRawText(), JsonOptions)
            ?? new ConversationPreferenceDto(null, null, null);
        var presentFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var property in root.EnumerateObject())
        {
            presentFields.Add(property.Name);
        }

        return new Update(preferences, presentFields);
    }
}
