using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using HarmonyLib;
using NeoModLoader.api;

namespace EternalImmortalMod
{
    /// <summary>
    /// Enhances the vanilla "immortal" trait. A creature must already possess it.
    /// WorldBox 0.51.2 (build 719), NeoModLoader 1.2.0.1.
    /// </summary>
    public partial class EternalImmortal : BasicMod<EternalImmortal>
    {
        private static Harmony _harmony;
        private static int _scanIndex;
        private static bool _traitConfigured;

        // There is only one eternal man and one eternal woman in each world.
        // Excess carriers of the same sex are LITERALLY fused into one actor.
        // We retain the primary Actor object, so its kingdom, city, king title,
        // family, owner and other world references survive the fusion.
        private const string FusionTraitID = "eternal_fusion";
        private const string FusionTierPrefix = "eternal_fusion_power_";
        private const int MaxFusionPowerTier = 20;
        // Native trait bit-bank persists actual donated attributes across saves.
        // Health, lifespan and scale are NOT transferred. No extra HP is
        // meaningful when eternal actors already show ∞ / ∞.
        private const string FusionStatPrefix = "eternal_fusion_bonus_";
        private const int FusionStatBits = 20;
        private static readonly string[] FusionStatNames = {
            "damage", "armor", "speed", "attack_speed",
            "critical_chance", "critical_damage_multiplier",
            "intelligence", "warfare", "diplomacy", "stewardship",
            "stamina", "mana"
        };
        private static readonly float[] FusionStatSteps = {
            1f, 1f, .25f, .25f, .01f, .1f,
            1f, 1f, 1f, 1f, 1f, 1f
        };
        private static readonly float[] FusionStatMaxBonuses = {
            1000000f, 100000f, 60f, 35f, 100f, 1000f,
            100000f, 100000f, 100000f, 100000f, 100000f, 100000f
        };
        private static readonly FieldInfo ActualStatsField =
            AccessTools.Field(typeof(BaseSimObject), "stats");
        private static Actor _fusionAbsorbing;
        private static Actor _malePrimary;
        private static Actor _femalePrimary;
        private static MapBox _fusionWorld;
        private static float _nextFusionReview;
        private static int _fusionErrors;
        private static readonly FieldInfo ActorDataField =
            AccessTools.Field(typeof(Actor), "data");
        private static readonly MethodInfo ActorNameSetter =
            AccessTools.PropertySetter(typeof(Actor), "name");
        private static bool _fusionTraitsInitialized;

        private static readonly HashSet<string> HarmfulStatuses =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "burning", "burn", "fire", "on_fire", "poison", "poisoned",
                "plague", "infected", "infection", "tumor", "tumor_infection",
                "madness", "mad", "insanity", "drowning", "frozen", "freeze",
                "stunned", "stun", "paralyzed", "paralysis", "cursed", "curse",
                "zombie", "bleeding", "disease", "sick", "petrified",
                "strange_urge", "possessed", "tantrum", "confused", "confusion",
                "slow", "weak", "injured", "crippled"
            };

        protected override void OnModLoad()
        {
            if (_harmony == null)
            {
                _harmony = new Harmony("eternalimmortal.worldbox.nml1201");
                _harmony.PatchAll(typeof(EternalImmortal).Assembly);
            }
            ConfigureVanillaTrait();
            ConfigureFusionTraits();
            RegisterImmortalMagnetPower();
            LogInfo("Eternal Immortal enabled: immortal actors resist death, damage, harmful traits and statuses. One male + one female; extras literally fuse without size changes.");
        }

        private static void ConfigureVanillaTrait()
        {
            if (_traitConfigured || AssetManager.traits == null)
                return;
            ActorTrait asset = AssetManager.traits.get("immortal");
            if (asset == null)
                return;
            // Make only the vanilla immortal trait usable without achievementTheKing.
            asset.unlocked_with_achievement = false;
            asset.needs_to_be_explored = false;
            asset.can_be_given = true;
            asset.show_in_meta_editor = true;
            // Prevent inheritance through either WorldBox baby-trait path.
            // BabyHelper.traitsClone and addTraitsFromParentToList both
            // use rate_birth and rate_inherit.
            asset.rate_birth = 0;
            asset.rate_inherit = 0;
            asset.can_be_cured = false;
            asset.can_be_removed_by_divine_light = false;
            asset.can_be_removed_by_accelerated_healing = false;
            asset.remove_for_zombie_actor_asset = false;
            _traitConfigured = true;
        }

        // Dedicated, non-hereditary trait showing the result of a REAL fusion.
        // Higher power levels remain saved as native WorldBox traits, so their
        // bonuses are retained when the world is reloaded.
        private static void ConfigureFusionTraits()
        {
            if (_fusionTraitsInitialized || AssetManager.traits == null)
                return;

            RegisterFusionTrait(FusionTraitID, 0, true);
            for (int tier = 1; tier <= MaxFusionPowerTier; tier++)
                RegisterFusionTrait(FusionTierPrefix + tier, tier, false);
            for (int i = 0; i < FusionStatNames.Length; i++)
                for (int bit = 0; bit < FusionStatBits; bit++)
                    RegisterFusionBonus(i, bit);

            _fusionTraitsInitialized = true;
            LogInfo("Fusion trait, rank markers and persistent non-health stat bank registered.");
        }

        private static void RegisterFusionTrait(string id, int tier, bool visible)
        {
            if (AssetManager.traits.get(id) != null)
                return;

            ActorTrait trait = new ActorTrait
            {
                id = id,
                path_icon = "ui/Icons/actor_traits/iconBlessing",
                rate_birth = 0,
                rate_inherit = 0,
                type = TraitType.Positive,
                unlocked_with_achievement = false,
                needs_to_be_explored = false,
                show_in_meta_editor = false,
                can_be_given = false,
                can_be_cured = false,
                can_be_removed_by_divine_light = false,
                can_be_removed_by_accelerated_healing = false
            };

            // WorldBox initializes base_stats in AssetLibrary.add(), not in
            // the ActorTrait constructor. Add the trait BEFORE setting stats.
            ActorTrait registered = AssetManager.traits.add(trait);
            // Fusion rank is visual metadata only. Combat power comes from
            // the ACTUAL donor's stats, stored in bit-bank traits below.
            // Critically, no fusion trait increases health or physical scale.

            // Vanilla trait names are localized from their asset IDs.
            try
            {
                string name = visible ? "Fusión" : "Poder de Fusión " + tier;
                LocalizedTextManager.add(id, name, false, "EternalImmortal", false);
            }
            catch (Exception) { /* Optional localization; never block the game. */ }
        }

        private static string FusionBonusID(int statIndex, int bit)
        {
            return FusionStatPrefix + statIndex + "_" + bit;
        }

        // A binary set of traits stores each absorbed statistic permanently
        // in WorldBox's native saved trait list. There is no external database,
        // no unpersisted runtime bonus and no health/scale bonus.
        private static void RegisterFusionBonus(int statIndex, int bit)
        {
            string id = FusionBonusID(statIndex, bit);
            if (AssetManager.traits.get(id) != null)
                return;

            ActorTrait trait = new ActorTrait
            {
                id = id,
                path_icon = "ui/Icons/actor_traits/iconBlessing",
                type = TraitType.Positive,
                rate_birth = 0,
                rate_inherit = 0,
                unlocked_with_achievement = false,
                needs_to_be_explored = false,
                can_be_given = false,
                can_be_removed = false,
                can_be_cured = false,
                show_in_meta_editor = false,
                show_in_knowledge_window = false,
                show_for_unlockables_ui = false,
                has_locales = false,
                is_mutation_box_allowed = false,
                can_be_removed_by_divine_light = false,
                can_be_removed_by_accelerated_healing = false
            };
            ActorTrait registered = AssetManager.traits.add(trait);
            if (registered != null && registered.base_stats != null)
            {
                float value = (1 << bit) * FusionStatSteps[statIndex];
                registered.base_stats[FusionStatNames[statIndex]] = value;
            }
        }

        private static int[] CurrentFusionBonuses(Actor actor)
        {
            int[] bonus = new int[FusionStatNames.Length];
            if (actor == null || actor.traits == null)
                return bonus;

            foreach (ActorTrait t in actor.traits)
            {
                if (t == null || t.id == null ||
                    !t.id.StartsWith(FusionStatPrefix,
                                     StringComparison.Ordinal))
                    continue;
                string suffix = t.id.Substring(FusionStatPrefix.Length);
                int separator = suffix.IndexOf('_');
                if (separator <= 0)
                    continue;
                if (!int.TryParse(suffix.Substring(0, separator), out int idx) ||
                    !int.TryParse(suffix.Substring(separator + 1), out int bit) ||
                    idx < 0 || idx >= bonus.Length ||
                    bit < 0 || bit >= FusionStatBits)
                    continue;
                bonus[idx] += 1 << bit;
            }
            return bonus;
        }

        private static int[] CaptureDonorStats(Actor donor)
        {
            if (donor == null || ActualStatsField == null)
                return null;

            BaseStats live = ActualStatsField.GetValue(donor) as BaseStats;
            // Never consume a character if its statistics are unavailable.
            if (live == null)
                return null;

            int[] units = new int[FusionStatNames.Length];
            for (int i = 0; i < units.Length; i++)
            {
                float value = live.get(FusionStatNames[i]);
                if (float.IsNaN(value) || float.IsInfinity(value) ||
                    value <= 0f)
                    continue;

                // The two fastest combat attributes have safety bounds to
                // avoid runaway physics/AI movement at high fusion counts.
                double maxUnits = Math.Min((1 << FusionStatBits) - 1,
                    Math.Floor(FusionStatMaxBonuses[i] / FusionStatSteps[i]));
                double measuredUnits = Math.Round((double)value /
                                                  FusionStatSteps[i]);
                units[i] = (int)Math.Min(maxUnits,
                                        Math.Max(0d, measuredUnits));
            }
            return units;
        }

        private static void ApplyAbsorbedStats(Actor primary, int[] donated)
        {
            if (primary == null || donated == null ||
                donated.Length != FusionStatNames.Length)
                return;

            int[] current = CurrentFusionBonuses(primary);
            for (int i = 0; i < donated.Length; i++)
            {
                int cap = (int)Math.Min((1 << FusionStatBits) - 1,
                    Math.Floor(FusionStatMaxBonuses[i] /
                               FusionStatSteps[i]));
                int requested = (int)Math.Min((long)cap,
                    (long)current[i] + donated[i]);
                if (requested == current[i])
                    continue;

                for (int bit = 0; bit < FusionStatBits; bit++)
                {
                    int flag = 1 << bit;
                    bool have = (current[i] & flag) != 0;
                    bool need = (requested & flag) != 0;
                    if (have == need)
                        continue;

                    string traitID = FusionBonusID(i, bit);
                    if (need)
                        primary.addTrait(traitID, false);
                    else
                        primary.removeTrait(traitID);
                }
            }
            primary.setStatsDirty();
        }

        private static int GetFusionTier(Actor actor)
        {
            if (actor == null || actor.traits == null) return 0;
            foreach (ActorTrait trait in actor.traits)
            {
                if (trait == null || string.IsNullOrEmpty(trait.id) ||
                    !trait.id.StartsWith(FusionTierPrefix, StringComparison.Ordinal))
                    continue;
                if (int.TryParse(trait.id.Substring(FusionTierPrefix.Length),
                                 out int tier))
                    return Math.Max(0, Math.Min(MaxFusionPowerTier, tier));
            }
            return 0;
        }

        private static void SetFusionTier(Actor actor, int value)
        {
            if (actor == null || AssetManager.traits == null) return;
            int tier = Math.Max(1, Math.Min(MaxFusionPowerTier, value));
            foreach (ActorTrait t in actor.getTraits().ToArray())
            {
                if (t != null && t.id.StartsWith(FusionTierPrefix,
                                                  StringComparison.Ordinal))
                    actor.removeTrait(t);
            }
            actor.addTrait(FusionTraitID, false);
            actor.addTrait(FusionTierPrefix + tier, false);
        }

        private static Actor ChoosePrimary(List<Actor> candidates,
                                           Actor previous)
        {
            if (candidates.Count == 0) return null;
            if (previous != null && previous.isAlive() &&
                candidates.Contains(previous))
                return previous;

            // If a candidate was king, preserve the king, the kingdom and
            // its existing leadership; we never create a new actor object.
            Actor first = candidates[0];
            foreach (Actor candidate in candidates)
            {
                if (candidate.is_profession_king &&
                    !first.is_profession_king)
                {
                    first = candidate;
                    continue;
                }
                if (candidate.is_profession_king == first.is_profession_king &&
                    candidate.isAdult() && !first.isAdult())
                {
                    first = candidate;
                    continue;
                }
                if (candidate.is_profession_king == first.is_profession_king &&
                    candidate.isAdult() == first.isAdult() &&
                    candidate.hasTrait(FusionTraitID) &&
                    !first.hasTrait(FusionTraitID))
                    first = candidate;
            }
            return first;
        }

        private static void ProcessFusion()
        {
            if (World.world == null || World.world.units == null ||
                Config.worldLoading)
                return;

            if (_fusionWorld != World.world)
            {
                _fusionWorld = World.world;
                _malePrimary = null;
                _femalePrimary = null;
                _nextFusionReview = 0f;
            }

            float now = UnityEngine.Time.unscaledTime;
            if (now < _nextFusionReview) return;
            _nextFusionReview = now + 1.0f;

            List<Actor> alive = World.world.units.units_only_alive;
            if (alive == null) return;
            var men = new List<Actor>();
            var women = new List<Actor>();
            foreach (Actor actor in alive)
            {
                // Enforce the limit for every sexed carrier, including any
                // manually granted to a juvenile; adults have priority.
                if (!HasEternity(actor) || !actor.isAlive())
                    continue;
                if (actor.isSexMale())
                    men.Add(actor);
                else if (actor.isSexFemale())
                    women.Add(actor);
            }

            _malePrimary = ChoosePrimary(men, _malePrimary);
            _femalePrimary = ChoosePrimary(women, _femalePrimary);

            // Max two fusions per review to protect simulation performance.
            int completed = 0;
            foreach (Actor person in men)
            {
                if (completed >= 2) break;
                if (person != _malePrimary &&
                    FuseCharacters(_malePrimary, person))
                    completed++;
            }
            foreach (Actor person in women)
            {
                if (completed >= 2) break;
                if (person != _femalePrimary &&
                    FuseCharacters(_femalePrimary, person))
                    completed++;
            }
        }

        private static bool FuseCharacters(Actor primary, Actor donor)
        {
            if (primary == null || donor == null || primary == donor ||
                !primary.isAlive() || !donor.isAlive() ||
                !HasEternity(primary) || !HasEternity(donor))
                return false;

            if (!(primary.isSexMale() && donor.isSexMale()) &&
                !(primary.isSexFemale() && donor.isSexFemale()))
                return false;

            // Save donor information BEFORE metamorphosis removes that actor.
            string firstName = primary.getName();
            string secondName = donor.getName();
            ActorTrait[] donorTraits = donor.getTraits() == null
                ? new ActorTrait[0]
                : donor.getTraits().ToArray();
            int nextTier = 1 + GetFusionTier(primary) + GetFusionTier(donor);

            // Snapshot the donor's ACTUAL in-game combat and leadership stats
            // before native metamorphosis removes that character. Do not
            // proceed if the data is unavailable; no stats may be lost.
            int[] absorbedStats = CaptureDonorStats(donor);
            if (absorbedStats == null)
                return false;

            int donorLevel = 0;
            ActorData primaryData = null;
            if (ActorDataField != null)
            {
                primaryData = ActorDataField.GetValue(primary) as ActorData;
                ActorData donorData = ActorDataField.GetValue(donor) as ActorData;
                if (donorData != null)
                    donorLevel = donorData.level;
            }

            // Secondary character truly disappears from the world. Temporarily
            // suspend only ITS immortality protections so the game's native
            // metamorphosis removal can update cities and succession normally.
            try
            {
                _fusionAbsorbing = donor;
                donor.spawnParticle(new UnityEngine.Color(.9f, .55f, 1f, 1f));
                donor.removeByMetamorphosis();
            }
            finally
            {
                _fusionAbsorbing = null;
            }

            if (donor.isAlive()) // If native removal failed, do not duplicate stats.
                return false;

            foreach (ActorTrait inherited in donorTraits)
            {
                if (inherited == null || inherited.id == "immortal" ||
                    inherited.id == FusionTraitID ||
                    inherited.id.StartsWith(FusionTierPrefix,
                                            StringComparison.Ordinal) ||
                    inherited.id.StartsWith(FusionStatPrefix,
                                            StringComparison.Ordinal) ||
                    BadTrait(inherited) ||
                    // Do not inherit giant/tiny/other scale-changing traits:
                    // the fusion must retain the original physical size.
                    (inherited.base_stats != null &&
                     Math.Abs(inherited.base_stats["scale"]) > 0.00001f))
                    continue;

                if (!primary.hasTrait(inherited.id))
                    primary.addTrait(inherited, false);
            }

            SetFusionTier(primary, nextTier);
            ApplyAbsorbedStats(primary, absorbedStats);
            if (primaryData != null && donorLevel > primaryData.level)
                primaryData.level = donorLevel;

            // Combine names but never touch actor scale, or replace the king
            // with a newly spawned unit (which would lose his royal title).
            if (ActorNameSetter != null)
            {
                string a = string.IsNullOrWhiteSpace(firstName) ? "Uno" : firstName;
                string b = string.IsNullOrWhiteSpace(secondName) ? "Dos" : secondName;
                string combined = a + "·" + b;
                if (combined.Length > 46)
                    combined = combined.Substring(0, 46);
                ActorNameSetter.Invoke(primary, new object[] { combined });
            }

            // Health is already effectively infinite for immortals. Fusion
            // deliberately leaves HP untouched; all power goes to combat stats.
            primary.setStatsDirty();

            // The animation is a magical visual merge: ONE remaining sprite,
            // with the normal unchanged WorldBox character size.
            primary.spawnParticle(new UnityEngine.Color(.3f, .95f, 1f, 1f));
            primary.spawnParticle(new UnityEngine.Color(1f, .7f, .25f, 1f));

            LogInfo("FUSIÓN literal: " + firstName + " + " + secondName +
                    " -> " + primary.getName() +
                    "; fusion rank=" + Math.Min(nextTier, MaxFusionPowerTier) +
                    "; donated damage=" + absorbedStats[0] +
                    ", armor=" + absorbedStats[1] +
                    ", attack speed=" + absorbedStats[3] +
                    "; HP remains infinite, size unchanged, king retained=" +
                    primary.is_profession_king);
            return true;
        }

        // WorldBox's checkDefault() can regenerate inheritance odds from the
        // birth rate. Keep both chances at zero for "immortal" only.
        [HarmonyPatch(typeof(ActorTraitLibrary), "checkDefault", new Type[] { typeof(ActorTrait) })]
        private static class KeepImmortalityNonHereditary
        {
            private static void Postfix(ActorTrait pAsset)
            {
                if (pAsset == null || pAsset.id != "immortal") return;
                pAsset.rate_birth = 0;
                pAsset.rate_inherit = 0;
            }
        }

        // Keep the trait unlocked even if achievement or player progress changes.
        // This does not unlock other traits or achievements.
        [HarmonyPatch(typeof(BaseUnlockableAsset), "isAvailable")]
        private static class ImmortalAlwaysAvailable
        {
            private static void Postfix(BaseUnlockableAsset __instance, ref bool __result)
            {
                if (__instance is ActorTrait trait && trait.id == "immortal")
                    __result = true;
            }
        }

        [HarmonyPatch(typeof(BaseUnlockableAsset), "isUnlocked")]
        private static class ImmortalAlwaysUnlocked
        {
            private static void Postfix(BaseUnlockableAsset __instance, ref bool __result)
            {
                if (__instance is ActorTrait trait && trait.id == "immortal")
                    __result = true;
            }
        }

        [HarmonyPatch(typeof(BaseUnlockableAsset), "isUnlockedByPlayer")]
        private static class ImmortalAlwaysUnlockedByPlayer
        {
            private static void Postfix(BaseUnlockableAsset __instance, ref bool __result)
            {
                if (__instance is ActorTrait trait && trait.id == "immortal")
                    __result = true;
            }
        }

        /// <summary>Keep the check inexpensive: it is called in damage and AI code.</summary>
        internal static bool HasEternity(Actor actor)
        {
            return actor != null && actor.traits != null && actor.hasTrait("immortal");
        }

        internal static bool Active(Actor actor)
        {
            return !Config.worldLoading && actor != _fusionAbsorbing && HasEternity(actor);
        }

        // WorldBox stores its real HP as an int. This is the unlimited
        // logical HP exposed by the mod, without unsafe int conversions.
        public static float GetEffectiveHealth(Actor actor)
        {
            return Active(actor) ? float.PositiveInfinity :
                   (actor == null ? 0f : actor.getHealth());
        }

        internal static bool BadTrait(ActorTrait trait)
        {
            if (trait == null) return false;
            return trait.type == TraitType.Negative ||
                   trait.id == "madness" || trait.id == "plague" ||
                   trait.id == "infected" || trait.id == "tumor_infection";
        }

        private static bool BadStatusName(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            if (HarmfulStatuses.Contains(name)) return true;
            string lower = name.ToLowerInvariant();
            return lower.Contains("poison") || lower.Contains("infection") ||
                   lower.Contains("madness") || lower.Contains("sick") ||
                   lower.Contains("disease") || lower.Contains("stun") ||
                   lower.Contains("burn") || lower.Contains("curse");
        }

        internal static bool BadStatus(StatusAsset status)
        {
            return status != null && (status.affects_mind || BadStatusName(status.id));
        }

        private void Update()
        {
            // The toolbar can initialize before a playable world exists.
            MaintainImmortalMagnetUI();
            if (World.world == null || World.world.units == null || Config.worldLoading)
                return;

            ConfigureVanillaTrait();
            ConfigureFusionTraits();
            try
            {
                ProcessFusion();
            }
            catch (Exception ex)
            {
                // Keep a broken game save from generating repeated errors
                // or locking up the main thread.
                if (_fusionErrors++ == 0)
                    LogWarning("Fusion processing error, next pass delayed: " +
                               ex.GetType().Name + ": " + ex.Message);
                _nextFusionReview = UnityEngine.Time.unscaledTime + 10f;
            }
            List<Actor> alive = World.world.units.units_only_alive;
            if (alive == null || alive.Count == 0)
            {
                _scanIndex = 0;
                return;
            }

            // Bounded maintenance work prevents full-world scans every frame.
            int count = Math.Min(160, alive.Count);
            for (int i = 0; i < count; i++)
            {
                if (_scanIndex >= alive.Count) _scanIndex = 0;
                Actor actor = alive[_scanIndex++];
                if (!HasEternity(actor)) continue;
                Maintain(actor);
            }
        }

        // WorldBox's UI uses several independently updated views. The vanilla
        // health bar renders integer HP (e.g. 107) and lifespan is another icon.
        // Keep their presentation infinite without changing the game's int HP
        // or passing float.Infinity to its tween/math system.
        private static readonly FieldInfo UiActorField =
            AccessTools.Field(typeof(UnitElement), "actor");
        private static readonly FieldInfo UiHealthField =
            AccessTools.Field(typeof(UnitHealthBarElement), "_health");
        private static readonly FieldInfo UiStatsField =
            AccessTools.Field(typeof(UnitStatsElement), "_stats_icons");

        private UnitHealthBarElement[] _visibleHealthBars = new UnitHealthBarElement[0];
        private UnitStatsElement[] _visibleStats = new UnitStatsElement[0];
        private float _nextUiRefresh;

        private void LateUpdate()
        {
            // Refresh view references twice a second, not every frame.
            if (UnityEngine.Time.unscaledTime >= _nextUiRefresh)
            {
                _nextUiRefresh = UnityEngine.Time.unscaledTime + 0.5f;
                _visibleHealthBars =
                    UnityEngine.Object.FindObjectsOfType<UnitHealthBarElement>(true);
                _visibleStats =
                    UnityEngine.Object.FindObjectsOfType<UnitStatsElement>(true);
            }

            if (UiActorField == null) return;

            foreach (UnitHealthBarElement element in _visibleHealthBars)
            {
                if (element == null || !element.gameObject.activeInHierarchy) continue;
                Actor actor = UiActorField.GetValue(element) as Actor;
                if (!Active(actor) || UiHealthField == null) continue;
                StatBar bar = UiHealthField.GetValue(element) as StatBar;
                if (bar == null || bar.textField == null) continue;
                if (bar.textField.text != "\u221E / \u221E")
                    bar.textField.text = "\u221E / \u221E";
            }

            foreach (UnitStatsElement element in _visibleStats)
            {
                if (element == null || !element.gameObject.activeInHierarchy) continue;
                Actor actor = UiActorField.GetValue(element) as Actor;
                if (!Active(actor) || UiStatsField == null) continue;
                StatsIconContainer container =
                    UiStatsField.GetValue(element) as StatsIconContainer;
                if (container == null) continue;
                StatsIcon lifespan = container.getIconViaId("i_lifespan");
                if (lifespan == null || lifespan.text == null) continue;
                if (lifespan.text.text != "\u221E")
                    lifespan.text.text = "\u221E";
            }
        }

        private static void Maintain(Actor actor)
        {
            int max = actor.getMaxHealth();
            if (max > 0 && actor.getHealth() < max)
                actor.setHealth(max, true);

            // Cure pre-existing mental illnesses, infections, injuries, etc.
            var traits = actor.getTraits();
            if (traits != null)
            {
                foreach (ActorTrait trait in traits.Where(BadTrait).ToArray())
                    actor.removeTrait(trait);
            }

            var active = actor.getStatusesIds();
            if (active != null)
            {
                foreach (string statusId in active.ToArray())
                {
                    StatusAsset asset = AssetManager.status == null ? null : AssetManager.status.get(statusId);
                    if (BadStatusName(statusId) || BadStatus(asset))
                        actor.finishStatusEffect(statusId);
                }
            }
        }

        // Show infinity in the vanilla unit health panel. The backing int HP
        // remains finite and safe, while all reductions are blocked below.
        [HarmonyPatch(typeof(UnitHealthBarElement), "showContent")]
        private static class InfiniteHealthPanel
        {
            private static readonly FieldInfo ActorField = AccessTools.Field(typeof(UnitElement), "actor");
            private static readonly FieldInfo HealthBarField = AccessTools.Field(typeof(UnitHealthBarElement), "_health");
            private static readonly FieldInfo TextField = AccessTools.Field(typeof(StatBar), "textField");

            private static bool Prefix(UnitHealthBarElement __instance, ref IEnumerator __result)
            {
                Actor actor = ActorField == null ? null : ActorField.GetValue(__instance) as Actor;
                if (!float.IsPositiveInfinity(GetEffectiveHealth(actor)))
                    return true;

                __result = ShowInfiniteHealth(__instance);
                return false;
            }

            private static IEnumerator ShowInfiniteHealth(UnitHealthBarElement element)
            {
                StatBar bar = HealthBarField == null ? null : HealthBarField.GetValue(element) as StatBar;
                if (bar != null)
                {
                    // Ratio 1/1 fills the bar without performing infinity / infinity (NaN).
                    bar.setBar(1f, 1f, "", false, false, false, 0f);
                    var label = TextField == null ? null : TextField.GetValue(bar) as UnityEngine.UI.Text;
                    if (label != null) label.text = "\u221E / \u221E";
                }
                yield break;
            }
        }

        // Combat damage, starvation, disease, natural death and god powers.
        [HarmonyPatch(typeof(BaseSimObject), "changeHealth", new Type[] { typeof(int) })]
        private static class BlockHealthDecrease
        {
            private static bool Prefix(BaseSimObject __instance, int pValue)
            {
                return pValue >= 0 || !Active(__instance as Actor);
            }
        }

        [HarmonyPatch(typeof(BaseSimObject), "setHealth", new Type[] { typeof(int), typeof(bool) })]
        private static class BlockHealthSet
        {
            private static bool Prefix(BaseSimObject __instance, int pValue)
            {
                Actor actor = __instance as Actor;
                return !Active(actor) || pValue >= actor.getHealth();
            }
        }

        [HarmonyPatch(typeof(Actor), "get_is_invincible")]
        private static class AlwaysInvincible
        {
            private static void Postfix(Actor __instance, ref bool __result)
            {
                if (Active(__instance)) __result = true;
            }
        }

        [HarmonyPatch(typeof(Actor), "die")]
        [HarmonyPatch(new Type[] { typeof(bool), typeof(AttackType), typeof(bool), typeof(bool) })]
        private static class BlockDie
        {
            private static bool Prefix(Actor __instance) { return !Active(__instance); }
        }

        [HarmonyPatch(typeof(Actor), "dieAndDestroy", new Type[] { typeof(AttackType) })]
        private static class BlockDieAndDestroy
        {
            private static bool Prefix(Actor __instance) { return !Active(__instance); }
        }

        [HarmonyPatch(typeof(Actor), "dieSimpleNone")]
        private static class BlockSilentDeath
        {
            private static bool Prefix(Actor __instance) { return !Active(__instance); }
        }

        [HarmonyPatch(typeof(Actor), "removeByMetamorphosis")]
        private static class BlockTransformationRemoval
        {
            private static bool Prefix(Actor __instance) { return !Active(__instance); }
        }

        [HarmonyPatch(typeof(Actor), "u4_deadCheck")]
        private static class BlockDeathCheck
        {
            private static bool Prefix(Actor __instance) { return !Active(__instance); }
        }

        [HarmonyPatch(typeof(Actor), "b55_updateNaturalDeaths")]
        private static class BlockNaturalDeath
        {
            private static bool Prefix(Actor __instance) { return !Active(__instance); }
        }

        [HarmonyPatch(typeof(Actor), "updateAge")]
        private static class FreezeAge
        {
            private static bool Prefix(Actor __instance) { return !Active(__instance); }
        }

        [HarmonyPatch(typeof(Actor), "updateNutritionDecay")]
        private static class NoStarvation
        {
            private static bool Prefix(Actor __instance) { return !Active(__instance); }
        }

        [HarmonyPatch(typeof(Actor), "getHitFullHealth")]
        private static class NoHitAtFullHealth
        {
            private static bool Prefix(Actor __instance) { return !Active(__instance); }
        }

        [HarmonyPatch(typeof(Actor), "addInjuryTrait")]
        private static class NoInjuries
        {
            private static bool Prefix(Actor __instance) { return !Active(__instance); }
        }

        [HarmonyPatch(typeof(Actor), "addTrait", new Type[] { typeof(ActorTrait), typeof(bool) })]
        private static class BlockHarmfulTraitObject
        {
            private static bool Prefix(Actor __instance, ActorTrait pTrait, ref bool __result)
            {
                if (!Active(__instance) || !BadTrait(pTrait)) return true;
                __result = false;
                return false;
            }
        }

        [HarmonyPatch(typeof(Actor), "addTrait", new Type[] { typeof(string), typeof(bool) })]
        private static class BlockHarmfulTraitId
        {
            private static bool Prefix(Actor __instance, string pTraitID, ref bool __result)
            {
                if (!Active(__instance) || string.IsNullOrEmpty(pTraitID)) return true;
                ActorTrait trait = AssetManager.traits == null ? null : AssetManager.traits.get(pTraitID);
                if (BadTrait(trait))
                {
                    __result = false;
                    return false;
                }
                return true;
            }
        }

        // Immortality itself cannot be stripped by curses, cures or trait conflicts.
        [HarmonyPatch(typeof(Actor), "removeTrait", new Type[] { typeof(string) })]
        private static class PreserveImmortalById
        {
            private static bool Prefix(Actor __instance, string pTraitID)
            {
                return pTraitID != "immortal" || !Active(__instance);
            }
        }

        [HarmonyPatch(typeof(Actor), "removeTrait", new Type[] { typeof(ActorTrait) })]
        private static class PreserveImmortalByObject
        {
            private static bool Prefix(Actor __instance, ActorTrait pTrait, ref bool __result)
            {
                if (pTrait == null || pTrait.id != "immortal" || !Active(__instance))
                    return true;
                __result = false;
                return false;
            }
        }

        [HarmonyPatch(typeof(Actor), "clearTraits")]
        private static class PreserveImmortalOnClear
        {
            private static bool Prefix(Actor __instance) { return !Active(__instance); }
        }

        [HarmonyPatch(typeof(Actor), "removeTraits")]
        private static class PreserveImmortalOnBatchRemoval
        {
            private static void Prefix(Actor __instance, ref ICollection<ActorTrait> pTraits)
            {
                if (!Active(__instance) || pTraits == null) return;
                pTraits = pTraits.Where(t => t == null || t.id != "immortal").ToList();
            }
        }

        [HarmonyPatch(typeof(BaseSimObject), "addStatusEffect",
            new Type[] { typeof(StatusAsset), typeof(float), typeof(bool) })]
        private static class BlockBadBaseStatus
        {
            private static bool Prefix(BaseSimObject __instance, StatusAsset pStatusAsset, ref bool __result)
            {
                if (!Active(__instance as Actor) || !BadStatus(pStatusAsset)) return true;
                __result = false;
                return false;
            }
        }

        [HarmonyPatch(typeof(Actor), "addStatusEffect",
            new Type[] { typeof(StatusAsset), typeof(float), typeof(bool) })]
        private static class BlockBadActorStatus
        {
            private static bool Prefix(Actor __instance, StatusAsset pStatusAsset, ref bool __result)
            {
                if (!Active(__instance) || !BadStatus(pStatusAsset)) return true;
                __result = false;
                return false;
            }
        }

        [HarmonyPatch(typeof(BaseSimObject), "addNewStatusEffect")]
        private static class PreventDirectBadStatus
        {
            private static bool Prefix(BaseSimObject __instance, StatusAsset pStatusAsset)
            {
                return !Active(__instance as Actor) || !BadStatus(pStatusAsset);
            }
        }

        [HarmonyPatch(typeof(Actor), "isSick")]
        private static class NeverSick
        {
            private static void Postfix(Actor __instance, ref bool __result)
            {
                if (Active(__instance)) __result = false;
            }
        }

        [HarmonyPatch(typeof(Actor), "calculateIsSick")]
        private static class IgnoreSickness
        {
            private static void Postfix(Actor __instance, ref bool __result)
            {
                if (Active(__instance)) __result = false;
            }
        }

        [HarmonyPatch(typeof(Actor), "isDamagedByRain")]
        private static class NoRainDamage
        {
            private static void Postfix(Actor __instance, ref bool __result)
            {
                if (Active(__instance)) __result = false;
            }
        }

        [HarmonyPatch(typeof(Actor), "isDamagedByOcean")]
        private static class NoOceanDamage
        {
            private static void Postfix(Actor __instance, ref bool __result)
            {
                if (Active(__instance)) __result = false;
            }
        }

        [HarmonyPatch(typeof(Actor), "getWaterDamage")]
        private static class NoWaterDamage
        {
            private static void Postfix(Actor __instance, ref int __result)
            {
                if (Active(__instance)) __result = 0;
            }
        }
    }
}