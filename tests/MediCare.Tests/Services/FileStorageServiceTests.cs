using System.Text;
using FluentAssertions;
using MediCare.Services.Implementations;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace MediCare.Tests.Services;

public class FileStorageServiceTests : IDisposable
{
    private readonly string _tempStorageRoot;
    private readonly Mock<ILogger<FileStorageService>> _loggerMock;
    private readonly FileStorageService _service;

    // Real binary magic byte signatures
    private static readonly byte[] ValidJpgBytes = { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00 };
    private static readonly byte[] ValidPngBytes = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D };
    private static readonly byte[] ValidPdfBytes = Encoding.ASCII.GetBytes("%PDF-1.4 clinical lab report test payload");

    public FileStorageServiceTests()
    {
        _tempStorageRoot = Path.Combine(Path.GetTempPath(), $"medicare_tests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempStorageRoot);

        _loggerMock = new Mock<ILogger<FileStorageService>>();
        _service = new FileStorageService(_loggerMock.Object);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempStorageRoot))
        {
            try
            {
                Directory.Delete(_tempStorageRoot, recursive: true);
            }
            catch
            {
                // Ignore cleanup errors in tests
            }
        }
    }

    private static IFormFile CreateMockFormFile(string fileName, string contentType, byte[] content)
    {
        var stream = new MemoryStream(content);
        return new FormFile(stream, 0, content.Length, "file", fileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = contentType
        };
    }

    [Fact]
    public async Task SaveMedicalAttachmentAsync_ValidJpgFile_SavesFileAndReturnsOnlyFileName()
    {
        // Arrange
        var file = CreateMockFormFile("scan_result.jpg", "image/jpeg", ValidJpgBytes);

        // Act
        var result = await _service.SaveMedicalAttachmentAsync(file, _tempStorageRoot);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNullOrEmpty();
        // Server generates random file name only (no directories stored in DB)
        result.Value.Should().NotContain("/");
        result.Value.Should().NotContain("\\");
        result.Value.Should().EndWith(".jpg");

        var physicalPath = Path.Combine(_tempStorageRoot, result.Value!);
        File.Exists(physicalPath).Should().BeTrue();
    }

    [Fact]
    public async Task SaveMedicalAttachmentAsync_ValidPngFile_SavesFileAndReturnsOnlyFileName()
    {
        // Arrange
        var file = CreateMockFormFile("xray.png", "image/png", ValidPngBytes);

        // Act
        var result = await _service.SaveMedicalAttachmentAsync(file, _tempStorageRoot);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotContain("/");
        result.Value.Should().NotContain("\\");
        result.Value.Should().EndWith(".png");
    }

    [Fact]
    public async Task SaveMedicalAttachmentAsync_ValidPdfFile_SavesFileAndReturnsOnlyFileName()
    {
        // Arrange
        var file = CreateMockFormFile("lab_report.pdf", "application/pdf", ValidPdfBytes);

        // Act
        var result = await _service.SaveMedicalAttachmentAsync(file, _tempStorageRoot);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotContain("/");
        result.Value.Should().NotContain("\\");
        result.Value.Should().EndWith(".pdf");
    }

    [Fact]
    public async Task SaveMedicalAttachmentAsync_FakePdfFile_WithPlainTextContent_RejectsMagicBytesMismatch()
    {
        // Arrange: plain text pretending to be a PDF
        var fakeBytes = Encoding.UTF8.GetBytes("Hello, I am actually a plain text file, not a real PDF document!");
        var file = CreateMockFormFile("fake_report.pdf", "application/pdf", fakeBytes);

        // Act
        var result = await _service.SaveMedicalAttachmentAsync(file, _tempStorageRoot);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("signature");
    }

    [Fact]
    public async Task SaveMedicalAttachmentAsync_ExtensionMismatch_RealPngWithPdfExtension_RejectsFile()
    {
        // Arrange: valid PNG bytes but user gave .pdf extension
        var file = CreateMockFormFile("mismatched.pdf", "application/pdf", ValidPngBytes);

        // Act
        var result = await _service.SaveMedicalAttachmentAsync(file, _tempStorageRoot);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().MatchRegex("(?i)(mismatch|signature|type)");
    }

    [Fact]
    public async Task SaveMedicalAttachmentAsync_FileExceeds5Mb_ReturnsFailure()
    {
        // Arrange (5MB + 1 byte) with JPG magic bytes at start
        var largeContent = new byte[5 * 1024 * 1024 + 1];
        Array.Copy(ValidJpgBytes, largeContent, ValidJpgBytes.Length);
        var file = CreateMockFormFile("too_large.jpg", "image/jpeg", largeContent);

        // Act
        var result = await _service.SaveMedicalAttachmentAsync(file, _tempStorageRoot);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("5 MB");
    }

    [Theory]
    [InlineData("script.exe", "application/x-msdownload")]
    [InlineData("malware.bat", "application/x-bat")]
    [InlineData("notes.txt", "text/plain")]
    public async Task SaveMedicalAttachmentAsync_DisallowedExtension_ReturnsFailure(string fileName, string contentType)
    {
        // Arrange
        var content = Encoding.UTF8.GetBytes("sample content");
        var file = CreateMockFormFile(fileName, contentType, content);

        // Act
        var result = await _service.SaveMedicalAttachmentAsync(file, _tempStorageRoot);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Only JPG, PNG, and PDF");
    }

    [Fact]
    public async Task SaveMedicalAttachmentAsync_PathTraversalAttemptInFileName_SafelyGeneratesRandomFileName()
    {
        // Arrange
        var file = CreateMockFormFile("../../../evil.png", "image/png", ValidPngBytes);

        // Act
        var result = await _service.SaveMedicalAttachmentAsync(file, _tempStorageRoot);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotContain("evil");
        result.Value.Should().NotContain("..");
        result.Value.Should().NotContain("/");
        result.Value.Should().NotContain("\\");
    }

    [Fact]
    public void ResolveAttachmentPath_ValidFile_ReturnsCanonicalPath()
    {
        // Arrange
        var fileName = "valid_lab_123.pdf";
        var fullPath = Path.Combine(_tempStorageRoot, fileName);
        File.WriteAllBytes(fullPath, ValidPdfBytes);

        // Act
        var resolved = _service.ResolveAttachmentPath(fileName, _tempStorageRoot);

        // Assert
        resolved.Should().NotBeNull();
        resolved.Should().Be(Path.GetFullPath(fullPath));
    }

    [Fact]
    public void ResolveAttachmentPath_PathTraversal_ReturnsNull()
    {
        // Act
        var resolved = _service.ResolveAttachmentPath("../../../secret.txt", _tempStorageRoot);

        // Assert
        resolved.Should().BeNull();
    }

    [Fact]
    public void ResolveAttachmentPath_LegacyRelativePath_SafelyExtractsFileName()
    {
        // Arrange
        var fileName = "legacy_scan_456.jpg";
        var fullPath = Path.Combine(_tempStorageRoot, fileName);
        File.WriteAllBytes(fullPath, ValidJpgBytes);

        // Act: passing old DB relative path 'uploads/records/legacy_scan_456.jpg'
        var resolved = _service.ResolveAttachmentPath("uploads/records/legacy_scan_456.jpg", _tempStorageRoot);

        // Assert
        resolved.Should().NotBeNull();
        resolved.Should().Be(Path.GetFullPath(fullPath));
    }

    [Fact]
    public void DeleteAttachment_ExistingFile_DeletesPhysicalFile()
    {
        // Arrange
        var fileName = "test_to_delete.pdf";
        var fullPath = Path.Combine(_tempStorageRoot, fileName);
        File.WriteAllBytes(fullPath, ValidPdfBytes);

        // Act
        _service.DeleteAttachment(fileName, _tempStorageRoot);

        // Assert
        File.Exists(fullPath).Should().BeFalse();
    }
}
