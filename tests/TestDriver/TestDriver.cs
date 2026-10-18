using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using BepInEx;
using RoR2;
using RoR2.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using Path = System.IO.Path;

namespace KarmaLocking.TestDriver
{
    // Not shipped. Drives a singleplayer session through the KarmaLocking scenarios, checks the results,
    // takes screenshots into BepInEx/kltest/ and quits the game. Results: BepInEx/kltest/results.txt
    [BepInDependency("com.elevated.KarmaLocking")]
    [BepInPlugin("com.elevated.KarmaLocking.TestDriver", "KarmaLockingTestDriver", "0.0.1")]
    public class TestDriver : BaseUnityPlugin
    {
        private const string Hint = "Click the selected level again to lock Karma at it";

        private string outDir;
        private readonly StringBuilder results = new StringBuilder();
        private int passed, failed;

        private static Type karmaLockType;

        public void Awake()
        {
            outDir = Path.Combine(Paths.BepInExRootPath, "kltest");
            Directory.CreateDirectory(outDir);
            File.Delete(Path.Combine(outDir, "results.txt"));
            karmaLockType = Type.GetType("KarmaLocking.KarmaLock, KarmaLocking");
            StartCoroutine(RunAll());
            Application.logMessageReceived += OnLog;
        }

        private bool finished;
        private float deadline = 900f;

        // A crash inside the coroutine stops it silently; report it and quit instead of hanging.
        private void OnLog(string condition, string stackTrace, LogType type)
        {
            if (!finished && type == LogType.Exception && stackTrace.Contains("KarmaLocking"))
            {
                Check("no exception", false, condition + " @ " + stackTrace.Split('\n')[0]);
                if (stackTrace.Contains("TestDriver"))
                {
                    StartCoroutine(Finish());
                }
            }
        }

        public void Update()
        {
            if (!finished && Time.realtimeSinceStartup > deadline)
            {
                Check("finished within time limit", false);
                StartCoroutine(Finish());
            }
        }

        #region Helpers

        private void Note(string text)
        {
            Logger.LogInfo("[KLTEST] " + text);
            results.AppendLine("      " + text);
            Flush();
        }

        private void Check(string name, bool ok, string detail = "")
        {
            if (ok) passed++; else failed++;
            string line = $"[{(ok ? "PASS" : "FAIL")}] {name}{(detail.Length > 0 ? " -- " + detail : "")}";
            Logger.LogInfo("[KLTEST] " + line);
            results.AppendLine(line);
            Flush();
        }

        private void Flush()
        {
            File.WriteAllText(Path.Combine(outDir, "results.txt"), results.ToString() + $"\n(passed {passed}, failed {failed})\n");
        }

        private IEnumerator Shot(string name)
        {
            yield return new WaitForSecondsRealtime(0.5f);
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(Path.Combine(outDir, name + ".png"));
            yield return new WaitForSecondsRealtime(0.5f);
            Note("screenshot " + name + ".png");
        }

        private IEnumerator WaitFor(Func<bool> condition, float timeout, string what)
        {
            float end = Time.realtimeSinceStartup + timeout;
            while (Time.realtimeSinceStartup < end)
            {
                bool ok = false;
                try { ok = condition(); } catch { }
                if (ok) { Note("ready: " + what); yield break; }
                yield return null;
            }
            Check("wait for " + what, false, $"timed out after {timeout}s");
        }

        private static object KL(string property)
        {
            return karmaLockType.GetProperty(property, BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public).GetValue(null);
        }

        private static int LobbyLevel => (int)KL("LobbyLevel");
        private static int RunLevel => (int)KL("RunLevel");
        private static RuleDef KarmaRule => RuleCatalog.FindRuleDef("Karma.InitialLevel");

        private static T Field<T>(object obj, string name)
        {
            return (T)obj.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).GetValue(obj);
        }

        private static RuleChoiceDef ChoiceOf(RuleChoiceController c) => Field<RuleChoiceDef>(c, "choiceDef");

        private static bool BadgeShown(Component host)
        {
            return host && host.GetComponentsInChildren<Transform>(true).Any(t => t.name == "KarmaLockBadge" && t.gameObject.activeInHierarchy);
        }

        private static LocalUser FirstUser => LocalUserManager.GetFirstLocalUser();

        private static PreGameRuleVoteController VoteController =>
            FirstUser?.currentNetworkUser ? PreGameRuleVoteController.FindForUser(FirstUser.currentNetworkUser) : null;

        private static RuleCategoryController KarmaCategoryController()
        {
            return Resources.FindObjectsOfTypeAll<RuleCategoryController>()
                .FirstOrDefault(c => c && c.gameObject.scene.IsValid() && Field<RuleCategoryDef>(c, "currentCategory") == KarmaRule.category);
        }

        private static IList<RuleChoiceController> Elements(RuleCategoryController c, string allocator)
        {
            var alloc = c.GetType().GetField(allocator, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(c);
            return (IList<RuleChoiceController>)alloc.GetType().GetField("elements").GetValue(alloc);
        }

        private static RuleChoiceController PopoutButton(int level)
        {
            var c = KarmaCategoryController();
            return c ? Elements(c, "popoutButtonIconAllocator").FirstOrDefault(b => b && LevelOf(ChoiceOf(b)) == level) : null;
        }

        private static RuleChoiceController SummaryIcon()
        {
            var c = KarmaCategoryController();
            return c ? Elements(c, "voteResultIconAllocator").FirstOrDefault(b => b && ChoiceOf(b)?.ruleDef == KarmaRule) : null;
        }

        private static int LevelOf(RuleChoiceDef choice) => choice?.ruleDef == KarmaRule && choice.extraData is int l ? l : -99;

        private static int VotedLevel()
        {
            var vc = VoteController;
            if (!vc) return -99;
            foreach (var choice in KarmaRule.choices)
            {
                if (vc.IsChoiceVoted(choice)) return LevelOf(choice);
            }
            return -98; // no vote
        }

        // What a real mouse click does: RuleChoiceController.OnClick with this user's event system current.
        private IEnumerator Click(int level)
        {
            var button = PopoutButton(level);
            if (!button)
            {
                Check($"popout button for level {level} exists", false);
                yield break;
            }
            var previous = EventSystem.current;
            EventSystem.current = FirstUser.eventSystem;
            try { button.OnClick(); }
            finally { EventSystem.current = previous; }
            yield return new WaitForSecondsRealtime(0.6f);
        }

        private void TogglePopout()
        {
            var c = KarmaCategoryController();
            c.GetType().GetMethod("TogglePopoutPanel", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(c, null);
        }

        private static int CurrentKarma => KarmaManager.instance ? KarmaManager.instance.GetEffectiveKarma() : -1;

        private static List<KarmaIcon> SceneKarmaIcons() =>
            Resources.FindObjectsOfTypeAll<KarmaIcon>().Where(k => k && k.gameObject.scene.IsValid()).ToList();

        private IEnumerator EnterLobby()
        {
            yield return WaitFor(() => PreGameController.instance && VoteController && KarmaCategoryController(), 90, "lobby");
            yield return new WaitForSecondsRealtime(3f);
        }

        private IEnumerator StartRun()
        {
            RoR2.Console.instance.SubmitCmd(null, "pregame_start_run");
            yield return WaitFor(() => Run.instance && Stage.instance && KarmaManager.instance && FirstUser.cachedBody, 120, "run + body");
            yield return new WaitForSecondsRealtime(4f);
        }

        #endregion

        private IEnumerator RunAll()
        {
            Note($"KarmaLock type found: {karmaLockType != null}");
            yield return WaitFor(() => RoR2Application.loadFinished && SceneCatalog.mostRecentSceneDef && SceneCatalog.mostRecentSceneDef.baseSceneName == "title", 300, "title screen");
            yield return new WaitForSecondsRealtime(3f);

            Note($"Karma.Lock rule present (must be false, server-side design): {RuleCatalog.FindRuleDef("Karma.Lock") != null}");
            Check("no rule added to the catalog", RuleCatalog.FindRuleDef("Karma.Lock") == null);

            RoR2.Console.instance.SubmitCmd(null, "transition_command \"gamemode ClassicRun; host 0;\"");
            yield return EnterLobby();

            // Pick a level we can vote for (unlocked on this save), below 8 so an increase is visible.
            var mask = PreGameController.instance.resolvedRuleChoiceMask;
            var available = KarmaRule.choices.Where(c => c.extraData is int l && l >= 0 && mask[c.globalIndex]).Select(c => (int)c.extraData).ToList();
            Note("available karma levels: " + string.Join(",", available));
            int L = available.Where(l => l < 8).DefaultIfEmpty(-1).Max();
            int M = available.Where(l => l < 8 && l != L).DefaultIfEmpty(-1).Max();
            Note($"testing with L={L}, M={M}");
            if (L < 0)
            {
                Check("an unlocked Karma level below 8 exists", false);
                yield return Finish();
                yield break;
            }

            TogglePopout();
            yield return new WaitForSecondsRealtime(1f);
            yield return Shot("01_lobby_popout_initial");
            Note($"initial: voted={VotedLevel()} lobbyLevel={LobbyLevel}");

            // If L is already our vote (persisted ballot), move away first so click 1 is a fresh selection.
            if (VotedLevel() == L)
            {
                yield return Click(M >= 0 ? M : -1);
                if (VotedLevel() == L) { yield return Click(L); } // withdraw if there was no other level
            }

            yield return Click(L);
            Check("click 1: level selected", VotedLevel() == L, $"voted={VotedLevel()}");
            Check("click 1: not locked", LobbyLevel == -1, $"lobbyLevel={LobbyLevel}");
            Check("click 1: no badge on button", !BadgeShown(PopoutButton(L)));

            yield return Click(L);
            Check("click 2: still selected", VotedLevel() == L, $"voted={VotedLevel()}");
            Check("click 2: locked at L", LobbyLevel == L, $"lobbyLevel={LobbyLevel}");
            Check("click 2: badge on popout button L", BadgeShown(PopoutButton(L)));
            Check("click 2: no badge on other popout buttons", available.Where(l => l != L).All(l => !BadgeShown(PopoutButton(l))));
            Check("click 2: badge on lobby summary icon", BadgeShown(SummaryIcon()), $"summary={(SummaryIcon() ? LevelOf(ChoiceOf(SummaryIcon())).ToString() : "none")}");
            var hover = PopoutButton(L).hgButton.hoverToken ?? "";
            Check("hover text has the lock hint", hover.Contains(Hint), hover.Length > 120 ? hover.Substring(hover.Length - 120) : hover);
            Check("hint appended only once", hover.IndexOf(Hint, StringComparison.Ordinal) == hover.LastIndexOf(Hint, StringComparison.Ordinal));
            yield return Shot("02_lobby_popout_locked");
            TogglePopout();
            yield return Shot("03_lobby_summary_locked");
            TogglePopout();
            yield return new WaitForSecondsRealtime(0.5f);

            yield return Click(L);
            Check("click 3: still selected", VotedLevel() == L, $"voted={VotedLevel()}");
            Check("click 3: unlocked", LobbyLevel == -1, $"lobbyLevel={LobbyLevel}");
            Check("click 3: badge gone", !BadgeShown(PopoutButton(L)) && !BadgeShown(SummaryIcon()));

            if (M >= 0)
            {
                yield return Click(L); // lock again
                yield return Click(M); // pick another level
                Check("other level: selected", VotedLevel() == M, $"voted={VotedLevel()}");
                Check("other level: unlocked", LobbyLevel == -1, $"lobbyLevel={LobbyLevel}");
                yield return Click(L); // back to L, unlocked
            }

            var disabled = KarmaRule.choices.First(c => c.extraData is int l && l < 0);
            if (mask[disabled.globalIndex])
            {
                yield return Click(L); // lock
                yield return Click(-1); // Karma Disabled
                Check("karma disabled: unlocks", LobbyLevel == -1, $"lobbyLevel={LobbyLevel} voted={VotedLevel()}");
                yield return Click(L);
            }
            Check("before unlocked run: L selected, unlocked", VotedLevel() == L && LobbyLevel == -1, $"voted={VotedLevel()} lobbyLevel={LobbyLevel}");
            TogglePopout();

            // --- Unlocked run: vanilla behaviour must be intact.
            yield return StartRun();
            Check("unlocked run: starts at L", CurrentKarma == L, $"karma={CurrentKarma}");
            Check("unlocked run: run not locked", RunLevel == -1, $"runLevel={RunLevel}");
            KarmaManager.instance.ModifyKarma(1);
            yield return new WaitForSecondsRealtime(0.5f);
            Check("unlocked run: karma rises", CurrentKarma == L + 1, $"karma={CurrentKarma}");
            var icons = SceneKarmaIcons();
            Note($"KarmaIcons in run scene: {icons.Count} ({string.Join(", ", icons.Select(i => i.name + (i.gameObject.activeInHierarchy ? "" : " [inactive]")))})");
            Check("unlocked run: no badge on karma icons", icons.All(i => !BadgeShown(i)));
            yield return Shot("04_run_unlocked");

            RoR2.Console.instance.SubmitCmd(null, "run_end");
            yield return EnterLobby();

            // --- Locked run.
            TogglePopout();
            yield return new WaitForSecondsRealtime(1f);
            Note($"back in lobby: voted={VotedLevel()} lobbyLevel={LobbyLevel}");
            if (VotedLevel() != L) yield return Click(L);
            yield return Click(L);
            Check("lobby again: locked at L", LobbyLevel == L && VotedLevel() == L, $"voted={VotedLevel()} lobbyLevel={LobbyLevel}");
            TogglePopout();

            yield return StartRun();
            Check("locked run: starts at L", CurrentKarma == L, $"karma={CurrentKarma}");
            Check("locked run: run locked at L", RunLevel == L, $"runLevel={RunLevel}");
            for (int i = 0; i < 3; i++) KarmaManager.instance.ModifyKarma(1);
            KarmaManager.instance.ModifyKarma(3);
            yield return new WaitForSecondsRealtime(0.5f);
            Check("locked run: karma does not rise", CurrentKarma == L, $"karma={CurrentKarma}");
            icons = SceneKarmaIcons();
            var visible = icons.Where(i => i.gameObject.activeInHierarchy).ToList();
            Note($"KarmaIcons in run scene: {icons.Count}, active: {visible.Count}");
            Check("locked run: badge on every active karma icon", visible.Count > 0 && visible.All(BadgeShown), $"active={visible.Count}");
            yield return Shot("05_run_locked_hud");

            // Scoreboard / pause might show karma too; open the scoreboard via the HUD flag.
            var hud = HUD.readOnlyInstanceList.FirstOrDefault();
            if (hud)
            {
                hud.scoreboardPanel?.SetActive(true);
                yield return new WaitForSecondsRealtime(1f);
                icons = SceneKarmaIcons().Where(i => i.gameObject.activeInHierarchy).ToList();
                Note($"with scoreboard open, active KarmaIcons: {icons.Count}");
                Check("scoreboard: badge on every active karma icon", icons.All(BadgeShown));
                yield return Shot("06_run_locked_scoreboard");
                hud.scoreboardPanel?.SetActive(false);
            }

            // --- End-of-run report.
            Run.instance.BeginGameOver(RoR2Content.GameEndings.StandardLoss);
            yield return WaitFor(() => FindObjectsOfType<GameEndReportPanelController>().Any(p => p.karmaEarnedIcon), 60, "game end report");
            yield return new WaitForSecondsRealtime(4f);
            var panel = FindObjectsOfType<GameEndReportPanelController>().FirstOrDefault(p => p.karmaEarnedIcon);
            if (panel)
            {
                Note($"report karma holder active: {panel.karmaEarnedHolder && panel.karmaEarnedHolder.activeInHierarchy}");
                Check("end report: badge on karma icon", BadgeShown(panel.karmaEarnedIcon));
            }
            yield return Shot("07_end_report_locked");

            yield return Finish();
        }

        private IEnumerator Finish()
        {
            if (finished) yield break;
            finished = true;
            RoR2.Console.instance.SubmitCmd(null, "karmalock_status");
            Note($"DONE passed={passed} failed={failed}");
            yield return new WaitForSecondsRealtime(2f);
            Application.Quit();
        }
    }
}
