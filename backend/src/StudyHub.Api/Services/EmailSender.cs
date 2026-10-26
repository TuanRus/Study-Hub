using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Options;

namespace StudyHub.Api.Services;

// VI: Cấu hình SMTP (vd. Gmail: smtp.gmail.com, cổng 587, mật khẩu ứng dụng 16 ký tự). Đặt qua biến môi trường
//     SMTP_HOST, SMTP_PORT, SMTP_USERNAME, SMTP_PASSWORD, SMTP_FROM (hoặc Smtp__Host...). Không bao giờ ghi vào appsettings.
// EN: SMTP settings (e.g. Gmail: smtp.gmail.com, port 587, a 16-character app password). Set through the environment
//     variables SMTP_HOST, SMTP_PORT, SMTP_USERNAME, SMTP_PASSWORD, SMTP_FROM (or Smtp__Host...). Never put them in appsettings.
public class SmtpOptions
{
    public const string Section = "Smtp";

    public string? Host { get; set; }
    // VI: 587 = STARTTLS. Cổng 465 (SSL ngay từ đầu) không được SmtpClient hỗ trợ. EN: 587 = STARTTLS; port 465 is not supported by SmtpClient.
    public int Port { get; set; } = 587;
    public string? Username { get; set; }
    public string? Password { get; set; }
    // VI: Địa chỉ người gửi, mặc định là Username. EN: Sender address, defaults to Username.
    public string? From { get; set; }
    public string FromName { get; set; } = "Study Hub";
    public bool EnableSsl { get; set; } = true;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Host) && !string.IsNullOrWhiteSpace(From ?? Username);
}

public record EmailMessage(string To, string Subject, string TextBody, string HtmlBody);

public interface IEmailSender
{
    // VI: false = server chưa cấu hình gửi mail. EN: false = the server has no mail settings.
    bool IsConfigured { get; }
    Task SendAsync(EmailMessage message, CancellationToken ct);
}

public class SmtpEmailSender(IOptions<SmtpOptions> options, IWebHostEnvironment env, ILogger<SmtpEmailSender> logger) : IEmailSender
{
    private readonly SmtpOptions _smtp = options.Value;

    // VI: Khi chạy Development mà chưa cấu hình SMTP thì in mail ra log để thử trên máy.
    // EN: In Development without SMTP settings the mail is written to the log so it can be tried locally.
    public bool IsConfigured => _smtp.IsConfigured || env.IsDevelopment();

    public async Task SendAsync(EmailMessage message, CancellationToken ct)
    {
        if (!_smtp.IsConfigured)
        {
            logger.LogWarning("SMTP is not configured; email to {To} was not sent. Subject: {Subject}\n{Body}",
                message.To, message.Subject, message.TextBody);
            return;
        }

        using var mail = new MailMessage
        {
            From = new MailAddress(_smtp.From ?? _smtp.Username!, _smtp.FromName),
            Subject = message.Subject,
            Body = message.TextBody,
        };
        mail.To.Add(message.To);
        mail.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(message.HtmlBody, null, "text/html"));

        using var client = new SmtpClient(_smtp.Host, _smtp.Port) { EnableSsl = _smtp.EnableSsl };
        if (!string.IsNullOrWhiteSpace(_smtp.Username))
            client.Credentials = new NetworkCredential(_smtp.Username, _smtp.Password);
        await client.SendMailAsync(mail, ct);
    }
}
