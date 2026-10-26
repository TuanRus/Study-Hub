using System.ComponentModel.DataAnnotations;
using StudyHub.Api.Entities;

namespace StudyHub.Api.Dtos;

public record CreateReportRequest(ReportReason Reason, [StringLength(500)] string? Details = null);
