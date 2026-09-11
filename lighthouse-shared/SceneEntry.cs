using System;

namespace Manimal.Lighthouse.Shared;

[Serializable]
public sealed class SceneEntry
{
    public string OriginalPath = ""; 
    public string ReplacementPath = ""; 
    public bool OnlyOffline;
}