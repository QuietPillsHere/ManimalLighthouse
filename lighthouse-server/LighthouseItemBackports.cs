using System.Text.Json;
using System.Text.Json.Serialization;
using Manimal.Lighthouse.Shared;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Models.Spt.Mod;
using SPTarkov.Server.Core.Utils;
using WTTServerCommonLib.Models;
using Path = System.IO.Path;

namespace Manimal.Lighthouse.Server;

// SPT calls Module.GetTypes before it loads other mod assemblies. A CommonLib
// base class here prevents SPT from discovering even our dependency metadata.
// Keep the disk model independent; use CommonLib validation after DI has loaded it.
public sealed class LighthouseCustomItem
{
    [JsonPropertyName("itemTplToClone")]
    public required string ItemTplToClone { get; set; }
    [JsonPropertyName("newName")]
    public string? NewName { get; set; }
    [JsonPropertyName("parentId")]
    public required string ParentId { get; set; }
    [JsonPropertyName("overrideProperties")]
    public required TemplateItemProperties OverrideProperties { get; set; }
    [JsonPropertyName("locales")]
    public required Dictionary<string, LocaleDetails> Locales { get; set; }
    [JsonPropertyName("registerInHandbook")]
    public bool? RegisterInHandbook { get; set; }
    [JsonPropertyName("registerInFleaPrices")]
    public bool? RegisterInFleaPrices { get; set; }
    [JsonPropertyName("handbookParentId")]
    public string? HandbookParentId { get; set; }
    [JsonPropertyName("handbookPriceRoubles")]
    public int? HandbookPriceRoubles { get; set; }
    [JsonPropertyName("fleaPriceRoubles")]
    public int? FleaPriceRoubles { get; set; }
    [JsonPropertyName("requiredTemplates")]
    public List<MongoId> RequiredTemplates { get; set; } = [];

    public IEnumerable<string> GetValidationErrors(string id) => new CustomItemConfig
    {
        ItemTplToClone = ItemTplToClone, NewName = NewName, ParentId = ParentId,
        OverrideProperties = OverrideProperties, Locales = Locales,
        RegisterInHandbook = RegisterInHandbook, RegisterInFleaPrices = RegisterInFleaPrices,
        HandbookParentId = HandbookParentId, HandbookPriceRoubles = HandbookPriceRoubles,
        FleaPriceRoubles = FleaPriceRoubles
    }.GetValidationErrors(id);
}

public static class LighthouseItemRegistration
{
    public const string RelativePath = "db/CustomItems/LighthouseItems.json";

    public static Dictionary<string, LighthouseCustomItem> SelectMissing(
        Dictionary<string, LighthouseCustomItem> definitions, Dictionary<MongoId, TemplateItem> items)
    {
        var missing = new Dictionary<string, LighthouseCustomItem>();
        foreach (var pair in definitions)
        {
            // ContentBackport owns every definition it has already registered.
            if (items.ContainsKey(new MongoId(pair.Key))) continue;
            foreach (var error in pair.Value.GetValidationErrors(pair.Key))
                throw new InvalidDataException($"Invalid Lighthouse item {pair.Key}: {error}");
            if (!items.ContainsKey(new MongoId(pair.Value.ParentId)))
                throw new InvalidDataException($"Missing Lighthouse item parent {pair.Value.ParentId}.");
            if (!items.ContainsKey(new MongoId(pair.Value.ItemTplToClone)))
                throw new InvalidDataException($"Missing Lighthouse clone source {pair.Value.ItemTplToClone}.");
            foreach (var dependency in pair.Value.RequiredTemplates)
                if (!items.ContainsKey(dependency))
                    throw new InvalidDataException($"Missing Lighthouse item dependency {dependency}.");
            missing.Add(pair.Key, pair.Value);
        }
        return missing;
    }

    public static async Task RegisterAsync(string root, Dictionary<string, LighthouseCustomItem> missing,
        TemplateTable templates, JsonUtil json, Func<string, Task> createCustomItems)
    {
        if (missing.Count == 0) return;
        // CommonLib's public API takes a directory, not an in-memory batch. Feed it
        // a private, short-lived selection so future ContentBackport updates do not
        // cause duplicate-ID errors or modify provider-owned ancillary data.
        var relative = ".commonlib-items-" + Guid.NewGuid().ToString("N");
        var directory = Path.Combine(root, relative);
        Directory.CreateDirectory(directory);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(directory, "LighthouseItems.json"), json.Serialize(missing));
            await createCustomItems(relative);
            // CommonLib logs and catches item creation errors. Do not activate the
            // map unless all requested templates and handbook entries exist.
            foreach (var pair in missing)
            {
                var id = new MongoId(pair.Key);
                if (!templates.Items.ContainsKey(id))
                    throw new InvalidDataException($"CommonLib did not register Lighthouse item {id}.");
                if (pair.Value.RegisterInHandbook != true) continue;
                var found = false;
                foreach (var entry in templates.Handbook.Items)
                    if (entry.Id == id) { found = true; break; }
                if (!found) throw new InvalidDataException($"CommonLib did not register Lighthouse handbook entry {id}.");
            }
        }
        finally { Directory.Delete(directory, true); }
    }
}

// ContentBackport registers at Preload + 2; map data follows us at + 4.
[Injectable(TypePriority = OnLoadOrder.Preload + 3)]
public sealed class LighthouseItemBackports(TemplateTable templates, JsonUtil json,
    WTTServerCommonLib.WTTServerCommonLib commonLib) : IOnLoad
{
    public async Task OnLoadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        LighthouseServerState.ItemsManifestHash = "";
        var assembly = typeof(LighthouseItemBackports).Assembly;
        var root = Path.GetDirectoryName(assembly.Location)!;
        var manifestPath = Path.Combine(root, ManifestRules.FileName);
        if (!File.Exists(manifestPath)) return;
        var manifest = JsonSerializer.Deserialize<ContentManifest>(File.ReadAllText(manifestPath),
            new JsonSerializerOptions { IncludeFields = true })!;
        ManifestRules.Validate(manifest);
        if (!LighthouseServerState.Allows(manifest, root)) return;
        var declared = false;
        foreach (var file in manifest.ServerFiles)
            if (file.Path == LighthouseItemRegistration.RelativePath)
            {
                ManifestRules.VerifyFile(root, file.Path, file.Sha256);
                declared = true;
                break;
            }
        if (!declared) throw new InvalidDataException("Lighthouse CommonLib items are not declared in the content manifest.");
        var definitions = json.Deserialize<Dictionary<string, LighthouseCustomItem>>(
            File.ReadAllText(Path.Combine(root, LighthouseItemRegistration.RelativePath)))
            ?? throw new InvalidDataException("Missing Lighthouse CommonLib items.");
        var missing = LighthouseItemRegistration.SelectMissing(definitions, templates.Items);
        await LighthouseItemRegistration.RegisterAsync(root, missing, templates, json,
            relative => commonLib.CustomItemServiceExtended.CreateCustomItems(assembly, relative));
        LighthouseServerState.ItemsManifestHash = ManifestRules.Hash(manifestPath);
    }
}
