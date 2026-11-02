using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
namespace Mocse.Core;

public record CatalogItem(string Id, string Name, string Category, string Quality, string Type, string Description, string EnglishName, string EnglishDescription, string UnitTypeAllowed)
{
    public bool UsableBy(string unitType) => UnitTypeAllowed == "All" || UnitTypeAllowed.Contains(unitType, StringComparison.Ordinal);
    public bool Equippable => UnitTypeAllowed is not ("" or "None");
}
public sealed class ItemCatalog
{
    public Dictionary<string, CatalogItem> Items { get; } = new(StringComparer.Ordinal);
    public string SourceDirectory { get; private set; } = "";
    public static ItemCatalog Load(string selected)
    {
        string path = Resolve(selected);
        if (!Directory.Exists(Path.Combine(path, "Items"))) throw new DirectoryNotFoundException("未找到 GameData/Items。请选择游戏目录、StreamingAssets 或 GameData 目录。");
        var catalog = new ItemCatalog { SourceDirectory = path };
        var translations = LoadTranslations(path, "Chinese.json");
        var english = LoadTranslations(path, "English.json");
        var raw = new Dictionary<string, (JsonObject Data, string Category)>();
        foreach (string file in Directory.EnumerateFiles(Path.Combine(path, "Items"), "*.json", SearchOption.AllDirectories))
            foreach (var item in JsonData.Objects(JsonNode.Parse(File.ReadAllText(file))))
            {
                string id = JsonData.Text(item, "ID");
                if (!string.IsNullOrEmpty(id)) raw[id] = (item, Category(Path.GetFileNameWithoutExtension(file)));
            }
        string Field(string id, string key, HashSet<string>? seen = null)
        {
            seen ??= new();
            if (!seen.Add(id) || !raw.TryGetValue(id, out var row)) return "";
            string value = JsonData.Text(row.Data, key);
            return value.Length > 0 ? value : Field(JsonData.Text(row.Data, "BaseInheritance"), key, seen);
        }
        foreach (var (id, row) in raw)
        {
            string key = Field(id, "Name");
            string name = translations.GetValueOrDefault(key, key.Length > 0 ? key : id);
            name = Regex.Replace(name, "<[^>]+>", "");
            string descKey = Field(id, "Description");
            string Strip(string text) => Regex.Replace(text, "<[^>]+>", "").Trim();
            catalog.Items[id] = new(id, name, row.Category, Quality(Field(id, "Quality")), Field(id, "ItemType"),
                Strip(translations.GetValueOrDefault(descKey, "")), Strip(english.GetValueOrDefault(key, "")), Strip(english.GetValueOrDefault(descKey, "")), Field(id, "UnitTypeAllowed"));
        }
        if (catalog.Items.Count == 0) throw new InvalidDataException("物品目录中没有可用的 ID。");
        return catalog;
    }
    public string NameOf(string id) => Items.TryGetValue(id, out var item) ? item.Name : id;
    public string EnglishNameOf(string id) => Items.TryGetValue(id, out var item) ? item.EnglishName : "";
    internal static Dictionary<string, string> LoadTranslations(string gameDataPath, string file)
    {
        var result = new Dictionary<string, string>();
        string lang = Path.Combine(gameDataPath, "Languages", file);
        if (File.Exists(lang))
        {
            var root = JsonNode.Parse(File.ReadAllText(lang));
            foreach (var term in JsonData.Objects(root?["Terms"])) result[JsonData.Text(term, "Key")] = JsonData.Text(term, "Translation");
        }
        return result;
    }
    private static string Resolve(string p)
    {
        foreach (var c in new[] { p, Path.Combine(p, "GameData"), Path.Combine(p, "StreamingAssets", "GameData"), Path.Combine(p, "Master of Command_Data", "StreamingAssets", "GameData") })
            if (Directory.Exists(Path.Combine(c, "Items"))) return Path.GetFullPath(c);
        return p;
    }
    private static string Quality(string q) => q switch { "BASIC" => "基础", "GOOD" => "良好", "SUPERIOR" => "优良", "EXCELLENT" => "卓越", "MASTERWORK" => "大师", _ => q };
    private static string Category(string q) => q switch { "Firearms" => "火器", "MeleeWeapons" => "近战武器", "Bayonets" => "刺刀", "Cannons" => "火炮", "ArtilleryAmmos" => "炮弹", "MusketAmmos" => "枪弹", "GenericItems" => "通用装备", "Deployables" => "工事", "Grenades" => "手榴弹", _ => q };
}
