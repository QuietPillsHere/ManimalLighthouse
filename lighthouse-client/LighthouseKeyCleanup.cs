using EFT;
using HarmonyLib;

namespace Manimal.Lighthouse.Client;

internal static class LighthouseKeyCleanup
{
    internal static void Install(Harmony harmony) => harmony.Patch(
        AccessTools.Method(typeof(DoorInteractState), nameof(DoorInteractState.Exit)),
        prefix: new HarmonyMethod(typeof(LighthouseKeyCleanup), nameof(BeforeExit)));

    internal static void BeforeExit(DoorInteractState __instance)
    {
        // Exit clears Door, so run before the original method. HandAway.OnExit
        // normally clears the visual, but leaving the parent movement state
        // can bypass that substate callback. ClearHands is already null-safe
        // when the normal animation path returned the key to its pool.
        var door = __instance.Door;
        if (!door || !__instance._spawned || !LighthouseSceneLoader.Owns(door.gameObject.scene.name)) return;
        __instance.MovementContext.RemoveKeyFromHand();
        __instance._spawned = false;
    }
}
