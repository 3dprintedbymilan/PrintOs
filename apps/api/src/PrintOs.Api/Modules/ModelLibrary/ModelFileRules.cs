using System.Buffers.Binary;

namespace PrintOs.Api.Modules.ModelLibrary;

public sealed class ModelLibraryOptions
{
    public long MaxModelFileBytes { get; set; } = 100L * 1024 * 1024;

    public long MaxThumbnailBytes { get; set; } = 2L * 1024 * 1024;

    public int DownloadUrlMinutes { get; set; } = 5;
}

public static class ModelFileRules
{
    public const long RequestSizeCeilingBytes = 150L * 1024 * 1024;

    public static readonly string[] ModelExtensions = [".stl", ".3mf", ".obj"];

    public static string? ModelFormat(string fileName) =>
        Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".stl" => "stl",
            ".3mf" => "3mf",
            ".obj" => "obj",
            _ => null,
        };

    public static string ModelContentType(string format) =>
        format switch
        {
            "3mf" => "model/3mf",
            "stl" => "model/stl",
            _ => "application/octet-stream",
        };

    public static bool LooksLikeModel(string format, ReadOnlySpan<byte> header, long length) =>
        format switch
        {
            "3mf" => header.Length >= 4 && header[0] == 0x50 && header[1] == 0x4B && header[2] == 0x03 && header[3] == 0x04,
            "stl" => LooksLikeStl(header, length),
            "obj" => header.Length > 0 && header.IndexOf((byte)0) < 0,
            _ => false,
        };

    private static bool LooksLikeStl(ReadOnlySpan<byte> header, long length)
    {
        if (header.Length >= 84)
        {
            var triangles = BinaryPrimitives.ReadUInt32LittleEndian(header[80..84]);
            if (84 + (50L * triangles) == length)
            {
                return true;
            }
        }

        return header.Length >= 5
            && header[..5].SequenceEqual("solid"u8)
            && header.IndexOf((byte)0) < 0;
    }

    public static string? ImageContentType(ReadOnlySpan<byte> header)
    {
        if (header.Length >= 8 && header[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }))
        {
            return "image/png";
        }

        if (header.Length >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF)
        {
            return "image/jpeg";
        }

        if (header.Length >= 12 && header[..4].SequenceEqual("RIFF"u8) && header.Slice(8, 4).SequenceEqual("WEBP"u8))
        {
            return "image/webp";
        }

        return null;
    }

    public static string ImageExtension(string contentType) =>
        contentType switch
        {
            "image/png" => ".png",
            "image/webp" => ".webp",
            _ => ".jpg",
        };
}
