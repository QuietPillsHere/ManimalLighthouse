using EFT;

namespace Manimal.Lighthouse.Client;

internal static class LighthouseKeyCleanup
{
    internal static void BeforeExit(DoorInteractState __instance)
    {
        var door = __instance.Door;

        if (!door || !__instance._spawned || !LighthouseSceneLoader.Owns(door.gameObject.scene.name))
        {
            return;
        }

        __instance.MovementContext.RemoveKeyFromHand();
        __instance._spawned = false;
    }
}
