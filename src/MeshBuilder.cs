using System.Collections.Generic;
using UnityEngine;

namespace HandyBeaconTracker
{
    sealed class MeshData
    {
        public readonly List<Vector3> Vertices = new List<Vector3>();
        public readonly List<Vector3> Normals = new List<Vector3>();
        public readonly List<Vector2> Local = new List<Vector2>();
        public readonly List<int> Region = new List<int>();
        public readonly List<int> Triangles = new List<int>();
        public readonly List<Vector2> Uvs = new List<Vector2>();

        public void ResolveUvs(ComicAtlas atlas)
        {
            Uvs.Clear();
            for (int i = 0; i < Vertices.Count; i++)
                Uvs.Add(atlas.ToUv(Region[i], Local[i]));
        }
    }

    // Axes of a part: the profile lies in X/Y and is extruded along Z, with Z = Cross(X, Y).
    struct Frame
    {
        public Vector3 Origin;
        public Vector3 X;
        public Vector3 Y;
        public Vector3 Z;

        public Frame(Vector3 origin, Vector3 x, Vector3 y)
        {
            Origin = origin;
            X = x.normalized;
            Y = (y - Vector3.Dot(y, X) * X).normalized;
            Z = Vector3.Cross(X, Y);
        }

        public Vector3 Point(Vector2 p, float z)
        {
            return Origin + X * p.x + Y * p.y + Z * z;
        }

        public Vector3 Direction(Vector2 d)
        {
            return X * d.x + Y * d.y;
        }

        // Along +Z (local forward), with Y as up.
        public static Frame AlongZ(Vector3 origin)
        {
            return new Frame(origin, Vector3.right, Vector3.up);
        }

        // Profile in the Z/Y plane (x = z, y = y), extruded along -X (local z = -world x).
        public static Frame AlongX(Vector3 origin)
        {
            return new Frame(origin, Vector3.forward, Vector3.up);
        }

        // Profile in the X/Z plane (x = x, y = z), extruded along +Y.
        public static Frame AlongY(Vector3 origin)
        {
            return new Frame(origin, Vector3.right, Vector3.back);
        }

        public Frame Rotated(Quaternion rotation, Vector3 pivot)
        {
            Frame f = this;
            f.Origin = pivot + rotation * (Origin - pivot);
            f.X = rotation * X;
            f.Y = rotation * Y;
            f.Z = rotation * Z;
            return f;
        }
    }

    sealed class PrismStyle
    {
        public Color32 Side;
        public Color32 Cap;
        public bool Smooth;
        public bool StartCap = true;
        public bool EndCap = true;
        public bool CornerLines = true;
        public bool Highlight = true;
        public float Outline = ComicAtlas.OutlineWidth;

        public static PrismStyle Of(Color32 color)
        {
            return new PrismStyle { Side = color, Cap = color };
        }

        public static PrismStyle Round(Color32 color)
        {
            return new PrismStyle { Side = color, Cap = color, Smooth = true, CornerLines = false };
        }
    }

    sealed class MeshBuilder
    {
        // Painted shading assumes light from above and slightly in front.
        static readonly Vector3 Light = new Vector3(0.15f, 1f, 0.3f).normalized;
        const float ShadowBelow = -0.25f;

        readonly ComicAtlas _atlas;
        public readonly MeshData Data = new MeshData();

        public MeshBuilder(ComicAtlas atlas)
        {
            _atlas = atlas;
        }

        public void Prism(Frame f, IList<Vector2> profile, float z0, float z1, PrismStyle style)
        {
            int n = profile.Count;
            float depth = Mathf.Abs(z1 - z0);

            var normals = new Vector2[n];
            var lengths = new float[n + 1];
            for (int i = 0; i < n; i++)
            {
                Vector2 edge = profile[(i + 1) % n] - profile[i];
                normals[i] = new Vector2(edge.y, -edge.x).normalized;
                lengths[i + 1] = lengths[i] + edge.magnitude;
            }

            var side = new AtlasRegion
            {
                Kind = RegionKind.Strip,
                Width = lengths[n],
                Height = depth,
                Base = style.Side,
                Highlight = style.Highlight,
                Outline = style.Outline,
                HatchSpans = new List<Vector2>(),
                CornerLines = new List<float>()
            };
            for (int i = 0; i < n; i++)
            {
                if (Vector3.Dot(f.Direction(normals[i]), Light) < ShadowBelow)
                    side.HatchSpans.Add(new Vector2(lengths[i], lengths[i + 1]));
                if (style.CornerLines && !style.Smooth)
                {
                    Vector2 previous = normals[(i + n - 1) % n];
                    if (Vector2.Dot(previous, normals[i]) < 0.6f)
                    {
                        side.CornerLines.Add(lengths[i]);
                        if (i == 0)
                            side.CornerLines.Add(lengths[n]);
                    }
                }
            }
            int sideRegion = _atlas.Add(side);

            for (int i = 0; i < n; i++)
            {
                Vector2 a = profile[i];
                Vector2 b = profile[(i + 1) % n];
                Vector3 na = f.Direction(style.Smooth ? VertexNormal(normals, i) : normals[i]);
                Vector3 nb = f.Direction(style.Smooth ? VertexNormal(normals, (i + 1) % n) : normals[i]);
                Vector3 face = f.Direction(normals[i]);
                Quad(
                    f.Point(a, z0), f.Point(b, z0), f.Point(b, z1), f.Point(a, z1),
                    na, nb, nb, na, face,
                    sideRegion,
                    new Vector2(lengths[i], 0f), new Vector2(lengths[i + 1], 0f), new Vector2(lengths[i + 1], depth), new Vector2(lengths[i], depth));
            }

            if (style.EndCap)
                Cap(f, profile, z1, style, z1 > z0 ? f.Z : -f.Z);
            if (style.StartCap)
                Cap(f, profile, z0, style, z1 > z0 ? -f.Z : f.Z);
        }

        public void Cylinder(Frame f, float radius, float z0, float z1, PrismStyle style, int segments = 20)
        {
            Prism(f, Circle(radius, segments), z0, z1, style);
        }

        public void Box(Frame f, Vector2 size, float z0, float z1, PrismStyle style)
        {
            Prism(f, Rectangle(size.x, size.y, 0f), z0, z1, style);
        }

        void Cap(Frame f, IList<Vector2> profile, float z, PrismStyle style, Vector3 normal)
        {
            Vector2 min = profile[0];
            Vector2 max = profile[0];
            Vector2 center = Vector2.zero;
            foreach (Vector2 p in profile)
            {
                min = Vector2.Min(min, p);
                max = Vector2.Max(max, p);
                center += p;
            }
            center /= profile.Count;

            var polygon = new List<Vector2>();
            foreach (Vector2 p in profile)
                polygon.Add(p - min);
            var region = new AtlasRegion
            {
                Kind = style.Smooth ? RegionKind.Disc : RegionKind.Polygon,
                Width = max.x - min.x,
                Height = max.y - min.y,
                Base = style.Cap,
                Highlight = style.Highlight,
                Outline = style.Outline,
                Polygon = polygon,
                Hatch = Vector3.Dot(normal, Light) < ShadowBelow
            };
            int index = _atlas.Add(region);

            int start = Data.Vertices.Count;
            Add(f.Point(center, z), normal, index, center - min);
            foreach (Vector2 p in profile)
                Add(f.Point(p, z), normal, index, p - min);
            for (int i = 0; i < profile.Count; i++)
                Triangle(start, start + 1 + i, start + 1 + (i + 1) % profile.Count, normal);
        }

        void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 na, Vector3 nb, Vector3 nc, Vector3 nd, Vector3 face, int region, Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud)
        {
            int start = Data.Vertices.Count;
            Add(a, na, region, ua);
            Add(b, nb, region, ub);
            Add(c, nc, region, uc);
            Add(d, nd, region, ud);
            Triangle(start, start + 1, start + 2, face);
            Triangle(start, start + 2, start + 3, face);
        }

        void Add(Vector3 position, Vector3 normal, int region, Vector2 local)
        {
            Data.Vertices.Add(position);
            Data.Normals.Add(normal.normalized);
            Data.Region.Add(region);
            Data.Local.Add(local);
        }

        // Unity draws a triangle from the side where Cross(b - a, c - a) points.
        void Triangle(int a, int b, int c, Vector3 outward)
        {
            Vector3 pa = Data.Vertices[a];
            Vector3 cross = Vector3.Cross(Data.Vertices[b] - pa, Data.Vertices[c] - pa);
            Data.Triangles.Add(a);
            if (Vector3.Dot(cross, outward) >= 0f)
            {
                Data.Triangles.Add(b);
                Data.Triangles.Add(c);
            }
            else
            {
                Data.Triangles.Add(c);
                Data.Triangles.Add(b);
            }
        }

        static Vector2 VertexNormal(Vector2[] normals, int i)
        {
            return (normals[i] + normals[(i + normals.Length - 1) % normals.Length]).normalized;
        }

        // Counter-clockwise outlines.
        public static List<Vector2> Circle(float radius, int segments)
        {
            var points = new List<Vector2>();
            for (int i = 0; i < segments; i++)
            {
                float angle = i / (float)segments * Mathf.PI * 2f;
                points.Add(new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius);
            }
            return points;
        }

        public static List<Vector2> Rectangle(float width, float height, float chamfer)
        {
            return Rectangle(new Vector2(-width * 0.5f, -height * 0.5f), new Vector2(width * 0.5f, height * 0.5f), chamfer);
        }

        public static List<Vector2> Rectangle(Vector2 min, Vector2 max, float chamfer)
        {
            var points = new List<Vector2>();
            if (chamfer <= 0f)
            {
                points.Add(new Vector2(min.x, min.y));
                points.Add(new Vector2(max.x, min.y));
                points.Add(new Vector2(max.x, max.y));
                points.Add(new Vector2(min.x, max.y));
                return points;
            }
            points.Add(new Vector2(min.x + chamfer, min.y));
            points.Add(new Vector2(max.x - chamfer, min.y));
            points.Add(new Vector2(max.x, min.y + chamfer));
            points.Add(new Vector2(max.x, max.y - chamfer));
            points.Add(new Vector2(max.x - chamfer, max.y));
            points.Add(new Vector2(min.x + chamfer, max.y));
            points.Add(new Vector2(min.x, max.y - chamfer));
            points.Add(new Vector2(min.x, min.y + chamfer));
            return points;
        }
    }
}
