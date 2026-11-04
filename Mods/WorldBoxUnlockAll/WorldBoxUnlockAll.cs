using System;
using System.Collections.Generic;
using NeoModLoader.api;
using UnityEngine;

namespace WorldBoxUnlockAllMod
{
    /// <summary>
    /// Unlocks the content discoverable through the *local* WorldBox
    /// progression system. It does NOT touch premium ownership, purchase
    /// checks, online accounts or Steam achievement APIs.
    ///
    /// WorldBox 0.51.2, build 719, NeoModLoader 1.2.0.1.
    /// </summary>
    public sealed class WorldBoxUnlockAll : BasicMod<WorldBoxUnlockAll>
    {
        private static bool _finished;
        private static float _nextAttempt;
        private static float _nextRefresh;
        private static int _failures;
        private static int _lastAchievements;
        private static int _lastTraits;
        private static int _lastEditorTraits;

        protected override void OnModLoad()
        {
            _finished = false;
            _nextAttempt = 0f;
            _nextRefresh = 0f;
            LogInfo("Desbloquear Todo loaded. Waiting for local game progress and assets.");
            LogInfo("Only in-game achievements/discoveries/editor content. " +
                    "Premium license checks and Steam achievements remain untouched.");
        }

        private void Update()
        {
            if (Time.unscaledTime < _nextAttempt)
                return;

            // Assets can be initialized after NML loads mods; never run
            // against the partially initialized libraries or write a bad
            // progress file while a profile is still being loaded.
            if (GameProgress.instance == null ||
                GameProgress.instance.data == null ||
                GameProgress.instance.data.achievements == null ||
                AssetManager.achievements == null ||
                AssetManager.achievements.list == null ||
                AssetManager.achievements.list.Count == 0 ||
                AssetManager.traits == null ||
                AssetManager.traits.list == null ||
                AssetManager.traits.list.Count == 0 ||
                AssetManager.actor_library == null ||
                AssetManager.actor_library.list == null ||
                AssetManager.actor_library.list.Count == 0)
            {
                _nextAttempt = Time.unscaledTime + 2f;
                return;
            }

            if (_finished)
            {
                // Register new vanilla or mod-provided traits added after
                // initial load, but without continuously saving progress.
                if (Time.unscaledTime < _nextRefresh)
                    return;

                _nextAttempt = Time.unscaledTime + 15f;
                _nextRefresh = Time.unscaledTime + 15f;

                try
                {
                    ExposeEditorTraits();
                }
                catch (Exception ex)
                {
                    if (_failures++ < 3)
                        LogWarning("Editor trait refresh: " + ex.Message);
                }
                return;
            }

            _nextAttempt = Time.unscaledTime + 5f;
            try
            {
                UnlockLocalProgress();
                _finished = true;
                _nextRefresh = Time.unscaledTime + 15f;

                LogInfo("ALL LOCAL CONTENT UNLOCKED. Achievements added=" +
                        _lastAchievements +
                        "; total actor traits=" + _lastTraits +
                        "; editor traits exposed=" + _lastEditorTraits +
                        ". No premium/Steam entitlement modified.");
            }
            catch (Exception ex)
            {
                if (_failures++ < 5)
                    LogWarning("Unlock retry " + _failures + ": " +
                               ex.GetType().Name + ": " + ex.Message);

                // Native debugUnlockAll normally saves at the end; if a
                // third-party asset unexpectedly throws, keep any legitimate
                // progress already collected safe.
                try { GameProgress.saveData(); }
                catch (Exception) { }
            }
        }

        private static void UnlockLocalProgress()
        {
            GameProgressData data = GameProgress.instance.data;

            // Unlock each *local* achievement once. Do NOT call
            // AchievementLibrary.unlock, which invokes SteamAchievements,
            // popups and analytics. This changes only WorldBox's own
            // worldboxProgress file.
            int added = 0;
            foreach (Achievement achievement in AssetManager.achievements.list)
            {
                if (achievement == null ||
                    string.IsNullOrEmpty(achievement.id))
                    continue;

                if (data.achievements.Add(achievement.id))
                    added++;
            }
            _lastAchievements = added;

            // Use the game's original native implementation to unlock
            // its supported discoverable data categories:
            // actor/culture/language/subspecies/clan/religion/kingdom traits,
            // equipment, genes, creature encyclopedia and plots.
            //
            // This function persists all categories with ONE save at end.
            GameProgress.instance.debugUnlockAll();

            // Reveal the game's own trait editor entries, including
            // formerly hidden achievement-bound and explorer traits.
            // Keep mod-internal fusion stat bookkeeping traits hidden.
            _lastEditorTraits = ExposeEditorTraits();
            _lastTraits = AssetManager.traits.list.Count;
            GameProgress.saveData();
        }

        private static int ExposeEditorTraits()
        {
            if (AssetManager.traits == null || AssetManager.traits.list == null)
                return 0;

            int changed = 0;
            foreach (ActorTrait trait in AssetManager.traits.list)
            {
                if (trait == null || string.IsNullOrEmpty(trait.id))
                    continue;

                // These are not usable character traits; they are internal
                // save-format stat-bank entries for the Fusion mod.
                // Exposing them as "God Powers" could corrupt balance.
                if (trait.id.StartsWith("eternal_fusion_bonus_",
                                       StringComparison.Ordinal) ||
                    trait.id.StartsWith("eternal_fusion_power_",
                                       StringComparison.Ordinal) ||
                    trait.id == "eternal_fusion")
                    continue;

                if (!trait.can_be_given)
                {
                    trait.can_be_given = true;
                    changed++;
                }

                if (!trait.show_in_meta_editor)
                {
                    trait.show_in_meta_editor = true;
                    changed++;
                }

                // Preserve all trait semantics: no changes to inherited
                // rates, effects, sizes, mutations or requirements.
            }
            return changed;
        }
    }
}