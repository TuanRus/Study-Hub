using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using StudyHub.Api.Auth;
using StudyHub.Api.Common;
using StudyHub.Api.Data;
using StudyHub.Api.Dtos;
using StudyHub.Api.Entities;

namespace StudyHub.Api.Services;

// VI: Quên mật khẩu: gửi mã OTP 6 số qua email, nhập đúng mã thì đặt mật khẩu mới.
//     Luôn trả cùng một kết quả dù email có tồn tại hay không, để không lộ email nào đã đăng ký.
// EN: Forgot password: email a 6-digit OTP; the right code lets the user set a new password.
//     Always answers the same way whether or not the email exists, so registered emails are not leaked.
public class PasswordResetService(
    AppDbContext db,
    IEmailSender email,
    IPasswordHasher<User> hasher,
    IOptions<JwtOptions> jwt,
    PasswordPolicy passwordPolicy,
    ILogger<PasswordResetService> logger)
{
    public static readonly TimeSpan CodeLifetime = TimeSpan.FromMinutes(10);
    // VI: Gửi lại mã sớm nhất sau chừng này. EN: Earliest a new code can be sent.
    public static readonly TimeSpan ResendCooldown = TimeSpan.FromSeconds(60);
    public const int MaxAttempts = 5;
    private const string InvalidCode = "The code is wrong or has expired. Request a new one.";

    public async Task RequestAsync(ForgotPasswordRequest req, CancellationToken ct)
    {
        if (!email.IsConfigured)
            throw new AppException(StatusCodes.Status503ServiceUnavailable, "Email not set up",
                "Password reset by email is not set up on this server yet.");

        var address = EmailAddresses.Normalize(req.Email);
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == address, ct);
        if (user is null || user.IsDisabled) return;

        var now = DateTime.UtcNow;
        var old = await db.PasswordResetCodes.Where(c => c.UserId == user.Id).ToListAsync(ct);
        if (old.Any(c => c.CreatedAt > now - ResendCooldown)) return;
        db.PasswordResetCodes.RemoveRange(old);

        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        db.PasswordResetCodes.Add(new PasswordResetCode
        {
            UserId = user.Id,
            CodeHash = Hash(user.Id, code),
            CreatedAt = now,
            ExpiresAt = now + CodeLifetime,
        });
        await db.SaveChangesAsync(ct);

        try
        {
            await email.SendAsync(Message(user, code), ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not send the password reset email.");
            throw new AppException(StatusCodes.Status503ServiceUnavailable, "Email failed",
                "Could not send the email right now. Try again in a few minutes.");
        }
    }

    public async Task ResetAsync(ResetPasswordRequest req, CancellationToken ct)
    {
        // VI: Kiểm tra trước mã, để mật khẩu yếu không bị tính là một lần nhập sai mã.
        // EN: Checked before the code, so a weak password does not count as a wrong code attempt.
        passwordPolicy.EnsureStrong(req.NewPassword);
        var address = EmailAddresses.Normalize(req.Email);
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == address, ct);
        var stored = user is null ? null : await db.PasswordResetCodes.FirstOrDefaultAsync(c => c.UserId == user.Id, ct);
        if (user is null || stored is null || user.IsDisabled)
            throw new BadRequestException(InvalidCode);

        if (stored.ExpiresAt <= DateTime.UtcNow || stored.FailedAttempts >= MaxAttempts)
        {
            db.PasswordResetCodes.Remove(stored);
            await db.SaveChangesAsync(ct);
            throw new BadRequestException(InvalidCode);
        }

        var given = Encoding.ASCII.GetBytes(Hash(user.Id, req.Code.Trim()));
        if (!CryptographicOperations.FixedTimeEquals(given, Encoding.ASCII.GetBytes(stored.CodeHash)))
        {
            stored.FailedAttempts++;
            await db.SaveChangesAsync(ct);
            throw new BadRequestException(InvalidCode);
        }

        user.PasswordHash = hasher.HashPassword(user, req.NewPassword);
        db.PasswordResetCodes.Remove(stored);
        // VI: Đăng xuất mọi thiết bị, như khi đổi mật khẩu. EN: Sign out every device, like a password change.
        var now = DateTime.UtcNow;
        await db.RefreshTokens.Where(t => t.UserId == user.Id && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now), ct);
        await db.SaveChangesAsync(ct);
    }

    private string Hash(Guid userId, string code) =>
        Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(jwt.Value.Key), Encoding.UTF8.GetBytes($"{userId:N}:{code}")));

    private static EmailMessage Message(User user, string code)
    {
        var name = WebUtility.HtmlEncode(user.DisplayName ?? user.Username);
        var minutes = (int)CodeLifetime.TotalMinutes;
        var text =
            $"Hi {user.DisplayName ?? user.Username},\n\n" +
            $"Your Study Hub password reset code is: {code}\n" +
            $"It expires in {minutes} minutes. If you did not ask for this, ignore this email.\n\n" +
            $"Mã đặt lại mật khẩu Study Hub của bạn là: {code} (hết hạn sau {minutes} phút).\n" +
            "Nếu bạn không yêu cầu, hãy bỏ qua email này.";
        var html = $"""
            <div style="font-family:Arial,sans-serif;max-width:420px;margin:auto;color:#181b22">
              <h2 style="margin:0 0 12px">Study Hub</h2>
              <p>Hi {name},</p>
              <p>Your password reset code is:</p>
              <p style="font-size:30px;font-weight:700;letter-spacing:6px;margin:16px 0">{code}</p>
              <p style="color:#5c6474">It expires in {minutes} minutes. If you did not ask for this, ignore this email.</p>
              <hr style="border:none;border-top:1px solid #e4e6ec;margin:20px 0">
              <p style="color:#5c6474">Mã đặt lại mật khẩu của bạn là <b>{code}</b>, hết hạn sau {minutes} phút.
              Nếu bạn không yêu cầu, hãy bỏ qua email này.</p>
            </div>
            """;
        return new EmailMessage(user.Email, $"Study Hub password reset code: {code}", text, html);
    }
}
