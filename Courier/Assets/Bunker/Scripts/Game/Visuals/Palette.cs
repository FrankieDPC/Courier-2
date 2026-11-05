using Bunker.Sim;
using UnityEngine;

namespace Bunker.Game
{
    /// <summary>All placeholder colours in one place: mossy, rusty, a thousand years after.</summary>
    public static class Palette
    {
        public static readonly Color Background = Hex("#1E2A21");

        // Ground
        public static readonly Color Moss = Hex("#5E7A3A");
        public static readonly Color MossAlt = Hex("#587336");
        public static readonly Color Soil = Hex("#7C5B3A");
        public static readonly Color SoilAlt = Hex("#755536");
        public static readonly Color Concrete = Hex("#9C988E");
        public static readonly Color ConcreteAlt = Hex("#928E84");
        public static readonly Color ConcreteDark = Hex("#6F6C65");
        public static readonly Color Water = Hex("#3E86B4");
        public static readonly Color WaterDeep = Hex("#2F6E99");
        public static readonly Color Tilled = Hex("#5A3A22");
        public static readonly Color TilledDamp = Hex("#4A2F1C");
        public static readonly Color TilledWet = Hex("#3A2416");

        // Overgrowth
        public static readonly Color Weed = Hex("#86A83F");
        public static readonly Color WeedDark = Hex("#6B8E35");
        public static readonly Color Flower = Hex("#E2C84B");
        public static readonly Color Thicket = Hex("#3F6B2E");
        public static readonly Color ThicketDark = Hex("#33592A");
        public static readonly Color Trunk = Hex("#5C4029");
        public static readonly Color Leaf = Hex("#3E7A34");
        public static readonly Color LeafDark = Hex("#2E5E2A");
        public static readonly Color Sprout = Hex("#6CC24A");

        // Ancient tech
        public static readonly Color Rust = Hex("#9A4F2A");
        public static readonly Color RustDark = Hex("#6E3820");
        public static readonly Color Metal = Hex("#7D8A8C");
        public static readonly Color MetalDark = Hex("#4E5759");
        public static readonly Color Screen = Hex("#58E0C8");
        public static readonly Color Glass = Hex("#2A3B40");
        public static readonly Color Hazard = Hex("#E8B931");
        public static readonly Color HazardDark = Hex("#2B2B2B");

        public static readonly Color Cursor = Hex("#FFFFFF");
        public static readonly Color Selection = Hex("#FFD54A");
        public static readonly Color Ahead = Hex("#FFE9A8");
        public static readonly Color Pick = Hex("#4FC3F7");
        public static readonly Color Valid = Hex("#5BBF6A");
        public static readonly Color Invalid = Hex("#E5533D");

        // UI
        public static readonly Color Panel = new Color(0.96f, 0.95f, 0.9f, 0.95f);
        public static readonly Color PanelSunken = Hex("#E6E2D6");
        public static readonly Color Ink = Hex("#2A2A24");
        public static readonly Color InkSoft = Hex("#6D6A5E");
        public static readonly Color Accent = Hex("#4F7A2E");
        public static readonly Color AccentSoft = Hex("#DCE8CC");
        public static readonly Color Button = Hex("#E4DFD0");
        public static readonly Color Warning = Hex("#E8A33A");
        public static readonly Color Error = Hex("#E5533D");
        public static readonly Color Good = Hex("#3FA65A");
        public static readonly Color Food = Hex("#D9446A");

        /// <summary>Your robot: warm yellow, so it stands out from the bots.</summary>
        public static readonly Color Player = Hex("#F2C14E");

        public static Color For(Item i)
        {
            switch (i)
            {
                case Item.Water: return Hex("#4FA3E0");
                case Item.RadishSeeds: return Hex("#E8A6B5");
                case Item.PotatoSeeds: return Hex("#C7A27A");
                case Item.SquashSeeds: return Hex("#F0C27A");
                case Item.Radish: return Hex("#D9446A");
                case Item.Potato: return Hex("#C1975B");
                case Item.Squash: return Hex("#E8892E");
                case Item.Fibre: return Hex("#A8C46A");
                case Item.Wood: return Hex("#8A5A33");
                case Item.Scrap: return Hex("#8E8A80");
                case Item.Circuit: return Hex("#3FC7A7");
                default: return Color.white;
            }
        }

        public static Color For(CropType c) => c == null ? Color.white : For(c.Produce);

        static readonly Color[] s_Bots =
        {
            Hex("#1FA2A6"), Hex("#E4572E"), Hex("#8E5AD6"), Hex("#3B82F6"), Hex("#EC4899"), Hex("#84CC16"), Hex("#0EA5E9"), Hex("#F2A541"),
        };

        public static Color Bot(Bot b) => b.IsPlayer ? Player : Bot(b.Id);

        public static Color Bot(int id) => s_Bots[(Mathf.Max(2, id) - 2) % s_Bots.Length]; // id 1 is you

        static readonly Color[] s_Macros =
        {
            Hex("#7C4DFF"), Hex("#D946EF"), Hex("#0D9488"), Hex("#DB2777"), Hex("#4F46E5"), Hex("#B45309"), Hex("#0891B2"), Hex("#9333EA"),
        };

        public static Color Macro(int id) => s_Macros[(Mathf.Max(1, id) - 1) % s_Macros.Length];

        public static Color For(Command c)
        {
            switch (c.Type)
            {
                case CommandType.GoTo:
                case CommandType.Step:
                case CommandType.Face: return Hex("#3B82F6");
                case CommandType.Clear: return Hex("#6E8F2E");
                case CommandType.Till: return Hex("#8A5A33");
                case CommandType.Plant: return Hex("#4E9A3E");
                case CommandType.Water: return Hex("#2F7FC1");
                case CommandType.Harvest: return Hex("#D08A2E");
                case CommandType.Grab: return Hex("#0EA5E9");
                case CommandType.Deliver: return Hex("#B5432F");
                case CommandType.Dump: return Hex("#5A7FA8");
                case CommandType.Run: return Macro(c.MacroId);
                default: return Hex("#8A8F98");
            }
        }

        public static Color Shade(Color c, float k) => new Color(c.r * k, c.g * k, c.b * k, c.a);

        public static Color Hex(string hex)
        {
            ColorUtility.TryParseHtmlString(hex, out var c);
            return c;
        }
    }
}
