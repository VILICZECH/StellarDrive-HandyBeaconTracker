using System;
using System.Collections.Generic;
using Core.Values;
using HarmonyLib;
using Items.Interface.Service;
using Items.Model;
using MelonLoader;
using Players.Interface.Services;
using Players.Inventory;
using Players.Services;
using Ships.Interface.Settings;
using TMPro;
using UI.BuildMenu;
using UI.Common;
using UI.Research;
using UI.TargetingComputer;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace HandyBeaconTracker
{
    // A crafting panel left of the ship's Targeting Computer list, cloned from the research info panel:
    // the same frame, the same cost rows (have / need in the valid or invalid color) and the same action button.
    // The handheld target list (TargetMenu) does not get this panel.
    public class CraftPanel : MonoBehaviour
    {
        // Canvas units of TargetingComputerCanvas (same 3840 x 2160 scaler). The ship list is Menu 1200 x 1800
        // at (0, 120). The panel sits to its left so the list and the craft costs stay inside the same window.
        const float Width = 680f;
        const float Height = 1100f;
        const float Gap = 48f;
        const float TopPadding = 110f;

        static readonly AccessTools.FieldRef<GenericPartOpenableMenuActivator, GameObject> ResearchPrefab = AccessTools.FieldRefAccess<GenericPartOpenableMenuActivator, GameObject>("researchMenuPrefab");
        static readonly AccessTools.FieldRef<ResearchInfo, GameObject> WrapperField = AccessTools.FieldRefAccess<ResearchInfo, GameObject>("wrapper");
        static readonly AccessTools.FieldRef<ResearchInfo, Transform> ContentField = AccessTools.FieldRefAccess<ResearchInfo, Transform>("contentTransform");
        static readonly AccessTools.FieldRef<ResearchInfo, TMP_Text> TitleField = AccessTools.FieldRefAccess<ResearchInfo, TMP_Text>("techName");
        static readonly AccessTools.FieldRef<ResearchInfo, Transform> UnlockField = AccessTools.FieldRefAccess<ResearchInfo, Transform>("unlockLabel");
        static readonly AccessTools.FieldRef<ResearchInfo, Transform> CostField = AccessTools.FieldRefAccess<ResearchInfo, Transform>("costLabel");
        static readonly AccessTools.FieldRef<ResearchInfo, Button> ButtonField = AccessTools.FieldRefAccess<ResearchInfo, Button>("researchButton");
        static readonly AccessTools.FieldRef<ResearchInfo, GameColorPalette> PaletteField = AccessTools.FieldRefAccess<ResearchInfo, GameColorPalette>("palette");
        static readonly AccessTools.FieldRef<ResearchInfo, TechUnlockItem> RowPrefabField = AccessTools.FieldRefAccess<ResearchInfo, TechUnlockItem>("techUnlockItemPrefab");
        static readonly AccessTools.FieldRef<ResearchInfo, ItemCostInfo> CostPrefabField = AccessTools.FieldRefAccess<ResearchInfo, ItemCostInfo>("itemCostInfoPrefab");
        static readonly AccessTools.FieldRef<TechUnlockItem, RawImage> RowThumbnail = AccessTools.FieldRefAccess<TechUnlockItem, RawImage>("thumbnail");
        static readonly AccessTools.FieldRef<TechUnlockItem, RawImage> RowShadow = AccessTools.FieldRefAccess<TechUnlockItem, RawImage>("thumbnailShadow");
        static readonly AccessTools.FieldRef<TechUnlockItem, GameObject> RowIcon = AccessTools.FieldRefAccess<TechUnlockItem, GameObject>("thumbnailContainer");
        static readonly AccessTools.FieldRef<TechUnlockItem, Image> RowBackground = AccessTools.FieldRefAccess<TechUnlockItem, Image>("background");
        static readonly AccessTools.FieldRef<TechUnlockItem, TMP_Text> RowLabel = AccessTools.FieldRefAccess<TechUnlockItem, TMP_Text>("label");

        static bool _warned;

        GameColorPalette _palette;
        Button _button;
        Transform _costLabel;
        TechUnlockItem _requirement;
        TMP_Text _status;
        readonly List<GameObject> _costRows = new List<GameObject>();
        List<ItemDrop> _cost;
        float _nextCheck;
        float _waitingUntil;
        string _message;
        bool _messageGood;
        float _messageUntil;

        public static void Attach(TargetingComputerCanvas canvas)
        {
            try
            {
                if (!TrackerItem.Ready || canvas == null || canvas.GetComponent<TargetMenu>() != null)
                    return;
                if (canvas.GetComponentInChildren<CraftPanel>(true) != null)
                    return;

                var root = canvas.transform as RectTransform;
                var menu = canvas.transform.Find("Menu") as RectTransform;
                ResearchInfo source = ResearchPanel();
                if (root == null || source == null)
                {
                    Warn("The crafting panel could not find the Targeting Computer menu or the research panel.");
                    return;
                }

                // Built inactive, so ItemCostInfo subscribes to inventory changes when it first turns on.
                var holder = new GameObject("HandyBeaconTrackerCraftHolder");
                holder.SetActive(false);
                ResearchInfo info = Object.Instantiate(source, holder.transform);
                info.gameObject.name = "HandyBeaconTrackerCrafting";
                CraftPanel panel = info.gameObject.AddComponent<CraftPanel>();
                if (!panel.Build(info))
                {
                    Object.Destroy(holder);
                    Warn("The crafting panel could not be built from the research panel.");
                    return;
                }

                var rect = (RectTransform)info.transform;
                rect.SetParent(root, false);
                rect.SetAsLastSibling();
                rect.anchorMin = new Vector2(0.5f, 0.5f);
                rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.sizeDelta = new Vector2(Width, Height);
                Vector2 menuPos = menu != null ? menu.anchoredPosition : new Vector2(0f, 120f);
                float menuHalf = menu != null ? menu.rect.width * 0.5f : 600f;
                rect.anchoredPosition = new Vector2(menuPos.x - menuHalf - Gap - Width * 0.5f, menuPos.y);
                rect.localScale = Vector3.one;
                Object.Destroy(holder);
            }
            catch (Exception ex)
            {
                Warn("The crafting panel failed: " + ex);
            }
        }

        static ResearchInfo ResearchPanel()
        {
            GenericPartOpenableMenuActivator activator = Object.FindAnyObjectByType<GenericPartOpenableMenuActivator>(FindObjectsInactive.Include);
            GameObject prefab = activator != null ? ResearchPrefab(activator) : null;
            return prefab != null ? prefab.GetComponentInChildren<ResearchInfo>(true) : null;
        }

        bool Build(ResearchInfo info)
        {
            _palette = PaletteField(info);
            Transform content = ContentField(info);
            TMP_Text title = TitleField(info);
            Transform unlock = UnlockField(info);
            GameObject wrapper = WrapperField(info);
            _costLabel = CostField(info);
            _button = ButtonField(info);
            TechUnlockItem rowPrefab = RowPrefabField(info);
            ItemCostInfo costPrefab = CostPrefabField(info);
            if (content == null || title == null || wrapper == null || _costLabel == null || _button == null || rowPrefab == null || costPrefab == null)
                return false;

            // The prefab copy comes with sample rows.
            foreach (TechUnlockItem row in GetComponentsInChildren<TechUnlockItem>(true))
                DestroyImmediate(row.gameObject);
            foreach (ItemCostInfo row in GetComponentsInChildren<ItemCostInfo>(true))
                DestroyImmediate(row.gameObject);

            var contentRect = (RectTransform)content;
            contentRect.anchorMin = new Vector2(0f, 1f);
            contentRect.anchorMax = new Vector2(1f, 1f);
            contentRect.pivot = new Vector2(0.5f, 1f);
            contentRect.anchoredPosition = new Vector2(0f, -TopPadding);

            Silence(title.gameObject);
            title.text = "Crafting";
            if (unlock != null)
                unlock.gameObject.SetActive(false);

            Texture2D icon = null;
            try
            {
                icon = GameServices.Get<IItemThumbnailProvider>()?.GetThumbnail(TrackerItem.Id);
            }
            catch (Exception)
            {
            }
            TechUnlockItem item = Instantiate(rowPrefab, content);
            item.transform.SetSiblingIndex(_costLabel.GetSiblingIndex());
            Row(item, TrackerItem.Name, icon);

            _requirement = Instantiate(rowPrefab, content);
            _requirement.transform.SetSiblingIndex(_costLabel.GetSiblingIndex());
            PartSettings computer = TrackerRecipe.TargetingComputer();
            if (computer != null)
            {
                try
                {
                    _requirement.InitializePart(computer);
                }
                catch (Exception)
                {
                }
            }
            Row(_requirement, "Research the " + (computer != null && !string.IsNullOrEmpty(computer.fullLabel) ? computer.fullLabel : "Targeting Computer") + " first", null);
            if (_palette != null)
                RowLabel(_requirement).color = _palette.invalidColor;

            Silence(_costLabel.gameObject);
            _cost = TrackerRecipe.Cost();
            int index = _costLabel.GetSiblingIndex();
            foreach (ItemDrop drop in _cost)
            {
                ItemCostInfo row = Instantiate(costPrefab, content);
                row.transform.SetSiblingIndex(++index);
                row.InitializeItemCost(drop);
                _costRows.Add(row.gameObject);
            }

            Silence(_button.gameObject);
            _button.onClick = new Button.ButtonClickedEvent();
            _button.onClick.AddListener(OnCraft);
            ActionButtonStylization style = _button.GetComponent<ActionButtonStylization>();
            TMP_Text label = style != null && style.buttonText != null ? style.buttonText : _button.GetComponentInChildren<TMP_Text>(true);
            if (label != null)
                label.text = "Craft";

            TMP_Text costText = _costLabel.GetComponent<TMP_Text>();
            if (costText != null)
            {
                GameObject status = Instantiate(costText.gameObject, wrapper.transform);
                status.name = "Status";
                Silence(status);
                foreach (LayoutElement element in status.GetComponents<LayoutElement>())
                    element.ignoreLayout = true;
                _status = status.GetComponent<TMP_Text>();
                var rect = (RectTransform)status.transform;
                rect.anchorMin = new Vector2(0.5f, 0f);
                rect.anchorMax = new Vector2(0.5f, 0f);
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.sizeDelta = new Vector2(Width - 80f, 60f);
                rect.anchoredPosition = new Vector2(0f, 205f);
                _status.alignment = TextAlignmentOptions.Center;
                _status.fontSize = costText.fontSize * 0.8f;
                _status.textWrappingMode = TextWrappingModes.Normal;
                _status.text = "";
            }
            return true;
        }

        void Row(TechUnlockItem row, string text, Texture texture)
        {
            foreach (EventTrigger trigger in row.GetComponentsInChildren<EventTrigger>(true))
                DestroyImmediate(trigger);
            Silence(row.gameObject);
            if (texture != null)
            {
                RowThumbnail(row).texture = texture;
                RowShadow(row).texture = texture;
                GameObject container = RowIcon(row);
                if (container != null)
                    container.SetActive(true);
            }
            TMP_Text label = RowLabel(row);
            if (label != null)
                label.text = text;
            Image background = RowBackground(row);
            if (background != null)
            {
                Color color = background.color;
                color.a = 0f;
                background.color = color;
            }
        }

        // LocalizeStringEvent writes its own text on enable, over ours.
        static void Silence(GameObject root)
        {
            foreach (Behaviour behaviour in root.GetComponentsInChildren<Behaviour>(true))
            {
                if (behaviour != null && behaviour.GetType().Name == "LocalizeStringEvent")
                    behaviour.enabled = false;
            }
        }

        void OnEnable()
        {
            CraftNet.Result += OnResult;
            _nextCheck = 0f;
        }

        void OnDisable()
        {
            CraftNet.Result -= OnResult;
        }

        void Update()
        {
            if (Time.unscaledTime < _nextCheck)
                return;
            _nextCheck = Time.unscaledTime + 0.2f;

            bool unlocked = TrackerRecipe.Unlocked();
            bool free = FreeBuild();
            bool enough = free || HasMaterials();
            bool room = HasRoom(free);
            bool waiting = Time.unscaledTime < _waitingUntil;

            if (_requirement != null)
                _requirement.gameObject.SetActive(!unlocked);
            if (_costLabel != null)
                _costLabel.gameObject.SetActive(!free);
            foreach (GameObject row in _costRows)
            {
                if (row != null)
                    row.SetActive(!free);
            }
            _button.interactable = TrackerItem.Ready && unlocked && enough && room && !waiting;

            if (_status == null)
                return;
            string text;
            bool good = false;
            if (_message != null && Time.unscaledTime < _messageUntil)
            {
                text = _message;
                good = _messageGood;
            }
            else if (!unlocked)
                text = "Needs the Targeting Computer research.";
            else if (!enough)
                text = "Not enough materials.";
            else if (!room)
                text = "No free inventory slot.";
            else
            {
                text = free ? "Free in creative." : "";
                good = true;
            }
            _status.text = text;
            if (_palette != null)
                _status.color = good ? _palette.validColor : _palette.invalidColor;
        }

        void OnCraft()
        {
            if (!CraftNet.RequestCraft())
            {
                Show("Not connected to the game.", false);
                return;
            }
            _waitingUntil = Time.unscaledTime + 3f;
            _nextCheck = 0f;
        }

        void OnResult(byte code)
        {
            _waitingUntil = 0f;
            switch (code)
            {
                case CraftNet.Crafted:
                    Show("Crafted a " + TrackerItem.Name + ".", true);
                    break;
                case CraftNet.Locked:
                    Show("Needs the Targeting Computer research.", false);
                    break;
                case CraftNet.Missing:
                    Show("Not enough materials.", false);
                    break;
                case CraftNet.Full:
                    Show("No free inventory slot. Nothing was used.", false);
                    break;
                default:
                    Show("Crafting failed.", false);
                    break;
            }
        }

        void Show(string message, bool good)
        {
            _message = message;
            _messageGood = good;
            _messageUntil = Time.unscaledTime + 3f;
            _nextCheck = 0f;
        }

        static bool FreeBuild()
        {
            PlayersClientTracker players = GameServices.Get<PlayersClientTracker>();
            return players != null && players.IsLocalPlayerUsingFreeBuildCheat();
        }

        bool HasMaterials()
        {
            IInventoryProvider inventory = GameServices.Get<IInventoryProvider>();
            if (inventory == null || _cost == null)
                return false;
            foreach (ItemDrop drop in _cost)
            {
                if (!inventory.HasItems(drop))
                    return false;
            }
            return true;
        }

        // An empty slot, or a material stack that the price uses up and so frees.
        bool HasRoom(bool free)
        {
            if (HasEmptySlot(GameServices.Get<IPlayerToolbarProvider>(), InventoryConstants.ToolBarSize)
                || HasEmptySlot(GameServices.Get<IPlayerMainInventoryProvider>(), InventoryConstants.GridSize))
                return true;
            if (free || _cost == null)
                return false;
            IInventoryProvider inventory = GameServices.Get<IInventoryProvider>();
            if (inventory == null)
                return true;
            foreach (ItemDrop drop in _cost)
            {
                if (inventory.GetItemQuantity(drop.ItemId) == drop.Quantity)
                    return true;
            }
            return false;
        }

        static bool HasEmptySlot(IGenericInventoryProvider provider, int size)
        {
            if (provider == null)
                return false;
            for (int i = 0; i < size; i++)
            {
                if (!provider.GetSlot((byte)i).HasItem)
                    return true;
            }
            return false;
        }

        static void Warn(string message)
        {
            if (_warned)
                return;
            _warned = true;
            MelonLogger.Warning(message);
        }
    }

    [HarmonyPatch(typeof(TargetingComputerCanvas), "Start")]
    static class CraftPanelOnShipTracker
    {
        static void Postfix(TargetingComputerCanvas __instance)
        {
            CraftPanel.Attach(__instance);
        }
    }
}
