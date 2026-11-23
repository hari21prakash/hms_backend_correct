using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HospitalManagement.Api.Migrations
{
    /// <summary>
    /// Additive only: nine NULLABLE columns on AmbulanceAssignments that record how and where an arrival was detected.
    /// Existing rows are untouched (all new values are NULL); nothing is dropped, rewritten or backfilled.
    /// </summary>
    public partial class AddAmbulanceAutoArrival : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ArrivalMethod",
                table: "AmbulanceAssignments",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "ArrivedAccuracyMeters",
                table: "AmbulanceAssignments",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "ArrivedLatitude",
                table: "AmbulanceAssignments",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "ArrivedLongitude",
                table: "AmbulanceAssignments",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HospitalArrivalMethod",
                table: "AmbulanceAssignments",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "HospitalArrivedAccuracyMeters",
                table: "AmbulanceAssignments",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "HospitalArrivedAt",
                table: "AmbulanceAssignments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "HospitalArrivedLatitude",
                table: "AmbulanceAssignments",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "HospitalArrivedLongitude",
                table: "AmbulanceAssignments",
                type: "double precision",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ArrivalMethod",
                table: "AmbulanceAssignments");

            migrationBuilder.DropColumn(
                name: "ArrivedAccuracyMeters",
                table: "AmbulanceAssignments");

            migrationBuilder.DropColumn(
                name: "ArrivedLatitude",
                table: "AmbulanceAssignments");

            migrationBuilder.DropColumn(
                name: "ArrivedLongitude",
                table: "AmbulanceAssignments");

            migrationBuilder.DropColumn(
                name: "HospitalArrivalMethod",
                table: "AmbulanceAssignments");

            migrationBuilder.DropColumn(
                name: "HospitalArrivedAccuracyMeters",
                table: "AmbulanceAssignments");

            migrationBuilder.DropColumn(
                name: "HospitalArrivedAt",
                table: "AmbulanceAssignments");

            migrationBuilder.DropColumn(
                name: "HospitalArrivedLatitude",
                table: "AmbulanceAssignments");

            migrationBuilder.DropColumn(
                name: "HospitalArrivedLongitude",
                table: "AmbulanceAssignments");
        }
    }
}
