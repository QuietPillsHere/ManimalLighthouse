using System.Text.Json;
using Manimal.Lighthouse.Shared;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Utils;
using SPTarkov.Server.Core.Utils.Json;
using Path = System.IO.Path;

namespace Manimal.Lighthouse.Server;

public static class LighthouseServerState
{
    public static string ItemsManifestHash { get; internal set; } = "";
    public static string AppliedManifestHash { get; internal set; } = "";
    public static bool Allows(ContentManifest manifest, string root) =>
        (manifest.Ready && manifest.Mode == "rework") ||
        (manifest.Mode == "test" && !manifest.Ready && File.Exists(Path.Combine(root, "allow-test")));
}

public static class LighthouseLocationData
{
    private static readonly string[] Names = ["base.json", "looseLoot.json", "staticLoot.json", "staticContainers.json", "staticAmmo.json", "statics.json", "allExtracts.json"];

    public static Location Read(string root, ContentManifest manifest, JsonUtil json, Dictionary<MongoId, TemplateItem> templates)
    {
        var texts = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var name in Names)
        {
            var relative = "db/locations/lighthouse/" + name;
            PayloadFile? declaration = null;
            foreach (var file in manifest.ServerFiles) if (file.Path == relative) { declaration = file; break; }
            if (declaration is null) throw new InvalidDataException("Missing Lighthouse database declaration: " + relative);
            ManifestRules.VerifyFile(root, relative, declaration.Sha256);
            var text = File.ReadAllText(ManifestRules.Resolve(root, relative));
            using var document = JsonDocument.Parse(text);
            ValidateTemplates(document.RootElement, templates);
            texts.Add(name, text);
        }
        T Parse<T>(string name) => json.Deserialize<T>(texts[name]) ?? throw new InvalidDataException("Null Lighthouse database: " + name);
        // Parse and validate every model before mutating the live map. Lazy loot
        // readers deserialize the frozen text afresh because generators mutate it.
        _ = Parse<LooseLoot>("looseLoot.json");
        _ = Parse<StaticContainerDetails>("staticContainers.json");
        var loot = Parse<Dictionary<MongoId, StaticLootDetails>>("staticLoot.json");
        foreach (var identity in loot.Keys) if (!templates.ContainsKey(identity)) throw new InvalidDataException("Unknown static container template: " + identity);
        return new Location
        {
            Base = Parse<LocationBase>("base.json"),
            LooseLoot = new LazyLoad<LooseLoot>(() => Parse<LooseLoot>("looseLoot.json"), false),
            StaticContainers = new LazyLoad<StaticContainerDetails>(() => Parse<StaticContainerDetails>("staticContainers.json"), false),
            StaticLoot = new LazyLoad<Dictionary<MongoId, StaticLootDetails>>(() => Parse<Dictionary<MongoId, StaticLootDetails>>("staticLoot.json"), false),
            StaticAmmo = Parse<Dictionary<string, IEnumerable<StaticAmmoDetails>>>("staticAmmo.json"),
            Statics = Parse<StaticContainer>("statics.json"),
            AllExtracts = Parse<IEnumerable<AllExtractsExit>>("allExtracts.json")
        };
    }

    private static void ValidateTemplates(JsonElement value, Dictionary<MongoId, TemplateItem> templates)
    {
        if (value.ValueKind == JsonValueKind.Object)
            foreach (var field in value.EnumerateObject())
            {
                if ((field.Name == "_tpl" || field.Name == "tpl") && field.Value.ValueKind == JsonValueKind.String)
                {
                    var id = new MongoId(field.Value.GetString()!);
                    if (!templates.ContainsKey(id)) throw new InvalidDataException("Missing Lighthouse loot template: " + id);
                }
                else ValidateTemplates(field.Value, templates);
            }
        else if (value.ValueKind == JsonValueKind.Array)
            foreach (var child in value.EnumerateArray()) ValidateTemplates(child, templates);
    }

    public static void Apply(Location target, Location replacement)
    {
        target.Base = replacement.Base;
        target.LooseLoot = replacement.LooseLoot;
        target.StaticLoot = replacement.StaticLoot;
        target.StaticContainers = replacement.StaticContainers;
        target.StaticAmmo = replacement.StaticAmmo;
        target.Statics = replacement.Statics;
        target.AllExtracts = replacement.AllExtracts;
    }
}

[Injectable(TypePriority = OnLoadOrder.Preload + 4)]
public sealed class LighthouseLocationBackport(LocationTable locations, TemplateTable templates, GlobalTable globals, LocaleTable locales, JsonUtil json) : IOnLoad
{
    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        LighthouseServerState.AppliedManifestHash = "";
        var root = Path.GetDirectoryName(typeof(LighthouseLocationBackport).Assembly.Location)!;
        var path = Path.Combine(root, ManifestRules.FileName);
        if (!File.Exists(path)) return Task.CompletedTask;
        var manifest = JsonSerializer.Deserialize<ContentManifest>(File.ReadAllText(path), new JsonSerializerOptions { IncludeFields = true })!;
        ManifestRules.Validate(manifest);
        if (!LighthouseServerState.Allows(manifest, root)) return Task.CompletedTask;
        var hash = ManifestRules.Hash(path);
        if (LighthouseServerState.ItemsManifestHash != hash) throw new InvalidDataException("Lighthouse item registration must complete before location activation.");
        var replacement = LighthouseLocationData.Read(root, manifest, json, templates.Items);
        var btr = LighthouseBtr.Read(json);
        cancellationToken.ThrowIfCancellationRequested();
        LighthouseBtr.Apply(globals.Configuration.BTRSettings, templates.LocationServices.BtrServerSettings, btr);
        LighthouseBtr.ApplyLocales(locales, btr);
        LighthouseLocationData.Apply(locations.Lighthouse, replacement);
        LighthouseServerState.AppliedManifestHash = hash;
        return Task.CompletedTask;
    }
}
