using HospitalManagement.Api.Data;
using HospitalManagement.Api.Helpers;
using HospitalManagement.Api.Models;

namespace HospitalManagement.Api.Services;

/// <summary>
/// Adds an audit row to the current unit of work. The row is saved by the caller's SaveChanges, so the audit entry
/// and the business change commit (or roll back) together.
/// </summary>
public static class AuditWriter
{
    public static void Stage(
        AppDbContext dbContext,
        AuditActor actor,
        string action,
        string entityName,
        string? entityId,
        string description)
    {
        dbContext.AuditLogs.Add(new AuditLog
        {
            Id = Guid.NewGuid(),
            Timestamp = DateTime.UtcNow,
            UserId = actor.UserId,
            UserEmail = Truncate(actor.Email, 256),
            UserRole = Truncate(actor.Role, 50),
            Action = Truncate(action, 100)!,
            EntityName = Truncate(entityName, 100)!,
            EntityId = Truncate(entityId, 100),
            Description = Truncate(description, 1000)!,
            IpAddress = Truncate(actor.IpAddress, 64),
        });
    }

    private static string? Truncate(string? value, int max) =>
        value is null || value.Length <= max ? value : value[..max];
}
