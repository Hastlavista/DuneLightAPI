using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BlueDragon.DuneLight.Core.Interfaces.Organization;
using BlueDragon.DuneLight.Core.Shared.Exceptions;
using BlueDragon.DuneLight.Infrastructure.Domain.Settings;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;

namespace BlueDragon.DuneLight.Infrastructure.Services;

public class BrandingFileStorage : IBrandingFileStorage
{
    private readonly BrandingSettings _brandingSettings;
    private readonly ILogger<BrandingFileStorage> _logger;
    private readonly string _storagePath;
    private readonly string _publicBasePath;

    private enum ImageKind
    {
        Unknown,
        Png,
        Jpeg,
        Gif,
        Webp,
        Ico,
        Avif
    }

    public BrandingFileStorage(BrandingSettings brandingSettings, IWebHostEnvironment env, ILogger<BrandingFileStorage> logger)
    {
        _brandingSettings = brandingSettings;
        _logger = logger;

        // Path.Combine ignori(ra) prvi argument ako je StoragePath već apsolutan (i na Windowsu i na Linuxu),
        // pa isti config radi i za relativni dev path ("wwwroot/branding") i za apsolutni produkcijski path
        // ako se ikad odluči izmjestiti izvan app foldera (npr. "/var/dunelight/storage/branding").
        _storagePath = Path.Combine(env.ContentRootPath, _brandingSettings.StoragePath);
        _publicBasePath = _brandingSettings.PublicBasePath.TrimEnd('/');

        Directory.CreateDirectory(_storagePath);
    }

    public async Task<string> SaveAsync(Stream content, string originalFileName, string declaredContentType, string organizationSlug, string filePrefix)
    {
        string ext = Path.GetExtension(originalFileName).ToLowerInvariant();
        if (string.IsNullOrEmpty(ext) || !_brandingSettings.AllowedExtensions.Contains(ext.TrimStart('.'), StringComparer.OrdinalIgnoreCase))
            throw new ValidationAppException($"Nije dozvoljena ekstenzija '{ext}'. Dozvoljeno: {string.Join(", ", _brandingSettings.AllowedExtensions)}.");

        byte[] bytes = await ReadAllBytesWithLimitAsync(content, _brandingSettings.MaxFileSizeBytes);

        if (LooksLikeMarkup(bytes))
            throw new ValidationAppException("SVG/HTML datoteke nisu dopuštene, bez obzira na ekstenziju ili deklarirani tip.");

        ImageKind detectedKind = DetectImageKind(bytes);
        if (detectedKind == ImageKind.Unknown)
            throw new ValidationAppException("Sadržaj datoteke ne odgovara nijednom dopuštenom slikovnom formatu.");

        if (!ExtensionMatchesKind(ext, detectedKind))
            throw new ValidationAppException("Stvarni sadržaj datoteke ne odgovara njezinoj ekstenziji.");

        if (!string.IsNullOrWhiteSpace(declaredContentType) && DeclaredContentTypeIsSuspicious(declaredContentType, detectedKind))
            throw new ValidationAppException("Deklarirani tip datoteke ne odgovara njezinom stvarnom sadržaju.");

        string safeSlug = SanitizeForPath(organizationSlug);
        string uniqueName = $"{filePrefix}-{Guid.NewGuid():N}{ext}";

        string orgDir = Path.Combine(_storagePath, safeSlug);
        Directory.CreateDirectory(orgDir);

        string physicalPath = Path.Combine(orgDir, uniqueName);
        await File.WriteAllBytesAsync(physicalPath, bytes);

        return $"{_publicBasePath}/{safeSlug}/{uniqueName}";
    }

    /// <summary>
    /// Best-effort brisanje: nikad ne baca. Neuspjeh se logira kao upozorenje za kasnije ručno čišćenje, jer
    /// ne smije srušiti inače uspješan zahtjev (npr. upload nove datoteke nakon uspješnog DB updatea).
    /// </summary>
    public async Task DeleteAsync(string publicUrl)
    {
        if (string.IsNullOrWhiteSpace(publicUrl))
            return;

        if (!publicUrl.StartsWith(_publicBasePath, StringComparison.OrdinalIgnoreCase))
            return;

        string physicalPath = GetPhysicalPath(publicUrl);
        if (string.IsNullOrEmpty(physicalPath))
            return;

        try
        {
            if (File.Exists(physicalPath))
                await Task.Run(() => File.Delete(physicalPath));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Brisanje branding datoteke nije uspjelo, potrebno ručno čišćenje: {PublicUrl}", publicUrl);
        }
    }

    public Stream OpenRead(string publicUrl, out string contentType)
    {
        string physicalPath = GetPhysicalPath(publicUrl);
        if (!File.Exists(physicalPath))
            throw new FileNotFoundException("Branding datoteka nije pronađena.", publicUrl);

        byte[] header = ReadHeader(physicalPath);
        ImageKind kind = DetectImageKind(header);
        contentType = kind != ImageKind.Unknown
            ? ContentTypeForKind(kind)
            : "application/octet-stream";

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

        string candidate = Path.GetFullPath(Path.Combine(_storagePath, relative.Replace('/', Path.DirectorySeparatorChar)));
        string root = Path.GetFullPath(_storagePath);

        // Dodatna provjera protiv path traversala nakon normalizacije (npr. enkodirani separatori) —
        // rezultat mora ostati fizički unutar branding root direktorija.
        if (!candidate.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) && candidate != root)
            return string.Empty;

        return candidate;
    }

    private static async Task<byte[]> ReadAllBytesWithLimitAsync(Stream content, long maxSizeBytes)
    {
        await using MemoryStream buffer = new MemoryStream();
        byte[] chunk = new byte[81920];
        long total = 0;
        int read;
        while ((read = await content.ReadAsync(chunk.AsMemory(0, chunk.Length))) > 0)
        {
            total += read;
            if (total > maxSizeBytes)
                throw new ValidationAppException($"Datoteka je prevelika. Maksimalna veličina: {maxSizeBytes / 1024 / 1024}MB.");

            await buffer.WriteAsync(chunk.AsMemory(0, read));
        }

        return buffer.ToArray();
    }

    private static byte[] ReadHeader(string physicalPath)
    {
        byte[] header = new byte[32];
        using FileStream fileStream = new FileStream(physicalPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        int read = fileStream.Read(header, 0, header.Length);
        return read == header.Length ? header : header.Take(read).ToArray();
    }

    /// <summary>
    /// Odbija SVG i HTML/XML poliglote bez obzira na deklarirani MIME ili ekstenziju — provjerava prvih par
    /// stotina bajtova (nakon whitespace/BOM-a) za tekstualni markup marker, jer PNG/JPEG/GIF/WEBP/ICO/AVIF
    /// magic bytes nikad ne počinju ovako.
    /// </summary>
    private static bool LooksLikeMarkup(byte[] bytes)
    {
        int sampleLength = Math.Min(bytes.Length, 512);
        string text = Encoding.UTF8.GetString(bytes, 0, sampleLength).TrimStart('﻿', ' ', '\t', '\r', '\n');
        return text.StartsWith("<", StringComparison.Ordinal);
    }

    private static ImageKind DetectImageKind(byte[] b)
    {
        if (b.Length >= 8 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47
            && b[4] == 0x0D && b[5] == 0x0A && b[6] == 0x1A && b[7] == 0x0A)
            return ImageKind.Png;

        if (b.Length >= 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF)
            return ImageKind.Jpeg;

        if (b.Length >= 6 && b[0] == 0x47 && b[1] == 0x49 && b[2] == 0x46 && b[3] == 0x38
            && (b[4] == 0x37 || b[4] == 0x39) && b[5] == 0x61)
            return ImageKind.Gif;

        if (b.Length >= 12 && b[0] == 0x52 && b[1] == 0x49 && b[2] == 0x46 && b[3] == 0x46
            && b[8] == 0x57 && b[9] == 0x45 && b[10] == 0x42 && b[11] == 0x50)
            return ImageKind.Webp;

        if (b.Length >= 4 && b[0] == 0x00 && b[1] == 0x00 && b[2] == 0x01 && b[3] == 0x00)
            return ImageKind.Ico;

        if (b.Length >= 12 && b[4] == 0x66 && b[5] == 0x74 && b[6] == 0x79 && b[7] == 0x70)
        {
            string brand = Encoding.ASCII.GetString(b, 8, Math.Min(4, b.Length - 8));
            if (brand is "avif" or "avis")
                return ImageKind.Avif;
        }

        return ImageKind.Unknown;
    }

    private static bool ExtensionMatchesKind(string extWithDot, ImageKind kind)
    {
        return extWithDot switch
        {
            ".png" => kind == ImageKind.Png,
            ".jpg" or ".jpeg" => kind == ImageKind.Jpeg,
            ".gif" => kind == ImageKind.Gif,
            ".webp" => kind == ImageKind.Webp,
            ".ico" => kind == ImageKind.Ico,
            ".avif" => kind == ImageKind.Avif,
            _ => false
        };
    }

    private static string ContentTypeForKind(ImageKind kind)
    {
        return kind switch
        {
            ImageKind.Png => "image/png",
            ImageKind.Jpeg => "image/jpeg",
            ImageKind.Gif => "image/gif",
            ImageKind.Webp => "image/webp",
            ImageKind.Ico => "image/x-icon",
            ImageKind.Avif => "image/avif",
            _ => "application/octet-stream"
        };
    }

    /// <summary>
    /// Ne zahtijeva strogo poklapanje (browseri/klijenti šalju različite sinonime za isti tip, npr.
    /// "image/x-icon" vs. "image/vnd.microsoft.icon", ili generički "application/octet-stream" za manje
    /// uobičajene tipove poput .avif) — samo odbija deklaracije koje jasno upućuju na drugačiji, potencijalno
    /// opasan sadržaj (svg/html/xml/script), ili nedvosmisleno na drugi slikovni tip od stvarno detektiranog.
    /// </summary>
    private static bool DeclaredContentTypeIsSuspicious(string declaredContentType, ImageKind detectedKind)
    {
        string normalized = declaredContentType.Trim().ToLowerInvariant();
        bool looksLikeMarkupType = normalized.Contains("svg") || normalized.Contains("html") || normalized.Contains("xml")
            || normalized.Contains("javascript") || normalized.Contains("script");

        if (looksLikeMarkupType)
            return true;

        if (!normalized.StartsWith("image/", StringComparison.Ordinal))
            return false;

        return !AcceptedContentTypesForKind(detectedKind).Contains(normalized);
    }

    private static readonly System.Collections.Generic.Dictionary<ImageKind, string[]> AcceptedContentTypes = new()
    {
        [ImageKind.Png] = new[] { "image/png" },
        [ImageKind.Jpeg] = new[] { "image/jpeg", "image/jpg", "image/pjpeg" },
        [ImageKind.Gif] = new[] { "image/gif" },
        [ImageKind.Webp] = new[] { "image/webp" },
        [ImageKind.Ico] = new[] { "image/x-icon", "image/vnd.microsoft.icon" },
        [ImageKind.Avif] = new[] { "image/avif" }
    };

    private static string[] AcceptedContentTypesForKind(ImageKind kind)
    {
        return AcceptedContentTypes.TryGetValue(kind, out string[] types) ? types : Array.Empty<string>();
    }

    private static string SanitizeForPath(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "default";

        char[] invalid = Path.GetInvalidFileNameChars().Concat(Path.GetInvalidPathChars()).Distinct().ToArray();
        return new string(value.Where(c => !invalid.Contains(c) && c != '/' && c != '\\' && c != '.').Take(100).ToArray());
    }
}
