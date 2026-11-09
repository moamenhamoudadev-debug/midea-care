using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediCare.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAppointmentPaymentDetailsAndBackfill : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "DiscountAmount",
                table: "Appointments",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PaidAmount",
                table: "Appointments",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PaidAt",
                table: "Appointments",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PromoCode",
                table: "Appointments",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TransactionReference",
                table: "Appointments",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            // 2. Best-Effort Data Backfill for Existing Paid Appointments (PaymentStatus == 1):
            // - Extracts Ref, PaidAmount, PromoCode, and DiscountAmount from 'Payment Received' notifications when available.
            // - Enforces safe trimming, pattern matching (LIKE 'TXN-%'), and culture-invariant numeric conversion.
            // - Fallback for legacy/seeded rows: PaidAmount = ConsultationFee, DiscountAmount = 0.00,
            //   deterministic UTC PaidAt, and reference generated via CONVERT without culture-sensitive FORMAT().
            // - Concurrency / duplicate safety: Deterministically deduplicates any collisions before index creation.
            migrationBuilder.Sql(@"
                WITH NotificationPaymentData AS (
                    SELECT 
                        a.Id AS AppointmentId,
                        n.CreatedAt AS NotifCreatedAt,
                        CASE 
                            WHEN CHARINDEX('Ref: ', n.Message) > 0 
                                 AND LTRIM(RTRIM(SUBSTRING(n.Message, CHARINDEX('Ref: ', n.Message) + 5, 50))) LIKE 'TXN-%'
                            THEN LTRIM(RTRIM(SUBSTRING(n.Message, CHARINDEX('Ref: ', n.Message) + 5, 50)))
                            ELSE NULL 
                        END AS ExtractedRef,
                        CASE 
                            WHEN CHARINDEX('Payment of $', n.Message) > 0 AND CHARINDEX(' for appointment #', n.Message) > CHARINDEX('Payment of $', n.Message)
                            THEN TRY_CAST(REPLACE(LTRIM(RTRIM(SUBSTRING(n.Message, CHARINDEX('Payment of $', n.Message) + 12, CHARINDEX(' for appointment #', n.Message) - (CHARINDEX('Payment of $', n.Message) + 12)))), ',', '.') AS DECIMAL(18,2))
                            ELSE NULL 
                        END AS ExtractedPaidAmount,
                        CASE 
                            WHEN CHARINDEX('(Promo ', n.Message) > 0 AND CHARINDEX(' applied: -$', n.Message) > CHARINDEX('(Promo ', n.Message)
                            THEN LTRIM(RTRIM(SUBSTRING(n.Message, CHARINDEX('(Promo ', n.Message) + 7, CHARINDEX(' applied: -$', n.Message) - (CHARINDEX('(Promo ', n.Message) + 7))))
                            ELSE NULL 
                        END AS ExtractedPromo,
                        CASE 
                            WHEN CHARINDEX('applied: -$', n.Message) > 0 AND CHARINDEX(')', n.Message, CHARINDEX('applied: -$', n.Message)) > CHARINDEX('applied: -$', n.Message)
                            THEN TRY_CAST(REPLACE(LTRIM(RTRIM(SUBSTRING(n.Message, CHARINDEX('applied: -$', n.Message) + 11, CHARINDEX(')', n.Message, CHARINDEX('applied: -$', n.Message)) - (CHARINDEX('applied: -$', n.Message) + 11)))), ',', '.') AS DECIMAL(18,2))
                            ELSE 0.00 
                        END AS ExtractedDiscountAmount,
                        ROW_NUMBER() OVER(PARTITION BY a.Id ORDER BY n.CreatedAt DESC) AS rn
                    FROM [Appointments] a
                    INNER JOIN [Notifications] n 
                        ON n.Title = 'Payment Received' 
                        AND n.Message LIKE '%for appointment #' + CAST(a.Id AS NVARCHAR(10)) + ' %'
                    WHERE a.PaymentStatus = 1
                )
                UPDATE a
                SET 
                    a.PaidAt = ISNULL(npd.NotifCreatedAt, ISNULL(a.UpdatedAt, a.CreatedAt)),
                    a.PaidAmount = ISNULL(npd.ExtractedPaidAmount, a.ConsultationFee),
                    a.DiscountAmount = ISNULL(npd.ExtractedDiscountAmount, 0.00),
                    a.PromoCode = npd.ExtractedPromo,
                    a.TransactionReference = ISNULL(npd.ExtractedRef, 
                        'TXN-' + CONVERT(varchar(8), ISNULL(a.UpdatedAt, a.CreatedAt), 112) 
                               + REPLACE(CONVERT(varchar(8), ISNULL(a.UpdatedAt, a.CreatedAt), 108), ':', '') 
                               + '-' + RIGHT('0000' + CAST(a.Id AS VARCHAR(10)), 4))
                FROM [Appointments] a
                LEFT JOIN NotificationPaymentData npd ON a.Id = npd.AppointmentId AND npd.rn = 1
                WHERE a.PaymentStatus = 1;

                -- Pre-index deduplication guard: resolve any unexpected duplicates deterministically
                WHILE EXISTS (
                    SELECT [TransactionReference] 
                    FROM [Appointments] 
                    WHERE [TransactionReference] IS NOT NULL 
                    GROUP BY [TransactionReference] 
                    HAVING COUNT(*) > 1
                )
                BEGIN
                    WITH Duplicates AS (
                        SELECT Id, TransactionReference,
                               ROW_NUMBER() OVER (PARTITION BY TransactionReference ORDER BY Id ASC) AS dup_rn
                        FROM [Appointments]
                        WHERE [TransactionReference] IS NOT NULL
                    )
                    UPDATE a
                    SET a.TransactionReference = a.TransactionReference + '-D' + CAST(a.Id AS VARCHAR(10))
                    FROM [Appointments] a
                    INNER JOIN Duplicates d ON a.Id = d.Id
                    WHERE d.dup_rn > 1;
                END;
            ");

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_TransactionReference",
                table: "Appointments",
                column: "TransactionReference",
                unique: true,
                filter: "[TransactionReference] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Appointments_TransactionReference",
                table: "Appointments");

            migrationBuilder.DropColumn(
                name: "DiscountAmount",
                table: "Appointments");

            migrationBuilder.DropColumn(
                name: "PaidAmount",
                table: "Appointments");

            migrationBuilder.DropColumn(
                name: "PaidAt",
                table: "Appointments");

            migrationBuilder.DropColumn(
                name: "PromoCode",
                table: "Appointments");

            migrationBuilder.DropColumn(
                name: "TransactionReference",
                table: "Appointments");
        }
    }
}
