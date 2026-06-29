using System.Security.Claims;
using System.Text.Encodings.Web;
using Amazon.Lambda.APIGatewayEvents;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Bolao.Functions.Auth;

public class GatewayAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IWebHostEnvironment environment)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (environment.IsEnvironment("E2E"))
        {
            var token = Request.Headers.Authorization.ToString().Replace("Bearer ", "", StringComparison.Ordinal);
            if (token is not ("e2e-user" or "e2e-admin"))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var claims = new List<Claim>
            {
                new("sub", token == "e2e-admin" ? "admin-1" : "user-1")
            };
            if (token == "e2e-admin") claims.Add(new Claim("is_admin", "true"));
            return Task.FromResult(Success(claims));
        }

        var gatewayRequest = Context.Items.Values
            .OfType<APIGatewayHttpApiV2ProxyRequest>()
            .FirstOrDefault();
        var gatewayClaims = gatewayRequest?.RequestContext?.Authorizer?.Jwt?.Claims;
        if (gatewayClaims is null || !gatewayClaims.TryGetValue("sub", out var subject))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var claimsFromGateway = gatewayClaims
            .Where(claim => claim.Key != "cognito:groups")
            .Select(claim => new Claim(claim.Key, claim.Value))
            .ToList();
        claimsFromGateway.Add(new Claim("sub", subject));

        return Task.FromResult(Success(claimsFromGateway));
    }

    private AuthenticateResult Success(IEnumerable<Claim> claims)
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name));
        return AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name));
    }
}
