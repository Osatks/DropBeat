namespace DropBeat
{
    /// <summary>The daily loop's game side: the daily challenge run and its attempts (see <see cref="Daily"/>).</summary>
    public sealed partial class GameController
    {
        private bool _challengeRun;

        /// <summary>Today's challenge, using one attempt. Locked songs can be played here: a free taste.</summary>
        public void PlayDailyChallenge()
        {
            if (Ads.Showing) return;
            Daily.Challenge c = Daily.TodaysChallenge(library);
            if (c.song == null) return;
            if (Daily.ChallengeDone)
            {
                ui.ShowToast("Challenge complete! A new one at midnight");
                return;
            }
            if (!Daily.TryUseAttempt())
            {
                ui.ShowToast("No attempts left today");
                return;
            }
            Daily.BeginChallenge(c);
            _challengeRun = true;
            StartSong(c.song, c.difficulty, false);
        }

        /// <summary>One more challenge attempt for a rewarded ad (at most <see cref="Daily.MaxAdAttempts"/> a day).</summary>
        public void BuyChallengeAttempt()
        {
            if (Ads.Showing || !Daily.CanBuyAttempt) return;
            if (!Ads.RewardedReady)
            {
                ui.ShowToast("Ad not ready yet, try again in a moment");
                return;
            }
            Ads.Rewarded(result =>
            {
                if (result == Ads.RewardResult.Earned)
                {
                    Daily.AddAdAttempt();
                    sfx.PlayNow(sfx.PowerUp, 0.6f);
                    ui.ShowToast("+1 attempt");
                }
                else ui.ShowToast(result == Ads.RewardResult.Failed ? "Ad not ready yet, try again in a moment" : "Watch the whole ad to get the attempt");
                ui.RefreshMenu();
            });
        }

        /// <summary>Retry of a challenge run costs an attempt; returns false (and says so) when none are left.</summary>
        private bool ChallengeRetryAllowed()
        {
            if (!_challengeRun) return true;
            if (Daily.ChallengeDone || !Daily.TryUseAttempt())
            {
                ui.ShowToast(Daily.ChallengeDone ? "Challenge complete! A new one at midnight" : "No attempts left today");
                return false;
            }
            return true;
        }
    }
}
