namespace RagAgentLab.Rag;

/// <summary>
/// Projects high-dimensional vectors down to two dimensions so they can be drawn.
/// <para>
/// An embedding has 768 numbers in it, which is 768 axes — nothing anyone can picture. Principal
/// component analysis finds the two directions along which the collection of vectors actually
/// varies the most, and measures every vector along just those two. What is left is a flat map:
/// distances on it are a shadow of the real distances, but points that sit together on the map
/// really are close in the full space, which is enough to see how a corpus is arranged.
/// </para>
/// <para>
/// Implemented with power iteration on the Gram matrix rather than a full eigendecomposition.
/// There are far fewer vectors than dimensions here, so the n×n Gram matrix is tiny and the two
/// leading components fall out of a few dozen iterations.
/// </para>
/// <para>
/// Fitting and projecting are separate steps on purpose. The axes should be chosen to spread out
/// the thing being examined — the corpus — and anything else can then be placed against those
/// same axes. Fitting on corpus and questions together produced a map whose main axis was simply
/// "document or question", because the embedding model marks the two with different task
/// prefixes and puts them in different regions; the documents all collapsed into one blob.
/// </para>
/// </summary>
public static class PrincipalComponentProjector
{
    private const int Iterations = 200;

    /// <summary>
    /// Finds the two axes along which a set of vectors varies most, so other vectors can then be
    /// placed against them.
    /// </summary>
    /// <param name="vectors">Vectors of equal length; at least two.</param>
    /// <returns>A projection that can flatten any vector of the same length.</returns>
    /// <exception cref="ArgumentException">Fewer than two vectors, or ragged lengths.</exception>
    public static Projection Fit(IReadOnlyList<ReadOnlyMemory<float>> vectors)
    {
        ArgumentNullException.ThrowIfNull(vectors);
        if (vectors.Count < 2)
        {
            throw new ArgumentException("At least two vectors are needed.", nameof(vectors));
        }

        var dimensions = vectors[0].Length;
        if (vectors.Any(v => v.Length != dimensions))
        {
            throw new ArgumentException("All vectors must have the same length.", nameof(vectors));
        }

        var count = vectors.Count;

        // Centre the cloud: PCA describes variation around the mean, not position.
        var mean = new double[dimensions];
        foreach (var vector in vectors)
        {
            var span = vector.Span;
            for (var d = 0; d < dimensions; d++)
            {
                mean[d] += span[d];
            }
        }

        for (var d = 0; d < dimensions; d++)
        {
            mean[d] /= count;
        }

        var centred = new double[count][];
        for (var i = 0; i < count; i++)
        {
            var span = vectors[i].Span;
            centred[i] = new double[dimensions];
            for (var d = 0; d < dimensions; d++)
            {
                centred[i][d] = span[d] - mean[d];
            }
        }

        // Gram matrix: cheaper than the 768×768 covariance when there are only a few dozen vectors.
        var gram = new double[count][];
        for (var i = 0; i < count; i++)
        {
            gram[i] = new double[count];
            for (var j = 0; j < count; j++)
            {
                double sum = 0;
                for (var d = 0; d < dimensions; d++)
                {
                    sum += centred[i][d] * centred[j][d];
                }

                gram[i][j] = sum;
            }
        }

        var (first, firstValue) = DominantEigenvector(gram, count);
        Deflate(gram, count, first, firstValue);
        var (second, secondValue) = DominantEigenvector(gram, count);

        // The Gram matrix works in sample space, so each component has to be carried back into
        // the 768-dimensional feature space before anything else can be measured against it:
        // w = Xcᵀu / √λ.
        return new Projection(
            mean,
            ToFeatureSpace(centred, first, firstValue, dimensions),
            ToFeatureSpace(centred, second, secondValue, dimensions));
    }

    private static double[] ToFeatureSpace(double[][] centred, double[] eigenvector, double eigenvalue, int dimensions)
    {
        var axis = new double[dimensions];
        var scale = Math.Sqrt(Math.Max(eigenvalue, 1e-12));

        for (var i = 0; i < centred.Length; i++)
        {
            for (var d = 0; d < dimensions; d++)
            {
                axis[d] += centred[i][d] * eigenvector[i];
            }
        }

        for (var d = 0; d < dimensions; d++)
        {
            axis[d] /= scale;
        }

        return axis;
    }

    /// <summary>Two axes through the embedding space, and the point they are measured from.</summary>
    public sealed class Projection
    {
        private readonly double[] _mean;
        private readonly double[] _axisX;
        private readonly double[] _axisY;

        internal Projection(double[] mean, double[] axisX, double[] axisY)
        {
            _mean = mean;
            _axisX = axisX;
            _axisY = axisY;
        }

        /// <summary>Flattens one vector onto the two axes.</summary>
        /// <param name="vector">A vector of the same length the projection was fitted on.</param>
        /// <returns>Its coordinates on the map.</returns>
        /// <exception cref="ArgumentException">The vector has the wrong length.</exception>
        public (double X, double Y) Transform(ReadOnlyMemory<float> vector)
        {
            if (vector.Length != _mean.Length)
            {
                throw new ArgumentException(
                    $"Expected {_mean.Length} dimensions, got {vector.Length}.", nameof(vector));
            }

            var span = vector.Span;
            double x = 0;
            double y = 0;

            for (var d = 0; d < _mean.Length; d++)
            {
                var centred = span[d] - _mean[d];
                x += centred * _axisX[d];
                y += centred * _axisY[d];
            }

            return (x, y);
        }
    }

    /// <summary>Finds the largest eigenvalue and its eigenvector by repeated multiplication.</summary>
    private static (double[] Vector, double Value) DominantEigenvector(double[][] matrix, int size)
    {
        // A fixed starting point keeps the output identical from run to run.
        var vector = Enumerable.Range(0, size).Select(i => Math.Sin(i + 1.0)).ToArray();
        Normalise(vector);

        for (var iteration = 0; iteration < Iterations; iteration++)
        {
            var next = new double[size];
            for (var i = 0; i < size; i++)
            {
                double sum = 0;
                for (var j = 0; j < size; j++)
                {
                    sum += matrix[i][j] * vector[j];
                }

                next[i] = sum;
            }

            if (Normalise(next) == 0)
            {
                break;
            }

            vector = next;
        }

        // Rayleigh quotient, which for a unit eigenvector is just vᵀ M v.
        double value = 0;
        for (var i = 0; i < size; i++)
        {
            double sum = 0;
            for (var j = 0; j < size; j++)
            {
                sum += matrix[i][j] * vector[j];
            }

            value += vector[i] * sum;
        }

        return (vector, value);
    }

    /// <summary>Removes a component from the matrix so the next iteration finds the one after it.</summary>
    private static void Deflate(double[][] matrix, int size, double[] eigenvector, double eigenvalue)
    {
        for (var i = 0; i < size; i++)
        {
            for (var j = 0; j < size; j++)
            {
                matrix[i][j] -= eigenvalue * eigenvector[i] * eigenvector[j];
            }
        }
    }

    /// <summary>Scales a vector to unit length in place; returns the length it had.</summary>
    private static double Normalise(double[] vector)
    {
        var length = Math.Sqrt(vector.Sum(x => x * x));
        if (length == 0)
        {
            return 0;
        }

        for (var i = 0; i < vector.Length; i++)
        {
            vector[i] /= length;
        }

        return length;
    }
}
