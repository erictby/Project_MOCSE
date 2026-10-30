using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Mocse.Core;

public record EntityRow(string Location, JsonObject Data)
{
    public string Name => JsonData.Text(Data, "Name");
}
public record ItemRow(string Location, JsonObject Slot, bool IsCamp)
{
    public JsonObject Item => (JsonObject)Slot["ItemSave"]!;
    public string Id => JsonData.Text(Item, "ID");
}
public record SaveResult(string Path, IReadOnlyList<string> Backups);

public static class JsonData
{
    public static string Text(JsonObject obj, string key) => obj[key] is JsonValue v && v.TryGetValue<string>(out var s) ? s : "";
    public static int Int(JsonObject obj, string key) => obj[key] is JsonValue v && v.TryGetValue<int>(out var n) ? n : 0;
    public static JsonArray? Array(JsonNode? node) => node as JsonArray ?? (node as JsonObject)?["$values"] as JsonArray;
    public static IEnumerable<JsonObject> Objects(JsonNode? node) => Array(node)?.OfType<JsonObject>() ?? Enumerable.Empty<JsonObject>();
    public static void SetInt(JsonObject obj, string key, int value)
    {
        if (value < 0) throw new ArgumentOutOfRangeException(key, "数值不能小于零。");
        if (obj[key] is not JsonValue old || !old.TryGetValue<int>(out _))
            throw new InvalidDataException($"字段 {key} 缺失或不是整数，无法安全编辑。");
        obj[key] = value;
    }
}

public sealed class SaveDocument
{
    private byte[] originalBytes;
    private JsonObject baseline;
    public JsonObject Root { get; private set; }
    public JsonObject Player => (JsonObject)Root["PlayerSaveData"]!;
    public JsonObject? Army => Player["ArmySaveData"] as JsonObject;
    public string SourcePath { get; private set; }
    public int Version => JsonData.Int(Root, "SaveGameVersion");
    public bool IsDirty => !JsonNode.DeepEquals(Root, baseline);
    public static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, MaxDepth = 256 };

    private SaveDocument(string path, byte[] bytes, JsonObject root)
    {
        SourcePath = path; originalBytes = bytes; Root = root; baseline = (JsonObject)root.DeepClone();
    }
    public static SaveDocument Open(string path)
    {
        path = Path.GetFullPath(path);
        var info = new FileInfo(path);
        if (info.Length > 128 * 1024 * 1024) throw new InvalidDataException("存档超过 128 MB，已停止读取。");
        var bytes = File.ReadAllBytes(path);
        string text = new UTF8Encoding(false, true).GetString(bytes).TrimStart('\uFEFF');
        var root = JsonNode.Parse(text, documentOptions: new JsonDocumentOptions { MaxDepth = 256 }) as JsonObject
            ?? throw new InvalidDataException("存档根节点必须是 JSON 对象。");
        if (root["PlayerSaveData"] is not JsonObject player || root["SaveGameVersion"] is not JsonValue)
            throw new InvalidDataException("这不是受支持的 Master of Command 战役存档。");
        if (JsonData.Int(root, "SaveGameVersion") != 10) throw new InvalidDataException("第一版仅支持存档版本 10。此存档版本为 " + JsonData.Int(root, "SaveGameVersion") + "，不会进行版本转换。");
        foreach (var key in new[] { "Cash", "Food", "Ammo", "Manpower" })
            if (player[key] is not JsonValue v || !v.TryGetValue<int>(out _))
                throw new InvalidDataException($"玩家资源字段 {key} 无效。");
        return new SaveDocument(path, bytes, root);
    }
    public List<EntityRow> Regiments()
    {
        var list = new List<EntityRow>();
        int i = 0;
        foreach (var div in JsonData.Objects(Army?["Divisions"]))
        {
            i++;
            string group = JsonData.Text(div, "Name");
            if (string.IsNullOrWhiteSpace(group)) group = $"编队 {i}";
            foreach (var unit in JsonData.Objects(div["Regiments"])) list.Add(new(group, unit));
        }
        foreach (var unit in JsonData.Objects(Army?["ReserveRegiments"])) list.Add(new("预备队", unit));
        if (Army?["GeneralRegimentSaveData"] is JsonObject general) list.Add(new("将军卫队", general));
        return list;
    }
    public List<EntityRow> Officers()
    {
        var list = new List<EntityRow>();
        foreach (var div in JsonData.Objects(Army?["Divisions"]))
            if (div["OfficerSave"] is JsonObject officer) list.Add(new(JsonData.Text(div, "Name"), officer));
        foreach (var officer in JsonData.Objects(Army?["ReserveOfficers"])) list.Add(new("预备军官", officer));
        return list;
    }
    public List<ItemRow> Items()
    {
        var list = new List<ItemRow>();
        var slots = JsonData.Array(Player["InventorySlotSaves"]);
        if (slots != null)
            for (int i = 0; i < slots.Count; i++)
                if (slots[i] is JsonObject slot && slot["ItemSave"] is JsonObject) list.Add(new($"营地库存 · {i + 1}", slot, true));
        // Visit only player regiment inventories, including legacy equipment slots; never touch AI inventories.
        foreach (var unit in Regiments())
            Visit(unit.Data["InventorySave"], $"{unit.Location} / {unit.Name}");
        return list;
        void Visit(JsonNode? node, string location)
        {
            if (node is JsonObject obj)
            {
                if (obj["ItemSave"] is JsonObject) { list.Add(new(location, obj, false)); return; }
                foreach (var pair in obj) Visit(pair.Value, location + " / " + SlotLabel(pair.Key));
            }
            else if (node is JsonArray array)
                for (int i = 0; i < array.Count; i++) Visit(array[i], location + $" {i + 1}");
        }
    }
    private static string SlotLabel(string key) => key switch
    {
        "BackpackSlot" => "背包", "BackpackSlots" => "旧背包", "OffhandSlots" => "副手", "RangedWeaponSlot" => "远程武器", "MeleeWeaponSlot" => "近战武器",
        "FlagSlot" => "旗帜", "HorseSlot" => "坐骑", "BodySlot" => "身体", "HeadSlot" => "头部", "$values" => "", _ => key
    };
    public ItemRow AddItem(string id, int amount, ItemCatalog catalog, int copies = 1)
    {
        if (!catalog.Items.ContainsKey(id)) throw new InvalidDataException("请从已加载的物品目录选择有效物品。");
        if (amount < 1 || copies < 1 || copies > 1000) throw new InvalidDataException("数量必须大于零；新增槽数范围为 1–1000。");
        var slots = JsonData.Array(Player["InventorySlotSaves"]);
        if (slots == null)
        {
            if (Player["InventorySlotSaves"] != null) throw new InvalidDataException("库存数组格式未知。");
            slots = new JsonArray(); Player["InventorySlotSaves"] = slots;
        }
        JsonObject? last = null;
        for (int n = 0; n < copies; n++)
        {
            last = slots.OfType<JsonObject>().FirstOrDefault(s => s.ContainsKey("ItemSave") && s["ItemSave"] == null);
            if (last == null)
            {
                int empty = -1;
                for (int i = 0; i < slots.Count; i++) if (slots[i] == null) { empty = i; break; }
                last = new JsonObject();
                if (empty >= 0) slots[empty] = last; else slots.Add(last);
            }
            last["ItemSave"] = new JsonObject { ["ID"] = id, ["Amount"] = amount };
        }
        return new("营地库存", last!, true);
    }
    public void Restore() => Root = (JsonObject)baseline.DeepClone();

    public SaveResult Save(string destination)
    {
        destination = Path.GetFullPath(destination);
        if (!string.Equals(Path.GetExtension(destination), ".fcs", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("请保存为 .fcs 文件。");
        if (!File.Exists(SourcePath) || !File.ReadAllBytes(SourcePath).AsSpan().SequenceEqual(originalBytes))
            throw new IOException("源存档已被游戏或其他程序修改／删除。请重新打开最新存档，避免覆盖进度。");
        bool same = string.Equals(destination, SourcePath, StringComparison.OrdinalIgnoreCase);
        var output = (JsonObject)Root.DeepClone();
        if (!same)
        {
            output["Name"] = Path.GetFileNameWithoutExtension(destination);
            if (output["IronManMode"] is JsonValue iron && iron.TryGetValue<bool>(out bool enabled) && enabled)
                output["IronManFileName"] = Path.GetFileNameWithoutExtension(destination);
        }
        output["SaveDate"] = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss.fffffff", System.Globalization.CultureInfo.InvariantCulture);
        byte[] content = Encoding.UTF8.GetBytes(output.ToJsonString(JsonOptions));
        // Reparse the exact payload before any destination mutation.
        using var check = JsonDocument.Parse(content, new JsonDocumentOptions { MaxDepth = 256 });
        string folder = Path.GetDirectoryName(destination)!;
        Directory.CreateDirectory(folder);
        var backups = new List<string>();
        backups.Add(Backup(SourcePath, originalBytes));
        byte[]? targetBytes = !same && File.Exists(destination) ? File.ReadAllBytes(destination) : null;
        if (targetBytes != null) backups.Add(Backup(destination, targetBytes));
        string temp = Path.Combine(folder, ".mocse-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { stream.Write(content); stream.Flush(true); }
            if (!File.ReadAllBytes(SourcePath).AsSpan().SequenceEqual(originalBytes)) throw new IOException("源存档发生变化，已取消写入。");
            if (same) File.Replace(temp, destination, null);
            else if (targetBytes != null)
            {
                if (!File.ReadAllBytes(destination).AsSpan().SequenceEqual(targetBytes)) throw new IOException("目标存档发生变化，已取消覆盖。");
                File.Replace(temp, destination, null);
            }
            else File.Move(temp, destination, false);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
        Root = output; baseline = (JsonObject)output.DeepClone(); originalBytes = content; SourcePath = destination;
        return new(destination, backups);
    }
    private static string Backup(string path, byte[] bytes)
    {
        string dir = Path.Combine(Path.GetDirectoryName(path)!, "_MOCSE_Backups");
        Directory.CreateDirectory(dir);
        string backup = Path.Combine(dir, Path.GetFileName(path) + "." + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + "." + Guid.NewGuid().ToString("N")[..8] + ".bak");
        using var file = new FileStream(backup, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        file.Write(bytes); file.Flush(true);
        return backup;
    }
}


