using System;

namespace Manimal.Lighthouse.Shared;

[Serializable]
public sealed class BundleEntry
{
    public string Path = ""; 
    public string Sha256 = "";
}