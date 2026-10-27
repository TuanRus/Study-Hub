using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using StudyHub.Api.Auth;
using StudyHub.Api.Common;
using StudyHub.Api.Data;
using StudyHub.Api.Dtos;
using StudyHub.Api.Entities;

namespace StudyHub.Api.Services;

public class AuthService(AppDbContext db, TokenService tokens, IPasswordHasher<User> hasher, AdminBootstrap admins,
    PasswordPolicy passwordPolicy)
{
    public async Task<AuthResponse> RegisterAsync(RegisterRequest req, string? ip, CancellationToken ct)
    {
        var username = req.Username.Trim();
        var email = EmailAddresses.Normalize(req.Email);

        // VI: Username SuperAdmin trong cấu hình chỉ do server tạo lúc khởi động, không đăng ký công khai được.
        // EN: Configured SuperAdmin usernames are created by the server at start-up and cannot be signed up for.
        if (admins.IsConfiguredSuperAdmin(username))
            throw new ConflictException("Username is already taken.");
        if (await db.Users.AnyAsync(u => u.Username.ToLower() == username.ToLower(), ct))
            throw new ConflictException("Username is already taken.");
        if (await EmailTakenAsync(email, ct))
            throw new ConflictException("Email is already registered.");
        if (req.TimeZone is not null && !TimeZones.IsValid(req.TimeZone))
            throw new BadRequestException($"Unknown time zone '{req.TimeZone}'.");
        passwordPolicy.EnsureStrong(req.Password);

        var user = new User
        {
            Username = username,
            Email = email,
            DisplayName = string.IsNullOrWhiteSpace(req.DisplayName) ? username : req.DisplayName.Trim(),
            TimeZone = req.TimeZone ?? "Asia/Ho_Chi_Minh",
            RegisterIp = ip,
            PomodoroSettings = new PomodoroSettings()
        };
        user.PasswordHash = hasher.HashPassword(user, req.Password);
        user.Playlists.Add(new Playlist { Name = "Favorites", IsDefault = true });
        user.Profile = await ProfileService.NewProfileAsync(db, user, ct);

        db.Users.Add(user);
        return await IssueTokensAsync(user, ip, ct);
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest req, string? ip, CancellationToken ct)
    {
        var key = req.UsernameOrEmail.Trim().ToLowerInvariant();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Username.ToLower() == key || u.Email == key, ct);

        // VI: Cùng một thông báo cho cả sai user và sai mật khẩu để không lộ user nào tồn tại.
        // EN: Same message for unknown user and wrong password so usernames are not leaked.
        if (user is null)
            throw new UnauthorizedException("Invalid username or password.");

        var result = hasher.VerifyHashedPassword(user, user.PasswordHash, req.Password);
        if (result == PasswordVerificationResult.Failed)
            throw new UnauthorizedException("Invalid username or password.");
        if (result == PasswordVerificationResult.SuccessRehashNeeded)
            user.PasswordHash = hasher.HashPassword(user, req.Password);
        if (user.IsDisabled)
            throw new AppException(StatusCodes.Status403Forbidden, "Account disabled", "This account has been disabled by an admin.");

        return await IssueTokensAsync(user, ip, ct);
    }

    public async Task<AuthResponse> RefreshAsync(string refreshToken, string? ip, CancellationToken ct)
    {
        var hash = TokenService.Hash(refreshToken);
        var stored = await db.RefreshTokens.Include(t => t.User)
            .FirstOrDefaultAsync(t => t.TokenHash == hash, ct);

        if (stored is null)
            throw new UnauthorizedException("Invalid refresh token.");

        if (stored.RevokedAt is not null)
        {
            // VI: Token đã bị dùng lại sau khi xoay vòng: có thể bị đánh cắp, thu hồi toàn bộ phiên của user.
            // EN: A rotated token was reused: possible theft, so revoke every session of this user.
            await RevokeAllAsync(stored.UserId, ct);
            throw new UnauthorizedException("Refresh token was already used.");
        }
        if (!stored.IsActive)
            throw new UnauthorizedException("Refresh token has expired.");
        if (stored.User.IsDisabled)
            throw new UnauthorizedException("This account has been disabled.");

        stored.RevokedAt = DateTime.UtcNow;
        return await IssueTokensAsync(stored.User, ip, ct);
    }

    public async Task LogoutAsync(string refreshToken, CancellationToken ct)
    {
        var hash = TokenService.Hash(refreshToken);
        var stored = await db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (stored is { RevokedAt: null })
        {
            stored.RevokedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
        }
    }

    public async Task<UserDto> GetMeAsync(Guid userId, CancellationToken ct) =>
        (await FindUserAsync(userId, ct)).ToDto();

    public async Task<UserDto> UpdateProfileAsync(Guid userId, UpdateProfileRequest req, CancellationToken ct)
    {
        var user = await FindUserAsync(userId, ct);
        if (req.TimeZone is not null)
        {
            if (!TimeZones.IsValid(req.TimeZone))
                throw new BadRequestException($"Unknown time zone '{req.TimeZone}'.");
            user.TimeZone = req.TimeZone;
        }
        if (req.DisplayName is not null)
            user.DisplayName = string.IsNullOrWhiteSpace(req.DisplayName) ? user.Username : req.DisplayName.Trim();

        await db.SaveChangesAsync(ct);
        return user.ToDto();
    }

    public async Task ChangePasswordAsync(Guid userId, ChangePasswordRequest req, CancellationToken ct)
    {
        var user = await FindUserAsync(userId, ct);
        if (hasher.VerifyHashedPassword(user, user.PasswordHash, req.CurrentPassword) == PasswordVerificationResult.Failed)
            throw new BadRequestException("Current password is incorrect.");
        passwordPolicy.EnsureStrong(req.NewPassword);

        user.PasswordHash = hasher.HashPassword(user, req.NewPassword);
        await RevokeAllAsync(userId, ct);
    }

    // VI: Trùng email chính xác, hoặc cùng một hộp thư Gmail viết khác (dấu chấm, "+...").
    // EN: Same email, or the same Gmail mailbox written differently (dots, "+...").
    private async Task<bool> EmailTakenAsync(string email, CancellationToken ct)
    {
        if (await db.Users.AnyAsync(u => u.Email == email, ct)) return true;
        if (!EmailAddresses.IsGmail(email)) return false;
        var canonical = EmailAddresses.Canonical(email);
        var gmails = await db.Users.Where(u => u.Email.EndsWith("@gmail.com") || u.Email.EndsWith("@googlemail.com"))
            .Select(u => u.Email).ToListAsync(ct);
        return gmails.Any(e => EmailAddresses.Canonical(e) == canonical);
    }

    private async Task<User> FindUserAsync(Guid userId, CancellationToken ct) =>
        await db.Users.FindAsync([userId], ct) ?? throw new NotFoundException("User");

    private async Task RevokeAllAsync(Guid userId, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        await db.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now), ct);
        await db.SaveChangesAsync(ct);
    }

    private async Task<AuthResponse> IssueTokensAsync(User user, string? ip, CancellationToken ct)
    {
        var (access, accessExpires) = tokens.CreateAccessToken(user);
        var (refresh, refreshHash, refreshExpires) = tokens.CreateRefreshToken();

        db.RefreshTokens.Add(new RefreshToken { User = user, TokenHash = refreshHash, ExpiresAt = refreshExpires });
        user.LastLoginAt = DateTime.UtcNow;
        if (ip is not null) user.LastIp = ip;
        await db.SaveChangesAsync(ct);

        return new AuthResponse(access, accessExpires, refresh, refreshExpires, user.ToDto());
    }
}
