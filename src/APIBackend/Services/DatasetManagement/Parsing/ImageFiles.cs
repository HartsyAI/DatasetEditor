using SkiaSharp;

namespace DatasetStudio.APIBackend.Services.DatasetManagement.Parsing;

/// <summary>
/// Which files ingestion treats as images, and how it reads their size. Folder and ZIP imports
/// used to accept <c>.tif</c>/<c>.tiff</c> too, which nothing else in the app (file serving,
/// thumbnails, browsers) handles as an image, so those items showed as broken (audit F-3).
/// </summary>
public static class ImageFiles
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp"
    };

    /// <summary>Whether a file name has an image extension the app can serve and thumbnail.</summary>
    public static bool IsSupported(string fileName)
    {
        return SupportedExtensions.Contains(Path.GetExtension(fileName));
    }

    /// <summary>
    /// The extension for image bytes, from their signature, or null when they are not an image
    /// format the app supports. Embedded images (Parquet <c>image.bytes</c>) carry no reliable name.
    /// </summary>
    public static string? SniffExtension(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 8 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47)
        {
            return ".png";
        }
        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
        {
            return ".jpg";
        }
        if (bytes.Length >= 4 && bytes[0] == (byte)'G' && bytes[1] == (byte)'I' && bytes[2] == (byte)'F' && bytes[3] == (byte)'8')
        {
            return ".gif";
        }
        if (bytes.Length >= 12 && bytes[0] == (byte)'R' && bytes[1] == (byte)'I' && bytes[2] == (byte)'F' && bytes[3] == (byte)'F'
            && bytes[8] == (byte)'W' && bytes[9] == (byte)'E' && bytes[10] == (byte)'B' && bytes[11] == (byte)'P')
        {
            return ".webp";
        }
        if (bytes.Length >= 2 && bytes[0] == (byte)'B' && bytes[1] == (byte)'M')
        {
            return ".bmp";
        }
        return null;
    }

    /// <summary>
    /// Reads width and height from the image header without decoding pixels. Returns (0, 0) when
    /// the file isn't a readable image, matching what ingestion stored before it probed at all.
    /// </summary>
    public static (int Width, int Height) ReadDimensions(string path)
    {
        try
        {
            using SKCodec? codec = SKCodec.Create(path);
            return codec is null ? (0, 0) : (codec.Info.Width, codec.Info.Height);
        }
        catch (Exception)
        {
            return (0, 0);
        }
    }

    /// <summary>Same as <see cref="ReadDimensions(string)"/> for image bytes already in memory.</summary>
    public static (int Width, int Height) ReadDimensions(byte[] bytes)
    {
        try
        {
            using SKData data = SKData.CreateCopy(bytes);
            using SKCodec? codec = SKCodec.Create(data);
            return codec is null ? (0, 0) : (codec.Info.Width, codec.Info.Height);
        }
        catch (Exception)
        {
            return (0, 0);
        }
    }
}
