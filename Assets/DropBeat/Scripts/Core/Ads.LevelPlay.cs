using System;
using Unity.Services.LevelPlay;
using UnityEngine;

namespace DropBeat
{
    /// <summary>
    /// <see cref="Ads.IProvider"/> backed by Unity LevelPlay (package com.unity.services.levelplay).
    /// Initialises the SDK with the app key from <see cref="AdsConfig"/>, keeps one interstitial and one
    /// rewarded ad loaded, reloads each after it is shown, and retries failed loads with a growing delay.
    /// </summary>
    public sealed class LevelPlayAds : Ads.IProvider
    {
        /// <summary>How long a closed rewarded ad waits for a late OnAdRewarded (networks may send it after the close).</summary>
        private const float RewardGrace = 1f;

        private LevelPlayInterstitialAd _interstitial;
        private LevelPlayRewardedAd _rewarded;
        private bool _ready;
        private Action _interstitialDone;
        private Action<Ads.RewardResult> _rewardedDone;
        private bool _earned;
        private float _closedAt = -1f;

        // Load retries: 5, 10, 20, 40, then every 60 s.
        private float _interstitialRetryAt = -1f, _rewardedRetryAt = -1f;
        private float _interstitialDelay = 5f, _rewardedDelay = 5f;

        public void Init(AdsConfig config)
        {
            LevelPlay.OnInitSuccess += _ => CreateAds(config);
            LevelPlay.OnInitFailed += error => Debug.LogWarning("[DropBeat] LevelPlay init failed: " + error);
            LevelPlay.Init(config.AppKey);
            Ticker.Create(Tick);
        }

        private void CreateAds(AdsConfig config)
        {
            _ready = true;
            if (!string.IsNullOrEmpty(config.InterstitialId))
            {
                _interstitial = new LevelPlayInterstitialAd(config.InterstitialId);
                _interstitial.OnAdClosed += _ => FinishInterstitial();
                _interstitial.OnAdDisplayFailed += (_, error) =>
                {
                    Debug.LogWarning("[DropBeat] Interstitial failed to show: " + error);
                    FinishInterstitial();
                };
                _interstitial.OnAdLoaded += _ => _interstitialDelay = 5f;
                _interstitial.OnAdLoadFailed += error =>
                {
                    Debug.Log("[DropBeat] Interstitial not loaded: " + error);
                    _interstitialRetryAt = Time.realtimeSinceStartup + _interstitialDelay;
                    _interstitialDelay = Mathf.Min(60f, _interstitialDelay * 2f);
                };
                _interstitial.LoadAd();
            }
            if (!string.IsNullOrEmpty(config.RewardedId))
            {
                _rewarded = new LevelPlayRewardedAd(config.RewardedId);
                _rewarded.OnAdRewarded += (_, reward) =>
                {
                    _earned = true;
                    if (_closedAt >= 0f) FinishRewarded(); // the reward came after the close
                };
                _rewarded.OnAdClosed += _ =>
                {
                    if (_earned) FinishRewarded();
                    else _closedAt = Time.realtimeSinceStartup; // wait briefly for a late reward
                };
                _rewarded.OnAdDisplayFailed += (_, error) =>
                {
                    Debug.LogWarning("[DropBeat] Rewarded ad failed to show: " + error);
                    FinishRewarded(Ads.RewardResult.Failed);
                };
                _rewarded.OnAdLoaded += _ => _rewardedDelay = 5f;
                _rewarded.OnAdLoadFailed += error =>
                {
                    Debug.Log("[DropBeat] Rewarded ad not loaded: " + error);
                    _rewardedRetryAt = Time.realtimeSinceStartup + _rewardedDelay;
                    _rewardedDelay = Mathf.Min(60f, _rewardedDelay * 2f);
                };
                _rewarded.LoadAd();
            }
        }

        public bool InterstitialReady => _ready && _interstitial != null && _interstitial.IsAdReady();
        public bool RewardedReady => _ready && _rewarded != null && _rewarded.IsAdReady();

        private void Tick()
        {
            float now = Time.realtimeSinceStartup;
            if (_rewardedDone != null && _closedAt >= 0f && now - _closedAt > RewardGrace) FinishRewarded();
            if (_interstitialRetryAt >= 0f && now >= _interstitialRetryAt)
            {
                _interstitialRetryAt = -1f;
                _interstitial?.LoadAd();
            }
            if (_rewardedRetryAt >= 0f && now >= _rewardedRetryAt)
            {
                _rewardedRetryAt = -1f;
                _rewarded?.LoadAd();
            }
        }

        public void ShowInterstitial(Action done)
        {
            if (!InterstitialReady)
            {
                done();
                return;
            }
            _interstitialDone = done;
            _interstitial.ShowAd();
        }

        public void ShowRewarded(Action<Ads.RewardResult> done)
        {
            if (!RewardedReady)
            {
                _rewarded?.LoadAd();
                done(Ads.RewardResult.Failed);
                return;
            }
            _earned = false;
            _closedAt = -1f;
            _rewardedDone = done;
            _rewarded.ShowAd();
        }

        private void FinishInterstitial()
        {
            Action done = _interstitialDone;
            _interstitialDone = null;
            _interstitial.LoadAd();
            done?.Invoke();
        }

        private void FinishRewarded(Ads.RewardResult? forced = null)
        {
            Action<Ads.RewardResult> done = _rewardedDone;
            if (done == null) return; // already settled: exactly one callback per ad
            _rewardedDone = null;
            _closedAt = -1f;
            _rewarded.LoadAd();
            done(forced ?? (_earned ? Ads.RewardResult.Earned : Ads.RewardResult.Skipped));
        }

        /// <summary>Calls back every frame (reward grace period and load retries).</summary>
        private sealed class Ticker : MonoBehaviour
        {
            private Action _tick;

            public static void Create(Action tick)
            {
                var go = new GameObject("LevelPlayTicker");
                go.hideFlags = HideFlags.HideInHierarchy;
                DontDestroyOnLoad(go);
                go.AddComponent<Ticker>()._tick = tick;
            }

            private void Update() => _tick?.Invoke();
        }
    }
}
