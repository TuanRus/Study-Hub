namespace StudyHub.Api.Common;

// VI: So sánh email để chặn đăng ký trùng. Gmail bỏ qua dấu chấm và phần "+..." trong tên,
//     nên a.b+abc@gmail.com và ab@gmail.com là cùng một hộp thư.
// EN: Compares emails to block duplicate sign-ups. Gmail ignores dots and anything after "+" in the name,
//     so a.b+abc@gmail.com and ab@gmail.com are the same mailbox.
public static class EmailAddresses
{
    private static readonly HashSet<string> GmailDomains = ["gmail.com", "googlemail.com"];

    public static string Normalize(string email) => email.Trim().ToLowerInvariant();

    public static bool IsGmail(string email) => GmailDomains.Contains(Domain(Normalize(email)));

    // VI: Dạng "gốc" của địa chỉ: Gmail thì bỏ dấu chấm và "+...", các nhà cung cấp khác giữ nguyên.
    // EN: The "canonical" address: Gmail drops dots and "+...", other providers stay as they are.
    public static string Canonical(string email)
    {
        var normalized = Normalize(email);
        var at = normalized.LastIndexOf('@');
        if (at <= 0 || !GmailDomains.Contains(normalized[(at + 1)..])) return normalized;
        var name = normalized[..at];
        var plus = name.IndexOf('+');
        if (plus >= 0) name = name[..plus];
        return name.Replace(".", "") + "@gmail.com";
    }

    private static string Domain(string email) => email[(email.LastIndexOf('@') + 1)..];
}
