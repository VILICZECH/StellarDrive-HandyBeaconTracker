using System;
using System.Collections.Generic;
using HarmonyLib;
using MelonLoader;
using Players.LocalClient;
using Ships.Interface.Model;
using Ships.Interface.Services;
using UI.Common;
using UI.Interface.Model;
using UI.Research;
using UI.TargetingComputer;
using UnityEngine;
using WorldTracking.Interface.Values;
using Object = UnityEngine.Object;

namespace HandyBeaconTracker
{
    // The Targeting Computer's own target list, opened from the hand like the Paint Tool's color menu:
    // the player keeps walking, looking and acting stay off until the list closes.
    public class TargetMenu : MonoBehaviour, IClosableMenu
    {
        static readonly AccessTools.FieldRef<GenericPartOpenableMenuActivator, GameObject> PrefabField = AccessTools.FieldRefAccess<GenericPartOpenableMenuActivator, GameObject>("targetingMenuPrefab");
        static readonly AccessTools.FieldRef<TargetingComputerCanvas, TargetingComputerButton> ButtonPrefab = AccessTools.FieldRefAccess<TargetingComputerCanvas, TargetingComputerButton>("buttonPrefab");
        static readonly AccessTools.FieldRef<TargetingComputerCanvas, Transform> ButtonsList = AccessTools.FieldRefAccess<TargetingComputerCanvas, Transform>("buttonsList");
        static readonly AccessTools.FieldRef<TargetingComputerCanvas, List<TargetingComputerButton>> Buttons = AccessTools.FieldRefAccess<TargetingComputerCanvas, List<TargetingComputerButton>>("_buttons");

        static TargetMenu _open;
        static bool _warned;

        bool _tracked;
        bool _closed;
        bool _shown;
        Target? _marked;

        public static bool IsOpen => _open != null;

        public static void Toggle()
        {
            if (_open != null)
                _open.Close();
            else
                Open();
        }

        public static void CloseIfOpen()
        {
            if (_open != null)
                _open.Close();
        }

        public static void SceneLoaded()
        {
            _open = null;
        }

        static void Open()
        {
            GenericPartOpenableMenuActivator activator = Object.FindAnyObjectByType<GenericPartOpenableMenuActivator>(FindObjectsInactive.Include);
            GameObject prefab = activator != null ? PrefabField(activator) : null;
            if (prefab == null)
            {
                if (!_warned)
                {
                    _warned = true;
                    MelonLogger.Warning("The Targeting Computer's target list was not found.");
                }
                return;
            }

            // The marker must exist before TargetingComputerCanvas.Start, so the copy is made inactive.
            var holder = new GameObject("HandyBeaconTrackerMenuHolder");
            holder.SetActive(false);
            GameObject menu = Object.Instantiate(prefab, holder.transform);
            menu.name = "HandyBeaconTrackerTargets";
            _open = menu.AddComponent<TargetMenu>();
            menu.transform.SetParent(activator.transform, false);
            Object.Destroy(holder);
        }

        void OnEnable()
        {
            MenuTracker tracker = GameServices.Get<MenuTracker>();
            if (tracker != null && !_tracked)
            {
                tracker.TrackMenu(this);
                _tracked = true;
            }
            Controls(true);
        }

        public void Close()
        {
            if (_closed)
                return;
            _closed = true;
            Release();
            Destroy(gameObject);
        }

        void OnDestroy()
        {
            Release();
        }

        void Release()
        {
            if (_tracked)
            {
                _tracked = false;
                GameServices.Get<MenuTracker>()?.UntrackMenu(this);
                Controls(false);
            }
            if (_open == this)
                _open = null;
        }

        // The paint menu flag switches off looking, tool selection and the other actions; the right click stays.
        static void Controls(bool menuOpen)
        {
            PlayerControlsActivator controls = Object.FindAnyObjectByType<PlayerControlsActivator>();
            if (controls != null)
                controls.OnPaintMenuToggle(menuOpen);
        }

        public void Fill(TargetingComputerCanvas canvas)
        {
            ITargetProvisionService provision = GameServices.Get<ITargetProvisionService>();
            TargetingComputerButton prefab = ButtonPrefab(canvas);
            Transform list = ButtonsList(canvas);
            List<TargetingComputerButton> buttons = Buttons(canvas);
            if (provision == null || prefab == null || list == null || buttons == null)
                return;

            Vector3 from = HandyTrackerTool.Held != null ? HandyTrackerTool.Held.transform.position : Vector3.zero;
            foreach (Target target in TrackerTarget.SortedTargets(from))
            {
                TargetingComputerButton button = Object.Instantiate(prefab, list);
                button.InitializeTarget(new TargetButtonData
                {
                    Target = target,
                    Name = provision.GetTargetName(target),
                    Distance = provision.CalculateTargetDistance(target, ClientOrigin.LocalVisualSpace, from)
                });
                buttons.Add(button);
            }
            _shown = false;
            Mark(canvas);
        }

        public void Mark(TargetingComputerCanvas canvas)
        {
            Target? current = TrackerTarget.Current;
            if (_shown && Nullable.Equals(current, _marked))
                return;
            _shown = true;
            _marked = current;
            List<TargetingComputerButton> buttons = Buttons(canvas);
            if (buttons == null)
                return;
            foreach (TargetingComputerButton button in buttons)
            {
                if (button == null)
                    continue;
                if (current.HasValue && button.Target == current.Value)
                    button.MarkSelected();
                else
                    button.MarkUnselected();
            }
        }
    }

    [HarmonyPatch(typeof(TargetingComputerCanvas), "Start")]
    static class TargetMenuStart
    {
        static bool Prefix(TargetingComputerCanvas __instance)
        {
            TargetMenu menu = __instance.GetComponent<TargetMenu>();
            if (menu == null)
                return true;
            menu.Fill(__instance);
            return false;
        }
    }

    [HarmonyPatch(typeof(TargetingComputerCanvas), nameof(TargetingComputerCanvas.SetTarget))]
    static class TargetMenuSetTarget
    {
        static bool Prefix(TargetingComputerCanvas __instance, Target target)
        {
            if (__instance.GetComponent<TargetMenu>() == null)
                return true;
            TrackerTarget.Toggle(target);
            return false;
        }
    }

    [HarmonyPatch(typeof(TargetingComputerCanvas), "Update")]
    static class TargetMenuUpdate
    {
        static bool Prefix(TargetingComputerCanvas __instance)
        {
            TargetMenu menu = __instance.GetComponent<TargetMenu>();
            if (menu == null)
                return true;
            menu.Mark(__instance);
            return false;
        }
    }
}
