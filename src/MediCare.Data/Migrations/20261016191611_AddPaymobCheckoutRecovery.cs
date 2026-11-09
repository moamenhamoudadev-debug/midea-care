using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediCare.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymobCheckoutRecovery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CheckoutUrl",
                table: "PaymentAttempts",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ExpiresAt",
                table: "PaymentAttempts",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.CreateIndex(
                name: "IX_PaymentAttempts_OnePendingPerAppointment",
                table: "PaymentAttempts",
                column: "AppointmentId",
                unique: true,
                filter: "[Status] = N'Pending'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PaymentAttempts_OnePendingPerAppointment",
                table: "PaymentAttempts");

            migrationBuilder.DropColumn(
                name: "CheckoutUrl",
                table: "PaymentAttempts");

            migrationBuilder.DropColumn(
                name: "ExpiresAt",
                table: "PaymentAttempts");
        }
    }
}
