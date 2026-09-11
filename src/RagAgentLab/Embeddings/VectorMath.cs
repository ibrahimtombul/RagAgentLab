using System.Numerics.Tensors;

namespace RagAgentLab.Embeddings;

/// <summary>Vector helpers used by the in-memory store.</summary>
public static class VectorMath
{
    /// <summary>
    /// Cosine similarity between two embedding vectors:
    /// <c>cos(a, b) = (a · b) / (‖a‖ · ‖b‖)</c>.
    /// It compares the direction of the two vectors and ignores their magnitude, which is
    /// what we want for semantic similarity: a long passage and a short question about the
    /// same topic should score highly. The result lies in -1..1 (1 = same direction).
    /// </summary>
    /// <param name="left">First vector.</param>
    /// <param name="right">Second vector.</param>
    /// <returns>Cosine similarity, or 0 when either vector has zero length.</returns>
    /// <exception cref="ArgumentException">The vectors have different dimensions.</exception>
    public static double CosineSimilarity(ReadOnlyMemory<float> left, ReadOnlyMemory<float> right)
    {
        if (left.Length != right.Length)
        {
            throw new ArgumentException(
                $"Vector dimensions differ ({left.Length} vs {right.Length}). " +
                "This usually means the store was populated with a different embedding model.");
        }

        if (left.Length == 0)
        {
            return 0d;
        }

        // A zero vector has no direction, so the formula divides by zero and TensorPrimitives
        // returns NaN. NaN would sort unpredictably in the ranking, so it is turned into the
        // "no similarity at all" score instead.
        if (TensorPrimitives.Norm(left.Span) == 0f || TensorPrimitives.Norm(right.Span) == 0f)
        {
            return 0d;
        }

        // TensorPrimitives is the SIMD-accelerated BCL implementation of the formula above.
        return TensorPrimitives.CosineSimilarity(left.Span, right.Span);
    }
}
