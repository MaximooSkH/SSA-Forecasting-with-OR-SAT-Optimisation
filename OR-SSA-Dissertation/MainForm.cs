using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;
using System.Windows.Forms.DataVisualization.Charting;

namespace OR_SSA_Dissertation
{
    public class MainForm : Form
    {
        private Panel leftPanel;
        private Panel rightPanel;
        private Chart chartMain;
        private FlowLayoutPanel buttonPanel;
        private Label lblLog;
        private RichTextBox rtbLog;

        private bool useExperimental = false;

        public MainForm()
        {
            Text = "SSA Optimizer";
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(1050, 650);
            BackColor = Color.White;
            DoubleBuffered = true;
            FormBorderStyle = FormBorderStyle.None; // custom rounded frame

            // drag-to-move (since borderless)
            var drag = false; Point dragStart = Point.Empty;
            MouseDown += (s, e) => { drag = true; dragStart = e.Location; };
            MouseMove += (s, e) =>
            {
                if (!drag) return;
                Location = new Point(Location.X + e.X - dragStart.X, Location.Y + e.Y - dragStart.Y);
            };
            MouseUp += (s, e) => drag = false;

            BuildUi();
            Resize += (s, e) => ApplyRoundRegion(20);
            Shown  += (s, e) => ApplyRoundRegion(20);

            RunSolverAndPlot();
        }

        private void BuildUi()
        {
            // Left (chart + buttons)
            leftPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.White,
                Padding = new Padding(16, 20, 8, 16)
            };
            Controls.Add(leftPanel);

            // Right (logger)
            rightPanel = new Panel
            {
                Dock = DockStyle.Right,
                Width = 320,
                Padding = new Padding(16, 20, 16, 16),
                BackColor = Color.FromArgb(24, 27, 31)
            };
            Controls.Add(rightPanel);

            // Chart
            chartMain = new Chart { Dock = DockStyle.Fill, BackColor = Color.White };
            var area = new ChartArea("main");
            area.AxisX.MajorGrid.LineColor = Color.Gainsboro;
            area.AxisY.MajorGrid.LineColor = Color.Gainsboro;
            chartMain.ChartAreas.Add(area);

            // Buttons under chart
            buttonPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 40,
                FlowDirection = FlowDirection.LeftToRight,
                Padding = new Padding(0),
                BackColor = Color.White
            };

            var btnGenerate = CreateButton("Generate Data", (s, e) => RunSolverAndPlot());
            var btnRun      = CreateButton("Run SSA (OR-Tools)", (s, e) => RunSolverAndPlot());
            var btnImport   = CreateButton("Import CSV", (s, e) => ImportCsvAndRun());

            buttonPanel.Controls.Add(btnImport);
            buttonPanel.Controls.Add(btnGenerate);
            buttonPanel.Controls.Add(btnRun);

            leftPanel.Controls.Add(chartMain);
            leftPanel.Controls.Add(buttonPanel);

            // Log label
            lblLog = new Label
            {
                Text = "Runtime Output",
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                Dock = DockStyle.Top,
                Height = 24
            };
            rightPanel.Controls.Add(lblLog);

            // Log box
            rtbLog = new RichTextBox
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                BorderStyle = BorderStyle.None,
                BackColor = Color.FromArgb(30, 34, 38),
                ForeColor = Color.Gainsboro,
                Font = new Font("Consolas", 9f)
            };
            rightPanel.Controls.Add(rtbLog);

            // Close button
            var btnClose = new Button
            {
                Text = "✕",
                ForeColor = Color.White,
                BackColor = Color.FromArgb(60, 64, 72),
                FlatStyle = FlatStyle.Flat,
                Width = 36,
                Height = 30,
                Location = new Point(Width - 56, 12),
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            btnClose.FlatAppearance.BorderSize = 0;
            btnClose.Click += (s, e) => Close();
            Controls.Add(btnClose);
        }

        private Button CreateButton(string text, EventHandler onClick)
        {
            var btn = new Button
            {
                Text = text,
                AutoSize = true,
                Height = 30,
                Padding = new Padding(8, 2, 8, 2),
                BackColor = Color.FromArgb(235, 239, 245),
                FlatStyle = FlatStyle.Flat
            };
            btn.FlatAppearance.BorderSize = 0;
            btn.Click += onClick;
            return btn;
        }

        private void ApplyRoundRegion(int radius)
        {
            if (ClientSize.Width <= 0 || ClientSize.Height <= 0) return;

            using (var path = new GraphicsPath())
            {
                int w = ClientSize.Width, h = ClientSize.Height, r = radius * 2;
                path.StartFigure();
                path.AddArc(new Rectangle(0, 0, r, r), 180, 90);
                path.AddArc(new Rectangle(w - r, 0, r, r), 270, 90);
                path.AddArc(new Rectangle(w - r, h - r, r, r),   0, 90);
                path.AddArc(new Rectangle(0, h - r, r, r),      90, 90);
                path.CloseFigure();
                Region?.Dispose();
                Region = new Region(path);
            }
            Invalidate();
        }

        // === OR-SSA helper ===
        private double[] OrSsaReconstruct(
            SsaResult ssa,
            out ComponentSelector.SelectionResult selOut,
            int rMin = 2, int rMax = 6, double lambda = 0.10,
            bool lockAdjacentPairs = true, int timeLimitSec = 5)
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
            for (int i = 0; i < ssa.DRank; i++)
                if (sel.Keep[i] == 1)
                {
                    var comp = elems[i];
                    for (int t = 0; t < ssa.N; t++) recon[t] += comp[t];
                }

            selOut = sel;
            return recon;
        }

        private void RunSolverAndPlot()
        {
            AppendLog(useExperimental ? "Generating experimental dataset..." : "Generating sine dataset...");

            int N = 200;
            double[] series = new double[N];
            var rand = new Random();

            if (!useExperimental)
            {
                for (int i = 0; i < N; i++)
                {
                    series[i] = 0.05 * i
                              + 3.0 * Math.Sin(2 * Math.PI * i / 20.0)
                              + rand.NextDouble() * 0.5;
                }
            }
            else
            {
                for (int i = 0; i < N; i++)
                {
                    double t = i;
                    double trend = (t < 100) ? 0.02 * t : 0.02 * 100 + 0.05 * (t - 100);
                    double wave = 2.0 * Math.Sin(2 * Math.PI * t / 40.0)
                                + 0.8 * Math.Cos(2 * Math.PI * t / 10.0);
                    double noise = (t < 100 ? 0.3 : 1.0) * (rand.NextDouble() - 0.5);
                    double outlier = (rand.NextDouble() < 0.03) ? rand.NextDouble() * 10.0 - 5.0 : 0.0;
                    series[i] = trend + wave + noise + outlier;
                }
            }

            useExperimental = !useExperimental;

            int L = N / 2;
            var ssa = SsaDecomposition.Decompose(series, L);

            double[] recon;
            ComponentSelector.SelectionResult sel;
            try
            {
                recon = OrSsaReconstruct(
                    ssa,
                    out sel,
                    rMin: 2, rMax: 6, lambda: 0.10, lockAdjacentPairs: true, timeLimitSec: 5);

                AppendLog($"CP-SAT: {sel.Status} | Obj={sel.Objective:F6} | Conflicts={sel.NumConflicts} | Branches={sel.NumBranches} | {sel.WallTimeSec:F3}s");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.ToString(), "SSA Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            chartMain.Series.Clear();

            var sOriginal = new Series("Original")
            {
                ChartType = SeriesChartType.Line,
                BorderWidth = 2,
                Color = Color.SteelBlue
            };
            var sRecon = new Series("OR-SSA Reconstruction")
            {
                ChartType = SeriesChartType.Line,
                BorderWidth = 2,
                Color = Color.IndianRed
            };

            for (int t = 0; t < ssa.N; t++)
            {
                sOriginal.Points.AddXY(t, series[t]);
                sRecon.Points.AddXY(t, recon[t]);
            }

            chartMain.Series.Add(sOriginal);
            chartMain.Series.Add(sRecon);

            AppendLog($"Finished. N={ssa.N}, L={ssa.L}, Rank={ssa.DRank}");
        }

        private void ImportCsvAndRun()
        {
            using (var ofd = new OpenFileDialog
            {
                Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*"
            })
            {
                if (ofd.ShowDialog() != DialogResult.OK) return;

                try
                {
                    var lines = System.IO.File.ReadAllLines(ofd.FileName);
                    var values = new System.Collections.Generic.List<double>();

                    foreach (var line in lines.Skip(1)) // skip header if present
                    {
                        var parts = line.Split(',');
                        if (parts.Length >= 2 && double.TryParse(parts[1], out double val))
                            values.Add(val);
                    }

                    if (values.Count > 0)
                    {
                        RunSsaWithSeries(values.ToArray());
                    }
                    else
                    {
                        AppendLog("CSV contained no usable data.");
                    }
                }
                catch (Exception ex)
                {
                    AppendLog($"Error reading CSV: {ex.Message}");
                }
            }
        }

        private void RunSsaWithSeries(double[] series)
        {
            int N = series.Length;
            int L = Math.Max(2, N / 2);
            var ssa = SsaDecomposition.Decompose(series, L);

            double[] recon;
            ComponentSelector.SelectionResult sel;
            try
            {
                recon = OrSsaReconstruct(
                    ssa,
                    out sel,
                    rMin: 2, rMax: 6, lambda: 0.10, lockAdjacentPairs: true, timeLimitSec: 5);

                AppendLog($"CP-SAT: {sel.Status} | Obj={sel.Objective:F6} | Conflicts={sel.NumConflicts} | Branches={sel.NumBranches} | {sel.WallTimeSec:F3}s");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.ToString(), "SSA Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            chartMain.Series.Clear();

            var sOriginal = new Series("Original")
            {
                ChartType = SeriesChartType.Line,
                BorderWidth = 2,
                Color = Color.SteelBlue
            };
            var sRecon = new Series("OR-SSA Reconstruction")
            {
                ChartType = SeriesChartType.Line,
                BorderWidth = 2,
                Color = Color.IndianRed
            };

            for (int t = 0; t < ssa.N; t++)
            {
                sOriginal.Points.AddXY(t, series[t]);
                sRecon.Points.AddXY(t, recon[t]);
            }

            chartMain.Series.Add(sOriginal);
            chartMain.Series.Add(sRecon);

            AppendLog($"Imported CSV and ran OR-SSA. N={ssa.N}, L={ssa.L}, Rank={ssa.DRank}");
        }

        public void AppendLog(string line)
        {
            if (rtbLog.IsDisposed) return;
            rtbLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {line}\r\n");
            rtbLog.SelectionStart = rtbLog.TextLength;
            rtbLog.ScrollToCaret();
        }
    }
}
