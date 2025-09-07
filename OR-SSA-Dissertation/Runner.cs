using System;
using System.Collections.Generic;
using System.Linq;

namespace OR_SSA_Dissertation
{
    public class RunConfig
    {
        public string CsvPath;
        public int ColumnIndex;
        public int L, RMin, RMax;
        public double Lambda;
        public bool LockAdjacent;
    }

    public class RunResult
    {
        public int N, L, K, Rank;
        public double[] Original;
        public double[] Reconstruction;
        public int[] SelectedIndices;
        public double[] Contributions;
        public double[,] WCorr;

        // OR-Tools stats
        public string SolverStatus;
        public double Objective;
        public long NumBranches;
        public long NumConflicts;
        public double WallTimeSec;
    }

    public static class Runner
    {
        public static RunResult RunAll(RunConfig cfg)
        {
            var series = CsvIo.LoadColumn(cfg.CsvPath, cfg.ColumnIndex);
            int N = series.Length;
            if (cfg.L < 2 || cfg.L > N - 1) throw new ArgumentException($"Window L must be in [2, {N - 1}]");

            var ssa = SsaDecomposition.Decompose(series, cfg.L); // SVD on Hankel
            var q = SsaMetrics.ComputeContributions(ssa.SingularValues);
            var elem = SsaReconstruction.ElementaryReconstructions(ssa);
            var w = SsaMetrics.Compute1DWeights(ssa.N, ssa.L, ssa.K);
            var R = SsaMetrics.WCorrelation(elem, w);

            // Locks (oscillatory pairs)
            var locks = new List<Tuple<int, int>>();
            if (cfg.LockAdjacent)
                for (int i = 0; i + 1 < ssa.DRank; i += 2) locks.Add(Tuple.Create(i, i + 1));

            var wAbs = new double[ssa.DRank, ssa.DRank];
            for (int i = 0; i < ssa.DRank; i++)
                for (int j = 0; j < ssa.DRank; j++) wAbs[i, j] = Math.Abs(R[i, j]);

            var sel = ComponentSelector.SelectComponents(
                q, wAbs, locks.ToArray(),
                cfg.RMin, cfg.RMax, cfg.Lambda, timeLimitSec: 10);

            // reconstruction
            var recon = new double[ssa.N];
            for (int i = 0; i < ssa.DRank; i++)
                if (sel.Keep[i] == 1)
                    for (int k = 0; k < ssa.N; k++) recon[k] += elem[i][k];

            return new RunResult
            {
                N = ssa.N, L = ssa.L, K = ssa.K, Rank = ssa.DRank,
                Original = series,
                Reconstruction = recon,
                SelectedIndices = Enumerable.Range(0, ssa.DRank).Where(i => sel.Keep[i] == 1).ToArray(),
                Contributions = q,
                WCorr = R,
                SolverStatus = sel.Status,
                Objective = sel.Objective,
                NumBranches = sel.NumBranches,
                NumConflicts = sel.NumConflicts,
                WallTimeSec = sel.WallTimeSec
            };
        }
    }
}
