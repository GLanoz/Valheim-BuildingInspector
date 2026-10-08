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

        private const int MaxNoteLength = 160;
        private const int MaxNoteLines = 5;
        private const string NoteRpcName = "BuildingInspector_SetFlagNote";
        private const string NoteZdoKey = "BuildingInspector_FlagNote";
        private const string ChangeCategoryRpcName = "BuildingInspector_ChangeFlagCategory";
        private const string ApplyFlagEditRpcName = "BuildingInspector_ApplyAuthorizedFlagEdit";
        private const string FlagEditResultRpcName = "BuildingInspector_FlagEditResult";
        private const int MaxPendingFlagEdits = 128;
        private const int MaxFlagEditAttempts = 2;
        private const float FlagEditReplyTimeout = 2f;
        private static readonly Dictionary<long, PendingFlagEdit> PendingFlagEdits = new Dictionary<long, PendingFlagEdit>();
        private static long nextFlagEditId;
        private const string FlagCategoryZdoKey = "BuildingInspector_FlagCategory";
        private const string CreatorNameZdoKey = "BuildingInspector_FlagCreatorName";
        private const string MultiplayerWorldGlobalKey = "BuildingInspector_MultiplayerWorld";

        private static ManualLogSource log;
        private static readonly List<FlagNoteReceiver> FlagReceivers = new List<FlagNoteReceiver>();

        private void Awake()
        {
            log = Logger;
            Logger.LogInfo("Building Inspector loading...");
            InitializeConfiguration();
            new Harmony(PluginGUID).PatchAll(typeof(BuildingInspector).Assembly);
            PrefabManager.OnVanillaPrefabsAvailable += RegisterInspectionFlag;
        }










        private static string LimitNoteLength(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            int characterCount = 0;
            System.Text.StringBuilder result = new System.Text.StringBuilder(text.Length);

            foreach (char character in text)
            {
                if (character == '\r' || character == '\n')
                {
                    result.Append(character);
                    continue;
                }

                if (characterCount >= MaxNoteLength)
                {
                    continue;
                }

                characterCount++;
                result.Append(character);
            }

            return result.ToString();
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










        private sealed class FlagNoteReceiver : MonoBehaviour, Hoverable
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

        private bool WouldExceedNoteLimits(string text)
        {
            int characterCount = 0;

            for (int i = 0; i < text.Length; i++)
            {
                char character = text[i];

                if (character != '\n' && character != '\r')
                {
                    characterCount++;
                }

                if (characterCount > MaxNoteLength)
                {
                    return true;
                }
            }

            Text textComponent = flagEditorNoteField.textComponent;
            if (!textComponent)
            {
                return false;
            }

            TextGenerator generator = new TextGenerator();

            TextGenerationSettings settings = textComponent.GetGenerationSettings(
                textComponent.rectTransform.rect.size
            );

            settings.generateOutOfBounds = true;
            settings.updateBounds = true;
            settings.horizontalOverflow = HorizontalWrapMode.Wrap;
            settings.verticalOverflow = VerticalWrapMode.Overflow;

            generator.Populate(text, settings);

            int lineCount = generator.lineCount;

            return lineCount > MaxNoteLines;
        }

        private char ValidateNoteInput(string text, int charIndex, char addedChar)
        {
            string newText = text.Insert(charIndex, addedChar.ToString());

            if (WouldExceedNoteLimits(newText))
            {
                return '\0';
            }

            return addedChar;
        }

        private sealed class PendingFlagEdit
        {
            public ZRpc RequestConnection;
            public ZDOID Target;
            public long CreatorId;
            public string Note;
            public int Category;
            public int Attempts;
            public long SentOwner;
            public float RetryAt;
        }

        private static void SendFlagEditRequest(ZDO zdo, string note, int category)
        {
            ZNet net = ZNet.instance;
            if (zdo == null || !net)
            {
                return;
            }

            if (net.IsServer())
            {
                QueueFlagEdit(null, zdo.m_uid, note, category);
                return;
            }

            ZNetPeer server = net.GetServerPeer();
            if (server != null)
            {
                if (note != null)
                    server.m_rpc.Invoke(NoteRpcName, zdo.m_uid, note);
                else
                    server.m_rpc.Invoke(ChangeCategoryRpcName, zdo.m_uid, category);
            }
        }

        private static long GetRequestCreatorId(ZRpc connection)
        {
            ZNet net = ZNet.instance;
            if (!net || !net.IsServer())
                return 0L;

            if (connection == null)
            {
                // Match Piece.IsCreator's profile-based check for the host/singleplayer path.
                PlayerProfile profile = Game.instance ? Game.instance.GetPlayerProfile() : null;
                return Player.m_localPlayer && profile != null ? profile.GetPlayerID() : 0L;
            }

            ZNetPeer peer = GetConnectionPeer(net, connection);
            if (peer == null || !peer.IsReady())
                return 0L;

            // PlayerID messages are not guaranteed to populate m_playerID in this game version.
            // Resolve the character announced by this connection, without needing a loaded Player.
            ZDO character = ZDOMan.instance != null ? ZDOMan.instance.GetZDO(peer.m_characterID) : null;
            if (character != null)
            {
                GameObject prefab = ZNetScene.instance ? ZNetScene.instance.GetPrefab(character.GetPrefab()) : null;
                return character.GetOwner() == peer.m_uid && prefab && prefab.GetComponent<Player>()
                    ? character.GetLong(ZDOVars.s_playerID, 0L) : 0L;
            }

            return peer.m_playerID;
        }

        private static ZNetPeer GetConnectionPeer(ZNet net, ZRpc connection)
        {
            // The DLL's GetPeer(ZRpc) overload is private; use its public peer list.
            foreach (ZNetPeer peer in net.GetPeers())
                if (ReferenceEquals(peer.m_rpc, connection))
                    return peer;
            return null;
        }

        private static ZDO GetEditableFlagZdo(ZDOID target)
        {
            ZDO zdo = ZDOMan.instance != null ? ZDOMan.instance.GetZDO(target) : null;
            GameObject prefab = zdo != null && ZNetScene.instance
                ? ZNetScene.instance.GetPrefab(zdo.GetPrefab()) : null;
            if (!prefab || !prefab.GetComponent<FlagNoteReceiver>() || flagVariants == null)
                return null;

            // GetPrefab also resolves the existing legacy aliases; no saved ID is changed.
            foreach (FlagVariant variant in flagVariants)
            {
                if (prefab.name == variant.PrefabName)
                    return zdo;
            }

            return testFlagVariant != null && prefab.name == testFlagVariant.PrefabName ? zdo : null;
        }

        private static void QueueFlagEdit(ZRpc connection, ZDOID target, string note, int category)
        {
            long creatorId = GetRequestCreatorId(connection);
            ZDO zdo = GetEditableFlagZdo(target);
            ZNet net = ZNet.instance;
            ZNetPeer peer = net && connection != null ? GetConnectionPeer(net, connection) : null;
            bool validConnection = net && net.IsServer() &&
                (connection == null ? creatorId != 0L : peer != null && peer.IsReady());
            ZDO rawZdo = ZDOMan.instance != null ? ZDOMan.instance.GetZDO(target) : null;
            if (!validConnection || (rawZdo != null && zdo == null) ||
                (zdo != null && creatorId != 0L && zdo.GetLong(ZDOVars.s_creator, 0L) != creatorId) ||
                (note == null && (category < 0 || category >= FlagVariantCount)) ||
                PendingFlagEdits.Count >= MaxPendingFlagEdits)
            {
                log.LogWarning("Rejected unauthorized inspection flag edit request.");
                return;
            }

            // Do not retry an older value over a newer edit of the same field.
            List<long> superseded = null;
            foreach (KeyValuePair<long, PendingFlagEdit> entry in PendingFlagEdits)
            {
                if (creatorId != 0L && entry.Value.CreatorId == creatorId &&
                    entry.Value.Target.Equals(target) && (entry.Value.Note != null) == (note != null))
                {
                    if (superseded == null)
                        superseded = new List<long>();
                    superseded.Add(entry.Key);
                }
            }
            if (superseded != null)
                foreach (long oldEditId in superseded)
                    PendingFlagEdits.Remove(oldEditId);

            long editId = ++nextFlagEditId;
            PendingFlagEdits.Add(editId, new PendingFlagEdit
            {
                RequestConnection = connection,
                Target = target,
                CreatorId = creatorId,
                Note = note == null ? null : LimitNoteLength(note),
                Category = category
            });
            DispatchFlagEdit(editId);
        }

        private static void DispatchFlagEdit(long editId)
        {
            PendingFlagEdit edit;
            if (!PendingFlagEdits.TryGetValue(editId, out edit))
                return;

            ZDO zdo = GetEditableFlagZdo(edit.Target);
            long creatorId = GetRequestCreatorId(edit.RequestConnection);
            ZDO rawZdo = ZDOMan.instance != null ? ZDOMan.instance.GetZDO(edit.Target) : null;
            if (edit.Attempts >= MaxFlagEditAttempts || (rawZdo != null && zdo == null) ||
                (creatorId != 0L && edit.CreatorId != 0L && creatorId != edit.CreatorId) ||
                (zdo != null && creatorId != 0L && zdo.GetLong(ZDOVars.s_creator, 0L) != creatorId))
            {
                PendingFlagEdits.Remove(editId);
                log.LogWarning("Inspection flag edit expired or authorization changed.");
                return;
            }

            edit.Attempts++;
            edit.RetryAt = Time.unscaledTime + FlagEditReplyTimeout;
            // Character/flag ZDO replication can trail the direct request after spawn or rejoin.
            // Wait within the same bounded retry budget; never approve an unresolved author.
            if (creatorId == 0L || zdo == null)
                return;

            edit.CreatorId = creatorId;
            edit.SentOwner = zdo.GetOwner();
            if (edit.SentOwner == ZNet.GetUID())
            {
                if (ApplyAuthorizedFlagEdit(edit.Target, edit.CreatorId, edit.Note, edit.Category))
                    PendingFlagEdits.Remove(editId);
                return;
            }

            ZNetPeer owner = ZNet.instance.GetPeer(edit.SentOwner);
            if (owner == null || !owner.IsReady())
                return; // A bounded timeout retries if ownership is still being assigned.

            ZPackage package = new ZPackage();
            package.Write(editId);
            package.Write(edit.Target);
            package.Write(edit.CreatorId);
            package.Write(edit.Note != null);
            if (edit.Note != null)
                package.Write(edit.Note);
            else
                package.Write(edit.Category);
            owner.m_rpc.Invoke(ApplyFlagEditRpcName, package);
        }

        private static bool ApplyAuthorizedFlagEdit(ZDOID target, long creatorId, string note, int category)
        {
            ZDO zdo = GetEditableFlagZdo(target);
            if (zdo == null || !zdo.IsOwner() || creatorId == 0L ||
                zdo.GetLong(ZDOVars.s_creator, 0L) != creatorId ||
                (note == null && (category < 0 || category >= FlagVariantCount)))
                return false;

            if (note != null)
                zdo.Set(NoteZdoKey, LimitNoteLength(note));
            else
                zdo.Set(FlagCategoryZdoKey, category);
            return true;
        }

        private static void ReceiveAuthorizedFlagEdit(ZRpc connection, ZPackage package)
        {
            ZNet net = ZNet.instance;
            ZNetPeer server = net && !net.IsServer() ? net.GetServerPeer() : null;
            if (server == null || !ReferenceEquals(server.m_rpc, connection))
            {
                log.LogWarning("Rejected inspection flag approval not sent by the server connection.");
                return;
            }

            long editId = package.ReadLong();
            ZDOID target = package.ReadZDOID();
            long creatorId = package.ReadLong();
            bool isNote = package.ReadBool();
            string note = isNote ? package.ReadString() : null;
            int category = isNote ? -1 : package.ReadInt();
            bool applied = ApplyAuthorizedFlagEdit(target, creatorId, note, category);
            connection.Invoke(FlagEditResultRpcName, editId, applied);
        }

        private static void ReceiveFlagEditResult(ZRpc connection, long editId, bool applied)
        {
            ZNet net = ZNet.instance;
            if (!net || !net.IsServer())
                return;

            PendingFlagEdit edit;
            ZNetPeer peer = GetConnectionPeer(net, connection);
            if (peer == null || !peer.IsReady() || !PendingFlagEdits.TryGetValue(editId, out edit) ||
                peer.m_uid != edit.SentOwner)
                return;

            ZDO zdo = GetEditableFlagZdo(edit.Target);
            if (applied && zdo != null && zdo.GetOwner() == edit.SentOwner)
                PendingFlagEdits.Remove(editId);
            else
                DispatchFlagEdit(editId); // At most one retry, using the server's current owner.
        }

        [HarmonyPatch(typeof(ZNet), "OnNewConnection")]
        private static class FlagEditConnectionPatch
        {
            [HarmonyPostfix]
            private static void Postfix(ZNetPeer __0)
            {
                __0.m_rpc.Register<ZDOID, string>(NoteRpcName,
                    (connection, target, note) => QueueFlagEdit(connection, target, note ?? string.Empty, -1));
                __0.m_rpc.Register<ZDOID, int>(ChangeCategoryRpcName,
                    (connection, target, category) => QueueFlagEdit(connection, target, null, category));
                __0.m_rpc.Register<ZPackage>(ApplyFlagEditRpcName, ReceiveAuthorizedFlagEdit);
                __0.m_rpc.Register<long, bool>(FlagEditResultRpcName, ReceiveFlagEditResult);
            }
        }

        [HarmonyPatch(typeof(ZNet), "Awake")]
        private static class FlagEditWorldResetPatch
        {
            [HarmonyPostfix]
            private static void Postfix()
            {
                PendingFlagEdits.Clear();
            }
        }

        [HarmonyPatch(typeof(ZNet), "Update")]
        private static class FlagEditRetryPatch
        {
            [HarmonyPostfix]
            private static void Postfix(ZNet __instance)
            {
                if (!__instance.IsServer() || PendingFlagEdits.Count == 0)
                    return;

                List<long> expired = null;
                foreach (KeyValuePair<long, PendingFlagEdit> entry in PendingFlagEdits)
                {
                    if (Time.unscaledTime >= entry.Value.RetryAt)
                    {
                        if (expired == null)
                            expired = new List<long>();
                        expired.Add(entry.Key);
                    }
                }
                if (expired != null)
                    foreach (long editId in expired)
                        DispatchFlagEdit(editId);
            }
        }

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

        [HarmonyPatch(typeof(ZNet), "OpenServer")]
        private static class MultiplayerWorldOpenServerPatch
        {
            [HarmonyPostfix]
            private static void Postfix(ZNet __instance)
            {
                MarkMultiplayerWorldIfNeeded(__instance);
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
