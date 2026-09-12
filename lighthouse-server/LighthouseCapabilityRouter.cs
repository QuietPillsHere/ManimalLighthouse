using System.Text.Json;
using JetBrains.Annotations;
using Manimal.Lighthouse.Shared;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Utils;

namespace Manimal.Lighthouse.Server;

[Injectable(TypePriority = OnLoadOrder.Routers + 1), UsedImplicitly]
public sealed class LighthouseCapabilityRouter(JsonUtil jsonUtil) : StaticRouter(
    jsonUtil, 
    [new RouteAction<EmptyRequestData>("/manimal/lighthouse/capability", (_, _, _, _, _) 
        => new ValueTask<string>(ReadCapability()))])
{
    private static readonly JsonSerializerOptions Options = new() { IncludeFields = true };
    private static string ReadCapability()
    {
        var root = Path.GetDirectoryName(typeof(LighthouseCapabilityRouter).Assembly.Location)!;
        var path = Path.Combine(root, ManifestRules.FileName);
        var capability = new ServerCapability();

        if (!File.Exists(path)) { return JsonSerializer.Serialize(capability, Options); }

        var manifest = JsonSerializer.Deserialize<ContentManifest>(File.ReadAllText(path), Options)!;

        ManifestRules.Validate(manifest);

        foreach (var file in manifest.ServerFiles)
        {
            ManifestRules.VerifyFile(root, file.Path, file.Sha256);
        }

        capability.ContentId = manifest.ContentId;
        capability.ManifestSha256 = ManifestRules.Hash(path);
        capability.Mode = manifest.Mode;
        capability.Ready = manifest is { Ready: true, Mode: "probe" } 
                           && File.Exists(Path.Combine(root, "allow-probe")) 
                            || LighthouseServerState.Allows(manifest, root) 
                           && LighthouseServerState.AppliedManifestHash == capability.ManifestSha256;

        return JsonSerializer.Serialize(capability, Options);
    }
}
