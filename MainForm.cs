namespace EmailChecker;

internal sealed class MainForm : Form
{
    // ── Controls ─────────────────────────────────────────────────────────────
    private readonly TextBox       _apiKeyBox;
    private readonly ListBox       _fileList;
    private readonly Button        _removeBtn;
    private readonly CheckBox      _dedupCb;
    private readonly CheckBox      _validityCb;
    private readonly CheckBox      _groupCb;
    private readonly NumericUpDown _threadBox;
    private readonly RichTextBox   _emailBox;
    private readonly Label         _statusLbl;
    private readonly Label         _elapsedLbl;
    private readonly ProgressBar   _progressBar;
    private readonly System.Windows.Forms.Timer _elapsedTimer = new() { Interval = 1000 };
    private DateTime _processStart;
    private readonly Button        _pauseBtn;
    private readonly Button        _stopBtn;
    private readonly Button        _logsBtn;
    private readonly Button        _saveBtn;

    // ── State ─────────────────────────────────────────────────────────────────
    private readonly List<string>                    _filesOrder  = new();
    private readonly Dictionary<string, string>      _filePaths   = new();
    private readonly Dictionary<string, List<string>> _fileEmails = new();
    private readonly List<string>                    _logLines    = new();

    private bool   _busy;
    private bool   _dedupDone;
    private volatile bool _paused;
    private CancellationTokenSource? _cts;
    private DateTime _lastUiUpdate = DateTime.MinValue;
    private string?  _logTempFile;

    // ── Constructor ───────────────────────────────────────────────────────────
    public MainForm()
    {
        Text          = "Email Checker";
        MinimumSize   = new Size(460, 400);
        Size          = new Size(500, 500);
        StartPosition = FormStartPosition.CenterScreen;
        Font          = new Font("Segoe UI", 9f);

        try
        {
            var ico = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            if (ico != null) Icon = ico;
        }
        catch { }

        // ── Bottom bar: progress bar on top, status label below ───────────────
        var bottomPanel = new Panel
        {
            Dock    = DockStyle.Bottom,
            Height  = 44,
            Padding = new Padding(10, 3, 10, 5),
        };
        _progressBar = new ProgressBar
        {
            Dock    = DockStyle.Top,
            Height  = 12,
            Minimum = 0,
            Maximum = 100,
            Value   = 0,
            Style   = ProgressBarStyle.Continuous,
        };
        // Status row: status text (left) + elapsed time (right)
        var statusRow = new Panel { Dock = DockStyle.Fill };
        _elapsedLbl = new Label
        {
            Dock      = DockStyle.Right,
            AutoSize  = false,
            Width     = 62,
            TextAlign = ContentAlignment.MiddleRight,
            ForeColor = SystemColors.GrayText,
            Text      = "",
        };
        _statusLbl = new Label
        {
            Dock      = DockStyle.Fill,
            AutoSize  = false,
            TextAlign = ContentAlignment.MiddleLeft,
            Text      = "Ready",
            ForeColor = SystemColors.GrayText,
        };
        statusRow.Controls.Add(_elapsedLbl); // Right docks first
        statusRow.Controls.Add(_statusLbl);  // Fill takes remaining left area

        bottomPanel.Controls.Add(_progressBar); // Top → docks first
        bottomPanel.Controls.Add(statusRow);    // Fill → status row below

        _elapsedTimer.Tick += (_, _) =>
        {
            var e = DateTime.Now - _processStart;
            _elapsedLbl.Text = e.TotalHours >= 1
                ? $"{(int)e.TotalHours}:{e.Minutes:D2}:{e.Seconds:D2}"
                : $"{e.Minutes}:{e.Seconds:D2}";
        };

        // ── Content area ─────────────────────────────────────────────────────
        var content = new TableLayoutPanel
        {
            Dock        = DockStyle.Fill,
            ColumnCount = 1,
            RowCount    = 5,
            Padding     = new Padding(12, 12, 12, 6),
        };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));       // 0: API key
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));       // 1: File controls
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));       // 2: Checkboxes + threads
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));   // 3: Email text
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));       // 4: Action buttons

        // ── Row 0: API key ────────────────────────────────────────────────────
        var apiRow = new TableLayoutPanel
        {
            ColumnCount = 2, RowCount = 1,
            Dock = DockStyle.Fill, AutoSize = true,
            Margin = new Padding(0, 0, 0, 10),
        };
        apiRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        apiRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        apiRow.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var apiLabel = new Label
        {
            Text    = "API key",
            AutoSize = true,
            Anchor  = AnchorStyles.Left | AnchorStyles.Top,
            Padding = new Padding(0, 4, 8, 0),
        };
        _apiKeyBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Text = EmailLogic.LoadApiKey(),
        };
        _apiKeyBox.Leave += (_, _) => EmailLogic.SaveApiKey(_apiKeyBox.Text);
        apiRow.Controls.Add(apiLabel,   0, 0);
        apiRow.Controls.Add(_apiKeyBox, 1, 0);

        // ── Row 1: File controls ──────────────────────────────────────────────
        var fileRow = new TableLayoutPanel
        {
            ColumnCount = 2, RowCount = 1,
            Dock = DockStyle.Fill, AutoSize = true,
            Margin = new Padding(0, 0, 0, 10),
        };
        fileRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        fileRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        fileRow.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var btnPanel = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            AutoSize      = true,
            WrapContents  = false,
            Padding       = new Padding(0, 0, 8, 0),
        };
        var importBtn = MakeButton("Import …", OnImport);
        _removeBtn    = MakeButton("Remove",   OnRemove);
        var clearBtn  = MakeButton("Clear all",OnClearAll);
        _removeBtn.Enabled = false;
        btnPanel.Controls.AddRange(new Control[] { importBtn, _removeBtn, clearBtn });

        _fileList = new ListBox
        {
            Dock              = DockStyle.Fill,
            SelectionMode     = SelectionMode.One,
            HorizontalScrollbar = true,
            Height            = 84,   // matches 3-button panel height
        };
        _fileList.SelectedIndexChanged += (_, _) =>
            _removeBtn.Enabled = !_busy && _fileList.SelectedIndex >= 0;

        fileRow.Controls.Add(btnPanel,  0, 0);
        fileRow.Controls.Add(_fileList, 1, 0);

        // ── Row 2: Checkboxes + Threads inline ───────────────────────────────
        var cbRow = new FlowLayoutPanel
        {
            Dock          = DockStyle.Fill,
            AutoSize      = true,
            FlowDirection = FlowDirection.LeftToRight,
            Margin        = new Padding(0, 0, 0, 8),
            WrapContents  = false,
        };
        _dedupCb    = new CheckBox { Text = "de-duplicate",    AutoSize = true, Margin = new Padding(0, 3, 8, 2) };
        _validityCb = new CheckBox { Text = "check validity",  AutoSize = true, Margin = new Padding(0, 3, 8, 2) };
        _groupCb    = new CheckBox { Text = "group by domain", AutoSize = true, Margin = new Padding(0, 3, 10, 2) };
        _dedupCb.CheckedChanged    += OnDedup;
        _validityCb.CheckedChanged += OnCheckValidity;
        _groupCb.CheckedChanged    += OnGroupByDomain;

        var threadsLbl = new Label
        {
            Text     = "Threads:",
            AutoSize = true,
            Padding  = new Padding(0, 4, 2, 0),
            Margin   = new Padding(0, 0, 0, 0),
        };
        _threadBox = new NumericUpDown
        {
            Minimum   = 1,
            Maximum   = 20,
            Value     = 4,
            Width     = 44,
            TextAlign = HorizontalAlignment.Center,
            Margin    = new Padding(0, 1, 0, 0),
        };
        cbRow.Controls.AddRange(new Control[] { _dedupCb, _validityCb, _groupCb, threadsLbl, _threadBox });

        // ── Row 3: Email text area ────────────────────────────────────────────
        _emailBox = new RichTextBox
        {
            Dock       = DockStyle.Fill,
            Font       = new Font("Consolas", 9f),
            WordWrap   = false,
            ScrollBars = RichTextBoxScrollBars.Both,
            DetectUrls = false,
        };

        // ── Row 4: Action buttons — 4 equal columns on one row ───────────────
        var actionRow = new TableLayoutPanel
        {
            ColumnCount = 4, RowCount = 1,
            Dock = DockStyle.Fill, AutoSize = true,
            Margin = new Padding(0, 8, 0, 2),
        };
        actionRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        actionRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        actionRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        actionRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        actionRow.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        _pauseBtn = MakeButton("Pause", OnPause);
        _stopBtn  = MakeButton("Stop",  OnStop);
        _logsBtn  = MakeButton("Logs",  OnShowLogs);
        _saveBtn  = MakeButton("Save",  OnSave);
        _pauseBtn.Enabled = false;
        _stopBtn.Enabled  = false;
        _pauseBtn.Dock = DockStyle.Fill;
        _stopBtn.Dock  = DockStyle.Fill;
        _logsBtn.Dock  = DockStyle.Fill;
        _saveBtn.Dock  = DockStyle.Fill;

        actionRow.Controls.Add(_pauseBtn, 0, 0);
        actionRow.Controls.Add(_stopBtn,  1, 0);
        actionRow.Controls.Add(_logsBtn,  2, 0);
        actionRow.Controls.Add(_saveBtn,  3, 0);

        // ── Assemble ─────────────────────────────────────────────────────────
        content.Controls.Add(apiRow,    0, 0);
        content.Controls.Add(fileRow,   0, 1);
        content.Controls.Add(cbRow,     0, 2);
        content.Controls.Add(_emailBox, 0, 3);
        content.Controls.Add(actionRow, 0, 4);

        Controls.Add(content);
        Controls.Add(bottomPanel);

        FormClosing += (_, _) =>
        {
            EmailLogic.SaveApiKey(_apiKeyBox.Text);
            DeleteLogTemp();
        };
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static Button MakeButton(string text, EventHandler handler)
    {
        var b = new Button
        {
            Text     = text,
            AutoSize = false,
            Width    = 80,
            Height   = 28,
            Margin   = new Padding(0, 0, 4, 0),
        };
        b.Click += handler;
        return b;
    }

    // ── File operations ───────────────────────────────────────────────────────

    private void OnImport(object? s, EventArgs e)
    {
        using var dlg = new OpenFileDialog
        {
            Title       = "Import email files",
            Filter      = "Text files (*.txt)|*.txt|All files (*.*)|*.*",
            Multiselect = true,
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        int imported = 0;
        foreach (var path in dlg.FileNames)
        {
            var baseName = Path.GetFileName(path);
            var name     = baseName;
            int n = 1;
            while (_filePaths.ContainsKey(name))
            {
                var stem = Path.GetFileNameWithoutExtension(baseName);
                var ext  = Path.GetExtension(baseName);
                name = $"{stem} ({n++}){ext}";
            }
            _filePaths[name]  = path;
            _filesOrder.Add(name);
            _fileEmails[name] = EmailLogic.ExtractEmailsFromFile(path);
            _fileList.Items.Add(name);
            imported++;
        }
        RefreshEmailText();
        SetStatus($"Imported {imported} file(s) — {TotalEmailCount()} emails");
    }

    private void OnRemove(object? s, EventArgs e)
    {
        var idx = _fileList.SelectedIndex;
        if (idx < 0) return;
        var name = (string)_fileList.Items[idx]!;
        _fileList.Items.RemoveAt(idx);
        _filesOrder.Remove(name);
        _filePaths.Remove(name);
        _fileEmails.Remove(name);
        _removeBtn.Enabled = false;
        RefreshEmailText();
        SetStatus($"Removed — {TotalEmailCount()} emails remain");
    }

    private void OnClearAll(object? s, EventArgs e)
    {
        _fileList.Items.Clear();
        _filesOrder.Clear();
        _filePaths.Clear();
        _fileEmails.Clear();
        _removeBtn.Enabled = false;
        RefreshEmailText();
        SetStatus("Cleared — 0 emails");
    }

    private void RefreshEmailText()
    {
        var all = new List<string>();
        foreach (var name in _filesOrder)
            if (_fileEmails.TryGetValue(name, out var list))
                all.AddRange(list);
        _emailBox.Lines = all.ToArray();
    }

    private int TotalEmailCount() =>
        _fileEmails.Values.Sum(l => l.Count);

    // ── Checkbox handlers ─────────────────────────────────────────────────────

    private void OnDedup(object? s, EventArgs e)
    {
        if (_busy || !_dedupCb.Checked || _dedupDone) return;

        var emails = GetPlainEmails();
        if (emails.Count == 0) { _dedupCb.Checked = false; return; }

        var (unique, removed) = EmailLogic.Deduplicate(emails);
        _emailBox.Lines = unique.ToArray();

        _dedupDone       = true;
        _dedupCb.Enabled = false;
        SetStatus($"De-duplicate: removed {removed}, {unique.Count} remain", 100);
    }

    private void OnCheckValidity(object? s, EventArgs e)
    {
        if (_busy || !_validityCb.Checked) return;

        var apiKey = _apiKeyBox.Text.Trim();
        if (string.IsNullOrEmpty(apiKey))
        {
            MessageBox.Show("Enter your YChecker API key first.", "API Key Required",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _validityCb.Checked = false;
            return;
        }

        var (main, existingInvalid) = EmailLogic.SplitInvalidSection(_emailBox.Text);
        var emailsToCheck = EmailLogic.StripGroupHeaders(main);
        if (emailsToCheck.Count == 0)
        {
            MessageBox.Show("No emails to check.", "Empty",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            _validityCb.Checked = false;
            return;
        }

        var result = MessageBox.Show(
            $"This will verify {emailsToCheck.Count} email(s) via the YChecker API.\n" +
            "Invalid emails will be moved to an INVALID EMAILS section.\n\nContinue?",
            "Check Validity", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (result != DialogResult.Yes) { _validityCb.Checked = false; return; }

        _ = RunValidityCheckAsync(apiKey, emailsToCheck, existingInvalid);
    }

    private async Task RunValidityCheckAsync(
        string apiKey, List<string> emailsToCheck, List<string> existingInvalid)
    {
        LockControls();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        _lastUiUpdate = DateTime.MinValue;

        _logLines.Clear();
        Log($"=== Validity check started: {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");
        Log($"Emails: {emailsToCheck.Count}  |  Threads: {(int)_threadBox.Value}");
        Log(new string('-', 60));

        int threads   = (int)_threadBox.Value;
        int total     = emailsToCheck.Count;
        int completed = 0;

        var valid        = new List<string>();
        var invalid      = new List<string>(existingInvalid);
        var sem          = new SemaphoreSlim(threads, threads);
        var pendingTasks = new List<Task>();

        foreach (var email in emailsToCheck)
        {
            // Honour pause before dispatching the next slot
            while (_paused && !ct.IsCancellationRequested)
                await Task.Delay(80);

            if (ct.IsCancellationRequested) break;

            try { await sem.WaitAsync(ct); }
            catch (OperationCanceledException) { break; }

            var emailCopy = email;
            var t = Task.Run(async () =>
            {
                try
                {
                    var (isValid, status) = await EmailLogic.CheckEmailValidityAsync(emailCopy, apiKey);

                    List<string> validSnap, invalidSnap;
                    int done;
                    lock (valid)
                    {
                        if (isValid) valid.Add(emailCopy);
                        else         invalid.Add(emailCopy);
                        done      = ++completed;
                        validSnap   = new List<string>(valid);
                        invalidSnap = new List<string>(invalid);
                    }

                    this.Invoke(() =>
                    {
                        Log($"[{(isValid ? " OK " : "INVL")}] {emailCopy} — {status}");

                        var now = DateTime.Now;
                        if ((now - _lastUiUpdate).TotalMilliseconds >= 250)
                        {
                            UpdateEmailBoxRealtime(validSnap, invalidSnap);
                            _lastUiUpdate = now;
                        }
                        SetStatus($"Checking {done}/{total} …",
                            (int)((double)done / total * 100));
                    });
                }
                finally { sem.Release(); }
            });
            pendingTasks.Add(t);
        }

        try { await Task.WhenAll(pendingTasks); } catch { }

        List<string> finalValid, finalInvalid;
        lock (valid) { finalValid = new List<string>(valid); finalInvalid = new List<string>(invalid); }

        this.Invoke(() =>
        {
            UpdateEmailBoxRealtime(finalValid, finalInvalid);
            UnlockControls();
            _validityCb.Checked = false;
            bool stopped   = ct.IsCancellationRequested;
            int newInvalid = finalInvalid.Count - existingInvalid.Count;
            var summary    = stopped
                ? $"Stopped — {newInvalid} invalid found, {finalValid.Count} valid kept"
                : $"Done — {newInvalid} invalid, {finalValid.Count} valid remain";

            Log(new string('-', 60));
            Log(summary);
            Log($"=== Finished: {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");

            SetStatus(summary, stopped ? _progressBar.Value : 100);
        });
    }

    private void UpdateEmailBoxRealtime(List<string> valid, List<string> invalid)
    {
        string mainText = _groupCb.Checked
            ? EmailLogic.FormatGrouped(EmailLogic.GroupByDomain(valid))
            : string.Join(Environment.NewLine, valid);
        _emailBox.Text = EmailLogic.MergeWithInvalid(mainText, invalid);
    }

    // ── Pause / Stop / Logs ───────────────────────────────────────────────────

    private void OnPause(object? s, EventArgs e)
    {
        if (!_busy) return;
        _paused = !_paused;
        _pauseBtn.Text = _paused ? "Resume" : "Pause";
        var msg = _paused ? "Paused — click Resume to continue" : "Resuming …";
        Log($"[----] {msg}");
        SetStatus(msg, _progressBar.Value);
    }

    private void OnStop(object? s, EventArgs e)
    {
        _cts?.Cancel();
        _paused           = false;
        _pauseBtn.Text    = "Pause";
        _stopBtn.Enabled  = false;
        _pauseBtn.Enabled = false;
        Log("[----] Stopped by user");
        SetStatus("Stopping …", _progressBar.Value);
    }

    private void OnShowLogs(object? s, EventArgs e)
    {
        if (_logLines.Count == 0)
        {
            MessageBox.Show("No log entries yet. Run a validity check first.",
                "Logs", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        try
        {
            DeleteLogTemp();
            _logTempFile = Path.Combine(Path.GetTempPath(),
                $"EmailChecker_log_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
            File.WriteAllLines(_logTempFile, _logLines);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName        = _logTempFile,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not open log file:\n{ex.Message}", "Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void DeleteLogTemp()
    {
        if (_logTempFile == null) return;
        try { if (File.Exists(_logTempFile)) File.Delete(_logTempFile); } catch { }
        _logTempFile = null;
    }

    private void Log(string line) =>
        _logLines.Add($"{DateTime.Now:HH:mm:ss.fff}  {line}");

    // ── Group by domain ───────────────────────────────────────────────────────

    private void OnGroupByDomain(object? s, EventArgs e)
    {
        if (_busy) return;

        var (main, invalid) = EmailLogic.SplitInvalidSection(_emailBox.Text);

        if (_groupCb.Checked)
        {
            var emails = EmailLogic.StripGroupHeaders(main);
            var groups = EmailLogic.GroupByDomain(emails);
            _emailBox.Text = EmailLogic.MergeWithInvalid(
                EmailLogic.FormatGrouped(groups), invalid);
            SetStatus($"Group by domain: {groups.Count} group(s)", 100);
        }
        else
        {
            var flat = EmailLogic.StripGroupHeaders(main);
            _emailBox.Text = EmailLogic.MergeWithInvalid(
                string.Join(Environment.NewLine, flat), invalid);
            SetStatus("Group by domain: ungrouped", 0);
        }
    }

    // ── Save ──────────────────────────────────────────────────────────────────

    private void OnSave(object? s, EventArgs e)
    {
        var text = _emailBox.Text.Trim();
        if (string.IsNullOrEmpty(text))
        {
            MessageBox.Show("The email list is empty.", "Nothing to save",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        bool isGrouped = text.Split('\n')
            .Any(l => l.Trim().StartsWith("=== ") && l.Trim().EndsWith(" ==="));

        if (isGrouped)
            SaveGroupedFiles(text);
        else
        {
            var ts = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            using var dlg = new SaveFileDialog
            {
                Title      = "Save emails",
                Filter     = "Text files (*.txt)|*.txt|All files (*.*)|*.*",
                FileName   = $"emails_{ts}.txt",
                DefaultExt = "txt",
            };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            File.WriteAllText(dlg.FileName, text);
            SetStatus($"Saved → {Path.GetFileName(dlg.FileName)}", 100);
        }
    }

    private void SaveGroupedFiles(string text)
    {
        using var dlg = new FolderBrowserDialog
        {
            Description = "Select folder to save grouped files",
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        var groups = EmailLogic.ParseGroupedText(text);
        var lowerGroups = groups.ToDictionary(
            kv => kv.Key.ToLowerInvariant(), kv => kv.Value,
            StringComparer.OrdinalIgnoreCase);

        var paths = EmailLogic.SaveGroups(lowerGroups, dlg.SelectedPath);
        SetStatus($"Saved {paths.Count} file(s) to folder", 100);
        MessageBox.Show($"Saved {paths.Count} file(s) to:\n{dlg.SelectedPath}",
            "Saved", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    // ── Control locking ───────────────────────────────────────────────────────

    private void LockControls()
    {
        _busy        = true;
        _paused      = false;
        _processStart = DateTime.Now;
        _elapsedLbl.Text    = "0:00";
        _elapsedTimer.Start();
        _apiKeyBox.Enabled  = false;
        _removeBtn.Enabled  = false;
        _dedupCb.Enabled    = false;
        _validityCb.Enabled = false;
        _groupCb.Enabled    = false;
        _threadBox.Enabled  = false;
        _pauseBtn.Enabled   = true;
        _pauseBtn.Text      = "Pause";
        _stopBtn.Enabled    = true;
        _saveBtn.Enabled    = false;
    }

    private void UnlockControls()
    {
        _elapsedTimer.Stop();
        _busy   = false;
        _paused = false;
        _pauseBtn.Text      = "Pause";
        _apiKeyBox.Enabled  = true;
        _removeBtn.Enabled  = _fileList.SelectedIndex >= 0;
        if (!_dedupDone) _dedupCb.Enabled = true;
        _validityCb.Enabled = true;
        _groupCb.Enabled    = true;
        _threadBox.Enabled  = true;
        _pauseBtn.Enabled   = false;
        _stopBtn.Enabled    = false;
        _saveBtn.Enabled    = true;
    }

    // ── Misc ──────────────────────────────────────────────────────────────────

    private List<string> GetPlainEmails()
    {
        var (main, _) = EmailLogic.SplitInvalidSection(_emailBox.Text);
        return EmailLogic.StripGroupHeaders(main);
    }

    private void SetStatus(string text, int pct = -1)
    {
        _statusLbl.Text = text;
        if (pct >= 0) _progressBar.Value = Math.Clamp(pct, 0, 100);
    }
}
