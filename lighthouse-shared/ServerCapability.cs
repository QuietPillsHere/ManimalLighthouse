using System;

namespace Manimal.Lighthouse.Shared;

[Serializable] 
public sealed class ServerCapability
{
    public int Schema = 1;
    public string ContentId = "";
    public string ManifestSha256 = "";
    public string Mode = "native";
    public bool Ready;
}