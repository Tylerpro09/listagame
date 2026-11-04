using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using NeoModLoader.General;
using UnityEngine;

namespace EternalImmortalMod
{
    public partial class EternalImmortal
    {
        private const string ImmortalMagnetPowerId = "immortal_main_magnet";
        private static readonly FieldInfo MagnetButtonPowerField =
            AccessTools.Field(typeof(PowerButton), "godPower");
        private static readonly FieldInfo MagnetTabButtonsField =
            AccessTools.Field(typeof(PowersTab), "_power_buttons");

        private static bool _immortalMagnetPowerRegistered;
        private static bool _immortalMagnetButtonRegistered;
        private static int _immortalMagnetUiErrorCount;
        private static float _immortalMagnetNextUiAttempt;
        private static Sprite _immortalMagnetFallbackIcon;

        private static void RegisterImmortalMagnetPower()
        {
            if (_immortalMagnetPowerRegistered || AssetManager.powers == null)
                return;

            GodPower vanilla = AssetManager.powers.get("magnet");
            if (vanilla == null)
                return;

            GodPower custom = AssetManager.powers.get(ImmortalMagnetPowerId);
            if (custom == null)
                custom = AssetManager.powers.clone(ImmortalMagnetPowerId, "magnet");
            if (custom == null)
                return;

            custom.name = "Imán de Inmortales";
            custom.requires_premium = false;
            custom.disabled_on_mobile = false;
            custom.show_tool_sizes = vanilla.show_tool_sizes;
            custom.hold_action = vanilla.hold_action;
            custom.highlight = vanilla.highlight;
            custom.sound_drawing = vanilla.sound_drawing;
            custom.click_interval = vanilla.click_interval;
            // Reuse the EXACT magnet movement/drag/release implementation;
            // only the pickup eligibility callback below is filtered.
            custom.click_brush_action = vanilla.click_brush_action;
            custom.click_action = vanilla.click_action;
            custom.click_power_brush_action = vanilla.click_power_brush_action;
            custom.click_power_action = vanilla.click_power_action;

            try
            {
                LocalizedTextManager.add(
                    ImmortalMagnetPowerId, "Imán de Inmortales",
                    false, "EternalImmortal", false);
                LocalizedTextManager.add(
                    ImmortalMagnetPowerId + " Description",
                    "Imán exclusivo para mover al hombre y la mujer inmortales principales.",
                    false, "EternalImmortal", false);
            }
            catch (Exception)
            {
                // UI translation is optional; original native drag must work.
            }

            _immortalMagnetPowerRegistered = true;
            LogInfo("Registered Imán de Inmortales: native magnet physics, principal immortal actors only.");
        }

        private void MaintainImmortalMagnetUI()
        {
            if (!_immortalMagnetPowerRegistered)
                RegisterImmortalMagnetPower();

            if (!_immortalMagnetButtonRegistered &&
                Time.unscaledTime >= _immortalMagnetNextUiAttempt)
            {
                _immortalMagnetNextUiAttempt = Time.unscaledTime + 3f;
                try
                {
                    RegisterImmortalMagnetButton();
                }
                catch (Exception ex)
                {
                    if (_immortalMagnetUiErrorCount++ < 3)
                        LogWarning("Imán de Inmortales button will retry: " +
                                   ex.GetType().Name + ": " + ex.Message);
                }
            }
        }

        private static void RegisterImmortalMagnetButton()
        {
            if (_immortalMagnetButtonRegistered || !_immortalMagnetPowerRegistered ||
                CanvasMain.instance == null || CanvasMain.instance.canvas_ui == null)
                return;

            if (MagnetButtonPowerField == null || MagnetTabButtonsField == null)
                return;

            // Prefer WorldBox's original magnet power and icon, so the new
            // skill appears in the same tab, with familiar visuals.
            PowerButton vanillaButton = null;
            foreach (PowerButton button in
                     UnityEngine.Object.FindObjectsOfType<PowerButton>(true))
            {
                if (button == null)
                    continue;

                GodPower power = MagnetButtonPowerField.GetValue(button) as GodPower;
                if (power != null && power.id == "magnet")
                {
                    vanillaButton = button;
                    break;
                }
            }

            PowersTab tab = vanillaButton == null ? null :
                            vanillaButton.GetComponentInParent<PowersTab>();
            if (tab == null)
                tab = PowerButtonCreator.GetTab(PowerTabNames.Nature);
            if (tab == null)
                return;

            foreach (PowerButton old in tab.GetComponentsInChildren<PowerButton>(true))
            {
                if (old != null && old.name == ImmortalMagnetPowerId)
                {
                    _immortalMagnetButtonRegistered = true;
                    return;
                }
            }

            List<PowerButton> registered =
                MagnetTabButtonsField.GetValue(tab) as List<PowerButton>;
            if (registered == null)
                return;

            Sprite sprite = null;
            if (vanillaButton != null && vanillaButton.icon != null)
                sprite = vanillaButton.icon.sprite;
            if (sprite == null)
                sprite = AssetManager.powers.get("magnet").sprite_icon;
            if (sprite == null)
                sprite = CreateImmortalMagnetFallbackIcon();

            PowerButton custom = PowerButtonCreator.CreateGodPowerButton(
                ImmortalMagnetPowerId, sprite);
            if (custom == null)
                return;

            MagnetButtonPowerField.SetValue(
                custom, AssetManager.powers.get(ImmortalMagnetPowerId));
            custom.transform.SetParent(tab.transform, false);
            custom.transform.localScale = Vector3.one;
            registered.Add(custom);
            tab.recalc();
            _immortalMagnetButtonRegistered = true;
            LogInfo("Imán de Inmortales added to native WorldBox powers tab, icon from original magnet.");
        }

        private static Sprite CreateImmortalMagnetFallbackIcon()
        {
            if (_immortalMagnetFallbackIcon != null)
                return _immortalMagnetFallbackIcon;

            const int side = 32;
            Texture2D texture = new Texture2D(
                side, side, TextureFormat.RGBA32, false);
            texture.filterMode = FilterMode.Point;
            Color32[] pixels = new Color32[side * side];

            for (int y = 0; y < side; y++)
            for (int x = 0; x < side; x++)
            {
                float dx = x - 15.5f;
                float dy = y - 16.5f;
                float distance = Mathf.Sqrt(dx * dx + dy * dy);
                bool curve = dy < 5f && dy > -12f &&
                             distance >= 7.5f && distance <= 12f &&
                             y <= 18;
                bool arms = y >= 15 && y <= 27 &&
                            ((x >= 4 && x <= 9) ||
                             (x >= 22 && x <= 27));
                bool bottom = y >= 4 && y <= 11 &&
                              Mathf.Abs(dx) <= 9.5f;
                bool magnet = curve || arms || bottom;
                bool pole = arms && y >= 24;
                pixels[y * side + x] = !magnet
                    ? new Color32(0, 0, 0, 0)
                    : pole ? new Color32(88, 195, 255, 255)
                           : new Color32(245, 88, 83, 255);
            }

            texture.SetPixels32(pixels);
            texture.Apply();
            _immortalMagnetFallbackIcon = Sprite.Create(
                texture, new Rect(0, 0, side, side),
                new Vector2(.5f, .5f), 32f);
            return _immortalMagnetFallbackIcon;
        }

        // The vanilla pickup callback chooses whom the original magnet
        // includes. Filter it BEFORE the game changes any actor state:
        // no other creature is grabbed, pushed, stunned, or thrown.
        // When normal magnet is selected this patch does nothing.
        [HarmonyPatch(typeof(Magnet), "<pickupUnits>b__18_0")]
        private static class FilterImmortalMagnetPickup
        {
            private static bool Prefix(Actor tActor)
            {
                if (World.world == null ||
                    !World.world.isSelectedPower(ImmortalMagnetPowerId))
                    return true;

                if (tActor == null || !tActor.isAlive() ||
                    !HasEternity(tActor))
                    return false;

                return tActor == _malePrimary || tActor == _femalePrimary;
            }
        }
    }
}