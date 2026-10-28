using System;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using RoR2;
using RoR2.RemoteGameBrowser;
using RoR2.UI;
using UnityEngine.EventSystems;
using UnityEngine.Networking;

namespace KarmaLocking
{
    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]
    public class KarmaLockingPlugin : BaseUnityPlugin
    {
        public const string PluginGUID = "com.elevated.KarmaLocking";
        public const string PluginName = "KarmaLocking";
        public const string PluginVersion = "1.0.0";

        internal static ManualLogSource Log;
        internal static ConfigEntry<bool> AllowClientLocking;

        private static FieldInfo ruleChoiceDefField;

        // Set while a known UI builds its rule icons, so RuleChoiceController.SetChoice knows
        // what a Karma icon there stands for. Null means "current lobby/run".
        private static Func<RuleChoiceDef, bool> setChoiceSource;
        private static bool setChoiceIsVoteButton;

        // Lobby popout buttons: the badge sits on the level the lobby lock is set to.
        private static readonly Func<RuleChoiceDef, bool> LobbyButtonSource = choice =>
            KarmaLock.LevelOf(choice) is int level && level >= 0 && level == KarmaLock.LobbyLevel;

        // Server browser: another lobby's state is unknown (nothing about the lock is in the lobby data).
        private static readonly Func<RuleChoiceDef, bool> NeverLocked = _ => false;

        public void Awake()
        {
            Log = Logger;

            AllowClientLocking = Config.Bind("General", "AllowClientLocking", true,
                "Host only. Let other players who have the mod lock and unlock Karma in your lobby. "
                + "If off, only the host can.");

            LockSprite.Load();
            LockNetwork.Init();

            ruleChoiceDefField = typeof(RuleChoiceController).GetField("choiceDef", BindingFlags.Instance | BindingFlags.NonPublic);
            if (ruleChoiceDefField == null)
            {
                Log.LogError("RuleChoiceController.choiceDef not found; clicking to lock will not work.");
            }

            Run.onRunStartGlobal += KarmaLock.OnRunStartServer;
            Run.onRunDestroyGlobal += Run_onRunDestroyGlobal;

            // Gameplay (host)
            On.RoR2.KarmaManager.ModifyKarma += KarmaManager_ModifyKarma;
            On.RoR2.KarmaManager.SetKarmaServer += KarmaManager_SetKarmaServer;

            // Lobby input
            On.RoR2.UI.RuleChoiceController.OnClick += RuleChoiceController_OnClick;
            On.RoR2.UI.MPButton.OnPointerClick += MPButton_OnPointerClick;
            On.RoR2.UI.RuleCategoryController.SetRandomVotes += RuleCategoryController_SetRandomVotes;

            // Icons
            On.RoR2.UI.RuleChoiceController.SetChoice += RuleChoiceController_SetChoice;
            On.RoR2.UI.RuleCategoryController.SetupGridVoteDisplay += RuleCategoryController_SetupGridVoteDisplay;
            On.RoR2.UI.RuleCategoryController.SetupGridVoteDisplayPopout += RuleCategoryController_SetupGridVoteDisplayPopout;
            On.RoR2.RemoteGameDetailsPanelController.SetGameInfoInternal += RemoteGameDetailsPanelController_SetGameInfoInternal;
            On.RoR2.UI.KarmaIcon.OnEnable += KarmaIcon_OnEnable;
            On.RoR2.UI.GameEndReportPanelController.SetDisplayData += GameEndReportPanelController_SetDisplayData;
        }

        public void OnDestroy()
        {
            Run.onRunStartGlobal -= KarmaLock.OnRunStartServer;
            Run.onRunDestroyGlobal -= Run_onRunDestroyGlobal;
            On.RoR2.KarmaManager.ModifyKarma -= KarmaManager_ModifyKarma;
            On.RoR2.KarmaManager.SetKarmaServer -= KarmaManager_SetKarmaServer;
            On.RoR2.UI.RuleChoiceController.OnClick -= RuleChoiceController_OnClick;
            On.RoR2.UI.MPButton.OnPointerClick -= MPButton_OnPointerClick;
            On.RoR2.UI.RuleCategoryController.SetRandomVotes -= RuleCategoryController_SetRandomVotes;
            On.RoR2.UI.RuleChoiceController.SetChoice -= RuleChoiceController_SetChoice;
            On.RoR2.UI.RuleCategoryController.SetupGridVoteDisplay -= RuleCategoryController_SetupGridVoteDisplay;
            On.RoR2.UI.RuleCategoryController.SetupGridVoteDisplayPopout -= RuleCategoryController_SetupGridVoteDisplayPopout;
            On.RoR2.RemoteGameDetailsPanelController.SetGameInfoInternal -= RemoteGameDetailsPanelController_SetGameInfoInternal;
            On.RoR2.UI.KarmaIcon.OnEnable -= KarmaIcon_OnEnable;
            On.RoR2.UI.GameEndReportPanelController.SetDisplayData -= GameEndReportPanelController_SetDisplayData;
        }

        private static void Run_onRunDestroyGlobal(Run run)
        {
            KarmaLock.OnRunDestroyServer();
        }

        #region Gameplay

        // Clamp the final value, after multipliers, including initialization and restored saves.
        private static void KarmaManager_SetKarmaServer(On.RoR2.KarmaManager.orig_SetKarmaServer orig, KarmaManager self, int value)
        {
            if (NetworkServer.active && Run.instance && KarmaLock.RunCap >= 0)
            {
                value = Math.Min(value, KarmaLock.RunCap);
            }
            orig(self, value);
        }

        // Every Karma increase (triggers, multipliers) goes through ModifyKarma on the host. The starting
        // level is set with SetKarmaServer directly, so blocking here keeps Karma where the run started.
        // Karma is a SyncVar, so every client (with or without the mod) sees the unchanged value.
        private static void KarmaManager_ModifyKarma(On.RoR2.KarmaManager.orig_ModifyKarma orig, KarmaManager self, int amount)
        {
            if (NetworkServer.active && KarmaLock.IsRunLocked)
            {
                Log.LogDebug($"Karma is locked at {KarmaLock.RunLevel}; ignoring change of {amount}.");
                return;
            }
            orig(self, amount);
        }

        #endregion

        #region Lobby input

        // First click on a Karma level votes for it (vanilla). Clicking the level you voted for again
        // toggles the lock instead of withdrawing the vote. Picking anything else in the Karma rule unlocks.
        // Without a modded host this does nothing and the click behaves as in vanilla.
        private static void RuleChoiceController_OnClick(On.RoR2.UI.RuleChoiceController.orig_OnClick orig, RuleChoiceController self)
        {
            var choice = ruleChoiceDefField?.GetValue(self) as RuleChoiceDef;
            if (!KarmaLock.CanLock || !KarmaLock.IsKarmaChoice(choice) || !self.canVote)
            {
                orig(self);
                return;
            }

            var networkUser = KarmaLock.NetworkUserForCurrentEventSystem();
            var voteController = networkUser ? PreGameRuleVoteController.FindForUser(networkUser) : null;
            if (!voteController)
            {
                orig(self);
                return;
            }

            int level = KarmaLock.LevelOf(choice);
            if (KarmaLock.LobbyCap >= 0 && level > KarmaLock.LobbyCap)
            {
                Log.LogInfo("The starting Karma cannot exceed the cap. Remove or raise the cap first.");
                return;
            }
            if (!self.cycleThroughOptions && level >= 0 && voteController.IsChoiceVoted(choice))
            {
                KarmaLock.RequestLobbyLevel(networkUser, KarmaLock.LobbyLevel == level ? KarmaLock.Unlocked : level);
                return;
            }

            orig(self);
            if (KarmaLock.LobbyLevel >= 0)
            {
                KarmaLock.RequestLobbyLevel(networkUser, KarmaLock.Unlocked);
            }
        }

        private static void MPButton_OnPointerClick(On.RoR2.UI.MPButton.orig_OnPointerClick orig, MPButton self, PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Right && !self.disablePointerClick
                && self.IsInteractable() && self.InputModuleIsAllowed(eventData.currentInputModule))
            {
                var controller = self.GetComponentInParent<RuleChoiceController>();
                var badge = controller ? controller.GetComponent<RuleChoiceLockBadge>() : null;
                if (badge && controller.hgButton == self && badge.ShowClickHint)
                {
                    badge.ToggleCap(self.eventSystem?.localUser?.currentNetworkUser);
                    return;
                }
            }
            orig(self, eventData);
        }

        // The "random" button can change the Karma vote; a lock set for the old level shouldn't carry over.
        private static void RuleCategoryController_SetRandomVotes(On.RoR2.UI.RuleCategoryController.orig_SetRandomVotes orig, RuleCategoryController self)
        {
            var networkUser = KarmaLock.NetworkUserForCurrentEventSystem();
            var voteController = networkUser ? PreGameRuleVoteController.FindForUser(networkUser) : null;
            var before = VotedKarmaChoice(voteController);
            orig(self);
            if (KarmaLock.LobbyLevel >= 0 && voteController && VotedKarmaChoice(voteController) != before)
            {
                KarmaLock.RequestLobbyLevel(networkUser, KarmaLock.Unlocked);
            }
        }

        private static RuleChoiceDef VotedKarmaChoice(PreGameRuleVoteController voteController)
        {
            if (!voteController || KarmaLock.KarmaRuleDef == null)
            {
                return null;
            }
            foreach (var choice in KarmaLock.KarmaRuleDef.choices)
            {
                if (voteController.IsChoiceVoted(choice))
                {
                    return choice;
                }
            }
            return null;
        }

        #endregion

        #region Icons

        private static void RuleChoiceController_SetChoice(On.RoR2.UI.RuleChoiceController.orig_SetChoice orig, RuleChoiceController self, RuleChoiceDef newChoiceDef)
        {
            orig(self, newChoiceDef);

            var badge = self.GetComponent<RuleChoiceLockBadge>();
            if (!badge)
            {
                if (!KarmaLock.IsKarmaLevelChoice(newChoiceDef))
                {
                    return;
                }
                badge = RuleChoiceLockBadge.For(self);
            }

            badge.Choice = newChoiceDef;
            // SetChoice is also called from OnClick (no context); keep what the UI set up then.
            if (setChoiceSource != null)
            {
                badge.Source = setChoiceSource;
                badge.ShowClickHint = setChoiceIsVoteButton;
                badge.SuppressCap = setChoiceSource == NeverLocked;
            }
        }

        // Rule summary grid in the lobby: shows the resolved rule book.
        private static void RuleCategoryController_SetupGridVoteDisplay(On.RoR2.UI.RuleCategoryController.orig_SetupGridVoteDisplay orig, RuleCategoryController self, RuleCategoryDef categoryDef, RuleChoiceMask availability, RuleBook ruleBook)
        {
            Func<RuleChoiceDef, bool> source = choice => KarmaLock.IsLobbyLockActive(ruleBook) && ruleBook.GetRuleChoice(KarmaLock.KarmaRuleDef) == choice;
            WithSource(source, false, () => orig(self, categoryDef, availability, ruleBook));
        }

        // "Edit" popout with one button per Karma level: these are what the player clicks.
        private static void RuleCategoryController_SetupGridVoteDisplayPopout(On.RoR2.UI.RuleCategoryController.orig_SetupGridVoteDisplayPopout orig, RuleCategoryController self, RuleCategoryDef categoryDef, RuleChoiceMask availability, RuleBook ruleBook)
        {
            WithSource(LobbyButtonSource, true, () => orig(self, categoryDef, availability, ruleBook));
        }

        private static void RemoteGameDetailsPanelController_SetGameInfoInternal(On.RoR2.RemoteGameDetailsPanelController.orig_SetGameInfoInternal orig, RemoteGameDetailsPanelController self, ref RemoteGameInfo newGameInfo)
        {
            var info = newGameInfo;
            WithSource(NeverLocked, false, () => orig(self, ref info));
            newGameInfo = info;
        }

        private static void KarmaIcon_OnEnable(On.RoR2.UI.KarmaIcon.orig_OnEnable orig, KarmaIcon self)
        {
            // Attach first: vanilla OnEnable throws when there is no KarmaManager (e.g. outside a run).
            KarmaIconLockBadge.For(self);
            orig(self);
        }

        // The end-of-run report is shown before the run is destroyed, so the run's lock state is still known.
        // Old runs from the run history have no lock information (vanilla hides their Karma icon anyway).
        private static void GameEndReportPanelController_SetDisplayData(On.RoR2.UI.GameEndReportPanelController.orig_SetDisplayData orig, GameEndReportPanelController self, GameEndReportPanelController.DisplayData newDisplayData)
        {
            orig(self, newDisplayData);
            if (self.karmaEarnedIcon)
            {
                KarmaIconLockBadge.For(self.karmaEarnedIcon).SetLockedOverride(KarmaLock.IsRunLocked, KarmaLock.RunCap);
            }
        }

        private static void WithSource(Func<RuleChoiceDef, bool> source, bool isVoteButton, Action action)
        {
            var previousSource = setChoiceSource;
            var previousIsVoteButton = setChoiceIsVoteButton;
            setChoiceSource = source;
            setChoiceIsVoteButton = isVoteButton;
            try
            {
                action();
            }
            finally
            {
                setChoiceSource = previousSource;
                setChoiceIsVoteButton = previousIsVoteButton;
            }
        }

        #endregion
    }
}
