using System.Collections.Generic;
using Google.OrTools.Sat;


namespace OR_SSA_Dissertation
{
    public static class ComponentSelector
    {
        private const long SCALE = 1_000_000L;

        public class SelectionResult
        {
            public int[] Keep;
            public string Status;
            public double Objective;
            public long NumBranches;
            public long NumConflicts;
            public double WallTimeSec;
        }

        public static SelectionResult SelectComponents(
            double[] q,
            double[,] wCorrAbs,
            System.Tuple<int, int>[] lockedPairs,
            int rMin, int rMax, double lambda,
            int timeLimitSec)
        {
            int n = q.Length;
            var model = new CpModel();

            var z = new BoolVar[n];
            for (int i = 0; i < n; i++) z[i] = model.NewBoolVar($"z_{i}");

            var y = new BoolVar[n, n];
            for (int i = 0; i < n; i++)
                for (int j = i + 1; j < n; j++)
                    y[i, j] = model.NewBoolVar($"y_{i}_{j}");

            // linearize y_ij = z_i AND z_j
            for (int i = 0; i < n; i++)
                for (int j = i + 1; j < n; j++)
                {
                    model.Add(y[i, j] <= z[i]);
                    model.Add(y[i, j] <= z[j]);
                    model.Add(LinearExpr.Sum(new IntVar[] { z[i], z[j] }) - y[i, j] <= 1);
                }

            model.AddLinearConstraint(LinearExpr.Sum(z), rMin, rMax);

            if (lockedPairs != null)
                foreach (var pair in lockedPairs)
                    model.Add(z[pair.Item1] == z[pair.Item2]);

            var terms = new List<LinearExpr>();
            for (int i = 0; i < n; i++)
            {
                long c = (long)System.Math.Round(q[i] * SCALE);
                if (c != 0) terms.Add(LinearExpr.Term(z[i], c));
            }
            for (int i = 0; i < n; i++)
                for (int j = i + 1; j < n; j++)
                {
                    long c = (long)System.Math.Round(lambda * System.Math.Abs(wCorrAbs[i, j]) * SCALE);
                    if (c != 0) terms.Add(LinearExpr.Term(y[i, j], -c));
                }

            model.Maximize(LinearExpr.Sum(terms));

            var solver = new CpSolver
            {
                StringParameters = $"max_time_in_seconds:{timeLimitSec}, num_search_workers:8"
            };
            var status = solver.Solve(model);

            var keep = new int[n];
            for (int i = 0; i < n; i++) keep[i] = solver.BooleanValue(z[i]) ? 1 : 0;

            return new SelectionResult
            {
                Keep = keep,
                Status = status.ToString(),
                Objective = solver.ObjectiveValue / SCALE,
                NumBranches = solver.NumBranches(),
                NumConflicts = solver.NumConflicts(),
                WallTimeSec = solver.WallTime()
            };
        }
    }
}
