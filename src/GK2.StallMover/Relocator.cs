using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace GK2.StallMover
{
    /// Performs moves and swaps. Mirrors the piece-spawning logic of TownSystem.CreateTownBuildingOnWgo
    /// (the game's own stall build/upgrade code) but never re-runs build side effects
    /// (character-create expressions, vendor unlocks, forced vendor level-ups).
    public static class Relocator
    {
        const string AVAILABLE_BY_TIME = "available_by_time";

        public static string CheckMovable(StallLocation s)
        {
            if (!s.IsBuilt) return "not built";
            if (s.Character == null) return "no merchant linked";
            if (s.Tent.CraftComponent != null && s.Tent.CraftComponent.IsStarted) return "upgrade in progress";
            var comp = s.Tent.TownBuildingWgoComponent;
            if (comp.TierIndex < 1 || comp.TierIndex > comp.SceneConfiguration.tierDataList.Count) return $"unexpected tier {comp.TierIndex}";
            if (s.BaseDef == null) return "unknown stall type";
            if (string.IsNullOrEmpty(s.SignboardId)) return "location not found in game content";
            return null;
        }

        /// Move built stall `from` onto empty location `to`. `from` becomes an empty signboard again.
        public static void Move(StallLocation from, StallLocation to)
        {
            string why = CheckMovable(from);
            if (why != null) throw new InvalidOperationException("Cannot move: " + why);
            if (to.IsBuilt || to.Signboard == null) throw new InvalidOperationException("Target is not empty");
            if (!Town.Fits(from, to.SignboardId)) throw new InvalidOperationException("Stall does not fit target location");
            var wd = MainGame.WorldData;

            // 1. Prepare everything that can fail BEFORE changing the world.
            var restored = ContentIndex.CreateRestoredSignboard(from.SceneId, from.GdTent);
            if (restored == null) throw new InvalidOperationException("Could not find original signboard in content for " + from.GdTent);
            if (wd.GetWgoData(restored.UniqueId) != null) throw new InvalidOperationException("Original signboard unexpectedly still exists");

            var oldComp = from.Tent.TownBuildingWgoComponent;
            string buildingId = oldComp.TownBuildingId;
            int tier = oldComp.TierIndex;
            var oldTent = from.Tent;
            var ch = from.Character;
            string fromTag = from.SignboardFormTag;
            string toTag = to.SignboardFormTag;

            Plugin.Log.LogInfo($"MOVE {buildingId} tier {tier}: {from.SignboardId} ({from.GdTent}) -> {to.SignboardId} ({to.GdTent})");

            // 2. Target component = the empty signboard's component (holds the target location's layout).
            var newComp = to.Signboard.TownBuildingWgoComponent;
            ClearCreatedIds(newComp.SceneConfiguration);
            newComp.TownBuildingId = buildingId;
            newComp.TierIndex = tier;

            // 3. Remove the stall's pieces at the old location (tent last).
            RemovePieces(oldComp, tier, oldTent);

            // 4. Spawn at the new location, reusing the same merchant.
            var newTent = SpawnPieces(newComp, tier, toTag, to.Signboard.WorldId, ch, oldTent);

            // 5. Vanilla builds run the stall's "execute_on_building_finished" expressions, which (per game data)
            //    may include RepairTownCluster() on the signboard: clears ruin objects and repairs the plot.
            //    Replicate ONLY that part for the target; other effects (town quality etc.) are not re-applied.
            var baseDef = from.BaseDef;
            bool defRepairs = Exprs.DefRepairsCluster(baseDef);
            if (defRepairs) RepairCluster(to.Signboard, to.SignboardId);

            // 6. Consume the target signboard, restore the old location's signboard, and return the old plot
            //    to its original ruined state (reverse of RepairTownCluster). The restored signboard keeps its
            //    ORIGINAL repair instructions, so a future vanilla build there repairs the plot normally.
            wd.RemoveWgoDataFromGameScene(to.Signboard.UniqueId);
            wd.AddWgoData(restored);
            if (defRepairs) UnrepairCluster(restored, from.SignboardId, from.SceneId);

            TeleportCharacter(ch, newComp);
            Plugin.Log.LogInfo($"MOVE done. newTent={newTent.UniqueId} restoredSignboard={restored.id}/{restored.UniqueId}");
        }

        /// Swap two built stalls between their locations.
        public static void Swap(StallLocation a, StallLocation b)
        {
            string why = CheckMovable(a) ?? CheckMovable(b);
            if (why != null) throw new InvalidOperationException("Cannot swap: " + why);
            if (!Town.Fits(a, b.SignboardId) || !Town.Fits(b, a.SignboardId)) throw new InvalidOperationException("Stalls do not fit each other's locations");

            var compA = a.Tent.TownBuildingWgoComponent;   // layout of location A
            var compB = b.Tent.TownBuildingWgoComponent;   // layout of location B
            string idA = compA.TownBuildingId, idB = compB.TownBuildingId;
            int tierA = compA.TierIndex, tierB = compB.TierIndex;
            var tentA = a.Tent; var tentB = b.Tent;
            var chA = a.Character; var chB = b.Character;
            string tagA = a.SignboardFormTag, tagB = b.SignboardFormTag;

            if (tierA > compB.SceneConfiguration.tierDataList.Count || tierB > compA.SceneConfiguration.tierDataList.Count)
                throw new InvalidOperationException("Tier does not exist in the other location's layout");

            Plugin.Log.LogInfo($"SWAP {idA} t{tierA} @ {a.SignboardId} <-> {idB} t{tierB} @ {b.SignboardId}");

            RemovePieces(compA, tierA, tentA);
            RemovePieces(compB, tierB, tentB);

            ClearCreatedIds(compA.SceneConfiguration);
            ClearCreatedIds(compB.SceneConfiguration);
            compA.TownBuildingId = idB; compA.TierIndex = tierB;
            compB.TownBuildingId = idA; compB.TierIndex = tierA;

            var newAtA = SpawnPieces(compA, tierB, tagA, tentA.WorldId, chB, tentB);
            var newAtB = SpawnPieces(compB, tierA, tagB, tentB.WorldId, chA, tentA);

            TeleportCharacter(chB, compA);
            TeleportCharacter(chA, compB);
            Plugin.Log.LogInfo($"SWAP done. at {a.SignboardId}: {newAtA.UniqueId}, at {b.SignboardId}: {newAtB.UniqueId}");
        }

        static void RepairCluster(WgoData signboard, string label)
        {
            var c = signboard.TownClusterRepairWgoComponent;
            if (c == null || c.Configurations == null || c.Configurations.Count == 0) { Plugin.Log.LogInfo($"Cluster repair: {label} has no repair configuration (already repaired or none)"); return; }
            Plugin.Log.LogInfo($"Cluster repair: {label} (cluster {c.Id})" + (Plugin.Verbose ? ": " + Exprs.DescribeCluster(c) : ""));
            c.DoRepairLogic();
            // Mark as used so it can never run twice for this signboard.
            NeutraliseCluster(signboard);
        }

        const float PosEpsSqr = 0.0001f;

        /// Reverse of TownClusterRepairWgoComponent.DoRepairLogic, driven by the original (content) repair config.
        static void UnrepairCluster(WgoData signboard, string label, string sceneId)
        {
            var c = signboard.TownClusterRepairWgoComponent;
            if (c?.Configurations == null || c.Configurations.Count == 0) { Plugin.Log.LogInfo($"Un-repair: {label} has no repair configuration"); return; }
            var wd = MainGame.WorldData;
            var scene = wd.GetGameSceneDataById(sceneId);
            if (Plugin.Verbose) Plugin.Log.LogInfo($"Un-repair: {label} cluster {c.Id} before: {Exprs.DescribeCluster(c)}");

            var missingWgo = new HashSet<SGuid>();
            var missingWso = new HashSet<SGuid>();
            int removedWgo = 0, removedWso = 0, resetHouses = 0;

            foreach (var k in c.Configurations)
            {
                // a) remove the objects the repair added (DoRepairLogic creates them at exact configured positions)
                foreach (var r in k.repairedWgoData)
                {
                    if (string.IsNullOrEmpty(r.wgoId)) continue;
                    var hit = scene.wgoDataList.FirstOrDefault(w => w != null && w.id == r.wgoId && (w.Position - r.position).sqrMagnitude <= PosEpsSqr);
                    if (hit != null) { wd.RemoveWgoDataFromGameScene(hit.UniqueId); removedWgo++; }
                    else Plugin.Log.LogWarning($"Un-repair: repaired WGO {r.wgoId}@{r.position} not found");
                }
                foreach (var r in k.repairedWsoData)
                {
                    if (string.IsNullOrEmpty(r.wgoId)) continue;
                    var hit = scene.wsoDataList.FirstOrDefault(w => w != null && w.id == r.wgoId && (w.Position - r.position).sqrMagnitude <= PosEpsSqr);
                    if (hit != null) { wd.RemoveWsoDataFromGameScene(hit.UniqueId); removedWso++; }
                    else Plugin.Log.LogWarning($"Un-repair: repaired WSO {r.wgoId}@{r.position} not found");
                }
                // b) collect the ruin objects the repair removed
                foreach (var u in k.destroyedWgoUniqueIds) if (wd.GetWgoData(u) == null) missingWgo.Add(u);
                // c) ruined houses: removed (Destroy mode) or repaired in place (HouseRepair mode)
                foreach (var u in k.destroyedWsoUniqueIds)
                {
                    var wso = wd.GetWsoData(u);
                    if (k.handleDestroyedWsoMode == TownCluster.HandleDestroyedWsoMode.Destroy) { if (wso == null) missingWso.Add(u); }
                    else if (k.handleDestroyedWsoMode == TownCluster.HandleDestroyedWsoMode.HouseRepair && wso != null)
                    {
                        var part = wso.GetComponentData<WsoRepairablePartData>();
                        if (part == null) continue;
                        part.ResetAllStages();
                        wso.NotifyRepairStateChanged();
                        if (part.isTownQualityAdded)
                        {
                            part.isTownQualityAdded = false;
                            MainGame.Instance.GameSave.townSystem.Quality -= wso.Definition.townQuality;
                        }
                        resetHouses++;
                    }
                }
            }

            var wgos = new List<WgoData>(); var wsos = new List<WsoData>();
            ContentIndex.CreateRestoredObjects(sceneId, missingWgo, missingWso, wgos, wsos);
            foreach (var w in wgos) wd.AddWgoData(w);
            foreach (var w in wsos) wd.AddWsoData(w);
            if (wgos.Count != missingWgo.Count || wsos.Count != missingWso.Count)
                Plugin.Log.LogWarning($"Un-repair: restored {wgos.Count}/{missingWgo.Count} ruin WGOs and {wsos.Count}/{missingWso.Count} ruin WSOs from content");

            Plugin.Log.LogInfo($"Un-repair: {label} removed {removedWgo} repaired WGOs + {removedWso} repaired WSOs, restored {wgos.Count} ruin WGOs + {wsos.Count} ruin WSOs, reset {resetHouses} houses.");
        }

        static void NeutraliseCluster(WgoData signboard)
        {
            var old = signboard.TownClusterRepairWgoComponent;
            // Replace (not mutate) - content copies share this object with the game's content template.
            signboard.TownClusterRepairWgoComponent = new TownClusterRepairWgoComponent { Id = old != null ? old.Id : 0, Configurations = new List<TownClusterData.TownClusterDataDto>() };
        }

        static void ClearCreatedIds(TownBuildingSceneConfiguration cfg)
        {
            foreach (var t in cfg.tierDataList)
                foreach (var o in new[] { t.tent, t.sign, t.yard, t.decor1, t.decor2, t.decor3 })
                    if (o != null) o.createdWgoUniqueId = SGuid.Empty;
        }

        static void RemovePieces(TownBuildingWgoComponent comp, int tier, WgoData tent)
        {
            var wd = MainGame.WorldData;
            var t = comp.SceneConfiguration.tierDataList[tier - 1];
            foreach (var o in new[] { t.yard, t.decor1, t.decor2, t.decor3, t.sign })
            {
                if (o == null || SGuid.IsNullOrEmpty(o.createdWgoUniqueId)) continue;
                if (wd.GetWgoData(o.createdWgoUniqueId) != null) wd.RemoveWgoDataFromGameScene(o.createdWgoUniqueId);
                else Plugin.Log.LogWarning($"piece {o.wgoId} {o.createdWgoUniqueId} already missing");
            }
            wd.RemoveWgoDataFromGameScene(tent.UniqueId);
        }

        /// Port of the tier branch of TownSystem.CreateTownBuildingOnWgo for an already-built stall.
        static WgoData SpawnPieces(TownBuildingWgoComponent comp, int tier, string signboardFormTag, string worldId, WgoData ch, WgoData oldTent)
        {
            var wd = MainGame.WorldData;
            var def = GameBalance.Me.GetData<TownBuildingDef>(comp.TownBuildingId);
            var t = comp.SceneConfiguration.tierDataList[tier - 1];
            string tag = signboardFormTag ?? "";

            SpawnDecor(t.yard, "t_b_yard_", tag, worldId, def);
            SpawnDecor(t.decor1, "t_b_decor_1_", tag, worldId, def);
            SpawnDecor(t.decor2, "t_b_decor_2_", tag, worldId, def);
            SpawnDecor(t.decor3, "t_b_decor_3_", tag, worldId, def);
            SpawnDecor(t.sign, "t_b_sign_", tag, worldId, def);

            var tent = new WgoData(t.tent.wgoId, t.tent.position, worldId);
            tent.Scale = t.tent.scale == Vector3.zero ? Vector3.one : t.tent.scale;
            tent.CustomTag = tag.Replace("t_b_signboard_", "t_b_tent_");
            tent.TownBuildingWgoComponent = comp;
            tent.MainWgoPartData.variationId = def.variationId;
            tent.MainWgoPartData.rotationIndex = -1;
            wd.AddWgoData(tent);
            t.tent.createdWgoUniqueId = tent.UniqueId;

            // Re-link the existing merchant, and carry over tent state like the game does on upgrade.
            ch.LinkedToTownBuildingUniqueId = tent.UniqueId;
            tent.LinkedFromTownBuildingUniqueId = ch.UniqueId;
            tent.SetGameRes(AVAILABLE_BY_TIME, oldTent.GetGameResInt(AVAILABLE_BY_TIME));
            return tent;
        }

        static void SpawnDecor(TownBuildingObjectConfiguration o, string prefix, string tag, string worldId, TownBuildingDef def)
        {
            if (o == null || string.IsNullOrEmpty(o.wgoId)) return;
            var w = new WgoData(o.wgoId, o.position, worldId);
            w.Scale = o.scale == Vector3.zero ? Vector3.one : o.scale;
            w.CustomTag = tag.Replace("t_b_signboard_", prefix);
            w.MainWgoPartData.variationId = def.variationId;
            w.MainWgoPartData.rotationIndex = -1;
            MainGame.WorldData.AddWgoData(w);
            o.createdWgoUniqueId = w.UniqueId;
        }

        static void TeleportCharacter(WgoData ch, TownBuildingWgoComponent comp)
        {
            try
            {
                var gd = MainGame.Instance.GameSave.worldData.gdPointsData.GetGDPointDataById(comp.SceneConfiguration.gdPointTent);
                if (gd == null) { Plugin.Log.LogWarning($"No GD point {comp.SceneConfiguration.gdPointTent}; merchant not teleported"); return; }
                try { WorldFX.Spawn(ch.Position, "puff_npc"); } catch { }
                ch.Position = gd.Position;
                try { WorldFX.Spawn(ch.Position, "puff_npc"); } catch { }
            }
            catch (Exception e) { Plugin.Log.LogWarning("Teleport failed: " + e.Message); }
        }
    }
}
