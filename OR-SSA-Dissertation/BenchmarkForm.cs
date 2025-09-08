using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Windows.Forms.DataVisualization.Charting;

namespace OR_SSA_Dissertation
{
    public sealed class BenchmarkForm : Form
    {
        // UI
        private TextBox txtStart, txtEnd, txtStep, txtRmin, txtRmax, txtLambda, txtTime;
        private CheckBox chkLockPairs, chkUseSameR;
        private Button btnRun, btnCancel, btnClear;
        private Chart chart;
        private RichTextBox logBox;

        private CancellationTokenSource _cts;

        public BenchmarkForm()
        {
            Text = "SSA vs OR-SSA Benchmark";
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(1100, 700);
            BuildUi();
        }

        //////////////////////// UI /////////////////////

        private void BuildUi()
        {
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
            };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 130));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            Controls.Add(root);

            var controlsPanel = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(10),
                BackColor = Color.FromArgb(245, 247, 250)
            };
            root.Controls.Add(controlsPanel, 0, 0);

            var grid = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 10,
            };
            for (int i = 0; i < 10; i++) grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 10));
            controlsPanel.Controls.Add(grid);

            // Inputs
            txtStart  = AddLabeledBox(grid, 0, "Start N", "100");
            txtEnd    = AddLabeledBox(grid, 1, "End N",   "500");
            txtStep   = AddLabeledBox(grid, 2, "Step",    "10");
            txtRmin   = AddLabeledBox(grid, 3, "r_min",   "2");
            txtRmax   = AddLabeledBox(grid, 4, "r_max",   "6");
            txtLambda = AddLabeledBox(grid, 5, "lambda",  "0.10");
            txtTime   = AddLabeledBox(grid, 6, "time(s)", "2");

            chkLockPairs = new CheckBox { Text = "Lock adjacent pairs", Checked = true, Dock = DockStyle.Fill };
            grid.Controls.Add(chkLockPairs, 7, 0);

            chkUseSameR = new CheckBox { Text = "Baseline uses CP-SAT r", Checked = true, Dock = DockStyle.Fill };
            grid.Controls.Add(chkUseSameR, 8, 0);

            var buttonsPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight };
            grid.Controls.Add(buttonsPanel, 9, 0);

            btnRun = new Button { Text = "Run", AutoSize = true };
            btnRun.Click += async (s, e) => await RunBenchmarkAsync();
            buttonsPanel.Controls.Add(btnRun);

            btnCancel = new Button { Text = "Cancel", AutoSize = true, Enabled = false };
            btnCancel.Click += (s, e) => _cts?.Cancel();
            buttonsPanel.Controls.Add(btnCancel);

            btnClear = new Button { Text = "Clear Plot", AutoSize = true };
            btnClear.Click += (s, e) => { chart.Series.Clear(); if (chart.ChartAreas.Count > 0) chart.ChartAreas[0].RecalculateAxesScale(); };
            buttonsPanel.Controls.Add(btnClear);

            // Plot + log
            var split = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Horizontal,
                SplitterDistance = 420
            };
            root.Controls.Add(split, 0, 1);

            chart = new Chart { Dock = DockStyle.Fill, BackColor = Color.White };
            if (chart.ChartAreas.Count == 0) chart.ChartAreas.Add(new ChartArea("main"));
            var area = chart.ChartAreas[0];
            area.AxisX.Title = "Dataset size N";
            area.AxisY.Title = "Wall time (seconds)";
            area.AxisX.MajorGrid.LineColor = Color.Gainsboro;
            area.AxisY.MajorGrid.LineColor = Color.Gainsboro;
            split.Panel1.Controls.Add(chart);

            logBox = new RichTextBox
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                BorderStyle = BorderStyle.None,
                BackColor = Color.FromArgb(30, 34, 38),
                ForeColor = Color.Gainsboro,
                Font = new Font("Consolas", 9f)
            };
            split.Panel2.Controls.Add(logBox);
        }

        private TextBox AddLabeledBox(TableLayoutPanel grid, int col, string label, string def)
        {
            var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
            panel.RowStyles.Add(new RowStyle(SizeType.Percent, 45));
            panel.RowStyles.Add(new RowStyle(SizeType.Percent, 55));
            var lbl = new Label { Text = label, Dock = DockStyle.Fill, TextAlign = ContentAlignment.BottomLeft };
            var box = new TextBox { Text = def, Dock = DockStyle.Fill };
            panel.Controls.Add(lbl, 0, 0);
            panel.Controls.Add(box, 0, 1);
            grid.Controls.Add(panel, col, 0);
            return box;
        }

        private void Log(string s)
        {
            if (InvokeRequired) { BeginInvoke(new Action<string>(Log), s); return; }
            logBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {s}\r\n");
            logBox.SelectionStart = logBox.TextLength;
            logBox.ScrollToCaret();
        }

        private void AddPoint(string seriesName, double x, double y)
        {
            if (InvokeRequired) { BeginInvoke(new Action<string,double,double>(AddPoint), seriesName, x, y); return; }
            if (chart.ChartAreas.Count == 0) chart.ChartAreas.Add(new ChartArea("main"));
            if (chart.Series.IndexOf(seriesName) < 0)
            {
                var s = new Series(seriesName)
                {
                    ChartType = SeriesChartType.Line,
                    BorderWidth = 2,
                    MarkerStyle = MarkerStyle.None
                };
                chart.Series.Add(s);
            }
            chart.Series[seriesName].Points.AddXY(x, y);
            chart.ChartAreas[0].RecalculateAxesScale();
        }

        ////////////////// Baselines + OR-SSA helpers //////////////////////////

        // Keep top-r components by singular value, measure time (no plotting here).
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
                var list = new List<Tuple<int,int>>();
                for (int i = 0; i + 1 < ssa.DRank; i += 2)
                    list.Add(Tuple.Create(i, i + 1));
                locks = list.ToArray();
            }

            var sel = ComponentSelector.SelectComponents(q, absR, locks, rMin, rMax, lambda, timeLimitSec);

            var recon = new double[ssa.N];
            int len = Math.Min(ssa.DRank, sel.Keep?.Length ?? 0); // guard against length mismatch
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

        ////////////// Synthetic data ////////////////

        private static double[] MakeSeries(int N, Random rng)
        {
            // mild trend + 2 waves + noise; stable across sizes
            double[] y = new double[N];
            for (int i = 0; i < N; i++)
            {
                double t = i;
                double trend = 0.0008 * t;
                double wave1 = 2.0 * Math.Sin(2 * Math.PI * t / 40.0);
                double wave2 = 0.7 * Math.Cos(2 * Math.PI * t / 11.0);
                double noise = 0.4 * (rng.NextDouble() - 0.5);
                y[i] = trend + wave1 + wave2 + noise;
            }
            return y;
        }

        //Main runner

        private async Task RunBenchmarkAsync()
        {
            // parse inputs
            if (!int.TryParse(txtStart.Text, out int nStart) ||
                !int.TryParse(txtEnd.Text, out int nEnd) ||
                !int.TryParse(txtStep.Text, out int nStep) ||
                !int.TryParse(txtRmin.Text, out int rMin) ||
                !int.TryParse(txtRmax.Text, out int rMax) ||
                !double.TryParse(txtLambda.Text, out double lambda) ||
                !int.TryParse(txtTime.Text, out int timeLimitSec))
            {
                MessageBox.Show(this, "Please enter valid numeric inputs.", "Benchmark", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (nStart < 50 || nEnd <= nStart || nStep <= 0)
            {
                MessageBox.Show(this, "Check N range/step.", "Benchmark", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (rMin < 0 || rMax < rMin)
            {
                MessageBox.Show(this, "Check r_min/r_max.", "Benchmark", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            btnRun.Enabled = false; btnCancel.Enabled = true;
            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            // prepare chart
            chart.Series.Clear();
            AddPoint("Baseline SSA", 0, 0); // create series
            AddPoint("OR-SSA (CP-SAT)", 0, 0);
            chart.Series["Baseline SSA"].Points.Clear();
            chart.Series["OR-SSA (CP-SAT)"].Points.Clear();

            Log($"Starting benchmark: N={nStart}:{nStep}:{nEnd}, r∈[{rMin},{rMax}], λ={lambda}, t≤{timeLimitSec}s, lockPairs={chkLockPairs.Checked}, baselineSameR={chkUseSameR.Checked}");

            try
            {
                await Task.Run(() =>
                {
                    var rng = new Random(12345);

                    for (int N = nStart; N <= nEnd; N += nStep)
                    {
                        token.ThrowIfCancellationRequested();

                        var series = MakeSeries(N, rng);

                        // Clamp L to [2, N-1]
                        int L = Math.Min(Math.Max(2, N / 2), N - 1);

                        var sw = Stopwatch.StartNew();
                        var ssa = SsaDecomposition.Decompose(series, L);
                        sw.Stop();
                        var decompSec = sw.Elapsed.TotalSeconds;

                        if (ssa == null || ssa.DRank <= 0)
                        {
                            Log($"N={N}: rank={ssa?.DRank ?? -1}, skipping.");
                            continue;
                        }

                        // OR-SSA selection
                        sw.Restart();
                        var reconOr = OrSsaReconstruct(ssa, out var sel, rMin, rMax, lambda, chkLockPairs.Checked, timeLimitSec);
                        sw.Stop();
                        double orTime = sw.Elapsed.TotalSeconds;

                        // Baseline r: same as CP-SAT (if chosen) else rMax; clamp to [1, rank]
                        int picked = (sel?.Keep != null) ? sel.Keep.Sum() : rMax;
                        int rBaseline = chkUseSameR.Checked ? picked : rMax;
                        rBaseline = Math.Max(1, Math.Min(rBaseline, ssa.DRank));

                        var baseRes = BaselineTopR(ssa, rBaseline);
                        double baseTime = baseRes.sec;

                        // Include SVD/embedding cost in both curves 
                        AddPoint("Baseline SSA", N, baseTime + decompSec);
                        AddPoint("OR-SSA (CP-SAT)", N, orTime + decompSec);

                        Log($"N={N}: Decomp={decompSec:F3}s | Base(r={rBaseline})={baseTime:F3}s | CP-SAT={sel.WallTimeSec:F3}s | Obj={sel.Objective:F5} | Rank={ssa.DRank}");
                    }
                }, token);

                Log("Benchmark complete.");
            }
            catch (OperationCanceledException)
            {
                Log("Benchmark cancelled.");
            }
            catch (Exception ex)
            {
                Log("Error: " + ex.Message);
            }
            finally
            {
                btnRun.Enabled = true; btnCancel.Enabled = false;
                _cts?.Dispose(); _cts = null;
            }
        }
    }
}
