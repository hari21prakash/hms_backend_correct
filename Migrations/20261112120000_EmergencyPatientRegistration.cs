using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HospitalManagement.Api.Migrations
{
    /// <inheritdoc />
    public partial class EmergencyPatientRegistration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Sequence for emergency patient numbers (EM-yyyy-nnnn).
            migrationBuilder.CreateSequence(
                name: "emergency_patient_number_seq");

            // Relaxation only: an emergency admission may wait for a doctor. Existing rows keep their doctor.
            migrationBuilder.AlterColumn<Guid>(
                name: "DoctorId",
                table: "Admissions",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            // Purely additive Patients columns. Existing patients become IsEmergencyRegistration = false / Identified.
            migrationBuilder.AddColumn<int>(
                name: "ApproximateAge",
                table: "Patients",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EmergencyNotes",
                table: "Patients",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EmergencyType",
                table: "Patients",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IdentificationStatus",
                table: "Patients",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "Identified");

            migrationBuilder.AddColumn<bool>(
                name: "IsEmergencyRegistration",
                table: "Patients",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "RegisteredByUserId",
                table: "Patients",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Patients_RegisteredByUserId",
                table: "Patients",
                column: "RegisteredByUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_Patients_Users_RegisteredByUserId",
                table: "Patients",
                column: "RegisteredByUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Patients_Users_RegisteredByUserId",
                table: "Patients");

            migrationBuilder.DropIndex(
                name: "IX_Patients_RegisteredByUserId",
                table: "Patients");

            migrationBuilder.DropColumn(name: "ApproximateAge", table: "Patients");
            migrationBuilder.DropColumn(name: "EmergencyNotes", table: "Patients");
            migrationBuilder.DropColumn(name: "EmergencyType", table: "Patients");
            migrationBuilder.DropColumn(name: "IdentificationStatus", table: "Patients");
            migrationBuilder.DropColumn(name: "IsEmergencyRegistration", table: "Patients");
            migrationBuilder.DropColumn(name: "RegisteredByUserId", table: "Patients");

            // Admissions without a doctor cannot exist in the old schema: remove them (and their dependants) first.
            migrationBuilder.Sql(@"DELETE FROM ""BedAssignments"" WHERE ""AdmissionId"" IN (SELECT ""Id"" FROM ""Admissions"" WHERE ""DoctorId"" IS NULL);");
            migrationBuilder.Sql(@"UPDATE ""Beds"" SET ""Status"" = 'Available' WHERE ""Status"" = 'Occupied' AND NOT EXISTS (SELECT 1 FROM ""BedAssignments"" ba WHERE ba.""BedId"" = ""Beds"".""Id"" AND ba.""ReleasedAt"" IS NULL);");
            migrationBuilder.Sql(@"DELETE FROM ""Admissions"" WHERE ""DoctorId"" IS NULL;");

            migrationBuilder.AlterColumn<Guid>(
                name: "DoctorId",
                table: "Admissions",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.DropSequence(
                name: "emergency_patient_number_seq");
        }
    }
}
