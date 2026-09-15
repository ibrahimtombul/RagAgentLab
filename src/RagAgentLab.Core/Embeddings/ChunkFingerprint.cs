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
/// <para>
/// The embedding model is part of the hash for the same reason. Swapping nomic-embed-text for
/// bge-m3 leaves every chunk's text untouched, so a text-only fingerprint would match and the
/// store would keep serving vectors from the old model — which produces 768 numbers where the
/// new one produces 1024, and the first query would fail on a dimension mismatch. Naming the
/// model in the fingerprint turns a model swap into an ordinary re-index.
/// </para>
/// </summary>
public static class ChunkFingerprint
{
    /// <summary>Computes the fingerprint of a chunk as embedded by a particular model.</summary>
    /// <param name="embeddingModel">Identifier of the model that produced the vector.</param>
    /// <param name="text">The exact text that was embedded.</param>
    /// <returns>A lowercase hexadecimal SHA-256 hash.</returns>
    public static string Compute(string embeddingModel, string text)
    {
        ArgumentNullException.ThrowIfNull(embeddingModel);
        ArgumentNullException.ThrowIfNull(text);

        var payload = Encoding.UTF8.GetBytes($"{embeddingModel}\u0000{text}");
        return Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();
    }
}
