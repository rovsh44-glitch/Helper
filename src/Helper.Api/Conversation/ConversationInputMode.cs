namespace Helper.Api.Conversation;

internal static class ConversationInputMode
{
    public const string Text = "text";
    public const string Voice = "voice";

    public static string Normalize(string? value)
    {
        return string.Equals(value, Voice, StringComparison.OrdinalIgnoreCase)
            ? Voice
            : Text;
    }

    public static bool IsVoice(string? value)
    {
        return string.Equals(Normalize(value), Voice, StringComparison.OrdinalIgnoreCase);
    }
}

