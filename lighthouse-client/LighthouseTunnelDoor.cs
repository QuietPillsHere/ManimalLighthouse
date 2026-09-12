using EFT.Interactive;

namespace Manimal.Lighthouse.Client;

internal static class LighthouseTunnelDoor
{
    private const string DoorId = "door_Lighthouse_Tunnel_00001";
#if !UNITY_EDITOR

    internal static void BeforeEnable(WorldInteractiveObject __instance)
    {
        if (__instance is Door door && Apply(door, LighthouseSceneLoader.Owns(door.gameObject.scene.name)))
        {
            Plugin.Log.LogInfo("Lighthouse southern tunnel exit door: normal opening enabled from both sides.");
        }
    }
#endif
    private static bool Apply(Door door, bool ownsScene)
    {
        if (!door || !ownsScene || door.Id != DoorId)
        {
            return false;
        }

        if (door._doorState == EDoorState.Locked)
        {
            door._doorState = EDoorState.Shut;
        }

        door.Operatable = true;
        door.NoInteractionsAllowed = false;
        door.CanBeBreached = false;
        door.CanInteractWithBreach = false;

        return true;
    }
}