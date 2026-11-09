using MediCare.Services.Common;
using Microsoft.AspNetCore.Http;

namespace MediCare.Services.Contracts;

public interface IFileStorageService
{
    Task<Result<string>> SaveMedicalAttachmentAsync(IFormFile file, string storageRootPath);
    string? ResolveAttachmentPath(string fileNameOrPath, string storageRootPath);
    void DeleteAttachment(string fileNameOrPath, string storageRootPath);
}
