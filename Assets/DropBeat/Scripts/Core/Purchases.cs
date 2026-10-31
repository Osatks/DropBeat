using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Purchasing;

namespace DropBeat
{
    /// <summary>
    /// In-app purchases (Unity IAP 5): Remove Ads, All Songs and All Styles, all non-consumable. A bought
    /// item is granted as soon as the store reports it and saved locally (<see cref="GameSettings.Owns"/>);
    /// previous purchases come back through <see cref="Restore"/> (iOS) or the automatic fetch (Android).
    /// In the editor Unity IAP uses its fake store, so every flow can be tried without a store account.
    /// Create products with these exact ids in App Store Connect and the Play Console before release.
    /// </summary>
    public static class Purchases
    {
        public const string RemoveAds = "dropbeat.removeads";
        public const string AllSongs = "dropbeat.allsongs";
        public const string AllStyles = "dropbeat.allstyles";

        public static readonly string[] All = { RemoveAds, AllSongs, AllStyles };

        /// <summary>Shown until the store returns real, localised prices.</summary>
        private static readonly Dictionary<string, string> FallbackPrice = new Dictionary<string, string>
        {
            { RemoveAds, "$2.99" }, { AllSongs, "$1.99" }, { AllStyles, "$2.99" }
        };

        private static StoreController _store;
        private static Action<bool, string> _buying;
        private static float _buyingSince;
        /// <summary>A purchase the store never answered stops blocking new ones after this long.</summary>
        private const float BuyTimeout = 30f;

        /// <summary>True once the store has returned the products (prices and buying work).</summary>
        public static bool Ready { get; private set; }

        /// <summary>Raised when ownership or prices change.</summary>
        public static event Action Changed;

        public static async void Init()
        {
            if (_store != null) return;
            try
            {
                try { await Unity.Services.Core.UnityServices.InitializeAsync(); }
                catch (Exception e) { Debug.LogWarning("[DropBeat] IAP: Unity Services init: " + e.Message); }

                _store = UnityIAPServices.StoreController();
                _store.OnProductsFetched += _ =>
                {
                    Ready = true;
                    _store.FetchPurchases();
                    Changed?.Invoke();
                };
                _store.OnProductsFetchFailed += f => Debug.LogWarning("[DropBeat] IAP: products not fetched: " + f.FailureReason);
                _store.OnPurchasePending += order =>
                {
                    Grant(order);
                    _store.ConfirmPurchase(order);
                    Finish(true, null);
                };
                _store.OnPurchaseFailed += order =>
                {
                    Finish(false, order.FailureReason == PurchaseFailureReason.UserCancelled ? null : "Purchase failed: " + order.FailureReason);
                };
                _store.OnPurchaseDeferred += _ => Finish(false, "Purchase is waiting for approval");
                _store.OnPurchasesFetched += orders =>
                {
                    foreach (ConfirmedOrder o in orders.ConfirmedOrders) Grant(o);
                    foreach (PendingOrder o in orders.PendingOrders)
                    {
                        Grant(o);
                        _store.ConfirmPurchase(o);
                    }
                };

                await _store.Connect();
                var defs = new List<ProductDefinition>();
                foreach (string id in All) defs.Add(new ProductDefinition(id, ProductType.NonConsumable));
                _store.FetchProducts(defs);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[DropBeat] IAP unavailable: " + e.Message);
            }
        }

        /// <summary>The store's localised price, or the planned price until it is known.</summary>
        public static string Price(string id)
        {
            Product p = _store != null && Ready ? _store.GetProductById(id) : null;
            if (p != null && p.metadata != null && !string.IsNullOrEmpty(p.metadata.localizedPriceString)) return p.metadata.localizedPriceString;
            return FallbackPrice.TryGetValue(id, out string s) ? s : string.Empty;
        }

        /// <summary>Buys a product; <paramref name="done"/> gets success and an error to show (null = say nothing).</summary>
        public static void Buy(string id, Action<bool, string> done)
        {
            if (GameSettings.Owns(id))
            {
                done?.Invoke(true, null);
                return;
            }
            if (_store == null || !Ready)
            {
                Init();
                done?.Invoke(false, "Store not available yet, try again in a moment");
                return;
            }
            if (_buying != null && Time.realtimeSinceStartup - _buyingSince < BuyTimeout)
            {
                done?.Invoke(false, null);
                return;
            }
            _buying = done;
            _buyingSince = Time.realtimeSinceStartup;
            _store.PurchaseProduct(id);
        }

        /// <summary>Restores earlier purchases (required by Apple; on Android they come back automatically).</summary>
        public static void Restore(Action<bool, string> done)
        {
            if (_store == null || !Ready)
            {
                Init();
                done?.Invoke(false, "Store not available yet, try again in a moment");
                return;
            }
            _store.RestoreTransactions((ok, error) =>
            {
                if (ok) _store.FetchPurchases();
                done?.Invoke(ok, ok ? null : "Restore failed" + (string.IsNullOrEmpty(error) ? string.Empty : ": " + error));
            });
        }

        private static void Grant(Order order)
        {
            if (order?.CartOrdered == null) return;
            bool any = false;
            foreach (CartItem item in order.CartOrdered.Items())
            {
                string id = item?.Product?.definition?.id;
                if (string.IsNullOrEmpty(id) || GameSettings.Owns(id)) continue;
                GameSettings.SetOwned(id);
                any = true;
            }
            if (!any) return;
            Ads.RefreshSegment();
            Changed?.Invoke();
        }

        private static void Finish(bool ok, string error)
        {
            Action<bool, string> cb = _buying;
            _buying = null;
            cb?.Invoke(ok, error);
        }
    }
}
