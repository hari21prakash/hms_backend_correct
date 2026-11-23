using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HospitalManagement.Api.Migrations
{
    /// <summary>
    /// Additive only: six NULLABLE columns on AmbulanceAssignments recording who confirmed the patient pick-up and the handover
    /// (driver or dispatch), and their name at the time. Existing rows are untouched (all new values are NULL).
    /// </summary>
    public partial class AddAmbulanceTripConfirmation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "HandoverConfirmedByName",
                table: "AmbulanceAssignments",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "HandoverConfirmedByUserId",
                table: "AmbulanceAssignments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HandoverConfirmedSource",
                table: "AmbulanceAssignments",
                type: "character varying(12)",
                maxLength: 12,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PickupConfirmedByName",
                table: "AmbulanceAssignments",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PickupConfirmedByUserId",
                table: "AmbulanceAssignments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PickupConfirmedSource",
                table: "AmbulanceAssignments",
                type: "character varying(12)",
                maxLength: 12,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "HandoverConfirmedByName",
                table: "AmbulanceAssignments");

            migrationBuilder.DropColumn(
                name: "HandoverConfirmedByUserId",
                table: "AmbulanceAssignments");

            migrationBuilder.DropColumn(
                name: "HandoverConfirmedSource",
                table: "AmbulanceAssignments");

            migrationBuilder.DropColumn(
                name: "PickupConfirmedByName",
                table: "AmbulanceAssignments");

            migrationBuilder.DropColumn(
                name: "PickupConfirmedByUserId",
                table: "AmbulanceAssignments");

            migrationBuilder.DropColumn(
                name: "PickupConfirmedSource",
                table: "AmbulanceAssignments");
        }
    }
}
