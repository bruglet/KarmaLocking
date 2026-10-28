using System;
using RoR2;
using RoR2.UI;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace KarmaLocking
{
    // Adds a small lock image to the bottom right corner of an icon and keeps its visibility
    // in sync every frame. Lives on the host object so it keeps updating while the badge is hidden.
    internal abstract class LockBadgeBase : MonoBehaviour
    {
        // Badge rect relative to the icon: bottom right corner, a bit under half the icon's size,
        // nudged slightly outside the edge so it doesn't cover the icon's centre.
        private static readonly Vector2 AnchorMin = new Vector2(0.56f, -0.04f);
        private static readonly Vector2 AnchorMax = new Vector2(1.04f, 0.44f);

        private GameObject badge;
        private TextMeshProUGUI capBadge;

        protected abstract Transform BadgeParent { get; }

        protected abstract bool ShouldShow();

        protected virtual int CapLevel => KarmaLock.Unlocked;

        protected void LateUpdate()
        {
            bool show;
            try
            {
                show = ShouldShow();
            }
            catch (Exception e)
            {
                KarmaLockingPlugin.Log.LogDebug($"Lock badge check failed: {e.Message}");
                show = false;
            }

            if (show && !badge)
            {
                badge = CreateBadge(BadgeParent);
            }
            if (badge && badge.activeSelf != show)
            {
                badge.SetActive(show);
            }

            int cap = CapLevel;
            if (cap >= 0 && !capBadge)
            {
                capBadge = CreateCapBadge(BadgeParent);
            }
            if (capBadge)
            {
                if (capBadge.gameObject.activeSelf != (cap >= 0))
                {
                    capBadge.gameObject.SetActive(cap >= 0);
                }
                if (cap >= 0 && capBadge.text != $"≤{cap}")
                {
                    capBadge.text = $"≤{cap}";
                }
            }

            OnLateUpdate();
        }

        protected virtual void OnLateUpdate()
        {
        }

        protected void OnDestroy()
        {
            if (badge)
            {
                Destroy(badge);
            }
            if (capBadge)
            {
                Destroy(capBadge.gameObject);
            }
        }

        private static TextMeshProUGUI CreateCapBadge(Transform parent)
        {
            if (!parent)
            {
                return null;
            }
            var go = new GameObject("KarmaCapBadge", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI), typeof(LayoutElement));
            go.layer = parent.gameObject.layer;
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = AnchorMin;
            rect.anchorMax = AnchorMax;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            go.GetComponent<LayoutElement>().ignoreLayout = true;
            var text = go.GetComponent<TextMeshProUGUI>();
            text.raycastTarget = false;
            text.enableAutoSizing = true;
            text.fontSizeMin = 6;
            text.fontSizeMax = 20;
            text.fontStyle = FontStyles.Bold;
            text.alignment = TextAlignmentOptions.Center;
            text.color = new Color(1f, 0.8f, 0.2f);
            return text;
        }

        private static GameObject CreateBadge(Transform parent)
        {
            if (!parent || !LockSprite.Sprite)
            {
                return null;
            }

            var go = new GameObject("KarmaLockBadge", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(LayoutElement));
            go.layer = parent.gameObject.layer;

            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = AnchorMin;
            rect.anchorMax = AnchorMax;
            rect.pivot = new Vector2(1f, 0f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.SetAsLastSibling();

            // Don't let layout groups on the icon move or resize the badge.
            go.GetComponent<LayoutElement>().ignoreLayout = true;

            var image = go.GetComponent<Image>();
            image.sprite = LockSprite.Sprite;
            image.preserveAspect = true;
            image.raycastTarget = false;

            return go;
        }
    }

    // Lobby rule buttons, the lobby/pause rule summary and the server browser's rule list.
    internal sealed class RuleChoiceLockBadge : LockBadgeBase
    {
        private RuleChoiceController controller;
        private string lastClickHint;

        // The choice this button currently shows.
        internal RuleChoiceDef Choice { get; set; }

        // Decides whether Choice is shown as locked. Null means "use the current lobby/run".
        internal Func<RuleChoiceDef, bool> Source { get; set; }

        // Lobby popout buttons: add a "click again to lock" line to the hover description.
        internal bool ShowClickHint { get; set; }
        internal bool SuppressCap { get; set; }

        protected override int CapLevel
        {
            get
            {
                if (SuppressCap || !KarmaLock.IsKarmaLevelChoice(Choice))
                {
                    return KarmaLock.Unlocked;
                }
                int cap = PreGameController.instance ? KarmaLock.LobbyCap : KarmaLock.RunCap;
                return !ShowClickHint || KarmaLock.LevelOf(Choice) == cap ? cap : KarmaLock.Unlocked;
            }
        }

        internal void ToggleCap(NetworkUser sender)
        {
            if (!ShowClickHint || !PreGameController.instance || !KarmaLock.CanLock || !controller.canVote
                || controller.cycleThroughOptions || !KarmaLock.IsKarmaLevelChoice(Choice))
            {
                return;
            }
            int level = KarmaLock.LevelOf(Choice);
            KarmaLock.RequestLobbyLevel(sender, KarmaLock.LobbyCap == level ? KarmaLock.Unlocked : level, true);
        }

        internal static RuleChoiceLockBadge For(RuleChoiceController controller)
        {
            var badge = controller.GetComponent<RuleChoiceLockBadge>();
            if (!badge)
            {
                badge = controller.gameObject.AddComponent<RuleChoiceLockBadge>();
                badge.controller = controller;
            }
            return badge;
        }

        protected override Transform BadgeParent
        {
            get
            {
                if (controller.image)
                {
                    return controller.image.transform;
                }
                return controller.asyncImage ? controller.asyncImage.transform : controller.transform;
            }
        }

        protected override bool ShouldShow()
        {
            if (!KarmaLock.IsKarmaLevelChoice(Choice))
            {
                return false;
            }
            return Source != null ? Source(Choice) : IsLockedHere(Choice);
        }

        // No UI context: the lobby's resolved Karma choice while in the lobby, otherwise the run's.
        private static bool IsLockedHere(RuleChoiceDef choice)
        {
            if (PreGameController.instance)
            {
                var ruleBook = PreGameController.instance.readOnlyRuleBook;
                return KarmaLock.IsLobbyLockActive(ruleBook) && ruleBook.GetRuleChoice(KarmaLock.KarmaRuleDef) == choice;
            }
            return KarmaLock.IsRunLocked && KarmaLock.LevelOf(choice) == KarmaLock.RunLevel;
        }

        // The vanilla hover text is rebuilt whenever the button's choice changes (and first in Start),
        // so re-append the hint whenever it's missing.
        protected override void OnLateUpdate()
        {
            if (!ShowClickHint || !KarmaLock.CanLock || !KarmaLock.IsKarmaLevelChoice(Choice))
            {
                return;
            }
            var button = controller.hgButton;
            if (!button)
            {
                return;
            }
            var eventSystem = button.eventSystem;
            if (eventSystem && eventSystem.currentInputSource == MPEventSystem.InputSource.Gamepad
                && eventSystem.currentSelectedGameObject == button.gameObject && button.IsInteractable()
                && !button.disableGamepadClick
                && (!button.requiredTopLayer || button.requiredTopLayer.representsTopLayer)
                && !InputMapperHelper.anyRemapperListening && eventSystem.player.GetButtonDown("UISubmitTertiary"))
            {
                ToggleCap(eventSystem.localUser?.currentNetworkUser);
            }
            if (!button.updateTextOnHover || string.IsNullOrEmpty(button.hoverToken))
            {
                return;
            }
            string hint = clickHint;
            if (eventSystem && eventSystem.currentInputSource == MPEventSystem.InputSource.Gamepad)
            {
                string glyph = Glyphs.GetGlyphString(eventSystem, "UISubmitTertiary", Rewired.AxisRange.Full);
                hint += $"\n<style=cSub>{glyph}: set/remove the Karma cap on this level.</style>";
            }
            if (lastClickHint == hint && button.hoverToken.Contains(hint))
            {
                return;
            }
            if (lastClickHint != null)
            {
                button.hoverToken = button.hoverToken.Replace("\n\n" + lastClickHint, "");
            }
            button.hoverToken += "\n\n" + hint;
            lastClickHint = hint;
        }

        private const string clickHint = "<style=cSub>Click the selected level again to lock Karma at it. Click once more to unlock. Right-click a level to set/remove its Karma cap. The cap cannot be below the starting level.</style>";
    }

    // In-run Karma icons (HUD etc.) and the end-of-run report.
    internal sealed class KarmaIconLockBadge : LockBadgeBase
    {
        private KarmaIcon icon;
        private bool? lockedOverride;
        private int? capOverride;

        internal static KarmaIconLockBadge For(KarmaIcon icon)
        {
            var badge = icon.GetComponent<KarmaIconLockBadge>();
            if (!badge)
            {
                badge = icon.gameObject.AddComponent<KarmaIconLockBadge>();
                badge.icon = icon;
            }
            return badge;
        }

        // The run report keeps showing a run's state after it ends, so it gets a snapshot.
        internal void SetLockedOverride(bool locked, int cap)
        {
            lockedOverride = locked;
            capOverride = cap;
        }

        protected override int CapLevel => capOverride ?? (Run.instance ? KarmaLock.RunCap : KarmaLock.Unlocked);

        protected override Transform BadgeParent => icon.image ? icon.image.transform : icon.transform;

        protected override bool ShouldShow()
        {
            return lockedOverride ?? KarmaLock.IsRunLocked;
        }
    }
}
