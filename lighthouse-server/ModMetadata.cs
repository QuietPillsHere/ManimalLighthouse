using SPTarkov.Server.Core.Models.Spt.Mod;
using Manimal.Lighthouse.Shared;

namespace Manimal.Lighthouse.Server;

public record ModMetadata : IModMetadata
{
    public string ModGuid { get; init; } = ModIdentity.Guid;
    public string Name { get; init; } = ModIdentity.ServerName;
    public string Author { get; init; } = ModIdentity.Author;
    public List<string>? Contributors { get; init; }
    public SemanticVersioning.Version Version { get; init; } = new(ModIdentity.Version);
    public SemanticVersioning.Range SptVersion { get; init; } = new("~4.1.5");
    public bool HasPrepatcher { get; init; }
    public List<string>? Incompatibilities { get; init; }
    public Dictionary<string, SemanticVersioning.Range>? ModDependencies { get; init; } = new()
    {
        ["com.wtt.commonlib"] = new("~3.0.6"),
        ["com.wtt.contentbackport"] = new("~2.0.1")
    };
    public string? Url { get; init; } = string.IsNullOrEmpty(ModIdentity.SourceUrl) ? null : ModIdentity.SourceUrl;
    public string License { get; init; } = "MIT";
}



