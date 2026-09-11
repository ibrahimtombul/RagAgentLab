using RagAgentLab.Embeddings;

namespace RagAgentLab.Tests;

/// <summary>Tests for the similarity metric the vector store ranks by.</summary>
public sealed class VectorMathTests
{
    [Fact]
    public void CosineSimilarity_IsOneForIdenticalDirection()
    {
        var vector = new[] { 1f, 2f, 3f }.AsMemory();

        Assert.Equal(1d, VectorMath.CosineSimilarity(vector, vector), precision: 5);
    }

    [Fact]
    public void CosineSimilarity_IgnoresMagnitude()
    {
        // The point of cosine over Euclidean distance: a long passage and a short question
        // about the same topic must still score as similar.
        var shortVector = new[] { 1f, 2f, 3f }.AsMemory();
        var longVector = new[] { 10f, 20f, 30f }.AsMemory();

        Assert.Equal(1d, VectorMath.CosineSimilarity(shortVector, longVector), precision: 5);
    }

    [Fact]
    public void CosineSimilarity_IsZeroForOrthogonalVectors() =>
        Assert.Equal(0d, VectorMath.CosineSimilarity(
            new[] { 1f, 0f }.AsMemory(), new[] { 0f, 1f }.AsMemory()), precision: 5);

    [Fact]
    public void CosineSimilarity_IsMinusOneForOppositeDirection() =>
        Assert.Equal(-1d, VectorMath.CosineSimilarity(
            new[] { 1f, 2f }.AsMemory(), new[] { -1f, -2f }.AsMemory()), precision: 5);

    [Fact]
    public void CosineSimilarity_IsZeroWhenAVectorHasNoLength() =>
        Assert.Equal(0d, VectorMath.CosineSimilarity(
            new[] { 0f, 0f }.AsMemory(), new[] { 1f, 2f }.AsMemory()), precision: 5);

    [Fact]
    public void CosineSimilarity_ThrowsWhenDimensionsDiffer()
    {
        // In practice this means the store was populated with one embedding model and is
        // being queried with another - worth failing loudly rather than ranking nonsense.
        var exception = Assert.Throws<ArgumentException>(() => VectorMath.CosineSimilarity(
            new[] { 1f, 2f, 3f }.AsMemory(), new[] { 1f, 2f }.AsMemory()));

        Assert.Contains("dimensions differ", exception.Message, StringComparison.OrdinalIgnoreCase);
    }
}
