using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediCare.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddEnterpriseRealTimeNotificationSystem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ActionUrl",
                table: "Notifications",
                type: "nvarchar(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AppointmentId",
                table: "Notifications",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EventReference",
                table: "Notifications",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PaymentAttemptId",
                table: "Notifications",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReadAtUtc",
                table: "Notifications",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RefundId",
                table: "Notifications",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Type",
                table: "Notifications",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "General");

            migrationBuilder.CreateTable(
                name: "NotificationOutbox",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    NotificationId = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    Channel = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    RecipientEmail = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    Subject = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    PayloadJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false, defaultValue: "Pending"),
                    RetryCount = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    MaxRetries = table.Column<int>(type: "int", nullable: false, defaultValue: 3),
                    LastError = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ProcessedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationOutbox", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NotificationOutbox_Notifications_NotificationId",
                        column: x => x.NotificationId,
                        principalTable: "Notifications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_AppointmentId",
                table: "Notifications",
                column: "AppointmentId");

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_PaymentAttemptId",
                table: "Notifications",
                column: "PaymentAttemptId");

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_RefundId",
                table: "Notifications",
                column: "RefundId");

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_UserId_CreatedAt",
                table: "Notifications",
                columns: new[] { "UserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_UserId_EventReference",
                table: "Notifications",
                columns: new[] { "UserId", "EventReference" },
                filter: "[EventReference] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_NotificationOutbox_NotificationId",
                table: "NotificationOutbox",
                column: "NotificationId");

            migrationBuilder.CreateIndex(
                name: "IX_NotificationOutbox_Status",
                table: "NotificationOutbox",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_NotificationOutbox_Status_CreatedAt",
                table: "NotificationOutbox",
                columns: new[] { "Status", "CreatedAt" });

            migrationBuilder.AddForeignKey(
                name: "FK_Notifications_Appointments_AppointmentId",
                table: "Notifications",
                column: "AppointmentId",
                principalTable: "Appointments",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Notifications_PaymentAttempts_PaymentAttemptId",
                table: "Notifications",
                column: "PaymentAttemptId",
                principalTable: "PaymentAttempts",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Notifications_Refunds_RefundId",
                table: "Notifications",
                column: "RefundId",
                principalTable: "Refunds",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Notifications_Appointments_AppointmentId",
                table: "Notifications");

            migrationBuilder.DropForeignKey(
                name: "FK_Notifications_PaymentAttempts_PaymentAttemptId",
                table: "Notifications");

            migrationBuilder.DropForeignKey(
                name: "FK_Notifications_Refunds_RefundId",
                table: "Notifications");

            migrationBuilder.DropTable(
                name: "NotificationOutbox");

            migrationBuilder.DropIndex(
                name: "IX_Notifications_AppointmentId",
                table: "Notifications");

            migrationBuilder.DropIndex(
                name: "IX_Notifications_PaymentAttemptId",
                table: "Notifications");

            migrationBuilder.DropIndex(
                name: "IX_Notifications_RefundId",
                table: "Notifications");

            migrationBuilder.DropIndex(
                name: "IX_Notifications_UserId_CreatedAt",
                table: "Notifications");

            migrationBuilder.DropIndex(
                name: "IX_Notifications_UserId_EventReference",
                table: "Notifications");

            migrationBuilder.DropColumn(
                name: "ActionUrl",
                table: "Notifications");

            migrationBuilder.DropColumn(
                name: "AppointmentId",
                table: "Notifications");

            migrationBuilder.DropColumn(
                name: "EventReference",
                table: "Notifications");

            migrationBuilder.DropColumn(
                name: "PaymentAttemptId",
                table: "Notifications");

            migrationBuilder.DropColumn(
                name: "ReadAtUtc",
                table: "Notifications");

            migrationBuilder.DropColumn(
                name: "RefundId",
                table: "Notifications");

            migrationBuilder.DropColumn(
                name: "Type",
                table: "Notifications");
        }
    }
}
