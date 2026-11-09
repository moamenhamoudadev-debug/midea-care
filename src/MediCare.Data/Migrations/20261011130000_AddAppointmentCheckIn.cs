using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediCare.Data.Migrations
{
    public partial class AddAppointmentCheckIn : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Defensive idempotency: Safely add CheckedInAt only if it does not already exist.
            // This prevents duplicate column errors on databases where 20261012163335_AddAppointmentCheckedInAt
            // was applied previously in develop before branch reconciliation.
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

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Only drop CheckedInAt if the later check-in migration (20261012163335_AddAppointmentCheckedInAt)
            // is NOT in __EFMigrationsHistory. If 20261012163335 is still recorded, the column must be preserved.
            migrationBuilder.Sql(@"
                IF NOT EXISTS (
                    SELECT 1 FROM [__EFMigrationsHistory]
                    WHERE [MigrationId] = N'20261012163335_AddAppointmentCheckedInAt'
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
