using System;

namespace OR_SSA_Dissertation
{
    public static class OrSsa
    {
        /// Reconstruct a series using CP-SAT to pick SSA components.
        public static double[] Reconstruct(
            SsaResult ssa,
            out ComponentSelector.SelectionResult selOut,
            int rMin = 2, int rMax = 6, double lambda = 0.10,
            bool lockAdjacentPairs = true, int timeLimitSec = 5)
        {
            if (ssa == null) throw new ArgumentNullException(nameof(ssa));
            if (ssa.DRank <= 0) { selOut = new ComponentSelector.SelectionResult { Keep = Array.Empty<int>() }; return new double[ssa.N]; }

            // Contributions per component
            var q     = SsaMetrics.ComputeContributions(ssa.SingularValues);

            // Elementary reconstructions (each component as a full-length series)
            var elems = SsaReconstruction.ElementaryReconstructions(ssa);

            // Weighted correlation between components (SSA standard)
            var weights = SsaMetrics.Compute1DWeights(ssa.N, ssa.L, ssa.K);
            var R = SsaMetrics.WCorrelation(elems, weights);

            // |R|
            var absR = new double[ssa.DRank, ssa.DRank];
            for (int i = 0; i < ssa.DRank; i++)
                for (int j = 0; j < ssa.DRank; j++)
                    absR[i, j] = Math.Abs(R[i, j]);

            // Optional: lock adjacent oscillatory pairs (0,1), (2,3), ...
            Tuple<int,int>[] locks = Array.Empty<Tuple<int,int>>();
            if (lockAdjacentPairs && ssa.DRank >= 2)
            {
                int pairs = ssa.DRank / 2;
                locks = new Tuple<int, int>[pairs];
                int k = 0;
                for (int i = 0; i + 1 < ssa.DRank; i += 2)
                    locks[k++] = Tuple.Create(i, i + 1);
            }

            // Call CP-SAT
            var sel = ComponentSelector.SelectComponents(q, absR, locks, rMin, rMax, lambda, timeLimitSec);

            // Reconstruct from selected components
            var recon = new double[ssa.N];
            for (int c = 0; c < ssa.DRank; c++)
            {
                if (sel.Keep[c] == 1)
                {
                    var comp = elems[c];
                    for (int t = 0; t < ssa.N; t++) recon[t] += comp[t];
                }
            }

            selOut = sel;
            return recon;
        }
    }
}
