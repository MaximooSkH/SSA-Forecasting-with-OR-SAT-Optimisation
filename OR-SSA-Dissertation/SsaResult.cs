using System;
using MathNet.Numerics.LinearAlgebra;
using MathNet.Numerics.LinearAlgebra.Double;

namespace OR_SSA_Dissertation
{
    public class SsaResult
    {
        public int N, L, K, DRank;
        public Matrix<double> Trajectory;       // L x K
        public Vector<double>[] U;              // len L
        public Vector<double>[] V;              // len K
        public double[] SingularValues;         // sigma_i
    }

    public static class SsaDecomposition
    {
        public static SsaResult Decompose(double[] series, int L)
        {
            int N = series.Length;
            int K = N - L + 1;

            var X = DenseMatrix.Create(L, K, 0.0);
            for (int i = 0; i < L; i++)
                for (int j = 0; j < K; j++)
                    X[i, j] = series[i + j];

            var svd = X.Svd(true);
            var S = svd.S.ToArray();
            var U = svd.U;
            var VT = svd.VT;

            double maxS = S.Length > 0 ? Math.Max(1e-30, S[0]) : 0.0;
            double tol = Math.Max(L, K) * 1e-12 * maxS;

            int r = 0;
            for (int i = 0; i < S.Length; i++) if (S[i] > tol) r++;

            var Ucols = new Vector<double>[r];
            var Vcols = new Vector<double>[r];
            var sig = new double[r];
            for (int idx = 0; idx < r; idx++)
            {
                Ucols[idx] = U.Column(idx);
                Vcols[idx] = VT.Row(idx).ToColumnMatrix().Column(0);
                sig[idx] = S[idx];
            }

            return new SsaResult
            {
                N = N, L = L, K = K, DRank = r,
                Trajectory = X,
                U = Ucols,
                V = Vcols,
                SingularValues = sig
            };
        }
    }

    public static class SsaReconstruction
    {
        public static double[][] ElementaryReconstructions(SsaResult ssa)
        {
            int r = ssa.DRank;
            var comps = new double[r][];
            for (int i = 0; i < r; i++)
            {
                var Xi = Outer(ssa.U[i], ssa.V[i], ssa.SingularValues[i]);
                comps[i] = HankelDiagonalAverage(Xi, ssa.N, ssa.L, ssa.K);
            }
            return comps;
        }

        private static Matrix<double> Outer(Vector<double> u, Vector<double> v, double sigma)
        {
            int L = u.Count, K = v.Count;
            var M = DenseMatrix.Create(L, K, 0.0);
            for (int i = 0; i < L; i++)
            {
                double ui = u[i] * sigma;
                for (int j = 0; j < K; j++) M[i, j] = ui * v[j];
            }
            return M;
        }

        private static double[] HankelDiagonalAverage(Matrix<double> M, int N, int L, int K)
        {
            var y = new double[N];
            var c = new int[N];
            for (int i = 0; i < L; i++)
                for (int j = 0; j < K; j++)
                {
                    int k = i + j;
                    y[k] += M[i, j];
                    c[k] += 1;
                }
            for (int k = 0; k < N; k++) if (c[k] > 0) y[k] /= c[k];
            return y;
        }
    }

    public static class SsaMetrics
    {
        public static double[] ComputeContributions(double[] sigmas)
        {
            double sum2 = 0; foreach (var s in sigmas) sum2 += s * s;
            if (sum2 <= 0) return new double[sigmas.Length];
            var q = new double[sigmas.Length];
            for (int i = 0; i < sigmas.Length; i++) q[i] = sigmas[i] * sigmas[i] / sum2;
            return q;
        }

        public static double[] Compute1DWeights(int N, int L, int K)
        {
            var w = new double[N];
            for (int k = 0; k <= L - 2 && k < N; k++) w[k] = k + 1;
            for (int k = L - 1; k <= K - 1 && k < N; k++) w[k] = L;
            for (int k = K; k < N; k++) w[k] = N - k;
            return w;
        }

        public static double[,] WCorrelation(double[][] elems, double[] weights)
        {
            int r = elems.Length, N = elems[0].Length;
            var R = new double[r, r];
            var norms = new double[r];

            for (int i = 0; i < r; i++)
            {
                double s = 0.0;
                var xi = elems[i];
                for (int k = 0; k < N; k++) s += weights[k] * xi[k] * xi[k];
                norms[i] = Math.Sqrt(Math.Max(1e-30, s));
            }

            for (int i = 0; i < r; i++)
            {
                R[i, i] = 1.0;
                for (int j = i + 1; j < r; j++)
                {
                    double num = 0.0;
                    var xi = elems[i]; var xj = elems[j];
                    for (int k = 0; k < N; k++) num += weights[k] * xi[k] * xj[k];
                    double rho = (norms[i] * norms[j]) > 0 ? num / (norms[i] * norms[j]) : 0.0;
                    R[i, j] = rho; R[j, i] = rho;
                }
            }
            return R;
        }
    }
}
