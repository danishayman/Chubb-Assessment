using Microsoft.OpenApi;

namespace Claims.Api.Infrastructure;

public static class OpenApiSetup
{
    /// <summary>Adds a bearer scheme to the document so Swagger UI shows an Authorize button.</summary>
    public static IServiceCollection AddClaimsOpenApi(this IServiceCollection services) =>
        services.AddOpenApi(o => o.AddDocumentTransformer((doc, _, _) =>
        {
            doc.Info.Title = "Claims API";
            doc.Components ??= new OpenApiComponents();
            doc.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
            doc.Components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                Description = "Paste the token from POST /api/auth/login",
            };
            doc.Security ??= [];
            doc.Security.Add(new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference("Bearer", doc)] = [] });
            return Task.CompletedTask;
        }));
}
