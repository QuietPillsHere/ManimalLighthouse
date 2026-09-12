using System;
using System.Collections.Generic;

namespace Manimal.Lighthouse.Shared;

[Serializable]
public sealed class ContentManifest
{
    public int Schema = 1;
    public string ContentId = "";
    public string TargetClientBuild = "0.16.9.40743";
    public string SourceBuild = "1.1.5.47242";
    public string Mode = "development";
    public bool Ready;
    public List<BundleEntry> Bundles = [];
    public List<SceneEntry> Scenes = [];
    public List<PayloadFile> ServerFiles = [];
    public List<PayloadFile> Sidecars = [];
}
