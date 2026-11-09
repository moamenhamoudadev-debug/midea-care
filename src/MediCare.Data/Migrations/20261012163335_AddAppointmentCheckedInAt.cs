using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediCare.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAppointmentCheckedInAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Defensive idempotency: CheckedInAt column was introduced in 20261011130000_AddAppointmentCheckIn.
            // This defensive check prevents duplicate column errors during sequential branch reconciliation.
            migrationBuilder.Sql(@"
                IF NOT EXISTS (
                    SELECT 1 FROM sys.columns 
                    WHERE object_id = OBJECT_ID(N'[Appointments]') 
                    AND name = N'CheckedInAt'
                )
                BEGIN
                    ALTER TABLE [Appointments] ADD [CheckedInAt] datetime2 NULL;
                END
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Only drop the column if 20261011130000_AddAppointmentCheckIn is not in the migration history,
            // otherwise column removal is safely deferred to 20261011130000_AddAppointmentCheckIn.Down().
            migrationBuilder.Sql(@"
                IF NOT EXISTS (
                    SELECT 1 FROM [__EFMigrationsHistory]
                    WHERE [MigrationId] = N'20261011130000_AddAppointmentCheckIn'
                )
                BEGIN
                    IF EXISTS (
                        SELECT 1 FROM sys.columns 
                        WHERE object_id = OBJECT_ID(N'[Appointments]') 
                        AND name = N'CheckedInAt'
                    )
                    BEGIN
                        ALTER TABLE [Appointments] DROP COLUMN [CheckedInAt];
                    END
                END
            ");
        }
    }
}
