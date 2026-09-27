using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace GK2.StallMover
{
    /// Reads the game's original world content (the same data its own save-fixer uses) to find
    /// the original signboard for a stall location, so an emptied location can be restored exactly.
    public static class ContentIndex
    {
        // sceneId -> (gdPointTent -> signboard wgo id)
        static readonly Dictionary<string, Dictionary<string, string>> signboardIdByGd = new Dictionary<string, Dictionary<string, string>>();

        public static void Clear() => signboardIdByGd.Clear();

        public static string GetSignboardIdForGdTent(string sceneId, string gdTent)
        {
            if (!signboardIdByGd.TryGetValue(sceneId, out var map))
            {
                map = new Dictionary<string, string>();
                ForEachContentSignboard(sceneId, (w, cfg) =>
                {
                    var gd = w.TownBuildingWgoComponent?.SceneConfiguration?.gdPointTent;
                    if (!string.IsNullOrEmpty(gd) && !map.ContainsKey(gd)) map[gd] = w.id;
                    return false;
                });
                signboardIdByGd[sceneId] = map;
                if (Plugin.Verbose) Plugin.Log.LogInfo($"ContentIndex: {map.Count} stall signboards indexed for scene {sceneId}");
            }
            return map.TryGetValue(gdTent ?? "", out var id) ? id : null;
        }

        /// Build a fresh, independent copy of the original signboard for this location (same UniqueId as the original).
        public static WgoData CreateRestoredSignboard(string sceneId, string gdTent)
        {
            WgoData result = null;
            ForEachContentSignboard(sceneId, (w, cfg) =>
            {
                if (w.TownBuildingWgoComponent?.SceneConfiguration?.gdPointTent != gdTent) return false;
                var copy = w.CreateDataFromMe(cfg.sceneGlobalPosition, sceneId, copySGuid: true);
                // The copy constructor shares the component with the content template - give it its own deep copy.
                copy.TownBuildingWgoComponent = CloneEmptyComponent(w.TownBuildingWgoComponent);
                copy.TownClusterRepairWgoComponent = CloneCluster(w.TownClusterRepairWgoComponent);
                result = copy;
                return true;
            });
            return result;
        }

        public static TownBuildingWgoComponent CloneEmptyComponent(TownBuildingWgoComponent src)
        {
            var c = new TownBuildingWgoComponent();
            c.SceneConfiguration = CloneConfig(src.SceneConfiguration);
            c.TierIndex = 0;
            c.TownBuildingId = null; // also sets isActive=false
            return c;
        }

        public static TownClusterRepairWgoComponent CloneCluster(TownClusterRepairWgoComponent src)
        {
            var c = new TownClusterRepairWgoComponent { Id = src != null ? src.Id : 0, Configurations = new List<TownClusterData.TownClusterDataDto>() };
            if (src?.Configurations == null) return c;
            foreach (var k in src.Configurations)
            {
                c.Configurations.Add(new TownClusterData.TownClusterDataDto
                {
                    id = k.id,
                    handleDestroyedWsoMode = k.handleDestroyedWsoMode,
                    destroyedWgoUniqueIds = new List<SGuid>(k.destroyedWgoUniqueIds),
                    destroyedWsoUniqueIds = new List<SGuid>(k.destroyedWsoUniqueIds),
                    repairedWgoData = new List<TownClusterData.ClusterWgoData>(k.repairedWgoData),
                    repairedWsoData = new List<TownClusterData.ClusterWgoData>(k.repairedWsoData),
                });
            }
            return c;
        }

        /// Fresh copies (original UniqueIds) of content WGOs/WSOs with the given ids, for restoring ruins.
        public static void CreateRestoredObjects(string sceneId, HashSet<SGuid> wgoIds, HashSet<SGuid> wsoIds, List<WgoData> wgosOut, List<WsoData> wsosOut)
        {
            if (wgoIds.Count == 0 && wsoIds.Count == 0) return;
            ForEachContentPart(sceneId, (part, cfg) =>
            {
                if (part.Wgos != null)
                    foreach (var w in part.Wgos)
                        if (w != null && wgoIds.Contains(w.UniqueId)) wgosOut.Add(w.CreateDataFromMe(cfg.sceneGlobalPosition, sceneId, copySGuid: true));
                if (part.Wsos != null)
                    foreach (var w in part.Wsos)
                        if (w != null && wsoIds.Contains(w.UniqueId)) wsosOut.Add(w.CreateDataFromMe(cfg.sceneGlobalPosition, sceneId, copySGuid: true));
                return false;
            });
        }

        public static TownBuildingSceneConfiguration CloneConfig(TownBuildingSceneConfiguration s)
        {
            var n = new TownBuildingSceneConfiguration
            {
                gdPointTent = s.gdPointTent,
                gdPointHomeOutside = s.gdPointHomeOutside,
                gdPointHomeInside = s.gdPointHomeInside,
                tierDataList = new List<TownBuildingTierSceneConfiguration>()
            };
            foreach (var t in s.tierDataList)
            {
                n.tierDataList.Add(new TownBuildingTierSceneConfiguration
                {
                    tent = CloneObj(t.tent), sign = CloneObj(t.sign), yard = CloneObj(t.yard),
                    decor1 = CloneObj(t.decor1), decor2 = CloneObj(t.decor2), decor3 = CloneObj(t.decor3)
                });
            }
            return n;
        }

        static TownBuildingObjectConfiguration CloneObj(TownBuildingObjectConfiguration o) =>
            o == null ? new TownBuildingObjectConfiguration() :
            new TownBuildingObjectConfiguration { wgoId = o.wgoId, position = o.position, scale = o.scale, createdWgoUniqueId = SGuid.Empty };

        /// Visit every TownBuildingPlace WGO in the scene's content. Stops when visitor returns true.
        static void ForEachContentSignboard(string sceneId, Func<WgoData, GameSceneConfig, bool> visitor)
        {
            ForEachContentPart(sceneId, (part, cfg) =>
            {
                if (part.Wgos == null) return false;
                foreach (var w in part.Wgos)
                {
                    if (w == null) continue;
                    bool isPlace = false;
                    try { isPlace = w.Definition != null && w.Definition.interactionType == WGODef.InteractionType.TownBuildingPlace; } catch { }
                    if (!isPlace || w.TownBuildingWgoComponent?.SceneConfiguration == null) continue;
                    if (visitor(w, cfg)) return true;
                }
                return false;
            });
        }

        /// Visit every content part of the scene (loading content temporarily if needed). Stops when visitor returns true.
        static void ForEachContentPart(string sceneId, Func<SceneWgoContentPart, GameSceneConfig, bool> visitor)
        {
            var cfg = MainGame.Instance.gameSceneConfigs.FirstOrDefault(c => c != null && c.name == sceneId);
            if (cfg?.contentDataRefs == null) { Plugin.Log.LogWarning($"ContentIndex: no GameSceneConfig for {sceneId}"); return; }

            foreach (var r in cfg.contentDataRefs)
            {
                if (r == null) continue;
                bool wasLoaded = false, stop = false;
                try
                {
                    wasLoaded = cfg.IsSceneContentDataLoaded(r);
                    if (!cfg.TryLoadSceneDataContentByRef(r, out var data) || data == null) continue;
                    foreach (var part in data.GetComponentsInChildren<SceneWgoContentPart>(true))
                    {
                        if (part == null) continue;
                        if (visitor(part, cfg)) { stop = true; break; }
                    }
                }
                catch (Exception e) { Plugin.Log.LogWarning($"ContentIndex: ref {r.AssetGUID}: {e.Message}"); }
                finally { try { if (!wasLoaded) cfg.TryUnloadSceneDataContent(r); } catch { } }
                if (stop) return;
            }
        }
    }
}
