using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediCare.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymobPaymentInfrastructure : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PaymentProvider",
                table: "Appointments",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PaymentAttempts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AppointmentId = table.Column<int>(type: "int", nullable: false),
                    MerchantReference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ProviderOrderId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ProviderTransactionId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    AmountCents = table.Column<long>(type: "bigint", nullable: false),
                    DiscountAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    PromoCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentAttempts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PaymentAttempts_Appointments_AppointmentId",
                        column: x => x.AppointmentId,
                        principalTable: "Appointments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentAttempts_AppointmentId_Status",
                table: "PaymentAttempts",
                columns: new[] { "AppointmentId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentAttempts_MerchantReference",
                table: "PaymentAttempts",
                column: "MerchantReference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PaymentAttempts_ProviderTransactionId",
                table: "PaymentAttempts",
                column: "ProviderTransactionId",
                unique: true,
                filter: "[ProviderTransactionId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PaymentAttempts");

            migrationBuilder.DropColumn(
                name: "PaymentProvider",
                table: "Appointments");
        }
    }
}
