using Tools;
using UnityEngine;

namespace HandyBeaconTracker
{
    // The tool in the player's hand. Right click opens the game's target list, left click picks the next target.
    // The prefab copy that other players, the icon and a dropped tracker use stays disabled, so it never updates.
    public class HandyTrackerTool : Tool
    {
        public MeshRenderer display;
        public Transform needle;

        static readonly int ScreenTexture = Shader.PropertyToID("_ScreenRenderTexture");

        TrackerScreen _painter;
        Texture2D _texture;
        Material _material;
        float _needleAngle;
        float _needleSpeed;
        float _wander;

        public static HandyTrackerTool Held { get; private set; }

        public override void EnableTool()
        {
            base.EnableTool();
            Held = this;
            FindParts();
            SetUpDisplay();
            TrackerKeyTips.Refresh();
        }

        void FindParts()
        {
            foreach (Transform child in GetComponentsInChildren<Transform>(true))
            {
                if (display == null && child.name == TrackerLook.DisplayName)
                    display = child.GetComponent<MeshRenderer>();
                else if (needle == null && child.name == TrackerLook.NeedleName)
                    needle = child;
            }
        }

        public override void DisableTool()
        {
            TargetMenu.CloseIfOpen();
            if (Held == this)
                Held = null;
            base.DisableTool();
            ReleaseDisplay();
            TrackerKeyTips.Refresh();
        }

        public override void PrimaryActionBegin()
        {
            TrackerTarget.SelectNext(transform.position);
        }

        public override void SecondaryActionBegin()
        {
            TargetMenu.Toggle();
        }

        void Update()
        {
            if (Held != this)
                return;
            UpdateNeedle();
            DrawDisplay();
        }

        void OnDestroy()
        {
            if (Held == this)
                Held = null;
            ReleaseDisplay();
        }

        void SetUpDisplay()
        {
            if (display == null || _texture != null)
                return;
            _painter = new TrackerScreen { Primary = TrackerLook.ScreenColor };
            _texture = TrackerLook.NewScreenTexture("HandyBeaconTracker.Screen");
            // .material makes a copy for this tracker only, so the template keeps its idle screen.
            _material = display.material;
            if (_material.HasProperty(ScreenTexture))
                _material.SetTexture(ScreenTexture, _texture);
            else
                _material.mainTexture = _texture;
            DrawDisplay();
        }

        void ReleaseDisplay()
        {
            if (_texture != null)
                Destroy(_texture);
            if (_material != null)
                Destroy(_material);
            _texture = null;
            _material = null;
            _painter = null;
        }

        void DrawDisplay()
        {
            if (_painter == null || _texture == null)
                return;
            _painter.Draw(TrackerNavigation.View(Time.unscaledTime));
            _texture.SetPixels32(_painter.Pixels);
            _texture.Apply(false, false);
        }

        // A damped needle that settles on the same world direction the card calls north.
        void UpdateNeedle()
        {
            if (needle == null)
                return;
            float dt = Mathf.Min(Time.deltaTime, 0.05f);
            float goal;
            if (TrackerNavigation.NeedleDirection(transform.position, out Vector3 north))
            {
                Vector3 local = needle.parent.InverseTransformDirection(north);
                local.y = 0f;
                goal = local.sqrMagnitude < 1e-4f ? _needleAngle : Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
            }
            else
            {
                _wander += dt * 23f;
                goal = _needleAngle + Mathf.Sin(_wander * 0.13f) * 40f;
            }
            float error = Mathf.DeltaAngle(_needleAngle, goal);
            _needleSpeed += (error * 40f - _needleSpeed * 7f) * dt;
            _needleAngle += _needleSpeed * dt;
            needle.localRotation = Rotations.Axis(Vector3.up, _needleAngle);
        }
    }
}
