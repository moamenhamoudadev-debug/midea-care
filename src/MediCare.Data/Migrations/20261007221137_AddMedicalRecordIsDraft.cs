using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediCare.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMedicalRecordIsDraft : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsDraft",
                table: "MedicalRecords",
                type: "bit",
                nullable: false,
                defaultValue: false);

            // Data Migration: Set IsDraft = true for existing pre-visit patient uploads
            // where diagnosis was placeholder, appointment is not completed, and no prescription exists.
            migrationBuilder.Sql(@"
                UPDATE m
                SET m.IsDraft = 1
                FROM MedicalRecords m
                INNER JOIN Appointments a ON m.AppointmentId = a.Id
                WHERE (m.Diagnosis = 'Patient Uploaded Diagnostic / Laboratory Files' OR m.Diagnosis IS NULL OR m.Diagnosis = '')
                  AND a.Status <> 2
                  AND NOT EXISTS (SELECT 1 FROM Prescriptions p WHERE p.MedicalRecordId = m.Id);
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsDraft",
                table: "MedicalRecords");
        }
    }
}
