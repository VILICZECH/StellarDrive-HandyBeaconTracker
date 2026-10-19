using MelonLoader;
using UnityEngine;

[assembly: MelonInfo(typeof(HandyBeaconTracker.TrackerMod), "Handy Beacon Tracker", "1.0.0", "VILIK")]
[assembly: MelonGame("CuriousOwlGames", "StellarDrive")]

namespace HandyBeaconTracker
{
    public class TrackerMod : MelonMod
    {
        bool _inGame;
        bool _registering;
        float _nextAttempt;
        float _giveUpAt;

        public override void OnInitializeMelon()
        {
            LoggerInstance.Msg("Handy Beacon Tracker is ready. Craft it at a ship's Targeting Computer once that part is researched.");
        }

        public override void OnSceneWasLoaded(int buildIndex, string sceneName)
        {
            GameServices.Reset();
            TrackerRecipe.Reset();
            TrackerNavigation.Reset();
            TrackerTarget.SceneLoaded();
            TargetMenu.SceneLoaded();
            _inGame = sceneName == "MainGame";
            _registering = _inGame;
            _nextAttempt = 0f;
            _giveUpAt = Time.unscaledTime + 60f;
            if (_inGame)
                RegisterItem();
        }

        public override void OnUpdate()
        {
            if (!_inGame)
                return;
            CraftNet.Ensure();
            if (_registering && Time.unscaledTime >= _nextAttempt)
                RegisterItem();
        }

        void RegisterItem()
        {
            _nextAttempt = Time.unscaledTime + 1f;
            if (TrackerItem.EnsureRegistered())
            {
                _registering = false;
            }
            else if (Time.unscaledTime > _giveUpAt)
            {
                _registering = false;
                LoggerInstance.Warning("The game's item list was not found, so the Handy Beacon Tracker is not available in this world.");
            }
        }
    }
}
