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
    private readonly RichTextBox   _emailBox;
    private readonly Label         _statusLbl;
    private readonly ProgressBar   _progressBar;

    // ── State ─────────────────────────────────────────────────────────────────
    private readonly List<string>              _filesOrder  = new();
    private readonly Dictionary<string, string> _filePaths  = new(); // name → full path
    private readonly Dictionary<string, List<string>> _fileEmails = new(); // name → emails

    private bool _busy;
    private bool _dedupDone;

    // ── Constructor ───────────────────────────────────────────────────────────
    public MainForm()
    {
        Text            = "Email Checker";
        MinimumSize     = new Size(720, 560);
        Size            = new Size(820, 640);
        StartPosition   = FormStartPosition.CenterScreen;
        Font            = new Font("Segoe UI", 9f);

        // ── Status bar (bottom) ───────────────────────────────────────────────
        var statusPanel = new Panel { Dock = DockStyle.Bottom, Height = 28 };
        _statusLbl = new Label
        {
            AutoSize    = false,
            TextAlign   = ContentAlignment.MiddleLeft,
            Width       = 280,
            Dock        = DockStyle.Left,
            Text        = "Ready",
            Padding     = new Padding(4, 0, 0, 0),
        };
        _progressBar = new ProgressBar
        {
            Dock    = DockStyle.Fill,
            Minimum = 0,
            Maximum = 100,
            Value   = 0,
            Style   = ProgressBarStyle.Continuous,
        };
        statusPanel.Controls.Add(_progressBar);
        statusPanel.Controls.Add(_statusLbl);  // Left docks over Fill, so add last

        // ── Content area (fills above status bar) ─────────────────────────────
        var content = new TableLayoutPanel
        {
            Dock        = DockStyle.Fill,
            ColumnCount = 1,
            RowCount    = 5,
            Padding     = new Padding(12),
        };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));       // 0: API key
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));       // 1: File controls
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));       // 2: Checkboxes
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));   // 3: Email text (grows)
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));       // 4: Save button

        // ── Row 0: API key ────────────────────────────────────────────────────
        var apiRow = new TableLayoutPanel
        {
            ColumnCount     = 2,
            RowCount        = 1,
            Dock            = DockStyle.Fill,
            AutoSize        = true,
            Margin          = new Padding(0, 0, 0, 12),
        };
        apiRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        apiRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        apiRow.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var apiLabel = new Label
        {
            Text      = "API key",
            AutoSize  = true,
            Anchor    = AnchorStyles.Left | AnchorStyles.Top,
            Padding   = new Padding(0, 4, 8, 0),
        };
        _apiKeyBox = new TextBox
        {
            UseSystemPasswordChar = true,
            Dock                  = DockStyle.Fill,
            Text                  = EmailLogic.LoadEnvApiKey(),
        };
        apiRow.Controls.Add(apiLabel,   0, 0);
        apiRow.Controls.Add(_apiKeyBox, 1, 0);

        // ── Row 1: File controls ──────────────────────────────────────────────
        var fileRow = new TableLayoutPanel
        {
            ColumnCount = 2,
            RowCount    = 1,
            Dock        = DockStyle.Fill,
            AutoSize    = true,
            Margin      = new Padding(0, 0, 0, 10),
        };
        fileRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        fileRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        fileRow.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        // Buttons column
        var btnPanel = new FlowLayoutPanel
        {
            FlowDirection  = FlowDirection.TopDown,
            AutoSize       = true,
            WrapContents   = false,
            Padding        = new Padding(0, 0, 10, 0),
        };
        var importBtn = MakeButton("Import …", OnImport);
        _removeBtn    = MakeButton("Remove",   OnRemove);
        var clearBtn  = MakeButton("Clear all",OnClearAll);
        _removeBtn.Enabled = false;
        btnPanel.Controls.AddRange(new Control[] { importBtn, _removeBtn, clearBtn });

        // File list box
        _fileList = new ListBox
        {
            Dock              = DockStyle.Fill,
            SelectionMode     = SelectionMode.One,
            HorizontalScrollbar = true,
            Height            = 120,
        };
        _fileList.SelectedIndexChanged += (_, _) =>
            _removeBtn.Enabled = !_busy && _fileList.SelectedIndex >= 0;

        fileRow.Controls.Add(btnPanel,  0, 0);
        fileRow.Controls.Add(_fileList, 1, 0);

        // ── Row 2: Checkboxes ─────────────────────────────────────────────────
        var cbPanel = new FlowLayoutPanel
        {
            Dock          = DockStyle.Fill,
            AutoSize      = true,
            FlowDirection = FlowDirection.LeftToRight,
            Margin        = new Padding(0, 0, 0, 8),
        };
        _dedupCb    = new CheckBox { Text = "de-duplicate",    AutoSize = true, Margin = new Padding(0, 2, 20, 2) };
        _validityCb = new CheckBox { Text = "check validity",  AutoSize = true, Margin = new Padding(0, 2, 20, 2) };
        _groupCb    = new CheckBox { Text = "group by domain", AutoSize = true, Margin = new Padding(0, 2, 0,  2) };

        _dedupCb.CheckedChanged    += OnDedup;
        _validityCb.CheckedChanged += OnCheckValidity;
        _groupCb.CheckedChanged    += OnGroupByDomain;

        cbPanel.Controls.AddRange(new Control[] { _dedupCb, _validityCb, _groupCb });

        // ── Row 3: Email text area ────────────────────────────────────────────
        _emailBox = new RichTextBox
        {
            Dock        = DockStyle.Fill,
            Font        = new Font("Consolas", 9f),
            WordWrap    = false,
            ScrollBars  = RichTextBoxScrollBars.Both,
            DetectUrls  = false,
        };

        // ── Row 4: Save button ────────────────────────────────────────────────
        var savePanel = new FlowLayoutPanel
        {
            Dock          = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            AutoSize      = true,
            Margin        = new Padding(0, 8, 0, 0),
        };
        var saveBtn = MakeButton("Save", OnSave);
        savePanel.Controls.Add(saveBtn);

        // ── Assemble layout ───────────────────────────────────────────────────
        content.Controls.Add(apiRow,   0, 0);
        content.Controls.Add(fileRow,  0, 1);
        content.Controls.Add(cbPanel,  0, 2);
        content.Controls.Add(_emailBox,0, 3);
        content.Controls.Add(savePanel,0, 4);

        Controls.Add(content);
        Controls.Add(statusPanel);
    }

    // ── Helper ────────────────────────────────────────────────────────────────

    private static Button MakeButton(string text, EventHandler handler)
    {
        var b = new Button
        {
            Text      = text,
            AutoSize  = false,
            Width     = 100,
            Height    = 28,
            Margin    = new Padding(0, 0, 0, 4),
        };
        b.Click += handler;
        return b;
    }

    // ── File operations ───────────────────────────────────────────────────────

    private void OnImport(object? s, EventArgs e)
    {
        using var dlg = new OpenFileDialog
        {
            Title     = "Import email files",
            Filter    = "Text files (*.txt)|*.txt|All files (*.*)|*.*",
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
        SetStatus($"Imported {imported} file(s) — {TotalEmailCount()} emails", 0);
    }

    private void OnRemove(object? s, EventArgs e)
    {
        var idx  = _fileList.SelectedIndex;
        if (idx < 0) return;
        var name = (string)_fileList.Items[idx]!;
        _fileList.Items.RemoveAt(idx);
        _filesOrder.Remove(name);
        _filePaths.Remove(name);
        _fileEmails.Remove(name);
        _removeBtn.Enabled = false;
        RefreshEmailText();
        SetStatus($"Removed — {TotalEmailCount()} emails remain", 0);
    }

    private void OnClearAll(object? s, EventArgs e)
    {
        _fileList.Items.Clear();
        _filesOrder.Clear();
        _filePaths.Clear();
        _fileEmails.Clear();
        _removeBtn.Enabled = false;
        RefreshEmailText();
        SetStatus("Cleared — 0 emails", 0);
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
        SetStatus($"de-duplicate: removed {removed}, {unique.Count} remain", 100);
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

        var valid   = new List<string>();
        var invalid = new List<string>(existingInvalid);
        int total   = emailsToCheck.Count;

        for (int i = 0; i < total; i++)
        {
            var (isValid, _) = await EmailLogic.CheckEmailValidityAsync(emailsToCheck[i], apiKey);
            if (isValid) valid.Add(emailsToCheck[i]);
            else         invalid.Add(emailsToCheck[i]);

            double pct = (double)(i + 1) / total * 100;
            SetStatus($"Checking {i + 1}/{total} …", (int)pct);
        }

        // Rebuild: re-group if the group checkbox is still on, otherwise flat
        string mainText = _groupCb.Checked
            ? EmailLogic.FormatGrouped(EmailLogic.GroupByDomain(valid))
            : string.Join(Environment.NewLine, valid);

        _emailBox.Text = EmailLogic.MergeWithInvalid(mainText, invalid);

        UnlockControls();
        _validityCb.Checked = false;  // reset so user can run again
        SetStatus($"check validity: {invalid.Count - existingInvalid.Count} new invalid found, " +
                  $"{valid.Count} valid remain", 100);
    }

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
            SetStatus($"group by domain: {groups.Count} group(s)", 100);
        }
        else
        {
            var flat = EmailLogic.StripGroupHeaders(main);
            _emailBox.Text = EmailLogic.MergeWithInvalid(
                string.Join(Environment.NewLine, flat), invalid);
            SetStatus("group by domain: ungrouped", 0);
        }
    }

    // ── Save ──────────────────────────────────────────────────────────────────

    private void OnSave(object? s, EventArgs e)
    {
        var content = _emailBox.Text.Trim();
        if (string.IsNullOrEmpty(content))
        {
            MessageBox.Show("The email list is empty.", "Nothing to save",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        // Check if any section headers exist (including INVALID)
        bool isGrouped = content.Split('\n')
            .Any(l => l.Trim().StartsWith("=== ") && l.Trim().EndsWith(" ==="));

        if (isGrouped)
        {
            SaveGroupedFiles(content);
        }
        else
        {
            var ts = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            using var dlg = new SaveFileDialog
            {
                Title             = "Save emails",
                Filter            = "Text files (*.txt)|*.txt|All files (*.*)|*.*",
                FileName          = $"emails_{ts}.txt",
                DefaultExt        = "txt",
            };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            File.WriteAllText(dlg.FileName, content);
            SetStatus($"Saved → {Path.GetFileName(dlg.FileName)}", 100);
        }
    }

    private void SaveGroupedFiles(string content)
    {
        using var dlg = new FolderBrowserDialog { Description = "Select folder to save grouped files" };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        var groups = EmailLogic.ParseGroupedText(content);
        // Lower-case keys for filenames
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
        _busy = true;
        _apiKeyBox.Enabled  = false;
        _removeBtn.Enabled  = false;
        _dedupCb.Enabled    = false;
        _validityCb.Enabled = false;
        _groupCb.Enabled    = false;
    }

    private void UnlockControls()
    {
        _busy = false;
        _apiKeyBox.Enabled  = true;
        _removeBtn.Enabled  = _fileList.SelectedIndex >= 0;
        if (!_dedupDone) _dedupCb.Enabled = true;
        _validityCb.Enabled = true;
        _groupCb.Enabled    = true;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>All plain emails from the text area, excluding group headers and the invalid section.</summary>
    private List<string> GetPlainEmails()
    {
        var (main, _) = EmailLogic.SplitInvalidSection(_emailBox.Text);
        return EmailLogic.StripGroupHeaders(main);
    }

    private void SetStatus(string text, int pct)
    {
        _statusLbl.Text   = text;
        _progressBar.Value = Math.Clamp(pct, 0, 100);
    }
}
