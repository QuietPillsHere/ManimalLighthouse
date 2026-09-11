#nullable enable
using EFT.Interactive;
#if !UNITY_EDITOR
using HarmonyLib;
#endif

namespace Manimal.Lighthouse.Client
{
    internal static class LighthouseTunnelDoor
    {
        internal const string DoorId = "door_Lighthouse_Tunnel_00001";
#if !UNITY_EDITOR
        internal static void Install(Harmony harmony) => harmony.Patch(
            AccessTools.Method(typeof(WorldInteractiveObject), nameof(WorldInteractiveObject.OnEnable)),
            prefix: new HarmonyMethod(typeof(LighthouseTunnelDoor), nameof(BeforeEnable)));

        private static void BeforeEnable(WorldInteractiveObject __instance)
        {
            if (__instance is Door door && Apply(door, LighthouseSceneLoader.Owns(door.gameObject.scene.name)))
                Plugin.Log.LogInfo("Lighthouse southern tunnel exit door: normal opening enabled from both sides.");
        }
#endif
        internal static bool Apply(Door door, bool ownsScene)
        {
            if (!door || !ownsScene || door.Id != DoorId) return false;
            // Set the serialized state before native OnEnable initializes the
            // angle, InitialDoorState and synchronization state. No early door
            // events are dispatched before the raid's event controller exists.
            if (door._doorState == EDoorState.Locked) door._doorState = EDoorState.Shut;
            door.Operatable = true;
            door.NoInteractionsAllowed = false;
            door.CanBeBreached = false;
            door.CanInteractWithBreach = false;
            return true;
        }
    }
}
