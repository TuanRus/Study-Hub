namespace StudyHub.Api.Common;

// VI: Địa chỉ công khai của trang (vd. https://studyhub.example.com) để FE tạo link /@username đúng domain,
//     kể cả khi mở app bằng IP nội bộ. Để trống = FE dùng origin hiện tại của trình duyệt.
// EN: Public address of the site (e.g. https://studyhub.example.com) so the FE builds /@username links on the right domain,
//     even when the app is opened through a LAN IP. Empty = the FE uses the browser's current origin.
public static class PublicSite
{
    public const string ConfigKey = "PublicSite:BaseUrl";
    public const string EnvName = "PUBLIC_BASE_URL";

    public const string InvalidMessage =
        "PUBLIC_BASE_URL (PublicSite:BaseUrl) must be an absolute http(s) URL with no path, e.g. https://studyhub.example.com";

    // VI: Trả về URL đã chuẩn hoá (không có / cuối), null nếu để trống. Sai định dạng thì báo lỗi lúc khởi động.
    // EN: Returns the normalized URL (no trailing slash), or null when empty. Throws at start-up when malformed.
    public static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || uri.AbsolutePath != "/" || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            throw new InvalidOperationException(InvalidMessage);
        return uri.GetLeftPart(UriPartial.Authority);
    }
}

public record SiteInfo(string? PublicBaseUrl, string Origin);
