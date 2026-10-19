using HarmonyLib;
using Ships.Interface.Model;
using UI.Targeting;

namespace HandyBeaconTracker
{
    // While the tracker is in hand and has a target, the game's targeting HUD shows that target, exactly as it
    // shows a ship's Targeting Computer target: reticle with name and distance, edge arrow, planet poles, orbit.
    [HarmonyPatch(typeof(TargetingUpdater), "UpdateCurrentTarget")]
    static class TrackerHud
    {
        static readonly AccessTools.FieldRef<TargetingUpdater, TargetingData> Data = AccessTools.FieldRefAccess<TargetingUpdater, TargetingData>("_targetingData");

        static void Postfix(TargetingUpdater __instance)
        {
            if (HandyTrackerTool.Held == null)
                return;
            Target? target = TrackerTarget.HudTarget;
            if (!target.HasValue)
                return;
            Data(__instance).CurrentTarget = target;
        }
    }
}
