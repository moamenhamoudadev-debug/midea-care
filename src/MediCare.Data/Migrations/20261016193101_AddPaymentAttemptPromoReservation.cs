using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediCare.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentAttemptPromoReservation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PatientId",
                table: "PaymentAttempts",
                type: "int",
                nullable: true);

            migrationBuilder.Sql(@"
                UPDATE pa
                SET PatientId = a.PatientId
                FROM PaymentAttempts pa
                INNER JOIN Appointments a ON a.Id = pa.AppointmentId;

                WITH RankedPendingPromos AS (
                    SELECT Id, ROW_NUMBER() OVER (PARTITION BY PatientId, PromoCode ORDER BY CreatedAt, Id) AS RowNumber
                    FROM PaymentAttempts
                    WHERE PromoCode IS NOT NULL AND Status = N'Pending'
                )
                UPDATE pa
                SET Status = N'Expired', CompletedAt = GETUTCDATE()
                FROM PaymentAttempts pa
                INNER JOIN RankedPendingPromos r ON r.Id = pa.Id
                WHERE r.RowNumber > 1;
            ");

            migrationBuilder.AlterColumn<int>(
                name: "PatientId",
                table: "PaymentAttempts",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PaymentAttempts_PatientPromo_ActiveOrSucceeded",
                table: "PaymentAttempts",
                columns: new[] { "PatientId", "PromoCode" },
                unique: true,
                filter: "[PromoCode] IS NOT NULL AND [Status] IN (N'Pending', N'Succeeded')");

            migrationBuilder.AddForeignKey(
                name: "FK_PaymentAttempts_Patients_PatientId",
                table: "PaymentAttempts",
                column: "PatientId",
                principalTable: "Patients",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PaymentAttempts_Patients_PatientId",
                table: "PaymentAttempts");

            migrationBuilder.DropIndex(
                name: "IX_PaymentAttempts_PatientPromo_ActiveOrSucceeded",
                table: "PaymentAttempts");

            migrationBuilder.DropColumn(
                name: "PatientId",
                table: "PaymentAttempts");
        }
    }
}
