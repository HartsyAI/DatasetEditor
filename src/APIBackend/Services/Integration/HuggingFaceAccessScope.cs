using System.Security.Cryptography;
using System.Text;

namespace DatasetStudio.APIBackend.Services.Integration;

/// <summary>
/// Names who an upstream HuggingFace fetch was made as, for use in cache keys. Rows and
/// thumbnails fetched with one user's access token were cached under keys without it and then
/// served to anyone who could read the dataset, so a gated repository's images became readable
/// through a public dataset by people without access (audit H-5). Keying on this scope means a
/// cached entry is only served to a request presenting the same token, which upstream would have
/// served anyway; anonymous requests share one scope. The token itself never appears in a key,
/// only a SHA-256 prefix of it.
/// </summary>
public static class HuggingFaceAccessScope
{
    /// <summary>The scope for requests without a token.</summary>
    public const string Anonymous = "anon";

    /// <summary>A stable, non-reversible cache-key fragment for <paramref name="accessToken"/>.</summary>
    public static string For(string? accessToken)
    {
        if (string.IsNullOrEmpty(accessToken))
        {
            return Anonymous;
        }

        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(accessToken));
        // 128 bits is plenty to keep two tokens apart and keeps keys short.
        return "token:" + Convert.ToHexString(hash, 0, 16);
    }
}
