using Microsoft.EntityFrameworkCore;
using StudyHub.Api.Common;
using StudyHub.Api.Data;

namespace StudyHub.Api.Services;

public static class UserContext
{
    // VI: Ưu tiên múi giờ client gửi lên (query ?timeZone=), không có thì dùng múi giờ trong hồ sơ.
    // EN: Prefer the time zone the client sends (?timeZone=), otherwise use the profile's.
    public static async Task<TimeZoneInfo> ResolveTimeZoneAsync(
        this AppDbContext db, Guid userId, string? requested, CancellationToken ct)
    {
        if (requested is not null)
        {
            if (!TimeZones.IsValid(requested))
                throw new BadRequestException($"Unknown time zone '{requested}'.");
            return TimeZones.Find(requested);
        }
        var stored = await db.Users.Where(u => u.Id == userId).Select(u => u.TimeZone).FirstOrDefaultAsync(ct);
        return TimeZones.Find(stored);
    }
}
