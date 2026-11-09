using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediCare.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentStructuredReconciliationAndMethods : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PaymentMethodDetail",
                table: "PaymentAttempts",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PaymentMethodType",
                table: "PaymentAttempts",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReconciledAt",
                table: "PaymentAttempts",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReconciliationReason",
                table: "PaymentAttempts",
                type: "nvarchar(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReconciliationStatus",
                table: "PaymentAttempts",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PaymentMethodDetail",
                table: "PaymentAttempts");

            migrationBuilder.DropColumn(
                name: "PaymentMethodType",
                table: "PaymentAttempts");

            migrationBuilder.DropColumn(
                name: "ReconciledAt",
                table: "PaymentAttempts");

            migrationBuilder.DropColumn(
                name: "ReconciliationReason",
                table: "PaymentAttempts");

            migrationBuilder.DropColumn(
                name: "ReconciliationStatus",
                table: "PaymentAttempts");
        }
    }
}
