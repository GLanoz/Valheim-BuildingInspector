using HarmonyLib;

namespace BuildingInspector
{
    public partial class BuildingInspector
    {
        private const string MultiplayerWorldGlobalKey = "BuildingInspector_MultiplayerWorld";

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
