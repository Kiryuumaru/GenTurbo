using Application.Workers.Interfaces.Outbound;
using Microsoft.Extensions.Logging;

namespace Infrastructure.FileSystem.Adapters;

internal sealed class FileSystemStorageAdapter(ILogger<FileSystemStorageAdapter> logger) : IFileStorage
{
    private const string BaseDir = "/app/files";

    private static string SanitizeFileName(string fileName)
    {
        var name = Path.GetFileName(fileName);
        if (string.IsNullOrEmpty(name))
            throw new ArgumentException("Invalid file name", nameof(fileName));
        return name;
    }

    public string Save(string fileName, byte[] data)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        ArgumentNullException.ThrowIfNull(data);

        var safeName = SanitizeFileName(fileName);
        var dir = Path.GetFullPath(BaseDir);
        Directory.CreateDirectory(dir);

        var filePath = Path.Combine(dir, safeName);
        File.WriteAllBytes(filePath, data);

        logger.LogInformation("Saved file {FileName} ({Size} bytes)", safeName, data.Length);
        return $"/files/{safeName}";
    }

    public bool Delete(string url)
    {
        var fileName = ExtractFileName(url);
        if (fileName is null)
            return false;

        var filePath = Path.Combine(Path.GetFullPath(BaseDir), fileName);
        if (File.Exists(filePath))
        {
            File.Delete(filePath);
            logger.LogInformation("Deleted file {FileName}", fileName);
            return true;
        }

        return false;
    }

    public bool Exists(string url)
    {
        var fileName = ExtractFileName(url);
        if (fileName is null)
            return false;

        return File.Exists(Path.Combine(Path.GetFullPath(BaseDir), fileName));
    }

    public string ResolvePath(string url)
    {
        var fileName = ExtractFileName(url)
            ?? throw new ArgumentException("Invalid URL", nameof(url));
        return Path.Combine(Path.GetFullPath(BaseDir), fileName);
    }

    private static string? ExtractFileName(string url)
    {
        if (string.IsNullOrEmpty(url))
            return null;

        var fileName = url.Contains('/') ? url.Split('/')[^1] : url;
        return string.IsNullOrEmpty(fileName) ? null : fileName;
    }
}
