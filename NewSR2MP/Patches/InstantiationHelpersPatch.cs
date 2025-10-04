﻿using System.Collections;
using Il2CppMonomiPark.SlimeRancher.DataModel;
using Il2CppMonomiPark.SlimeRancher.SceneManagement;
using Il2CppMonomiPark.SlimeRancher.World;
using UnityEngine;

namespace NewSR2MP.Patches
{
    internal static class ActorSpawnHelper
    {
        internal static IEnumerator Spawn(GameObject __result, GameObject original, SceneGroup sceneGroup,
            Vector3 position, Quaternion rotation, bool nonActorOk = false,
            SlimeAppearance.AppearanceSaveSet appearance = SlimeAppearance.AppearanceSaveSet.NONE,
            SlimeAppearance.AppearanceSaveSet secondAppearance = SlimeAppearance.AppearanceSaveSet.NONE)
        {
            yield return null;

            if (!__result) yield break;

            if (isJoiningAsClient) yield break;
            try
            {
                // Only for client
                if (ClientActive())
                {
                    // Пропускаем если идет смена сцены (переход через портал)
                    if (systemContext == null || systemContext.SceneLoader == null || systemContext.SceneLoader.IsSceneLoadInProgress)
                    {
                        SRMP.Debug("Skipping actor spawn - scene transition in progress");
                        yield break;
                    }
                    
                    if (sceneContext == null || sceneContext.GameModel == null)
                    {
                        SRMP.Debug("Skipping actor spawn - sceneContext or GameModel is null (portal transition)");
                        yield break;
                    }
                    
                    Identifiable ident = null;

                    var isActor = __result.TryGetComponent<IdentifiableActor>(out var actor);
                    if (isActor) ident = actor;

                    var isGadget = __result.TryGetComponent<Gadget>(out var gadget);
                    if (isGadget) ident = gadget;

                    // Launch immediately for client - предмет сразу летит!
                    if (__result.TryGetComponent<Vacuumable>(out var vac))
                        vac.Launch(Vacuumable.LaunchSource.PLAYER);

                    Vector3 vel = Vector3.zero;
                    if (__result.TryGetComponent<Rigidbody>(out var rb))
                        vel = rb.velocity;

                    // Проверяем что текущая сцена известна
                    string currentSceneName = systemContext.SceneLoader.CurrentSceneGroup.name;
                    if (!sceneGroupsReverse.ContainsKey(currentSceneName))
                    {
                        SRMP.Debug($"Cannot spawn actor - unknown scene: {currentSceneName}");
                        yield break;
                    }

                    var packet = new ActorSpawnClientPacket()
                    {
                        ident = GetIdentID(ident.identType),
                        position = __result.transform.position, // Используем актуальную позицию после Launch
                        rotation = rotation.eulerAngles,
                        velocity = vel,
                        player = currentPlayerID,
                        scene = sceneGroupsReverse[currentSceneName]
                    };

                    MultiplayerManager.NetworkSend(packet);

                    SRMP.Debug($"Client threw {ident.identType.name} with velocity {vel.magnitude:F2} m/s from {__result.transform.position}");
                    
                    // Уничтожаем локальный актер - хост пришлет его обратно
                    DestroyActor(__result, "SR2MP.ClientActorSpawn", true);
                }
            }
            catch (Exception e)
            {
                MelonLogger.Error($"Error in loading actor (client): {e}");
            }

        }
    }

    [HarmonyPatch(typeof(InstantiationHelpers), nameof(InstantiateActor))]
    public class InstantiationHelpersInstantiateActor
    {
        public static void Postfix(GameObject __result, GameObject original, SceneGroup sceneGroup, Vector3 position,
            Quaternion rotation, bool nonActorOk = false,
            SlimeAppearance.AppearanceSaveSet appearance = SlimeAppearance.AppearanceSaveSet.NONE,
            SlimeAppearance.AppearanceSaveSet secondAppearance = SlimeAppearance.AppearanceSaveSet.NONE)
        {
            if (handlingPacket) return;
            
            // For host, execute immediately without delay
            if (ServerActive())
            {
                try
                {
                    if (!__result) return;
                    if (isJoiningAsClient) return;
                    
                    // Пропускаем если идет смена сцены (переход через портал)
                    if (sceneContext == null || sceneContext.GameModel == null)
                    {
                        SRMP.Debug("Skipping actor spawn packet - scene transition in progress");
                        return;
                    }
                    
                    if (__result.GetComponent<NetworkActor>() == null)
                    {
                        __result.AddComponent<NetworkActor>();
                        __result.AddComponent<TransformSmoother>();
                        __result.AddComponent<NetworkActorOwnerToggle>();
                    }

                    var ts = __result.GetComponent<TransformSmoother>();
                    var id = __result.GetComponent<IdentifiableActor>().GetActorId().Value;

                    if (!actors.TryAdd(id, __result.GetComponent<NetworkActor>()))
                        actors[id] = __result.GetComponent<NetworkActor>();

                    ts.interpolPeriod = ActorTimer;
                    ts.enabled = false;

                    // Launch for host - throw at distance (immediate, no delay)
                    if (__result.TryGetComponent<Vacuumable>(out var vac))
                        vac.Launch(Vacuumable.LaunchSource.PLAYER);

                    Vector3 vel = Vector3.zero;
                    if (__result.TryGetComponent<Rigidbody>(out var rb))
                        vel = rb.velocity;

                    MultiplayerManager.NetworkSend(new ActorSpawnPacket
                    {
                        rotation = rotation.ToEuler(),
                        position = position,
                        velocity = vel,
                        id = id,
                        ident = GetIdentID(__result.GetComponent<IdentifiableActor>().identType),
                        scene = sceneGroupsReverse[sceneGroup.name]
                    });
                }
                catch (Exception e)
                {
                    MelonLogger.Error($"Error in loading actor (host): {e}");
                }
            }
            else
            {
                // For client, use coroutine
                MelonCoroutines.Start(ActorSpawnHelper.Spawn(
                    __result,
                    original,
                    sceneGroup,
                    position,
                    rotation,
                    nonActorOk,
                    appearance,
                    secondAppearance));
            }
        }
    }
}