using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using LazyBearTechnology;

namespace GK2.StallMover
{
    /// One stall location in the town: either an empty signboard or a built stall (tent + merchant).
    public class StallLocation
    {
        public string SceneId;
        public string GdTent;               // e.g. gd_1208_tent - unique per location
        public string SignboardId;          // e.g. repair_sign_1208 - the location's signboard WGO type
        public WgoData Signboard;           // set when the location is empty
        public WgoData Tent;                // set when a stall is built here
        public WgoData Character;           // the merchant linked to Tent
        public TownBuildingWgoComponent Comp => Tent != null ? Tent.TownBuildingWgoComponent : Signboard?.TownBuildingWgoComponent;
        public bool IsBuilt => Tent != null;
        public string LocationNumber => SignboardId != null && SignboardId.StartsWith("repair_sign_") ? SignboardId.Substring("repair_sign_".Length) : (SignboardId ?? GdTent);

        public TownBuildingDef CurrentDef => IsBuilt ? GameBalance.Me.GetDataOrNull<TownBuildingDef>(Tent.TownBuildingWgoComponent.TownBuildingId) : null;

        /// The tier-1 def of the stall built here (holds craftsIn = which locations it fits).
        public TownBuildingDef BaseDef => IsBuilt ? Town.FindBaseDef(Tent.TownBuildingWgoComponent.TownBuildingId) : null;

        public string DisplayName
        {
            get
            {
                var d = BaseDef ?? CurrentDef;
                string name = d != null ? SafeL(d.id) : "?";
                return name;
            }
        }

        static string SafeL(string id) { try { return LLBase.L(id); } catch { return id; } }

        /// Location tag in "signboard form", used to derive tags of spawned pieces the same way the game does.
        public string SignboardFormTag
        {
            get
            {
                if (Signboard != null) return Signboard.CustomTag ?? "";
                string t = Tent?.CustomTag ?? "";
                return t.Replace("t_b_tent_", "t_b_signboard_");
            }
        }
    }

    public static class Town
    {
        public static bool IsVendorDef(TownBuildingDef d) => d != null && d.townBuildingType == TownBuildingType.Vendor;

        public static TownBuildingDef FindBaseDef(string buildingId)
        {
            if (string.IsNullOrEmpty(buildingId)) return null;
            foreach (var d in GameBalance.Me.townBuildingDefs)
            {
                if (d.craftsIn == null || d.craftsIn.Count == 0) continue;
                var cur = d; int guard = 0;
                while (cur != null && guard++ < 10)
                {
                    if (cur.id == buildingId) return d;
                    cur = string.IsNullOrEmpty(cur.lvlUpId) ? null : GameBalance.Me.GetDataOrNull<TownBuildingDef>(cur.lvlUpId);
                }
            }
            return null;
        }

        /// True when this signboard type can host vendor stalls at all.
        public static bool IsVendorSignboardId(string signboardId) =>
            !string.IsNullOrEmpty(signboardId) && GameBalance.Me.townBuildingDefs.Any(d => IsVendorDef(d) && d.craftsIn.Contains(signboardId));

        public static bool Fits(StallLocation stall, string signboardId)
        {
            var b = stall.BaseDef;
            return b != null && b.craftsIn.Contains(signboardId);
        }

        /// Scan one scene of the save for all vendor stall locations.
        public static List<StallLocation> Scan(string sceneId)
        {
            var result = new List<StallLocation>();
            var scene = MainGame.WorldData.GetGameSceneDataById(sceneId);
            if (scene?.wgoDataList == null) return result;

            foreach (var w in scene.wgoDataList)
            {
                if (w == null) continue;
                var tb = w.TownBuildingWgoComponent;
                var cfg = tb?.SceneConfiguration;
                if (cfg == null || cfg.tierDataList == null || cfg.tierDataList.Count == 0) continue;

                bool isPlace = false;
                try { isPlace = w.Definition != null && w.Definition.interactionType == WGODef.InteractionType.TownBuildingPlace; } catch { }

                if (isPlace && !tb.IsActive)
                {
                    if (!IsVendorSignboardId(w.id)) continue;
                    result.Add(new StallLocation { SceneId = sceneId, GdTent = cfg.gdPointTent, SignboardId = w.id, Signboard = w });
                    if (Plugin.Verbose) Plugin.Log.LogInfo($"SCAN empty {w.id} gd={cfg.gdPointTent} cluster: {Exprs.DescribeCluster(w.TownClusterRepairWgoComponent)}");
                }
                else if (tb.IsActive && IsVendorDef(tb.TownBuildingDef) && !isPlace)
                {
                    var ch = SGuid.IsNullOrEmpty(w.LinkedFromTownBuildingUniqueId) ? null : MainGame.WorldData.GetWgoData(w.LinkedFromTownBuildingUniqueId);
                    var loc = new StallLocation { SceneId = sceneId, GdTent = cfg.gdPointTent, Tent = w, Character = ch };
                    loc.SignboardId = ContentIndex.GetSignboardIdForGdTent(sceneId, cfg.gdPointTent);
                    result.Add(loc);
                }
            }
            return result;
        }

        public static bool IsCoop()
        {
            try { return LazyNetwork.IsInitialized && LazyNetwork.NetworkManager != null && LazyNetwork.NetworkManager.IsCoopGame; }
            catch { return false; }
        }
    }
}
