using System.Security.Claims;
using FluentAssertions;
using MediCare.Services.Common;
using MediCare.Services.Contracts;
using MediCare.Services.DTOs;
using MediCare.Web.Controllers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace MediCare.Tests.Controllers;

public class MedicalRecordsControllerAttachmentTests : IDisposable
{
    private readonly Mock<IMedicalRecordService> _medicalRecordServiceMock;
    private readonly Mock<IFileStorageService> _fileStorageMock;
    private readonly Mock<IWebHostEnvironment> _webHostEnvironmentMock;
    private readonly Mock<ILogger<MedicalRecordsController>> _loggerMock;
    private readonly MedicalRecordsController _controller;
    private readonly string _testContentRoot;

    public MedicalRecordsControllerAttachmentTests()
    {
        _medicalRecordServiceMock = new Mock<IMedicalRecordService>();
        _fileStorageMock = new Mock<IFileStorageService>();
        _webHostEnvironmentMock = new Mock<IWebHostEnvironment>();
        _loggerMock = new Mock<ILogger<MedicalRecordsController>>();

        _testContentRoot = Path.Combine(Path.GetTempPath(), $"medicare_ctrl_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testContentRoot);
        _webHostEnvironmentMock.Setup(w => w.ContentRootPath).Returns(_testContentRoot);

        _controller = new MedicalRecordsController(
            _medicalRecordServiceMock.Object,
            _webHostEnvironmentMock.Object,
            _loggerMock.Object,
            _fileStorageMock.Object);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testContentRoot))
        {
            try
            {
                Directory.Delete(_testContentRoot, recursive: true);
            }
            catch
            {
                // Ignore cleanup
            }
        }
    }

    private void SetUserContext(string? userId, string role)
    {
        var claims = new List<Claim>();
        if (!string.IsNullOrEmpty(userId))
        {
            claims.Add(new Claim(ClaimTypes.NameIdentifier, userId));
            claims.Add(new Claim(ClaimTypes.Role, role));
        }

        var identity = new ClaimsIdentity(claims, string.IsNullOrEmpty(userId) ? null : "TestAuth");
        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) }
        };
    }

    [Fact]
    public async Task DownloadAttachment_PatientOwner_ReturnsPhysicalFileWithSecurityHeaders()
    {
        // Arrange
        SetUserContext("patient_user_1", "Patient");
        var recordDetails = new MedicalRecordDetailsDto
        {
            Id = 10,
            AttachmentPath = "scan_123.pdf"
        };

        _medicalRecordServiceMock.Setup(m => m.GetRecordDetailsAsync(10, "patient_user_1", false, true, false))
            .ReturnsAsync(Result<MedicalRecordDetailsDto>.Success(recordDetails));

        var storageFolder = Path.Combine(_testContentRoot, "App_Data", "uploads", "records");
        Directory.CreateDirectory(storageFolder);
        var physicalFile = Path.Combine(storageFolder, "scan_123.pdf");
        File.WriteAllText(physicalFile, "%PDF-1.4 mock content");

        _fileStorageMock.Setup(f => f.ResolveAttachmentPath("scan_123.pdf", It.IsAny<string>()))
            .Returns(physicalFile);

        // Act
        var result = await _controller.DownloadAttachment(10);

        // Assert
        result.Should().BeOfType<PhysicalFileResult>();
        var fileResult = (PhysicalFileResult)result;
        fileResult.FileName.Should().Be(physicalFile);
        fileResult.ContentType.Should().Be("application/pdf");

        // Verify security response headers
        _controller.Response.Headers.Should().ContainKey("X-Content-Type-Options");
        _controller.Response.Headers["X-Content-Type-Options"].ToString().Should().Be("nosniff");
        _controller.Response.Headers["Cache-Control"].ToString().Should().Contain("no-store");
        _controller.Response.Headers["Content-Disposition"].ToString().Should().Contain("attachment");
    }

    [Fact]
    public async Task DownloadAttachment_TreatingDoctor_ReturnsPhysicalFileWithSecurityHeaders()
    {
        // Arrange
        SetUserContext("doc_user_1", "Doctor");
        var recordDetails = new MedicalRecordDetailsDto
        {
            Id = 10,
            AttachmentPath = "scan_123.pdf"
        };

        _medicalRecordServiceMock.Setup(m => m.GetRecordDetailsAsync(10, "doc_user_1", true, false, false))
            .ReturnsAsync(Result<MedicalRecordDetailsDto>.Success(recordDetails));

        var storageFolder = Path.Combine(_testContentRoot, "App_Data", "uploads", "records");
        Directory.CreateDirectory(storageFolder);
        var physicalFile = Path.Combine(storageFolder, "scan_123.pdf");
        File.WriteAllText(physicalFile, "%PDF-1.4 mock content");

        _fileStorageMock.Setup(f => f.ResolveAttachmentPath("scan_123.pdf", It.IsAny<string>()))
            .Returns(physicalFile);

        // Act
        var result = await _controller.DownloadAttachment(10);

        // Assert
        result.Should().BeOfType<PhysicalFileResult>();
    }

    [Fact]
    public async Task DownloadAttachment_Admin_ReturnsForbid()
    {
        // Arrange
        SetUserContext("admin_user_1", "Admin");

        // Act
        var result = await _controller.DownloadAttachment(10);

        // Assert
        result.Should().BeOfType<ForbidResult>();
    }

    [Fact]
    public async Task DownloadAttachment_UnrelatedPatient_ReturnsForbid()
    {
        // Arrange
        SetUserContext("other_patient_user", "Patient");
        _medicalRecordServiceMock.Setup(m => m.GetRecordDetailsAsync(10, "other_patient_user", false, true, false))
            .ReturnsAsync(Result<MedicalRecordDetailsDto>.Failure("Forbidden: You are not authorized to view this clinical record."));

        // Act
        var result = await _controller.DownloadAttachment(10);

        // Assert
        result.Should().BeOfType<ForbidResult>();
    }

    [Fact]
    public async Task DownloadAttachment_UnrelatedDoctor_ReturnsForbid()
    {
        // Arrange
        SetUserContext("unrelated_doc_user", "Doctor");
        _medicalRecordServiceMock.Setup(m => m.GetRecordDetailsAsync(10, "unrelated_doc_user", true, false, false))
            .ReturnsAsync(Result<MedicalRecordDetailsDto>.Failure("Forbidden: You are not authorized to view this clinical record."));

        // Act
        var result = await _controller.DownloadAttachment(10);

        // Assert
        result.Should().BeOfType<ForbidResult>();
    }

    [Fact]
    public async Task DownloadAttachment_Anonymous_ReturnsChallenge()
    {
        // Arrange
        SetUserContext(null, "");

        // Act
        var result = await _controller.DownloadAttachment(10);

        // Assert
        result.Should().BeOfType<ChallengeResult>();
    }

    [Fact]
    public async Task DownloadAttachment_WhenRecordNotFound_ReturnsNotFound()
    {
        // Arrange
        SetUserContext("patient_user_1", "Patient");
        _medicalRecordServiceMock.Setup(m => m.GetRecordDetailsAsync(10, "patient_user_1", false, true, false))
            .ReturnsAsync(Result<MedicalRecordDetailsDto>.Failure("Record not found."));

        // Act
        var result = await _controller.DownloadAttachment(10);

        // Assert
        result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task DownloadAttachment_WhenAttachmentPathIsEmpty_ReturnsNotFoundObject()
    {
        // Arrange
        SetUserContext("patient_user_1", "Patient");
        var recordDetails = new MedicalRecordDetailsDto
        {
            Id = 10,
            AttachmentPath = null
        };
        _medicalRecordServiceMock.Setup(m => m.GetRecordDetailsAsync(10, "patient_user_1", false, true, false))
            .ReturnsAsync(Result<MedicalRecordDetailsDto>.Success(recordDetails));

        // Act
        var result = await _controller.DownloadAttachment(10);

        // Assert
        result.Should().BeOfType<NotFoundObjectResult>()
            .Which.Value.Should().Be("No attachment associated with this medical record.");
    }

    [Fact]
    public async Task DownloadAttachment_WhenFileDoesNotExistOnServer_ReturnsNotFoundObject()
    {
        // Arrange
        SetUserContext("patient_user_1", "Patient");
        var recordDetails = new MedicalRecordDetailsDto
        {
            Id = 10,
            AttachmentPath = "missing_file.pdf"
        };
        _medicalRecordServiceMock.Setup(m => m.GetRecordDetailsAsync(10, "patient_user_1", false, true, false))
            .ReturnsAsync(Result<MedicalRecordDetailsDto>.Success(recordDetails));

        _fileStorageMock.Setup(f => f.ResolveAttachmentPath("missing_file.pdf", It.IsAny<string>()))
            .Returns(Path.Combine(_testContentRoot, "non_existent.pdf"));

        // Act
        var result = await _controller.DownloadAttachment(10);

        // Assert
        result.Should().BeOfType<NotFoundObjectResult>()
            .Which.Value.Should().Be("Attachment file not found on server.");
    }
}
