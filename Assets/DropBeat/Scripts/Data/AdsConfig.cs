using UnityEngine;

namespace DropBeat
{
    /// <summary>
    /// Ad settings (Resources/AdsConfig). Fill in the app key and ad unit IDs from the Unity LevelPlay
    /// dashboard. With an empty app key no real ads are requested: the editor shows a simulated test ad so
    /// the flow can be tried, and builds simply skip ads (rewards are still granted, so nothing gets stuck).
    /// </summary>
    [CreateAssetMenu(menuName = "DropBeat/Ads Config", fileName = "AdsConfig")]
    public sealed class AdsConfig : ScriptableObject
    {
        [Header("Unity LevelPlay (dashboard: Apps > your app)")]
        public string iosAppKey = "";
        public string androidAppKey = "";

        [Header("Ad units")]
        public string iosInterstitialId = "";
        public string iosRewardedId = "";
        public string androidInterstitialId = "";
        public string androidRewardedId = "";

        [Header("Rules")]
        [Tooltip("Show a full-screen ad after every Nth finished song (0 = never).")]
        [Min(0)] public int interstitialEvery = 3;
        [Tooltip("In the editor, show a simulated test ad instead of a real one.")]
        public bool simulateInEditor = true;
        [Tooltip("Shows the 'Test mode (unlock all)' switch in Settings. Turn OFF before publishing.")]
        public bool allowTestMode = true;

        public string AppKey => Application.platform == RuntimePlatform.Android ? androidAppKey : iosAppKey;
        public string InterstitialId => Application.platform == RuntimePlatform.Android ? androidInterstitialId : iosInterstitialId;
        public string RewardedId => Application.platform == RuntimePlatform.Android ? androidRewardedId : iosRewardedId;
        public bool Configured => !string.IsNullOrEmpty(AppKey);

        private static AdsConfig _instance;

        public static AdsConfig Instance
        {
            get
            {
                if (_instance == null) _instance = Resources.Load<AdsConfig>("AdsConfig");
                if (_instance == null) _instance = CreateInstance<AdsConfig>();
                return _instance;
            }
        }
    }
}
