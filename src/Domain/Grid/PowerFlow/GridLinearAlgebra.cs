using System.Numerics;

namespace Domain.Grid.PowerFlow;

public static class GridLinearAlgebra
{
    public static double[]? Solve(double[,] matrix, double[] rhs)
    {
        var n = rhs.Length;
        var a = (double[,])matrix.Clone();
        var b = (double[])rhs.Clone();
        for (var k = 0; k < n; k++)
        {
            var pivot = k;
            for (var i = k + 1; i < n; i++) if (Math.Abs(a[i, k]) > Math.Abs(a[pivot, k])) pivot = i;
            if (Math.Abs(a[pivot, k]) < 1e-13) return null;
            for (var j = k; j < n; j++) (a[k, j], a[pivot, j]) = (a[pivot, j], a[k, j]);
            (b[k], b[pivot]) = (b[pivot], b[k]);
            for (var i = k + 1; i < n; i++)
            {
                var ratio = a[i, k] / a[k, k];
                for (var j = k; j < n; j++) a[i, j] -= ratio * a[k, j];
                b[i] -= ratio * b[k];
            }
        }
        var result = new double[n];
        for (var i = n - 1; i >= 0; i--)
        {
            var sum = b[i];
            for (var j = i + 1; j < n; j++) sum -= a[i, j] * result[j];
            result[i] = sum / a[i, i];
        }
        return result;
    }

    public static Complex[]? Solve(Complex[,] matrix, Complex[] rhs)
    {
        var n = rhs.Length;
        var real = new double[n * 2, n * 2];
        var b = new double[n * 2];
        for (var i = 0; i < n; i++)
        {
            b[i] = rhs[i].Real; b[i + n] = rhs[i].Imaginary;
            for (var j = 0; j < n; j++)
            {
                real[i, j] = real[i + n, j + n] = matrix[i, j].Real;
                real[i, j + n] = -matrix[i, j].Imaginary;
                real[i + n, j] = matrix[i, j].Imaginary;
            }
        }
        var x = Solve(real, b);
        return x is null ? null : Enumerable.Range(0, n).Select(i => new Complex(x[i], x[i + n])).ToArray();
    }
}
