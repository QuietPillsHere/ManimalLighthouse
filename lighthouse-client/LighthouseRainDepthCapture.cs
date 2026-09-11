#nullable enable
using UnityEngine;
namespace Manimal.Lighthouse.Client
{
internal static class LighthouseRainDepthCapture
{
    internal static float[] Read(Texture source)
    {
        var previous = RenderTexture.active;
        RenderTexture? target = null;
        Texture2D? copy = null;
        try
        {
            target = RenderTexture.GetTemporary(64, 64, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            Graphics.Blit(source, target);
            RenderTexture.active = target;
            copy = new Texture2D(64, 64, TextureFormat.RGBAFloat, false, true);
            copy.ReadPixels(new Rect(0, 0, 64, 64), 0, 0);
            var pixels = copy.GetPixels();
            var depth = new float[pixels.Length];
            for (var i = 0; i < pixels.Length; i++) depth[i] = pixels[i].r;
            return depth;
        }
        finally
        {
            RenderTexture.active = previous;
            if (target) RenderTexture.ReleaseTemporary(target);
            if (copy) Object.Destroy(copy);
        }
    }
}

}
