using Core.Services;
using Planets.EnvironmentGeneration.Tracking;
using Planets.Interface.Model;
using Planets.Interface.Services;
using Players.Services;
using Players.Visuals.Camera;
using Ships.Interface.Model;
using Ships.Interface.Services;
using UnityEngine;
using WorldTracking.Interface.Model.Spaces;
using WorldTracking.Interface.Services;
using WorldTracking.Interface.Values;
using Object = UnityEngine.Object;

namespace HandyBeaconTracker
{
    struct Bearing
    {
        // A world direction on the player's horizon: magnetic north, the planet, or a nearby asteroid.
        public bool Known;
        // N/E/S/W only for magnetic north. A planet or an asteroid is one mark, not a compass rose of letters.
        public bool Cardinals;
        public Vector3 Direction;
    }

    // The card and the target arrow share one frame: the player's look, flattened onto the player's up.
    // The top of the screen is where the player is looking. North and the target sit on that same horizon,
    // so turning the view slides both together and the angle between them stays the world angle.
    static class TrackerNavigation
    {
        const float NorthEnter = 0.18f;
        const float NorthLeave = 0.32f;
        const float AsteroidReach = 45f;
        const float AsteroidHold = 60f;
        const float RingReach = 80f;

        static PlayerCamera _eyes;
        static PlayerUpProvider _up;
        static bool _magnetic;
        static bool _haveNorth;
        static Vector3 _flatNorth;
        static bool _haveFacing;
        static Vector3 _facing;
        static float _asteroidAt;
        static bool _haveRock;
        static Vector3 _rock;

        public static void Reset()
        {
            _eyes = null;
            _up = null;
            _magnetic = false;
            _haveNorth = false;
            _haveFacing = false;
            _haveRock = false;
            _asteroidAt = 0f;
        }

        public static ScreenView View(float time)
        {
            var view = new ScreenView { Time = time, Hint = "RMB: targets" };
            if (!Gaze(out Vector3 position, out Vector3 look, out Vector3 up))
                return view;

            Vector3 facing = Horizon(look, up);
            Bearing bearing = Orient(position, up);
            view.Compass = bearing.Known && _haveFacing;
            view.Cardinals = bearing.Cardinals;
            if (view.Compass)
                view.NorthAngle = Clockwise(facing, bearing.Direction, up);

            Target? target = TrackerTarget.Current;
            if (!target.HasValue || TrackerTarget.OnSurface(target.Value, position))
                return view;

            view.HasTarget = true;
            ITargetProvisionService provision = GameServices.Get<ITargetProvisionService>();
            view.Name = TrackerTarget.NameOf(target.Value, provision);
            if (provision == null || !TrackerTarget.HasSignal(target.Value) || !_haveFacing)
                return view;

            view.Signal = true;
            Vector3 at = provision.CalculateTargetEffectivePos(target.Value);
            Vector3 toTarget = at - position;
            Vector3 flat = Vector3.ProjectOnPlane(toTarget, up);
            // Straight above or below has no side. Drawing it on the left or the right is what made the arrow jump.
            if (flat.sqrMagnitude > 0.25f)
                view.TargetAngle = Clockwise(facing, flat, up);
            else
                view.TargetAngle = float.NaN;
            view.Elevation = Mathf.Atan2(Vector3.Dot(toTarget, up), flat.magnitude) * Mathf.Rad2Deg;
            float distance = provision.CalculateTargetDistance(target.Value, ClientOrigin.LocalVisualSpace, position);
            UnitFormatsProvider formats = GameServices.Get<UnitFormatsProvider>();
            view.Distance = formats != null ? formats.FormatDistance(distance) : Mathf.RoundToInt(distance) + " m";
            return view;
        }

        // World direction the physical needle points at. Same direction the card calls north.
        public static bool NeedleDirection(Vector3 position, out Vector3 direction)
        {
            direction = Vector3.zero;
            if (!Gaze(out _, out _, out Vector3 up))
                return false;
            Bearing bearing = Orient(position, up);
            if (!bearing.Known)
                return false;
            direction = bearing.Direction;
            return true;
        }

        static Bearing Orient(Vector3 position, Vector3 up)
        {
            var result = new Bearing();
            IPlanetsClientProvider planets = GameServices.Get<IPlanetsClientProvider>();
            IWorldTransposeClient transpose = GameServices.Get<IWorldTransposeClient>();
            if (planets == null || transpose == null || up.sqrMagnitude < 0.5f)
                return result;

            float bestAltitude = float.MaxValue;
            IPlanetProperties nearest = null;
            Vector3 center = Vector3.zero;
            foreach (IPlanetProperties planet in planets.Planets)
            {
                if (planet == null)
                    continue;
                Vector3 c = transpose.Position(planet.SurfaceSpace, ClientOrigin.LocalVisualSpace, Vector3.zero);
                float altitude = (position - c).magnitude - planet.Radius;
                if (altitude < bestAltitude)
                {
                    bestAltitude = altitude;
                    nearest = planet;
                    center = c;
                }
            }

            if (TryAsteroid(planets, position, out Vector3 rock))
            {
                Vector3 flat = Vector3.ProjectOnPlane(rock - position, up);
                if (flat.sqrMagnitude > 1f)
                    return Mark(flat, false);
            }

            if (nearest == null)
                return result;

            float atmosphere = 0f;
            bool hasAir = false;
            try
            {
                PlanetPhysicalProperties physical = nearest.PhysicalProperties;
                if (physical != null)
                {
                    hasAir = physical.HasAtmosphere;
                    atmosphere = physical.AtmosphereThickness;
                }
            }
            catch
            {
            }

            float band = nearest.Radius + atmosphere;
            bool canBeMagnetic = hasAir || nearest.Radius > 800f;
            if (canBeMagnetic && bestAltitude < band * (_magnetic ? NorthLeave : NorthEnter))
                _magnetic = true;
            else
                _magnetic = false;

            if (_magnetic)
            {
                Vector3 pole = transpose.Position(nearest.SurfaceSpace, ClientOrigin.LocalVisualSpace, Vector3.up * nearest.Radius);
                Vector3 north = Vector3.ProjectOnPlane(pole - position, up);
                if (north.sqrMagnitude > 1f)
                    return Mark(north, true);
            }

            // Far from the surface the planet sits on the horizon of the ship, not underfoot.
            Vector3 toPlanet = Vector3.ProjectOnPlane(center - position, up);
            if (toPlanet.sqrMagnitude > 1f)
                return Mark(toPlanet, false);
            if (_haveNorth)
                return Mark(_flatNorth, false);
            return result;
        }

        static Bearing Mark(Vector3 flat, bool cardinals)
        {
            _flatNorth = flat.normalized;
            _haveNorth = true;
            return new Bearing { Known = true, Cardinals = cardinals, Direction = _flatNorth };
        }

        // Look, with pitch removed. Straight up or down keeps the last heading instead of flipping it.
        static Vector3 Horizon(Vector3 look, Vector3 up)
        {
            Vector3 flat = Vector3.ProjectOnPlane(look, up);
            if (flat.sqrMagnitude > 1e-4f)
            {
                _facing = flat.normalized;
                _haveFacing = true;
            }
            return _haveFacing ? _facing : look;
        }

        // Degrees clockwise from where the player looks to the direction, around the player's up.
        static float Clockwise(Vector3 facing, Vector3 direction, Vector3 up)
        {
            Vector3 d = Vector3.ProjectOnPlane(direction, up);
            if (facing.sqrMagnitude < 1e-6f || d.sqrMagnitude < 1e-6f)
                return float.NaN;
            return Vector3.SignedAngle(facing, d, up);
        }

        static bool Gaze(out Vector3 position, out Vector3 forward, out Vector3 up)
        {
            position = Vector3.zero;
            forward = Vector3.forward;
            up = Vector3.up;
            if (_eyes == null)
                _eyes = Object.FindAnyObjectByType<PlayerCamera>();
            if (_eyes == null)
                return false;
            position = _eyes.Position;
            forward = _eyes.Direction;
            if (_up == null)
                _up = Object.FindAnyObjectByType<PlayerUpProvider>();
            if (_up != null)
            {
                Vector3 playerUp = _up.WorldPlayerUp;
                if (playerUp.sqrMagnitude > 0.5f)
                    up = playerUp.normalized;
            }
            return forward.sqrMagnitude > 1e-6f;
        }

        static bool TryAsteroid(IPlanetsClientProvider planets, Vector3 position, out Vector3 rock)
        {
            rock = _rock;
            if (Time.unscaledTime >= _asteroidAt)
                ScanAsteroids(planets, position);
            if (!_haveRock)
                return false;
            if ((_rock - position).sqrMagnitude > AsteroidHold * AsteroidHold)
            {
                _haveRock = false;
                return false;
            }
            rock = _rock;
            return true;
        }

        static void ScanAsteroids(IPlanetsClientProvider planets, Vector3 position)
        {
            _asteroidAt = Time.unscaledTime + 0.25f;
            RelativeRingData? ring = null;
            try
            {
                ring = planets.GetNearestRing(ClientOrigin.LocalVisualSpace, position);
            }
            catch
            {
                return;
            }
            if (!ring.HasValue || ring.Value.Distance > RingReach)
            {
                _haveRock = false;
                return;
            }

            PlanetObjectPhysics[] objects;
            try
            {
                objects = Object.FindObjectsByType<PlanetObjectPhysics>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            }
            catch
            {
                return;
            }
            if (objects == null)
                return;

            float best = AsteroidReach * AsteroidReach;
            Vector3 found = Vector3.zero;
            bool hit = false;
            foreach (PlanetObjectPhysics obj in objects)
            {
                if (obj == null || !(obj.Space is PlanetRingSpace))
                    continue;
                float sqr = (obj.Position - position).sqrMagnitude;
                if (sqr < best)
                {
                    best = sqr;
                    found = obj.Position;
                    hit = true;
                }
            }
            if (!hit)
            {
                _haveRock = false;
                return;
            }
            // Keep the rock we already track unless the new one is clearly closer, so the needle does not hop.
            if (_haveRock)
            {
                float held = (_rock - position).sqrMagnitude;
                if (held <= AsteroidHold * AsteroidHold && best > held * 0.6f)
                    return;
            }
            _haveRock = true;
            _rock = found;
        }
    }
}
