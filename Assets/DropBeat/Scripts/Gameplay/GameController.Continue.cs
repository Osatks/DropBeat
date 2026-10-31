using UnityEngine;

namespace DropBeat
{
    /// <summary>
    /// Continue after failing: when life runs out, the song stops and the player gets one offer per run to
    /// watch a rewarded ad and carry on with half their life (after the usual 3-2-1 resume). The offer
    /// times out on its own, so declining never needs a tap.
    /// </summary>
    public sealed partial class GameController
    {
        /// <summary>How long the offer stays up (seconds, not counting an ad on screen).</summary>
        private const double ContinueSeconds = 6.0;
        /// <summary>Life the player continues with.</summary>
        private const float ContinueHealth = 0.5f;

        private bool _continueUsed;
        private bool _continueAdUp;
        private double _offerAt;

        /// <summary>Puts up the offer if this run can still take it; false = fail as usual.</summary>
        private bool OfferContinue()
        {
            if (_continueUsed || _autoplay || GameSettings.NoFail || !Ads.RewardedReady) return false;
            _continueUsed = true;
            clock.Pause();
            sfx.StopScheduled();
            sfx.StopHold();
            sfx.StopLayer();
            _swipeNote = null;
            _state = State.ContinueOffer;
            _offerAt = Time.realtimeSinceStartupAsDouble;
            cameraRig.Shake(0.6f);
            sfx.PlayNow(sfx.Miss, 0.7f);
            Screen.sleepTimeout = SleepTimeout.SystemSetting;
            ui.ShowContinueOffer(true, Progress(clock.SongTime));
            return true;
        }

        private void TickContinueOffer()
        {
            _input.Clear();
            if (_continueAdUp || Ads.Showing) return;
            double left = ContinueSeconds - (Time.realtimeSinceStartupAsDouble - _offerAt);
            ui.SetContinueTimer((float)(left / ContinueSeconds), Mathf.CeilToInt((float)left));
            if (left <= 0.0) DeclineContinue();
        }

        /// <summary>Plays the rewarded ad; on the reward the run resumes (3-2-1) with half life.</summary>
        public void AcceptContinue()
        {
            if (_state != State.ContinueOffer || _continueAdUp) return;
            _continueAdUp = true;
            Ads.Rewarded(result =>
            {
                _continueAdUp = false;
                if (_state != State.ContinueOffer) return;
                if (result == Ads.RewardResult.Earned)
                {
                    _score.Revive(ContinueHealth);
                    _healthHitZero = false; // a continued run can still clear
                    ui.ShowContinueOffer(false, 0f);
                    _state = State.Paused;
                    ui.Show(GameUI.Screen.Pause);
                    ResumeGame();
                    sfx.PlayNow(sfx.PowerUp, 0.7f);
                }
                else
                {
                    ui.ShowToast(result == Ads.RewardResult.Failed ? "Ad not ready yet" : "Watch the whole ad to continue");
                    // A few more seconds to decide.
                    _offerAt = Time.realtimeSinceStartupAsDouble - (ContinueSeconds - 4.0);
                }
            });
        }

        /// <summary>Gives up the offer: the usual fail (the music winds down, then the results).</summary>
        public void DeclineContinue()
        {
            if (_state != State.ContinueOffer || _continueAdUp) return;
            ui.ShowContinueOffer(false, 0f);
            ui.Show(GameUI.Screen.Hud);
            clock.Resume(0.02);
            BeginFail();
        }
    }
}
