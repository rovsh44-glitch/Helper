using System.Globalization;

namespace Helper.Runtime.Generation;

public sealed class TemplatePromotionFeatureProfileService : ITemplatePromotionFeatureProfileService
{
    public TemplatePromotionFeatureProfile GetCurrent()
    {
        var profileName = ResolveProfileName();
        var emergencyDisabled = ReadFlag("HELPER_TEMPLATE_PROMOTION_EMERGENCY_DISABLE", false);
        var runtimeDefault = profileName.Equals("prod", StringComparison.OrdinalIgnoreCase) ||
                             profileName.Equals("production", StringComparison.OrdinalIgnoreCase);

        return new TemplatePromotionFeatureProfile(
            RuntimePromotionEnabled: !emergencyDisabled && ReadFlag("HELPER_FF_TEMPLATE_RUNTIME_PROMOTION_V1", runtimeDefault),
            AutoActivateEnabled: ReadFlag("HELPER_TEMPLATE_PROMOTION_AUTO_ACTIVATE", true),
            PostActivationFullRecertifyEnabled: ReadFlag("HELPER_TEMPLATE_PROMOTION_POST_ACTIVATION_FULL_RECERTIFY", false),
            FormatMode: ReadFormatMode(),
            RouterV2Enabled: ReadFlag("HELPER_FF_TEMPLATE_ROUTER_V2", true),
            RouterMinConfidence: ReadDouble("HELPER_TEMPLATE_ROUTER_MIN_CONFIDENCE", 0.34, 0.05, 0.95));
    }

    public static CompileGateFormatMode ReadFormatMode()
    {
        var modeRaw = Environment.GetEnvironmentVariable("HELPER_TEMPLATE_PROMOTION_FORMAT_MODE");
        if (!string.IsNullOrWhiteSpace(modeRaw))
        {
            if (modeRaw.Equals("strict", StringComparison.OrdinalIgnoreCase))
            {
                return CompileGateFormatMode.Strict;
            }

            if (modeRaw.Equals("advisory", StringComparison.OrdinalIgnoreCase))
            {
                return CompileGateFormatMode.Advisory;
            }

            if (modeRaw.Equals("off", StringComparison.OrdinalIgnoreCase))
            {
                return CompileGateFormatMode.Off;
            }
        }

        var legacy = Environment.GetEnvironmentVariable("HELPER_REQUIRE_GENERATION_FORMAT");
        if (bool.TryParse(legacy, out var requireLegacy))
        {
            return requireLegacy ? CompileGateFormatMode.Strict : CompileGateFormatMode.Off;
        }

        // Promotion should not fail by default on formatting-only drift.
        return CompileGateFormatMode.Advisory;
    }

    private static bool ReadFlag(string envName, bool fallback)
    {
        var raw = Environment.GetEnvironmentVariable(envName);
        return bool.TryParse(raw, out var parsed) ? parsed : fallback;
    }

    private static string ResolveProfileName()
    {
        var explicitProfile = Environment.GetEnvironmentVariable("HELPER_GOLDEN_PROMOTION_PROFILE");
        if (!string.IsNullOrWhiteSpace(explicitProfile))
        {
            return explicitProfile.Trim();
        }

        var aspNetProfile = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
        if (!string.IsNullOrWhiteSpace(aspNetProfile))
        {
            return aspNetProfile.Trim();
        }

        return "dev";
    }

    private static double ReadDouble(string envName, double fallback, double min, double max)
    {
        var raw = Environment.GetEnvironmentVariable(envName);
        if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
        {
            value = fallback;
        }

        return Math.Clamp(value, min, max);
    }
}

