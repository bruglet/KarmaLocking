using RoR2;
using RoR2.UI;
using UnityEngine.EventSystems;
using UnityEngine.Networking;

namespace KarmaLocking
{
    // Lock state. The host owns it; players with the mod get a copy from the host (see LockNetwork).
    // Nothing is added to the game's catalogs, so players without the mod can still join and play:
    // for them Karma just doesn't rise in a locked run.
    internal static class KarmaLock
    {
        internal const string KarmaRuleName = "Karma.InitialLevel";
        internal const int Unlocked = -1;

        private static RuleDef karmaRuleDef;

        // Host: the level locked in the lobby, and the level locked for the current run.
        private static int hostLobbyLevel = Unlocked;
        private static int hostRunLevel = Unlocked;
        private static int hostLobbyCap = Unlocked;
        private static int hostRunCap = Unlocked;

        // Client: the same two values as last sent by the host.
        private static int receivedLobbyLevel = Unlocked;
        private static int receivedRunLevel = Unlocked;
        private static int receivedLobbyCap = Unlocked;
        private static int receivedRunCap = Unlocked;

        // Client: whether the host has this mod (it answered our hello).
        internal static bool HostHasMod { get; private set; }

        internal static RuleDef KarmaRuleDef => karmaRuleDef ??= RuleCatalog.FindRuleDef(KarmaRuleName);

        // Locking needs a host that runs the mod: either we are the host or the host told us it has it.
        internal static bool CanLock => NetworkServer.active || HostHasMod;

        internal static int LobbyLevel => NetworkServer.active ? hostLobbyLevel : HostHasMod ? receivedLobbyLevel : Unlocked;

        internal static int RunLevel => NetworkServer.active ? hostRunLevel : HostHasMod ? receivedRunLevel : Unlocked;

        internal static int LobbyCap => NetworkServer.active ? hostLobbyCap : HostHasMod ? receivedLobbyCap : Unlocked;

        internal static int RunCap => NetworkServer.active ? hostRunCap : HostHasMod ? receivedRunCap : Unlocked;

        internal static bool IsRunLocked => RunLevel >= 0 && Run.instance;

        // Karma levels 0-8 have their level as extraData; "KarmaDisabled" has -1.
        internal static int LevelOf(RuleChoiceDef choiceDef)
        {
            if (choiceDef != null && choiceDef.ruleDef != null && choiceDef.ruleDef == KarmaRuleDef && choiceDef.extraData is int level)
            {
                return level;
            }
            return Unlocked;
        }

        internal static bool IsKarmaChoice(RuleChoiceDef choiceDef)
        {
            return choiceDef != null && choiceDef.ruleDef != null && choiceDef.ruleDef == KarmaRuleDef;
        }

        internal static bool IsKarmaLevelChoice(RuleChoiceDef choiceDef)
        {
            return LevelOf(choiceDef) >= 0;
        }

        // The lobby lock only applies while the lobby's resolved Karma level is the locked one
        // (another player's vote can move the level away from it).
        internal static bool IsLobbyLockActive(RuleBook ruleBook)
        {
            int level = LobbyLevel;
            return level >= 0 && ruleBook != null && KarmaRuleDef != null && LevelOf(ruleBook.GetRuleChoice(KarmaRuleDef)) == level;
        }

        #region Host

        // Applies a lock request from the host's own UI or from a client. Returns false if not allowed now.
        internal static bool SetLobbyLevelServer(int level, bool capped = false)
        {
            if (!NetworkServer.active)
            {
                return false;
            }
            if (!PreGameController.instance)
            {
                KarmaLockingPlugin.Log.LogInfo("Karma can only be locked or unlocked in the lobby.");
                return false;
            }
            if (level < KarmaManager.minKarma || level > KarmaManager.maxKarma)
            {
                level = Unlocked;
            }
            if (capped && level >= 0 && PreGameController.instance.readOnlyRuleBook.startingKarma > level)
            {
                KarmaLockingPlugin.Log.LogInfo("The Karma cap cannot be below the starting level.");
                return false;
            }
            int lobbyLevel = capped ? hostLobbyLevel : level;
            int lobbyCap = capped ? level : hostLobbyCap;
            if (level >= 0)
            {
                if (capped)
                {
                    lobbyLevel = Unlocked;
                }
                else
                {
                    lobbyCap = Unlocked;
                }
            }
            if (hostLobbyLevel != lobbyLevel || hostLobbyCap != lobbyCap)
            {
                hostLobbyLevel = lobbyLevel;
                hostLobbyCap = lobbyCap;
                KarmaLockingPlugin.Log.LogInfo($"Karma lock: {lobbyLevel}; cap: {lobbyCap}.");
                LockNetwork.BroadcastState();
            }
            return true;
        }

        // Decides once per run whether it is locked: only if the run starts on the locked lobby level.
        internal static void OnRunStartServer(Run run)
        {
            if (!NetworkServer.active)
            {
                return;
            }
            int starting = run.ruleBook.startingKarma;
            hostRunLevel = hostLobbyLevel >= 0 && starting == hostLobbyLevel ? hostLobbyLevel : Unlocked;
            hostRunCap = starting >= 0 ? hostLobbyCap : Unlocked;
            if (hostRunCap >= 0)
            {
                KarmaLockingPlugin.Log.LogInfo($"Run Karma capped at {hostRunCap}.");
            }
            KarmaLockingPlugin.Log.LogInfo(hostRunLevel >= 0
                ? $"Run started with Karma locked at {hostRunLevel}."
                : $"Run started with Karma unlocked (starting level {starting}, lobby lock {hostLobbyLevel}).");
            LockNetwork.BroadcastState();
        }

        internal static void OnRunDestroyServer()
        {
            if (NetworkServer.active && (hostRunLevel != Unlocked || hostRunCap != Unlocked))
            {
                hostRunLevel = Unlocked;
                hostRunCap = Unlocked;
                LockNetwork.BroadcastState();
            }
        }

        internal static void GetHostState(out int lobbyLevel, out int runLevel, out int lobbyCap, out int runCap)
        {
            lobbyLevel = hostLobbyLevel;
            runLevel = hostRunLevel;
            lobbyCap = hostLobbyCap;
            runCap = hostRunCap;
        }

        #endregion

        #region Client

        internal static void ReceiveState(int lobbyLevel, int runLevel, int lobbyCap, int runCap)
        {
            if (!HostHasMod)
            {
                KarmaLockingPlugin.Log.LogInfo("Host has KarmaLocking; Karma can be locked.");
            }
            HostHasMod = true;
            receivedLobbyLevel = lobbyLevel;
            receivedRunLevel = runLevel;
            receivedLobbyCap = lobbyCap;
            receivedRunCap = runCap;
        }

        internal static void ResetClientState()
        {
            HostHasMod = false;
            receivedLobbyLevel = Unlocked;
            receivedRunLevel = Unlocked;
            receivedLobbyCap = Unlocked;
            receivedRunCap = Unlocked;
        }

        #endregion

        // Lock or unlock from this game's UI: directly on the host, as a request from a client.
        internal static void RequestLobbyLevel(NetworkUser sender, int level, bool capped = false)
        {
            if (NetworkServer.active)
            {
                SetLobbyLevelServer(level, capped);
            }
            else if (HostHasMod && sender)
            {
                LockNetwork.SendRequest(sender, level, capped);
            }
        }

        // Same lookup RuleChoiceController.OnClick uses: the user that owns the clicking event system.
        internal static NetworkUser NetworkUserForCurrentEventSystem()
        {
            return (EventSystem.current as MPEventSystem)?.localUser?.currentNetworkUser;
        }
    }
}
