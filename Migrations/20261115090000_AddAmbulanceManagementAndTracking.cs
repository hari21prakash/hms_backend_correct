using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HospitalManagement.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddAmbulanceManagementAndTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateSequence(
                name: "ambulance_request_number_seq");

            migrationBuilder.CreateTable(
                name: "AmbulanceProviders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Address = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    ContactPerson = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    LicenseNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Phone = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    ReviewedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReviewedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    StatusReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AmbulanceProviders", x => x.Id);
                    table.CheckConstraint("CK_AmbulanceProviders_Review", "\"Status\" = 'Pending' OR (\"ReviewedByUserId\" IS NOT NULL AND \"ReviewedAt\" IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_AmbulanceProviders_Users_ReviewedByUserId",
                        column: x => x.ReviewedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AmbulanceProviderUsers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    ProviderId = table.Column<Guid>(type: "uuid", nullable: false),
                    Role = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AmbulanceProviderUsers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AmbulanceProviderUsers_AmbulanceProviders_ProviderId",
                        column: x => x.ProviderId,
                        principalTable: "AmbulanceProviders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AmbulanceProviderUsers_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Ambulances",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Capacity = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CurrentLatitude = table.Column<double>(type: "double precision", nullable: true),
                    CurrentLongitude = table.Column<double>(type: "double precision", nullable: true),
                    DriverUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    LastLocationAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LocationAccuracyMeters = table.Column<double>(type: "double precision", nullable: true),
                    LocationSpeedKmh = table.Column<double>(type: "double precision", nullable: true),
                    Make = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Model = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    OwnerType = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    ProviderId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    VehicleNumber = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Year = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Ambulances", x => x.Id);
                    table.CheckConstraint("CK_Ambulances_Capacity", "\"Capacity\" >= 1");
                    table.CheckConstraint("CK_Ambulances_Latitude", "\"CurrentLatitude\" IS NULL OR \"CurrentLatitude\" BETWEEN -90 AND 90");
                    table.CheckConstraint("CK_Ambulances_Longitude", "\"CurrentLongitude\" IS NULL OR \"CurrentLongitude\" BETWEEN -180 AND 180");
                    table.CheckConstraint("CK_Ambulances_Owner", "(\"OwnerType\" = 'Hospital' AND \"ProviderId\" IS NULL) OR (\"OwnerType\" = 'External' AND \"ProviderId\" IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_Ambulances_AmbulanceProviders_ProviderId",
                        column: x => x.ProviderId,
                        principalTable: "AmbulanceProviders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Ambulances_Users_DriverUserId",
                        column: x => x.DriverUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AmbulanceRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Address = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    ContactPhone = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    EmergencyType = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    InjuredCount = table.Column<int>(type: "integer", nullable: false),
                    Latitude = table.Column<double>(type: "double precision", nullable: false),
                    Longitude = table.Column<double>(type: "double precision", nullable: false),
                    PatientId = table.Column<Guid>(type: "uuid", nullable: true),
                    PatientLinkedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PatientLinkedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    RequestNumber = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    RequesterName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    StatusReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    TrackingTokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AmbulanceRequests", x => x.Id);
                    table.CheckConstraint("CK_AmbulanceRequests_InjuredCount", "\"InjuredCount\" BETWEEN 0 AND 500");
                    table.CheckConstraint("CK_AmbulanceRequests_Latitude", "\"Latitude\" BETWEEN -90 AND 90");
                    table.CheckConstraint("CK_AmbulanceRequests_Longitude", "\"Longitude\" BETWEEN -180 AND 180");
                    table.ForeignKey(
                        name: "FK_AmbulanceRequests_Patients_PatientId",
                        column: x => x.PatientId,
                        principalTable: "Patients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AmbulanceRequests_Users_PatientLinkedByUserId",
                        column: x => x.PatientLinkedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AmbulanceAssignments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AcceptedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AmbulanceId = table.Column<Guid>(type: "uuid", nullable: false),
                    ArrivedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AssignedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AssignedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DeclineReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    DriverUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    EndedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    PickedUpAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AmbulanceAssignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AmbulanceAssignments_AmbulanceRequests_RequestId",
                        column: x => x.RequestId,
                        principalTable: "AmbulanceRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AmbulanceAssignments_Ambulances_AmbulanceId",
                        column: x => x.AmbulanceId,
                        principalTable: "Ambulances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AmbulanceAssignments_Users_DriverUserId",
                        column: x => x.DriverUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AmbulanceAssignments_Users_AssignedByUserId",
                        column: x => x.AssignedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AmbulanceLocationHistories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccuracyMeters = table.Column<double>(type: "double precision", nullable: true),
                    AmbulanceId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssignmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Heading = table.Column<double>(type: "double precision", nullable: true),
                    Latitude = table.Column<double>(type: "double precision", nullable: false),
                    Longitude = table.Column<double>(type: "double precision", nullable: false),
                    ReceivedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SpeedKmh = table.Column<double>(type: "double precision", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AmbulanceLocationHistories", x => x.Id);
                    table.CheckConstraint("CK_AmbulanceLocationHistory_Accuracy", "\"AccuracyMeters\" IS NULL OR \"AccuracyMeters\" >= 0");
                    table.CheckConstraint("CK_AmbulanceLocationHistory_Latitude", "\"Latitude\" BETWEEN -90 AND 90");
                    table.CheckConstraint("CK_AmbulanceLocationHistory_Longitude", "\"Longitude\" BETWEEN -180 AND 180");
                    table.CheckConstraint("CK_AmbulanceLocationHistory_Speed", "\"SpeedKmh\" IS NULL OR \"SpeedKmh\" >= 0");
                    table.ForeignKey(
                        name: "FK_AmbulanceLocationHistories_AmbulanceAssignments_AssignmentId",
                        column: x => x.AssignmentId,
                        principalTable: "AmbulanceAssignments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AmbulanceLocationHistories_Ambulances_AmbulanceId",
                        column: x => x.AmbulanceId,
                        principalTable: "Ambulances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AmbulanceStatusHistories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AmbulanceId = table.Column<Guid>(type: "uuid", nullable: true),
                    ChangedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ChangedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    FromStatus = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    Note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: true),
                    Scope = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    ToStatus = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AmbulanceStatusHistories", x => x.Id);
                    table.CheckConstraint("CK_AmbulanceStatusHistory_Target", "\"RequestId\" IS NOT NULL OR \"AmbulanceId\" IS NOT NULL");
                    table.ForeignKey(
                        name: "FK_AmbulanceStatusHistories_AmbulanceRequests_RequestId",
                        column: x => x.RequestId,
                        principalTable: "AmbulanceRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AmbulanceStatusHistories_Ambulances_AmbulanceId",
                        column: x => x.AmbulanceId,
                        principalTable: "Ambulances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AmbulanceStatusHistories_Users_ChangedByUserId",
                        column: x => x.ChangedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AmbulanceProviders_Name",
                table: "AmbulanceProviders",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AmbulanceProviders_LicenseNumber",
                table: "AmbulanceProviders",
                column: "LicenseNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AmbulanceProviders_Status",
                table: "AmbulanceProviders",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_AmbulanceProviders_ReviewedByUserId",
                table: "AmbulanceProviders",
                column: "ReviewedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_AmbulanceProviderUsers_UserId",
                table: "AmbulanceProviderUsers",
                column: "UserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AmbulanceProviderUsers_ProviderId",
                table: "AmbulanceProviderUsers",
                column: "ProviderId");

            migrationBuilder.CreateIndex(
                name: "IX_Ambulances_VehicleNumber",
                table: "Ambulances",
                column: "VehicleNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Ambulances_Status",
                table: "Ambulances",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Ambulances_ProviderId",
                table: "Ambulances",
                column: "ProviderId");

            migrationBuilder.CreateIndex(
                name: "IX_Ambulances_DriverUserId",
                table: "Ambulances",
                column: "DriverUserId");

            migrationBuilder.CreateIndex(
                name: "UX_Ambulances_DriverUserId",
                table: "Ambulances",
                column: "DriverUserId",
                unique: true,
                filter: "\"DriverUserId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AmbulanceRequests_RequestNumber",
                table: "AmbulanceRequests",
                column: "RequestNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AmbulanceRequests_Status_CreatedAt",
                table: "AmbulanceRequests",
                columns: new[] { "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AmbulanceRequests_PatientId",
                table: "AmbulanceRequests",
                column: "PatientId");

            migrationBuilder.CreateIndex(
                name: "IX_AmbulanceRequests_PatientLinkedByUserId",
                table: "AmbulanceRequests",
                column: "PatientLinkedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_AmbulanceAssignments_RequestId",
                table: "AmbulanceAssignments",
                column: "RequestId");

            migrationBuilder.CreateIndex(
                name: "IX_AmbulanceAssignments_AmbulanceId",
                table: "AmbulanceAssignments",
                column: "AmbulanceId");

            migrationBuilder.CreateIndex(
                name: "IX_AmbulanceAssignments_DriverUserId",
                table: "AmbulanceAssignments",
                column: "DriverUserId");

            migrationBuilder.CreateIndex(
                name: "IX_AmbulanceAssignments_AssignedByUserId",
                table: "AmbulanceAssignments",
                column: "AssignedByUserId");

            migrationBuilder.CreateIndex(
                name: "UX_AmbulanceAssignments_RequestId_Active",
                table: "AmbulanceAssignments",
                column: "RequestId",
                unique: true,
                filter: "\"IsActive\" = TRUE");

            migrationBuilder.CreateIndex(
                name: "UX_AmbulanceAssignments_AmbulanceId_Active",
                table: "AmbulanceAssignments",
                column: "AmbulanceId",
                unique: true,
                filter: "\"IsActive\" = TRUE");

            migrationBuilder.CreateIndex(
                name: "IX_AmbulanceLocationHistories_AssignmentId_ReceivedAt",
                table: "AmbulanceLocationHistories",
                columns: new[] { "AssignmentId", "ReceivedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AmbulanceLocationHistories_AmbulanceId",
                table: "AmbulanceLocationHistories",
                column: "AmbulanceId");

            migrationBuilder.CreateIndex(
                name: "IX_AmbulanceStatusHistories_RequestId_ChangedAt",
                table: "AmbulanceStatusHistories",
                columns: new[] { "RequestId", "ChangedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AmbulanceStatusHistories_AmbulanceId",
                table: "AmbulanceStatusHistories",
                column: "AmbulanceId");

            migrationBuilder.CreateIndex(
                name: "IX_AmbulanceStatusHistories_ChangedByUserId",
                table: "AmbulanceStatusHistories",
                column: "ChangedByUserId");

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AmbulanceStatusHistories");

            migrationBuilder.DropTable(
                name: "AmbulanceLocationHistories");

            migrationBuilder.DropTable(
                name: "AmbulanceAssignments");

            migrationBuilder.DropTable(
                name: "AmbulanceRequests");

            migrationBuilder.DropTable(
                name: "Ambulances");

            migrationBuilder.DropTable(
                name: "AmbulanceProviderUsers");

            migrationBuilder.DropTable(
                name: "AmbulanceProviders");

            migrationBuilder.DropSequence(
                name: "ambulance_request_number_seq");
        }
    }
}
