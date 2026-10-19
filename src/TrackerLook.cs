using HarmonyLib;
using MelonLoader;
using Tools.Build;
using Tools.Interface.Model;
using UnityEngine;

namespace HandyBeaconTracker
{
    // Turns the generated geometry into the tool prefab: the Build Tool's own materials (Custom/PlanetObject for the
    // body, Custom/PlanetScreen for the display) with the tracker's painted albedo, and the Paint Tool's hand pose.
    static class TrackerLook
    {
        public const string DisplayName = "Display";
        public const string NeedleName = "CompassNeedle";
        const int AtlasSize = 1024;
        static readonly int Albedo = Shader.PropertyToID("_Albedo");
        static readonly int Palette = Shader.PropertyToID("_ColorPalette");
        static readonly int ScreenTexture = Shader.PropertyToID("_ScreenRenderTexture");
        static readonly int ScreenBackLight = Shader.PropertyToID("_ScreenBackLightColor");

        static readonly AccessTools.FieldRef<BuildToolScreenUpdater, MeshRenderer> ScreenField = AccessTools.FieldRefAccess<BuildToolScreenUpdater, MeshRenderer>("screen");
        static readonly AccessTools.FieldRef<BuildToolScreenUpdater, Color> BackLitField = AccessTools.FieldRefAccess<BuildToolScreenUpdater, Color>("screenBackLitColor");
        static readonly AccessTools.FieldRef<BuildToolScreenUpdater, Color> ObjectColorField = AccessTools.FieldRefAccess<BuildToolScreenUpdater, Color>("objectOnScreenColor");

        // Paint Tool RightHandTransform in its Visuals, read from sharedassets0.assets.
        static readonly Vector3 FallbackHandPosition = new Vector3(0.043f, -0.013f, -0.099f);
        static readonly Quaternion FallbackHandRotation = new Quaternion(0f, 0f, -0.726f, 0.688f);

        public static Color32 ScreenColor { get; private set; } = new Color32(120, 230, 255, 255);
        public static Color ScreenBackLightColor { get; private set; } = new Color(0.035f, 0.104f, 0.149f, 1f);
        public static bool HasBackLight { get; private set; }

        public static GameObject BuildTemplate(GameObject buildTool, GameObject paintTool, Transform holder)
        {
            var geometry = new TrackerGeometry(AtlasSize);
            ReadScreenColors(buildTool);
            Material body = BodyMaterial(buildTool, geometry);
            Material screen = ScreenMaterial(buildTool, IdleScreen());

            var root = new GameObject("HandyBeaconTracker");
            root.layer = buildTool.layer;
            root.transform.SetParent(holder, false);

            var visuals = new GameObject("Visuals");
            visuals.layer = root.layer;
            visuals.transform.SetParent(root.transform, false);

            MeshRenderer model = Part(visuals.transform, "Model", ToMesh(geometry.Body, "HandyBeaconTracker.Body"), body, Vector3.zero);
            MeshRenderer needle = Part(visuals.transform, NeedleName, ToMesh(geometry.Needle, "HandyBeaconTracker.Needle"), body, geometry.NeedlePivot);
            MeshRenderer display = Part(visuals.transform, DisplayName, ScreenQuad(geometry), screen, Vector3.zero);

            var hand = new GameObject("RightHandTransform").transform;
            hand.SetParent(visuals.transform, false);
            HandPose(paintTool, out Vector3 handPosition, out Quaternion handRotation);
            hand.localPosition = handPosition;
            hand.localRotation = handRotation;

            ToolVisuals pose = visuals.AddComponent<ToolVisuals>();
            CopyThirdPerson(buildTool.GetComponentInChildren<ToolVisuals>(true), pose);
            pose.firstPersonPosition = TrackerGeometry.FirstPersonPosition;
            pose.firstPersonRotation = TrackerGeometry.FirstPersonRotation;
            pose.rightHandTargetTransform = hand;
            pose.leftHandFirstPersonTargetTransform = null;
            pose.leftHandThirdPersonTargetTransform = null;

            HandyTrackerTool tool = root.AddComponent<HandyTrackerTool>();
            tool.display = display;
            tool.needle = needle.transform;
            tool.enabled = false;

            MelonLogger.Msg("Tracker model: " + geometry.Body.Triangles.Count / 3 + " triangles, albedo " + AtlasSize + " px at " + Mathf.RoundToInt(geometry.Atlas.Density) + " px/m, body shader " + (body != null ? body.shader.name : "none") + ", screen shader " + (screen != null ? screen.shader.name : "none") + ", hand at " + handPosition.ToString("F3") + ".");
            return root;
        }

        static MeshRenderer Part(Transform parent, string name, Mesh mesh, Material material, Vector3 position)
        {
            var node = new GameObject(name);
            node.layer = parent.gameObject.layer;
            node.transform.SetParent(parent, false);
            node.transform.localPosition = position;
            node.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = node.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            return renderer;
        }

        static Mesh ToMesh(MeshData data, string name)
        {
            var mesh = new Mesh { name = name, hideFlags = HideFlags.DontUnloadUnusedAsset };
            mesh.SetVertices(data.Vertices);
            mesh.SetNormals(data.Normals);
            mesh.SetUVs(0, data.Uvs);
            mesh.SetTriangles(data.Triangles, 0);
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            return mesh;
        }

        static Mesh ScreenQuad(TrackerGeometry g)
        {
            Vector3 right = Vector3.Cross(g.ScreenUp, -g.ScreenNormal).normalized;
            float h = g.ScreenSize * 0.5f;
            Vector3 c = g.ScreenCenter;
            var mesh = new Mesh { name = "HandyBeaconTracker.Display", hideFlags = HideFlags.DontUnloadUnusedAsset };
            mesh.vertices = new[]
            {
                c - right * h - g.ScreenUp * h,
                c + right * h - g.ScreenUp * h,
                c + right * h + g.ScreenUp * h,
                c - right * h + g.ScreenUp * h
            };
            mesh.normals = new[] { g.ScreenNormal, g.ScreenNormal, g.ScreenNormal, g.ScreenNormal };
            mesh.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) };
            // Cross(b - a, c - a) must point along the screen normal for Unity to draw the front.
            mesh.triangles = Vector3.Dot(Vector3.Cross(right, g.ScreenUp), g.ScreenNormal) >= 0f
                ? new[] { 0, 1, 2, 0, 2, 3 }
                : new[] { 0, 2, 1, 0, 3, 2 };
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            return mesh;
        }

        static Material BodyMaterial(GameObject buildTool, TrackerGeometry geometry)
        {
            Material source = null;
            foreach (MeshRenderer renderer in buildTool.GetComponentsInChildren<MeshRenderer>(true))
            {
                Material m = renderer.sharedMaterial;
                if (m != null && m.HasProperty(Albedo) && !m.HasProperty(ScreenTexture))
                {
                    source = m;
                    break;
                }
            }

            var atlas = new Texture2D(AtlasSize, AtlasSize, TextureFormat.RGBA32, true, false)
            {
                name = "HandyBeaconTracker.Albedo",
                hideFlags = HideFlags.DontUnloadUnusedAsset,
                filterMode = FilterMode.Trilinear,
                anisoLevel = 4,
                wrapMode = TextureWrapMode.Clamp
            };
            atlas.SetPixels32(geometry.Atlas.Paint());
            atlas.Apply(true, true);

            Material material;
            if (source != null)
            {
                material = new Material(source);
                material.SetTexture(Albedo, atlas);
                if (material.HasProperty(Palette))
                    material.SetTexture(Palette, Texture2D.whiteTexture);
                material.DisableKeyword("_COLOR_PALETTE");
            }
            else
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                material.SetTexture("_BaseMap", atlas);
                MelonLogger.Warning("The Build Tool material was not found; the tracker uses a plain lit material.");
            }
            foreach (string property in new[] { "_BaseColor", "_Color" })
            {
                if (material.HasProperty(property))
                    material.SetColor(property, Color.white);
            }
            material.name = "HandyBeaconTracker.Body";
            material.hideFlags = HideFlags.DontUnloadUnusedAsset;
            return material;
        }

        static Material ScreenMaterial(GameObject buildTool, Texture2D idle)
        {
            Material source = null;
            BuildToolScreenUpdater updater = buildTool.GetComponentInChildren<BuildToolScreenUpdater>(true);
            if (updater != null && ScreenField(updater) != null)
                source = ScreenField(updater).sharedMaterial;
            if (source == null)
            {
                foreach (MeshRenderer renderer in buildTool.GetComponentsInChildren<MeshRenderer>(true))
                {
                    if (renderer.sharedMaterial != null && renderer.sharedMaterial.HasProperty(ScreenTexture))
                    {
                        source = renderer.sharedMaterial;
                        break;
                    }
                }
            }

            Material material;
            if (source != null)
            {
                material = new Material(source);
                material.SetTexture(ScreenTexture, idle);
                if (HasBackLight)
                    material.SetColor(ScreenBackLight, ScreenBackLightColor);
            }
            else
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("UI/Default"));
                material.mainTexture = idle;
                MelonLogger.Warning("The Build Tool screen material was not found; the tracker display is unlit.");
            }
            material.name = "HandyBeaconTracker.Screen";
            material.hideFlags = HideFlags.DontUnloadUnusedAsset;
            return material;
        }

        // The Build Tool draws its part preview in this color over a black background.
        static void ReadScreenColors(GameObject buildTool)
        {
            BuildToolScreenUpdater updater = buildTool.GetComponentInChildren<BuildToolScreenUpdater>(true);
            if (updater == null)
                return;
            Color color = ObjectColorField(updater);
            if (color.maxColorComponent > 0.05f)
            {
                color.a = 1f;
                ScreenColor = color;
            }
            ScreenBackLightColor = BackLitField(updater);
            HasBackLight = true;
        }

        public static Texture2D NewScreenTexture(string name)
        {
            return new Texture2D(TrackerScreen.Size, TrackerScreen.Size, TextureFormat.RGBA32, false, false)
            {
                name = name,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
        }

        // Shown on the template: the icon, a dropped tracker and other players' trackers.
        static Texture2D IdleScreen()
        {
            var painter = new TrackerScreen { Primary = ScreenColor };
            painter.Draw(new ScreenView { Compass = true, Cardinals = true, NorthAngle = 0f, Time = 0.35f, Hint = "RMB: targets" });
            Texture2D texture = NewScreenTexture("HandyBeaconTracker.IdleScreen");
            texture.hideFlags = HideFlags.DontUnloadUnusedAsset;
            texture.SetPixels32(painter.Pixels);
            texture.Apply(false, true);
            return texture;
        }

        static void HandPose(GameObject paintTool, out Vector3 position, out Quaternion rotation)
        {
            position = FallbackHandPosition;
            rotation = FallbackHandRotation;
            ToolVisuals visuals = paintTool != null ? paintTool.GetComponentInChildren<ToolVisuals>(true) : null;
            if (visuals == null || visuals.rightHandTargetTransform == null)
                return;
            Transform origin = visuals.transform;
            Transform hand = visuals.rightHandTargetTransform;
            position = origin.InverseTransformPoint(hand.position);
            rotation = Quaternion.Inverse(origin.rotation) * hand.rotation;
        }

        static void CopyThirdPerson(ToolVisuals from, ToolVisuals to)
        {
            if (from == null)
            {
                to.maxLookAngles = new Vector2(-80f, 60f);
                to.lookUpPosition = new Vector3(0.14f, 0.55f, 0.15f);
                to.lookForwardPosition = new Vector3(0.14f, 0.21f, 0.24f);
                to.lookDownPosition = new Vector3(0.16f, 0.02f, 0.12f);
                return;
            }
            to.maxLookAngles = from.maxLookAngles;
            to.lookUpPosition = from.lookUpPosition;
            to.lookForwardPosition = from.lookForwardPosition;
            to.lookDownPosition = from.lookDownPosition;
            to.lookDownRotationOffset = from.lookDownRotationOffset;
        }
    }
}
