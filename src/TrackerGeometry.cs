using System.Collections.Generic;
using UnityEngine;

namespace HandyBeaconTracker
{
    // The tracker in tool space: +Z points forward out of the hand, +Y up, +X right, like the game's tools.
    // The grip sits under the hand target of the game's Paint Tool, so the first-person hand closes around it.
    sealed class TrackerGeometry
    {
        public static readonly Color32 Paint = new Color32(224, 224, 216, 255);
        public static readonly Color32 Steel = new Color32(168, 174, 182, 255);
        public static readonly Color32 Dark = new Color32(74, 78, 88, 255);
        public static readonly Color32 Bezel = new Color32(42, 45, 52, 255);
        public static readonly Color32 Yellow = new Color32(242, 188, 44, 255);
        public static readonly Color32 Blue = new Color32(62, 118, 200, 255);
        public static readonly Color32 Red = new Color32(218, 66, 56, 255);
        public static readonly Color32 White = new Color32(246, 246, 240, 255);
        public static readonly Color32 Cyan = new Color32(96, 232, 255, 255);

        // Slanted screen face of the housing (z, y), from its lower rear edge up toward the antenna, 50 degrees steep.
        static readonly Vector2 FaceLow = new Vector2(-0.040f, 0.080f);
        static readonly Vector2 FaceHigh = new Vector2(0.0217f, 0.1535f);
        const float HousingFront = 0.032f;
        const float HousingHalfWidth = 0.050f;
        const float GripRake = 12f;

        // Pose in the first-person tool root (camera space). The game's tools sit at about (0.33, -0.34, 0.56);
        // the tracker is held a little closer and turned toward the eye so its screen can be read.
        public static readonly Vector3 FirstPersonPosition = new Vector3(0.23f, -0.28f, 0.46f);
        public static readonly Quaternion FirstPersonRotation = Rotations.Axis(Vector3.up, -12f) * Rotations.Axis(Vector3.right, -8f);

        // The item icon looks at the screen from above and behind, with the antenna pointing to the right.
        public static readonly Quaternion ThumbnailRotation = Rotations.Look(new Vector3(0.82f, 0.22f, 0.53f), new Vector3(-0.15f, 0.80f, -0.58f));

        public readonly ComicAtlas Atlas = new ComicAtlas();
        public readonly MeshData Body;
        public readonly MeshData Needle;

        public Vector3 ScreenCenter { get; private set; }
        public Vector3 ScreenNormal { get; private set; }
        public Vector3 ScreenUp { get; private set; }
        public float ScreenSize { get; private set; }
        public Vector3 NeedlePivot { get; private set; }

        public TrackerGeometry(int atlasSize)
        {
            var body = new MeshBuilder(Atlas);
            var needle = new MeshBuilder(Atlas);
            BuildBody(body);
            BuildGrip(body);
            BuildHousing(body);
            BuildCompass(body, needle);
            BuildAntenna(body);
            Atlas.Pack(atlasSize);
            body.Data.ResolveUvs(Atlas);
            needle.Data.ResolveUvs(Atlas);
            Body = body.Data;
            Needle = needle.Data;
        }

        static void BuildBody(MeshBuilder b)
        {
            b.Prism(Frame.AlongX(Vector3.zero), MeshBuilder.Rectangle(new Vector2(-0.105f, 0.012f), new Vector2(0.085f, 0.072f), 0.012f), -0.038f, 0.038f, PrismStyle.Of(Paint));

            // Yellow band around the front of the body.
            b.Prism(Frame.AlongZ(Vector3.zero), MeshBuilder.Rectangle(new Vector2(-0.041f, 0.009f), new Vector2(0.041f, 0.075f), 0.004f), 0.060f, 0.078f, PrismStyle.Of(Yellow));

            // Blue side plates with rivets. AlongX extrudes toward -X, so the right side has negative z.
            List<Vector2> plate = MeshBuilder.Rectangle(new Vector2(-0.082f, 0.024f), new Vector2(0.040f, 0.061f), 0.008f);
            foreach (float side in new[] { 1f, -1f })
            {
                float inner = 0.0378f * side;
                float outer = 0.0418f * side;
                b.Prism(Frame.AlongX(Vector3.zero), plate, -inner, -outer, PrismStyle.Of(Blue));
                foreach (Vector2 corner in new[] { new Vector2(-0.075f, 0.030f), new Vector2(0.033f, 0.030f), new Vector2(-0.075f, 0.055f), new Vector2(0.033f, 0.055f) })
                {
                    var origin = new Vector3(outer, corner.y, corner.x);
                    var frame = new Frame(origin, Vector3.forward, Vector3.up);
                    b.Cylinder(frame, 0.0028f, 0f, -0.0016f * side, PrismStyle.Round(Dark), 10);
                }
            }

            // Two buttons between the screen housing and the yellow band.
            foreach (float x in new[] { -0.016f, 0.016f })
                b.Cylinder(Frame.AlongY(new Vector3(x, 0.072f, 0.046f)), 0.0058f, -0.001f, 0.004f, PrismStyle.Round(Yellow), 14);
        }

        static void BuildGrip(MeshBuilder b)
        {
            var pivot = new Vector3(0f, 0.012f, -0.052f);
            Quaternion rake = Rotations.Axis(Vector3.right, GripRake);

            // AlongY: profile x = world x, profile y = -world z.
            Frame grip = Frame.AlongY(Vector3.zero).Rotated(rake, pivot);
            b.Prism(grip, MeshBuilder.Rectangle(new Vector2(-0.016f, 0.030f), new Vector2(0.016f, 0.074f), 0.009f), -0.098f, 0.016f, PrismStyle.Of(Dark));
            b.Prism(grip, MeshBuilder.Rectangle(new Vector2(-0.019f, 0.026f), new Vector2(0.019f, 0.078f), 0.010f), -0.106f, -0.098f, PrismStyle.Of(Yellow));

            // Scan button on the front of the grip, where a trigger would be.
            Frame button = Frame.AlongZ(Vector3.zero).Rotated(rake, pivot);
            b.Box(new Frame(button.Point(new Vector2(0f, -0.006f), -0.031f), button.X, button.Y), new Vector2(0.012f, 0.016f), -0.004f, 0.006f, PrismStyle.Of(Yellow));
        }

        void BuildHousing(MeshBuilder b)
        {
            var wedge = new List<Vector2>
            {
                new Vector2(FaceLow.x, 0.070f),
                new Vector2(HousingFront, 0.070f),
                new Vector2(HousingFront, FaceHigh.y - 0.009f),
                FaceHigh,
                FaceLow
            };
            b.Prism(Frame.AlongX(Vector3.zero), wedge, -HousingHalfWidth, HousingHalfWidth, PrismStyle.Of(Paint));

            Vector2 slant = FaceHigh - FaceLow;
            var up = new Vector3(0f, slant.y, slant.x).normalized;
            var normal = new Vector3(0f, up.z, -up.y);
            Vector2 middle = (FaceHigh + FaceLow) * 0.5f;
            var faceCenter = new Vector3(0f, middle.y, middle.x);

            // Frame(right, up) has Z = Cross(right, up) pointing into the housing, so negative z grows outward.
            var face = new Frame(faceCenter, Vector3.right, up);
            b.Box(face, new Vector2(0.092f, 0.088f), -0.0022f, 0f, PrismStyle.Of(Bezel));

            ScreenCenter = faceCenter + normal * 0.0030f;
            ScreenNormal = normal;
            ScreenUp = up;
            ScreenSize = 0.080f;

            // A short visor over the top edge of the screen.
            Vector3 visorBase = faceCenter + up * (slant.magnitude * 0.5f - 0.002f);
            var visor = new Frame(visorBase, Vector3.right, normal);
            b.Box(visor, new Vector2(0.096f, 0.016f), -0.004f, 0.0f, PrismStyle.Of(Yellow));
        }

        void BuildCompass(MeshBuilder b, MeshBuilder needle)
        {
            // Behind the screen, nearest to the player's eye.
            var center = new Vector3(0f, 0.072f, -0.066f);
            b.Cylinder(Frame.AlongY(center), 0.0205f, -0.001f, 0.0055f, PrismStyle.Round(Dark), 24);
            b.Cylinder(Frame.AlongY(center), 0.0168f, 0.0050f, 0.0062f, PrismStyle.Round(White), 24);

            NeedlePivot = center + Vector3.up * 0.0066f;

            // Frame(right, forward) extrudes toward -Y, so the needle stands on the dial when z goes negative.
            var flat = new Frame(Vector3.zero, Vector3.right, Vector3.forward);
            needle.Prism(flat, new List<Vector2> { new Vector2(-0.0034f, 0f), new Vector2(0.0034f, 0f), new Vector2(0f, 0.0145f) }, 0f, -0.0014f, NeedleStyle(Red));
            needle.Prism(flat, new List<Vector2> { new Vector2(0.0034f, 0f), new Vector2(-0.0034f, 0f), new Vector2(0f, -0.0125f) }, 0f, -0.0014f, NeedleStyle(White));
            needle.Cylinder(Frame.AlongY(Vector3.zero), 0.0022f, 0f, 0.0024f, PrismStyle.Round(Dark), 10);
        }

        static PrismStyle NeedleStyle(Color32 color)
        {
            PrismStyle style = PrismStyle.Of(color);
            style.Outline = 0.0009f;
            style.CornerLines = false;
            return style;
        }

        static void BuildAntenna(MeshBuilder b)
        {
            const float height = 0.042f;
            Frame boom = Frame.AlongZ(new Vector3(0f, height, 0f));
            b.Cylinder(boom, 0.0105f, 0.076f, 0.092f, PrismStyle.Round(Dark), 16);
            b.Cylinder(boom, 0.0055f, 0.092f, 0.236f, PrismStyle.Round(Steel), 12);
            b.Cylinder(boom, 0.0085f, 0.236f, 0.250f, PrismStyle.Round(Cyan), 16);

            float[] at = { 0.112f, 0.150f, 0.188f, 0.224f };
            float[] half = { 0.062f, 0.054f, 0.047f, 0.040f };
            for (int i = 0; i < at.Length; i++)
            {
                // Frame(up, forward) extrudes along +X.
                var element = new Frame(new Vector3(0f, height, at[i]), Vector3.up, Vector3.forward);
                b.Cylinder(element, 0.0034f, -half[i], half[i], PrismStyle.Round(Steel), 10);
                b.Cylinder(element, 0.0047f, half[i] - 0.007f, half[i] + 0.001f, PrismStyle.Round(Yellow), 10);
                b.Cylinder(element, 0.0047f, -half[i] - 0.001f, -half[i] + 0.007f, PrismStyle.Round(Yellow), 10);
            }
        }
    }

    // Quaternion helpers that do not call into the engine, so the model also builds outside the game.
    static class Rotations
    {
        public static Quaternion Axis(Vector3 axis, float degrees)
        {
            axis = axis.normalized;
            float half = degrees * Mathf.Deg2Rad * 0.5f;
            float s = Mathf.Sin(half);
            return new Quaternion(axis.x * s, axis.y * s, axis.z * s, Mathf.Cos(half));
        }

        // Rotation whose forward is the given direction and whose up is as close as possible to the given up.
        public static Quaternion Look(Vector3 forward, Vector3 up)
        {
            Vector3 z = forward.normalized;
            Vector3 x = Vector3.Cross(up, z).normalized;
            Vector3 y = Vector3.Cross(z, x);
            float trace = x.x + y.y + z.z;
            if (trace > 0f)
            {
                float s = Mathf.Sqrt(trace + 1f) * 2f;
                return new Quaternion((y.z - z.y) / s, (z.x - x.z) / s, (x.y - y.x) / s, 0.25f * s);
            }
            if (x.x > y.y && x.x > z.z)
            {
                float s = Mathf.Sqrt(1f + x.x - y.y - z.z) * 2f;
                return new Quaternion(0.25f * s, (y.x + x.y) / s, (z.x + x.z) / s, (y.z - z.y) / s);
            }
            if (y.y > z.z)
            {
                float s = Mathf.Sqrt(1f + y.y - x.x - z.z) * 2f;
                return new Quaternion((y.x + x.y) / s, 0.25f * s, (z.y + y.z) / s, (z.x - x.z) / s);
            }
            float t = Mathf.Sqrt(1f + z.z - x.x - y.y) * 2f;
            return new Quaternion((z.x + x.z) / t, (z.y + y.z) / t, 0.25f * t, (x.y - y.x) / t);
        }
    }
}
