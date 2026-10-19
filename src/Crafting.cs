using System;
using System.Collections.Generic;
using FishNet;
using FishNet.Broadcast;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Serializing;
using FishNet.Transporting;
using HarmonyLib;
using Items.Model;
using MelonLoader;
using Players;
using Players.Inventory;
using Players.Services;
using Research.Interface.Services;
using Ships.Interface.Services;
using Ships.Interface.Settings;

namespace HandyBeaconTracker
{
    // Survival price is 10 iron and 4 copper. The Targeting Computer (part 39) must already be researched.
    static class TrackerRecipe
    {
        public const ushort TargetingComputerId = 39;
        public const uint IronId = 101;
        public const uint CopperId = 112;

        static readonly ItemDrop[] Price =
        {
            new ItemDrop { ItemId = IronId, Quantity = 10 },
            new ItemDrop { ItemId = CopperId, Quantity = 4 }
        };

        static PartSettings _computer;
        static bool _looked;

        public static void Reset()
        {
            _computer = null;
            _looked = false;
        }

        public static PartSettings TargetingComputer()
        {
            if (_computer != null || _looked)
                return _computer;
            _looked = true;
            try
            {
                _computer = new PartSettingsProvider().GetPartSettingById(TargetingComputerId);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning("The Targeting Computer part was not found: " + ex.Message);
            }
            return _computer;
        }

        public static List<ItemDrop> Cost()
        {
            return new List<ItemDrop>(Price);
        }

        public static bool Unlocked()
        {
            PartSettings computer = TargetingComputer();
            IResearchProvider research = GameServices.Get<IResearchProvider>();
            return computer != null && research != null && research.IsPartUnlocked(computer);
        }
    }

    public struct HandyTrackerCraftRequest : IBroadcast
    {
        public byte Kind;
    }

    public struct HandyTrackerCraftResult : IBroadcast
    {
        public byte Code;
    }

    // Client to server: craft one tracker. Server to that client: what happened.
    // FishNet has no generated serializer for types from a mod, so it is set here.
    static class CraftNet
    {
        public const byte Crafted = 1;
        public const byte Locked = 2;
        public const byte Missing = 3;
        public const byte Full = 4;
        public const byte Failed = 5;

        static NetworkManager _manager;

        public static event Action<byte> Result;

        static CraftNet()
        {
            GenericWriter<HandyTrackerCraftRequest>.SetWrite((writer, value) => writer.WriteUInt8Unpacked(value.Kind));
            GenericReader<HandyTrackerCraftRequest>.SetRead(reader => new HandyTrackerCraftRequest { Kind = reader.ReadUInt8Unpacked() });
            GenericWriter<HandyTrackerCraftResult>.SetWrite((writer, value) => writer.WriteUInt8Unpacked(value.Code));
            GenericReader<HandyTrackerCraftResult>.SetRead(reader => new HandyTrackerCraftResult { Code = reader.ReadUInt8Unpacked() });
        }

        public static void Ensure()
        {
            NetworkManager manager = InstanceFinder.NetworkManager;
            if (manager == null || manager == _manager)
                return;
            _manager = manager;
            manager.ServerManager.RegisterBroadcast<HandyTrackerCraftRequest>(OnRequest, false);
            manager.ClientManager.RegisterBroadcast<HandyTrackerCraftResult>(OnResult);
        }

        public static bool RequestCraft()
        {
            Ensure();
            if (!InstanceFinder.IsClientStarted)
                return false;
            InstanceFinder.ClientManager.Broadcast(new HandyTrackerCraftRequest { Kind = 1 }, Channel.Reliable);
            return true;
        }

        static void OnRequest(NetworkConnection connection, HandyTrackerCraftRequest request, Channel channel)
        {
            byte code = Failed;
            try
            {
                code = CraftServer.Craft(connection);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning("Crafting a tracker failed: " + ex);
            }
            if (connection != null && InstanceFinder.IsServerStarted)
                InstanceFinder.ServerManager.Broadcast(connection, new HandyTrackerCraftResult { Code = code }, false, Channel.Reliable);
        }

        static void OnResult(HandyTrackerCraftResult result, Channel channel)
        {
            Result?.Invoke(result.Code);
        }
    }

    static class CraftServer
    {
        static readonly AccessTools.FieldRef<TrackedPlayerServer, PlayerNetworkInventoryReader> Reader = AccessTools.FieldRefAccess<TrackedPlayerServer, PlayerNetworkInventoryReader>("_networkInventoryReader");

        public static byte Craft(NetworkConnection connection)
        {
            PlayersServerTracker players = GameServices.Get<PlayersServerTracker>();
            TrackedPlayerServer player = connection != null && players != null ? players.GetTrackedPlayerFromConnectionId(connection.ClientId) : null;
            if (player == null)
                return CraftNet.Failed;
            if (!TrackerRecipe.Unlocked())
                return CraftNet.Locked;

            List<ItemDrop> cost = TrackerRecipe.Cost();
            bool free = player.IsUsingCheatFreeBuild;
            if (!free && !player.HasItemsInInventory(cost))
                return CraftNet.Missing;

            PlayerNetworkInventoryReader reader = Reader(player);
            int before = reader != null ? reader.GetItemQuantity(TrackerItem.Id) : -1;
            if (!free)
                player.RemoveItemsFromInventory(cost);
            player.AddItemToInventory(new ItemDrop { ItemId = TrackerItem.Id, Quantity = 1 });
            // The game drops what does not fit without a word, so a full inventory gets the materials back.
            if (reader == null || reader.GetItemQuantity(TrackerItem.Id) > before)
            {
                MelonLogger.Msg("Player " + player.Name + " crafted a Handy Beacon Tracker" + (free ? " (free build)." : "."));
                return CraftNet.Crafted;
            }
            if (!free)
                player.AddItemsToInventory(cost);
            return CraftNet.Full;
        }
    }
}
