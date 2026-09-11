using System;
using EFT.GameTriggers;
using HarmonyLib;

namespace Manimal.Lighthouse.Client;

internal static class LighthousePadlockAudio
{
    internal static void Install(Harmony harmony)
    {
        var play = AccessTools.Method(typeof(HandlerPlaySoundAdvanced), nameof(HandlerPlaySoundAdvanced.PlaySound),
            new[] { typeof(HandlerPlaySoundAdvanced.PlaySoundConfig) });
        harmony.Patch(play, prefix: new HarmonyMethod(typeof(LighthousePadlockAudio), nameof(BeforePlay)));
    }

    private static void BeforePlay(HandlerPlaySoundAdvanced __instance, ref HandlerPlaySoundAdvanced.PlaySoundConfig __0)
    {
        if (!LighthouseSceneLoader.Owns(__instance.gameObject.scene.name)) return;
        var trigger = __instance.GetComponentInParent<TriggerBallistic>();
        if (!trigger || !trigger.gameObject.name.StartsWith("INTERACTIVE_Shootable_Padlock_set", StringComparison.Ordinal)
            || trigger._triggerId != __instance._playTriggerId || !trigger._targetCollider) return;

        // Serialized retail mixer assets do not belong to SPT's live audio graph.
        // Resolve the matching native bus when the hit actually plays the sound.
        var audio = MonoBehaviourSingleton<BetterAudio>.Instance;
        if (audio && audio.EnvTechnicalSoundsGroup) __0.MixerGroup = audio.EnvTechnicalSoundsGroup;
    }
}
