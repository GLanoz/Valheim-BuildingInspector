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
                    if (InspectorDebug.Enabled)
                        InspectorDebug.Write($"Flag RPC guards registered: flag={(nview.GetZDO() != null ? nview.GetZDO().m_uid : ZDOID.None)}; role={InspectorDebug.Role}.");
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
