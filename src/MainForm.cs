using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace DefenderRestore
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            string[] args = Environment.GetCommandLineArgs();

            string reportPath = GetValue(args, "--report");
            if (reportPath != null)
            {
                if (reportPath.Length == 0)
                    reportPath = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                        "DefenderRestore_Report.txt");

                Diag d = new Diag();
                try
                {
                    d.RunAll();
                    File.WriteAllText(reportPath, d.Report(), new UTF8Encoding(true));
                }
                catch (Exception ex)
                {
                    try { File.WriteAllText(reportPath, "診断に失敗しました: " + ex, new UTF8Encoding(true)); }
                    catch { }
                }
                return;
            }

            bool autoRepair = false;
            bool autoYes = false;
            foreach (string a in args)
            {
                if (a.Equals("--repair", StringComparison.OrdinalIgnoreCase)) autoRepair = true;
                if (a.Equals("--yes", StringComparison.OrdinalIgnoreCase)) autoYes = true;
            }

            Application.Run(new MainForm(autoRepair, autoYes));
        }

        private static string GetValue(string[] args, string name)
        {
            for (int i = 0; i < args.Length; i++)
            {
                if (!args[i].Equals(name, StringComparison.OrdinalIgnoreCase)) continue;
                if (i + 1 < args.Length && !args[i + 1].StartsWith("--")) return args[i + 1];
                return "";
            }
            return null;
        }
    }

    internal sealed class MainForm : Form
    {
        private static readonly Color Accent = Color.FromArgb(15, 108, 189);
        private static readonly Color Ink = Color.FromArgb(23, 35, 52);
        private static readonly Color Muted = Color.FromArgb(90, 100, 115);
        private static readonly Color OkBack = Color.FromArgb(230, 244, 234);
        private static readonly Color OkFore = Color.FromArgb(15, 81, 50);
        private static readonly Color WarnBack = Color.FromArgb(255, 244, 206);
        private static readonly Color WarnFore = Color.FromArgb(102, 60, 0);
        private static readonly Color FailBack = Color.FromArgb(253, 231, 233);
        private static readonly Color FailFore = Color.FromArgb(164, 38, 44);

        private readonly bool _autoYes;
        private readonly List<string> _lastRepairLog = new List<string>();
        private bool _pendingRepair;
        private bool _busy;

        private Diag _diag = new Diag();
        private Font _boldFont;

        private DataGridView _grid;
        private Label _lblOs;
        private Panel _banner;
        private Label _lblBanner;
        private Label _lblBannerSub;
        private Label _lblFooter;
        private TextBox _txtLog;
        private Button _btnScan;
        private Button _btnRepair;
        private Button _btnReport;
        private Button _btnClear;
        private Button _btnFolder;
        private CheckBox _chkRestorePoint;
        private CheckBox _chkRestoreDefaults;
        private CheckBox _chkUpdateSigs;
        private ProgressBar _progress;

        public MainForm(bool autoRepair, bool autoYes)
        {
            _autoYes = autoYes;
            _pendingRepair = autoRepair;
            BuildUi();
        }

        private void BuildUi()
        {
            Text = "Defender 復元ツール";
            Size = new Size(1060, 840);
            MinimumSize = new Size(940, 720);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(243, 246, 250);
            AutoScaleMode = AutoScaleMode.Dpi;
            KeyPreview = true;

            try { Font = new Font("Yu Gothic UI", 9.5f); }
            catch { Font = SystemFonts.MessageBoxFont; }

            _boldFont = new Font(Font, FontStyle.Bold);
            try { Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
            catch { }

            TableLayoutPanel root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 7,
                BackColor = BackColor,
                Margin = new Padding(0),
                Padding = new Padding(0)
            };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 92));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 68));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 160));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));

            root.Controls.Add(BuildHeader(), 0, 0);
            root.Controls.Add(BuildBanner(), 0, 1);
            root.Controls.Add(BuildGrid(), 0, 2);
            root.Controls.Add(BuildOptions(), 0, 3);
            root.Controls.Add(BuildButtons(), 0, 4);
            root.Controls.Add(BuildLog(), 0, 5);
            root.Controls.Add(BuildFooter(), 0, 6);

            Controls.Add(root);
        }

        private Control BuildHeader()
        {
            TableLayoutPanel header = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.White,
                Padding = new Padding(22, 12, 22, 8),
                ColumnCount = 2,
                RowCount = 2,
                Margin = new Padding(0)
            };
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            header.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            header.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            Label title = new Label
            {
                Text = "Windows セキュリティ 復元ツール",
                Font = new Font("Yu Gothic UI", 17f, FontStyle.Bold),
                ForeColor = Ink,
                AutoSize = true,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft
            };
            Label sub = new Label
            {
                Text = "dControl などの無効化ツールが残した設定を検査し、Microsoft Defender を正常な状態に戻します。",
                ForeColor = Muted,
                AutoSize = true,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft
            };
            _lblOs = new Label
            {
                Text = "",
                ForeColor = Muted,
                AutoSize = true,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleRight,
                Padding = new Padding(12, 6, 0, 0)
            };

            header.Controls.Add(title, 0, 0);
            header.Controls.Add(sub, 0, 1);
            header.Controls.Add(_lblOs, 1, 0);
            header.SetRowSpan(_lblOs, 2);
            return header;
        }

        private Control BuildBanner()
        {
            _banner = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = OkBack,
                Margin = new Padding(18, 0, 18, 10),
                Padding = new Padding(0)
            };
            _banner.Paint += delegate(object s, PaintEventArgs e)
            {
                using (Pen p = new Pen(Color.FromArgb(214, 225, 238)))
                    e.Graphics.DrawRectangle(p, 0, 0, _banner.Width - 1, _banner.Height - 1);
            };

            _lblBanner = new Label
            {
                AutoSize = true,
                Font = new Font("Yu Gothic UI", 11.5f, FontStyle.Bold),
                ForeColor = OkFore,
                Location = new Point(18, 8),
                BackColor = Color.Transparent
            };
            _lblBannerSub = new Label
            {
                AutoSize = false,
                ForeColor = Muted,
                Location = new Point(20, 36),
                Height = 20,
                AutoEllipsis = true,
                BackColor = Color.Transparent
            };
            _banner.Resize += delegate
            {
                _lblBannerSub.Width = Math.Max(120, _banner.ClientSize.Width - 40);
            };

            _banner.Controls.Add(_lblBanner);
            _banner.Controls.Add(_lblBannerSub);
            return _banner;
        }

        private Control BuildGrid()
        {
            _grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(18, 0, 18, 6),
                BackgroundColor = Color.White,
                BorderStyle = BorderStyle.None,
                CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
                GridColor = Color.FromArgb(232, 237, 244),
                RowHeadersVisible = false,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                ReadOnly = true,
                MultiSelect = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.DisplayedCells,
                ScrollBars = ScrollBars.Vertical,
                EnableHeadersVisualStyles = false,
                ColumnHeadersHeight = 32,
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
                RowTemplate = { Height = 28 }
            };
            _grid.DefaultCellStyle.BackColor = Color.White;
            _grid.DefaultCellStyle.ForeColor = Ink;
            _grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(224, 238, 252);
            _grid.DefaultCellStyle.SelectionForeColor = Ink;
            _grid.DefaultCellStyle.Padding = new Padding(6, 0, 6, 0);
            _grid.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
            _grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(238, 243, 249);
            _grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(60, 72, 88);
            _grid.ColumnHeadersDefaultCellStyle.Font = _boldFont;
            _grid.ColumnHeadersDefaultCellStyle.Padding = new Padding(6, 0, 6, 0);

            _grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                HeaderText = "状態",
                Name = "colState",
                Width = 78,
                ReadOnly = true,
                SortMode = DataGridViewColumnSortMode.NotSortable
            });
            _grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                HeaderText = "項目",
                Name = "colName",
                Width = 330,
                ReadOnly = true,
                SortMode = DataGridViewColumnSortMode.NotSortable
            });
            _grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                HeaderText = "詳細 / 対処",
                Name = "colDetail",
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
                ReadOnly = true,
                SortMode = DataGridViewColumnSortMode.NotSortable
            });

            _grid.CellDoubleClick += delegate(object s, DataGridViewCellEventArgs e)
            {
                if (e.RowIndex < 0 || e.RowIndex >= _diag.Items.Count) return;
                CheckItem it = _diag.Items[e.RowIndex];
                string text = it.Name + "\n\n" + it.Detail;
                if (!string.IsNullOrEmpty(it.Fix)) text += "\n\n対処: " + it.Fix;
                MessageBox.Show(this, text, "詳細", MessageBoxButtons.OK, MessageBoxIcon.Information);
            };

            return _grid;
        }

        private Control BuildOptions()
        {
            FlowLayoutPanel p = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(18, 6, 18, 0),
                BackColor = Color.Transparent,
                WrapContents = false,
                Margin = new Padding(0)
            };
            _chkRestorePoint = new CheckBox
            {
                Text = "修復前に復元ポイントを作成（時間がかかります）",
                AutoSize = true,
                Margin = new Padding(0, 4, 24, 0),
                ForeColor = Muted
            };
            _chkRestoreDefaults = new CheckBox
            {
                Text = "Defender 設定を既定値に戻す（除外設定も消えます）",
                AutoSize = true,
                Margin = new Padding(0, 4, 24, 0),
                ForeColor = Muted
            };
            _chkUpdateSigs = new CheckBox
            {
                Text = "修復後に定義を更新",
                AutoSize = true,
                Margin = new Padding(0, 4, 0, 0),
                ForeColor = Muted
            };
            p.Controls.Add(_chkRestorePoint);
            p.Controls.Add(_chkRestoreDefaults);
            p.Controls.Add(_chkUpdateSigs);
            return p;
        }

        private Control BuildButtons()
        {
            FlowLayoutPanel p = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(18, 8, 18, 8),
                BackColor = Color.Transparent,
                WrapContents = false,
                Margin = new Padding(0)
            };

            _btnRepair = MakeButton("修復を実行", true, 180);
            _btnScan = MakeButton("再診断 (F5)", false, 130);
            _btnReport = MakeButton("レポート保存", false, 130);
            _btnClear = MakeButton("ログをクリア", false, 130);
            _btnFolder = MakeButton("フォルダーを開く", false, 150);

            _btnRepair.Click += delegate { StartRepair(false); };
            _btnScan.Click += delegate { RunDiagnostics(); };
            _btnReport.Click += delegate { SaveReport(); };
            _btnClear.Click += delegate { _txtLog.Clear(); };
            _btnFolder.Click += delegate
            {
                try { Process.Start("explorer.exe", Application.StartupPath); }
                catch { }
            };

            p.Controls.Add(_btnRepair);
            p.Controls.Add(_btnScan);
            p.Controls.Add(_btnReport);
            p.Controls.Add(_btnClear);
            p.Controls.Add(_btnFolder);
            return p;
        }

        private Button MakeButton(string text, bool primary, int width)
        {
            Button b = new Button
            {
                Text = text,
                Width = width,
                Height = 36,
                FlatStyle = FlatStyle.Flat,
                UseVisualStyleBackColor = false,
                BackColor = primary ? Accent : Color.White,
                ForeColor = primary ? Color.White : Color.FromArgb(40, 52, 68),
                Cursor = Cursors.Hand,
                Margin = new Padding(0, 0, 10, 0),
                Font = primary ? new Font(Font, FontStyle.Bold) : Font
            };
            b.FlatAppearance.BorderSize = primary ? 0 : 1;
            b.FlatAppearance.BorderColor = Color.FromArgb(201, 211, 223);
            b.FlatAppearance.MouseOverBackColor = primary ? Color.FromArgb(12, 90, 160) : Color.FromArgb(245, 248, 252);
            return b;
        }

        private Control BuildLog()
        {
            Panel box = new Panel
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(18, 0, 18, 8),
                Padding = new Padding(12, 0, 12, 8),
                BackColor = Color.White
            };
            _txtLog = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                Dock = DockStyle.Fill,
                BackColor = Color.White,
                BorderStyle = BorderStyle.None,
                Font = new Font("Consolas", 9f),
                WordWrap = false
            };
            Label caption = new Label
            {
                Text = "実行ログ",
                Dock = DockStyle.Top,
                Height = 24,
                Font = _boldFont,
                ForeColor = Color.FromArgb(60, 72, 88),
                Padding = new Padding(0, 4, 0, 0),
                BackColor = Color.White
            };
            box.Controls.Add(_txtLog);
            box.Controls.Add(caption);
            return box;
        }

        private Control BuildFooter()
        {
            Panel p = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent,
                Padding = new Padding(20, 5, 20, 5),
                Margin = new Padding(0)
            };
            _lblFooter = new Label
            {
                Dock = DockStyle.Fill,
                AutoSize = false,
                ForeColor = Muted,
                TextAlign = ContentAlignment.MiddleLeft,
                Text = ""
            };
            _progress = new ProgressBar
            {
                Dock = DockStyle.Right,
                Width = 180,
                Height = 14,
                Style = ProgressBarStyle.Marquee,
                Visible = false,
                Margin = new Padding(0)
            };
            p.Controls.Add(_progress);
            p.Controls.Add(_lblFooter);
            return p;
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            AppendLog("Defender 復元ツール v1.0");
            AppendLog("dControl などの無効化ツールが残した設定を検査します。");
            RunDiagnostics();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.F5)
            {
                RunDiagnostics();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void RunDiagnostics()
        {
            if (_busy) return;
            SetBusy(true, "診断中...");

            Diag d = new Diag();
            Exception error = null;

            Thread t = new Thread(delegate()
            {
                try { d.RunAll(); }
                catch (Exception ex) { error = ex; }

                try
                {
                    BeginInvoke(new Action(delegate
                    {
                        _diag = d;
                        if (error != null) AppendLog("診断エラー: " + error.Message);
                        Render();
                        SetBusy(false, null);

                        if (_pendingRepair)
                        {
                            _pendingRepair = false;
                            if (_diag.Admin) StartRepair(true);
                            else Elevate();
                        }
                    }));
                }
                catch { }
            });
            t.IsBackground = true;
            t.Start();
        }

        private void Render()
        {
            if (_diag == null) return;

            _grid.SuspendLayout();
            _grid.Rows.Clear();
            foreach (CheckItem it in _diag.Items)
            {
                string detail = it.Detail;
                if (!string.IsNullOrEmpty(it.Fix)) detail += "  → " + it.Fix;

                int index = _grid.Rows.Add(it.StateText, it.Name, detail);
                DataGridViewRow row = _grid.Rows[index];
                row.Cells[0].Style.ForeColor = StateColor(it.State);
                row.Cells[0].Style.Font = _boldFont;
                row.Cells[2].Style.ForeColor = Color.FromArgb(70, 82, 98);
                row.Cells[1].ToolTipText = it.Name;
                row.Cells[2].ToolTipText = detail;

                if (it.State == St.Fail) row.DefaultCellStyle.BackColor = Color.FromArgb(255, 246, 247);
                else if (it.State == St.Warn) row.DefaultCellStyle.BackColor = Color.FromArgb(255, 251, 240);
            }
            _grid.ResumeLayout();

            RenderBanner();

            _lblOs.Text = "OS: " + _diag.OsText;
            _lblFooter.Text = "最終診断 " + DateTime.Now.ToString("HH:mm:ss") + " / " +
                (_diag.Admin ? "管理者権限: あり" : "管理者権限: なし（診断のみ）");
            _btnRepair.Text = _diag.Admin ? "修復を実行" : "管理者として実行して修復";
        }

        private void RenderBanner()
        {
            St overall = _diag.Overall;
            Color back, fore;
            string title, sub;

            if (overall == St.Fail)
            {
                back = FailBack;
                fore = FailFore;
                title = "修復が必要な項目があります（" + _diag.FailCount + " 件）";
                sub = "「修復を実行」で Defender を正常な状態に戻します。";
            }
            else if (overall == St.Warn)
            {
                back = WarnBack;
                fore = WarnFore;
                title = "確認が必要な項目があります（" + _diag.WarnCount + " 件）";
                sub = "内容を確認し、必要に応じて「修復を実行」してください。";
            }
            else
            {
                back = OkBack;
                fore = OkFore;
                title = "この PC の Defender は正常に動作しています";
                sub = "セキュリティの主要コンポーネントはすべて有効です。";
            }

            if (!_diag.Admin)
                sub += "（管理者権限で起動すると修復できます）";

            _banner.BackColor = back;
            _lblBanner.ForeColor = fore;
            _lblBanner.Text = title;
            _lblBannerSub.Text = sub;
            _lblBannerSub.ForeColor = fore;
        }

        private static Color StateColor(St s)
        {
            switch (s)
            {
                case St.Ok: return Color.FromArgb(15, 112, 62);
                case St.Warn: return Color.FromArgb(150, 92, 0);
                case St.Fail: return Color.FromArgb(180, 35, 45);
                case St.Skip: return Color.FromArgb(130, 140, 152);
                default: return Color.FromArgb(70, 90, 115);
            }
        }

        private void SetBusy(bool busy, string message)
        {
            _busy = busy;
            UseWaitCursor = busy;
            _btnScan.Enabled = !busy;
            _btnRepair.Enabled = !busy;
            _btnReport.Enabled = !busy;
            _progress.Visible = busy;
            if (message != null) _lblFooter.Text = message;
        }

        private void StartRepair(bool auto)
        {
            if (_busy) return;

            if (!_diag.Admin)
            {
                Elevate();
                return;
            }

            string msg =
                "次の修復を実行します。\n\n" +
                "・Defender を無効化するポリシー / レジストリ値の削除\n" +
                "・サービスとドライバーの開始構成の復元\n" +
                "・IFEO 乗っ取りの除去\n" +
                "・dControl の除外設定の削除\n" +
                "・Windows セキュリティ アプリの再登録（必要な場合）\n" +
                "・ファイアウォールの有効化\n\n" +
                "レジストリとサービス構成を変更します。続行しますか？";

            if (!auto && !_autoYes)
            {
                if (MessageBox.Show(this, msg, "修復の確認", MessageBoxButtons.OKCancel,
                        MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.OK)
                    return;
            }

            bool restorePoint = _chkRestorePoint.Checked;
            bool restoreDefaults = _chkRestoreDefaults.Checked;
            bool updateSigs = _chkUpdateSigs.Checked;
            int before = _diag.FailCount;

            SetBusy(true, "修復中...");
            AppendLog("");
            AppendLog("■ 修復開始 " + DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss"));

            Action<string> sink = delegate(string line)
            {
                try
                {
                    if (!IsDisposed) BeginInvoke(new Action(delegate { AppendLog(line); }));
                }
                catch { }
            };

            Thread t = new Thread(delegate()
            {
                Repairer rep = new Repairer(_diag, sink);
                try { rep.Run(restorePoint, restoreDefaults, updateSigs); }
                catch (Exception ex) { sink("修復エラー: " + ex.Message); }

                Diag after = new Diag();
                Exception error = null;
                try { after.RunAll(); }
                catch (Exception ex) { error = ex; }

                try
                {
                    BeginInvoke(new Action(delegate
                    {
                        _lastRepairLog.Clear();
                        _lastRepairLog.AddRange(rep.Log);
                        _diag = after;
                        if (error != null) AppendLog("再診断エラー: " + error.Message);

                        Render();
                        SetBusy(false, null);
                        AppendLog("■ 修復完了");
                        AppendLog("");

                        string summary = "修復処理が完了しました。\n\n要修復: " + before + " → " + _diag.FailCount + " 件";
                        if (rep.RebootNeeded)
                            summary += "\n\nドライバー設定を変更したため、Windows を再起動してください。";
                        if (_diag.FailCount > 0)
                            summary += "\n\n残っている項目は「実行ログ」と診断結果を確認してください。";

                        MessageBox.Show(this, summary, "修復結果",
                            MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }));
                }
                catch { }
            });
            t.IsBackground = true;
            t.Start();
        }

        private void Elevate()
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(Application.ExecutablePath, "--repair --yes");
                psi.UseShellExecute = true;
                psi.Verb = "runas";
                Process.Start(psi);
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this,
                    "管理者として起動できませんでした。\n\n" + ex.Message,
                    "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void SaveReport()
        {
            using (SaveFileDialog dlg = new SaveFileDialog())
            {
                dlg.Filter = "テキスト ファイル (*.txt)|*.txt|すべてのファイル (*.*)|*.*";
                dlg.FileName = "DefenderRestore_Report_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".txt";
                try { dlg.InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory); }
                catch { }

                if (dlg.ShowDialog(this) != DialogResult.OK) return;

                try
                {
                    StringBuilder sb = new StringBuilder();
                    sb.AppendLine(_diag.Report());
                    if (_lastRepairLog.Count > 0)
                    {
                        sb.AppendLine();
                        sb.AppendLine("--- 修復ログ ---");
                        foreach (string l in _lastRepairLog) sb.AppendLine(l);
                    }
                    File.WriteAllText(dlg.FileName, sb.ToString(), new UTF8Encoding(true));
                    MessageBox.Show(this, "レポートを保存しました。\n" + dlg.FileName,
                        "保存完了", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "保存に失敗しました。\n\n" + ex.Message,
                        "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void AppendLog(string line)
        {
            if (_txtLog == null) return;
            _txtLog.AppendText(line + Environment.NewLine);
        }
    }
}
