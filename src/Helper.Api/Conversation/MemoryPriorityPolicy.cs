namespace Helper.Api.Conversation;

public interface IMemoryPriorityPolicy
{
    int Score(MemoryScope scope, bool isPersonal, string content);
}

public sealed class MemoryPriorityPolicy : IMemoryPriorityPolicy
{
    public int Score(MemoryScope scope, bool isPersonal, string content)
    {
        var score = scope switch
        {
            MemoryScope.User => 90,
            MemoryScope.Project => 80,
            MemoryScope.Task => 65,
            MemoryScope.Session => 45,
            _ => 20
        };

        if (isPersonal)
        {
            score += 5;
        }

        if (!string.IsNullOrWhiteSpace(content) && content.Length <= 120)
        {
            score += 5;
        }

        return Math.Clamp(score, 0, 100);
    }
}
