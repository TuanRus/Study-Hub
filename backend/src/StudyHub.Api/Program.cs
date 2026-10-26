using System.Text;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
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

// VI: Nhận luôn tên biến trong .env (JWT_KEY, SUPERADMIN_USERNAME), để `docker run --env-file .env` chạy được như docker compose.
//     Jwt__Key / Admin__SuperAdmins__0 nếu có vẫn được ưu tiên.
// EN: Also accept the .env names (JWT_KEY, SUPERADMIN_USERNAME) so `docker run --env-file .env` works like docker compose.
//     Jwt__Key / Admin__SuperAdmins__0 still win when set.
var envAliases = new Dictionary<string, string?>();
if (string.IsNullOrWhiteSpace(config["Jwt:Key"]) && !string.IsNullOrWhiteSpace(config["JWT_KEY"]))
    envAliases["Jwt:Key"] = config["JWT_KEY"]!.Trim();
if (!config.GetSection("Admin:SuperAdmins").GetChildren().Any(c => !string.IsNullOrWhiteSpace(c.Value))
    && !string.IsNullOrWhiteSpace(config["SUPERADMIN_USERNAME"]))
    envAliases["Admin:SuperAdmins:0"] = config["SUPERADMIN_USERNAME"]!.Trim();
if (string.IsNullOrWhiteSpace(config[PublicSite.ConfigKey]) && !string.IsNullOrWhiteSpace(config[PublicSite.EnvName]))
    envAliases[PublicSite.ConfigKey] = config[PublicSite.EnvName]!.Trim();
// VI: SMTP_HOST → Smtp:Host... (gửi mã quên mật khẩu). EN: SMTP_HOST → Smtp:Host... (forgot-password emails).
foreach (var (env, key) in new[] { ("SMTP_HOST", "Host"), ("SMTP_PORT", "Port"), ("SMTP_USERNAME", "Username"),
             ("SMTP_PASSWORD", "Password"), ("SMTP_FROM", "From") })
    if (string.IsNullOrWhiteSpace(config[$"{SmtpOptions.Section}:{key}"]) && !string.IsNullOrWhiteSpace(config[env]))
        envAliases[$"{SmtpOptions.Section}:{key}"] = config[env]!.Trim();
if (envAliases.Count > 0) config.AddInMemoryCollection(envAliases);

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
builder.Services.Configure<PasswordPolicyOptions>(config.GetSection(PasswordPolicyOptions.Section));
builder.Services.AddSingleton<PasswordPolicy>();
builder.Services.AddSingleton<TokenService>();

// ---------- App services ----------
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<PasswordResetService>();
builder.Services.Configure<SmtpOptions>(config.GetSection(SmtpOptions.Section));
builder.Services.AddSingleton<IEmailSender, SmtpEmailSender>();
builder.Services.AddScoped<TaskService>();
builder.Services.Configure<AbuseOptions>(config.GetSection(AbuseOptions.Section));
builder.Services.AddSingleton<IpBanList>();
builder.Services.AddSingleton<AbuseSettings>();
builder.Services.AddScoped<ReportService>();
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

// ---------- Reverse proxy (Nginx Proxy Manager, Caddy...) ----------
// VI: BEHIND_PROXY=true: tin X-Forwarded-For/Proto/Host từ proxy, để có IP thật của người dùng (rate limit, lượt xem)
//     và đúng https://<domain>. Chỉ bật khi cổng app không mở thẳng ra Internet, vì header này giả được.
// EN: BEHIND_PROXY=true: trust X-Forwarded-For/Proto/Host from the proxy, so we get the real client IP (rate limits, views)
//     and the right https://<domain>. Only enable when the app port is not exposed to the Internet directly; the headers can be faked.
// VI: ASPNETCORE_FORWARDEDHEADERS_ENABLED được ASP.NET đọc thành FORWARDEDHEADERS_ENABLED và tự thêm middleware.
// EN: ASP.NET reads ASPNETCORE_FORWARDEDHEADERS_ENABLED as FORWARDEDHEADERS_ENABLED and adds the middleware itself.
var builtInForwardedHeaders = config.GetValue("FORWARDEDHEADERS_ENABLED", false);
var behindProxy = config.GetValue("BEHIND_PROXY", false) || builtInForwardedHeaders;
if (behindProxy)
{
    builder.Services.Configure<ForwardedHeadersOptions>(o =>
    {
        o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
        // VI: Proxy chạy trong mạng Docker nên IP không cố định. EN: The proxy sits on a Docker network, so its IP is not fixed.
        o.KnownIPNetworks.Clear();
        o.KnownProxies.Clear();
    });
}
var publicBaseUrl = PublicSite.Normalize(config[PublicSite.ConfigKey]);

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

// VI: Username trong Admin:SuperAdmins: đã có tài khoản thì nâng lên SuperAdmin, chưa có thì tạo với mật khẩu ngẫu nhiên (in ra log).
// EN: Usernames in Admin:SuperAdmins: existing accounts become SuperAdmin, missing ones are created with a random password (logged).
using (var scope = app.Services.CreateScope())
{
    var bootstrap = app.Services.GetRequiredService<AdminBootstrap>();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var promoted = await bootstrap.PromoteConfiguredSuperAdminsAsync(db);
    if (promoted > 0) app.Logger.LogInformation("Promoted {Count} configured super admin account(s).", promoted);
    await bootstrap.CreateMissingSuperAdminsAsync(db, scope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>(), app.Logger);
    await app.Services.GetRequiredService<IpBanList>().ReloadAsync(db);
    await app.Services.GetRequiredService<AbuseSettings>().LoadAsync(db);
}

// VI: Không thêm lần nữa nếu ASP.NET đã tự thêm. EN: Do not add it twice when ASP.NET already did.
if (behindProxy && !builtInForwardedHeaders) app.UseForwardedHeaders();
// VI: Sau forwarded headers để thấy IP thật của người dùng. EN: After forwarded headers so it sees the real client IP.
app.UseMiddleware<IpBanMiddleware>();

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

// VI: FE đọc publicBaseUrl để tạo link /@username; origin là địa chỉ server nhìn thấy (dùng để kiểm tra proxy).
// EN: The FE reads publicBaseUrl to build /@username links; origin is the address the server sees (handy to check the proxy).
app.MapGet("/api/site", (HttpContext ctx) => new SiteInfo(publicBaseUrl, $"{ctx.Request.Scheme}://{ctx.Request.Host}"));

app.Run();

// VI: Để project test dùng được WebApplicationFactory<Program>.
// EN: Lets the test project use WebApplicationFactory<Program>.
public partial class Program;
