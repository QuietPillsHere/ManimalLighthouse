using Manimal.Lighthouse.Shared;

namespace Manimal.Lighthouse.Server;

public static class LighthouseServerState
{
    public static string ItemsManifestHash { get; internal set; } = "";
    public static string AppliedManifestHash { get; internal set; } = "";
    public static bool Allows(ContentManifest manifest, string root) =>
        manifest is { Ready: true, Mode: "rework" }
        || manifest is { Mode: "test", Ready: false }
        && File.Exists(Path.Combine(root, "allow-test"));
}