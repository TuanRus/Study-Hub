using Microsoft.EntityFrameworkCore;
using StudyHub.Api.Data;
using StudyHub.Api.Entities;

namespace StudyHub.Api.Services;

// VI: Super admin được chỉ định bằng cấu hình (Admin:SuperAdmins), không có mật khẩu viết sẵn trong code.
//     Username trong danh sách thành SuperAdmin khi đăng ký, hoặc khi server khởi động nếu đã có tài khoản.
//     Admin thường chỉ do SuperAdmin nâng quyền trong trang admin.
// EN: Super admins are named in configuration (Admin:SuperAdmins), no password is hard-coded.
//     Listed usernames become SuperAdmin when they sign up, or at server start if the account already exists.
//     Regular Admins are only promoted by a SuperAdmin from the admin page.
public class AdminBootstrap(IConfiguration config)
{
    private readonly HashSet<string> _usernames = (config.GetSection("Admin:SuperAdmins").Get<string[]>() ?? [])
        .Where(u => !string.IsNullOrWhiteSpace(u))
        .Select(u => u.Trim().ToLowerInvariant())
        .ToHashSet();

    public bool IsConfiguredSuperAdmin(string username) => _usernames.Contains(username.Trim().ToLowerInvariant());

    public async Task<int> PromoteConfiguredSuperAdminsAsync(AppDbContext db, CancellationToken ct = default)
    {
        if (_usernames.Count == 0) return 0;
        var names = _usernames.ToList();
        return await db.Users
            .Where(u => names.Contains(u.Username.ToLower()) && u.Role != UserRole.SuperAdmin)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.Role, UserRole.SuperAdmin), ct);
    }
}
