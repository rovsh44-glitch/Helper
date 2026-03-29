namespace Helper.Runtime.Swarm;

internal sealed record TumenRuntimeSettings(
    TimeSpan CriticTimeout,
    bool SmokeProfile,
    int SmokeMaxFiles,
    int SmokeMaxMethodsPerFile,
    int SmokeCriticAttempts)
{
    public static TumenRuntimeSettings Load()
    {
        var smokeProfile = ReadFlag("HELPER_SMOKE_PROFILE", false);
        return new TumenRuntimeSettings(
            ReadTimeout("HELPER_CRITIC_TIMEOUT_SEC", 30, 5, 180),
            smokeProfile,
            ReadInt("HELPER_SMOKE_MAX_FILES", smokeProfile ? 4 : 16, 1, 32),
            ReadInt("HELPER_SMOKE_MAX_METHODS_PER_FILE", smokeProfile ? 2 : 7, 1, 12),
            ReadIntFromAny(
                new[] { "HELPER_MAX_CRITIC_ATTEMPTS", "HELPER_SMOKE_MAX_CRITIC_ATTEMPTS" },
                smokeProfile ? 0 : 1,
                0,
                5));
    }

    private static TimeSpan ReadTimeout(string envName, int fallbackSeconds, int minSeconds, int maxSeconds)
    {
        var raw = Environment.GetEnvironmentVariable(envName);
        if (!int.TryParse(raw, out var seconds))
        {
            seconds = fallbackSeconds;
        }

        return TimeSpan.FromSeconds(Math.Clamp(seconds, minSeconds, maxSeconds));
    }

    private static bool ReadFlag(string envName, bool fallback)
    {
        var raw = Environment.GetEnvironmentVariable(envName);
        return bool.TryParse(raw, out var parsed) ? parsed : fallback;
    }

    private static int ReadInt(string envName, int fallback, int min, int max)
    {
        var raw = Environment.GetEnvironmentVariable(envName);
        if (!int.TryParse(raw, out var value))
        {
            value = fallback;
        }

        return Math.Clamp(value, min, max);
    }

    private static int ReadIntFromAny(IEnumerable<string> envNames, int fallback, int min, int max)
    {
        foreach (var env in envNames)
        {
            var raw = Environment.GetEnvironmentVariable(env);
            if (int.TryParse(raw, out var value))
            {
                return Math.Clamp(value, min, max);
            }
        }

        return Math.Clamp(fallback, min, max);
    }
}

