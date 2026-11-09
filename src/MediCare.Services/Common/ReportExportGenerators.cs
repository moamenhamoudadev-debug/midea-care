using System.Globalization;
using System.IO.Compression;
using System.Security;
using System.Text;

namespace MediCare.Services.Common;

public class AppointmentExportRow
{
    public int Id { get; set; }
    public string Date { get; set; } = string.Empty;
    public string Time { get; set; } = string.Empty;
    public string DoctorName { get; set; } = string.Empty;
    public string Specialization { get; set; } = string.Empty;
    public string PatientName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal Fee { get; set; }
    public string PaymentStatus { get; set; } = string.Empty;
}

public static class ReportExportGenerators
{
    /// <summary>
    /// Generates a genuine Office Open XML (.xlsx) spreadsheet without third-party dependencies.
    /// </summary>
    public static byte[] GenerateExcel(IEnumerable<AppointmentExportRow> rows)
    {
        using var memoryStream = new MemoryStream();
        using (var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, true))
        {
            // 1. [Content_Types].xml
            CreateEntry(archive, "[Content_Types].xml", @"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>
<Types xmlns=""http://schemas.openxmlformats.org/package/2006/content-types"">
  <Default Extension=""rels"" ContentType=""application/vnd.openxmlformats-package.relationships+xml""/>
  <Default Extension=""xml"" ContentType=""application/xml""/>
  <Override PartName=""/xl/workbook.xml"" ContentType=""application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml""/>
  <Override PartName=""/xl/worksheets/sheet1.xml"" ContentType=""application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml""/>
  <Override PartName=""/xl/styles.xml"" ContentType=""application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml""/>
</Types>");

            // 2. _rels/.rels
            CreateEntry(archive, "_rels/.rels", @"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>
<Relationships xmlns=""http://schemas.openxmlformats.org/package/2006/relationships"">
  <Relationship Id=""rId1"" Type=""http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument"" Target=""xl/workbook.xml""/>
</Relationships>");

            // 3. xl/_rels/workbook.xml.rels
            CreateEntry(archive, "xl/_rels/workbook.xml.rels", @"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>
<Relationships xmlns=""http://schemas.openxmlformats.org/package/2006/relationships"">
  <Relationship Id=""rId1"" Type=""http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet"" Target=""worksheets/sheet1.xml""/>
  <Relationship Id=""rId2"" Type=""http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles"" Target=""styles.xml""/>
</Relationships>");

            // 4. xl/workbook.xml
            CreateEntry(archive, "xl/workbook.xml", @"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>
<workbook xmlns=""http://schemas.openxmlformats.org/spreadsheetml/2006/main"" xmlns:r=""http://schemas.openxmlformats.org/officeDocument/2006/relationships"">
  <sheets>
    <sheet name=""Appointments"" sheetId=""1"" r:id=""rId1""/>
  </sheets>
</workbook>");

            // 5. xl/styles.xml
            CreateEntry(archive, "xl/styles.xml", @"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>
<styleSheet xmlns=""http://schemas.openxmlformats.org/spreadsheetml/2006/main"">
  <fonts count=""2"">
    <font><sz val=""11""/><name val=""Calibri""/></font>
    <font><b/><sz val=""11""/><name val=""Calibri""/></font>
  </fonts>
  <fills count=""2"">
    <fill><patternFill patternType=""none""/></fill>
    <fill><patternFill patternType=""gray125""/></fill>
  </fills>
  <borders count=""1""><border/></borders>
  <cellStyleXfs count=""1""><xf/></cellStyleXfs>
  <cellXfs count=""2"">
    <xf fontId=""0"" fillId=""0"" borderId=""0""/>
    <xf fontId=""1"" fillId=""0"" borderId=""0"" applyFont=""1""/>
  </cellXfs>
</styleSheet>");

            // 6. xl/worksheets/sheet1.xml
            var sheetBuilder = new StringBuilder();
            sheetBuilder.AppendLine(@"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>");
            sheetBuilder.AppendLine(@"<worksheet xmlns=""http://schemas.openxmlformats.org/spreadsheetml/2006/main"">");
            sheetBuilder.AppendLine(@"  <sheetData>");

            // Header row
            sheetBuilder.AppendLine(@"    <row r=""1"">");
            string[] headers = { "Appointment ID", "Date", "Time", "Doctor", "Specialization", "Patient", "Status", "Fee (EGP)", "Payment Status" };
            for (int i = 0; i < headers.Length; i++)
            {
                var colLetter = GetColumnLetter(i + 1);
                sheetBuilder.AppendLine($@"      <c r=""{colLetter}1"" t=""inlineStr"" s=""1""><is><t>{EscapeXml(headers[i])}</t></is></c>");
            }
            sheetBuilder.AppendLine(@"    </row>");

            // Data rows
            int rowIdx = 2;
            foreach (var r in rows)
            {
                sheetBuilder.AppendLine($@"    <row r=""{rowIdx}"">");
                sheetBuilder.AppendLine($@"      <c r=""A{rowIdx}"" t=""n""><v>{r.Id}</v></c>");
                sheetBuilder.AppendLine($@"      <c r=""B{rowIdx}"" t=""inlineStr""><is><t>{EscapeXml(r.Date)}</t></is></c>");
                sheetBuilder.AppendLine($@"      <c r=""C{rowIdx}"" t=""inlineStr""><is><t>{EscapeXml(r.Time)}</t></is></c>");
                sheetBuilder.AppendLine($@"      <c r=""D{rowIdx}"" t=""inlineStr""><is><t>{EscapeXml(r.DoctorName)}</t></is></c>");
                sheetBuilder.AppendLine($@"      <c r=""E{rowIdx}"" t=""inlineStr""><is><t>{EscapeXml(r.Specialization)}</t></is></c>");
                sheetBuilder.AppendLine($@"      <c r=""F{rowIdx}"" t=""inlineStr""><is><t>{EscapeXml(r.PatientName)}</t></is></c>");
                sheetBuilder.AppendLine($@"      <c r=""G{rowIdx}"" t=""inlineStr""><is><t>{EscapeXml(r.Status)}</t></is></c>");
                sheetBuilder.AppendLine($@"      <c r=""H{rowIdx}"" t=""n""><v>{r.Fee.ToString(CultureInfo.InvariantCulture)}</v></c>");
                sheetBuilder.AppendLine($@"      <c r=""I{rowIdx}"" t=""inlineStr""><is><t>{EscapeXml(r.PaymentStatus)}</t></is></c>");
                sheetBuilder.AppendLine(@"    </row>");
                rowIdx++;
            }

            sheetBuilder.AppendLine(@"  </sheetData>");
            sheetBuilder.AppendLine(@"</worksheet>");

            CreateEntry(archive, "xl/worksheets/sheet1.xml", sheetBuilder.ToString());
        }

        return memoryStream.ToArray();
    }

    /// <summary>
    /// Generates a standard PDF-1.4 report document without third-party dependencies.
    /// </summary>
    public static byte[] GeneratePdf(IEnumerable<AppointmentExportRow> rows, string reportTitle = "MediCare Clinical Appointments Report")
    {
        var rowList = rows.ToList();
        var generatedAt = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm UTC");
        var totalFee = rowList.Sum(r => r.Fee);

        // Build PDF stream commands
        var contentStream = new StringBuilder();

        // 1. Header Banner Box
        contentStream.AppendLine("0.0 0.4 0.96 rg"); // MediCare Blue (#0066F5)
        contentStream.AppendLine("30 760 535 50 re f");

        // 2. Banner Text
        contentStream.AppendLine("BT");
        contentStream.AppendLine("/F2 16 Tf");
        contentStream.AppendLine("1 1 1 rg"); // White
        contentStream.AppendLine("45 780 Td");
        contentStream.AppendLine($"({EscapePdf(reportTitle)}) Tj");
        contentStream.AppendLine("ET");

        // 3. Sub-header metadata
        contentStream.AppendLine("BT");
        contentStream.AppendLine("/F1 9 Tf");
        contentStream.AppendLine("0.2 0.2 0.2 rg"); // Dark Gray
        contentStream.AppendLine("30 740 Td");
        contentStream.AppendLine($"({EscapePdf($"Generated: {generatedAt}  |  Total Appointments: {rowList.Count}  |  Total Revenue: {totalFee:N2} EGP")}) Tj");
        contentStream.AppendLine("ET");

        // 4. Table Header Box
        contentStream.AppendLine("0.9 0.93 0.98 rg"); // Light blue-gray
        contentStream.AppendLine("30 710 535 20 re f");
        contentStream.AppendLine("0.7 0.7 0.7 RG 1 w");
        contentStream.AppendLine("30 710 535 20 re S");

        // 5. Table Header Columns
        contentStream.AppendLine("BT");
        contentStream.AppendLine("/F2 8 Tf");
        contentStream.AppendLine("0 0 0 rg");
        contentStream.AppendLine("35 716 Td (ID) Tj");
        contentStream.AppendLine("25 0 Td (Date) Tj");
        contentStream.AppendLine("55 0 Td (Time) Tj");
        contentStream.AppendLine("45 0 Td (Doctor) Tj");
        contentStream.AppendLine("95 0 Td (Specialization) Tj");
        contentStream.AppendLine("95 0 Td (Patient) Tj");
        contentStream.AppendLine("85 0 Td (Status) Tj");
        contentStream.AppendLine("65 0 Td (Fee) Tj");
        contentStream.AppendLine("ET");

        // 6. Data Rows (display up to 35 rows per page overview)
        float currentY = 695;
        int displayLimit = Math.Min(rowList.Count, 32);

        for (int i = 0; i < displayLimit; i++)
        {
            var r = rowList[i];
            // Alternating row background
            if (i % 2 == 1)
            {
                contentStream.AppendLine($"0.97 0.98 1.0 rg 30 {currentY - 4} 535 15 re f");
            }

            contentStream.AppendLine("BT");
            contentStream.AppendLine("/F1 8 Tf");
            contentStream.AppendLine("0.1 0.1 0.1 rg");
            contentStream.AppendLine($"35 {currentY} Td ({EscapePdf(r.Id.ToString())}) Tj");
            contentStream.AppendLine($"25 0 Td ({EscapePdf(r.Date)}) Tj");
            contentStream.AppendLine($"55 0 Td ({EscapePdf(r.Time)}) Tj");
            contentStream.AppendLine($"45 0 Td ({EscapePdf(Truncate(r.DoctorName, 18))}) Tj");
            contentStream.AppendLine($"95 0 Td ({EscapePdf(Truncate(r.Specialization, 16))}) Tj");
            contentStream.AppendLine($"95 0 Td ({EscapePdf(Truncate(r.PatientName, 16))}) Tj");
            contentStream.AppendLine($"85 0 Td ({EscapePdf(r.Status)}) Tj");
            contentStream.AppendLine($"65 0 Td ({EscapePdf($"{r.Fee:N0} EGP")}) Tj");
            contentStream.AppendLine("ET");

            currentY -= 16;
        }

        if (rowList.Count > displayLimit)
        {
            contentStream.AppendLine("BT");
            contentStream.AppendLine("/F1 8 Tf");
            contentStream.AppendLine("0.4 0.4 0.4 rg");
            contentStream.AppendLine($"35 {currentY} Td ({EscapePdf($"... and {rowList.Count - displayLimit} additional appointment records available in Excel/CSV export.")}) Tj");
            contentStream.AppendLine("ET");
            currentY -= 16;
        }

        // Footer line
        contentStream.AppendLine("0.8 0.8 0.8 RG 0.5 w");
        contentStream.AppendLine("30 40 535 0.5 re S");
        contentStream.AppendLine("BT");
        contentStream.AppendLine("/F1 8 Tf");
        contentStream.AppendLine("0.5 0.5 0.5 rg");
        contentStream.AppendLine("30 28 Td (MediCare Medical & Clinic Management System — Official Confidential Report) Tj");
        contentStream.AppendLine("ET");

        var contentBytes = Encoding.ASCII.GetBytes(contentStream.ToString());

        // Construct standard PDF document
        using var pdfMs = new MemoryStream();
        using var writer = new StreamWriter(pdfMs, Encoding.ASCII);

        var offsets = new List<long>();

        // Header
        writer.Write("%PDF-1.4\n%\xE2\xE3\xCF\xD3\n");
        writer.Flush();

        // Obj 1: Catalog
        offsets.Add(pdfMs.Position);
        writer.Write("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");
        writer.Flush();

        // Obj 2: Pages
        offsets.Add(pdfMs.Position);
        writer.Write("2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n");
        writer.Flush();

        // Obj 3: Page (A4: 595.28 x 841.89)
        offsets.Add(pdfMs.Position);
        writer.Write("3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595.28 841.89] /Resources << /Font << /F1 4 0 R /F2 5 0 R >> >> /Contents 6 0 R >>\nendobj\n");
        writer.Flush();

        // Obj 4: Font F1 (Helvetica)
        offsets.Add(pdfMs.Position);
        writer.Write("4 0 obj\n<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>\nendobj\n");
        writer.Flush();

        // Obj 5: Font F2 (Helvetica-Bold)
        offsets.Add(pdfMs.Position);
        writer.Write("5 0 obj\n<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold >>\nendobj\n");
        writer.Flush();

        // Obj 6: Contents stream
        offsets.Add(pdfMs.Position);
        writer.Write($"6 0 obj\n<< /Length {contentBytes.Length} >>\nstream\n");
        writer.Flush();
        pdfMs.Write(contentBytes, 0, contentBytes.Length);
        writer.Write("\nendstream\nendobj\n");
        writer.Flush();

        // XRef table
        var startXref = pdfMs.Position;
        writer.Write("xref\n0 7\n0000000000 65535 f \n");
        foreach (var off in offsets)
        {
            writer.Write($"{off:D10} 00000 n \n");
        }

        // Trailer
        writer.Write($"trailer\n<< /Size 7 /Root 1 0 R >>\nstartxref\n{startXref}\n%%EOF\n");
        writer.Flush();

        return pdfMs.ToArray();
    }

    private static void CreateEntry(ZipArchive archive, string entryName, string content)
    {
        var entry = archive.CreateEntry(entryName, CompressionLevel.Fastest);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream, Encoding.UTF8);
        writer.Write(content);
    }

    private static string GetColumnLetter(int colIndex)
    {
        string letter = "";
        while (colIndex > 0)
        {
            colIndex--;
            letter = (char)('A' + (colIndex % 26)) + letter;
            colIndex /= 26;
        }
        return letter;
    }

    private static string EscapeXml(string? input)
    {
        if (string.IsNullOrEmpty(input)) return "";
        return SecurityElement.Escape(input) ?? "";
    }

    private static string EscapePdf(string input)
    {
        if (string.IsNullOrEmpty(input)) return "";
        return input.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");
    }

    private static string Truncate(string val, int maxLength)
    {
        if (string.IsNullOrEmpty(val) || val.Length <= maxLength) return val;
        return val.Substring(0, maxLength - 2) + "..";
    }
}
