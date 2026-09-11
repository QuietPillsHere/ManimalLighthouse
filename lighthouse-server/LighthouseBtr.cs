using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Utils;

namespace Manimal.Lighthouse.Server;

public sealed class LighthouseBtrData
{
    public BtrMapConfig Routes { get; set; } = null!;
    public ServerMapBtrsettings Spawn { get; set; } = null!;
    public Dictionary<string, string> Locales { get; set; } = null!;
}

public static class LighthouseBtr
{
    // Routes: upstream SPT 5.0 globals. Spawn parameters: seven retail Lighthouse captures.
    // Embedded in the server DLL so settings travel with the implementation version.
    public static LighthouseBtrData Read(JsonUtil json)
    {
        using var stream = typeof(LighthouseBtr).Assembly.GetManifestResourceStream("Manimal.Lighthouse.Btr.json")
            ?? throw new InvalidDataException("Missing Lighthouse BTR configuration.");
        using var reader = new StreamReader(stream);
        var data = json.Deserialize<LighthouseBtrData>(reader.ReadToEnd())
            ?? throw new InvalidDataException("Invalid Lighthouse BTR configuration.");
        if (data.Routes?.MapID != "Lighthouse" || data.Spawn?.MapID != "Lighthouse")
            throw new InvalidDataException("BTR configuration must target Lighthouse.");
        var routes = data.Routes.PathsConfigurations?.ToArray();
        if (routes is null || routes.Length != 4)
            throw new InvalidDataException("Expected four Lighthouse BTR routes.");
        if (data.Locales is null || data.Locales.Count != 16)
            throw new InvalidDataException("Expected Lighthouse BTR taxi labels and descriptions.");
        return data;
    }

    public static void Apply(BTRSettings global, BtrServerSettings local, LighthouseBtrData data)
    {
        // Replace only Lighthouse entries, preserving native maps and other mods' entries.
        var maps = new Dictionary<string, BtrMapConfig>(global.MapsConfigs);
        maps["Lighthouse"] = data.Routes;
        var spawn = new Dictionary<string, ServerMapBtrsettings>(local.ServerMapBTRSettings);
        spawn["Lighthouse"] = data.Spawn;
        var locations = new List<string>(global.LocationsWithBTR);
        if (!locations.Contains("Lighthouse")) locations.Add("Lighthouse");
        global.MapsConfigs = maps;
        local.ServerMapBTRSettings = spawn;
        global.LocationsWithBTR = locations;
    }

    public static void ApplyLocales(LocaleTable locales, LighthouseBtrData data)
    {
        foreach (var locale in locales.Global.Values)
            locale.AddTransformer(values =>
            {
                values ??= new GlobalLocaleDictionary();
                // English fallback only where the target language lacks these new stops.
                foreach (var entry in data.Locales) values.TryAdd(entry.Key, entry.Value);
                return values;
            });
    }
}
