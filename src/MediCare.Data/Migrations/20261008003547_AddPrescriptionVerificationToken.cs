using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediCare.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPrescriptionVerificationToken : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "DispensedAt",
                table: "Prescriptions",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DispensedByUserId",
                table: "Prescriptions",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDispensed",
                table: "Prescriptions",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "PharmacyNotes",
                table: "Prescriptions",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VerificationToken",
                table: "Prescriptions",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            // Backfill existing prescriptions with 128-bit cryptographic tokens via CRYPT_GEN_RANDOM(16)
            migrationBuilder.Sql(@"
                UPDATE [Prescriptions] 
                SET [VerificationToken] = LOWER(CONVERT(varchar(32), CRYPT_GEN_RANDOM(16), 2))
                WHERE [VerificationToken] IS NULL;

                UPDATE [Prescriptions]
                SET [IsDispensed] = 1
                WHERE [Notes] LIKE '%\[DISPENSED:%' ESCAPE '\';
            ");

            migrationBuilder.AlterColumn<string>(
                name: "VerificationToken",
                table: "Prescriptions",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(64)",
                oldMaxLength: 64,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Prescriptions_DispensedByUserId",
                table: "Prescriptions",
                column: "DispensedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Prescriptions_VerificationToken",
                table: "Prescriptions",
                column: "VerificationToken",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Prescriptions_AspNetUsers_DispensedByUserId",
                table: "Prescriptions",
                column: "DispensedByUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Prescriptions_AspNetUsers_DispensedByUserId",
                table: "Prescriptions");

            migrationBuilder.DropIndex(
                name: "IX_Prescriptions_DispensedByUserId",
                table: "Prescriptions");

            migrationBuilder.DropIndex(
                name: "IX_Prescriptions_VerificationToken",
                table: "Prescriptions");

            migrationBuilder.DropColumn(
                name: "DispensedAt",
                table: "Prescriptions");

            migrationBuilder.DropColumn(
                name: "DispensedByUserId",
                table: "Prescriptions");

            migrationBuilder.DropColumn(
                name: "IsDispensed",
                table: "Prescriptions");

            migrationBuilder.DropColumn(
                name: "PharmacyNotes",
                table: "Prescriptions");

            migrationBuilder.DropColumn(
                name: "VerificationToken",
                table: "Prescriptions");
        }
    }
}
