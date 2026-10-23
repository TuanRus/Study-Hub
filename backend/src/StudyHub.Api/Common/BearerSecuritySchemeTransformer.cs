using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace StudyHub.Api.Common;

// VI: Thêm ô "Authorize" (Bearer JWT) vào tài liệu OpenAPI / Scalar.
// EN: Adds the "Authorize" (Bearer JWT) box to the OpenAPI / Scalar docs.
public sealed class BearerSecuritySchemeTransformer : IOpenApiDocumentTransformer
{
    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken ct)
    {
        document.Info.Title = "Study Hub API";
        document.Info.Description = "Backend for Study Hub: accounts, tasks & deadlines, Pomodoro stats and music playlists.";

        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            Description = "Paste the accessToken from /api/auth/login."
        };
        document.Security = [new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference("Bearer", document)] = [] }];
        return Task.CompletedTask;
    }
}
