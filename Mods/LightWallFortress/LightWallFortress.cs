using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using NeoModLoader.api;
using UnityEngine;

namespace LightWallFortress
{
    /// <summary>
    /// Strengthens the vanilla Wall of Light without replacing any WorldBox
    /// assemblies. Characters cannot cross its occupied tiles, nor squeeze
    /// between the two orthogonal wall tiles at a diagonal corner.
    /// Works for kings, immortals, walkers, flyers and pushed units.
    /// Explicit user magnet pickup may move a character across the boundary.
    /// </summary>
    public sealed partial class LightWallFortressMod : BasicMod<LightWallFortressMod>
    {
        private const string WallId = "wall_light";
        private static Harmony _harmony;
        private static bool _ready;
        private static TopTileType _lastConfiguredWall;
        private static readonly FieldInfo MapMagnetField =
            AccessTools.Field(typeof(MapBox), "magnet");
        private static readonly FieldInfo MagnetPickedListField =
            AccessTools.Field(typeof(Magnet), "magnet_units");
        private static int _errorCount;
        private static long _blockedCrossings;
        private static float _nextLoggedReport;

        protected override void OnModLoad()
        {
            if (_harmony == null)
            {
                _harmony = new Harmony("worldbox.lightwall.fortress.1");
                _harmony.PatchAll(typeof(LightWallFortressMod).Assembly);
            }
            _ready = true;
            FortifyVanillaLightWall();
            LogInfo("Wall of Light fortress loaded. Movement/force/diagonal crossing guards installed.");
        }

        private void Update()
        {
            // Asset objects can be initialized later than NeoModLoader.
            if (_ready && !Config.worldLoading)
                FortifyVanillaLightWall();

            UpdateReinoAislado();

            if (_blockedCrossings > 0 &&
                Time.unscaledTime > _nextLoggedReport)
            {
                _nextLoggedReport = Time.unscaledTime + 60f;
                LogInfo("Wall of Light prevented " + _blockedCrossings +
                        " boundary crossings since loading.");
            }
        }

        private static void FortifyVanillaLightWall()
        {
            TopTileType wall = TopTileLibrary.wall_light;
            if (wall == null || wall == _lastConfiguredWall)
                return;

            // Protect the existing native asset: preserve its gold pixels,
            // animations and icon. Do not change other wall types.
            wall.wall = true;
            wall.block = true;
            wall.block_height = 100f;
            wall.strength = 1000000;
            wall.explodable = false;
            wall.explodable_delayed = false;
            wall.explodable_timed = false;
            wall.explodable_by_ocean = false;
            wall.burnable = false;
            wall.can_be_set_on_fire = false;
            wall.remove_on_heat = false;
            wall.remove_on_freeze = false;
            wall.can_be_frozen = false;
            wall.can_be_unfrozen = false;
            // Keep normal user editing possible via the demolish tool.
            wall.can_be_removed_with_demolish = true;

            _lastConfiguredWall = wall;
            LogInfo("Native wall_light reinforced: blocks movement and flight, " +
                    "strength 1000000, heat/fire/explosion resistance.");
        }

        private static bool IsLightWall(int x, int y)
        {
            MapBox world = World.world;
            if (world == null)
                return false;

            WorldTile tile = world.GetTile(x, y);
            return IsLightWallTile(tile);
        }

        private static bool IsLightWallTile(WorldTile tile)
        {
            if (tile == null || tile.top_type == null ||
                (tile.top_type != _lastConfiguredWall &&
                 tile.top_type.id != WallId))
                return false;

            // Walls loaded from an old save might still have old HP=10.
            // Strengthen their CURRENT tile health when they are examined.
            if (tile.health < 1000000)
                tile.health = 1000000;
            return true;
        }

        // True when a straight segment enters any light-wall cell, or
        // crosses diagonally beside a wall corner. A grid traversal is
        // stronger than sampling every few pixels: a fast unit cannot
        // skip a 1-tile-wide barrier between two updates.
        private static bool SegmentCrossesWall(Vector2 from, Vector2 to)
        {
            if (World.world == null ||
                float.IsNaN(from.x) || float.IsNaN(from.y) ||
                float.IsNaN(to.x) || float.IsNaN(to.y) ||
                float.IsInfinity(from.x) || float.IsInfinity(from.y) ||
                float.IsInfinity(to.x) || float.IsInfinity(to.y))
                return false;

            int x = Mathf.FloorToInt(from.x);
            int y = Mathf.FloorToInt(from.y);
            int endX = Mathf.FloorToInt(to.x);
            int endY = Mathf.FloorToInt(to.y);

            if (x == endX && y == endY)
                return false;

            float dx = to.x - from.x;
            float dy = to.y - from.y;
            int stepX = dx > 0f ? 1 : dx < 0f ? -1 : 0;
            int stepY = dy > 0f ? 1 : dy < 0f ? -1 : 0;

            float tDeltaX = stepX == 0 ? float.PositiveInfinity : 1f / Mathf.Abs(dx);
            float tDeltaY = stepY == 0 ? float.PositiveInfinity : 1f / Mathf.Abs(dy);
            float nextX = stepX > 0 ? x + 1f : x;
            float nextY = stepY > 0 ? y + 1f : y;
            float tMaxX = stepX == 0 ? float.PositiveInfinity : (nextX - from.x) / dx;
            float tMaxY = stepY == 0 ? float.PositiveInfinity : (nextY - from.y) / dy;

            int totalSteps = Math.Min(100000,
                Math.Abs(endX - x) + Math.Abs(endY - y) + 3);

            for (int i = 0; i < totalSteps && (x != endX || y != endY); i++)
            {
                if (tMaxX + 0.00001f < tMaxY)
                {
                    x += stepX;
                    if (IsLightWall(x, y))
                        return true;
                    tMaxX += tDeltaX;
                }
                else if (tMaxY + 0.00001f < tMaxX)
                {
                    y += stepY;
                    if (IsLightWall(x, y))
                        return true;
                    tMaxY += tDeltaY;
                }
                else
                {
                    // Exact diagonal through the vertex: don't let an
                    // immortal slip through the 4-tile corner seam.
                    if (IsLightWall(x + stepX, y) ||
                        IsLightWall(x, y + stepY) ||
                        IsLightWall(x + stepX, y + stepY))
                        return true;

                    x += stepX;
                    y += stepY;
                    tMaxX += tDeltaX;
                    tMaxY += tDeltaY;
                }
            }
            return false;
        }

        private static bool UserIsDraggingThisActor(Actor actor)
        {
            MapBox world = World.world;
            if (world == null || actor == null)
                return false;

            // Deliberate god-mode repositioning with the original magnet
            // or the Immortal-only magnet is allowed. Ordinary AI/forces,
            // including a king attempting to travel, remain blocked.
            string selected = world.getSelectedPowerID();
            if (selected != "magnet" && selected != "immortal_main_magnet")
                return false;

            if (MapMagnetField == null || MagnetPickedListField == null)
                return false;

            Magnet magnet = MapMagnetField.GetValue(world) as Magnet;
            var units = magnet == null ? null :
                MagnetPickedListField.GetValue(magnet) as List<Actor>;

            return units != null && units.Contains(actor);
        }

        private static void RestoreSafePosition(Actor actor, Vector2 safe)
        {
            actor.current_position = safe;
            // Position and tile must agree. Otherwise AI could keep an
            // outside tile while the sprite visually returned inside.
            WorldTile tile = Toolbox.getTileAt(safe.x, safe.y);
            if (tile != null)
                actor.current_tile = tile;

            actor.stopMovement();
            _blockedCrossings++;

            // Don't log on each simulated tick; high speeds may attempt
            // thousands of blocked movements against the barrier.
        }

        private static void GuardMovement(Actor actor, Vector2 lastSafe)
        {
            if (!_ready || _lastConfiguredWall == null ||
                World.world == null || Config.worldLoading ||
                actor == null || !actor.isAlive())
                return;

            try
            {
                Vector2 current = actor.current_position;
                if (Mathf.FloorToInt(lastSafe.x) == Mathf.FloorToInt(current.x) &&
                    Mathf.FloorToInt(lastSafe.y) == Mathf.FloorToInt(current.y))
                    return;

                if (UserIsDraggingThisActor(actor))
                    return;

                if (SegmentCrossesWall(lastSafe, current))
                    RestoreSafePosition(actor, lastSafe);
            }
            catch (Exception ex)
            {
                if (_errorCount++ < 3)
                    LogWarning("Movement guard failed: " +
                               ex.GetType().Name + ": " + ex.Message);
            }
        }

        // WorldBox's own forced-break routine must never destroy light wall
        // tiles. Manual demolish/terraform tools remain available.
        [HarmonyPatch(typeof(WorldTile), "tryToBreak")]
        private static class ProtectLightWallFromBreak
        {
            private static bool Prefix(WorldTile __instance)
            {
                return !_ready || !IsLightWallTile(__instance);
            }
        }

        // New wall tiles may be installed after this mod has loaded.
        [HarmonyPatch(typeof(WorldTile), "setTopTileType",
                      new Type[] { typeof(TopTileType), typeof(bool) })]
        private static class FortifyNewlyDrawnLightWall
        {
            private static void Postfix(WorldTile __instance)
            {
                if (_ready)
                    IsLightWallTile(__instance);
            }
        }

        // Normal walking (including fast kings and superpowered immortals).
        [HarmonyPatch(typeof(Actor), "updateMovement",
                      new Type[] { typeof(float), typeof(float) })]
        private static class BlockWalkingThroughLightWall
        {
            private static void Prefix(Actor __instance, out Vector2 __state)
            {
                __state = __instance.current_position;
            }

            private static void Postfix(Actor __instance, Vector2 __state)
            {
                GuardMovement(__instance, __state);
            }
        }

        // Airborne/impulse movement such as jumps and knockback can bypass
        // pathfinding; guard the resulting position as well.
        [HarmonyPatch(typeof(Actor), "b1_checkUnderForce",
                      new Type[] { typeof(float) })]
        private static class BlockForceThroughLightWall
        {
            private static void Prefix(Actor __instance, out Vector2 __state)
            {
                __state = __instance.current_position;
            }

            private static void Postfix(Actor __instance, Vector2 __state)
            {
                GuardMovement(__instance, __state);
            }
        }

        // Teleports and direct position updates eventually synchronize
        // the current tile. Reject a crossed wall before tile sync occurs.
        [HarmonyPatch(typeof(Actor), "findCurrentTile",
                      new Type[] { typeof(bool) })]
        private static class BlockTileSyncAcrossLightWall
        {
            private static void Prefix(Actor __instance)
            {
                if (!_ready || _lastConfiguredWall == null ||
                    __instance == null || __instance.current_tile == null)
                    return;

                WorldTile originalTile = __instance.current_tile;
                Vector2 position = __instance.current_position;
                if (Mathf.FloorToInt(position.x) == originalTile.x &&
                    Mathf.FloorToInt(position.y) == originalTile.y)
                    return;

                if (UserIsDraggingThisActor(__instance))
                    return;

                // This uses the center of the last trusted tile. In
                // long-distance teleports the ray still encounters walls.
                GuardMovement(__instance,
                    new Vector2(originalTile.x + 0.5f,
                                originalTile.y + 0.5f));
            }
        }

        // Other direct tile placement hooks, e.g. scripted relocation,
        // also must respect light walls unless the user uses a magnet.
        [HarmonyPatch(typeof(Actor), "setCurrentTilePosition",
                      new Type[] { typeof(WorldTile) })]
        private static class BlockDirectRelocationAcrossLightWall
        {
            private static void Prefix(Actor __instance, out Vector2 __state)
            {
                __state = __instance.current_position;
            }

            private static void Postfix(Actor __instance, Vector2 __state)
            {
                GuardMovement(__instance, __state);
            }
        }
    }
}