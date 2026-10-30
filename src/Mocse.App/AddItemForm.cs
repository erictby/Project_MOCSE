using Mocse.Core;
namespace Mocse.App;

public sealed class AddItemForm : Form
{
    private readonly ItemCatalog catalog;
    private readonly DataGridView grid = Ui.Grid();
    private readonly TextBox search = new() { Dock = DockStyle.Fill, PlaceholderText = "输入中文名称或 ID，例如：刺刀、POTZDAM" };
    private readonly ComboBox category = new() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly NumericUpDown amount = new() { Minimum = 1, Maximum = int.MaxValue, Value = 1, Width = 100, ThousandsSeparator = true };
    private readonly NumericUpDown copies = new() { Minimum = 1, Maximum = 1000, Value = 1, Width = 85 };
    private readonly Label selection = Ui.Label("请选择物品", 36);
    private readonly Button add;
    public string? SelectedId { get; private set; }
    public int Amount => (int)amount.Value;
    public int Copies => (int)copies.Value;
    public AddItemForm(ItemCatalog source)
    {
        catalog = source; Text = "新增物品到营地库存"; Width = 1080; Height = 720; MinimumSize = new Size(940, 620); StartPosition = FormStartPosition.CenterParent; Font = new Font("Microsoft YaHei UI", 10); BackColor = Color.White;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20), ColumnCount = 1, RowCount = 6 };
        foreach (int h in new[] { 46, 42 }) layout.RowStyles.Add(new RowStyle(SizeType.Absolute,h));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent,100)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute,42)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute,50)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute,50));
        Controls.Add(layout);
        var heading = Ui.Label("从游戏物品目录选择，支持中文名称和 ID 搜索", 42); heading.Font = new Font(Font.FontFamily, 13, FontStyle.Bold); layout.Controls.Add(heading,0,0);
        var filters = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 }; filters.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100)); filters.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,170)); filters.Controls.Add(search,0,0); filters.Controls.Add(category,1,0); layout.Controls.Add(filters,0,1);
        category.Items.Add("全部类别"); foreach (string s in catalog.Items.Values.Select(x=>x.Category).Distinct().Order()) category.Items.Add(s); category.SelectedIndex = 0;
        Ui.Column(grid,"Name","物品名称",310,fill:true); Ui.Column(grid,"Category","类别",100); Ui.Column(grid,"Quality","品质",100); Ui.Column(grid,"ID","物品 ID",340); grid.ReadOnly = true;
        layout.Controls.Add(grid,0,2); layout.Controls.Add(selection,0,3);
        var options = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Padding = new Padding(0,8,0,0) };
        options.Controls.Add(new Label { Text = "每槽数量", AutoSize = true, Margin = new Padding(0,5,8,0) }); options.Controls.Add(amount);
        options.Controls.Add(new Label { Text = "新增槽数", AutoSize = true, Margin = new Padding(22,5,8,0) }); options.Controls.Add(copies);
        options.Controls.Add(new Label { Text = "多件独立装备：每槽数量 1，新增槽数填件数。", AutoSize = true, ForeColor = Ui.Muted, Margin = new Padding(20,5,0,0) }); layout.Controls.Add(options,0,4);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(0,6,0,0) };
        var cancel = Ui.Button("取消",()=> { DialogResult = DialogResult.Cancel; Close(); }); add = Ui.Button("加入营地库存",Accept,true); actions.Controls.Add(add); actions.Controls.Add(cancel); layout.Controls.Add(actions,0,5);
        CancelButton = cancel; AcceptButton = add;
        search.TextChanged += (_,_)=>RefreshList(); category.SelectedIndexChanged += (_,_)=>RefreshList(); grid.SelectionChanged += (_,_)=>SelectItem(); grid.CellDoubleClick += (_,e)=> { if(e.RowIndex>=0) Accept(); };
        RefreshList();
    }
    private void RefreshList()
    {
        string q = search.Text.Trim(), cat = category.SelectedItem?.ToString() ?? "全部类别";
        grid.Rows.Clear();
        foreach(var item in catalog.Items.Values.Where(i=>(cat=="全部类别" || i.Category==cat) && (i.Name+" "+i.Id).Contains(q,StringComparison.OrdinalIgnoreCase)).OrderBy(i=>i.Category).ThenBy(i=>i.Name))
        {
            int index=grid.Rows.Add(item.Name,item.Category,item.Quality,item.Id); grid.Rows[index].Tag=item;
        }
        SelectItem();
    }
    private void SelectItem()
    {
        var item=grid.CurrentRow?.Tag as CatalogItem; SelectedId=item?.Id; add.Enabled=item!=null;
        selection.Text=item==null ? "没有匹配物品，请更换关键词。" : $"已选：{item.Name}  ·  {item.Id}";
    }
    private void Accept()
    {
        SelectItem(); if(SelectedId==null) return; DialogResult=DialogResult.OK; Close();
    }
}
