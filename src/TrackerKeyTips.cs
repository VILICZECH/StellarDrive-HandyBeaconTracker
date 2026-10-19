using System;
using System.Reflection;
using HarmonyLib;
using UI.ToolBar;
using UnityEngine;
using Object = UnityEngine.Object;

namespace HandyBeaconTracker
{
    // The key hints under the toolbar know only the Build and Paint tools; the tracker borrows their mouse buttons.
    [HarmonyPatch(typeof(ToolKeyTipsUpdater), "UpdateKeyTips")]
    static class TrackerKeyTips
    {
        static readonly AccessTools.FieldRef<ToolKeyTipsUpdater, bool> MenuOpen = AccessTools.FieldRefAccess<ToolKeyTipsUpdater, bool>("_menuIsOpened");
        static readonly AccessTools.FieldRef<ToolKeyTipsUpdater, ToolKeyTip> PrimaryTip = AccessTools.FieldRefAccess<ToolKeyTipsUpdater, ToolKeyTip>("paintTip");
        static readonly AccessTools.FieldRef<ToolKeyTipsUpdater, ToolKeyTip> SecondaryTip = AccessTools.FieldRefAccess<ToolKeyTipsUpdater, ToolKeyTip>("openMenu");
        static readonly AccessTools.FieldRef<ToolKeyTipsUpdater, ToolKeyTipUi> TipPrefab = AccessTools.FieldRefAccess<ToolKeyTipsUpdater, ToolKeyTipUi>("toolKeyTipPrefab");
        static readonly AccessTools.FieldRef<ToolKeyTipsUpdater, Transform> Wrapper = AccessTools.FieldRefAccess<ToolKeyTipsUpdater, Transform>("tipWrapper");
        static readonly MethodInfo Update = AccessTools.Method(typeof(ToolKeyTipsUpdater), "UpdateKeyTips");

        static void Postfix(ToolKeyTipsUpdater __instance)
        {
            if (HandyTrackerTool.Held == null || MenuOpen(__instance))
                return;
            Add(__instance, PrimaryTip(__instance), "Next target");
            Add(__instance, SecondaryTip(__instance), "Target list");
        }

        static void Add(ToolKeyTipsUpdater updater, ToolKeyTip source, string text)
        {
            ToolKeyTipUi prefab = TipPrefab(updater);
            Transform wrapper = Wrapper(updater);
            if (source == null || prefab == null || wrapper == null)
                return;
            var tip = new ToolKeyTip { action = source.action, actionKeyAlt = source.actionKeyAlt, actionDesc = text };
            Object.Instantiate(prefab, wrapper).Setup(tip);
        }

        public static void Refresh()
        {
            try
            {
                ToolKeyTipsUpdater updater = Object.FindAnyObjectByType<ToolKeyTipsUpdater>();
                if (updater != null && Update != null)
                    Update.Invoke(updater, null);
            }
            catch (Exception)
            {
            }
        }
    }
}
