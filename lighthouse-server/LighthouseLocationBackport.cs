using System.Text.Json;
using JetBrains.Annotations;
using Manimal.Lighthouse.Shared;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Utils;
using Path = System.IO.Path;

namespace Manimal.Lighthouse.Server;

[Injectable(TypePriority = OnLoadOrder.Preload + 4), UsedImplicitly]
public sealed class LighthouseLocationBackport(LocationTable locations, TemplateTable templates, GlobalTable globals, LocaleTable locales, JsonUtil json) : IOnLoad
{
    internal static readonly JsonSerializerOptions JSONSerializerOptions = new() { IncludeFields = true };
    
    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        LighthouseServerState.AppliedManifestHash = "";

        var root = Path.GetDirectoryName(typeof(LighthouseLocationBackport).Assembly.Location)!;
        var path = Path.Combine(root, ManifestRules.FileName);

        if (!File.Exists(path))
        {
            return Task.CompletedTask;
        }

        var manifest = JsonSerializer.Deserialize<ContentManifest>(File.ReadAllText(path), JSONSerializerOptions)!;

        ManifestRules.Validate(manifest);

        if (!LighthouseServerState.Allows(manifest, root))
        {
            return Task.CompletedTask;
        }

        var hash = ManifestRules.Hash(path);

        if (LighthouseServerState.ItemsManifestHash != hash)
        {
            throw new InvalidDataException("Lighthouse item registration must complete before location activation.");
        }

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
