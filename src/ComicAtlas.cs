using System;
using System.Collections.Generic;
using UnityEngine;

namespace HandyBeaconTracker
{
    enum RegionKind
    {
        Rect,
        Strip,
        Polygon,
        Disc
    }

    // One painted patch of the albedo atlas. Sizes are in meters of the model surface.
    sealed class AtlasRegion
    {
        public RegionKind Kind;
        public float Width;
        public float Height;
        public Color32 Base;
        public bool Highlight = true;
        public bool Hatch;
        public float Outline = ComicAtlas.OutlineWidth;
        // Polygon: the outline in region coordinates. Disc: center = (Width/2, Height/2), radius = Width/2.
        public List<Vector2> Polygon;
        // Strip: positions along the strip where a sharp corner gets a thin line, and spans that lie in shadow.
        public List<float> CornerLines;
        public List<Vector2> HatchSpans;

        public int X;
        public int Y;
        public int PixelWidth;
        public int PixelHeight;
    }

    // Paints the albedo like the game's hand-painted tool textures (BuildTool_base): a light rim band with a
    // ragged inner edge, a black outline around every part, and diagonal hatching on faces turned away from the light.
    sealed class ComicAtlas
    {
        public const float OutlineWidth = 0.0024f;
        const float RimWidth = 0.0045f;
        const float HatchPeriod = 0.0042f;
        const float HatchDuty = 0.42f;
        const int Padding = 3;

        static readonly Color32 Ink = new Color32(22, 22, 24, 255);

        public readonly List<AtlasRegion> Regions = new List<AtlasRegion>();
        public int Size { get; private set; }
        public float Density { get; private set; }

        public int Add(AtlasRegion region)
        {
            Regions.Add(region);
            return Regions.Count - 1;
        }

        public Vector2 ToUv(int region, Vector2 local)
        {
            AtlasRegion r = Regions[region];
            float x = r.X + Mathf.Clamp(local.x, 0f, r.Width) * Density;
            float y = r.Y + Mathf.Clamp(local.y, 0f, r.Height) * Density;
            return new Vector2(x / Size, y / Size);
        }

        // Shelf packing at the highest texel density that fits.
        public void Pack(int size)
        {
            Size = size;
            for (float density = 2400f; density >= 300f; density *= 0.9f)
            {
                if (TryPack(density))
                {
                    Density = density;
                    return;
                }
            }
            throw new InvalidOperationException("The tracker texture atlas does not fit.");
        }

        bool TryPack(float density)
        {
            var order = new List<AtlasRegion>(Regions);
            foreach (AtlasRegion r in order)
            {
                r.PixelWidth = Math.Max(2, Mathf.CeilToInt(r.Width * density));
                r.PixelHeight = Math.Max(2, Mathf.CeilToInt(r.Height * density));
            }
            order.Sort((a, b) => b.PixelHeight.CompareTo(a.PixelHeight));

            int x = 0;
            int y = 0;
            int shelf = 0;
            foreach (AtlasRegion r in order)
            {
                int w = r.PixelWidth + Padding * 2;
                int h = r.PixelHeight + Padding * 2;
                if (w > Size)
                    return false;
                if (x + w > Size)
                {
                    x = 0;
                    y += shelf;
                    shelf = 0;
                }
                if (y + h > Size)
                    return false;
                r.X = x + Padding;
                r.Y = y + Padding;
                x += w;
                shelf = Math.Max(shelf, h);
            }
            return true;
        }

        // Rows go from the bottom, as Texture2D.SetPixels32 expects.
        public Color32[] Paint()
        {
            var pixels = new Color32[Size * Size];
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = Ink;
            foreach (AtlasRegion region in Regions)
                PaintRegion(pixels, region);
            return pixels;
        }

        void PaintRegion(Color32[] pixels, AtlasRegion r)
        {
            Color32 rim = Lighten(r.Base, 0.42f);
            Color32 hatch = Darken(r.Base, 0.42f);
            float pixel = 1f / Density;
            for (int py = 0; py < r.PixelHeight; py++)
            {
                for (int px = 0; px < r.PixelWidth; px++)
                {
                    float u = (px + 0.5f) * pixel;
                    float v = (py + 0.5f) * pixel;
                    float edge = EdgeDistance(r, u, v);
                    Color32 color = r.Base;

                    if (ShadowAt(r, u) && Stripe(u, v))
                        color = hatch;

                    if (r.Highlight)
                    {
                        float ragged = RimWidth * (0.55f + 0.45f * Noise(u * 900f, v * 900f));
                        if (edge < r.Outline + ragged)
                            color = Mix(color, rim, 0.85f);
                    }

                    float ink = Mathf.Clamp01((r.Outline - edge) / pixel + 0.5f);
                    if (r.CornerLines != null)
                    {
                        foreach (float c in r.CornerLines)
                            ink = Mathf.Max(ink, Mathf.Clamp01((r.Outline * 0.55f - Mathf.Abs(u - c)) / pixel + 0.5f));
                    }
                    if (ink > 0f)
                        color = Mix(color, Ink, ink);

                    pixels[(r.Y + py) * Size + r.X + px] = color;
                }
            }
        }

        static float EdgeDistance(AtlasRegion r, float u, float v)
        {
            switch (r.Kind)
            {
                case RegionKind.Strip:
                    return Mathf.Min(v, r.Height - v);
                case RegionKind.Disc:
                    {
                        float dx = u - r.Width * 0.5f;
                        float dy = v - r.Height * 0.5f;
                        return r.Width * 0.5f - Mathf.Sqrt(dx * dx + dy * dy);
                    }
                case RegionKind.Polygon:
                    return PolygonDistance(r.Polygon, new Vector2(u, v));
                default:
                    return Mathf.Min(Mathf.Min(u, r.Width - u), Mathf.Min(v, r.Height - v));
            }
        }

        // Signed distance to a convex counter-clockwise polygon, positive inside.
        static float PolygonDistance(List<Vector2> polygon, Vector2 p)
        {
            float best = float.MaxValue;
            for (int i = 0; i < polygon.Count; i++)
            {
                Vector2 a = polygon[i];
                Vector2 b = polygon[(i + 1) % polygon.Count];
                Vector2 edge = b - a;
                float length = edge.magnitude;
                if (length < 1e-6f)
                    continue;
                var inward = new Vector2(-edge.y, edge.x) / length;
                best = Mathf.Min(best, Vector2.Dot(p - a, inward));
            }
            return best;
        }

        static bool ShadowAt(AtlasRegion r, float u)
        {
            if (r.Hatch)
                return true;
            if (r.HatchSpans == null)
                return false;
            foreach (Vector2 span in r.HatchSpans)
            {
                if (u >= span.x && u <= span.y)
                    return true;
            }
            return false;
        }

        static bool Stripe(float u, float v)
        {
            float t = (u - v) / HatchPeriod;
            return t - Mathf.Floor(t) < HatchDuty;
        }

        // Cheap value noise in 0..1, stable for the same input.
        static float Noise(float x, float y)
        {
            int xi = Mathf.FloorToInt(x);
            int yi = Mathf.FloorToInt(y);
            float fx = x - xi;
            float fy = y - yi;
            float a = Hash(xi, yi);
            float b = Hash(xi + 1, yi);
            float c = Hash(xi, yi + 1);
            float d = Hash(xi + 1, yi + 1);
            float sx = fx * fx * (3f - 2f * fx);
            float sy = fy * fy * (3f - 2f * fy);
            return Mathf.Lerp(Mathf.Lerp(a, b, sx), Mathf.Lerp(c, d, sx), sy);
        }

        static float Hash(int x, int y)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263);
                h = (h ^ (h >> 13)) * 1274126177u;
                return (h ^ (h >> 16)) / (float)uint.MaxValue;
            }
        }

        public static Color32 Lighten(Color32 c, float amount)
        {
            return Mix(c, new Color32(255, 255, 255, 255), amount);
        }

        public static Color32 Darken(Color32 c, float amount)
        {
            return Mix(c, Ink, amount);
        }

        static Color32 Mix(Color32 a, Color32 b, float t)
        {
            return new Color32(
                (byte)(a.r + (b.r - a.r) * t),
                (byte)(a.g + (b.g - a.g) * t),
                (byte)(a.b + (b.b - a.b) * t),
                255);
        }
    }
}
