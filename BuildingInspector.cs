using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Jotunn.Configs;
using Jotunn.Entities;
using Jotunn.Managers;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BuildingInspector
{
    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]
    [BepInDependency("com.jotunn.jotunn", BepInDependency.DependencyFlags.HardDependency)]
    public partial class BuildingInspector : BaseUnityPlugin
    {
        public const string PluginGUID = "Lanoz.BuildingInspector";
        public const string PluginName = "Building Inspector";
        public const string PluginVersion = "0.3.0";

        private const string MultiplayerWorldGlobalKey = "BuildingInspector_MultiplayerWorld";

        private static ManualLogSource log;

        private void Awake()
        {
            log = Logger;
            Logger.LogInfo("Building Inspector loading...");
            InitializeConfiguration();
            new Harmony(PluginGUID).PatchAll(typeof(BuildingInspector).Assembly);
            PrefabManager.OnVanillaPrefabsAvailable += RegisterInspectionFlag;
        }












        private static bool ShouldShowInspectedBy()
        {
            ZoneSystem zoneSystem = ZoneSystem.instance;
            if (zoneSystem && zoneSystem.GetGlobalKey(MultiplayerWorldGlobalKey))
            {
                return true;
            }

            ZNet net = ZNet.instance;
            if (!net)
            {
                return false;
            }

            if (net.IsServer() && ZNet.IsOpenServer())
            {
                MarkMultiplayerWorldIfNeeded(net);
                return true;
            }

            bool connectedClient = !net.IsServer() && net.GetServerPeer() != null;
            return connectedClient && zoneSystem && zoneSystem.GetGlobalKey(MultiplayerWorldGlobalKey);
        }

        private static void MarkMultiplayerWorldIfNeeded(ZNet net)
        {
            ZoneSystem zoneSystem = ZoneSystem.instance;
            if (zoneSystem && net && net.IsServer() && ZNet.IsOpenServer() &&
                !zoneSystem.GetGlobalKey(MultiplayerWorldGlobalKey))
            {
                zoneSystem.SetGlobalKey(MultiplayerWorldGlobalKey);
            }
        }



























        [HarmonyPatch(typeof(ZNet), "OpenServer")]
        private static class MultiplayerWorldOpenServerPatch
        {
            [HarmonyPostfix]
            private static void Postfix(ZNet __instance)
            {
                MarkMultiplayerWorldIfNeeded(__instance);
            }
        }




    }
}
