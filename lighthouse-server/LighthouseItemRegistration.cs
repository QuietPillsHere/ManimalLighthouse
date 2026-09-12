using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Utils;
using Path = System.IO.Path;

namespace Manimal.Lighthouse.Server;

public static class LighthouseItemRegistration
{
    public const string RelativePath = "db/CustomItems/LighthouseItems.json";

    public static Dictionary<string, LighthouseCustomItem> SelectMissing(
        Dictionary<string, LighthouseCustomItem> definitions, 
        Dictionary<MongoId, TemplateItem> items)
    {
        var missing = new Dictionary<string, LighthouseCustomItem>();

        foreach (var pair in definitions)
        {
            if (items.ContainsKey(new MongoId(pair.Key)))
            {
                continue;
            }

            foreach (var error in pair.Value.GetValidationErrors(pair.Key))
            {
                throw new InvalidDataException($"Invalid Lighthouse item {pair.Key}: {error}");
            }

            if (!items.ContainsKey(new MongoId(pair.Value.ParentId)))
            {
                throw new InvalidDataException($"Missing Lighthouse item parent {pair.Value.ParentId}.");
            }

            if (!items.ContainsKey(new MongoId(pair.Value.ItemTplToClone)))
            {
                throw new InvalidDataException($"Missing Lighthouse clone source {pair.Value.ItemTplToClone}.");
            }

            foreach (var dependency in pair.Value.RequiredTemplates)
            {
                if (!items.ContainsKey(dependency))
                {
                    throw new InvalidDataException($"Missing Lighthouse item dependency {dependency}.");
                }
            }

            missing.Add(pair.Key, pair.Value);
        }

        return missing;
    }

    public static async Task RegisterAsync(string root, Dictionary<string, LighthouseCustomItem> missing,
        TemplateTable templates, JsonUtil json, Func<string, Task> createCustomItems)
    {
        if (missing.Count == 0)
        {
            return;
        }
        
        var relative = ".commonlib-items-" + Guid.NewGuid().ToString("N");
        var directory = Path.Combine(root, relative);

        Directory.CreateDirectory(directory);

        try
        {
            await File.WriteAllTextAsync(Path.Combine(directory, "LighthouseItems.json"), json.Serialize(missing));
            await createCustomItems(relative);

            foreach (var pair in missing)
            {
                var id = new MongoId(pair.Key);

                if (!templates.Items.ContainsKey(id))
                {
                    throw new InvalidDataException($"CommonLib did not register Lighthouse item {id}.");
                }

                if (pair.Value.RegisterInHandbook != true) { continue; }

                var found = false;

                foreach (var entry in templates.Handbook.Items)
                {
                    if (entry.Id != id) { continue; }

                    found = true;
                    break;
                }

                if (!found)
                {
                    throw new InvalidDataException($"CommonLib did not register Lighthouse handbook entry {id}.");
                }
            }
        }
        finally { Directory.Delete(directory, true); }
    }
}