using System.Security.Cryptography;
using MediCare.Data.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MediCare.Services.Common;

public class AttachmentStorageMigrationHelper
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ILogger<AttachmentStorageMigrationHelper> _logger;

    public AttachmentStorageMigrationHelper(
        ApplicationDbContext dbContext,
        ILogger<AttachmentStorageMigrationHelper> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<AttachmentMigrationResult> MigrateAsync(string contentRootPath, string webRootPath)
    {
        var result = new AttachmentMigrationResult();
        var sourceDir = Path.Combine(webRootPath, "uploads", "records");
        var destDir = Path.Combine(contentRootPath, "App_Data", "uploads", "records");

        _logger.LogInformation("Starting attachment migration from {SourceDir} to {DestDir}", sourceDir, destDir);

        if (!Directory.Exists(destDir))
        {
            Directory.CreateDirectory(destDir);
        }

        // 1. Move and verify physical files if legacy source folder exists
        if (Directory.Exists(sourceDir))
        {
            var files = Directory.GetFiles(sourceDir);
            foreach (var filePath in files)
            {
                var fileName = Path.GetFileName(filePath);
                var destFilePath = Path.Combine(destDir, fileName);

                try
                {
                    // Copy first
                    File.Copy(filePath, destFilePath, overwrite: true);

                    // Verify copy (size & SHA256)
                    var srcInfo = new FileInfo(filePath);
                    var destInfo = new FileInfo(destFilePath);
                    if (srcInfo.Length != destInfo.Length)
                    {
                        throw new IOException($"Size mismatch for {fileName}: src={srcInfo.Length}, dest={destInfo.Length}");
                    }

                    using var sha = SHA256.Create();
                    using (var srcStream = File.OpenRead(filePath))
                    using (var destStream = File.OpenRead(destFilePath))
                    {
                        var srcHash = await sha.ComputeHashAsync(srcStream);
                        var destHash = await sha.ComputeHashAsync(destStream);

                        if (!srcHash.SequenceEqual(destHash))
                        {
                            throw new IOException($"SHA256 hash mismatch for {fileName}");
                        }
                    }

                    // Only delete source after verified
                    File.Delete(filePath);
                    result.FilesMigrated++;
                    _logger.LogInformation("Successfully migrated and verified attachment: {FileName}", fileName);
                }
                catch (Exception ex)
                {
                    result.FilesFailed++;
                    _logger.LogError(ex, "Failed to migrate attachment file: {FileName}. Source preserved.", fileName);
                }
            }

            // If source directory is now empty, delete it
            if (!Directory.EnumerateFileSystemEntries(sourceDir).Any())
            {
                Directory.Delete(sourceDir);
                _logger.LogInformation("Deleted empty legacy directory: {SourceDir}", sourceDir);
            }
        }

        // 2. Update database records to strip path prefixes and store only safe filenames
        var recordsWithPaths = await _dbContext.MedicalRecords
            .Where(m => m.AttachmentPath != null && (m.AttachmentPath.Contains("/") || m.AttachmentPath.Contains("\\")))
            .ToListAsync();

        foreach (var record in recordsWithPaths)
        {
            var raw = record.AttachmentPath!;
            var pureFileName = Path.GetFileName(raw.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar));
            if (!string.IsNullOrEmpty(pureFileName))
            {
                record.AttachmentPath = pureFileName;
                result.DatabaseRowsUpdated++;
            }
        }

        if (result.DatabaseRowsUpdated > 0)
        {
            await _dbContext.SaveChangesAsync();
            _logger.LogInformation("Sanitized {Count} MedicalRecord.AttachmentPath entries to pure file names.", result.DatabaseRowsUpdated);
        }

        result.IsSuccess = result.FilesFailed == 0;
        return result;
    }
}

public class AttachmentMigrationResult
{
    public bool IsSuccess { get; set; }
    public int FilesMigrated { get; set; }
    public int FilesFailed { get; set; }
    public int DatabaseRowsUpdated { get; set; }
}
