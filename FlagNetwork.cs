using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace BuildingInspector
{
    public partial class BuildingInspector
    {
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

        private static void TraceFlagEdit(string eventName, ZDOID target, ZRpc connection, long checkedCharacterId,
            string note, int category, string reason, long editId = 0L)
        {
            if (!InspectorDebug.Enabled) return;
            ZNet net = ZNet.instance;
            ZNetPeer peer = net && connection != null ? GetConnectionPeer(net, connection) : null;
            if (net && !net.IsServer() && connection == null &&
                (eventName == "change applied" || eventName == "apply denied"))
                peer = net.GetServerPeer();
            ZDO zdo = ZDOMan.instance != null ? ZDOMan.instance.GetZDO(target) : null;
            long sender = peer != null ? peer.m_uid : connection == null && net && ZDOMan.instance != null ? ZNet.GetUID() : 0L;
            InspectorDebug.Write($"Network {eventName}: operation={(note != null ? "note" : "category")}; request={editId}; " +
                $"flag={target}; senderUID={sender}; creator={(zdo != null ? zdo.GetLong(ZDOVars.s_creator, 0L) : 0L)}; " +
                $"checkedCharacter={checkedCharacterId}; owner={(zdo != null ? zdo.GetOwner() : 0L)}; " +
                $"role={InspectorDebug.Role}; noteLength={(note != null ? note.Length : 0)}; category={category}; reason={reason}.");
        }

        private static string GetCreatorResolutionReason(ZRpc connection)
        {
            ZNet net = ZNet.instance;
            if (!net || !net.IsServer()) return "NotServer";
            if (connection == null) return !Player.m_localPlayer ? "LocalPlayerUnavailable" : "LocalProfileIdUnavailable";
            ZNetPeer peer = GetConnectionPeer(net, connection);
            if (peer == null || !peer.IsReady()) return "SenderConnectionNotReady";
            ZDO character = ZDOMan.instance != null ? ZDOMan.instance.GetZDO(peer.m_characterID) : null;
            if (character == null) return "CharacterZdoNotReplicated";
            if (character.GetOwner() != peer.m_uid) return "CharacterOwnerDoesNotMatchConnection";
            GameObject prefab = ZNetScene.instance ? ZNetScene.instance.GetPrefab(character.GetPrefab()) : null;
            if (!prefab || !prefab.GetComponent<Player>()) return "CharacterPrefabNotPlayer";
            return "CharacterProfileIdMissing";
        }

        private static void SendFlagEditRequest(ZDO zdo, string note, int category)
        {
            ZNet net = ZNet.instance;
            if (zdo == null || !net)
            {
                if (InspectorDebug.Enabled)
                    TraceFlagEdit("send denied", zdo != null ? zdo.m_uid : ZDOID.None, null, 0L,
                        note, category, zdo == null ? "FlagZdoUnavailable" : "NetworkUnavailable");
                return;
            }

            if (InspectorDebug.Enabled)
                TraceFlagEdit("sending request", zdo.m_uid, null, 0L, note, category, "ToServer");
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
            else if (InspectorDebug.Enabled)
                TraceFlagEdit("send denied", zdo.m_uid, null, 0L, note, category, "ServerPeerUnavailable");
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
            if (InspectorDebug.Enabled)
                TraceFlagEdit("request received", target, connection, creatorId, note, category, "CheckingAuthorization");
            if (!validConnection || (rawZdo != null && zdo == null) ||
                (zdo != null && creatorId != 0L && zdo.GetLong(ZDOVars.s_creator, 0L) != creatorId) ||
                (note == null && (category < 0 || category >= FlagVariantCount)) ||
                PendingFlagEdits.Count >= MaxPendingFlagEdits)
            {
                if (InspectorDebug.Enabled)
                    TraceFlagEdit("authorization denied", target, connection, creatorId, note, category,
                        !validConnection ? "ConnectionNotReadyOrNotServer" :
                        rawZdo != null && zdo == null ? "UnsupportedFlagPrefab" :
                        zdo != null && creatorId != 0L && zdo.GetLong(ZDOVars.s_creator, 0L) != creatorId ? "CreatorMismatch" :
                        note == null && (category < 0 || category >= FlagVariantCount) ? "CategoryOutOfRange" : "PendingLimitReached");
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
                {
                    if (InspectorDebug.Enabled)
                    {
                        PendingFlagEdit oldEdit = PendingFlagEdits[oldEditId];
                        TraceFlagEdit("request superseded", oldEdit.Target, oldEdit.RequestConnection, oldEdit.CreatorId,
                            oldEdit.Note, oldEdit.Category, "NewerEditOfSameField", oldEditId);
                    }
                    PendingFlagEdits.Remove(oldEditId);
                }

            long editId = ++nextFlagEditId;
            PendingFlagEdits.Add(editId, new PendingFlagEdit
            {
                RequestConnection = connection,
                Target = target,
                CreatorId = creatorId,
                Note = note == null ? null : LimitNoteLength(note),
                Category = category
            });
            if (InspectorDebug.Enabled)
                TraceFlagEdit("request queued", target, connection, creatorId, note, category,
                    creatorId == 0L ? GetCreatorResolutionReason(connection) :
                    zdo == null ? "WaitingForFlagZdo" : "Authorized", editId);
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
                if (InspectorDebug.Enabled)
                    TraceFlagEdit("request ended without applying", edit.Target, edit.RequestConnection, creatorId,
                        edit.Note, edit.Category, edit.Attempts >= MaxFlagEditAttempts ? "RetryBudgetExhausted" :
                        rawZdo != null && zdo == null ? "UnsupportedFlagPrefab" :
                        creatorId != 0L && edit.CreatorId != 0L && creatorId != edit.CreatorId ? "SenderCharacterChanged" :
                        "CreatorMismatch", editId);
                PendingFlagEdits.Remove(editId);
                log.LogWarning("Inspection flag edit expired or authorization changed.");
                return;
            }

            edit.Attempts++;
            edit.RetryAt = Time.unscaledTime + FlagEditReplyTimeout;
            // Character/flag ZDO replication can trail the direct request after spawn or rejoin.
            // Wait within the same bounded retry budget; never approve an unresolved author.
            if (creatorId == 0L || zdo == null)
            {
                if (InspectorDebug.Enabled)
                    TraceFlagEdit("waiting for replication", edit.Target, edit.RequestConnection, creatorId,
                        edit.Note, edit.Category, creatorId == 0L ? GetCreatorResolutionReason(edit.RequestConnection) :
                        "FlagZdoUnavailable", editId);
                return;
            }

            edit.CreatorId = creatorId;
            if (InspectorDebug.Enabled && edit.Attempts > 1)
            {
                TraceFlagEdit("retrying request", edit.Target, edit.RequestConnection, creatorId, edit.Note, edit.Category,
                    edit.SentOwner != zdo.GetOwner() ? "OwnerChanged" : "OwnerReplyMissingOrApplyRejected", editId);
                InspectorDebug.Write($"Retry ownership: request={editId}; flag={edit.Target}; previousOwner={edit.SentOwner}; currentOwner={zdo.GetOwner()}.");
            }
            edit.SentOwner = zdo.GetOwner();
            if (edit.SentOwner == ZNet.GetUID())
            {
                if (ApplyAuthorizedFlagEdit(edit.Target, edit.CreatorId, edit.Note, edit.Category))
                    PendingFlagEdits.Remove(editId);
                return;
            }

            ZNetPeer owner = ZNet.instance.GetPeer(edit.SentOwner);
            if (owner == null || !owner.IsReady())
            {
                if (InspectorDebug.Enabled)
                    TraceFlagEdit("owner unavailable", edit.Target, edit.RequestConnection, creatorId,
                        edit.Note, edit.Category, "OwnerPeerNotReady", editId);
                return; // A bounded timeout retries if ownership is still being assigned.
            }

            ZPackage package = new ZPackage();
            package.Write(editId);
            package.Write(edit.Target);
            package.Write(edit.CreatorId);
            package.Write(edit.Note != null);
            if (edit.Note != null)
                package.Write(edit.Note);
            else
                package.Write(edit.Category);
            if (InspectorDebug.Enabled)
                TraceFlagEdit("forwarding approval", edit.Target, edit.RequestConnection, creatorId,
                    edit.Note, edit.Category, "ToCurrentOwner", editId);
            owner.m_rpc.Invoke(ApplyFlagEditRpcName, package);
        }

        private static bool ApplyAuthorizedFlagEdit(ZDOID target, long creatorId, string note, int category)
        {
            ZDO zdo = GetEditableFlagZdo(target);
            if (zdo == null || !zdo.IsOwner() || creatorId == 0L ||
                zdo.GetLong(ZDOVars.s_creator, 0L) != creatorId ||
                (note == null && (category < 0 || category >= FlagVariantCount)))
            {
                if (InspectorDebug.Enabled)
                    TraceFlagEdit("apply denied", target, null, creatorId, note, category,
                        zdo == null ? "FlagZdoUnavailableOrUnsupported" : !zdo.IsOwner() ? "NotCurrentOwner" :
                        creatorId == 0L ? "CreatorIdMissing" : zdo.GetLong(ZDOVars.s_creator, 0L) != creatorId ?
                        "CreatorMismatch" : "CategoryOutOfRange");
                return false;
            }

            if (note != null)
                zdo.Set(NoteZdoKey, LimitNoteLength(note));
            else
                zdo.Set(FlagCategoryZdoKey, category);
            if (InspectorDebug.Enabled)
                TraceFlagEdit("change applied", target, null, creatorId, note, category, "Applied");
            return true;
        }

        private static void ReceiveAuthorizedFlagEdit(ZRpc connection, ZPackage package)
        {
            ZNet net = ZNet.instance;
            ZNetPeer server = net && !net.IsServer() ? net.GetServerPeer() : null;
            if (server == null || !ReferenceEquals(server.m_rpc, connection))
            {
                if (InspectorDebug.Enabled)
                    InspectorDebug.Write($"Approval denied: role={InspectorDebug.Role}; reason=NotServerConnection.");
                log.LogWarning("Rejected inspection flag approval not sent by the server connection.");
                return;
            }

            long editId = package.ReadLong();
            ZDOID target = package.ReadZDOID();
            long creatorId = package.ReadLong();
            bool isNote = package.ReadBool();
            string note = isNote ? package.ReadString() : null;
            int category = isNote ? -1 : package.ReadInt();
            if (InspectorDebug.Enabled)
                TraceFlagEdit("approval received", target, connection, creatorId, note, category, "ServerConnectionVerified", editId);
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
            {
                if (InspectorDebug.Enabled)
                    InspectorDebug.Write($"Edit result ignored: request={editId}; role={InspectorDebug.Role}; reason=UnknownRequestOrUnexpectedConnection.");
                return;
            }

            ZDO zdo = GetEditableFlagZdo(edit.Target);
            if (InspectorDebug.Enabled)
                TraceFlagEdit("owner result received", edit.Target, connection, edit.CreatorId, edit.Note, edit.Category,
                    !applied ? "OwnerRejectedApply" : zdo == null ? "FlagZdoUnavailable" :
                    zdo.GetOwner() != edit.SentOwner ? "OwnerChanged" : "AppliedAndConfirmed", editId);
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
                if (InspectorDebug.Enabled)
                    InspectorDebug.Write($"RPC registered on connection: role={InspectorDebug.Role}; peerUID={__0.m_uid}.");
            }
        }

        [HarmonyPatch(typeof(ZNet), "Awake")]
        private static class FlagEditWorldResetPatch
        {
            [HarmonyPostfix]
            private static void Postfix()
            {
                if (InspectorDebug.Enabled)
                    InspectorDebug.Write($"Network world initialized: pendingRequestsCleared={PendingFlagEdits.Count}.");
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

        private sealed partial class FlagNoteReceiver
        {
            public void RequestCategoryChange(int categoryIndex)
            {
                if (categoryIndex < 0 || categoryIndex >= flagVariants.Length || !CanEditCategory())
                {
                    if (InspectorDebug.Enabled)
                        TraceFlagEdit("category request denied", nview && nview.GetZDO() != null ? nview.GetZDO().m_uid : ZDOID.None,
                            null, 0L, null, categoryIndex, categoryIndex < 0 || categoryIndex >= flagVariants.Length ?
                            "CategoryOutOfRange" : "LocalCreatorCheckFailed");
                    return;
                }

                SendFlagEditRequest(nview.GetZDO(), null, categoryIndex);
            }

            private void ChangeCategoryRpc(long sender, int categoryIndex)
            {
                // Object-routed requests cannot prove the original connection on a client owner.
                if (InspectorDebug.Enabled)
                    InspectorDebug.Write($"Legacy category RPC denied: senderUID={sender}; category={categoryIndex}; " +
                        $"role={InspectorDebug.Role}; reason=ObjectRoutedTransportDisabled.");
                log.LogWarning("Rejected legacy object-routed inspection flag category update.");
            }

            public void SetText(string text)
            {
                Piece piece = nview ? nview.GetComponent<Piece>() : null;
                if (nview && nview.GetZDO() != null && piece && piece.IsCreator())
                {
                    SendFlagEditRequest(nview.GetZDO(), LimitNoteLength((text ?? string.Empty).Trim()), -1);
                }
                else if (InspectorDebug.Enabled)
                    TraceFlagEdit("note request denied", nview && nview.GetZDO() != null ? nview.GetZDO().m_uid : ZDOID.None,
                        null, 0L, text ?? string.Empty, -1, "LocalCreatorCheckFailedOrZdoUnavailable");
            }

            private void SetNoteRpc(long sender, string text)
            {
                if (InspectorDebug.Enabled)
                    InspectorDebug.Write($"Legacy note RPC denied: senderUID={sender}; noteLength={(text != null ? text.Length : 0)}; " +
                        $"role={InspectorDebug.Role}; reason=ObjectRoutedTransportDisabled.");
                log.LogWarning("Rejected legacy object-routed inspection flag note update.");
            }
        }
    }
}
