namespace DropBeat
{
    /// <summary>Ball looks (DropBeat/Chrome _Skin modes). Order matches the shader.</summary>
    public enum BallSkin
    {
        Chrome = 0,
        Gold = 1,
        BlackChrome = 2,
        RoseGold = 3,
        Plasma = 4,
        Lava = 5,
        Disco = 6,
        Pixel = 7,
        NeonWire = 8,
        Planet = 9,
        /// <summary>Reward for the first Hard clear: a prismatic diamond with gold seams.</summary>
        Champion = 10
    }

    /// <summary>What the ball leaves behind it.</summary>
    public enum TrailEffect
    {
        Classic = 0,
        Rainbow,
        Comet,
        Sparkle,
        Pixel,
        Electric,
        Ghost,
        /// <summary>Reward for the first Normal clear: a wide, shimmering northern-lights ribbon.</summary>
        Aurora
    }

    public static class BallStyles
    {
        public const int SkinCount = 11;
        public const int TrailCount = 8;

        public static string Name(BallSkin s)
        {
            switch (s)
            {
                case BallSkin.Gold: return "GOLD";
                case BallSkin.BlackChrome: return "BLACK CHROME";
                case BallSkin.RoseGold: return "ROSE GOLD";
                case BallSkin.Plasma: return "PLASMA";
                case BallSkin.Lava: return "LAVA";
                case BallSkin.Disco: return "DISCO";
                case BallSkin.Pixel: return "PIXEL";
                case BallSkin.NeonWire: return "NEON WIRE";
                case BallSkin.Planet: return "PLANET";
                case BallSkin.Champion: return "CHAMPION";
                default: return "CHROME";
            }
        }

        public static string Name(TrailEffect t)
        {
            switch (t)
            {
                case TrailEffect.Rainbow: return "RAINBOW";
                case TrailEffect.Comet: return "COMET";
                case TrailEffect.Sparkle: return "SPARKLE";
                case TrailEffect.Pixel: return "PIXEL";
                case TrailEffect.Electric: return "ELECTRIC";
                case TrailEffect.Ghost: return "GHOST";
                case TrailEffect.Aurora: return "AURORA";
                default: return "CLASSIC";
            }
        }
    }
}
