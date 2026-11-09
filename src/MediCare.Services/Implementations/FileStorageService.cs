using MediCare.Services.Common;
using MediCare.Services.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace MediCare.Services.Implementations;

public class FileStorageService : IFileStorageService
{
    public const long MaxFileSizeBytes = 5 * 1024 * 1024; // 5 MB per FR-15

    private readonly ILogger<FileStorageService> _logger;

    public FileStorageService(ILogger<FileStorageService> logger)
    {
        _logger = logger;
    }

    public async Task<Result<string>> SaveMedicalAttachmentAsync(IFormFile file, string storageRootPath)
    {
        if (file == null || file.Length == 0)
        {
            return Result<string>.Failure("The selected attachment is empty.");
        }

        if (file.Length > MaxFileSizeBytes)
        {
            return Result<string>.Failure("File size exceeds the 5 MB limit.");
        }

        if (string.IsNullOrWhiteSpace(storageRootPath))
        {
            return Result<string>.Failure("Storage root path is not configured.");
        }

        // 1. Inspect Magic Bytes
        byte[] headerBytes = new byte[16];
        await using (var readStream = file.OpenReadStream())
        {
            int bytesRead = await readStream.ReadAsync(headerBytes, 0, headerBytes.Length);
            if (bytesRead < 4)
            {
                return Result<string>.Failure("Invalid file content or corrupt file header signature.");
            }
        }

        string? detectedExtension = null;

        // JPEG: FF D8 FF
        if (headerBytes[0] == 0xFF && headerBytes[1] == 0xD8 && headerBytes[2] == 0xFF)
        {
            detectedExtension = ".jpg";
        }
        // PNG: 89 50 4E 47 0D 0A 1A 0A
        else if (headerBytes[0] == 0x89 && headerBytes[1] == 0x50 && headerBytes[2] == 0x4E && headerBytes[3] == 0x47 &&
                 headerBytes[4] == 0x0D && headerBytes[5] == 0x0A && headerBytes[6] == 0x1A && headerBytes[7] == 0x0A)
        {
            detectedExtension = ".png";
        }
        // PDF: 25 50 44 46 (%PDF)
        else if (headerBytes[0] == 0x25 && headerBytes[1] == 0x50 && headerBytes[2] == 0x44 && headerBytes[3] == 0x46)
        {
            detectedExtension = ".pdf";
        }

        if (detectedExtension == null)
        {
            return Result<string>.Failure("Invalid file signature. Only JPG, PNG, and PDF files are permitted.");
        }

        // 2. Validate user extension matches detected type
        var clientExt = Path.GetExtension(file.FileName).ToLowerInvariant();
        bool extMatches = (detectedExtension == ".jpg" && (clientExt == ".jpg" || clientExt == ".jpeg")) ||
                          (detectedExtension == ".png" && clientExt == ".png") ||
                          (detectedExtension == ".pdf" && clientExt == ".pdf");

        if (!extMatches)
        {
            return Result<string>.Failure($"File extension '{clientExt}' does not match detected file signature '{detectedExtension}'.");
        }

        try
        {
            if (!Directory.Exists(storageRootPath))
            {
                Directory.CreateDirectory(storageRootPath);
            }

            // Generate safe non-guessable GUID filename
            var safeFileName = $"{Guid.NewGuid():N}{detectedExtension}";
            var fullPath = Path.Combine(storageRootPath, safeFileName);

            // Double check for directory traversal
            var fullNormalizedPath = Path.GetFullPath(fullPath);
            var normalizedFolder = Path.GetFullPath(storageRootPath);
            if (!fullNormalizedPath.StartsWith(normalizedFolder, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Security: Path traversal attempt detected during file upload: {FileName}", file.FileName);
                return Result<string>.Failure("Invalid file destination path.");
            }

            await using var stream = new FileStream(fullNormalizedPath, FileMode.Create, FileAccess.Write);
            await file.CopyToAsync(stream);

            _logger.LogInformation("Diagnostic attachment securely stored: {SafeFileName}", safeFileName);

            // Return strictly the filename without directory structure
            return Result<string>.Success(safeFileName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error saving diagnostic file attachment.");
            return Result<string>.Failure("Failed to save the diagnostic attachment to the server.");
        }
    }

    public string? ResolveAttachmentPath(string fileNameOrPath, string storageRootPath)
    {
        if (string.IsNullOrWhiteSpace(fileNameOrPath) || string.IsNullOrWhiteSpace(storageRootPath)) return null;

        // Extract strictly the file name to prevent any path traversal
        var fileName = Path.GetFileName(fileNameOrPath.Trim().Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar));
        if (string.IsNullOrWhiteSpace(fileName)) return null;

        var normalizedRoot = Path.GetFullPath(storageRootPath);
        var fullPath = Path.GetFullPath(Path.Combine(normalizedRoot, fileName));

        if (!fullPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("Security: Path traversal attempt blocked for file: {FileName}", fileNameOrPath);
            return null;
        }

        if (!File.Exists(fullPath))
        {
            return null;
        }

        return fullPath;
    }

    public void DeleteAttachment(string fileNameOrPath, string storageRootPath)
    {
        if (string.IsNullOrWhiteSpace(fileNameOrPath)) return;

        try
        {
            var fullPath = ResolveAttachmentPath(fileNameOrPath, storageRootPath);
            if (fullPath != null && File.Exists(fullPath))
            {
                File.Delete(fullPath);
                _logger.LogInformation("Deleted attachment file: {FullPath}", fullPath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not delete attachment file: {FileName}", fileNameOrPath);
        }
    }
}
