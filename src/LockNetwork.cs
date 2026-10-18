using System.Collections.Generic;
using System.Globalization;
using RoR2;
using RoR2.Networking;
using UnityEngine.Networking;

namespace KarmaLocking
{
    // Host <-> modded client communication that never sends anything a vanilla peer can't handle.
    //
    // A vanilla game that receives an unknown network message ID logs an error and drops the rest of that
    // packet, which can lose unrelated messages batched with it. So:
    //   client -> host: console commands flagged ExecuteOnServer. They travel inside the vanilla
    //                   "send console command" call; a vanilla host just logs "not a recognized ConCommand".
    //   host -> client: a custom message, but only to connections that announced the mod with
    //                   karmalock_hello. Clients without the mod never receive it.
    internal static class LockNetwork
    {
        // Vanilla uses 48-88; other mods use e.g. 20024 (WolfoLibrary). "KL" = 0x4B4C.
        internal const short StateMessageType = 0x4B4C;
        private const byte ProtocolVersion = 1;

        private static readonly HashSet<NetworkConnection> moddedConnections = new HashSet<NetworkConnection>();

        internal static void Init()
        {
            NetworkUser.onPostNetworkUserStart += OnPostNetworkUserStart;
            NetworkManagerSystem.onStartClientGlobal += _ => KarmaLock.ResetClientState();
            NetworkManagerSystem.onStopClientGlobal += KarmaLock.ResetClientState;
            NetworkManagerSystem.onStartServerGlobal += moddedConnections.Clear;
            NetworkManagerSystem.onStopServerGlobal += moddedConnections.Clear;
            NetworkManagerSystem.onServerDisconnectGlobal += conn => moddedConnections.Remove(conn);
        }

        // A client tells the host it has the mod as soon as its local user exists on the connection.
        private static void OnPostNetworkUserStart(NetworkUser networkUser)
        {
            if (!NetworkServer.active && networkUser && networkUser.isLocalPlayer)
            {
                RoR2.Console.instance.SubmitCmd(networkUser, "karmalock_hello");
            }
        }

        internal static void SendRequest(NetworkUser sender, int level)
        {
            RoR2.Console.instance.SubmitCmd(sender, "karmalock_set " + level.ToString(CultureInfo.InvariantCulture));
        }

        #region Host

        internal static void BroadcastState()
        {
            if (!NetworkServer.active)
            {
                return;
            }
            moddedConnections.RemoveWhere(conn => conn == null || !conn.isConnected);
            foreach (var conn in moddedConnections)
            {
                SendState(conn);
            }
        }

        private static void SendState(NetworkConnection conn)
        {
            KarmaLock.GetHostState(out int lobbyLevel, out int runLevel);
            var writer = new NetworkWriter();
            writer.StartMessage(StateMessageType);
            writer.Write(ProtocolVersion);
            writer.Write((sbyte)lobbyLevel);
            writer.Write((sbyte)runLevel);
            writer.FinishMessage();
            conn.SendWriter(writer, QosChannelIndex.defaultReliable.intVal);
        }

        private static NetworkConnection RemoteConnectionOf(NetworkUser networkUser)
        {
            if (!networkUser || networkUser.isLocalPlayer)
            {
                return null;
            }
            var conn = networkUser.connectionToClient;
            return conn != null && conn.isConnected ? conn : null;
        }

        [ConCommand(commandName = "karmalock_hello", flags = ConVarFlags.ExecuteOnServer,
            helpText = "Sent by clients with KarmaLocking so the host shares the Karma lock state with them.")]
        private static void CCHello(ConCommandArgs args)
        {
            var conn = RemoteConnectionOf(args.sender);
            if (!NetworkServer.active || conn == null)
            {
                return;
            }
            if (moddedConnections.Add(conn))
            {
                KarmaLockingPlugin.Log.LogInfo($"{args.sender.userName} has KarmaLocking.");
            }
            SendState(conn);
        }

        [ConCommand(commandName = "karmalock_set", flags = ConVarFlags.ExecuteOnServer,
            helpText = "Lobby only. Locks Karma at the given level (0-8), or unlocks it with -1. Usage: karmalock_set <level>")]
        private static void CCSet(ConCommandArgs args)
        {
            if (!NetworkServer.active)
            {
                return;
            }
            args.CheckArgumentCount(1);
            int level = args.GetArgInt(0);

            bool fromRemote = args.sender && !args.sender.isLocalPlayer;
            if (fromRemote && !KarmaLockingPlugin.AllowClientLocking.Value)
            {
                KarmaLockingPlugin.Log.LogInfo($"Ignored Karma lock request from {args.sender.userName} (AllowClientLocking is off).");
                // Resend so their UI drops the lock it may be showing.
                var conn = RemoteConnectionOf(args.sender);
                if (conn != null && moddedConnections.Contains(conn))
                {
                    SendState(conn);
                }
                return;
            }
            KarmaLock.SetLobbyLevelServer(level);
        }

        #endregion

        #region Client

        [NetworkMessageHandler(msgType = StateMessageType, client = true)]
        private static void HandleState(NetworkMessage netMsg)
        {
            if (NetworkServer.active)
            {
                return;
            }
            byte version = netMsg.reader.ReadByte();
            if (version != ProtocolVersion)
            {
                KarmaLockingPlugin.Log.LogWarning($"Host uses a different KarmaLocking protocol ({version}, expected {ProtocolVersion}); update the mod.");
                return;
            }
            int lobbyLevel = netMsg.reader.ReadSByte();
            int runLevel = netMsg.reader.ReadSByte();
            KarmaLock.ReceiveState(lobbyLevel, runLevel);
        }

        #endregion

        [ConCommand(commandName = "karmalock_status", flags = ConVarFlags.None, helpText = "Prints the Karma lock state.")]
        private static void CCStatus(ConCommandArgs args)
        {
            string role = NetworkServer.active ? "host" : NetworkClient.active ? "client" : "offline";
            int karma = KarmaManager.instance ? KarmaManager.instance.GetEffectiveKarma() : -1;
            UnityEngine.Debug.Log($"[KarmaLocking] role={role} canLock={KarmaLock.CanLock} hostHasMod={KarmaLock.HostHasMod} "
                + $"lobbyLevel={KarmaLock.LobbyLevel} runLevel={KarmaLock.RunLevel} currentKarma={karma} moddedClients={moddedConnections.Count}");
        }
    }
}
