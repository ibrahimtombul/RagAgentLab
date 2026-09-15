using System.Security.Cryptography;
using System.Text;

namespace RagAgentLab.Embeddings;

/// <summary>
/// Content fingerprint of a chunk, used to decide whether it needs re-embedding.
/// <para>
/// A chunk id such as <c>01-izin-politikasi.txt#3</c> says where a chunk came from but not
/// what it says, so an edited document would keep the same ids and silently keep the old
/// vectors. Hashing the text is what makes "only embed what changed" correct rather than
/// merely fast.
/// </para>
/// </summary>
public static class ChunkFingerprint
{
    /// <summary>Computes the fingerprint of a chunk's text.</summary>
    /// <param name="text">The chunk text.</param>
    /// <returns>A lowercase hexadecimal SHA-256 hash.</returns>
    public static string Compute(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    }
}
