namespace Helper.Runtime.WebResearch.Fetching;

public interface IRedirectGuard
{
    Task<WebFetchSecurityDecision> EvaluateAsync(
        Uri origin,
        Uri redirectTarget,
        int redirectHop,
        bool allowTrustedLoopback = false,
        CancellationToken ct = default);
}

public sealed class RedirectGuard : IRedirectGuard
{
    private readonly IWebFetchSecurityPolicy _securityPolicy;
    private readonly int _maxRedirects;

    public RedirectGuard(IWebFetchSecurityPolicy? securityPolicy = null)
    {
        _securityPolicy = securityPolicy ?? new WebFetchSecurityPolicy();
        _maxRedirects = Providers.WebSearchProviderSettings.ReadMaxRedirects();
    }

    public async Task<WebFetchSecurityDecision> EvaluateAsync(
        Uri origin,
        Uri redirectTarget,
        int redirectHop,
        bool allowTrustedLoopback = false,
        CancellationToken ct = default)
    {
        if (redirectHop > _maxRedirects)
        {
            return new WebFetchSecurityDecision(
                false,
                "redirect_limit_exceeded",
                new[]
                {
                    $"web_fetch.redirect_blocked reason=redirect_limit_exceeded from={origin} to={redirectTarget} hop={redirectHop} max={_maxRedirects}"
                });
        }

        if (origin.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
            redirectTarget.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase))
        {
            return new WebFetchSecurityDecision(
                false,
                "redirect_scheme_downgrade",
                new[]
                {
                    $"web_fetch.redirect_blocked reason=redirect_scheme_downgrade from={origin} to={redirectTarget} hop={redirectHop}"
                });
        }

        var targetDecision = await _securityPolicy.EvaluateAsync(
            redirectTarget,
            WebFetchTargetKind.Redirect,
            allowTrustedLoopback,
            ct).ConfigureAwait(false);

        if (!targetDecision.Allowed)
        {
            return new WebFetchSecurityDecision(
                false,
                targetDecision.ReasonCode,
                targetDecision.Trace
                    .Concat(new[]
                    {
                        $"web_fetch.redirect_blocked from={origin} to={redirectTarget} hop={redirectHop}"
                    })
                    .ToArray());
        }

        return new WebFetchSecurityDecision(
            true,
            "redirect_allowed",
            targetDecision.Trace
                .Concat(new[]
                {
                    $"web_fetch.redirect_allowed from={origin} to={redirectTarget} hop={redirectHop}"
                })
                .ToArray());
    }
}

