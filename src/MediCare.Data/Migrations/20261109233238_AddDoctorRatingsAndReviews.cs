using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediCare.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDoctorRatingsAndReviews : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DoctorReviews",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DoctorId = table.Column<int>(type: "int", nullable: false),
                    PatientId = table.Column<int>(type: "int", nullable: true),
                    AppointmentId = table.Column<int>(type: "int", nullable: true),
                    Rating = table.Column<int>(type: "int", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Comment = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    PatientDisplayName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    IsVerifiedAppointment = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    IsDemo = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    ModerationStatus = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    HelpfulCount = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    DoctorResponse = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    DoctorResponseDateUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ModeratedByAdminId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    ModeratedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ModerationReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DoctorReviews", x => x.Id);
                    table.CheckConstraint("CK_DoctorReviews_Rating", "[Rating] >= 1 AND [Rating] <= 5");
                    table.ForeignKey(
                        name: "FK_DoctorReviews_Appointments_AppointmentId",
                        column: x => x.AppointmentId,
                        principalTable: "Appointments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DoctorReviews_AspNetUsers_ModeratedByAdminId",
                        column: x => x.ModeratedByAdminId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DoctorReviews_Doctors_DoctorId",
                        column: x => x.DoctorId,
                        principalTable: "Doctors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DoctorReviews_Patients_PatientId",
                        column: x => x.PatientId,
                        principalTable: "Patients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DoctorReviewHelpfulVotes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ReviewId = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    UserIpHash = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DoctorReviewHelpfulVotes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DoctorReviewHelpfulVotes_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DoctorReviewHelpfulVotes_DoctorReviews_ReviewId",
                        column: x => x.ReviewId,
                        principalTable: "DoctorReviews",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DoctorReviewHelpfulVotes_ReviewId_UserId",
                table: "DoctorReviewHelpfulVotes",
                columns: new[] { "ReviewId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DoctorReviewHelpfulVotes_UserId",
                table: "DoctorReviewHelpfulVotes",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_DoctorReviews_AppointmentId",
                table: "DoctorReviews",
                column: "AppointmentId",
                unique: true,
                filter: "[AppointmentId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_DoctorReviews_DoctorId_IsDemo_ModerationStatus",
                table: "DoctorReviews",
                columns: new[] { "DoctorId", "IsDemo", "ModerationStatus" });

            migrationBuilder.CreateIndex(
                name: "IX_DoctorReviews_DoctorId_ModerationStatus_CreatedAt",
                table: "DoctorReviews",
                columns: new[] { "DoctorId", "ModerationStatus", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_DoctorReviews_ModeratedByAdminId",
                table: "DoctorReviews",
                column: "ModeratedByAdminId");

            migrationBuilder.CreateIndex(
                name: "IX_DoctorReviews_PatientId_DoctorId",
                table: "DoctorReviews",
                columns: new[] { "PatientId", "DoctorId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DoctorReviewHelpfulVotes");

            migrationBuilder.DropTable(
                name: "DoctorReviews");
        }
    }
}
