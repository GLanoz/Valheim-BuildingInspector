using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace BuildingInspector
{
    public partial class BuildingInspector
    {
        private static readonly List<FlagNoteReceiver> FlagReceivers = new List<FlagNoteReceiver>();

        private sealed partial class FlagNoteReceiver : MonoBehaviour, Hoverable
        {
            private ZNetView nview;
            private MeshRenderer clothRenderer;
            private int appliedCategory = -1;

            private void Awake()
            {
                if (!FlagReceivers.Contains(this))
                {
                    FlagReceivers.Add(this);
                }

                nview = GetComponent<ZNetView>();
                if (nview)
                {
                    nview.Register<string>(NoteRpcName, SetNoteRpc);
                    nview.Register<int>(ChangeCategoryRpcName, ChangeCategoryRpc);
                }

                Transform clothTransform = transform.Find("InspectionFlagPole/InspectionFlagCloth");
                clothRenderer = clothTransform ? clothTransform.GetComponent<MeshRenderer>() : null;
                ApplyPlayerCollisionIgnores();
            }

            private void Update()
            {
                int categoryIndex = GetCategoryIndex();
                if (categoryIndex < 0 || categoryIndex == appliedCategory)
                {
                    return;
                }

                int prefabCategoryIndex = GetPrefabCategoryIndex();
                if (appliedCategory < 0 && prefabCategoryIndex == categoryIndex)
                {
                    appliedCategory = categoryIndex;
                    return;
                }

                ApplyCategoryAppearance(categoryIndex);
                appliedCategory = categoryIndex;
            }

            private void OnDestroy()
            {
                FlagReceivers.Remove(this);
            }

            public void IgnoreCollisionWith(Player player)
            {
                if (!player)
                {
                    return;
                }

                Collider flagCollider = GetComponent<Collider>();
                if (!flagCollider)
                {
                    return;
                }

                Collider[] playerColliders = player.GetComponentsInChildren<Collider>();
                foreach (Collider playerCollider in playerColliders)
                {
                    if (playerCollider && playerCollider != flagCollider)
                    {
                        Physics.IgnoreCollision(flagCollider, playerCollider, true);
                    }
                }
            }

            private void ApplyPlayerCollisionIgnores()
            {
                Player[] players = FindObjectsByType<Player>(FindObjectsSortMode.None);
                foreach (Player player in players)
                {
                    IgnoreCollisionWith(player);
                }
            }

            private long GetCreatorId()
            {
                Piece piece = nview ? nview.GetComponent<Piece>() : null;
                return piece ? piece.GetCreator() : 0L;
            }

            private int GetCategoryIndex()
            {
                ZDO zdo = nview ? nview.GetZDO() : null;
                if (zdo == null)
                {
                    return -1;
                }

                int storedCategory = zdo.GetInt(FlagCategoryZdoKey, -1);
                return storedCategory >= 0 && storedCategory < flagVariants.Length
                    ? storedCategory
                    : GetPrefabCategoryIndex();
            }

            public int GetEditorCategoryIndex()
            {
                return GetCategoryIndex();
            }

            private int GetPrefabCategoryIndex()
            {
                ZDO zdo = nview ? nview.GetZDO() : null;
                ZNetScene scene = ZNetScene.instance;
                GameObject prefab = zdo != null && scene ? scene.GetPrefab(zdo.GetPrefab()) : null;
                if (!prefab || flagVariants == null)
                {
                    return -1;
                }

                for (int index = 0; index < flagVariants.Length; index++)
                {
                    if (string.Equals(prefab.name, flagVariants[index].PrefabName, StringComparison.Ordinal))
                    {
                        return index;
                    }
                }

                return -1;
            }

            private string GetCategoryDisplayName()
            {
                int categoryIndex = GetCategoryIndex();
                if (categoryIndex >= 0 && categoryIndex < labelNames.Count)
                {
                    string categoryName = string.IsNullOrWhiteSpace(labelNames[categoryIndex].Value)
                        ? $"Category {categoryIndex + 1}"
                        : labelNames[categoryIndex].Value.Trim();
                    return $"Inspection Flag - {categoryName}";
                }

                Piece piece = GetComponent<Piece>();
                return piece ? piece.m_name : "Inspection Flag";
            }

            private void ApplyCategoryAppearance(int categoryIndex)
            {
                if (clothRenderer && flagVariants != null && categoryIndex < flagVariants.Length)
                {
                    Color color = flagVariants[categoryIndex].Color;
                    color.a = Mathf.Min(color.a, 0.85f);
                    clothRenderer.material.color = color;
                }
            }

            public bool CanEditCategory()
            {
                if (!nview)
                {
                    nview = GetComponent<ZNetView>();
                }

                Piece piece = nview ? nview.GetComponent<Piece>() : null;
                ZDO zdo = nview ? nview.GetZDO() : null;
                long creatorId = GetCreatorId();
                bool isCreator = piece && piece.IsCreator();
                return nview && zdo != null && piece && creatorId != 0L && isCreator;
            }

            public void RequestCategoryChange(int categoryIndex)
            {
                if (categoryIndex < 0 || categoryIndex >= flagVariants.Length || !CanEditCategory())
                {
                    return;
                }

                SendFlagEditRequest(nview.GetZDO(), null, categoryIndex);
            }

            private void ChangeCategoryRpc(long sender, int categoryIndex)
            {
                // Object-routed requests cannot prove the original connection on a client owner.
                log.LogWarning("Rejected legacy object-routed inspection flag category update.");
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

            public string GetText()
            {
                string note = nview && nview.GetZDO() != null
                    ? nview.GetZDO().GetString(NoteZdoKey, string.Empty)
                    : string.Empty;
                return LimitNoteLength(note);
            }

            public string GetHoverText()
            {
                Piece piece = GetComponent<Piece>();
                string text = GetCategoryDisplayName();

                string note = GetText();
                if (!string.IsNullOrWhiteSpace(note))
                {
                    text += $"\n<color=#FFE080>Note:</color> {note}";
                }

                if (ShouldShowInspectedBy())
                {
                    text += $"\nInspected by: {GetCreatorName()}";
                }

                long creatorId = GetCreatorId();
                bool canEdit = nview && nview.GetZDO() != null && piece &&
                    creatorId != 0L && piece.IsCreator();
                return canEdit
                    ? text + $"\nPress [<color=yellow><b>{editNoteKey.Value}</b></color>] to edit flag"
                    : text + "\n<color=#AAAAAA>Only the player who placed this flag can edit its note.</color>";
            }

            public string GetHoverName()
            {
                return GetCategoryDisplayName();
            }

            public float GetHoverOffset()
            {
                return 0f;
            }

            public void SetText(string text)
            {
                Piece piece = nview ? nview.GetComponent<Piece>() : null;
                if (nview && nview.GetZDO() != null && piece && piece.IsCreator())
                {
                    SendFlagEditRequest(nview.GetZDO(), LimitNoteLength((text ?? string.Empty).Trim()), -1);
                }
            }

            private void SetNoteRpc(long sender, string text)
            {
                log.LogWarning("Rejected legacy object-routed inspection flag note update.");
            }
        }

        [HarmonyPatch(typeof(Player), "Start")]
        private static class PlayerStartFlagCollisionPatch
        {
            [HarmonyPostfix]
            private static void Postfix(Player __instance)
            {
                foreach (FlagNoteReceiver receiver in FlagReceivers)
                {
                    if (receiver)
                    {
                        receiver.IgnoreCollisionWith(__instance);
                    }
                }
            }
        }
    }
}
