using Mocse.Core;
namespace Mocse.App;

public sealed class AddItemForm : Form
{
    private readonly TableLayoutPanel list = new() { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1, Padding = new Padding(0, 0, 8, 0) };
    private readonly CheckBox infantry = new() { Text = "步兵可用", Checked = true, AutoSize = true, Margin = new Padding(0, 10, 18, 0) };
    private readonly CheckBox artillery = new() { Text = "炮兵可用", Checked = true, AutoSize = true, Margin = new Padding(0, 10, 18, 0) };
    private readonly CheckBox cavalry = new() { Text = "骑兵可用", Checked = true, AutoSize = true, Margin = new Padding(0, 10, 18, 0) };
    private readonly TextBox search = new() { Dock = DockStyle.Fill, PlaceholderText = "搜索中文 / 英文名称或物品 ID，例如：刺刀、Bayonet" };
    private readonly Label status;
    private readonly Label count = new() { AutoSize = true, ForeColor = Ui.Muted, Margin = new Padding(0, 12, 0, 0) };
    private readonly List<CatalogItem> items;
    private readonly Action<CatalogItem> addItem;
    private readonly Dictionary<string, int> addedCounts = new(StringComparer.Ordinal);
    private readonly Font nameFont, smallFont;
    public int AddedTotal { get; private set; }
    public AddItemForm(ItemCatalog catalog, Action<CatalogItem> add)
    {
        Text = "新增物品到营地库存"; Width = 1080; Height = 720; MinimumSize = new Size(860, 560); StartPosition = FormStartPosition.CenterParent; Font = new Font("Microsoft YaHei UI", 10); BackColor = Color.White;
        nameFont = new Font(Font.FontFamily, 10, FontStyle.Bold); smallFont = new Font(Font.FontFamily, 8.5f);
        addItem = add;
        items = catalog.Items.Values.OrderBy(x => x.Category).ThenBy(x => x.Name).ToList();
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20), ColumnCount = 1, RowCount = 5 };
        foreach (int h in new[] { 46, 42, 40 }) layout.RowStyles.Add(new RowStyle(SizeType.Absolute, h));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        Controls.Add(layout);
        var heading = Ui.Label($"游戏内全部 {catalog.Items.Count} 种物品 · 点击卡片右侧「添加」加入营地库存", 42); heading.Font = new Font(Font.FontFamily, 13, FontStyle.Bold); layout.Controls.Add(heading, 0, 0);
        var filters = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        filters.Controls.Add(infantry); filters.Controls.Add(artillery); filters.Controls.Add(cavalry); filters.Controls.Add(count);
        layout.Controls.Add(filters, 0, 1); layout.Controls.Add(search, 0, 2);
        var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Color.White, BorderStyle = BorderStyle.FixedSingle, Margin = new Padding(0, 6, 0, 6) };
        list.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        scroll.Controls.Add(list); layout.Controls.Add(scroll, 0, 3);
        status = Ui.Label("尚未添加物品。", 36); status.ForeColor = Ui.Muted; layout.Controls.Add(status, 0, 4);
        infantry.CheckedChanged += (_, _) => RefreshList(); artillery.CheckedChanged += (_, _) => RefreshList(); cavalry.CheckedChanged += (_, _) => RefreshList(); search.TextChanged += (_, _) => RefreshList();
        RefreshList();
    }
    private void RefreshList()
    {
        list.SuspendLayout();
        foreach (Control control in list.Controls) control.Dispose();
        list.Controls.Clear(); list.RowStyles.Clear();
        var shown = items.Where(Matches).ToList();
        list.RowCount = shown.Count;
        for (int i = 0; i < shown.Count; i++) { list.RowStyles.Add(new RowStyle(SizeType.Absolute, 88)); list.Controls.Add(Card(shown[i], addItem, i), 0, i); }
        count.Text = $"显示 {shown.Count} / {items.Count} 种";
        list.ResumeLayout();
    }
    private bool Matches(CatalogItem item)
    {
        if (item.Equippable && !((infantry.Checked && item.UsableBy("Infantry")) || (artillery.Checked && item.UsableBy("Artillery")) || (cavalry.Checked && item.UsableBy("Cavalry")))) return false;
        string q = search.Text.Trim();
        return q.Length == 0 || (item.Name + " " + item.EnglishName + " " + item.Id).Contains(q, StringComparison.OrdinalIgnoreCase);
    }
    private Control Card(CatalogItem item, Action<CatalogItem> add, int index)
    {
        var card = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Margin = new Padding(0, 0, 0, 1), Padding = new Padding(12, 6, 12, 6), BackColor = index % 2 == 1 ? Color.FromArgb(247, 250, 251) : Color.White };
        card.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        card.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220)); card.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); card.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        card.Controls.Add(TwoLines(item.Name, nameFont, Ui.Ink, item.EnglishName, smallFont, Ui.Muted), 0, 0);
        string cnDesc = item.Description.Length > 0 ? item.Description : "（无描述）";
        card.Controls.Add(TwoLines(cnDesc, new Font(Font.FontFamily, 9), Ui.Muted, item.EnglishDescription, smallFont, Color.FromArgb(140, 156, 168), 40), 1, 0);
        var button = Ui.Button("添加", () => Add(item, add), true); button.Margin = new Padding(0, 20, 0, 0);
        card.Controls.Add(button, 2, 0);
        return card;
    }
    private static Control TwoLines(string topText, Font topFont, Color topColor, string bottomText, Font bottomFont, Color bottomColor, int topHeight = 30)
    {
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = Padding.Empty, Padding = new Padding(0, 0, 10, 0) };
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, topHeight)); panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.Controls.Add(new Label { Text = topText, Dock = DockStyle.Fill, AutoEllipsis = true, Font = topFont, ForeColor = topColor, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
        panel.Controls.Add(new Label { Text = bottomText, Dock = DockStyle.Fill, AutoEllipsis = true, Font = bottomFont, ForeColor = bottomColor, TextAlign = ContentAlignment.TopLeft }, 0, 1);
        return panel;
    }
    private void Add(CatalogItem item, Action<CatalogItem> add)
    {
        try
        {
            add(item);
            addedCounts[item.Id] = addedCounts.GetValueOrDefault(item.Id) + 1; AddedTotal++;
            status.Text = $"已加入营地：{item.Name} ×{addedCounts[item.Id]} · 本次共添加 {AddedTotal} 件，关闭窗口后显示在物品列表中。尚未保存。";
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "MOCSE · 操作未完成", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }
    internal void SmokeFilter(string query, bool inf, bool art, bool cav)
    {
        infantry.Checked = inf; artillery.Checked = art; cavalry.Checked = cav; search.Text = query;
    }
}
