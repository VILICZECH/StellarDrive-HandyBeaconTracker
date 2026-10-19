using System;
using System.Collections.Generic;
using HarmonyLib;
using Items.Model;
using MelonLoader;
using UnityEngine;
using Object = UnityEngine.Object;

namespace HandyBeaconTracker
{
    // The tracker is an item with a tool prefab, added to the game's item list at run time.
    // Item ids 0 to 3 are the game's tools and 100 to 112 its materials.
    static class TrackerItem
    {
        public const uint Id = 4300;
        public const string Name = "Handy Beacon Tracker";
        const uint BuildToolId = 0;
        const uint PaintToolId = 3;

        static readonly AccessTools.FieldRef<ItemSettingsList, ItemSettings[]> Items = AccessTools.FieldRefAccess<ItemSettingsList, ItemSettings[]>("items");
        static readonly AccessTools.FieldRef<ItemSettingsList, Dictionary<uint, ItemSettings>> Lookup = AccessTools.FieldRefAccess<ItemSettingsList, Dictionary<uint, ItemSettings>>("_dict");

        static ItemSettings _settings;
        static ToolSettings _tool;
        static GameObject _holder;
        static bool _warned;

        public static GameObject Template { get; private set; }

        public static bool Ready => _settings != null && _tool != null && Template != null;

        // The item list is an asset that is loaded again with the scene, so this runs on every MainGame load.
        public static bool EnsureRegistered()
        {
            ItemSettingsList[] lists = Resources.FindObjectsOfTypeAll<ItemSettingsList>();
            if (lists.Length == 0)
                return false;
            if (!EnsureSettings(lists))
                return false;
            foreach (ItemSettingsList list in lists)
                Register(list);
            return true;
        }

        static bool EnsureSettings(ItemSettingsList[] lists)
        {
            if (_settings != null && _tool != null && Template != null)
                return true;

            ItemSettings build = Find(lists, BuildToolId);
            ItemSettings paint = Find(lists, PaintToolId);
            GameObject buildPrefab = build != null && build.toolSettings != null ? build.toolSettings.prefab : null;
            if (buildPrefab == null)
            {
                if (!_warned)
                {
                    _warned = true;
                    MelonLogger.Warning("The Build Tool prefab was not found yet, so the tracker cannot copy its look.");
                }
                return false;
            }

            try
            {
                if (_holder == null)
                {
                    // Active, so the game finds ToolVisuals on the template, and far below the scene, so nobody sees it.
                    _holder = new GameObject("HandyBeaconTrackerTemplates");
                    _holder.transform.position = new Vector3(0f, -10000f, 0f);
                    Object.DontDestroyOnLoad(_holder);
                }
                if (Template == null)
                {
                    GameObject paintPrefab = paint != null && paint.toolSettings != null ? paint.toolSettings.prefab : null;
                    Template = TrackerLook.BuildTemplate(buildPrefab, paintPrefab, _holder.transform);
                }

                if (_tool == null)
                {
                    _tool = ScriptableObject.CreateInstance<ToolSettings>();
                    _tool.name = "HandyBeaconTrackerTool";
                    _tool.hideFlags = HideFlags.DontUnloadUnusedAsset;
                }
                _tool.prefab = Template;

                if (_settings == null)
                {
                    _settings = ScriptableObject.CreateInstance<ItemSettings>();
                    _settings.name = "HandyBeaconTrackerItem";
                    _settings.hideFlags = HideFlags.DontUnloadUnusedAsset;
                    _settings.id = Id;
                    _settings.itemName = Name;
                    _settings.mass = build.mass;
                    _settings.maxStackSize = 1;
                }
                _settings.toolSettings = _tool;
                // The game drops an item into the world only when it has an item object; the Build Tool uses its tool prefab too.
                _settings.itemObject = Template;
                MelonLogger.Msg("The Handy Beacon Tracker is item " + Id + ".");
                return true;
            }
            catch (Exception ex)
            {
                MelonLogger.Error("The Handy Beacon Tracker could not be built.", ex);
                return false;
            }
        }

        static void Register(ItemSettingsList list)
        {
            ItemSettings[] items = Items(list) ?? new ItemSettings[0];
            foreach (ItemSettings item in items)
            {
                if (item == null || item.id != Id)
                    continue;
                if (item != _settings)
                    MelonLogger.Warning("Item id " + Id + " is already taken by " + item.itemName + ". The Handy Beacon Tracker is not added.");
                return;
            }
            var extended = new ItemSettings[items.Length + 1];
            Array.Copy(items, extended, items.Length);
            extended[items.Length] = _settings;
            Items(list) = extended;
            Lookup(list) = null;
        }

        static ItemSettings Find(ItemSettingsList[] lists, uint id)
        {
            foreach (ItemSettingsList list in lists)
            {
                ItemSettings[] items = Items(list);
                if (items == null)
                    continue;
                foreach (ItemSettings item in items)
                {
                    if (item != null && item.id == id)
                        return item;
                }
            }
            return null;
        }
    }
}
