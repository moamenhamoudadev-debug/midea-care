using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediCare.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase3PerformanceIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Doctors_Approved_Search",
                table: "Doctors",
                columns: new[] { "SpecializationId", "Governorate" },
                filter: "[IsApproved] = 1")
                .Annotation("SqlServer:Include", new[] { "ConsultationFee", "SlotDurationMinutes" });

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_Doctor_Status_Date",
                table: "Appointments",
                columns: new[] { "DoctorId", "Status", "AppointmentDate", "StartTime" },
                descending: new[] { false, false, true, true })
                .Annotation("SqlServer:Include", new[] { "PaymentStatus", "ConsultationFee", "Type", "CheckedInAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_Patient_AppointmentDate",
                table: "Appointments",
                columns: new[] { "PatientId", "AppointmentDate", "StartTime" },
                descending: new[] { false, true, true })
                .Annotation("SqlServer:Include", new[] { "Status", "PaymentStatus", "ConsultationFee", "Type" });

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_PendingReminders",
                table: "Appointments",
                columns: new[] { "AppointmentDate", "StartTime" },
                filter: "[ReminderSent] = 0")
                .Annotation("SqlServer:Include", new[] { "DoctorId", "PatientId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Doctors_Approved_Search",
                table: "Doctors");

            migrationBuilder.DropIndex(
                name: "IX_Appointments_Doctor_Status_Date",
                table: "Appointments");

            migrationBuilder.DropIndex(
                name: "IX_Appointments_Patient_AppointmentDate",
                table: "Appointments");

            migrationBuilder.DropIndex(
                name: "IX_Appointments_PendingReminders",
                table: "Appointments");
        }
    }
}
