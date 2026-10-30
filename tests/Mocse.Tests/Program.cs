using System.Text;
using System.Text.Json.Nodes;
using Mocse.Core;

string output = args.Length > 0 ? Path.GetFullPath(args[0]) : Path.Combine(Path.GetTempPath(),"MocseTests-"+Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(output);
int count=0;
void Check(bool value,string message) { if(!value) throw new Exception("FAIL: "+message); count++; Console.WriteLine("PASS: "+message); }
void Throws(Action action,string message) { bool thrown=false; try{action();}catch{thrown=true;} Check(thrown,message); }
JsonObject Obj(string json)=>(JsonObject)JsonNode.Parse(json)!;
JsonObject Unit(string name)=>Obj("""
{"$type":"GameData.Save.PlayerRegimentSaveData, GameCore","Name":"UNIT","UnitID":"PRU_Musketeers","Manpower":100,"TargetManpower":150,"MaxManpower":150,"CurrentExperience":20,"CurrentLevel":1,"InventorySave":{"BackpackSlot":{"ItemSave":null},"OffhandSlots":[{"ItemSave":{"ID":"SOCKET_BAYONET","Amount":1,"UnknownItem":[1,2]}}],"RangedWeaponSlot":{"ItemSave":null}},"UnknownUnit":{"number":1.2345678901234567,"type":"preserve"}}
""".Replace("UNIT",name));
JsonObject Officer()=>Obj("""{"Name":"测试","LastName":"军官","Level":2,"Experience":50,"SkillPointsAvailable":3,"SkillSaves":["SteadyAim"],"Seed":4294967295} """);
var fixture=Obj("""
{"$type":"GameData.MocCampaign.CampaignSaveDataMoc, MocCampaign","SaveGameVersion":10,"Name":"测试战役","PlayerNationTag":"PRU","SaveDate":"2026-10-30T12:00:00","IronManMode":false,"IronManFileName":"ORIGINAL","PlayerSaveData":{"$type":"GameData.Save.PlayerSaveDataMoc, GameCore","Cash":1000,"Food":200,"Ammo":300,"Manpower":400,"InventorySlotSaves":[{"ItemSave":{"ID":"SOCKET_BAYONET","Amount":1,"Extra":42}},{"ItemSave":null,"SlotMarker":"keep"},null],"ArmySaveData":{"$type":"GameData.Save.PlayerPartyArmySaveData, GameCore","Divisions":[],"ReserveRegiments":[],"ReserveOfficers":[]}},"RegionSaves":{"complex":{"nodes":[null,{"$type":"SomeNode, MocCampaign","u32":4294967295,"float":0.1234567890123456789}]}},"FowSave":"AAECAwQFBg==","UnknownModData":{"$type":"Unknown, ModAssembly","keep":[1,null,"中文"]}}
""");
var army=(JsonObject)fixture["PlayerSaveData"]!["ArmySaveData"]!;
army["Divisions"]=new JsonArray(new JsonObject { ["Name"]="第 1 步兵旅",["OfficerSave"]=Officer(),["Regiments"]=new JsonArray(Unit("第 1 步兵团"),null) });
army["ReserveRegiments"]=new JsonArray(null,Unit("预备步兵")); army["ReserveOfficers"]=new JsonArray(null,Officer()); army["GeneralRegimentSaveData"]=Unit("将军卫队");
string input=Path.Combine(output,"fixture-v10.fcs"); File.WriteAllText(input,fixture.ToJsonString(SaveDocument.JsonOptions),new UTF8Encoding(true));
var original=File.ReadAllBytes(input); var doc=SaveDocument.Open(input);
Check(!doc.IsDirty,"newly opened document is clean"); Check(doc.Regiments().Count==3,"enumerates active/reserve/general regiments, skips nulls"); Check(doc.Officers().Count==2,"enumerates active and reserve officers"); Check(doc.Items().Count==4,"enumerates camp and regiment equipment");
foreach(string key in new[]{"Cash","Food","Ammo","Manpower"}) JsonData.SetInt(doc.Player,key,123456);
JsonData.SetInt(doc.Regiments()[0].Data,"Manpower",120); JsonData.SetInt(doc.Regiments()[0].Data,"TargetManpower",160); JsonData.SetInt(doc.Regiments()[0].Data,"MaxManpower",180); JsonData.SetInt(doc.Regiments()[0].Data,"CurrentExperience",600); JsonData.SetInt(doc.Officers()[1].Data,"SkillPointsAvailable",99); JsonData.SetInt(doc.Items()[0].Item,"Amount",8);
Check(doc.IsDirty,"edits mark document dirty"); Throws(()=>JsonData.SetInt(doc.Player,"Cash",-1),"rejects negative resource"); Throws(()=>JsonData.SetInt(doc.Player,"Bogus",1),"rejects absent editable fields");
var catalog=ItemCatalog.Load(@"D:\SteamLibrary\steamapps\common\Master of Command\Master of Command_Data\StreamingAssets\GameData");
Check(catalog.Items.Count>300,"loads installed item catalog"); Check(catalog.Items.Values.Any(i=>i.Name.Any(c=>c>127)),"loads Chinese item names");
string itemId=catalog.Items.Keys.First(); doc.AddItem(itemId,3,catalog,3);
var slots=JsonData.Array(doc.Player["InventorySlotSaves"])!;
Check(slots.Count==4,"new items reuse two empty slots then append one"); Check(JsonData.Text((JsonObject)slots[1]!,"SlotMarker")=="keep","preserves metadata on reused slots");
Throws(()=>doc.AddItem("INVALID_ID",1,catalog),"rejects invalid new item ID"); Throws(()=>doc.AddItem(itemId,0,catalog),"rejects zero item quantity");
string destination=Path.Combine(output,"另存测试.fcs"); var expected=(JsonObject)doc.Root.DeepClone(); var result=doc.Save(destination);
Check(File.ReadAllBytes(input).AsSpan().SequenceEqual(original),"save-as leaves source byte-for-byte unchanged"); Check(File.ReadAllBytes(result.Backups[0]).AsSpan().SequenceEqual(original),"automatic backup exactly matches original including BOM");
var read=SaveDocument.Open(destination);
expected["Name"]="另存测试"; expected["SaveDate"]=read.Root["SaveDate"]!.DeepClone();
Check(JsonNode.DeepEquals(expected,read.Root),"entire saved JSON matches intended edits and metadata only");
Check(read.Version==10 && JsonData.Text(read.Root,"Name")=="另存测试","preserves version and sets save-as name");
Check(JsonData.Int(read.Player,"Cash")==123456 && JsonData.Int(read.Player,"Ammo")==123456 && JsonData.Int(read.Player,"Food")==123456 && JsonData.Int(read.Player,"Manpower")==123456,"all four resources persist");
Check(JsonData.Int(read.Regiments()[0].Data,"Manpower")==120 && JsonData.Int(read.Regiments()[0].Data,"TargetManpower")==160 && JsonData.Int(read.Regiments()[0].Data,"MaxManpower")==180 && JsonData.Int(read.Regiments()[0].Data,"CurrentExperience")==600,"regiment manpower and experience persist");
Check(JsonData.Int(read.Officers()[1].Data,"SkillPointsAvailable")==99,"reserve officer points persist"); Check(JsonData.Int(read.Items()[0].Item,"Amount")==8,"existing item amount persists");
Check(JsonNode.DeepEquals(fixture["UnknownModData"],read.Root["UnknownModData"]) && JsonNode.DeepEquals(fixture["RegionSaves"],read.Root["RegionSaves"]) && JsonNode.DeepEquals(fixture["FowSave"],read.Root["FowSave"]),"preserves unknown types, high precision numbers, region state, and fog data");
Check(JsonNode.DeepEquals(fixture["PlayerSaveData"]!["ArmySaveData"]!["ReserveRegiments"]![1]!["UnknownUnit"],read.Regiments()[1].Data["UnknownUnit"]),"preserves untouched unit data");
var beforeOverwrite=File.ReadAllBytes(destination); JsonData.SetInt(read.Player,"Cash",777); var overwritten=read.Save(destination); Check(File.ReadAllBytes(overwritten.Backups[0]).AsSpan().SequenceEqual(beforeOverwrite),"in-place save backs up prior version");
Check(!read.IsDirty,"successful save clears dirty state"); JsonData.SetInt(read.Player,"Cash",888); read.Restore(); Check(JsonData.Int(read.Player,"Cash")==777 && !read.IsDirty,"undo restores last saved state");
string target=Path.Combine(output,"existing.fcs"); File.WriteAllText(target,"existing target content"); var replace=read.Save(target); Check(replace.Backups.Count==2 && File.ReadAllText(replace.Backups[1])=="existing target content","save-as backs up source and overwritten destination");
var conflict=SaveDocument.Open(target); File.AppendAllText(target," "); var conflicting=File.ReadAllBytes(target); Throws(()=>conflict.Save(target),"rejects externally changed source"); Check(File.ReadAllBytes(target).AsSpan().SequenceEqual(conflicting),"conflict leaves disk untouched");
var old=(JsonObject)fixture.DeepClone(); old["SaveGameVersion"]=5; string oldPath=Path.Combine(output,"old.fcs"); File.WriteAllText(oldPath,old.ToJsonString()); Throws(()=>SaveDocument.Open(oldPath),"rejects old version 5 without migration"); old["SaveGameVersion"]=11; File.WriteAllText(oldPath,old.ToJsonString()); Throws(()=>SaveDocument.Open(oldPath),"rejects unverified future version");
string malformed=Path.Combine(output,"broken.fcs"); File.WriteAllText(malformed,"{broken"); Throws(()=>SaveDocument.Open(malformed),"rejects malformed JSON");
var iron=(JsonObject)fixture.DeepClone(); iron["IronManMode"]=true; string ironPath=Path.Combine(output,"iron.fcs"); File.WriteAllText(ironPath,iron.ToJsonString()); var ironDoc=SaveDocument.Open(ironPath); ironDoc.Save(Path.Combine(output,"iron-copy.fcs")); Check(JsonData.Text(ironDoc.Root,"IronManFileName")=="iron-copy","ironman save-as points subsequent autosaves to new name");
var wrapped=(JsonObject)fixture.DeepClone(); var inv=wrapped["PlayerSaveData"]!["InventorySlotSaves"]!.DeepClone(); wrapped["PlayerSaveData"]!["InventorySlotSaves"]=new JsonObject { ["$type"]="InventorySlotSave[], GameCore",["$values"]=inv }; string wrappedPath=Path.Combine(output,"wrapped.fcs"); File.WriteAllText(wrappedPath,wrapped.ToJsonString()); var wrappedDoc=SaveDocument.Open(wrappedPath); wrappedDoc.AddItem(itemId,2,catalog); Check(wrappedDoc.Items().Count==5,"supports JSON.NET array wrappers without losing metadata");
Throws(()=>doc.Save(Path.Combine(output,"invalid.json")),"rejects invalid output extension");
Console.WriteLine($"RESULT: {count} checks passed. Synthetic version-10 fixture; game load not tested.");

