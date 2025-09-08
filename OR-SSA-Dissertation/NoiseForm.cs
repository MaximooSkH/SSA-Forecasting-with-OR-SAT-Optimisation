using System;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using System.Windows.Forms.DataVisualization.Charting;

namespace OR_SSA_Dissertation
{
    public sealed class NoiseForm : Form
    {
        // UI
        private NumericUpDown numN, numScale, numNoise, numRmin, numRmax, numLambda, numTime;
        private CheckBox chkLockPairs;
        private Button btnGenerate, btnRun, btnClose;
        private Chart chart;
        private RichTextBox log;

        // State
        private double[] clean;   // clean (pre-noise)
        private double[] noisy;   // clean + noise
        private int seed = 12345;

        public NoiseForm()
        {
            Text = "SSA Playground — Noise vs Reconstruction";
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(1100, 700);
            BuildUi();
            GenerateDataAndRun();
        }

        private void BuildUi()
        {
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 140));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            Controls.Add(root);

            var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 12 };
            for (int i = 0; i < 12; i++) panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / 12f));
            root.Controls.Add(panel, 0, 0);

            int col = 0;
            numN       = AddSpin(panel, col++, "N", 200, 50, 5000, 50);
            numScale   = AddSpin(panel, col++, "Scale", 1.0m, 0.1m, 10m, 0.1m);
            numNoise   = AddSpin(panel, col++, "Noise σ", 0.5m, 0.0m, 5.0m, 0.05m);
            numRmin    = AddSpin(panel, col++, "r_min", 2, 0, 50, 1);
            numRmax    = AddSpin(panel, col++, "r_max", 6, 1, 60, 1);
            numLambda  = AddSpin(panel, col++, "λ", 0.10m, 0.0m, 5.0m, 0.01m);
            numTime    = AddSpin(panel, col++, "time(s)", 2, 1, 60, 1);

            chkLockPairs = new CheckBox { Text = "Lock adjacent pairs", Checked = true, Dock = DockStyle.Fill };
            panel.Controls.Add(chkLockPairs, col++, 0);

            btnGenerate = new Button { Text = "Re-generate", AutoSize = true };
            btnGenerate.Click += (s, e) => { seed = new Random().Next(); GenerateDataAndRun(); };
            panel.Controls.Add(btnGenerate, col++, 0);

            btnRun = new Button { Text = "Run", AutoSize = true };
            btnRun.Click += (s, e) => RunOnce();
            panel.Controls.Add(btnRun, col++, 0);

            btnClose = new Button { Text = "Close", AutoSize = true };
            btnClose.Click += (s, e) => Close();
            panel.Controls.Add(btnClose, col++, 0);

            var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterDistance = 460 };
            root.Controls.Add(split, 0, 1);

            chart = new Chart { Dock = DockStyle.Fill, BackColor = Color.White };
            var area = new ChartArea("main");
            area.AxisX.Title = "t";
            area.AxisY.Title = "value";
            area.AxisX.MajorGrid.LineColor = Color.Gainsboro;
            area.AxisY.MajorGrid.LineColor = Color.Gainsboro;
            chart.ChartAreas.Add(area);
            split.Panel1.Controls.Add(chart);

            log = new RichTextBox
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                BorderStyle = BorderStyle.None,
                BackColor = Color.FromArgb(30, 34, 38),
                ForeColor = Color.Gainsboro,
                Font = new Font("Consolas", 9f)
            };
            split.Panel2.Controls.Add(log);
        }

        private NumericUpDown AddSpin(TableLayoutPanel parent, int col, string label, decimal value, decimal min, decimal max, decimal inc)
        {
            var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
            panel.RowStyles.Add(new RowStyle(SizeType.Percent, 45));
            panel.RowStyles.Add(new RowStyle(SizeType.Percent, 55));
            var lbl = new Label { Text = label, Dock = DockStyle.Fill, TextAlign = ContentAlignment.BottomLeft };
            var num = new NumericUpDown { DecimalPlaces = (inc < 1m ? 2 : 0), Increment = inc, Minimum = min, Maximum = max, Value = value, Dock = DockStyle.Fill };
            panel.Controls.Add(lbl, 0, 0);
            panel.Controls.Add(num, 0, 1);
            parent.Controls.Add(panel, col, 0);
            return num;
        }

        private void Log(string s)
        {
            log.AppendText($"[{DateTime.Now:HH:mm:ss}] {s}\r\n");
            log.SelectionStart = log.TextLength;
            log.ScrollToCaret();
        }

        // ///////////// Data + runs /////////////

        private void GenerateDataAndRun()
        {
            // build clean and noisy
            int N = (int)numN.Value;
            double scale = (double)numScale.Value;
            double sigma = (double)numNoise.Value;

            clean = MakeCleanSeries(N, scale);
            noisy = AddNoise(clean, sigma, seed);

            RunOnce();
        }

        private void RunOnce()
        {
            int N = (int)numN.Value;
            int L = Math.Min(Math.Max(2, N / 2), N - 1);

            // Decompose noisy series
            var sw = Stopwatch.StartNew();
            var ssa = SsaDecomposition.Decompose(noisy, L);
            sw.Stop();
            double decompSec = sw.Elapsed.TotalSeconds;

            if (ssa == null || ssa.DRank <= 0)
            {
                Log($"Rank={ssa?.DRank ?? -1}, nothing to reconstruct.");
                PlotAll(null, null); // still show clean/noisy
                return;
            }

            // OR-SSA (CP-SAT)
            sw.Restart();
            var orRecon = OrSsaReconstruct(ssa,
                                           out var sel,
                                           rMin: (int)numRmin.Value,
                                           rMax: (int)numRmax.Value,
                                           lambda: (double)numLambda.Value,
                                           lockAdjacentPairs: chkLockPairs.Checked,
                                           timeLimitSec: (int)numTime.Value);
            sw.Stop();
            double orTime = sw.Elapsed.TotalSeconds;

            // Baseline SSA with same #components as CP-SAT selected (bounded)
            int picked = (sel?.Keep != null) ? sel.Keep.Sum() : (int)numRmax.Value;
            int rBase = Math.Max(1, Math.Min(picked, ssa.DRank));
            var baseRes = BaselineTopR(ssa, rBase);

            // Log a compact summary
            double mseOr   = Mse(noisy, orRecon);
            double mseBase = Mse(noisy, baseRes.recon);
            Log($"N={N}, L={L}, Rank={ssa.DRank} | Decomp={decompSec:F3}s | Base(r={rBase})={baseRes.sec:F3}s (MSE={mseBase:F4}) | OR={orTime:F3}s (MSE={mseOr:F4})");

            PlotAll(baseRes.recon, orRecon);
        }

        // ///////////// Recon helpers /////////////

        private (double[] recon, double sec) BaselineTopR(SsaResult ssa, int r)
        {
            var sw = Stopwatch.StartNew();
            var elems = SsaReconstruction.ElementaryReconstructions(ssa);
            var top = Enumerable.Range(0, ssa.DRank)
                                .OrderByDescending(i => ssa.SingularValues[i])
                                .Take(Math.Max(0, Math.Min(r, ssa.DRank)))
                                .ToArray();
            var recon = new double[ssa.N];
            foreach (var i in top)
                for (int t = 0; t < ssa.N; t++) recon[t] += elems[i][t];
            sw.Stop();
            return (recon, sw.Elapsed.TotalSeconds);
        }

        private double[] OrSsaReconstruct(
            SsaResult ssa,
            out ComponentSelector.SelectionResult selOut,
            int rMin, int rMax, double lambda, bool lockAdjacentPairs, int timeLimitSec)
        {
            var q     = SsaMetrics.ComputeContributions(ssa.SingularValues);
            var elems = SsaReconstruction.ElementaryReconstructions(ssa);
            var w     = SsaMetrics.Compute1DWeights(ssa.N, ssa.L, ssa.K);
            var R     = SsaMetrics.WCorrelation(elems, w);

            var absR = new double[ssa.DRank, ssa.DRank];
            for (int i = 0; i < ssa.DRank; i++)
                for (int j = 0; j < ssa.DRank; j++)
                    absR[i, j] = Math.Abs(R[i, j]);

            Tuple<int,int>[] locks = Array.Empty<Tuple<int,int>>();
            if (lockAdjacentPairs && ssa.DRank >= 2)
            {
                var list = new System.Collections.Generic.List<Tuple<int,int>>();
                for (int i = 0; i + 1 < ssa.DRank; i += 2)
                    list.Add(Tuple.Create(i, i + 1));
                locks = list.ToArray();
            }

            var sel = ComponentSelector.SelectComponents(q, absR, locks, rMin, rMax, lambda, timeLimitSec);

            var recon = new double[ssa.N];
            int len = Math.Min(ssa.DRank, sel.Keep?.Length ?? 0);
            for (int i = 0; i < len; i++)
            {
                if (sel.Keep[i] == 1)
                {
                    var comp = elems[i];
                    for (int t = 0; t < ssa.N; t++) recon[t] += comp[t];
                }
            }

            selOut = sel;
            return recon;
        }

        private static double[] MakeCleanSeries(int N, double scale)
        {
            // “cooler sine wave”: slow trend + two waves + optional modulation
            double[] y = new double[N];
            for (int i = 0; i < N; i++)
            {
                double t = i;
                double trend = 0.003 * t;
                double wave1 = Math.Sin(2 * Math.PI * t / 40.0);
                double wave2 = 0.5 * Math.Cos(2 * Math.PI * t / 11.0 + 0.4);
                double mod   = 0.2 * Math.Sin(2 * Math.PI * t / 180.0); // slow amplitude modulation
                y[i] = scale * ((1.0 + mod) * (2.0 * wave1 + wave2) + trend);
            }
            return y;
        }

        private static double[] AddNoise(double[] clean, double sigma, int seed)
        {
            var rng = new Random(seed);
            var y = new double[clean.Length];
            for (int i = 0; i < clean.Length; i++)
            {
                double n = sigma * (rng.NextDouble() - 0.5) * 2.0; // ~U(-sigma, sigma)
                y[i] = clean[i] + n;
            }
            return y;
        }

        private static double Mse(double[] a, double[] b)
        {
            double s = 0; for (int i = 0; i < a.Length; i++) { double d = a[i] - b[i]; s += d * d; }
            return s / a.Length;
        }

        // ///////////// Plot /////////////

        private void PlotAll(double[] reconBase, double[] reconOr)
        {
            chart.Series.Clear();

            var sClean = new Series("Clean (pre-noise)")
            {
                ChartType = SeriesChartType.Line,
                BorderWidth = 2,
                Color = Color.ForestGreen,
                BorderDashStyle = ChartDashStyle.Dash
            };
            var sNoisy = new Series("Noisy series")
            {
                ChartType = SeriesChartType.Line,
                BorderWidth = 2,
                Color = Color.Gray
            };
            var sBase = new Series("SSA baseline")
            {
                ChartType = SeriesChartType.Line,
                BorderWidth = 2,
                Color = Color.SteelBlue
            };
            var sOr = new Series("OR-SSA (CP-SAT)")
            {
                ChartType = SeriesChartType.Line,
                BorderWidth = 2,
                Color = Color.IndianRed
            };

            if (clean != null)
                for (int t = 0; t < clean.Length; t++) sClean.Points.AddXY(t, clean[t]);
            if (noisy != null)
                for (int t = 0; t < noisy.Length; t++) sNoisy.Points.AddXY(t, noisy[t]);
            if (reconBase != null)
                for (int t = 0; t < reconBase.Length; t++) sBase.Points.AddXY(t, reconBase[t]);
            if (reconOr != null)
                for (int t = 0; t < reconOr.Length; t++) sOr.Points.AddXY(t, reconOr[t]);

            chart.Series.Add(sClean);
            chart.Series.Add(sNoisy);
            if (reconBase != null) chart.Series.Add(sBase);
            if (reconOr != null)   chart.Series.Add(sOr);

            chart.ChartAreas[0].RecalculateAxesScale();
            chart.Legends.Clear();
            chart.Legends.Add(new Legend { Docking = Docking.Top, Alignment = StringAlignment.Near });
        }
    }
}
