using System.Security.Claims;
using System.Text;
using Claims.Domain;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Claims.Api.Auth;

public class JwtOptions
{
    public string Issuer { get; set; } = "claims-api";
    public string Audience { get; set; } = "claims-web";
    public string Key { get; set; } = "";
    public int ExpiryMinutes { get; set; } = 480;
}

public static class Policies
{
    public const string Staff = "staff";
    public const string Claimant = "claimant";
    public const string Manager = "manager";
}

public static class AuthSetup
{
    public static IServiceCollection AddClaimsAuth(this IServiceCollection services, IConfiguration config)
    {
        var jwt = config.GetSection("Jwt").Get<JwtOptions>() ?? new JwtOptions();
        if (jwt.Key.Length < 32)
            throw new InvalidOperationException("Jwt:Key must be at least 32 characters.");
        services.AddSingleton(jwt);
        services.AddSingleton<TokenService>();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(o =>
            {
                o.MapInboundClaims = false;
                o.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = jwt.Issuer,
                    ValidAudience = jwt.Audience,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Key)),
                    NameClaimType = "name",
                    RoleClaimType = "role",
                };
            });

        services.AddAuthorizationBuilder()
            .AddPolicy(Policies.Staff, p => p.RequireRole(nameof(UserRole.ClaimsOfficer), nameof(UserRole.Manager)))
            .AddPolicy(Policies.Claimant, p => p.RequireRole(nameof(UserRole.Claimant)))
            .AddPolicy(Policies.Manager, p => p.RequireRole(nameof(UserRole.Manager)));

        return services;
    }

    public static Guid UserId(this ClaimsPrincipal user) =>
        Guid.Parse(user.FindFirstValue(JwtRegisteredClaimNames.Sub)!);

    public static bool IsStaff(this ClaimsPrincipal user) =>
        user.IsInRole(nameof(UserRole.ClaimsOfficer)) || user.IsInRole(nameof(UserRole.Manager));

    public static bool IsManager(this ClaimsPrincipal user) => user.IsInRole(nameof(UserRole.Manager));
}

public class TokenService(JwtOptions options, TimeProvider clock)
{
    private readonly JsonWebTokenHandler _handler = new();

    public (string Token, DateTime ExpiresAt) Create(User user)
    {
        var expires = clock.GetUtcNow().UtcDateTime.AddMinutes(options.ExpiryMinutes);
        var token = _handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = options.Issuer,
            Audience = options.Audience,
            Expires = expires,
            Claims = new Dictionary<string, object>
            {
                [JwtRegisteredClaimNames.Sub] = user.Id.ToString(),
                ["name"] = user.Name,
                ["role"] = user.Role.ToString(),
            },
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.Key)), SecurityAlgorithms.HmacSha256),
        });
        return (token, expires);
    }
}
