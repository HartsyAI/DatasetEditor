using Microsoft.Win32.SafeHandles;

namespace DatasetStudio.APIBackend.DataAccess.Parquet;

/// <summary>
/// The id index beside a segment (<c>{segment}.ids</c>): every item id in the segment with its
/// row, sorted by id, so finding an item is a binary search instead of a scan. Finding an item
/// used to read the id column of every row group of every dataset on the server (audit P-2).
/// </summary>
/// <remarks>
/// Layout: a 4-byte entry count, then fixed 20-byte entries (16-byte id, 4-byte row), sorted by
/// id bytes. The index is written next to its segment before the manifest commit that publishes
/// the segment, through a temp file and rename, so a published segment always has a complete one.
/// A segment's rows never move (edits rewrite values in place), so its index never goes stale.
/// </remarks>
public static class SegmentIdIndex
{
    private const int EntrySize = 20;

    /// <summary>The index file name for a segment file name.</summary>
    public static string FileNameFor(string segmentFile) => segmentFile + ".ids";

    /// <summary>Writes the index for ids given in row order.</summary>
    public static async Task WriteAsync(string path, IReadOnlyList<Guid> idsInRowOrder, CancellationToken cancellationToken)
    {
        (Guid Id, int Row)[] entries = new (Guid, int)[idsInRowOrder.Count];
        for (int row = 0; row < entries.Length; row++)
        {
            entries[row] = (idsInRowOrder[row], row);
        }
        Array.Sort(entries, (a, b) => CompareIds(a.Id, b.Id));

        byte[] buffer = new byte[4 + (entries.Length * EntrySize)];
        BitConverter.TryWriteBytes(buffer.AsSpan(0, 4), entries.Length);
        for (int i = 0; i < entries.Length; i++)
        {
            Span<byte> entry = buffer.AsSpan(4 + (i * EntrySize), EntrySize);
            entries[i].Id.TryWriteBytes(entry[..16]);
            BitConverter.TryWriteBytes(entry[16..], entries[i].Row);
        }

        string tempPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllBytesAsync(tempPath, buffer, cancellationToken);
            File.Move(tempPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    /// <summary>The row of <paramref name="id"/> in the segment, or null when it isn't there.</summary>
    public static int? Find(string path, Guid id)
    {
        using SafeFileHandle handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        Span<byte> countBytes = stackalloc byte[4];
        if (RandomAccess.Read(handle, countBytes, 0) != 4)
        {
            return null;
        }

        int count = BitConverter.ToInt32(countBytes);
        Span<byte> entry = stackalloc byte[EntrySize];
        int low = 0;
        int high = count - 1;
        while (low <= high)
        {
            int mid = low + ((high - low) / 2);
            RandomAccess.Read(handle, entry, 4 + ((long)mid * EntrySize));
            int comparison = CompareIds(new Guid(entry[..16]), id);
            if (comparison == 0)
            {
                return BitConverter.ToInt32(entry[16..]);
            }
            if (comparison < 0)
            {
                low = mid + 1;
            }
            else
            {
                high = mid - 1;
            }
        }
        return null;
    }

    /// <summary>Byte-order comparison, matching the order entries are written in.</summary>
    private static int CompareIds(Guid a, Guid b)
    {
        Span<byte> left = stackalloc byte[16];
        Span<byte> right = stackalloc byte[16];
        a.TryWriteBytes(left);
        b.TryWriteBytes(right);
        return left.SequenceCompareTo(right);
    }
}
