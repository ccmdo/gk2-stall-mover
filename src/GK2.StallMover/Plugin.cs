using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using LazyBearTechnology;

namespace GK2.StallMover
{
    [BepInPlugin(Guid, Name, Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "steven.gk2.stallmover";
        public const string Name = "Stall Mover";
        public const string Version = "0.4.1";

        internal static ManualLogSource Log;
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<bool> VerboseLogging;
        internal static ConfigEntry<string> TextRelocateHere;
        internal static ConfigEntry<string> TextSwapWith;
        internal static ConfigEntry<string> TextCancel;

        internal static bool Verbose => VerboseLogging != null && VerboseLogging.Value;

        void Awake()
        {
            Log = Logger;
            Enabled = Config.Bind("General", "Enabled", true,
                "Enable relocating and swapping town merchant stalls.");
            TextRelocateHere = Config.Bind("Text", "RelocateHere", "Relocate",
                "Option shown at an empty stall location (moves a built stall here).");
            TextSwapWith = Config.Bind("Text", "SwapWith", "Swap",
                "Option added to a stall merchant's dialogue (swaps this stall with another).");
            TextCancel = Config.Bind("Text", "Cancel", "Cancel",
                "Last option in the stall picker.");
            VerboseLogging = Config.Bind("Debug", "VerboseLogging", false,
                "Write detailed diagnostics to the BepInEx log (every dialogue, stall scans, stall build expressions).");

            new Harmony(Guid).PatchAll(typeof(Plugin).Assembly);
            Log.LogInfo($"{Name} {Version} loaded");
        }
    }

    /// Menu flow, built on the game's own dialogue bubble.
    public static class Ui
    {
        public const string OptBuild = "hint_build";       // the game's own "Build" answer
        public const string OptLeave = "common_leave";     // the game's own "Leave" answer

        static string RelocateHere => Plugin.TextRelocateHere.Value;
        public static string SwapWith => Plugin.TextSwapWith.Value;
        static string Cancel => Plugin.TextCancel.Value;

        static bool busy;

        public static void ShowChoice(List<string> options, WgoData participant, Action<string> onChosen)
        {
            var answers = options.Select(o => new AnswerVisualData { id = o, hiddenByDefault = false, answerData = new AnswerData() }).ToList();
            var pc = MainGame.PlayerController;
            pc.SetControlTakenType(TakenControlType.ByFlow, isEnabled: false);
            bool done = false;
            Patches.Bypass = true;
            try
            {
                Bubble.ShowMultiAnswer(answers, pc.BubblePoint, participant, chosen =>
                {
                    done = true;
                    pc.SetControlTakenType(TakenControlType.ByFlow, isEnabled: true);
                    try { onChosen(chosen); } catch (Exception e) { Plugin.Log.LogError("Menu: " + e); }
                }, () =>
                {
                    if (!done) pc.SetControlTakenType(TakenControlType.ByFlow, isEnabled: true);
                });
            }
            finally { Patches.Bypass = false; }
        }

        /// Empty location: "Relocate" -> pick a built stall.
        public static void OpenRelocateMenu(StallLocation target, List<StallLocation> candidates, Action openBuild)
        {
            var opts = new List<string> { RelocateHere, OptBuild, OptLeave };
            ShowChoice(opts, target.Signboard, chosen =>
            {
                if (chosen == OptBuild) { LazyTimer.AddTimer(0.1f, openBuild); return; }
                if (chosen != RelocateHere) return;
                LazyTimer.AddTimer(0.1f, () => PickStall(candidates, target.Signboard, s => RunWithFade(() => Relocator.Move(s, target))));
            });
        }

        /// Built stall: "Swap" -> pick another built stall.
        public static void OpenSwapPicker(StallLocation here, List<StallLocation> candidates)
        {
            PickStall(candidates, here.Character, other => RunWithFade(() => Relocator.Swap(here, other)));
        }

        static void PickStall(List<StallLocation> candidates, WgoData participant, Action<StallLocation> onPick)
        {
            var labels = new Dictionary<string, StallLocation>();
            foreach (var c in candidates)
            {
                string label = c.DisplayName;
                if (labels.ContainsKey(label) || label == Cancel) label = $"{label} ({c.LocationNumber})";
                labels[label] = c;
            }
            var opts = labels.Keys.ToList();
            opts.Add(Cancel);
            ShowChoice(opts, participant, chosen =>
            {
                if (labels.TryGetValue(chosen, out var s)) onPick(s);
            });
        }

        static void RunWithFade(Action op)
        {
            if (busy) return;
            busy = true;
            var pc = MainGame.PlayerController;
            UIFade fade = null;
            try { fade = LazyUI.Get<UIFade>(); } catch { }
            pc.SetControlTakenType(TakenControlType.ByFlow, isEnabled: false);

            void Finish()
            {
                pc.SetControlTakenType(TakenControlType.ByFlow, isEnabled: true);
                busy = false;
            }
            void Do()
            {
                try { op(); }
                catch (Exception e) { Plugin.Log.LogError("Relocation failed: " + e); }
            }

            if (fade == null) { Do(); Finish(); return; }
            fade.FadeIn(0.6f, () =>
            {
                Do();
                LazyTimer.AddTimer(1.0f, () => fade.FadeOut(0.6f, Finish));
            });
        }
    }

    [HarmonyPatch]
    public static class Patches
    {
        internal static bool Bypass;

        static readonly AccessTools.FieldRef<WGOInteractionHandlerBase, Wgo> AssignedWgo =
            AccessTools.FieldRefAccess<WGOInteractionHandlerBase, Wgo>("assignedWgo");

        static bool Allowed() => Plugin.Enabled.Value && !Town.IsCoop() && MainGame.Instance?.GameSave != null;

        /// Interacting with an empty stall location: offer "Relocate" when a compatible stall exists.
        [HarmonyPrefix]
        [HarmonyPatch(typeof(TownBuildingPlaceInteractionHandler), nameof(TownBuildingPlaceInteractionHandler.Interact))]
        static bool PlaceInteract(TownBuildingPlaceInteractionHandler __instance, PlayerController interactor, ref bool __result)
        {
            if (Bypass) return true;
            try
            {
                if (!Allowed()) return true;
                var data = AssignedWgo(__instance)?.Data;
                if (data == null || data.TownBuildingWgoComponent.IsActive || data.CraftComponent.IsStarted) return true;
                if (!Town.IsVendorSignboardId(data.id)) return true;

                var all = Town.Scan(data.WorldId);
                var target = all.FirstOrDefault(l => l.Signboard != null && l.Signboard.UniqueId == data.UniqueId);
                if (target == null) return true;
                var candidates = all.Where(l => l.IsBuilt && Relocator.CheckMovable(l) == null && Town.Fits(l, target.SignboardId)).ToList();
                if (candidates.Count == 0) return true;

                GlobalEventsSystem.FireTrigger(GlobalEventsSystem.Event.Type.Interaction, AssignedWgo(__instance).Id ?? "");
                Ui.OpenRelocateMenu(target, candidates, () =>
                {
                    Bypass = true;
                    try { __instance.Interact(interactor); } finally { Bypass = false; }
                });
                __result = true;
                return false;
            }
            catch (Exception e) { Plugin.Log.LogError("PlaceInteract: " + e); Bypass = false; return true; }
        }

        static readonly string[] LeaveHints = { "leave", "bye", "exit", "goodbye" };

        /// Every dialogue bubble: add "Swap" to a stall merchant's dialogue when a swap partner exists.
        [HarmonyPrefix]
        [HarmonyPatch(typeof(Bubble), nameof(Bubble.ShowMultiAnswer))]
        static void ShowMultiAnswer(ref List<AnswerVisualData> answers, WgoData dialogParticipant, ref Action<string> onChosen)
        {
            if (Bypass) return;
            try
            {
                if (Plugin.Verbose)
                    Plugin.Log.LogInfo($"DIALOGUE participant={dialogParticipant?.id} answers=[{string.Join(", ", answers?.Select(a => a?.id) ?? new string[0])}]");

                if (!Allowed() || dialogParticipant == null || answers == null) return;
                if (SGuid.IsNullOrEmpty(dialogParticipant.LinkedToTownBuildingUniqueId)) return;
                var tent = MainGame.WorldData.GetWgoData(dialogParticipant.LinkedToTownBuildingUniqueId);
                if (tent?.TownBuildingWgoComponent == null || !tent.TownBuildingWgoComponent.IsActive || !Town.IsVendorDef(tent.TownBuildingWgoComponent.TownBuildingDef)) return;

                var all = Town.Scan(tent.WorldId);
                var here = all.FirstOrDefault(l => l.Tent != null && l.Tent.UniqueId == tent.UniqueId);
                if (here == null || Relocator.CheckMovable(here) != null) return;
                var candidates = all.Where(l => l != here && l.IsBuilt && Relocator.CheckMovable(l) == null && Town.Fits(l, here.SignboardId) && Town.Fits(here, l.SignboardId)).ToList();
                if (candidates.Count == 0) return;

                var leave = answers.LastOrDefault(a => a?.id != null && LeaveHints.Any(h => a.id.IndexOf(h, StringComparison.OrdinalIgnoreCase) >= 0));
                if (leave == null) { if (Plugin.Verbose) Plugin.Log.LogInfo("Merchant dialogue has no recognisable leave option; not adding Swap."); return; }
                string swap = Ui.SwapWith;
                if (answers.Any(a => a?.id == swap)) return;

                var list = new List<AnswerVisualData>(answers);
                list.Insert(list.IndexOf(leave), new AnswerVisualData { id = swap, hiddenByDefault = false, answerData = new AnswerData() });
                answers = list;

                var original = onChosen;
                string leaveId = leave.id;
                onChosen = chosen =>
                {
                    if (chosen == swap)
                    {
                        original?.Invoke(leaveId); // let the merchant's dialogue graph end normally
                        LazyTimer.AddTimer(0.3f, () =>
                        {
                            try { Ui.OpenSwapPicker(here, candidates); } catch (Exception e) { Plugin.Log.LogError("Swap picker: " + e); }
                        });
                    }
                    else original?.Invoke(chosen);
                };
            }
            catch (Exception e) { Plugin.Log.LogError("ShowMultiAnswer patch: " + e); }
        }

        static object lastSave;
        static bool loggedDefs;

        /// Reset per-save caches when a different save is loaded.
        [HarmonyPostfix]
        [HarmonyPatch(typeof(MainGame), "Update")]
        static void MainGameUpdate()
        {
            var s = MainGame.Instance?.GameSave;
            if (!ReferenceEquals(s, lastSave)) { lastSave = s; ContentIndex.Clear(); loggedDefs = false; }
            if (Plugin.Verbose && !loggedDefs && s != null && GameBalance.Me != null)
            {
                loggedDefs = true;
                try { Exprs.LogStallDefs(); } catch (Exception e) { Plugin.Log.LogWarning("LogStallDefs: " + e.Message); }
            }
        }
    }
}
