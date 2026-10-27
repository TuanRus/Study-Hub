using Microsoft.Extensions.Options;

namespace StudyHub.Api.Common;

// VI: Độ dài tối thiểu của mật khẩu mới, chỉnh bằng Auth:PasswordMinLength (mặc định 8).
// EN: Minimum length of a new password, set with Auth:PasswordMinLength (default 8).
public class PasswordPolicyOptions
{
    public const string Section = "Auth";

    public int PasswordMinLength { get; set; } = 8;
}

public record PasswordPolicyDto(int MinLength, int MaxLength);

// VI: Quy tắc mật khẩu mạnh: đủ dài, có chữ hoa, chữ thường, chữ số và ký tự đặc biệt.
//     Chỉ áp dụng khi đặt mật khẩu mới (đăng ký, đổi, đặt lại), nên tài khoản cũ vẫn đăng nhập được.
// EN: Strong password rules: long enough, with an uppercase letter, a lowercase letter, a digit and a special character.
//     Only checked when a password is set (register, change, reset), so existing accounts still log in.
public class PasswordPolicy(IOptions<PasswordPolicyOptions> options)
{
    public const int MaxLength = 100;

    public int MinLength => Math.Clamp(options.Value.PasswordMinLength, 1, MaxLength);

    public PasswordPolicyDto ToDto() => new(MinLength, MaxLength);

    public IReadOnlyList<string> Problems(string password)
    {
        var problems = new List<string>();
        if (password.Length < MinLength) problems.Add($"be at least {MinLength} characters long");
        if (password.Length > MaxLength) problems.Add($"be at most {MaxLength} characters long");
        if (!password.Any(char.IsUpper)) problems.Add("contain an uppercase letter");
        if (!password.Any(char.IsLower)) problems.Add("contain a lowercase letter");
        if (!password.Any(char.IsDigit)) problems.Add("contain a digit");
        if (!password.Any(IsSpecial)) problems.Add("contain a special character (e.g. !@#$%)");
        return problems;
    }

    public void EnsureStrong(string password)
    {
        var problems = Problems(password);
        if (problems.Count > 0)
            throw new BadRequestException($"Password must {string.Join(", ", problems)}.");
    }

    private static bool IsSpecial(char c) => !char.IsLetterOrDigit(c) && !char.IsWhiteSpace(c);
}
