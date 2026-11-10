using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace DatasetStudio.APIBackend.Services.DatasetManagement;

/// <summary>
/// Deterministic ids for items that are streamed rather than stored (HuggingFace rows, S3
/// objects). They used to get a fresh <see cref="Guid"/> on every page request, so an item's id
/// changed each time it was listed and every edit of it answered 404 (audit E-3). An id is now a
/// name-based UUID: the SHA-256 of a namespaced description of where the item comes from, cut to
/// 16 bytes with the RFC 9562 version 8 and variant bits set. Nothing is stored to make it
/// stable, and the same row always gets the same id, whether it was reached by paging or search.
/// </summary>
public static class StreamingItemIds
{
    /// <summary>
    /// The id of a HuggingFace datasets-server row. <paramref name="rowIndex"/> is the row's index
    /// in its split (<c>row_idx</c>), which <c>/search</c> reports as the original index too, so a
    /// row found by search has the id it has when paged to.
    /// </summary>
    public static Guid ForHuggingFaceRow(Guid datasetId, string repository, string? config, string? split, long rowIndex)
    {
        return NameBased("hf", datasetId, repository, config, split, rowIndex.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>The id of an object in an S3 bucket, by storage profile, bucket and object key.</summary>
    public static Guid ForS3Object(Guid datasetId, string profile, string bucket, string key)
    {
        return NameBased("s3", datasetId, profile, bucket, key);
    }

    /// <summary>
    /// Hashes the parts with each one length-prefixed, so no choice of values can make two
    /// different sets of parts read as the same name (an S3 key may contain any separator).
    /// The dataset id is a part, so two datasets streaming the same source keep separate ids,
    /// and so separate edits.
    /// </summary>
    private static Guid NameBased(string kind, Guid datasetId, params string?[] parts)
    {
        StringBuilder name = new("dataset-studio/streaming-item/v1");
        Append(name, kind);
        Append(name, datasetId.ToString("N"));
        foreach (string? part in parts)
        {
            Append(name, part ?? string.Empty);
        }

        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(name.ToString()));
        Span<byte> bytes = hash.AsSpan(0, 16);
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x80); // version 8: custom (here SHA-256 name-based)
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80); // RFC 9562 variant
        return new Guid(bytes, bigEndian: true);
    }

    private static void Append(StringBuilder name, string part)
    {
        name.Append('|').Append(part.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(part);
    }
}
