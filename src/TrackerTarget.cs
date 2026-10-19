using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Audio.Interface;
using Managers.Interface;
using MelonLoader;
using Planets.Interface.Model;
using Planets.Interface.Services;
using Players.Services;
using Ships.Interface.Model;
using Ships.Interface.Model.Parts;
using Ships.Interface.Model.Parts.State;
using Ships.Interface.Services;
using UnityEngine;
using WorldTracking.Interface.Model.Spaces;
using WorldTracking.Interface.Model.Spaces.Common;
using WorldTracking.Interface.Services;
using WorldTracking.Interface.Values;

namespace HandyBeaconTracker
{
    // The tracker's chosen target. It belongs to this player and this world: it is kept in a file next to the save.
    static class TrackerTarget
    {
        const string FileName = "handy-beacon-tracker.txt";
        const string Beep = "Event:/Ships/ShipInteractionConfirmBeep";

        static Target? _current;
        static string _name;
        static bool _loaded;

        public static Target? Current
        {
            get
            {
                EnsureLoaded();
                Validate();
                return _current;
            }
        }

        // The HUD marker only when the target's position is known, and not for the planet under the player's feet.
        public static Target? HudTarget
        {
            get
            {
                Target? target = Current;
                if (!target.HasValue || !HasSignal(target.Value))
                    return null;
                Vector3 at = HandyTrackerTool.Held != null ? HandyTrackerTool.Held.transform.position : Vector3.zero;
                return OnSurface(target.Value, at) ? (Target?)null : target;
            }
        }

        public static void SceneLoaded()
        {
            _current = null;
            _name = null;
            _loaded = false;
        }

        // The game names a beacon only while its ship is loaded, so the last known name is kept.
        public static string NameOf(Target target, ITargetProvisionService provision)
        {
            string name = provision != null ? provision.GetTargetName(target) : null;
            bool current = _current.HasValue && _current.Value == target;
            if (!string.IsNullOrEmpty(name))
            {
                if (current && name != _name)
                {
                    _name = name;
                    Save();
                }
                return name;
            }
            if (current && !string.IsNullOrEmpty(_name))
                return _name;
            return target.TargetType == TargetType.Planet ? "Planet" : "Beacon";
        }

        public static void Toggle(Target target)
        {
            Target? current = Current;
            Set(current.HasValue && current.Value == target ? (Target?)null : target);
        }

        public static void Set(Target? target)
        {
            EnsureLoaded();
            _current = target;
            _name = null;
            if (target.HasValue)
                NameOf(target.Value, GameServices.Get<ITargetProvisionService>());
            Save();
            PlayBeep();
        }

        // Next target by distance, as the target list sorts them; after the farthest one it starts over.
        public static void SelectNext(Vector3 from)
        {
            List<Target> targets = SortedTargets(from);
            if (targets.Count == 0)
                return;
            Target? current = Current;
            int index = current.HasValue ? targets.IndexOf(current.Value) : -1;
            Set(targets[(index + 1) % targets.Count]);
        }

        public static List<Target> SortedTargets(Vector3 from)
        {
            var result = new List<Target>();
            ITargetProvisionService provision = GameServices.Get<ITargetProvisionService>();
            if (provision == null)
                return result;
            var distances = new Dictionary<Target, float>();
            foreach (Target target in provision.GetTargetsInWorld())
            {
                if (OnSurface(target, from))
                    continue;
                result.Add(target);
                distances[target] = provision.CalculateTargetDistance(target, ClientOrigin.LocalVisualSpace, from);
            }
            result.Sort((a, b) => distances[a].CompareTo(distances[b]));
            return result;
        }

        // The planet whose surface space the player is in (on foot or aboard a ship) is not a target.
        // It comes back once that space is left. The choice itself stays saved.
        public static bool OnSurface(Target target, Vector3 position)
        {
            if (target.TargetType != TargetType.Planet)
                return false;
            try
            {
                PlayersClientTracker players = GameServices.Get<PlayersClientTracker>();
                ISpacesClientProvider spaces = GameServices.Get<ISpacesClientProvider>();
                uint? spaceId = players != null ? players.LocalPlayerPhysicsStateSpaceId : null;
                if (spaces != null && spaceId.HasValue)
                {
                    ISpace outside = spaces.GetSpaceOutsideOfShip(spaceId.Value);
                    if (outside is PlanetSurfaceSpace surface && surface.PlanetId == target.PlanetId)
                        return true;
                }
            }
            catch (Exception)
            {
            }

            IPlanetsClientProvider planets = GameServices.Get<IPlanetsClientProvider>();
            IWorldTransposeClient transpose = GameServices.Get<IWorldTransposeClient>();
            if (planets == null || transpose == null)
                return false;
            IPlanetProperties planet = null;
            try
            {
                planet = planets.GetPlanetProps(target.PlanetId);
            }
            catch (Exception)
            {
            }
            if (planet == null)
                return false;
            Vector3 center = transpose.Position(planet.SurfaceSpace, ClientOrigin.LocalVisualSpace, Vector3.zero);
            float altitude = (position - center).magnitude - planet.Radius;
            float ceiling = 250f;
            try
            {
                PlanetPhysicalProperties physical = planet.PhysicalProperties;
                if (physical != null && physical.HasAtmosphere)
                    ceiling = Mathf.Max(ceiling, physical.AtmosphereThickness);
            }
            catch (Exception)
            {
            }
            return altitude < ceiling;
        }

        // A beacon's ship may be out of the client's range; its position is unknown until it loads again.
        public static bool HasSignal(Target target)
        {
            if (target.TargetType != TargetType.Beacon)
                return true;
            IShipsClientProvider ships = GameServices.Get<IShipsClientProvider>();
            return ships != null && ships.TryGetShip(target.Beacon.ShipId, out var ship) && ship.TryGetPartPlacement(target.Beacon.PartId, out _);
        }

        // Forget a beacon that was removed or lost its name; the game's list leaves unnamed beacons out too.
        static void Validate()
        {
            if (!_current.HasValue || _current.Value.TargetType != TargetType.Beacon)
                return;
            IShipsClientProvider ships = GameServices.Get<IShipsClientProvider>();
            if (ships == null)
                return;
            PartContext beacon = _current.Value.Beacon;
            if (!ships.TryGetShip(beacon.ShipId, out var ship))
                return;
            // The ship's state can arrive a frame late. Only drop the target when the part is gone
            // or its name is confirmed empty; a failed read used to clear it and the HUD jumped.
            if (!ship.TryGetPartPlacement(beacon.PartId, out _))
            {
                _current = null;
                _name = null;
                Save();
                return;
            }
            if (!ships.TryGetPartState<BeaconState>(beacon, out BeaconState state) || !string.IsNullOrEmpty(state.Name))
                return;
            _current = null;
            _name = null;
            Save();
        }

        static void PlayBeep()
        {
            try
            {
                GameServices.Get<IAudioPlayer>()?.PlayAudioEvent(Beep);
            }
            catch (Exception)
            {
            }
        }

        static string SaveFile()
        {
            string save = MainGameSceneLoadData.SaveFileFullName;
            if (string.IsNullOrEmpty(save))
                return null;
            string directory = Path.GetDirectoryName(save);
            return string.IsNullOrEmpty(directory) ? null : Path.Combine(directory, FileName);
        }

        // A joined player has no copy of the host's save. Remembering the target there would write into
        // whatever world this computer opened last, so a guest keeps the choice only until they leave.
        static bool KeepInThisWorld()
        {
            switch (MainGameSceneLoadData.Mode)
            {
                case MainGameSceneLoadData.GameMode.SinglePlayer:
                case MainGameSceneLoadData.GameMode.HostGameSteam:
                case MainGameSceneLoadData.GameMode.HostGameIp:
                    return true;
                default:
                    return false;
            }
        }

        static void EnsureLoaded()
        {
            if (_loaded)
                return;
            _loaded = true;
            if (!KeepInThisWorld())
                return;
            string path = SaveFile();
            if (path == null || !File.Exists(path))
                return;
            try
            {
                string[] lines = File.ReadAllLines(path);
                string[] parts = lines.Length > 0 ? lines[0].Trim().Split(' ') : new string[0];
                if (lines.Length > 1)
                    _name = lines[1].Trim();
                if (parts.Length == 3 && parts[0] == "beacon")
                {
                    _current = new Target
                    {
                        TargetType = TargetType.Beacon,
                        Beacon = new PartContext(uint.Parse(parts[1], CultureInfo.InvariantCulture), ushort.Parse(parts[2], CultureInfo.InvariantCulture))
                    };
                }
                else if (parts.Length == 2 && parts[0] == "planet")
                {
                    _current = new Target { TargetType = TargetType.Planet, PlanetId = ulong.Parse(parts[1], CultureInfo.InvariantCulture) };
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning("The tracker target could not be read from " + path + ": " + ex.Message);
            }
        }

        static void Save()
        {
            if (!KeepInThisWorld())
                return;
            string path = SaveFile();
            if (path == null)
                return;
            try
            {
                if (!_current.HasValue)
                {
                    if (File.Exists(path))
                        File.Delete(path);
                    return;
                }
                Target t = _current.Value;
                string text = t.TargetType == TargetType.Beacon
                    ? "beacon " + t.Beacon.ShipId.ToString(CultureInfo.InvariantCulture) + " " + t.Beacon.PartId.ToString(CultureInfo.InvariantCulture)
                    : "planet " + t.PlanetId.ToString(CultureInfo.InvariantCulture);
                File.WriteAllText(path, text + "\n" + (_name ?? "").Replace('\n', ' ').Replace('\r', ' '));
            }
            catch (Exception ex)
            {
                MelonLogger.Warning("The tracker target could not be saved to " + path + ": " + ex.Message);
            }
        }
    }
}
