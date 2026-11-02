using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
namespace Mocse.Core;

public record CatalogUnit(string Id, string Name, string EnglishName);
public sealed class UnitCatalog
{
    public Dictionary<string, CatalogUnit> Units { get; } = new(StringComparer.Ordinal);
    public static UnitCatalog Load(string gameDataPath)
    {
        var catalog = new UnitCatalog();
        string unitsDir = Path.Combine(gameDataPath, "Units");
        if (!Directory.Exists(unitsDir)) return catalog;
        var chinese = ItemCatalog.LoadTranslations(gameDataPath, "Chinese.json");
        var english = ItemCatalog.LoadTranslations(gameDataPath, "English.json");
        var raw = new Dictionary<string, JsonObject>();
        foreach (string file in Directory.EnumerateFiles(unitsDir, "*.json", SearchOption.AllDirectories))
            foreach (var unit in JsonData.Objects(JsonNode.Parse(File.ReadAllText(file))))
            {
                string id = JsonData.Text(unit, "ID");
                if (!string.IsNullOrEmpty(id)) raw[id] = unit;
            }
        string Field(string id, string key, HashSet<string>? seen = null)
        {
            seen ??= new();
            if (!seen.Add(id) || !raw.TryGetValue(id, out var row)) return "";
            string value = JsonData.Text(row, key);
            return value.Length > 0 ? value : Field(JsonData.Text(row, "BaseInheritance"), key, seen);
        }
        foreach (var (id, row) in raw)
        {
            string key = Field(id, "Name");
            string Strip(string text) => Regex.Replace(text, "<[^>]+>", "").Trim();
            string name = Strip(chinese.GetValueOrDefault(key, key.Contains('/') ? id : key));
            string englishName = Strip(english.GetValueOrDefault(key, key.Contains('/') ? "" : key));
            catalog.Units[id] = new(id, name, englishName);
        }
        return catalog;
    }
    public string NameOf(string id) => Units.TryGetValue(id, out var unit) ? unit.Name : id;
    public string EnglishNameOf(string id) => Units.TryGetValue(id, out var unit) ? unit.EnglishName : "";
}
