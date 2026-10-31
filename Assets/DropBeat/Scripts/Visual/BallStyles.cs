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
        Planet = 9
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
        Ghost
    }

    public static class BallStyles
    {
        public const int SkinCount = 10;
        public const int TrailCount = 7;

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
                default: return "CLASSIC";
            }
        }
    }
}
