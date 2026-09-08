using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.Interfaces.Organization;
using BlueDragon.DuneLight.Core.Shared.Exceptions;
using BlueDragon.DuneLight.Infrastructure.Domain.Settings;
using Microsoft.AspNetCore.Hosting;

namespace BlueDragon.DuneLight.Infrastructure.Services;

public class BrandingFileStorage : IBrandingFileStorage
{
    private readonly BrandingSettings _brandingSettings;
    private readonly string _storagePath;
    private readonly string _publicBasePath;

    private static readonly Dictionary<string, string> ExtensionToContentType = new(StringComparer.OrdinalIgnoreCase)
    {
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".gif"] = "image/gif",
        [".svg"] = "image/svg+xml",
        [".webp"] = "image/webp",
        [".ico"] = "image/x-icon",
        [".avif"] = "image/avif"
    };

    public BrandingFileStorage(BrandingSettings brandingSettings, IWebHostEnvironment env)
    {
        _brandingSettings = brandingSettings;

        // Path.Combine ignori(ra) prvi argument ako je StoragePath već apsolutan (i na Windowsu i na Linuxu),
        // pa isti config radi i za relativni dev path ("wwwroot/branding") i za apsolutni produkcijski path
        // ako se ikad odluči izmjestiti izvan app foldera (npr. "/var/dunelight/storage/branding").
        _storagePath = Path.Combine(env.ContentRootPath, _brandingSettings.StoragePath);
        _publicBasePath = _brandingSettings.PublicBasePath.TrimEnd('/');

        Directory.CreateDirectory(_storagePath);
    }

    public async Task<string> SaveAsync(Stream content, string originalFileName, string organizationSlug, string filePrefix)
    {
        string ext = Path.GetExtension(originalFileName).ToLowerInvariant();
        if (string.IsNullOrEmpty(ext) || !_brandingSettings.AllowedExtensions.Contains(ext.TrimStart('.'), StringComparer.OrdinalIgnoreCase))
            throw new ValidationAppException($"Nije dozvoljena ekstenzija '{ext}'. Dozvoljeno: {string.Join(", ", _brandingSettings.AllowedExtensions)}.");

        if (content.Length > _brandingSettings.MaxFileSizeBytes)
            throw new ValidationAppException($"Datoteka je prevelika. Maksimalna veličina: {_brandingSettings.MaxFileSizeBytes / 1024 / 1024}MB.");

        string safeSlug = SanitizeForPath(organizationSlug);
        string uniqueName = $"{filePrefix}-{Guid.NewGuid():N}{ext}";

        string orgDir = Path.Combine(_storagePath, safeSlug);
        Directory.CreateDirectory(orgDir);

        string physicalPath = Path.Combine(orgDir, uniqueName);
        await using FileStream fileStream = new FileStream(physicalPath, FileMode.Create, FileAccess.Write, FileShare.None);
        await content.CopyToAsync(fileStream);

        return $"{_publicBasePath}/{safeSlug}/{uniqueName}";
    }

    public async Task DeleteAsync(string publicUrl)
    {
        if (string.IsNullOrWhiteSpace(publicUrl))
            return;

        if (!publicUrl.StartsWith(_publicBasePath, StringComparison.OrdinalIgnoreCase))
            return;

        string physicalPath = GetPhysicalPath(publicUrl);
        if (File.Exists(physicalPath))
            await Task.Run(() => File.Delete(physicalPath));
    }

    public Stream OpenRead(string publicUrl, out string contentType)
    {
        string physicalPath = GetPhysicalPath(publicUrl);
        if (!File.Exists(physicalPath))
            throw new FileNotFoundException("Branding datoteka nije pronađena.", publicUrl);

        string ext = Path.GetExtension(physicalPath).ToLowerInvariant();
        contentType = ExtensionToContentType.TryGetValue(ext, out string ct) ? ct : "application/octet-stream";

        return new FileStream(physicalPath, FileMode.Open, FileAccess.Read, FileShare.Read);
    }

    public string GetPhysicalPath(string publicUrl)
    {
        if (string.IsNullOrWhiteSpace(publicUrl))
            return string.Empty;

        if (!publicUrl.StartsWith(_publicBasePath, StringComparison.OrdinalIgnoreCase))
            return string.Empty;

        string relative = publicUrl.Substring(_publicBasePath.Length).TrimStart('/').Replace('\\', '/');
        if (relative.Contains("..", StringComparison.Ordinal))
            return string.Empty;

        return Path.Combine(_storagePath, relative.Replace('/', Path.DirectorySeparatorChar));
    }

    private static string SanitizeForPath(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "default";

        char[] invalid = Path.GetInvalidFileNameChars().Concat(Path.GetInvalidPathChars()).Distinct().ToArray();
        return new string(value.Where(c => !invalid.Contains(c) && c != '/' && c != '\\' && c != '.').Take(100).ToArray());
    }
}
