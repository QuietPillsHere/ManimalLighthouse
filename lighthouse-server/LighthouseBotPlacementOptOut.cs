using System.Reflection;
using SPTarkov.Common.Models.Logging;
using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.Models.Eft.Common;

namespace Manimal.Lighthouse.Server;

// bot placement system replaces every map's spawn tables with its own old-layout lighthouse data and
// rebuilds them after each raid. the reworked base.json is BSG's layout, so keep the mod off this map:
// drop lighthouse from its map list before its MapSpawns loader (PostLoad + 69420) runs, and hand the
// scav-raid wave adjustment back to SPT for lighthouse instead of the mod's replacement.
internal static class LighthouseBotPlacementOptOut
{
    private const string MapSpawnsTypeName = "BotPlacementSystemServer.Controllers.MapSpawns";
    private const string WavesPatchTypeName = "BotPlacementSystemServer.Patches.AdjustWavesPatch";
    private const string PmcPatchTypeName = "BotPlacementSystemServer.Patches.AdjustPmcSpawnsPatch";

    private static readonly List<AbstractPatch> Patches = [];

    [ThreadStatic]
    private static bool _adjustingLighthouse;

    public static void Apply(IServiceProvider provider, ISptLogger<LighthouseLocationBackport> logger)
    {
        var mapSpawns = FindType(MapSpawnsTypeName);

        if (mapSpawns is null)
        {
            return;
        }

        var instance = provider.GetService(mapSpawns);
        var field = mapSpawns.GetField("_validMaps", BindingFlags.NonPublic | BindingFlags.Instance);

        if (instance is null || field?.GetValue(instance) is not List<string> maps)
        {
            logger.Warning("Lighthouse: Bot Placement System map list is not the expected layout; it will still manage Lighthouse.");
            return;
        }

        maps.RemoveAll(map => string.Equals(map, "lighthouse", StringComparison.OrdinalIgnoreCase));

        if (Patches.Count == 0)
        {
            Patches.Add(new WavesPatch());
            Patches.Add(new PmcPatch());

            foreach (var patch in Patches)
            {
                patch.Enable();
            }
        }

        logger.Info("Lighthouse: Bot Placement System is disabled for Lighthouse; the reworked spawn tables are used as shipped.");
    }

    private static Type? FindType(string name)
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            var type = assembly.GetType(name, false);

            if (type is not null)
            {
                return type;
            }
        }

        return null;
    }

    private static MethodBase? FindPrefix(string typeName) =>
        FindType(typeName)?.GetMethod("Prefix", BindingFlags.Public | BindingFlags.Static);

    private sealed class WavesPatch() : AbstractPatch("com.manimal.lighthouse.bps.waves")
    {
        protected override MethodBase? GetTargetMethod() => FindPrefix(WavesPatchTypeName);

        [PatchPrefix]
        private static bool Prefix(LocationBase mapBase, ref bool __result)
        {
            _adjustingLighthouse = string.Equals(mapBase.Id, "lighthouse", StringComparison.OrdinalIgnoreCase);

            if (!_adjustingLighthouse)
            {
                return true;
            }

            __result = true;
            return false;
        }
    }

    // the mod's pmc prefix has no map argument; AdjustPMCSpawns always follows AdjustWaves on the same thread
    private sealed class PmcPatch() : AbstractPatch("com.manimal.lighthouse.bps.pmcs")
    {
        protected override MethodBase? GetTargetMethod() => FindPrefix(PmcPatchTypeName);

        [PatchPrefix]
        private static bool Prefix(ref bool __result)
        {
            if (!_adjustingLighthouse)
            {
                return true;
            }

            __result = true;
            return false;
        }
    }
}
