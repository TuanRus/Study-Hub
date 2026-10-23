namespace StudyHub.Api.Auth;

public static class Policies
{
    // VI: Admin hoặc SuperAdmin. EN: Admin or SuperAdmin.
    public const string Admin = "Admin";
    // VI: Chỉ SuperAdmin (thêm/bỏ Admin). EN: SuperAdmin only (adding/removing Admins).
    public const string SuperAdmin = "SuperAdmin";
}
