using JetBrains.Annotations;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Spt.Tables;

namespace Manimal.Lighthouse.Server;

[UsedImplicitly]
public sealed class LighthouseBtrData
{
    public BtrMapConfig Routes { get; set; } = null!;
    public ServerMapBtrsettings Spawn { get; set; } = null!;
    public Dictionary<string, string> Locales { get; set; } = null!;
}