using System;
using System.Diagnostics;
using System.Linq; // for Take
using MathNet.Numerics.LinearAlgebra;
using MathNet.Numerics.LinearAlgebra.Double;

namespace OR_SSA_Dissertation
{
    public static class SsaBaseline
    {
        public static (double mse, double wallTimeSec) Run(double[] series, int r, int window)
        {
            var sw = Stopwatch.StartNew();

            int n = series.Length;
            int k = n - window + 1;

            // 1. Embed trajectory matrix
            var X = Matrix.Build.Dense(window, k, (i, j) => series[i + j]);

            // 2. SVD
            var svd = X.Svd(computeVectors: true);

            // 3. Rank-r approximation
            var U = svd.U.SubMatrix(0, window, 0, r);
            var S = DiagonalMatrix.OfDiagonal(r, r, svd.S.Take(r).ToArray());
            var Vt = svd.VT.SubMatrix(0, r, 0, k);

            var Xr = U * S * Vt;

            // 4. Diagonal averaging
            var recon = new double[n];
            var counts = new int[n];

            for (int i = 0; i < window; i++)
            for (int j = 0; j < k; j++)
            {
                recon[i + j] += Xr[i, j];
                counts[i + j]++;
            }

            for (int i = 0; i < n; i++) recon[i] /= counts[i];

            // 5. Error
            double mse = 0;
            for (int i = 0; i < n; i++)
            {
                double diff = series[i] - recon[i];
                mse += diff * diff;
            }
            mse /= n;

            sw.Stop();
            return (mse, sw.Elapsed.TotalSeconds);
        }
    }
}