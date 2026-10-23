using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using StudyHub.Api.Entities;

namespace StudyHub.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<StudyTask> Tasks => Set<StudyTask>();
    public DbSet<PomodoroSettings> PomodoroSettings => Set<PomodoroSettings>();
    public DbSet<FocusSession> FocusSessions => Set<FocusSession>();
    public DbSet<Playlist> Playlists => Set<Playlist>();
    public DbSet<Track> Tracks => Set<Track>();
    public DbSet<Profile> Profiles => Set<Profile>();
    public DbSet<ProfileView> ProfileViews => Set<ProfileView>();

    protected override void ConfigureConventions(ModelConfigurationBuilder builder)
    {
        // VI: Mọi DateTime lưu và đọc ra đều là UTC (SQLite không giữ DateTimeKind).
        // EN: Every DateTime is stored and read back as UTC (SQLite drops DateTimeKind).
        builder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
        builder.Properties<DateTime?>().HaveConversion<NullableUtcDateTimeConverter>();
        builder.Properties<Enum>().HaveConversion<string>().HaveMaxLength(20);
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<User>(e =>
        {
            e.Property(u => u.Username).HasMaxLength(32).IsRequired();
            e.Property(u => u.Email).HasMaxLength(256).IsRequired();
            e.Property(u => u.DisplayName).HasMaxLength(64);
            e.Property(u => u.TimeZone).HasMaxLength(64);
            e.HasIndex(u => u.Username).IsUnique();
            e.HasIndex(u => u.Email).IsUnique();
            e.Property(u => u.Role).HasDefaultValue(UserRole.User).HasSentinel(UserRole.User);
            e.HasOne(u => u.PomodoroSettings).WithOne(s => s.User)
                .HasForeignKey<PomodoroSettings>(s => s.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<RefreshToken>(e =>
        {
            e.Property(t => t.TokenHash).HasMaxLength(64).IsRequired();
            e.HasIndex(t => t.TokenHash).IsUnique();
            e.HasOne(t => t.User).WithMany(u => u.RefreshTokens).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<StudyTask>(e =>
        {
            e.ToTable("Tasks");
            e.Property(t => t.Title).HasMaxLength(200).IsRequired();
            e.Property(t => t.Description).HasMaxLength(2000);
            e.Property(t => t.Color).HasMaxLength(7);
            e.HasIndex(t => new { t.UserId, t.EndAt });
            e.HasIndex(t => new { t.UserId, t.StartAt });
            e.HasOne(t => t.User).WithMany(u => u.Tasks).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<PomodoroSettings>().HasKey(s => s.UserId);

        b.Entity<FocusSession>(e =>
        {
            e.HasIndex(s => new { s.UserId, s.StartedAt });
            e.HasOne(s => s.User).WithMany(u => u.FocusSessions).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(s => s.Task).WithMany().HasForeignKey(s => s.TaskId).OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<Playlist>(e =>
        {
            e.Property(p => p.Name).HasMaxLength(100).IsRequired();
            e.HasOne(p => p.User).WithMany(u => u.Playlists).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Track>(e =>
        {
            e.Property(t => t.Title).HasMaxLength(200).IsRequired();
            e.Property(t => t.SourceUrl).HasMaxLength(500).IsRequired();
            e.Property(t => t.EmbedUrl).HasMaxLength(500).IsRequired();
            e.HasOne(t => t.Playlist).WithMany(p => p.Tracks).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Profile>(e =>
        {
            e.HasKey(p => p.UserId);
            e.HasOne(p => p.User).WithOne(u => u.Profile)
                .HasForeignKey<Profile>(p => p.UserId).OnDelete(DeleteBehavior.Cascade);
            e.Property(p => p.Slug).HasMaxLength(32).IsRequired();
            e.HasIndex(p => p.Slug).IsUnique();
            e.Property(p => p.DisplayName).HasMaxLength(40).IsRequired();
            e.Property(p => p.AvatarUrl).HasMaxLength(500);
            e.Property(p => p.Bio).HasMaxLength(400);
            e.Property(p => p.Location).HasMaxLength(40);
            e.Property(p => p.Background).HasMaxLength(20);
            e.Property(p => p.BackgroundUrl).HasMaxLength(500);
            e.Property(p => p.Effect).HasMaxLength(20);
            e.Property(p => p.AccentColor).HasMaxLength(7);
            e.Property(p => p.SongUrl).HasMaxLength(500);

            // VI: Danh sách nhỏ, lưu dạng chuỗi JSON trong một cột.
            // EN: Small collections, stored as a JSON string in one column.
            e.Property(p => p.Badges).HasConversion(
                v => JsonSerializer.Serialize(v, JsonSerializerOptions.Default),
                v => JsonSerializer.Deserialize<List<string>>(v, JsonSerializerOptions.Default) ?? new List<string>(),
                new ValueComparer<List<string>>(
                    (a, c) => a!.SequenceEqual(c!), v => v.Aggregate(0, (h, x) => HashCode.Combine(h, x.GetHashCode())), v => v.ToList()));
            e.Property(p => p.Links).HasConversion(
                v => JsonSerializer.Serialize(v, JsonSerializerOptions.Default),
                v => JsonSerializer.Deserialize<Dictionary<string, string>>(v, JsonSerializerOptions.Default) ?? new Dictionary<string, string>(),
                new ValueComparer<Dictionary<string, string>>(
                    (a, c) => a!.Count == c!.Count && !a.Except(c).Any(),
                    v => v.Aggregate(0, (h, x) => HashCode.Combine(h, x.Key.GetHashCode(), x.Value.GetHashCode())),
                    v => new Dictionary<string, string>(v)));
        });

        b.Entity<ProfileView>(e =>
        {
            e.Property(v => v.VisitorHash).HasMaxLength(64).IsRequired();
            e.Property(v => v.IpHash).HasMaxLength(64).IsRequired();
            e.HasIndex(v => new { v.ProfileId, v.Day, v.VisitorHash }).IsUnique();
            e.HasIndex(v => new { v.ProfileId, v.Day, v.IpHash });
            e.HasOne(v => v.Profile).WithMany(p => p.Views).HasForeignKey(v => v.ProfileId).OnDelete(DeleteBehavior.Cascade);
        });
    }

    private sealed class UtcDateTimeConverter() : ValueConverter<DateTime, DateTime>(
        v => v.Kind == DateTimeKind.Utc ? v : v.ToUniversalTime(),
        v => DateTime.SpecifyKind(v, DateTimeKind.Utc));

    private sealed class NullableUtcDateTimeConverter() : ValueConverter<DateTime?, DateTime?>(
        v => v == null ? v : v.Value.Kind == DateTimeKind.Utc ? v : v.Value.ToUniversalTime(),
        v => v == null ? v : DateTime.SpecifyKind(v.Value, DateTimeKind.Utc));
}
