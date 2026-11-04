using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace LightWallFortress
{
    public sealed partial class LightWallFortressMod
    {
        // This subsystem deliberately uses native WorldBox succession and
        // City.makeOwnKingdom. It never fakes Kingdom or City save records.
        private const int MinimumEnclosedTiles = 24;
        private const int MaximumEnclosedTiles = 16000;
        private const int MinimumCityPopulation = 2;
        private const float StableEnclosureSeconds = 24f;
        private const float IsolationReviewInterval = 6f;

        private sealed class IsolationWatch
        {
            public City City;
            public Kingdom OriginalKingdom;
            public ulong Fingerprint;
            public float FirstSeen;
        }

        private sealed class EnclosedArea
        {
            public HashSet<int> Cells;
            public ulong Fingerprint;
            public int Count;
        }

        private static readonly Dictionary<Actor, IsolationWatch> IsolationCandidates =
            new Dictionary<Actor, IsolationWatch>();
        private static MapBox _isolationWorld;
        private static float _nextIsolationReview;
        private static int _isolationScanIndex;
        private static int _isolationErrorCount;

        private void UpdateReinoAislado()
        {
            MapBox world = World.world;
            if (!_ready)
                return;

            // The MapBox singleton may be reused between saved worlds.
            // Clear old king references on every world unloading/loading.
            if (Config.worldLoading || !Config.game_loaded || world == null)
            {
                if (IsolationCandidates.Count > 0)
                    IsolationCandidates.Clear();
                _isolationWorld = null;
                return;
            }

            if (Config.paused || world.units == null ||
                _lastConfiguredWall == null)
                return;

            if (world != _isolationWorld)
            {
                _isolationWorld = world;
                IsolationCandidates.Clear();
                _isolationScanIndex = 0;
                _nextIsolationReview = Time.unscaledTime + IsolationReviewInterval;
            }

            if (Time.unscaledTime < _nextIsolationReview)
                return;
            _nextIsolationReview = Time.unscaledTime + IsolationReviewInterval;

            // Process up to two kings in a review. Avoid flooding the entire
            // map every frame and keep large populated worlds responsive.
            List<Actor> living = world.units.units_only_alive;
            if (living == null || living.Count == 0)
                return;

            int checkedActors = 0;
            int kingsReviewed = 0;
            while (checkedActors < living.Count && kingsReviewed < 2)
            {
                if (_isolationScanIndex >= living.Count)
                    _isolationScanIndex = 0;

                Actor person = living[_isolationScanIndex++];
                checkedActors++;

                if (person == null || !person.isAlive() ||
                    !person.is_profession_king || person.city == null ||
                    person.kingdom == null ||
                    person.kingdom.king != person ||
                    UserIsDraggingThisActor(person))
                    continue;

                kingsReviewed++;
                try
                {
                    ReviewEnclosedKing(person);
                }
                catch (Exception ex)
                {
                    IsolationCandidates.Remove(person);
                    if (_isolationErrorCount++ < 5)
                        LogWarning("Reino Aislado review failed: " +
                                   ex.GetType().Name + ": " + ex.Message);
                }
            }
        }

        private static bool IsSolidWall(WorldTile tile)
        {
            return tile != null && tile.top_type != null &&
                   (tile.top_type == _lastConfiguredWall ||
                    tile.top_type.id == WallId);
        }

        private static int CellKey(int x, int y, int width)
        {
            return y * width + x;
        }

        private static bool InsideArea(EnclosedArea area, WorldTile tile,
                                       int width)
        {
            return tile != null && area.Cells.Contains(
                CellKey(tile.x, tile.y, width));
        }

        // Eight-direction flood fill, with blocked corner cutting.
        // If any route reaches the map edge, there is an opening.
        // This is deliberately conservative: only clearly finite,
        // fully enclosed areas trigger irreversible political changes.
        private static EnclosedArea FindClosedWallRegion(WorldTile seed)
        {
            MapBox world = World.world;
            if (world == null || seed == null || IsSolidWall(seed))
                return null;

            int width = MapBox.width;
            int height = MapBox.height;
            if (width < 4 || height < 4)
                return null;

            var seen = new HashSet<int>();
            var pending = new Queue<WorldTile>();
            int start = CellKey(seed.x, seed.y, width);
            seen.Add(start);
            pending.Enqueue(seed);
            ulong hash = 14695981039346656037UL;
            bool touchesWall = false;

            while (pending.Count > 0)
            {
                if (seen.Count > MaximumEnclosedTiles)
                    return null;

                WorldTile here = pending.Dequeue();
                if (here.x <= 0 || here.y <= 0 ||
                    here.x >= width - 1 || here.y >= height - 1)
                    return null;

                unchecked
                {
                    hash ^= (ulong)CellKey(here.x, here.y, width);
                    hash *= 1099511628211UL;
                }

                for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                {
                    if (dx == 0 && dy == 0) continue;

                    int nx = here.x + dx;
                    int ny = here.y + dy;
                    WorldTile target = world.GetTile(nx, ny);
                    if (target == null) return null;

                    if (IsSolidWall(target))
                    {
                        touchesWall = true;
                        continue;
                    }

                    if (dx != 0 && dy != 0)
                    {
                        WorldTile orthX = world.GetTile(nx, here.y);
                        WorldTile orthY = world.GetTile(here.x, ny);
                        // Existing collision code does not permit cutting
                        // diagonally past a Wall of Light corner. A gap is
                        // counted only when both side cells are passable.
                        if (IsSolidWall(orthX) || IsSolidWall(orthY))
                            continue;
                    }

                    int key = CellKey(nx, ny, width);
                    if (seen.Add(key))
                        pending.Enqueue(target);
                }
            }

            if (!touchesWall || seen.Count < MinimumEnclosedTiles)
                return null;

            return new EnclosedArea
            {
                Cells = seen,
                Fingerprint = hash,
                Count = seen.Count
            };
        }

        private static bool IsWholeCityInside(Actor formerKing, City city,
                                              EnclosedArea area)
        {
            if (formerKing == null || city == null ||
                !city.isAlive() || formerKing.city != city ||
                city.kingdom != formerKing.kingdom)
                return false;

            int width = MapBox.width;
            WorldTile cityCenterTile = Toolbox.getTileAt(
                city.city_center.x, city.city_center.y);
            if (!InsideArea(area, cityCenterTile, width))
                return false;

            List<Actor> members = city.units;
            if (members == null)
                return false;

            int inside = 0;
            foreach (Actor inhabitant in members)
            {
                if (inhabitant == null || !inhabitant.isAlive())
                    continue;

                if (inhabitant.current_tile == null ||
                    !InsideArea(area, inhabitant.current_tile, width))
                    return false;

                inside++;
            }
            if (inside < MinimumCityPopulation)
                return false;

            // A walled room inside the middle of a city is NOT a new city.
            // Require the actual buildings of the city to be inside too;
            // otherwise its exterior buildings would be annexed incorrectly.
            if (city.buildings != null)
            {
                foreach (Building building in city.buildings)
                {
                    if (building == null || building.isRemoved())
                        continue;
                    if (building.current_tile == null ||
                        !InsideArea(area, building.current_tile, width))
                        return false;
                }
            }

            return true;
        }

        private static bool OldRealmHasOutsideCity(Kingdom oldRealm,
                                                   City walledCity,
                                                   EnclosedArea area)
        {
            if (oldRealm == null || oldRealm.cities == null)
                return false;

            foreach (City existing in oldRealm.cities)
            {
                if (existing == null || existing == walledCity ||
                    !existing.isAlive())
                    continue;

                WorldTile center = Toolbox.getTileAt(
                    existing.city_center.x, existing.city_center.y);
                if (center != null &&
                    !InsideArea(area, center, MapBox.width))
                    return true;
            }
            return false;
        }

        private static void ReviewEnclosedKing(Actor king)
        {
            City city = king.city;
            Kingdom oldKingdom = king.kingdom;
            if (city == null || oldKingdom == null ||
                oldKingdom.king != king || city.kingdom != oldKingdom ||
                king.current_tile == null)
            {
                IsolationCandidates.Remove(king);
                return;
            }

            EnclosedArea area = FindClosedWallRegion(king.current_tile);
            if (area == null ||
                !IsWholeCityInside(king, city, area) ||
                !OldRealmHasOutsideCity(oldKingdom, city, area))
            {
                IsolationCandidates.Remove(king);
                return;
            }

            float now = Time.unscaledTime;
            if (!IsolationCandidates.TryGetValue(king, out IsolationWatch watch) ||
                watch.City != city || watch.OriginalKingdom != oldKingdom ||
                watch.Fingerprint != area.Fingerprint)
            {
                IsolationCandidates[king] = new IsolationWatch
                {
                    City = city,
                    OriginalKingdom = oldKingdom,
                    Fingerprint = area.Fingerprint,
                    FirstSeen = now
                };
                LogInfo("Reino Aislado: verified a closed Wall of Light around " +
                        king.getName() + " and their city (" + area.Count +
                        " tiles). Waiting for wall stability.");
                return;
            }

            if (now - watch.FirstSeen < StableEnclosureSeconds)
                return;

            // Revalidate the relationships immediately before committing.
            if (!king.isAlive() || !city.isAlive() ||
                oldKingdom.king != king || king.city != city ||
                city.kingdom != oldKingdom)
                return;

            // WorldBox native election: old realm loses its king and will
            // select a replacement automatically. The actor stays alive,
            // keeps their traits, fusion stats, sex, and normal size.
            oldKingdom.kingLeftEvent();

            // Native city secession. The city, all its residents and
            // buildings transfer into the new independent kingdom and the
            // same character becomes its king. No manual save mutations.
            Kingdom founded = null;
            try
            {
                founded = city.makeOwnKingdom(king, false, false);
            }
            catch
            {
                // Best-effort rollback if the city has not been transferred.
                // Never leave a healthy old kingdom rulerless merely because
                // a third-party mod interfered with native independence.
                if (city.kingdom == oldKingdom &&
                    oldKingdom.king == null && king.isAlive())
                    oldKingdom.setKing(king, false);
                throw;
            }

            if (founded != null)
            {
                LogInfo("REINO AISLADO CREATED: " + king.getName() +
                        " now rules kingdom #" + founded.id +
                        "; old kingdom #" + oldKingdom.id +
                        " will elect a successor; area=" + area.Count);
            }
            else
            {
                if (city.kingdom == oldKingdom &&
                    oldKingdom.king == null && king.isAlive())
                    oldKingdom.setKing(king, false);
                LogWarning("Reino Aislado: native secession returned no kingdom " +
                           "for " + king.getName() + "; attempting rollback.");
            }

            IsolationCandidates.Remove(king);
        }
    }
}