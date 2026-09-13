using EFT;
using Comfort.Common;
using System;
using System.Runtime.CompilerServices;

namespace Manimal.Lighthouse.Client;

internal static class LighthouseKeyCleanup
{
    private sealed class InteractionLifetime
    {
        public bool Finished;
    }

    private static readonly ConditionalWeakTable<DoorInteractState, InteractionLifetime> Lifetimes = new();

    private static bool IsLighthouse(DoorInteractState state)
    {
        if (state.Door && LighthouseSceneLoader.Owns(state.Door.gameObject.scene.name))
        {
            return true;
        }
        // Runtime-created interactables can live outside the authored scene.
        return LighthouseSceneLoader.HasReplacement && Singleton<GameWorld>.Instantiated
            && string.Equals(Singleton<GameWorld>.Instance.LocationId, "Lighthouse", StringComparison.OrdinalIgnoreCase);
    }

    internal static void AfterEnter(DoorInteractState state)
    {
        Lifetimes.Remove(state);
        if (IsLighthouse(state))
        {
            Lifetimes.Add(state, new InteractionLifetime());
        }
    }

    internal static bool BeforeBaseUpdate(DoorInteractState state) =>
        !Lifetimes.TryGetValue(state, out var lifetime) || !lifetime.Finished;

    internal static bool BeforeSpawn(MovementContext context)
    {
        if (context.CurrentState is not DoorInteractState state || !IsLighthouse(state))
        {
            return true;
        }
        if (!BeforeBaseUpdate(state))
        {
            return false;
        }
        // Player.SpawnInHands overwrites its only visual reference. Retire any
        // previous visual first so a repeated spawn cannot orphan it on the hand.
        context.RemoveKeyFromHand();
        return true;
    }

    internal static void BeforeStop(DoorInteractState state) => BeforeExit(state);

    internal static void BeforeExit(DoorInteractState __instance)
    {
        if (!Lifetimes.TryGetValue(__instance, out var lifetime))
        {
            if (!IsLighthouse(__instance))
            {
                return;
            }
            lifetime = Lifetimes.GetOrCreateValue(__instance);
        }
        lifetime.Finished = true;
        // Clear the player's actual visual even if the state flag was reset.
        // Native ClearHands is null-safe and does not consume inventory items.
        __instance.MovementContext.RemoveKeyFromHand();
        __instance._spawned = false;
    }
}
