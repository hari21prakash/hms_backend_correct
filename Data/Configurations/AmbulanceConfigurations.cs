using HospitalManagement.Api.Models;
using HospitalManagement.Api.Models.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HospitalManagement.Api.Data.Configurations;

// History is never deleted: every relationship is Restrict.

public class AmbulanceProviderConfiguration : IEntityTypeConfiguration<AmbulanceProvider>
{
    public void Configure(EntityTypeBuilder<AmbulanceProvider> builder)
    {
        builder.Property(p => p.Name).IsRequired().HasMaxLength(200);
        builder.Property(p => p.ContactPerson).IsRequired().HasMaxLength(150);
        builder.Property(p => p.Email).IsRequired().HasMaxLength(256);
        builder.Property(p => p.Phone).IsRequired().HasMaxLength(30);
        builder.Property(p => p.Address).IsRequired().HasMaxLength(300);
        builder.Property(p => p.LicenseNumber).IsRequired().HasMaxLength(100);
        builder.Property(p => p.Notes).HasMaxLength(1000);
        builder.Property(p => p.StatusReason).HasMaxLength(500);

        builder.HasIndex(p => p.Name).IsUnique();
        builder.HasIndex(p => p.LicenseNumber).IsUnique();
        builder.HasIndex(p => p.Status);
        builder.HasIndex(p => p.ReviewedByUserId);

        // Approved / rejected / suspended providers must record who decided and when.
        builder.ToTable(t => t.HasCheckConstraint(
            "CK_AmbulanceProviders_Review",
            $"\"Status\" = '{ProviderStatus.Pending}' OR (\"ReviewedByUserId\" IS NOT NULL AND \"ReviewedAt\" IS NOT NULL)"));

        builder.HasOne(p => p.ReviewedByUser).WithMany()
            .HasForeignKey(p => p.ReviewedByUserId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class AmbulanceProviderUserConfiguration : IEntityTypeConfiguration<AmbulanceProviderUser>
{
    public void Configure(EntityTypeBuilder<AmbulanceProviderUser> builder)
    {
        // A login belongs to at most one provider.
        builder.HasIndex(p => p.UserId).IsUnique();
        builder.HasIndex(p => p.ProviderId);

        builder.HasOne(p => p.Provider).WithMany(p => p.Users)
            .HasForeignKey(p => p.ProviderId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(p => p.User).WithMany()
            .HasForeignKey(p => p.UserId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class AmbulanceConfiguration : IEntityTypeConfiguration<Ambulance>
{
    public void Configure(EntityTypeBuilder<Ambulance> builder)
    {
        builder.Property(a => a.VehicleNumber).IsRequired().HasMaxLength(30);
        builder.Property(a => a.Make).HasMaxLength(100);
        builder.Property(a => a.Model).HasMaxLength(100);
        builder.Property(a => a.Notes).HasMaxLength(500);

        builder.HasIndex(a => a.VehicleNumber).IsUnique();
        builder.HasIndex(a => a.Status);
        builder.HasIndex(a => a.ProviderId);
        builder.HasIndex(a => a.DriverUserId);

        // One driver operates at most one ambulance.
        builder.HasIndex(a => a.DriverUserId, "UX_Ambulances_DriverUserId")
            .IsUnique()
            .HasFilter("\"DriverUserId\" IS NOT NULL");

        builder.ToTable(t =>
        {
            t.HasCheckConstraint("CK_Ambulances_Capacity", "\"Capacity\" >= 1");
            t.HasCheckConstraint("CK_Ambulances_Latitude", "\"CurrentLatitude\" IS NULL OR \"CurrentLatitude\" BETWEEN -90 AND 90");
            t.HasCheckConstraint("CK_Ambulances_Longitude", "\"CurrentLongitude\" IS NULL OR \"CurrentLongitude\" BETWEEN -180 AND 180");
            // Hospital-owned vehicles have no provider; external vehicles always have one.
            t.HasCheckConstraint(
                "CK_Ambulances_Owner",
                $"(\"OwnerType\" = '{AmbulanceOwnerType.Hospital}' AND \"ProviderId\" IS NULL) OR (\"OwnerType\" = '{AmbulanceOwnerType.External}' AND \"ProviderId\" IS NOT NULL)");
        });

        builder.HasOne(a => a.Provider).WithMany(p => p.Ambulances)
            .HasForeignKey(a => a.ProviderId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(a => a.DriverUser).WithMany()
            .HasForeignKey(a => a.DriverUserId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class AmbulanceRequestConfiguration : IEntityTypeConfiguration<AmbulanceRequest>
{
    public void Configure(EntityTypeBuilder<AmbulanceRequest> builder)
    {
        builder.Property(r => r.RequestNumber).IsRequired().HasMaxLength(30);
        builder.Property(r => r.TrackingTokenHash).IsRequired().HasMaxLength(64);
        builder.Property(r => r.Address).IsRequired().HasMaxLength(500);
        builder.Property(r => r.RequesterName).HasMaxLength(100);
        builder.Property(r => r.ContactPhone).HasMaxLength(30);
        builder.Property(r => r.Description).HasMaxLength(1000);
        builder.Property(r => r.StatusReason).HasMaxLength(500);

        builder.HasIndex(r => r.RequestNumber).IsUnique();
        builder.HasIndex(r => new { r.Status, r.CreatedAt });
        builder.HasIndex(r => r.PatientId);
        builder.HasIndex(r => r.PatientLinkedByUserId);

        builder.ToTable(t =>
        {
            t.HasCheckConstraint("CK_AmbulanceRequests_InjuredCount", "\"InjuredCount\" BETWEEN 0 AND 500");
            t.HasCheckConstraint("CK_AmbulanceRequests_Latitude", "\"Latitude\" BETWEEN -90 AND 90");
            t.HasCheckConstraint("CK_AmbulanceRequests_Longitude", "\"Longitude\" BETWEEN -180 AND 180");
        });

        builder.HasOne(r => r.Patient).WithMany()
            .HasForeignKey(r => r.PatientId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(r => r.PatientLinkedByUser).WithMany()
            .HasForeignKey(r => r.PatientLinkedByUserId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class AmbulanceAssignmentConfiguration : IEntityTypeConfiguration<AmbulanceAssignment>
{
    public void Configure(EntityTypeBuilder<AmbulanceAssignment> builder)
    {
        builder.Property(a => a.DeclineReason).HasMaxLength(500);
        builder.Property(a => a.ArrivalMethod).HasMaxLength(10);
        builder.Property(a => a.HospitalArrivalMethod).HasMaxLength(10);
        builder.Property(a => a.PickupConfirmedSource).HasMaxLength(12);
        builder.Property(a => a.PickupConfirmedByName).HasMaxLength(150);
        builder.Property(a => a.HandoverConfirmedSource).HasMaxLength(12);
        builder.Property(a => a.HandoverConfirmedByName).HasMaxLength(150);

        builder.HasIndex(a => a.RequestId);
        builder.HasIndex(a => a.AmbulanceId);
        builder.HasIndex(a => a.DriverUserId);
        builder.HasIndex(a => a.AssignedByUserId);

        // Database-level protection against double dispatch: one active assignment per request and per ambulance.
        builder.HasIndex(a => a.RequestId, "UX_AmbulanceAssignments_RequestId_Active")
            .IsUnique()
            .HasFilter("\"IsActive\" = TRUE");
        builder.HasIndex(a => a.AmbulanceId, "UX_AmbulanceAssignments_AmbulanceId_Active")
            .IsUnique()
            .HasFilter("\"IsActive\" = TRUE");

        builder.HasOne(a => a.Request).WithMany(r => r.Assignments)
            .HasForeignKey(a => a.RequestId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(a => a.Ambulance).WithMany(m => m.Assignments)
            .HasForeignKey(a => a.AmbulanceId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(a => a.DriverUser).WithMany()
            .HasForeignKey(a => a.DriverUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(a => a.AssignedByUser).WithMany()
            .HasForeignKey(a => a.AssignedByUserId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class AmbulanceLocationHistoryConfiguration : IEntityTypeConfiguration<AmbulanceLocationHistory>
{
    public void Configure(EntityTypeBuilder<AmbulanceLocationHistory> builder)
    {
        builder.HasIndex(h => new { h.AssignmentId, h.ReceivedAt });
        builder.HasIndex(h => h.AmbulanceId);

        builder.ToTable(t =>
        {
            t.HasCheckConstraint("CK_AmbulanceLocationHistory_Accuracy", "\"AccuracyMeters\" IS NULL OR \"AccuracyMeters\" >= 0");
            t.HasCheckConstraint("CK_AmbulanceLocationHistory_Latitude", "\"Latitude\" BETWEEN -90 AND 90");
            t.HasCheckConstraint("CK_AmbulanceLocationHistory_Longitude", "\"Longitude\" BETWEEN -180 AND 180");
            t.HasCheckConstraint("CK_AmbulanceLocationHistory_Speed", "\"SpeedKmh\" IS NULL OR \"SpeedKmh\" >= 0");
        });

        builder.HasOne(h => h.Assignment).WithMany(a => a.LocationHistory)
            .HasForeignKey(h => h.AssignmentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(h => h.Ambulance).WithMany()
            .HasForeignKey(h => h.AmbulanceId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class AmbulanceStatusHistoryConfiguration : IEntityTypeConfiguration<AmbulanceStatusHistory>
{
    public void Configure(EntityTypeBuilder<AmbulanceStatusHistory> builder)
    {
        builder.Property(h => h.FromStatus).HasMaxLength(30);
        builder.Property(h => h.ToStatus).IsRequired().HasMaxLength(30);
        builder.Property(h => h.Note).HasMaxLength(500);

        builder.HasIndex(h => new { h.RequestId, h.ChangedAt });
        builder.HasIndex(h => h.AmbulanceId);
        builder.HasIndex(h => h.ChangedByUserId);

        builder.ToTable(t => t.HasCheckConstraint(
            "CK_AmbulanceStatusHistory_Target", "\"RequestId\" IS NOT NULL OR \"AmbulanceId\" IS NOT NULL"));

        builder.HasOne(h => h.Request).WithMany(r => r.StatusHistory)
            .HasForeignKey(h => h.RequestId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(h => h.Ambulance).WithMany()
            .HasForeignKey(h => h.AmbulanceId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(h => h.ChangedByUser).WithMany()
            .HasForeignKey(h => h.ChangedByUserId).OnDelete(DeleteBehavior.Restrict);
    }
}
