using System.IO.Compression;
using System.Text.RegularExpressions;
using DatasetStudio.Core.Utilities.Logging;

namespace DatasetStudio.Core.Utilities.Helpers;

/// <summary>Utility class for handling ZIP file operations including extraction, validation, and multi-part detection.</summary>
public static class ZipHelpers
{
    /// <summary>Supported dataset file extensions.</summary>
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".csv", ".tsv", ".txt",
        ".csv000", ".tsv000", ".csv001", ".tsv001", // Multi-part files
        ".json", ".jsonl" // Future support
    };

    /// <summary>Extracts all dataset files from a ZIP archive into memory streams.</summary>
    /// <param name="zipStream">Stream containing the ZIP archive.</param>
    /// <returns>Dictionary of filename to content stream.</returns>
    public static async Task<Dictionary<string, MemoryStream>> ExtractDatasetFilesAsync(Stream zipStream)
    {
        Dictionary<string, MemoryStream> extractedFiles = new();

        try
        {
            using ZipArchive archive = new(zipStream, ZipArchiveMode.Read, leaveOpen: true);

            Logs.Info($"ZIP archive contains {archive.Entries.Count} entries");

            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                // Skip directories
                if (string.IsNullOrEmpty(entry.Name) || entry.FullName.EndsWith("/"))
                {
                    continue;
                }

                // Check if it's a dataset file
                string extension = Path.GetExtension(entry.Name);
                if (!SupportedExtensions.Contains(extension))
                {
                    Logs.Info($"Skipping non-dataset file: {entry.Name}");
                    continue;
                }

                Logs.Info($"Extracting: {entry.Name} ({entry.Length} bytes)");

                // Extract to memory stream
                MemoryStream ms = new();
                using (Stream entryStream = entry.Open())
                {
                    await entryStream.CopyToAsync(ms);
                }
                ms.Position = 0;

                extractedFiles[entry.Name] = ms;
            }

            Logs.Info($"Extracted {extractedFiles.Count} dataset files from ZIP");
            return extractedFiles;
        }
        catch (Exception ex)
        {
            // Cleanup on error
            foreach (var stream in extractedFiles.Values)
            {
                stream.Dispose();
            }

            Logs.Error("Failed to extract ZIP file", ex);
            throw new InvalidOperationException($"Failed to extract ZIP file: {ex.Message}", ex);
        }
    }

    /// <summary>Checks if a stream is a valid ZIP archive.</summary>
    public static bool IsZipFile(Stream stream)
    {
        if (stream == null || !stream.CanRead || !stream.CanSeek)
        {
            return false;
        }

        long originalPosition = stream.Position;

        try
        {
            stream.Position = 0;

            // Check for ZIP magic number (PK\x03\x04)
            byte[] header = new byte[4];
            int bytesRead = stream.Read(header, 0, 4);

            stream.Position = originalPosition;

            return bytesRead == 4 &&
                   header[0] == 0x50 && // 'P'
                   header[1] == 0x4B && // 'K'
                   (header[2] == 0x03 || header[2] == 0x05) && // \x03 or \x05
                   (header[3] == 0x04 || header[3] == 0x06);   // \x04 or \x06
        }
        catch
        {
            stream.Position = originalPosition;
            return false;
        }
    }

    /// <summary>IsZipFile by extension.</summary>
    public static bool IsZipFile(string filename)
    {
        return Path.GetExtension(filename).Equals(".zip", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Detects multi-part files (e.g., photos.csv000, photos.csv001, photos.csv002).</summary>
    /// <param name="filenames">List of filenames to analyze.</param>
    /// <returns>Dictionary of base filename to list of parts in order.</returns>
    public static Dictionary<string, List<string>> DetectMultiPartFiles(IEnumerable<string> filenames)
    {
        Dictionary<string, List<string>> multiPartGroups = new();

        // Regex to match files ending in digits (e.g., .csv000, .tsv001)
        Regex multiPartPattern = new(@"^(.+)\.(csv|tsv)(\d{3,})$", RegexOptions.IgnoreCase);

        foreach (string filename in filenames)
        {
            Match match = multiPartPattern.Match(filename);

            if (match.Success)
            {
                string baseName = match.Groups[1].Value;
                string extension = match.Groups[2].Value;
                string partNumber = match.Groups[3].Value;

                string key = $"{baseName}.{extension}";

                if (!multiPartGroups.ContainsKey(key))
                {
                    multiPartGroups[key] = new List<string>();
                }

                multiPartGroups[key].Add(filename);
            }
        }

        // Sort each group by part number
        foreach (var group in multiPartGroups.Values)
        {
            group.Sort(StringComparer.OrdinalIgnoreCase);
        }

        // Remove single-file "groups"
        return multiPartGroups.Where(kvp => kvp.Value.Count > 1)
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
    }

    /// <summary>Merges multiple part files into a single stream.</summary>
    /// <param name="partStreams">Dictionary of filename to stream, in order.</param>
    /// <param name="skipHeadersAfterFirst">If true, skips header row in subsequent parts (for CSV/TSV).</param>
    /// <returns>Merged stream.</returns>
    public static async Task<MemoryStream> MergePartFilesAsync(
        List<(string filename, Stream stream)> partStreams,
        bool skipHeadersAfterFirst = true)
    {
        if (partStreams.Count == 0)
        {
            throw new ArgumentException("No part files provided", nameof(partStreams));
        }

        if (partStreams.Count == 1)
        {
            // Single part, just copy it
            MemoryStream single = new();
            partStreams[0].stream.Position = 0;
            await partStreams[0].stream.CopyToAsync(single);
            single.Position = 0;
            return single;
        }

        Logs.Info($"Merging {partStreams.Count} part files...");

        MemoryStream merged = new();
        StreamWriter writer = new(merged, leaveOpen: true);

        bool isFirstPart = true;

        foreach (var (filename, stream) in partStreams)
        {
            stream.Position = 0;
            StreamReader reader = new(stream);

            string? line;
            bool isFirstLine = true;

            while ((line = await reader.ReadLineAsync()) != null)
            {
                // Skip header in subsequent parts if requested
                if (!isFirstPart && isFirstLine && skipHeadersAfterFirst)
                {
                    isFirstLine = false;
                    continue;
                }

                await writer.WriteLineAsync(line);
                isFirstLine = false;
            }

            isFirstPart = false;
            Logs.Info($"Merged part: {filename}");
        }

        await writer.FlushAsync();
        merged.Position = 0;

        Logs.Info($"Merge complete: {merged.Length} bytes");
        return merged;
    }

    /// <summary>Estimates the decompressed size of a ZIP archive.</summary>
    public static long EstimateDecompressedSize(Stream zipStream)
    {
        long originalPosition = zipStream.Position;

        try
        {
            zipStream.Position = 0;
            using ZipArchive archive = new(zipStream, ZipArchiveMode.Read, leaveOpen: true);

            long totalSize = archive.Entries.Sum(e => e.Length);
            return totalSize;
        }
        catch
        {
            return -1; // Unknown
        }
        finally
        {
            zipStream.Position = originalPosition;
        }
    }

    /// <summary>
    /// Validates that a ZIP file contains at least one dataset file.
    /// </summary>
    public static bool ContainsDatasetFiles(Stream zipStream)
    {
        long originalPosition = zipStream.Position;

        try
        {
            zipStream.Position = 0;
            using ZipArchive archive = new(zipStream, ZipArchiveMode.Read, leaveOpen: true);

            return archive.Entries.Any(e =>
                !string.IsNullOrEmpty(e.Name) &&
                SupportedExtensions.Contains(Path.GetExtension(e.Name)));
        }
        catch
        {
            return false;
        }
        finally
        {
            zipStream.Position = originalPosition;
        }
    }
}
