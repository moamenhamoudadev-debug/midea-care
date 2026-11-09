using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediCare.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTelehealthRoomSecret : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TelehealthRoomSecret",
                table: "Appointments",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            // Backfill existing Telehealth (Type = 2) appointments with unique cryptographically random secrets
            migrationBuilder.Sql(@"
                UPDATE [Appointments]
                SET [TelehealthRoomSecret] = LOWER(CONVERT(VARCHAR(32), HASHBYTES('SHA2_256', CONCAT(Id, '_', CreatedAt, '_', NEWID())), 2))
                WHERE [Type] = 2 AND [TelehealthRoomSecret] IS NULL;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TelehealthRoomSecret",
                table: "Appointments");
        }
    }
}
