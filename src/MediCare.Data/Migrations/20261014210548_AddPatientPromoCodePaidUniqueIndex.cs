using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediCare.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPatientPromoCodePaidUniqueIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Appointments_Patient_PromoCode_Paid",
                table: "Appointments",
                columns: new[] { "PatientId", "PromoCode" },
                unique: true,
                filter: "[PromoCode] IS NOT NULL AND [PaymentStatus] = 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Appointments_Patient_PromoCode_Paid",
                table: "Appointments");
        }
    }
}
