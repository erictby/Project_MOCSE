using Mocse.Core;
using System.Text.Json.Nodes;

namespace Mocse.App;
internal static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => MessageBox.Show(e.Exception.Message, "MOCSE · 操作失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        var form = new MainForm();
        if (args.Length == 3 && args[0] == "--smoke-ui")
            form.Shown += (_, _) => form.BeginInvoke(() => form.RunSmoke(args[1], args[2]));
        else if (args.Length == 1) form.Shown += (_, _) => form.OpenPath(args[0]);
        Application.Run(form);
    }
}

internal static class Ui
{
    public static readonly Color Ink = Color.FromArgb(27, 45, 59), Teal = Color.FromArgb(13, 116, 115), Muted = Color.FromArgb(91, 111, 126);
    public static Label Label(string text, int height = 32) => new() { Text = text, UseMnemonic = false, AutoSize = false, Height = height, Dock = DockStyle.Top, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Ink };
    public static Button Button(string text, Action action, bool primary = false)
    {
        var b = new Button { Text = text, AutoSize = true, Height = 36, Padding = new Padding(10, 5, 10, 5), FlatStyle = FlatStyle.Flat, BackColor = primary ? Teal : Color.White, ForeColor = primary ? Color.White : Ink, Margin = new Padding(0, 0, 10, 0) };
        b.FlatAppearance.BorderColor = primary ? Teal : Color.FromArgb(207, 217, 225);
        b.Click += (_, _) => action(); return b;
    }
    public static DataGridView Grid() => new()
    {
        Dock = DockStyle.Fill, BackgroundColor = Color.White, BorderStyle = BorderStyle.None, RowHeadersVisible = false,
        AllowUserToAddRows = false, AllowUserToDeleteRows = false, AllowUserToResizeRows = false, AutoGenerateColumns = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false, RowTemplate = { Height = 34 },
        ColumnHeadersHeight = 40, ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
        EnableHeadersVisualStyles = false, ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle { BackColor = Color.FromArgb(232, 239, 243), ForeColor = Ink, Font = new Font("Microsoft YaHei UI", 10, FontStyle.Bold) },
        DefaultCellStyle = new DataGridViewCellStyle { BackColor = Color.White, ForeColor = Ink, SelectionBackColor = Color.FromArgb(213, 239, 235), SelectionForeColor = Ink, Padding = new Padding(5, 0, 5, 0) },
        AlternatingRowsDefaultCellStyle = new DataGridViewCellStyle { BackColor = Color.FromArgb(247, 250, 251) }, GridColor = Color.FromArgb(230, 236, 240)
    };
    public static void Column(DataGridView grid, string key, string title, int width, bool edit = false, bool fill = false)
    {
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = key, HeaderText = title, Width = width, MinimumWidth = 60, ReadOnly = !edit, SortMode = DataGridViewColumnSortMode.NotSortable, AutoSizeMode = fill ? DataGridViewAutoSizeColumnMode.Fill : DataGridViewAutoSizeColumnMode.None,
            DefaultCellStyle = edit ? new DataGridViewCellStyle { ForeColor = Teal, BackColor = Color.FromArgb(240, 251, 247) } : new DataGridViewCellStyle() });
    }
}

public sealed class MainForm : Form
{
    private SaveDocument? doc;
    private ItemCatalog catalog = new();
    private UnitCatalog unitCatalog = new();
    private bool refreshing, smoke;
    private readonly Label fileInfo = Ui.Label("尚未打开存档 · 选择 .fcs 文件开始编辑", 55);
    private readonly Label status = Ui.Label("就绪 · 保存前自动备份，所有编辑先保留在内存中。", 46);
    private readonly Label overview = Ui.Label("支持版本 10 的战役存档。", 76);
    private readonly Label catalogStatus = Ui.Label("", 42);
    private readonly TabControl tabs = new() { Dock = DockStyle.Fill, Padding = new Point(24, 9) };
    private readonly Dictionary<string, NumericUpDown> resources = new();
    private readonly DataGridView units = Ui.Grid(), officers = Ui.Grid(), items = Ui.Grid();
    private readonly List<Button> editButtons = new();
    private readonly string configPath = Path.Combine(AppContext.BaseDirectory, "settings.json");
    private string lastBackup = "";

    public MainForm()
    {
        Text = "MOCSE · Master of Command 存档修改器"; Width = 1280; Height = 900; MinimumSize = new Size(1100, 720);
        StartPosition = FormStartPosition.CenterScreen; Font = new Font("Microsoft YaHei UI", 10); BackColor = Color.FromArgb(244, 247, 249);
        var shell = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(22, 16, 22, 12) };
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 96)); shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 48)); shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 60)); shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        Controls.Add(shell);
        var heading = new Panel { Dock = DockStyle.Fill, BackColor = Ui.Ink, Padding = new Padding(16, 6, 16, 6), Margin = new Padding(0, 0, 0, 10) };
        var title = Ui.Label("MOCSE  /  指挥官存档工作台", 42); title.Font = new Font(Font.FontFamily, 16, FontStyle.Bold); title.ForeColor = Color.White;
        var subtitle = Ui.Label("MASTER OF COMMAND     ·     中文存档修改器 v0.1", 24); subtitle.Dock = DockStyle.Bottom; subtitle.ForeColor = Color.FromArgb(184, 209, 218); subtitle.Font = new Font(Font.FontFamily, 8);
        heading.Controls.Add(title); heading.Controls.Add(subtitle); shell.Controls.Add(heading, 0, 0);
        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Padding = new Padding(0, 3, 0, 0) };
        toolbar.Controls.Add(Ui.Button("打开存档", OpenDialog, true));
        AddEdit("保存", () => Save(false)); AddEdit("另存为…", () => Save(true)); AddEdit("撤销全部修改", Restore);
        toolbar.Controls.Add(Ui.Button("游戏数据目录…", ChooseCatalog)); toolbar.Controls.Add(Ui.Button("备份目录", OpenBackups));
        shell.Controls.Add(toolbar, 0, 1); fileInfo.Dock = DockStyle.Fill; shell.Controls.Add(fileInfo, 0, 2);
        shell.Controls.Add(tabs, 0, 3); status.Dock = DockStyle.Fill; status.ForeColor = Ui.Muted; status.Font = new Font(Font.FontFamily, 9); shell.Controls.Add(status, 0, 4);
        BuildResourcePage(); BuildUnitsPage(); BuildOfficersPage(); BuildItemsPage();
        foreach (var g in new[] { units, officers, items }) ConnectEditing(g);
        foreach (var b in editButtons) b.Enabled = false;
        LoadCatalogAutomatically();
        FormClosing += (_, e) => { if (!smoke && !CanLeave()) e.Cancel = true; };
        KeyPreview = true;
        KeyDown += (_, e) => { if (e.Control && e.KeyCode == Keys.S) { Save(e.Shift); e.SuppressKeyPress = true; } else if (e.Control && e.KeyCode == Keys.O) { OpenDialog(); e.SuppressKeyPress = true; } };
        void AddEdit(string text, Action action) { var b = Ui.Button(text, action); editButtons.Add(b); toolbar.Controls.Add(b); }
    }
    private TabPage Page(string title, string hint, Control body)
    {
        var page = new TabPage(title) { BackColor = Color.White, Padding = new Padding(16) };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 56)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var label = Ui.Label(hint, 56); label.Dock = DockStyle.Fill; label.ForeColor = Ui.Muted; label.Font = new Font(Font.FontFamily, 9);
        layout.Controls.Add(label, 0, 0); layout.Controls.Add(body, 0, 1); page.Controls.Add(layout); tabs.TabPages.Add(page); return page;
    }
    private void BuildResourcePage()
    {
        var body = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        var cards = new TableLayoutPanel { Dock = DockStyle.Top, Height = 145, ColumnCount = 4 };
        foreach (var (key, name) in new[] { ("Cash", "资金"), ("Food", "粮食"), ("Ammo", "弹药"), ("Manpower", "人力储备") })
        {
            cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
            var card = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(239, 247, 247), Padding = new Padding(18), Margin = new Padding(0, 0, 12, 12) };
            var nameLabel = Ui.Label(name, 28); nameLabel.Font = new Font(Font.FontFamily, 11, FontStyle.Bold);
            var value = new NumericUpDown { Dock = DockStyle.Bottom, Maximum = int.MaxValue, Minimum = 0, ThousandsSeparator = true, Font = new Font(Font.FontFamily, 18), Enabled = false, Height = 44 };
            value.ValueChanged += (_, _) => { if (!refreshing && doc != null) { JsonData.SetInt(doc.Player, key, (int)value.Value); Dirty(); } };
            card.Controls.Add(nameLabel); card.Controls.Add(value); cards.Controls.Add(card); resources[key] = value;
        }
        overview.Dock = DockStyle.Top; overview.Padding = new Padding(4, 14, 0, 0);
        var notes = Ui.Label("使用步骤\r\n打开存档 → 修改绿色数值单元格 → 保存或另存为\r\n\r\n自动备份位于存档旁的 _MOCSE_Backups 文件夹。\r\n请在游戏退出后编辑，并在游戏中选择本地存档读取。Steam 云端存档不会由本工具直接修改。", 170);
        notes.Dock = DockStyle.Top; notes.ForeColor = Ui.Muted;
        body.Controls.Add(notes); body.Controls.Add(overview); body.Controls.Add(cards);
        Page("资源", "修改玩家战役资源。这里的人力是储备池，单支部队的兵力在「部队」页编辑。", body);
    }
    private void BuildUnitsPage()
    {
        Ui.Column(units, "Location", "所属编队", 130); Ui.Column(units, "Name", "部队名称", 150, fill: true);
        Ui.Column(units, "Manpower", "当前兵力", 108, true); Ui.Column(units, "TargetManpower", "目标兵力", 108, true); Ui.Column(units, "MaxManpower", "基础上限", 108, true);
        Ui.Column(units, "CurrentExperience", "当前经验", 103, true); Ui.Column(units, "CurrentLevel", "等级", 70);
        Ui.Column(units, "UnitName", "兵种名称", 150); Ui.Column(units, "UnitEnglishName", "英文名称", 150); Ui.Column(units, "UnitID", "兵种 ID", 170);
        Page("部队", "双击绿色数值编辑；包含现役、预备队及将军卫队。经验为当前等级内的经验，不自动换算等级。\r\n目标兵力低于当前兵力可能触发游戏裁员；实际兵力上限还受游戏修正影响。", units);
    }
    private void BuildOfficersPage()
    {
        Ui.Column(officers, "Location", "所属编队", 180); Ui.Column(officers, "Name", "军官姓名", 240, fill: true);
        Ui.Column(officers, "Level", "等级", 100); Ui.Column(officers, "Experience", "经验", 100); Ui.Column(officers, "SkillPointsAvailable", "可用技能点", 150, true); Ui.Column(officers, "Skills", "已有技能数", 120);
        Page("军官", "编辑可用技能点，保留已有技能、军官等级及外观。包含编队军官和预备军官。", officers);
    }
    private void BuildItemsPage()
    {
        Ui.Column(items, "Location", "位置", 220); Ui.Column(items, "Name", "物品名称", 240, fill: true); Ui.Column(items, "EnglishName", "英文名称", 200); Ui.Column(items, "ID", "物品 ID", 220); Ui.Column(items, "Amount", "数量", 100, true);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 45)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var bar = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        var add = Ui.Button("＋ 新增物品到营地", AddItem, true); editButtons.Add(add); bar.Controls.Add(add);
        catalogStatus.Dock = DockStyle.Fill; catalogStatus.Font = new Font(Font.FontFamily, 9); catalogStatus.ForeColor = Ui.Muted;
        layout.Controls.Add(bar, 0, 0); layout.Controls.Add(catalogStatus, 0, 1); layout.Controls.Add(items, 0, 2);
        Page("物品", "可修改营地库存及部队已装备物品的数量。新增物品仅放入营地库存，优先使用空槽。\r\n在新增窗口中点击「添加」即可加入营地，重复点击可添加多份；每槽数量对应存档 Amount，可直接在表格中修改。", layout);
    }
    private void ConnectEditing(DataGridView grid)
    {
        grid.CellValidating += (_, e) =>
        {
            if (refreshing || e.RowIndex < 0 || grid.Columns[e.ColumnIndex].ReadOnly || grid.Rows[e.RowIndex].Cells[e.ColumnIndex].ReadOnly) return;
            bool ok = int.TryParse(Convert.ToString(e.FormattedValue), out int n) && n >= (grid.Columns[e.ColumnIndex].Name == "Amount" ? 1 : 0);
            e.Cancel = !ok; grid.Rows[e.RowIndex].ErrorText = ok ? "" : "请输入有效整数，数量至少为 1，其他数值至少为 0。";
            if (!ok) status.Text = grid.Rows[e.RowIndex].ErrorText;
        };
        grid.CellValueChanged += (_, e) =>
        {
            if (refreshing || e.RowIndex < 0 || e.ColumnIndex < 0 || grid.Columns[e.ColumnIndex].ReadOnly) return;
            var row = grid.Rows[e.RowIndex]; string key = grid.Columns[e.ColumnIndex].Name;
            if (row.Tag is not JsonObject obj || !int.TryParse(Convert.ToString(row.Cells[e.ColumnIndex].Value), out int n)) return;
            try { JsonData.SetInt(obj, key, n); row.ErrorText = ""; Dirty(); }
            catch (Exception ex) { status.Text = ex.Message; }
        };
        grid.CellEndEdit += (_, e) => { if (e.RowIndex >= 0) grid.Rows[e.RowIndex].ErrorText = ""; };
        grid.DataError += (_, e) => { e.ThrowException = false; status.Text = "输入格式无效，请输入整数。"; };
    }
    private bool Commit()
    {
        foreach (var grid in new[] { units, officers, items })
            if (!grid.EndEdit() || grid.Rows.Cast<DataGridViewRow>().Any(r => r.ErrorText.Length > 0)) return false;
        foreach (var control in resources.Values) { _ = control.Value; }
        return ValidateChildren();
    }
    private void Dirty()
    {
        if (doc == null) return;
        Text = $"{(doc.IsDirty ? "● " : "")}{Path.GetFileName(doc.SourcePath)} — MOCSE 存档修改器";
        status.Text = doc.IsDirty ? "有未保存修改 · Ctrl+S 保存，Ctrl+Shift+S 另存为。" : "没有未保存修改。";
    }
    private bool CanLeave()
    {
        if (!Commit()) return false;
        if (doc?.IsDirty != true) return true;
        var result = MessageBox.Show(this, "当前存档有未保存的修改，是否保存？", "未保存修改", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
        return result == DialogResult.No || result == DialogResult.Yes && Save(false);
    }
    private void OpenDialog()
    {
        if (!CanLeave()) return;
        using var dialog = new OpenFileDialog { Filter = "战役存档 (*.fcs)|*.fcs", InitialDirectory = doc == null ? SaveFolder() : Path.GetDirectoryName(doc.SourcePath), Title = "打开 Master of Command 存档" };
        if (dialog.ShowDialog(this) == DialogResult.OK) OpenPath(dialog.FileName);
    }
    public void OpenPath(string path)
    {
        try { var loaded = SaveDocument.Open(path); doc = loaded; RefreshAll(); status.Text = "存档已打开 · 仅支持版本 10；源文件尚未修改。"; }
        catch (Exception ex) { Error(ex); }
    }
    private void RefreshAll()
    {
        if (doc == null) return;
        refreshing = true;
        try
        {
            foreach (var (key, control) in resources) { control.Minimum = Math.Min(0, JsonData.Int(doc.Player, key)); control.Value = JsonData.Int(doc.Player, key); control.Enabled = true; }
            foreach (var b in editButtons) b.Enabled = true;
            fileInfo.Text = $"{JsonData.Text(doc.Root, "Name")}    ·    国家 {JsonData.Text(doc.Root, "PlayerNationTag")}    ·    存档版本 {doc.Version}\r\n{doc.SourcePath}";
            var regiments = doc.Regiments(); var officerList = doc.Officers();
            overview.Text = $"当前战役    {regiments.Count} 支部队    /    {officerList.Count} 位军官    /    {doc.Items().Count(i => i.IsCamp)} 个营地物品槽\r\n绿色单元格可编辑，其他数据将完整保留。";
            units.Rows.Clear();
            foreach (var unit in regiments)
            {
                var o = unit.Data; string unitId = JsonData.Text(o,"UnitID");
                int row = units.Rows.Add(unit.Location, unit.Name, Value(o,"Manpower"), Value(o,"TargetManpower"), Value(o,"MaxManpower"), Value(o,"CurrentExperience"), Value(o,"CurrentLevel"), unitCatalog.NameOf(unitId), unitCatalog.EnglishNameOf(unitId), unitId);
                BindRow(units.Rows[row], o);
            }
            officers.Rows.Clear();
            foreach (var officer in officerList)
            {
                var o = officer.Data; int row = officers.Rows.Add(officer.Location, (officer.Name + " " + JsonData.Text(o,"LastName")).Trim(), Value(o,"Level"), Value(o,"Experience"), Value(o,"SkillPointsAvailable"), JsonData.Array(o["SkillSaves"])?.Count ?? 0);
                BindRow(officers.Rows[row], o);
            }
            RefreshItems();
        }
        finally { refreshing = false; }
        Dirty();
    }
    private static object Value(JsonObject o, string key) => o[key] is JsonValue v && v.TryGetValue<int>(out int n) ? n : "—";
    private static void BindRow(DataGridViewRow row, JsonObject obj)
    {
        row.Tag = obj;
        foreach (DataGridViewCell cell in row.Cells)
            if (!cell.OwningColumn.ReadOnly && (obj[cell.OwningColumn.Name] is not JsonValue v || !v.TryGetValue<int>(out _))) cell.ReadOnly = true;
    }
    private void RefreshItems()
    {
        if (!items.EndEdit()) return;
        bool prior = refreshing; refreshing = true;
        try
        {
            items.Rows.Clear(); if (doc == null) return;
            foreach (var item in doc.Items())
            {
                int row = items.Rows.Add(item.Location, catalog.NameOf(item.Id), catalog.EnglishNameOf(item.Id), item.Id, Value(item.Item, "Amount")); BindRow(items.Rows[row], item.Item);
            }
        }
        finally { refreshing = prior; }
    }
    private bool Save(bool saveAs)
    {
        if (doc == null || !Commit()) return false;
        string path = doc.SourcePath;
        if (saveAs)
        {
            using var dialog = new SaveFileDialog { Filter = "战役存档 (*.fcs)|*.fcs", DefaultExt = "fcs", AddExtension = true, OverwritePrompt = true, InitialDirectory = Path.GetDirectoryName(path), FileName = Path.GetFileNameWithoutExtension(path) + "_修改.fcs", Title = "另存为新存档" };
            if (dialog.ShowDialog(this) != DialogResult.OK) return false;
            path = dialog.FileName;
        }
        try
        {
            var result = doc.Save(path); lastBackup = Path.GetDirectoryName(result.Backups[^1])!; RefreshAll();
            status.Text = $"已保存 {Path.GetFileName(path)} · 自动备份 {result.Backups.Count} 份；点击「备份目录」查看。"; return true;
        }
        catch (Exception ex) { Error(ex); return false; }
    }
    private void Restore()
    {
        if (doc == null) return;
        if (MessageBox.Show(this, "撤销本次尚未保存的全部修改？", "撤销修改", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        foreach (var g in new[] { units, officers, items }) { g.CancelEdit(); foreach (DataGridViewRow row in g.Rows) row.ErrorText = ""; }
        doc.Restore(); RefreshAll();
    }
    private void AddItem()
    {
        if (doc == null || !Commit()) return;
        if (catalog.Items.Count == 0) { ChooseCatalog(); if (catalog.Items.Count == 0) return; }
        string lastAdded = "";
        using var dialog = new AddItemForm(catalog, item => { doc.AddItem(item.Id, 1, catalog, 1); lastAdded = item.Name; });
        dialog.ShowDialog(this);
        if (dialog.AddedTotal == 0) return;
        RefreshAll(); tabs.SelectedIndex = 3; status.Text = $"已加入营地 {dialog.AddedTotal} 件物品（最新：{lastAdded}）。尚未保存。";
    }
    private void ChooseCatalog()
    {
        using var dialog = new FolderBrowserDialog { Description = "选择 Master of Command 安装目录或 GameData 目录", UseDescriptionForTitle = true };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            SetCatalog(ItemCatalog.Load(dialog.SelectedPath));
            try { File.WriteAllText(configPath, new JsonObject { ["GameData"] = catalog.SourceDirectory }.ToJsonString()); } catch { status.Text = "物品目录已加载，但无法保存目录设置。"; }
        }
        catch (Exception ex) { Error(ex); }
    }
    private void SetCatalog(ItemCatalog value)
    {
        catalog = value; unitCatalog = UnitCatalog.Load(value.SourceDirectory); catalogStatus.Text = $"已加载 {catalog.Items.Count} 种物品 · 使用本机 GameData 与中文名称；可通过上方按钮切换数据目录。"; RefreshItems();
    }
    private void LoadCatalogAutomatically()
    {
        var candidates = new List<string>();
        try { if (File.Exists(configPath) && JsonNode.Parse(File.ReadAllText(configPath))?["GameData"]?.GetValue<string>() is string path) candidates.Add(path); } catch { }
        candidates.Add(@"D:\SteamLibrary\steamapps\common\Master of Command");
        candidates.Add(@"C:\Program Files (x86)\Steam\steamapps\common\Master of Command");
        foreach (string p in candidates) { try { SetCatalog(ItemCatalog.Load(p)); return; } catch { } }
        catalogStatus.Text = "尚未加载物品目录：点击「游戏数据目录…」后可搜索和新增物品。";
    }
    private void OpenBackups()
    {
        string path = lastBackup.Length > 0 ? lastBackup : doc != null ? Path.Combine(Path.GetDirectoryName(doc.SourcePath)!, "_MOCSE_Backups") : "";
        if (!Directory.Exists(path)) { status.Text = "尚未生成备份。首次保存时会自动创建备份目录。"; return; }
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", path) { UseShellExecute = true });
    }
    private void Error(Exception ex) => MessageBox.Show(this, ex.Message, "MOCSE · 操作未完成", MessageBoxButtons.OK, MessageBoxIcon.Error);
    private static string SaveFolder() => Path.GetFullPath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "..", "LocalLow", "Armchair Interactive", "Master Of Command", "Campaigns"));
    public void RunSmoke(string fixture, string output)
    {
        smoke = true;
        try
        {
            Directory.CreateDirectory(output); doc = SaveDocument.Open(fixture); RefreshAll();
            resources["Cash"].Value = 123456;
            tabs.SelectedIndex = 1; units.CurrentCell = units.Rows[0].Cells["Manpower"]; units.BeginEdit(true);
            ((TextBox)units.EditingControl!).Text = "321";
            if (!units.EndEdit() || JsonData.Int(doc.Regiments()[0].Data,"Manpower") != 321) throw new Exception("UI unit edit failed");
            tabs.SelectedIndex = 2; officers.CurrentCell = officers.Rows[0].Cells["SkillPointsAvailable"]; officers.BeginEdit(true); ((TextBox)officers.EditingControl!).Text = "12"; officers.EndEdit();
            if (JsonData.Int(doc.Officers()[0].Data,"SkillPointsAvailable") != 12) throw new Exception("UI officer edit failed");
            if (catalog.Items.Count == 0) throw new Exception("Missing item catalog");
            doc.AddItem(catalog.Items.Keys.First(), 3, catalog, 2); RefreshAll();
            tabs.SelectedIndex = 3; items.CurrentCell = items.Rows[0].Cells["Amount"]; items.BeginEdit(true); ((TextBox)items.EditingControl!).Text = "7"; items.EndEdit();
            if (JsonData.Int(doc.Items()[0].Item,"Amount") != 7) throw new Exception("UI amount edit failed");
            for (int i = 0; i < tabs.TabCount; i++) { tabs.SelectedIndex = i; Refresh(); using var b = new Bitmap(Width, Height); DrawToBitmap(b, new Rectangle(0, 0, Width, Height)); b.Save(Path.Combine(output, $"ui-{i}.png")); }
            using (var add = new AddItemForm(catalog, item => doc.AddItem(item.Id, 1, catalog, 1))) { add.Show(this); add.Refresh(); using (var b = new Bitmap(add.Width, add.Height)) { add.DrawToBitmap(b, new Rectangle(0, 0, add.Width, add.Height)); b.Save(Path.Combine(output,"ui-add.png")); } add.SmokeFilter("Bayonet", true, false, false); add.Refresh(); using (var b = new Bitmap(add.Width, add.Height)) { add.DrawToBitmap(b, new Rectangle(0, 0, add.Width, add.Height)); b.Save(Path.Combine(output,"ui-add-filtered.png")); } add.Close(); }
            var result = doc.Save(Path.Combine(output,"ui-edited.fcs"));
            if (SaveDocument.Open(result.Path).IsDirty) throw new Exception("Saved document should be clean");
            File.WriteAllText(Path.Combine(output,"ui-smoke.txt"), "PASS: resources, unit and officer grid editing, item addition/quantity, four pages, catalog dialog, save and reopen.");
        }
        catch (Exception ex) { File.WriteAllText(Path.Combine(output,"ui-smoke.txt"), ex.ToString()); Environment.ExitCode = 1; }
        Close();
    }
}



