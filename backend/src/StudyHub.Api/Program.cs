using System.Text;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Scalar.AspNetCore;
using StudyHub.Api.Auth;
using StudyHub.Api.Common;
using StudyHub.Api.Data;
using StudyHub.Api.Entities;
using StudyHub.Api.Services;

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;

// ---------- Database ----------
builder.Services.AddDbContext<AppDbContext>(o =>
    o.UseSqlite(config.GetConnectionString("Default") ?? "Data Source=studyhub.db"));

// ---------- Auth (JWT) ----------
builder.Services.AddOptions<JwtOptions>()
    .Bind(config.GetSection(JwtOptions.Section))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IOptions<JwtOptions>>((bearer, jwtOptions) =>
    {
        var jwt = jwtOptions.Value;
        bearer.MapInboundClaims = false;
        bearer.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = jwt.Issuer,
            ValidAudience = jwt.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Key)),
            NameClaimType = "unique_name",
            RoleClaimType = "role",
            ClockSkew = TimeSpan.FromSeconds(30)
        };
        // VI: Mỗi request kiểm tra lại DB: tài khoản bị khóa/xóa bị chặn ngay, quyền lấy theo DB chứ không theo token cũ.
        // EN: Re-check the DB on each request: locked/deleted accounts are blocked at once, and the role comes from the DB, not an old token.
        bearer.Events = new JwtBearerEvents
        {
            OnTokenValidated = async ctx =>
            {
                var db = ctx.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
                var id = ctx.Principal?.FindFirst("sub")?.Value;
                var user = Guid.TryParse(id, out var userId)
                    ? await db.Users.AsNoTracking().Where(u => u.Id == userId)
                        .Select(u => new { u.IsDisabled, u.Role }).FirstOrDefaultAsync()
                    : null;
                if (user is null || user.IsDisabled)
                {
                    ctx.Fail("Account is disabled or no longer exists.");
                    return;
                }
                var identity = (System.Security.Claims.ClaimsIdentity)ctx.Principal!.Identity!;
                foreach (var claim in identity.FindAll("role").ToList()) identity.RemoveClaim(claim);
                identity.AddClaim(new System.Security.Claims.Claim("role", user.Role.ToString()));
            }
        };
    });
builder.Services.AddAuthorizationBuilder()
    .AddPolicy(Policies.Admin, p => p.RequireRole(nameof(UserRole.Admin), nameof(UserRole.SuperAdmin)))
    .AddPolicy(Policies.SuperAdmin, p => p.RequireRole(nameof(UserRole.SuperAdmin)));

builder.Services.AddScoped<IPasswordHasher<User>, PasswordHasher<User>>();
builder.Services.AddSingleton<TokenService>();

// ---------- App services ----------
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<TaskService>();
builder.Services.AddScoped<PomodoroService>();
builder.Services.AddScoped<StatsService>();
builder.Services.AddScoped<PlaylistService>();
builder.Services.AddScoped<ImportService>();
builder.Services.AddScoped<ProfileService>();
builder.Services.AddScoped<AdminService>();
builder.Services.AddSingleton<AdminBootstrap>();

builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddHealthChecks();

// ---------- CORS: allow the static frontend (Live Server, http.server...) ----------
var origins = config.GetSection("Cors:Origins").Get<string[]>() ?? [];
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod()));

// ---------- Rate limit login/register/refresh per IP ----------
var authPermits = config.GetValue("RateLimiting:AuthPermitsPerMinute", 10);
var publicPermits = config.GetValue("RateLimiting:PublicProfilePermitsPerMinute", 120);
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy("auth", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = authPermits, Window = TimeSpan.FromMinutes(1) }));
    o.AddPolicy("public", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = publicPermits, Window = TimeSpan.FromMinutes(1) }));
});

// ---------- API docs ----------
builder.Services.AddOpenApi(o => o.AddDocumentTransformer<BearerSecuritySchemeTransformer>());

var app = builder.Build();

if (config.GetValue("Database:MigrateOnStartup", true))
{
    using var scope = app.Services.CreateScope();
    scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.Migrate();
}

// VI: Nâng quyền SuperAdmin cho các username khai báo trong Admin:SuperAdmins (nếu tài khoản đã tồn tại).
// EN: Promote the usernames listed in Admin:SuperAdmins to SuperAdmin (when the accounts already exist).
using (var scope = app.Services.CreateScope())
{
    var promoted = await app.Services.GetRequiredService<AdminBootstrap>()
        .PromoteConfiguredSuperAdminsAsync(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    if (promoted > 0) app.Logger.LogInformation("Promoted {Count} configured super admin account(s).", promoted);
}

app.UseExceptionHandler();
app.UseStatusCodePages();

// ---------- Frontend: serve the static site so /@username links work ----------
// VI: Chỉ phục vụ index.html, css/, js/, assets/ trong thư mục frontend/; không bao giờ lộ backend/ hay file .db.
// EN: Only serves index.html, css/, js/ and assets/ from the frontend/ folder; never exposes backend/ or the .db file.
var frontendRoot = Path.GetFullPath(Path.Combine(app.Environment.ContentRootPath, config["Frontend:RootPath"] ?? "../../../frontend"));
var frontendIndex = Path.Combine(frontendRoot, "index.html");
var serveFrontend = config.GetValue("Frontend:Enabled", true) && File.Exists(frontendIndex);
if (serveFrontend)
{
    foreach (var dir in new[] { "css", "js", "assets" }.Where(d => Directory.Exists(Path.Combine(frontendRoot, d))))
    {
        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = new PhysicalFileProvider(Path.Combine(frontendRoot, dir)),
            RequestPath = "/" + dir,
            OnPrepareResponse = ctx => ctx.Context.Response.Headers.CacheControl = "no-cache"
        });
    }
    IResult Index(HttpContext ctx)
    {
        ctx.Response.Headers.CacheControl = "no-cache";
        return TypedResults.PhysicalFile(frontendIndex, "text/html; charset=utf-8");
    }
    app.MapGet("/", Index).ExcludeFromDescription();
    app.MapGet("/index.html", Index).ExcludeFromDescription();
    app.MapGet("/@{slug}", Index).ExcludeFromDescription();
    app.MapGet("/admin", Index).ExcludeFromDescription();
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference(o => o
        .WithTitle("Study Hub API")
        .AddPreferredSecuritySchemes("Bearer"));
    if (!serveFrontend)
        app.MapGet("/", () => Results.Redirect("/scalar")).ExcludeFromDescription();
}
else
{
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseCors();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/health");

app.Run();

// VI: Để project test dùng được WebApplicationFactory<Program>.
// EN: Lets the test project use WebApplicationFactory<Program>.
public partial class Program;
