using Claims.Api.Auth;
using Claims.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Claims.Api.Features;

public record LoginRequest(string Email, string Password);
public record UserDto(Guid Id, string Name, string Email, string Role);
public record LoginResponse(string Token, DateTime ExpiresAt, UserDto User);

public static class AuthEndpoints
{
    public static void MapAuth(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/auth").WithTags("Auth");

        g.MapPost("/login", async (LoginRequest req, ClaimsDbContext db, TokenService tokens) =>
        {
            var email = (req.Email ?? "").Trim().ToLowerInvariant();
            var user = await db.Users.SingleOrDefaultAsync(u => u.Email == email);
            if (user is null || !PasswordHasher.Verify(req.Password ?? "", user.PasswordHash))
                return Results.Problem("Invalid email or password.", statusCode: StatusCodes.Status401Unauthorized);

            var (token, expires) = tokens.Create(user);
            return Results.Ok(new LoginResponse(token, expires, new UserDto(user.Id, user.Name, user.Email, user.Role.ToString())));
        });

        g.MapGet("/me", async (System.Security.Claims.ClaimsPrincipal principal, ClaimsDbContext db) =>
        {
            var user = await db.Users.FindAsync(principal.UserId());
            return user is null ? Results.NotFound() : Results.Ok(new UserDto(user.Id, user.Name, user.Email, user.Role.ToString()));
        }).RequireAuthorization();
    }
}
