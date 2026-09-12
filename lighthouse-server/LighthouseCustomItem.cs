using System.Text.Json.Serialization;
using JetBrains.Annotations;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Spt.Mod;
using WTTServerCommonLib.Models;

namespace Manimal.Lighthouse.Server;

[UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
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
        ItemTplToClone = ItemTplToClone,
        NewName = NewName,
        ParentId = ParentId,
        OverrideProperties = OverrideProperties,
        Locales = Locales,
        RegisterInHandbook = RegisterInHandbook,
        RegisterInFleaPrices = RegisterInFleaPrices,
        HandbookParentId = HandbookParentId,
        HandbookPriceRoubles = HandbookPriceRoubles,
        FleaPriceRoubles = FleaPriceRoubles
    }.GetValidationErrors(id);
}