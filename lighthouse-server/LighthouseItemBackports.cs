using System.Text.Json;
using JetBrains.Annotations;
using Manimal.Lighthouse.Shared;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Utils;
using Path = System.IO.Path;

namespace Manimal.Lighthouse.Server;

[Injectable(TypePriority = OnLoadOrder.Preload + 3), UsedImplicitly]
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

        if (!File.Exists(manifestPath))
        {
            return;
        }

        var manifest = JsonSerializer.Deserialize<ContentManifest>(
            await File.ReadAllTextAsync(manifestPath, cancellationToken), 
            LighthouseLocationBackport.JSONSerializerOptions)!;

        ManifestRules.Validate(manifest);

        if (!LighthouseServerState.Allows(manifest, root)) { return; }

        var declared = false;

        foreach (var file in manifest.ServerFiles)
        {
            if (file.Path != LighthouseItemRegistration.RelativePath) { continue; }

            ManifestRules.VerifyFile(root, file.Path, file.Sha256);
            declared = true;
            break;
        }

        if (!declared)
        {
            throw new InvalidDataException("Lighthouse CommonLib items are not declared in the content manifest.");
        }

        var definitions = json.Deserialize<Dictionary<string, LighthouseCustomItem>>(
                    await File.ReadAllTextAsync(Path.Combine(root, LighthouseItemRegistration.RelativePath), cancellationToken))
                    ?? throw new InvalidDataException("Missing Lighthouse CommonLib items.");
        var missing = LighthouseItemRegistration.SelectMissing(definitions, templates.Items);

        await LighthouseItemRegistration.RegisterAsync(root, missing, templates, json,
            relative => commonLib.CustomItemServiceExtended.CreateCustomItems(assembly, relative));
        LighthouseServerState.ItemsManifestHash = ManifestRules.Hash(manifestPath);
    }
}
