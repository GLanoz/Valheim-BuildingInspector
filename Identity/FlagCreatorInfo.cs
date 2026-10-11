using HarmonyLib;
using UnityEngine;

namespace BuildingInspector
{
    public partial class BuildingInspector
    {
        private const string CreatorNameZdoKey = "BuildingInspector_FlagCreatorName";

        [HarmonyPatch(typeof(Piece), nameof(Piece.SetCreator))]
        private static class InspectionFlagCreatorNamePatch
        {
            [HarmonyPostfix]
            private static void Postfix(Piece __instance)
            {
                if (!__instance)
                {
                    return;
                }

                FlagNoteReceiver receiver = __instance.GetComponent<FlagNoteReceiver>();
                if (!receiver)
                {
                    return;
                }

                long creatorId = __instance.GetCreator();
                Player localPlayer = Player.m_localPlayer;
                string creatorName = localPlayer && localPlayer.GetPlayerID() == creatorId
                    ? localPlayer.GetPlayerName()
                    : string.Empty;
                receiver.SaveCreatorName(creatorName);
            }
        }

        private sealed partial class FlagNoteReceiver
        {
            private long GetCreatorId()
            {
                Piece piece = nview ? nview.GetComponent<Piece>() : null;
                return piece ? piece.GetCreator() : 0L;
            }

            public void SaveCreatorName(string creatorName)
            {
                Piece piece = GetComponent<Piece>();
                ZDO zdo = nview ? nview.GetZDO() : null;
                long creatorId = piece ? piece.GetCreator() : 0L;

                if (piece && zdo != null && !string.IsNullOrWhiteSpace(creatorName) &&
                    Player.m_localPlayer && Player.m_localPlayer.GetPlayerID() == creatorId)
                {
                    zdo.Set(CreatorNameZdoKey, creatorName);
                }

            }

            private string GetCreatorName()
            {
                ZDO zdo = nview ? nview.GetZDO() : null;
                string savedName = zdo != null ? zdo.GetString(CreatorNameZdoKey, string.Empty) : string.Empty;
                if (!string.IsNullOrWhiteSpace(savedName))
                {
                    return savedName;
                }

                long creatorId = GetCreatorId();
                Player localPlayer = Player.m_localPlayer;
                bool localPlayerFound = localPlayer && localPlayer.GetPlayerID() == creatorId;

                if (creatorId == 0L)
                {
                    return "Odin";
                }

                string resolvedName = string.Empty;
                if (localPlayerFound)
                {
                    string localName = localPlayer.GetPlayerName();
                    if (!string.IsNullOrWhiteSpace(localName))
                    {
                        resolvedName = localName;
                    }
                }

                Player creatorPlayer = Player.GetPlayer(creatorId);
                if (string.IsNullOrWhiteSpace(resolvedName) && creatorPlayer)
                {
                    string playerName = creatorPlayer.GetPlayerName();
                    if (!string.IsNullOrWhiteSpace(playerName))
                    {
                        resolvedName = playerName;
                    }
                }

                if (ZNet.instance)
                {
                    foreach (ZNetPeer peer in ZNet.instance.GetPeers())
                    {
                        if (peer.m_playerID != creatorId)
                        {
                            continue;
                        }

                        foreach (ZNet.PlayerInfo playerInfo in ZNet.instance.GetPlayerList())
                        {
                            if (!playerInfo.m_characterID.Equals(peer.m_characterID))
                            {
                                continue;
                            }

                            if (string.IsNullOrWhiteSpace(resolvedName) && !string.IsNullOrWhiteSpace(playerInfo.m_name))
                            {
                                resolvedName = playerInfo.m_name;
                            }
                        }

                        break;
                    }
                }

                if (string.IsNullOrWhiteSpace(resolvedName))
                {
                    return "Odin";
                }

                if (zdo != null && nview && nview.IsOwner())
                {
                    zdo.Set(CreatorNameZdoKey, resolvedName);
                }

                return resolvedName;
            }
        }
    }
}
