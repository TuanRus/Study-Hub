using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using StudyHub.Api.Data;
using StudyHub.Api.Entities;

namespace StudyHub.Api.Services;

// VI: Super admin được chỉ định bằng cấu hình (Admin:SuperAdmins), không có mật khẩu viết sẵn trong code.
//     Khi server khởi động, username chưa có tài khoản sẽ được tạo sẵn với mật khẩu ngẫu nhiên (in ra log một lần),
//     username đã có tài khoản thì được nâng lên SuperAdmin. Đăng ký công khai không được dùng các username này,
//     nên không ai "giành" trước được tài khoản SuperAdmin. Admin thường chỉ do SuperAdmin nâng quyền trong trang admin.
// EN: Super admins are named in configuration (Admin:SuperAdmins), no password is hard-coded.
//     At server start a listed username without an account is created with a random password (logged once),
//     and an existing account is promoted to SuperAdmin. Public sign-up cannot use these usernames,
//     so nobody can grab the SuperAdmin account first. Regular Admins are only promoted by a SuperAdmin from the admin page.
public partial class AdminBootstrap(IConfiguration config)
{
    private readonly HashSet<string> _usernames = (config.GetSection("Admin:SuperAdmins").Get<string[]>() ?? [])
        .Where(u => !string.IsNullOrWhiteSpace(u))
        .Select(u => u.Trim().ToLowerInvariant())
        .ToHashSet();

    public record SeededAccount(string Username, string Password);

    public bool IsConfiguredSuperAdmin(string username) => _usernames.Contains(username.Trim().ToLowerInvariant());

    public async Task<int> PromoteConfiguredSuperAdminsAsync(AppDbContext db, CancellationToken ct = default)
    {
        if (_usernames.Count == 0) return 0;
        var names = _usernames.ToList();
        return await db.Users
            .Where(u => names.Contains(u.Username.ToLower()) && u.Role != UserRole.SuperAdmin)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.Role, UserRole.SuperAdmin), ct);
    }

    // VI: Tạo tài khoản cho username chưa tồn tại. Chạy lại nhiều lần cũng không tạo trùng.
    // EN: Creates accounts for usernames that do not exist yet. Running it again never creates duplicates.
    public async Task<IReadOnlyList<SeededAccount>> CreateMissingSuperAdminsAsync(
        AppDbContext db, IPasswordHasher<User> hasher, ILogger logger, CancellationToken ct = default)
    {
        var created = new List<SeededAccount>();
        foreach (var username in _usernames)
        {
            if (!ValidUsername().IsMatch(username))
            {
                logger.LogWarning("Admin:SuperAdmins entry '{Username}' is not a valid username (3-32 letters, digits, '_' or '.'); skipped.", username);
                continue;
            }
            if (await db.Users.AnyAsync(u => u.Username.ToLower() == username, ct)) continue;

            var email = $"{username}@studyhub.local";
            if (await db.Users.AnyAsync(u => u.Email == email, ct))
            {
                logger.LogWarning("Cannot create super admin '{Username}': email {Email} is already used.", username, email);
                continue;
            }

            var password = RandomPassword();
            var user = new User
            {
                Username = username,
                Email = email,
                DisplayName = username,
                Role = UserRole.SuperAdmin,
                PomodoroSettings = new PomodoroSettings()
            };
            user.PasswordHash = hasher.HashPassword(user, password);
            user.Playlists.Add(new Playlist { Name = "Favorites", IsDefault = true });
            user.Profile = await ProfileService.NewProfileAsync(db, user, ct);
            db.Users.Add(user);
            await db.SaveChangesAsync(ct);

            created.Add(new SeededAccount(username, password));
            logger.LogWarning(
                "Created super admin account '{Username}' with one-time password: {Password} . " +
                "Sign in and change this password and the email ({Email}) right away. It is not shown again.",
                username, password, email);
        }
        return created;
    }

    private static string RandomPassword() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(18)).Replace('+', '-').Replace('/', '_');

    [GeneratedRegex("^[a-z0-9_.]{3,32}$")]
    private static partial Regex ValidUsername();
}
