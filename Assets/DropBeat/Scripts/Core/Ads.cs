using System;
using UnityEngine;
using UnityEngine.UI;

namespace DropBeat
{
    /// <summary>
    /// The game's ads: a full-screen ad between songs (every <see cref="AdsConfig.interstitialEvery"/>
    /// finished songs) and a rewarded ad that unlocks a locked song.
    ///
    /// On devices this uses Unity LevelPlay (<see cref="LevelPlayAds"/>). In the editor, or while no app
    /// key is configured, a simulated test ad is shown instead so the flow can be tried. Game audio is
    /// paused while an ad is on screen.
    /// </summary>
    public static class Ads
    {
        /// <summary>How a rewarded ad ended.</summary>
        public enum RewardResult { Earned, Skipped, Failed }

        public interface IProvider
        {
            void Init(AdsConfig config);
            bool InterstitialReady { get; }
            bool RewardedReady { get; }
            void ShowInterstitial(Action done);
            void ShowRewarded(Action<RewardResult> done);
        }

        /// <summary>Raised with true when an ad covers the game and false when it is gone (audio is paused meanwhile).</summary>
        public static event Action<bool> ShowingChanged;

        private static IProvider _provider;
        private static int _songsFinished;

        /// <summary>True while an ad (or the test ad) is on screen.</summary>
        public static bool Showing { get; private set; }

        public static void Init()
        {
            if (_provider != null) return;
            AdsConfig config = AdsConfig.Instance;
#if !UNITY_EDITOR
            if (config.Configured) _provider = new LevelPlayAds();
#endif
            if (_provider == null)
            {
                bool simulate = Application.isEditor ? config.simulateInEditor : false;
                _provider = new SimulatedAds(simulate);
                if (!Application.isEditor) Debug.LogWarning("[DropBeat] Ads: no LevelPlay app key in Resources/AdsConfig, ads are skipped.");
            }
            _provider.Init(config);
        }

        /// <summary>Tells the ad network this player's ad-frequency group and whether they paid (call after a purchase).</summary>
        public static void RefreshSegment()
        {
            Init();
            (_provider as LevelPlayAds)?.SetSegment(GameSettings.InterstitialEvery, GameSettings.PaidAnything);
        }

        public static bool RewardedReady
        {
            get
            {
                Init();
                return _provider.RewardedReady;
            }
        }

        /// <summary>Call when a song reaches the results screen.</summary>
        public static void SongFinished()
        {
            if (!GameSettings.TestMode) _songsFinished++;
        }

        /// <summary>
        /// Leaving the results screen: shows the between-songs ad if one is due, then runs
        /// <paramref name="next"/>. If no ad is due or ready, runs it immediately.
        /// </summary>
        public static void BetweenSongs(Action next)
        {
            Init();
            int every = GameSettings.InterstitialEvery;
            // Due from the Nth song on (not only on exact multiples), so an ad that was not ready is shown next time.
            // Remove Ads (purchase) turns these off; rewarded ads stay, since the player chooses them.
            bool due = every > 0 && _songsFinished >= every && !GameSettings.TestMode && !GameSettings.NoAds;
            if (!due || !_provider.InterstitialReady)
            {
                next();
                return;
            }
            _songsFinished = 0;
            Show(done => _provider.ShowInterstitial(done), next);
        }

        /// <summary>Shows a rewarded ad; <paramref name="done"/> gets whether it was earned, skipped, or never shown.</summary>
        public static void Rewarded(Action<RewardResult> done)
        {
            Init();
            RewardResult result = RewardResult.Failed;
            Show(finish => _provider.ShowRewarded(r => { result = r; finish(); }), () => done(result));
        }

        private static void Show(Action<Action> show, Action after)
        {
            Showing = true;
            AudioListener.pause = true;
            ShowingChanged?.Invoke(true);
            bool finished = false;
            show(() =>
            {
                if (finished) return;
                finished = true;
                AudioListener.pause = false;
                Showing = false;
                ShowingChanged?.Invoke(false);
                after();
            });
        }

        // ------------------------------------------------------------------ editor / unconfigured

        /// <summary>
        /// Stand-in used in the editor (a 3-second "TEST AD" screen) and in builds without an app key
        /// (no ad at all: interstitials are skipped, rewards are granted straight away).
        /// </summary>
        private sealed class SimulatedAds : IProvider
        {
            private readonly bool _visible;
            public SimulatedAds(bool visible) { _visible = visible; }

            public void Init(AdsConfig config) { }
            public bool InterstitialReady => _visible;
            public bool RewardedReady => true;

            public void ShowInterstitial(Action done)
            {
                if (_visible) TestAdScreen.Show("TEST AD\n(between songs)", done);
                else done();
            }

            public void ShowRewarded(Action<RewardResult> done)
            {
                if (_visible) TestAdScreen.Show("TEST REWARDED AD\n(unlocks the song)", () => done(RewardResult.Earned));
                else done(RewardResult.Earned);
            }
        }

        /// <summary>Full-screen placeholder that closes itself after a short countdown.</summary>
        private sealed class TestAdScreen : MonoBehaviour
        {
            private Action _done;
            private Text _label;
            private string _title;
            private float _left = 3f;

            public static void Show(string title, Action done)
            {
                var go = new GameObject("TestAd", typeof(RectTransform));
                DontDestroyOnLoad(go);
                var canvas = go.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 1000;
                var scaler = go.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1080, 1920);
                go.AddComponent<GraphicRaycaster>();
                var bg = UIFactory.Stretch(go.transform, "Bg");
                UIFactory.Image(bg, new Color(0.02f, 0.02f, 0.05f, 0.97f), null, true);
                var screen = go.AddComponent<TestAdScreen>();
                screen._done = done;
                screen._title = title;
                screen._label = UIFactory.Label(go.transform, "Label", title, 64, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
            }

            private void Update()
            {
                _left -= Time.unscaledDeltaTime;
                _label.text = _title + "\n\n" + Mathf.CeilToInt(Mathf.Max(0f, _left));
                if (_left > 0f) return;
                Action done = _done;
                _done = null;
                Destroy(gameObject);
                done?.Invoke();
            }
        }
    }
}
