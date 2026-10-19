using UnityEngine;

namespace HandyBeaconTracker
{
    struct ScreenView
    {
        public bool HasTarget;
        // The target's position is known (a beacon's ship can be out of range).
        public bool Signal;
        public string Name;
        public string Distance;
        // Degrees clockwise from the top of the screen, which points where the tracker points.
        public float TargetAngle;
        // Degrees above the horizon.
        public float Elevation;
        // A body is near, so the needle has a heading.
        public bool Compass;
        // Magnetic north of a planet: show N/E/S/W. Off when the card points at a planet or an asteroid.
        public bool Cardinals;
        public float NorthAngle;
        public float Time;
        public string Hint;
    }

    // Draws the tracker display into a 128 x 128 image: the target's name on top, a compass card with the
    // target arrow in the middle and the distance at the bottom.
    sealed class TrackerScreen
    {
        public const int Size = 128;
        const int CenterX = 64;
        const int CenterY = 62;
        const int RingRadius = 35;
        const int LetterRadius = 44;

        public readonly Color32[] Pixels = new Color32[Size * Size];
        public Color32 Primary = new Color32(120, 230, 255, 255);
        public Color32 Accent = new Color32(255, 204, 64, 255);
        public Color32 North = new Color32(255, 92, 72, 255);
        public Color32 Background = new Color32(0, 0, 0, 255);

        Color32 Dim => Scale(Primary, 0.38f);
        Color32 Faint => Scale(Primary, 0.16f);

        public void Draw(ScreenView view)
        {
            for (int i = 0; i < Pixels.Length; i++)
                Pixels[i] = Background;

            string title = view.HasTarget ? Fit(PixelFont.Normalize(view.Name), 1) : "COMPASS";
            Text(title, CenterX, 3, 1, view.HasTarget ? Primary : Dim);

            Ring(CenterX, CenterY, RingRadius, Dim);

            float north = view.Compass && !float.IsNaN(view.NorthAngle) ? view.NorthAngle : 0f;
            for (int i = 0; i < 12; i++)
            {
                float angle = north + i * 30f;
                bool cardinal = i % 3 == 0;
                bool home = cardinal && i == 0;
                Color32 color = !view.Compass ? Faint : home ? (view.Cardinals ? North : Accent) : cardinal ? Primary : Dim;
                Line(CenterX, CenterY, angle, RingRadius - (cardinal ? 5f : 3f), RingRadius + (cardinal ? 2f : 0f), color, cardinal ? 2f : 1f);
            }
            if (view.Compass && view.Cardinals)
            {
                string[] letters = { "N", "E", "S", "W" };
                for (int i = 0; i < 4; i++)
                {
                    float angle = (north + i * 90f) * Mathf.Deg2Rad;
                    int x = Mathf.RoundToInt(CenterX + Mathf.Sin(angle) * LetterRadius);
                    int y = Mathf.RoundToInt(CenterY - Mathf.Cos(angle) * LetterRadius) - PixelFont.Height / 2;
                    Text(letters[i], x, y, 1, i == 0 ? North : Primary);
                }
            }

            Disc(CenterX, CenterY, 2f, Primary);
            if (view.HasTarget && view.Signal && !float.IsNaN(view.TargetAngle))
                Arrow(view.TargetAngle, Accent);
            else if (view.HasTarget && Mathf.Repeat(view.Time, 1f) < 0.6f)
                Text("NO SIGNAL", CenterX, CenterY + 9, 1, Accent);

            if (view.HasTarget && view.Signal)
            {
                string distance = PixelFont.Normalize(view.Distance);
                if (Mathf.Abs(view.Elevation) > 12f)
                    distance = (view.Elevation > 0f ? PixelFont.Up : PixelFont.Down) + distance;
                int scale = PixelFont.Measure(distance, 2) <= Size - 6 ? 2 : 1;
                Text(Fit(distance, scale), CenterX, scale == 2 ? 113 : 118, scale, Primary);
            }
            else if (!view.HasTarget && !string.IsNullOrEmpty(view.Hint))
            {
                Text(Fit(PixelFont.Normalize(view.Hint), 1), CenterX, 118, 1, Dim);
            }
        }

        void Arrow(float angle, Color32 color)
        {
            float a = angle * Mathf.Deg2Rad;
            var dir = new Vector2(Mathf.Sin(a), -Mathf.Cos(a));
            var side = new Vector2(-dir.y, dir.x);
            var center = new Vector2(CenterX, CenterY);
            Line(CenterX, CenterY, angle, 5f, 18f, color, 3f);
            Vector2 tip = center + dir * 27f;
            Vector2 left = center + dir * 16f + side * 7f;
            Vector2 right = center + dir * 16f - side * 7f;
            Triangle(tip, left, right, color);
        }

        static string Fit(string text, int scale)
        {
            int max = (Size - 4) / (PixelFont.Advance * scale);
            if (text.Length <= max)
                return text;
            return text.Substring(0, Mathf.Max(0, max - 2)) + "..";
        }

        // Centered on x, with y the top row.
        void Text(string text, int x, int y, int scale, Color32 color)
        {
            int left = x - PixelFont.Measure(text, scale) / 2;
            for (int i = 0; i < text.Length; i++)
            {
                int gx = left + i * PixelFont.Advance * scale;
                for (int row = 0; row < PixelFont.Height; row++)
                {
                    for (int column = 0; column < PixelFont.Width; column++)
                    {
                        if (!PixelFont.Pixel(text[i], column, row))
                            continue;
                        for (int sy = 0; sy < scale; sy++)
                        {
                            for (int sx = 0; sx < scale; sx++)
                                Plot(gx + column * scale + sx, y + row * scale + sy, color);
                        }
                    }
                }
            }
        }

        // A radial segment from radius r0 to r1 at a screen angle.
        void Line(int cx, int cy, float angle, float r0, float r1, Color32 color, float width)
        {
            float a = angle * Mathf.Deg2Rad;
            var dir = new Vector2(Mathf.Sin(a), -Mathf.Cos(a));
            var center = new Vector2(cx, cy);
            Segment(center + dir * r0, center + dir * r1, color, width);
        }

        void Segment(Vector2 a, Vector2 b, Color32 color, float width)
        {
            int minX = Mathf.FloorToInt(Mathf.Min(a.x, b.x) - width);
            int maxX = Mathf.CeilToInt(Mathf.Max(a.x, b.x) + width);
            int minY = Mathf.FloorToInt(Mathf.Min(a.y, b.y) - width);
            int maxY = Mathf.CeilToInt(Mathf.Max(a.y, b.y) + width);
            Vector2 ab = b - a;
            float length = Mathf.Max(ab.sqrMagnitude, 1e-4f);
            float half = width * 0.5f;
            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    var p = new Vector2(x + 0.5f, y + 0.5f);
                    float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / length);
                    if ((a + ab * t - p).magnitude <= half + 0.25f)
                        Plot(x, y, color);
                }
            }
        }

        void Ring(int cx, int cy, int radius, Color32 color)
        {
            for (int y = cy - radius - 1; y <= cy + radius + 1; y++)
            {
                for (int x = cx - radius - 1; x <= cx + radius + 1; x++)
                {
                    float dx = x + 0.5f - cx;
                    float dy = y + 0.5f - cy;
                    if (Mathf.Abs(Mathf.Sqrt(dx * dx + dy * dy) - radius) <= 0.6f)
                        Plot(x, y, color);
                }
            }
        }

        void Disc(int cx, int cy, float radius, Color32 color)
        {
            for (int y = Mathf.FloorToInt(cy - radius - 1); y <= cy + radius + 1; y++)
            {
                for (int x = Mathf.FloorToInt(cx - radius - 1); x <= cx + radius + 1; x++)
                {
                    float dx = x + 0.5f - cx;
                    float dy = y + 0.5f - cy;
                    if (dx * dx + dy * dy <= radius * radius + 0.5f)
                        Plot(x, y, color);
                }
            }
        }

        void Triangle(Vector2 a, Vector2 b, Vector2 c, Color32 color)
        {
            int minX = Mathf.FloorToInt(Mathf.Min(a.x, Mathf.Min(b.x, c.x)));
            int maxX = Mathf.CeilToInt(Mathf.Max(a.x, Mathf.Max(b.x, c.x)));
            int minY = Mathf.FloorToInt(Mathf.Min(a.y, Mathf.Min(b.y, c.y)));
            int maxY = Mathf.CeilToInt(Mathf.Max(a.y, Mathf.Max(b.y, c.y)));
            float area = Cross(b - a, c - a);
            if (Mathf.Abs(area) < 1e-4f)
                return;
            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    var p = new Vector2(x + 0.5f, y + 0.5f);
                    float w0 = Cross(b - p, c - p) / area;
                    float w1 = Cross(c - p, a - p) / area;
                    float w2 = 1f - w0 - w1;
                    if (w0 >= -0.02f && w1 >= -0.02f && w2 >= -0.02f)
                        Plot(x, y, color);
                }
            }
        }

        static float Cross(Vector2 a, Vector2 b)
        {
            return a.x * b.y - a.y * b.x;
        }

        // y grows downward here; the texture's first row is the bottom one.
        void Plot(int x, int y, Color32 color)
        {
            if (x < 0 || y < 0 || x >= Size || y >= Size)
                return;
            Pixels[(Size - 1 - y) * Size + x] = color;
        }

        static Color32 Scale(Color32 c, float f)
        {
            return new Color32((byte)(c.r * f), (byte)(c.g * f), (byte)(c.b * f), 255);
        }
    }
}
