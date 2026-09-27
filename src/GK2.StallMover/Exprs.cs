using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using LazyBearTechnology;

namespace GK2.StallMover
{
    /// Reads the text of the game's data-driven expressions (used only for logging and for deciding
    /// whether a stall build repairs its plot).
    public static class Exprs
    {
        static readonly System.Reflection.FieldInfo FUnparsed = AccessTools.Field(typeof(LazyExpressionBase), "expressionStringUnparsed");
        static readonly System.Reflection.FieldInfo FParsed = AccessTools.Field(typeof(LazyExpressionBase), "expressionString");

        public static string Text(LazyExpressionBase e)
        {
            if (e == null) return "";
            try
            {
                var u = FUnparsed?.GetValue(e) as string;
                if (!string.IsNullOrEmpty(u)) return u;
                return FParsed?.GetValue(e) as string ?? "";
            }
            catch { return "?"; }
        }

        public static string Join(IEnumerable<LazyExpressionBase> list) => list == null ? "" : string.Join(" ; ", list.Select(Text));

        public static bool DefRepairsCluster(TownBuildingDef d) =>
            d?.onCraftEndExpressions != null && d.onCraftEndExpressions.Any(e => Text(e).IndexOf("RepairTownCluster", StringComparison.OrdinalIgnoreCase) >= 0);

        public static string DescribeCluster(TownClusterRepairWgoComponent c)
        {
            if (c?.Configurations == null) return "none";
            var wd = MainGame.WorldData;
            return string.Join(" | ", c.Configurations.Select(k =>
                $"cfg{k.id} mode={k.handleDestroyedWsoMode} destroyedWgo={k.destroyedWgoUniqueIds.Count}(inSave {k.destroyedWgoUniqueIds.Count(u => wd.GetWgoData(u) != null)}) destroyedWso={k.destroyedWsoUniqueIds.Count}(inSave {k.destroyedWsoUniqueIds.Count(u => wd.GetWsoData(u) != null)}) repairedWgo={k.repairedWgoData.Count} repairedWso={k.repairedWsoData.Count}"));
        }

        public static void LogStallDefs()
        {
            foreach (var d in GameBalance.Me.townBuildingDefs.Where(x => Town.IsVendorDef(x) && x.craftsIn != null && x.craftsIn.Count > 0 && !x.id.StartsWith("test")))
                Plugin.Log.LogInfo($"DEF {d.id}: onBuildFinished=[{Join(d.onCraftEndExpressions)}] onCharCreate=[{Join(d.expressionOnCharCreate)}] repairsCluster={DefRepairsCluster(d)}");
        }
    }
}
