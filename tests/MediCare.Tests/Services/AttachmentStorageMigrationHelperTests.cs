using System.Text;
using FluentAssertions;
using MediCare.Data.Context;
using MediCare.Data.Entities;
using MediCare.Services.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace MediCare.Tests.Services;

public class AttachmentStorageMigrationHelperTests : IDisposable
{
    private readonly ApplicationDbContext _dbContext;
    private readonly Mock<ILogger<AttachmentStorageMigrationHelper>> _loggerMock;
    private readonly AttachmentStorageMigrationHelper _helper;
    private readonly string _testWebRoot;
    private readonly string _testContentRoot;

    public AttachmentStorageMigrationHelperTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _dbContext = new ApplicationDbContext(options);
        _loggerMock = new Mock<ILogger<AttachmentStorageMigrationHelper>>();
        _helper = new AttachmentStorageMigrationHelper(_dbContext, _loggerMock.Object);

        _testWebRoot = Path.Combine(Path.GetTempPath(), $"medicare_webroot_{Guid.NewGuid():N}");
        _testContentRoot = Path.Combine(Path.GetTempPath(), $"medicare_contentroot_{Guid.NewGuid():N}");

        Directory.CreateDirectory(_testWebRoot);
        Directory.CreateDirectory(_testContentRoot);
    }

    public void Dispose()
    {
        _dbContext.Dispose();

        if (Directory.Exists(_testWebRoot))
        {
            try { Directory.Delete(_testWebRoot, recursive: true); } catch { }
        }

        if (Directory.Exists(_testContentRoot))
        {
            try { Directory.Delete(_testContentRoot, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task MigrateAsync_MovesFiles_VerifiesContent_AndSanitizesDatabasePaths()
    {
        // Arrange
        var legacyDir = Path.Combine(_testWebRoot, "uploads", "records");
        Directory.CreateDirectory(legacyDir);

        var testFile1 = Path.Combine(legacyDir, "report_001.pdf");
        var testFile2 = Path.Combine(legacyDir, "scan_002.jpg");
        var content1 = Encoding.UTF8.GetBytes("%PDF-1.4 report content for file 1");
        var content2 = Encoding.UTF8.GetBytes("image data for file 2");

        await File.WriteAllBytesAsync(testFile1, content1);
        await File.WriteAllBytesAsync(testFile2, content2);

        // Add records with old relative path in DB
        _dbContext.MedicalRecords.Add(new MedicalRecord
        {
            Id = 1,
            AppointmentId = 1,
            DoctorId = 1,
            PatientId = 1,
            Diagnosis = "Hypertension",
            AttachmentPath = "uploads/records/report_001.pdf"
        });
        _dbContext.MedicalRecords.Add(new MedicalRecord
        {
            Id = 2,
            AppointmentId = 2,
            DoctorId = 1,
            PatientId = 2,
            Diagnosis = "Checkup",
            AttachmentPath = "uploads/records/scan_002.jpg"
        });
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _helper.MigrateAsync(_testContentRoot, _testWebRoot);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.FilesMigrated.Should().Be(2);
        result.FilesFailed.Should().Be(0);
        result.DatabaseRowsUpdated.Should().Be(2);

        // Files in destination App_Data/uploads/records exist and match content
        var destDir = Path.Combine(_testContentRoot, "App_Data", "uploads", "records");
        var destFile1 = Path.Combine(destDir, "report_001.pdf");
        var destFile2 = Path.Combine(destDir, "scan_002.jpg");

        File.Exists(destFile1).Should().BeTrue();
        File.Exists(destFile2).Should().BeTrue();
        (await File.ReadAllBytesAsync(destFile1)).Should().Equal(content1);
        (await File.ReadAllBytesAsync(destFile2)).Should().Equal(content2);

        // Legacy files deleted
        File.Exists(testFile1).Should().BeFalse();
        File.Exists(testFile2).Should().BeFalse();

        // Database records sanitized to pure file names
        var rec1 = await _dbContext.MedicalRecords.FindAsync(1);
        var rec2 = await _dbContext.MedicalRecords.FindAsync(2);
        rec1!.AttachmentPath.Should().Be("report_001.pdf");
        rec2!.AttachmentPath.Should().Be("scan_002.jpg");
    }

    [Fact]
    public async Task MigrateAsync_SecondRun_IsIdempotent()
    {
        // Arrange
        // Initial clean state with no legacy files
        // Act
        var result = await _helper.MigrateAsync(_testContentRoot, _testWebRoot);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.FilesMigrated.Should().Be(0);
        result.FilesFailed.Should().Be(0);
        result.DatabaseRowsUpdated.Should().Be(0);
    }
}
