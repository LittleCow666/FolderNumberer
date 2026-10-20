using System.Globalization;
using System.Text.RegularExpressions;
using System.Drawing;
using System.Text.Json;

namespace FolderNumberer;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}

internal sealed class MainForm : Form
{
    private readonly TextBox _folderBox = new();
    private readonly ComboBox _extensionBox = new();
    private readonly ComboBox _sortBox = new();
    private readonly CheckBox _stripPrefixBox = new();
    private readonly Button _previewButton = new();
    private readonly Button _renameButton = new();
    private readonly Button _undoButton = new();
    private readonly Label _statusLabel = new();
    private readonly DataGridView _grid = new();
    private readonly List<FilePlan> _plans = [];
    private bool _loadingExtensions;
    private string? _loadedFolder;
    private static readonly Regex NumberPrefix = new(@"^\d+\.\s*", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public MainForm()
    {
        Text = "文件编号";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(760, 520);
        Size = new Size(980, 680);
        BackColor = Color.FromArgb(246, 247, 249);
        Font = new Font("Microsoft YaHei UI", 9F);

        BuildLayout();
        UpdateActions();
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(22, 18, 22, 16),
            BackColor = BackColor
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 136));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        Controls.Add(root);

        var title = new Label
        {
            Text = "文件编号",
            Dock = DockStyle.Fill,
            Font = new Font("Microsoft YaHei UI", 17F, FontStyle.Bold),
            ForeColor = Color.FromArgb(33, 43, 54),
            TextAlign = ContentAlignment.MiddleLeft
        };
        root.Controls.Add(title, 0, 0);

        var settings = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(0, 6, 0, 6)
        };
        settings.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        settings.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        settings.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.Controls.Add(settings, 0, 1);

        var folderRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1 };
        folderRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 84));
        folderRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        folderRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 104));
        settings.Controls.Add(folderRow, 0, 0);
        folderRow.Controls.Add(MakeLabel("文件夹"), 0, 0);
        _folderBox.Dock = DockStyle.Fill;
        _folderBox.ReadOnly = true;
        _folderBox.BackColor = Color.White;
        folderRow.Controls.Add(_folderBox, 1, 0);
        var browseButton = MakeButton("选择文件夹…");
        browseButton.Click += (_, _) => ChooseFolder();
        folderRow.Controls.Add(browseButton, 2, 0);

        var optionsRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 5, RowCount = 1 };
        optionsRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 84));
        optionsRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));
        optionsRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 84));
        optionsRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        optionsRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        settings.Controls.Add(optionsRow, 0, 1);
        optionsRow.Controls.Add(MakeLabel("文件格式"), 0, 0);
        ConfigureCombo(_extensionBox);
        _extensionBox.SelectedIndexChanged += (_, _) => RefreshPreview();
        optionsRow.Controls.Add(_extensionBox, 1, 0);
        optionsRow.Controls.Add(MakeLabel("排序方式"), 2, 0);
        ConfigureCombo(_sortBox);
        _sortBox.Items.Add("修改时间（从早到晚）");
        _sortBox.Items.Add("名称（A 到 Z）");
        _sortBox.SelectedIndex = 0;
        _sortBox.SelectedIndexChanged += (_, _) => RefreshPreview();
        optionsRow.Controls.Add(_sortBox, 3, 0);
        _previewButton.Text = "刷新预览";
        _previewButton.Dock = DockStyle.Fill;
        _previewButton.Margin = new Padding(8, 3, 0, 3);
        _previewButton.Click += (_, _) => LoadFolder(_folderBox.Text);
        optionsRow.Controls.Add(_previewButton, 4, 0);

        _stripPrefixBox.Text = "重新编号时移除开头已有的数字序号（例如 1.）";
        _stripPrefixBox.Checked = true;
        _stripPrefixBox.Dock = DockStyle.Fill;
        _stripPrefixBox.ForeColor = Color.FromArgb(71, 81, 91);
        _stripPrefixBox.CheckedChanged += (_, _) => RefreshPreview();
        settings.Controls.Add(_stripPrefixBox, 0, 2);

        var previewArea = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = Color.White,
            Margin = new Padding(0, 4, 0, 8),
            Padding = new Padding(12, 8, 12, 10)
        };
        previewArea.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        previewArea.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.Controls.Add(previewArea, 0, 2);
        var previewTitle = new Label
        {
            Text = "重命名预览",
            Dock = DockStyle.Fill,
            Font = new Font(Font, FontStyle.Bold),
            ForeColor = Color.FromArgb(55, 65, 75),
            TextAlign = ContentAlignment.MiddleLeft
        };
        previewArea.Controls.Add(previewTitle, 0, 0);

        _grid.Dock = DockStyle.Fill;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.AllowUserToResizeRows = false;
        _grid.ReadOnly = true;
        _grid.MultiSelect = false;
        _grid.RowHeadersVisible = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.BackgroundColor = Color.White;
        _grid.BorderStyle = BorderStyle.None;
        _grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        _grid.GridColor = Color.FromArgb(232, 235, 239);
        _grid.ColumnHeadersHeight = 36;
        _grid.RowTemplate.Height = 34;
        _grid.EnableHeadersVisualStyles = false;
        _grid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Color.FromArgb(242, 244, 247),
            ForeColor = Color.FromArgb(64, 73, 82),
            Font = new Font(Font, FontStyle.Bold),
            SelectionBackColor = Color.FromArgb(242, 244, 247),
            SelectionForeColor = Color.FromArgb(64, 73, 82)
        };
        _grid.DefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Color.White,
            ForeColor = Color.FromArgb(45, 54, 63),
            SelectionBackColor = Color.FromArgb(229, 240, 250),
            SelectionForeColor = Color.FromArgb(33, 43, 54),
            Padding = new Padding(5, 0, 5, 0)
        };
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "index", HeaderText = "序号", FillWeight = 9 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "original", HeaderText = "原文件名", FillWeight = 42 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "target", HeaderText = "新文件名", FillWeight = 42 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "result", HeaderText = "检查", FillWeight = 12 });
        previewArea.Controls.Add(_grid, 0, 1);

        var footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1 };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220));
        root.Controls.Add(footer, 0, 3);
        _statusLabel.Text = "选择文件夹并指定一种文件格式。";
        _statusLabel.Dock = DockStyle.Fill;
        _statusLabel.TextAlign = ContentAlignment.MiddleLeft;
        _statusLabel.ForeColor = Color.FromArgb(91, 101, 111);
        footer.Controls.Add(_statusLabel, 0, 0);
        _undoButton.Text = "撤回上次";
        _undoButton.Dock = DockStyle.Fill;
        _undoButton.Click += (_, _) => UndoLastBatch();
        footer.Controls.Add(_undoButton, 1, 0);
        _renameButton.Text = "重命名文件";
        _renameButton.Dock = DockStyle.Fill;
        _renameButton.BackColor = Color.FromArgb(32, 112, 170);
        _renameButton.ForeColor = Color.White;
        _renameButton.FlatStyle = FlatStyle.Flat;
        _renameButton.FlatAppearance.BorderSize = 0;
        _renameButton.Font = new Font(Font, FontStyle.Bold);
        _renameButton.Click += (_, _) => ApplyRenames();
        footer.Controls.Add(_renameButton, 2, 0);
    }

    private static Label MakeLabel(string text) => new()
    {
        Text = text,
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleLeft,
        ForeColor = Color.FromArgb(71, 81, 91)
    };

    private static Button MakeButton(string text) => new()
    {
        Text = text,
        Dock = DockStyle.Fill,
        Margin = new Padding(8, 3, 0, 3),
        FlatStyle = FlatStyle.System
    };

    private static void ConfigureCombo(ComboBox combo)
    {
        combo.Dock = DockStyle.Fill;
        combo.DropDownStyle = ComboBoxStyle.DropDownList;
        combo.IntegralHeight = false;
        combo.Margin = new Padding(0, 3, 8, 3);
    }

    private void ChooseFolder()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "选择要整理文件的文件夹",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false
        };
        if (Directory.Exists(_folderBox.Text)) dialog.SelectedPath = _folderBox.Text;
        if (dialog.ShowDialog(this) == DialogResult.OK) LoadFolder(dialog.SelectedPath);
    }

    private void LoadFolder(string folder)
    {
        if (!Directory.Exists(folder))
        {
            _loadedFolder = null;
            _folderBox.Text = "";
            _extensionBox.Items.Clear();
            RefreshPreview();
            _statusLabel.Text = "请选择一个有效文件夹。";
            return;
        }

        try
        {
            _folderBox.Text = Path.GetFullPath(folder);
            var oldExtension = (_extensionBox.SelectedItem as ExtensionOption)?.Extension;
            var extensions = Directory.EnumerateFiles(_folderBox.Text)
                .Select(path => Path.GetExtension(path) ?? "")
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value, StringComparer.CurrentCultureIgnoreCase)
                .Select(value => new ExtensionOption(value))
                .ToList();

            _loadingExtensions = true;
            _extensionBox.Items.Clear();
            foreach (var extension in extensions) _extensionBox.Items.Add(extension);
            var retainedIndex = extensions.FindIndex(x => string.Equals(x.Extension, oldExtension, StringComparison.OrdinalIgnoreCase));
            _extensionBox.SelectedIndex = retainedIndex >= 0 ? retainedIndex : -1;
            _loadingExtensions = false;
            _loadedFolder = _folderBox.Text;
            RefreshPreview();
            if (extensions.Count == 0) _statusLabel.Text = "此文件夹中没有文件。";
            else if (_extensionBox.SelectedIndex < 0) _statusLabel.Text = "请选择一种文件格式。";
        }
        catch (Exception ex)
        {
            _loadingExtensions = false;
            _loadedFolder = null;
            _plans.Clear();
            RenderPlans();
            _statusLabel.Text = "无法读取文件夹：" + ex.Message;
            UpdateActions();
        }
    }

    private void RefreshPreview()
    {
        if (_loadingExtensions) return;
        _plans.Clear();
        if (_loadedFolder is null || !Directory.Exists(_loadedFolder) || _extensionBox.SelectedItem is not ExtensionOption option)
        {
            RenderPlans();
            UpdateActions();
            return;
        }

        try
        {
            var files = Directory.EnumerateFiles(_loadedFolder)
                .Where(path => string.Equals(Path.GetExtension(path), option.Extension, StringComparison.OrdinalIgnoreCase))
                .Select(path => new FilePlan(path, File.GetLastWriteTime(path), Path.GetFileName(path)))
                .ToList();

            IEnumerable<FilePlan> ordered = _sortBox.SelectedIndex == 1
                ? files.OrderBy(x => GetBaseName(x.OriginalName, _stripPrefixBox.Checked), StringComparer.CurrentCultureIgnoreCase)
                       .ThenBy(x => GetBaseName(x.OriginalName, _stripPrefixBox.Checked), StringComparer.Ordinal)
                       .ThenBy(x => x.OriginalName, StringComparer.OrdinalIgnoreCase)
                : files.OrderBy(x => x.ModifiedAt)
                       .ThenBy(x => x.OriginalName, StringComparer.OrdinalIgnoreCase);
            var index = 1;
            foreach (var plan in ordered)
            {
                plan.TargetName = $"{index++}.{GetBaseName(plan.OriginalName, _stripPrefixBox.Checked)}";
                _plans.Add(plan);
            }

            CheckForConflicts();
            RenderPlans();
            var changes = _plans.Count(x => !string.Equals(x.OriginalName, x.TargetName, StringComparison.Ordinal));
            _statusLabel.Text = _plans.Count == 0
                ? "所选格式下没有文件。"
                : changes == 0 ? $"共 {_plans.Count} 个文件，名称已符合预览。" : $"共 {_plans.Count} 个文件，待重命名 {changes} 个。";
        }
        catch (Exception ex)
        {
            _plans.Clear();
            RenderPlans();
            _statusLabel.Text = "无法生成预览：" + ex.Message;
        }
        UpdateActions();
    }

    private void CheckForConflicts()
    {
        var selectedPaths = new HashSet<string>(_plans.Select(x => Path.GetFullPath(x.FullPath)), StringComparer.OrdinalIgnoreCase);
        var targetNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var conflictNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var plan in _plans)
        {
            if (!targetNames.Add(plan.TargetName)) conflictNames.Add(plan.TargetName);
            var targetPath = Path.Combine(_loadedFolder!, plan.TargetName);
            if (File.Exists(targetPath) && !selectedPaths.Contains(Path.GetFullPath(targetPath))) conflictNames.Add(plan.TargetName);
        }
        foreach (var plan in _plans)
            plan.Conflict = conflictNames.Contains(plan.TargetName);
    }

    private void RenderPlans()
    {
        _grid.Rows.Clear();
        for (var i = 0; i < _plans.Count; i++)
        {
            var plan = _plans[i];
            var row = _grid.Rows.Add((i + 1).ToString(CultureInfo.InvariantCulture), plan.OriginalName, plan.TargetName, plan.Conflict ? "名称冲突" : "可用");
            if (plan.Conflict) _grid.Rows[row].Cells[3].Style.ForeColor = Color.FromArgb(190, 45, 45);
        }
    }

    private void UpdateActions()
    {
        var changes = _plans.Count(x => !string.Equals(x.OriginalName, x.TargetName, StringComparison.Ordinal));
        _renameButton.Enabled = changes > 0 && _plans.All(x => !x.Conflict);
        _renameButton.Text = changes > 0 ? $"重命名 {changes} 个文件" : "重命名文件";
        _previewButton.Enabled = !string.IsNullOrWhiteSpace(_folderBox.Text);
        _undoButton.Enabled = _loadedFolder is not null && File.Exists(UndoJournalPath(_loadedFolder));
    }

    private void ApplyRenames()
    {
        if (_loadedFolder is null || _plans.Count == 0) return;
        CheckForConflicts();
        if (_plans.Any(x => x.Conflict))
        {
            RenderPlans();
            _statusLabel.Text = "存在目标名称冲突，请先调整文件夹内容后刷新预览。";
            UpdateActions();
            return;
        }

        var changes = _plans.Where(x => !string.Equals(x.OriginalName, x.TargetName, StringComparison.Ordinal)).ToList();
        if (changes.Count == 0) return;
        var answer = MessageBox.Show(this,
            $"将按当前预览重命名 {changes.Count} 个文件。\n\n文件夹：{_loadedFolder}\n\n此操作不会覆盖其他文件。是否继续？",
            "确认重命名", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
        if (answer != DialogResult.Yes) return;

        var staged = new List<FilePlan>();
        var finalized = new List<FilePlan>();
        try
        {
            foreach (var plan in changes)
            {
                if (!File.Exists(plan.FullPath)) throw new IOException($"找不到源文件：{plan.OriginalName}");
                plan.TempPath = Path.Combine(_loadedFolder, $".foldernumberer-{Guid.NewGuid():N}.tmp");
                File.Move(plan.FullPath, plan.TempPath);
                staged.Add(plan);
            }
            foreach (var plan in staged)
            {
                var targetPath = Path.Combine(_loadedFolder, plan.TargetName);
                if (File.Exists(targetPath)) throw new IOException($"目标文件已存在：{plan.TargetName}");
                File.Move(plan.TempPath!, targetPath);
                finalized.Add(plan);
            }
            SaveUndoJournal(_loadedFolder, changes);
            LoadFolder(_loadedFolder);
            MessageBox.Show(this, $"已重命名 {changes.Count} 个文件。", "完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            var rollbackErrors = new List<string>();
            foreach (var plan in finalized.AsEnumerable().Reverse())
            {
                try
                {
                    var targetPath = Path.Combine(_loadedFolder, plan.TargetName);
                    if (File.Exists(targetPath) && !File.Exists(plan.TempPath!)) File.Move(targetPath, plan.TempPath!);
                }
                catch (Exception rollbackException) { rollbackErrors.Add(rollbackException.Message); }
            }
            foreach (var plan in staged.AsEnumerable().Reverse())
            {
                try
                {
                    if (File.Exists(plan.TempPath!) && !File.Exists(plan.FullPath)) File.Move(plan.TempPath!, plan.FullPath);
                }
                catch (Exception rollbackException) { rollbackErrors.Add(rollbackException.Message); }
            }
            var detail = rollbackErrors.Count == 0
                ? $"操作未完成，已尝试恢复原文件名。\n{ex.Message}"
                : $"操作未完成，部分文件恢复失败，请检查文件夹中的 .foldernumberer-*.tmp 文件。\n{ex.Message}\n{string.Join("\n", rollbackErrors)}";
            MessageBox.Show(this, detail, "重命名失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            LoadFolder(_loadedFolder);
        }
    }

    private static string UndoJournalPath(string folder) => Path.Combine(folder, ".foldernumberer-undo.json");

    private static void SaveUndoJournal(string folder, IReadOnlyCollection<FilePlan> changes)
    {
        var entries = changes.Select(x => new UndoEntry(x.OriginalName, x.TargetName)).ToArray();
        var path = UndoJournalPath(folder);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(entries));
        File.Move(temp, path, true);
    }

    private void UndoLastBatch()
    {
        if (_loadedFolder is null) return;
        var journalPath = UndoJournalPath(_loadedFolder);
        if (!File.Exists(journalPath)) { UpdateActions(); return; }
        try
        {
            var entries = JsonSerializer.Deserialize<UndoEntry[]>(File.ReadAllText(journalPath)) ?? [];
            if (entries.Length == 0) throw new InvalidDataException("撤回记录为空");
            foreach (var entry in entries)
            {
                if (!File.Exists(Path.Combine(_loadedFolder, entry.NewName)))
                    throw new IOException($"找不到改名后的文件：{entry.NewName}");
                if (File.Exists(Path.Combine(_loadedFolder, entry.OldName)))
                    throw new IOException($"原文件名已被占用：{entry.OldName}");
            }
            if (MessageBox.Show(this, $"撤回上次批量改名（{entries.Length} 个文件）？", "确认撤回",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;

            var staged = new List<(UndoEntry Entry, string Temp)>();
            try
            {
                foreach (var entry in entries)
                {
                    var source = Path.Combine(_loadedFolder, entry.NewName);
                    var temp = Path.Combine(_loadedFolder, $".foldernumberer-{Guid.NewGuid():N}.undo");
                    File.Move(source, temp);
                    staged.Add((entry, temp));
                }
                foreach (var item in staged) File.Move(item.Temp, Path.Combine(_loadedFolder, item.Entry.OldName));
                File.Delete(journalPath);
                LoadFolder(_loadedFolder);
                MessageBox.Show(this, $"已撤回 {entries.Length} 个文件。", "撤回完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch
            {
                foreach (var item in staged.AsEnumerable().Reverse())
                {
                    var originalTarget = Path.Combine(_loadedFolder, item.Entry.OldName);
                    if (File.Exists(originalTarget) && !File.Exists(item.Temp)) File.Move(originalTarget, item.Temp);
                    if (File.Exists(item.Temp) && !File.Exists(Path.Combine(_loadedFolder, item.Entry.NewName)))
                        File.Move(item.Temp, Path.Combine(_loadedFolder, item.Entry.NewName));
                }
                throw;
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "撤回失败：" + ex.Message, "撤回失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            UpdateActions();
        }
    }

    private static string GetBaseName(string fileName, bool stripNumberPrefix) =>
        stripNumberPrefix ? NumberPrefix.Replace(fileName, "") : fileName;

    private sealed class ExtensionOption(string extension)
    {
        public string Extension { get; } = extension;
        public override string ToString() => string.IsNullOrEmpty(Extension) ? "(无扩展名)" : Extension;
    }

    private sealed record UndoEntry(string OldName, string NewName);

    private sealed class FilePlan(string fullPath, DateTime modifiedAt, string originalName)
    {
        public string FullPath { get; } = fullPath;
        public DateTime ModifiedAt { get; } = modifiedAt;
        public string OriginalName { get; } = originalName;
        public string TargetName { get; set; } = "";
        public bool Conflict { get; set; }
        public string? TempPath { get; set; }
    }
}
