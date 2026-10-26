using System.Net;
using System.Net.Sockets;
using Microsoft.EntityFrameworkCore;
using StudyHub.Api.Data;

namespace StudyHub.Api.Services;

// VI: Danh sách IP bị ban giữ trong bộ nhớ, để mỗi request không phải đọc DB. Nạp lúc khởi động, cập nhật khi admin thêm/bỏ.
// EN: Banned IPs kept in memory so requests don't hit the DB. Loaded at start-up and updated when an admin adds/removes one.
public class IpBanList
{
    private volatile HashSet<string> _ips = [];

    public bool IsBanned(IPAddress? ip) => ip is not null && _ips.Contains(Normalize(ip));
    public bool IsBanned(string? ip) => ip is not null && _ips.Contains(ip);

    public async Task ReloadAsync(AppDbContext db, CancellationToken ct = default) =>
        _ips = (await db.IpBans.AsNoTracking().Select(b => b.Ip).ToListAsync(ct)).ToHashSet();

    // VI: IPv4 đi qua IPv6 (::ffff:1.2.3.4) được đưa về dạng IPv4. EN: IPv4-mapped IPv6 (::ffff:1.2.3.4) becomes plain IPv4.
    public static string Normalize(IPAddress ip) => (ip.IsIPv4MappedToIPv6 ? ip.MapToIPv4() : ip).ToString();

    // VI: IP nội bộ (LAN, Docker, proxy, localhost). Ban những IP này có thể chặn cả trang (vd. khi mọi request đi qua
    //     Nginx Proxy Manager mà chưa bật BEHIND_PROXY), nên không cho ban.
    // EN: Private addresses (LAN, Docker, proxy, localhost). Banning one could block the whole site (e.g. every request
    //     comes through Nginx Proxy Manager without BEHIND_PROXY), so they cannot be banned.
    public static bool IsPrivate(IPAddress ip)
    {
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        if (IPAddress.IsLoopback(ip)) return true;
        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = ip.GetAddressBytes();
            return b[0] == 10 || b[0] == 0 || (b[0] == 172 && b[1] >= 16 && b[1] <= 31) || (b[0] == 192 && b[1] == 168)
                   || (b[0] == 169 && b[1] == 254) || (b[0] == 100 && b[1] >= 64 && b[1] <= 127);
        }
        return ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || ip.IsIPv6UniqueLocal || ip.Equals(IPAddress.IPv6None);
    }
}

// VI: Chặn mọi request /api từ IP bị ban (trang tĩnh vẫn tải được để hiện thông báo).
// EN: Blocks every /api request from a banned IP (the static page still loads so it can show the message).
public class IpBanMiddleware(RequestDelegate next, IpBanList bans)
{
    public async Task InvokeAsync(HttpContext ctx)
    {
        if (ctx.Request.Path.StartsWithSegments("/api") && bans.IsBanned(ctx.Connection.RemoteIpAddress))
        {
            ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
            await ctx.Response.WriteAsJsonAsync(new
            {
                title = "Blocked",
                status = 403,
                detail = "Access from your network has been blocked by an administrator."
            }, ctx.RequestAborted);
            return;
        }
        await next(ctx);
    }
}
