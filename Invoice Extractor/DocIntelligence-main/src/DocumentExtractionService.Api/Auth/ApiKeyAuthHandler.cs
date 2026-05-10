using System.Security.Claims;
using System.Text.Encodings.Web;
using DocumentExtractionService.Core.Configuration;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace DocumentExtractionService.Api.Auth;

/// <summary>
/// API Key authentication scheme.
/// Reads the key from X-Api-Key header (configurable).
/// Falls back to Authorization: Bearer {apiKey}.
///
/// Keys are validated against the configured key store.
/// In production, use Azure Key Vault as the store.
/// </summary>
public class ApiKeyAuthenticationOptions : AuthenticationSchemeOptions
{
    public const string DefaultScheme = "ApiKey";
}

public class ApiKeyAuthenticationHandler : AuthenticationHandler<ApiKeyAuthenticationOptions>
{
    private readonly ApiKeySettings _settings;

    public ApiKeyAuthenticationHandler(
        IOptionsMonitor<ApiKeyAuthenticationOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IOptions<AppSettings> appSettings)
        : base(options, logger, encoder)
    {
        _settings = appSettings.Value.Auth.ApiKey;
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!_settings.Enabled)
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        // Try to get API key from configured header
        string? apiKey = null;

        if (!string.IsNullOrEmpty(_settings.HeaderName) &&
            Request.Headers.TryGetValue(_settings.HeaderName, out var headerValue))
        {
            apiKey = headerValue.FirstOrDefault();
        }

        // Fallback: Authorization: Bearer {apiKey}
        if (string.IsNullOrEmpty(apiKey) &&
            Request.Headers.TryGetValue("Authorization", out var authHeader))
        {
            var bearerValue = authHeader.FirstOrDefault();
            if (bearerValue?.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) == true)
            {
                var potentialKey = bearerValue[7..].Trim();
                // Only treat as API key if it's not a JWT (JWTs have 2 dots)
                if (!potentialKey.Contains('.') || potentialKey.Count(c => c == '.') != 2)
                {
                    apiKey = potentialKey;
                }
            }
        }

        if (string.IsNullOrEmpty(apiKey))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        // Validate the key
        if (!_settings.Keys.TryGetValue(apiKey, out var clientId))
        {
            Logger.LogWarning("Invalid API key attempt from {IP}",
                Context.Connection.RemoteIpAddress);
            return Task.FromResult(AuthenticateResult.Fail("Invalid API key"));
        }

        // Build claims identity
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, clientId),
            new Claim(ClaimTypes.Name, clientId),
            new Claim("client_id", clientId),
            new Claim("auth_method", "api_key")
        };

        var identity = new ClaimsIdentity(claims, ApiKeyAuthenticationOptions.DefaultScheme);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, ApiKeyAuthenticationOptions.DefaultScheme);

        Logger.LogDebug("API key authenticated: {ClientId}", clientId);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
