namespace DropBeat
{
    /// <summary>The shop's game side: buying and restoring purchases (see <see cref="Purchases"/>).</summary>
    public sealed partial class GameController
    {
        public void BuyProduct(string id)
        {
            if (Ads.Showing) return;
            Purchases.Buy(id, (ok, error) =>
            {
                if (ok)
                {
                    sfx.PlayNow(sfx.Fanfare, 0.7f);
                    ui.ShowToast(id == Purchases.RemoveAds ? "Ads removed. Thank you!" : "Unlocked. Thank you!");
                }
                else if (error != null) ui.ShowToast(error);
                ui.RefreshMenu();
            });
        }

        public void RestorePurchases()
        {
            Purchases.Restore((ok, error) =>
            {
                ui.ShowToast(ok ? "Purchases restored" : error ?? "Restore failed");
                ui.RefreshMenu();
            });
        }

        private void OnPurchasesChanged()
        {
            // An unlocked style may now be equipped; prices may have arrived.
            ball.SetSkin(GameSettings.BallSkin);
            ball.SetTrailEffect(GameSettings.Trail, visuals);
            ui.RefreshMenu();
        }
    }
}
