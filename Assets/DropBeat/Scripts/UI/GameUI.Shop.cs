using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace DropBeat
{
    /// <summary>
    /// The SHOP sheet (Remove Ads, All Songs, All Styles, Restore Purchases) and the continue-after-failing
    /// card shown on the pause screen.
    /// </summary>
    public sealed partial class GameUI
    {
        // ---------------------------------------------------------------- shop

        private RectTransform _shop, _shopCard;
        private float _shopOpened;
        private readonly List<Text> _shopPrice = new List<Text>();
        private readonly List<Button> _shopBuy = new List<Button>();

        private static readonly string[] ShopTitles = { "remove ads", "all songs", "all styles" };
        private static readonly string[] ShopLines =
        {
            "No more full-screen ads between songs. Rewarded ads stay optional: you still choose them for extra tries and unlocks.",
            "Unlock every locked song now: Jade Temple, Pixel Arcade, Desert Mirage, Deep Ocean and more.",
            "Every ball and trail that ads unlock, at once. Champion and Aurora are still won by playing."
        };

        private void BuildShop()
        {
            _shop = UIFactory.Stretch(_menu, "Shop");
            UIFactory.Image(_shop, new Color(0.01f, 0f, 0.04f, 0.95f), null, true);
            _shop.offsetMin = new Vector2(0, -300);
            _shop.offsetMax = new Vector2(0, 300);

            _shopCard = UIFactory.Rect(_shop, "Card", new Vector2(0.5f, 0f), new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            _shopCard.offsetMin = new Vector2(-500, 330);
            _shopCard.offsetMax = new Vector2(500, -330);
            UIFactory.Card(_shopCard).raycastTarget = true;

            var header = UIFactory.Rect(_shopCard, "Header", new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), new Vector2(0, -28), new Vector2(-64, 84));
            var title = UIFactory.Label(UIFactory.Rect(header, "Title", Vector2.zero, Vector2.one, new Vector2(0, 0.5f), new Vector2(12, 0), Vector2.zero),
                UIFactory.Spaced("shop"), 40, Color.white, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Gradient(title, UIFactory.Gold, UIFactory.Pink, true);
            var done = UIFactory.Button(header, "Done", UIFactory.Spaced("done"), 24, () => ShowShop(false), out _, UIFactory.ButtonStyle.Primary);
            Place((RectTransform)done.transform, new Vector2(1f, 0.5f), Vector2.zero, new Vector2(190, 76), new Vector2(1f, 0.5f));

            var stack = UIFactory.Rect(_shopCard, "Stack", Vector2.zero, Vector2.one, new Vector2(0.5f, 1f), Vector2.zero, Vector2.zero);
            stack.offsetMin = new Vector2(30, 30);
            stack.offsetMax = new Vector2(-30, -130);
            UIFactory.VStack(stack, 20, 6);

            for (int i = 0; i < Purchases.All.Length; i++)
            {
                string id = Purchases.All[i];
                var card = UIFactory.Rect(stack, ShopTitles[i], Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
                UIFactory.Layout(card, 230);
                UIFactory.Image(card, UIFactory.GlassTint, UIFactory.Glass);
                var icon = UIFactory.At(card, "Icon", new Vector2(0f, 0.5f), new Vector2(100, 0), new Vector2(150, 150));
                ShopIcon(icon, i);
                icon.localScale = Vector3.one * 0.78f; // keeps the drawn icons clear of the text column
                UIFactory.Label(UIFactory.Rect(card, "Title", new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, 1), new Vector2(210, -26), new Vector2(-470, 48)),
                    UIFactory.Spaced(ShopTitles[i]), 32, Color.white, TextAnchor.MiddleLeft, FontStyle.Bold);
                var line = UIFactory.Label(UIFactory.Rect(card, "Line", new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, 1), new Vector2(210, -80), new Vector2(-470, 120)),
                    ShopLines[i], 20, UIFactory.TextColor, TextAnchor.UpperLeft);
                line.horizontalOverflow = HorizontalWrapMode.Wrap;
                var buy = UIFactory.Button(card, "Buy", "$0.99", 30, () => _game.BuyProduct(id), out Text price, UIFactory.ButtonStyle.Primary);
                Place((RectTransform)buy.transform, new Vector2(1f, 0.5f), new Vector2(-24, 0), new Vector2(210, 90), new Vector2(1f, 0.5f));
                _shopBuy.Add(buy);
                _shopPrice.Add(price);
            }

            var restore = UIFactory.Button(stack, "Restore", UIFactory.Spaced("restore purchases"), 24, () => _game.RestorePurchases(), out _, UIFactory.ButtonStyle.Outline);
            UIFactory.Layout(restore, 84);
            var note = UIFactory.Label(stack, "Note", "Purchases are one-time and stay on this account. Prices are set by the App Store and Google Play.", 18, UIFactory.Dim, TextAnchor.MiddleCenter);
            note.horizontalOverflow = HorizontalWrapMode.Wrap;
            UIFactory.Layout(note, 60);
            _shop.gameObject.SetActive(false);
        }

        /// <summary>Drawn icons: a crossed-out AD badge, a stack of song discs, a ball with its trail.</summary>
        private static void ShopIcon(RectTransform area, int kind)
        {
            switch (kind)
            {
                case 0:
                {
                    var badge = UIFactory.Rect(area, "Badge", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(120, 84));
                    UIFactory.Gradient(UIFactory.Image(badge, Color.white, UIFactory.Rounded), UIFactory.Pink, new Color(0.6f, 0.2f, 1f), true);
                    UIFactory.Label(badge, "Ad", "AD", 44, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
                    var slash = UIFactory.Rect(area, "Slash", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(150, 12));
                    slash.localRotation = Quaternion.Euler(0f, 0f, 35f);
                    UIFactory.Image(slash, UIFactory.Gold, UIFactory.Pill);
                    break;
                }
                case 1:
                {
                    Color[] c = { UIFactory.Cyan, UIFactory.Pink, UIFactory.Gold };
                    for (int i = 0; i < 3; i++)
                    {
                        var disc = UIFactory.Rect(area, "Disc" + i, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-30 + i * 30, 10 - i * 10), new Vector2(86, 86));
                        UIFactory.Image(disc, c[i], UIFactory.RingOutline);
                        var hole = UIFactory.Rect(disc, "Hole", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(18, 18));
                        UIFactory.Image(hole, c[i], UIFactory.Circle);
                    }
                    break;
                }
                default:
                    TrailIcon(area, TrailEffect.Rainbow);
                    break;
            }
        }

        private void ShowShop(bool on)
        {
            if (_shop == null || _shop.gameObject.activeSelf == on) return;
            _shop.gameObject.SetActive(on);
            _sheet.gameObject.SetActive(!on);
            _shopOpened = Time.unscaledTime;
            if (on) RefreshShop();
        }

        private void RefreshShop()
        {
            if (_shop == null) return;
            for (int i = 0; i < _shopBuy.Count; i++)
            {
                bool owned = GameSettings.Owns(Purchases.All[i]);
                _shopPrice[i].text = owned ? UIFactory.Spaced("owned") : Purchases.Price(Purchases.All[i]);
                _shopPrice[i].fontSize = owned ? 22 : 30;
                _shopBuy[i].interactable = !owned;
                UIFactory.TintButton(_shopBuy[i], owned ? new Color(1f, 1f, 1f, 0.25f) : Color.white);
            }
        }

        private void AnimateShop(float time)
        {
            if (_shop == null || !_shop.gameObject.activeSelf) return;
            float a = Mathf.Clamp01((time - _shopOpened) / 0.25f);
            float e = 1f - (1f - a) * (1f - a) * (1f - a);
            _shopCard.anchoredPosition = new Vector2(0, Mathf.Lerp(-1600f, 0f, e));
        }

        // ---------------------------------------------------------------- continue offer

        private RectTransform _continueCard;
        private Image _continueRing;
        private Text _continueSecs, _continueLine;

        private void BuildContinue()
        {
            _continueCard = UIFactory.At(_pause, "Continue", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(700, 760));
            UIFactory.Card(_continueCard);
            var title = UIFactory.Label(UIFactory.Rect(_continueCard, "Title", new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), new Vector2(0, -40), new Vector2(0, 84)),
                UIFactory.Spaced("out of life"), 56, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Gradient(title, UIFactory.Red, UIFactory.Pink, true);
            _continueLine = UIFactory.Label(UIFactory.Rect(_continueCard, "Line", new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), new Vector2(0, -126), new Vector2(-80, 40)),
                string.Empty, 24, UIFactory.TextColor, TextAnchor.MiddleCenter);

            var ring = UIFactory.At(_continueCard, "Ring", new Vector2(0.5f, 0.5f), new Vector2(0, 70), new Vector2(220, 220));
            UIFactory.Image(ring, new Color(1f, 1f, 1f, 0.12f), UIFactory.RingOutline);
            _continueRing = UIFactory.Image(UIFactory.Stretch(ring, "Fill"), UIFactory.Gold, UIFactory.RingOutline);
            _continueRing.type = Image.Type.Filled;
            _continueRing.fillMethod = Image.FillMethod.Radial360;
            _continueRing.fillOrigin = (int)Image.Origin360.Top;
            _continueSecs = UIFactory.Label(ring, "Secs", "6", 96, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);

            var buttons = UIFactory.Rect(_continueCard, "Buttons", new Vector2(0, 0), new Vector2(1, 0), new Vector2(0.5f, 0), new Vector2(0, 40), new Vector2(-120, 210));
            UIFactory.VStack(buttons, 18, 0, TextAnchor.MiddleCenter);
            UIFactory.Layout(UIFactory.Button(buttons, "Accept", UIFactory.Spaced("continue · watch ad"), 28, () => _game.AcceptContinue(), out _, UIFactory.ButtonStyle.Primary), 100);
            UIFactory.Layout(UIFactory.Button(buttons, "Decline", UIFactory.Spaced("no thanks"), 24, () => _game.DeclineContinue(), out _, UIFactory.ButtonStyle.Outline), 80);
            _continueCard.gameObject.SetActive(false);
        }

        /// <summary>Shows (or hides) the continue offer on the pause screen; <paramref name="progress"/> 0..1 of the song reached.</summary>
        public void ShowContinueOffer(bool on, float progress)
        {
            if (_continueCard == null) return;
            if (on)
            {
                Show(Screen.Pause);
                _pauseCard.gameObject.SetActive(false);
                _resumeCount.text = string.Empty;
                _continueLine.text = "You reached " + Mathf.RoundToInt(progress * 100f) + "%. Keep going with half your life?";
                SetContinueTimer(1f, 6);
            }
            _continueCard.gameObject.SetActive(on);
        }

        public void SetContinueTimer(float fill, int seconds)
        {
            if (_continueRing == null) return;
            _continueRing.fillAmount = Mathf.Clamp01(fill);
            _continueRing.color = Color.Lerp(UIFactory.Red, UIFactory.Gold, Mathf.Clamp01(fill * 1.5f));
            _continueSecs.text = Mathf.Max(0, seconds).ToString();
        }
    }
}
