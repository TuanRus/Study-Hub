namespace StudyHub.Api.Common;

// VI: Giá trị mặc định của ngưỡng chống spam (admin chỉnh lại được trên trang Admin, lưu trong DB).
//     Tạo từ SuspiciousTasksPerDay task trong 24 giờ, hoặc bị ReportsToFlag người khác nhau báo cáo, thì tài khoản tự bị
//     đánh dấu sao; quá MaxTasksPerDay thì bị chặn tạo thêm (429). Đặt 0 để tắt.
// EN: Default anti-spam limits (admins can change them on the Admin page; saved in the DB).
//     SuspiciousTasksPerDay tasks in 24 hours, or reports from ReportsToFlag different people, star the account
//     automatically; more than MaxTasksPerDay blocks new tasks (429). Set 0 to turn one off.
public class AbuseOptions
{
    public const string Section = "Abuse";

    public int SuspiciousTasksPerDay { get; set; } = 50;
    public int MaxTasksPerDay { get; set; } = 300;
    public int ReportsToFlag { get; set; } = 3;
}
