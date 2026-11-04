using Farm.Sim;
using UnityEngine;

namespace Farm.Game
{
    /// <summary>All placeholder colours in one place (warm Stardew-ish tones).</summary>
    public static class Palette
    {
        public static readonly Color Background = Hex("#2F4A2A");
        public static readonly Color Grass = Hex("#7DB65A");
        public static readonly Color GrassAlt = Hex("#75AE53");
        public static readonly Color Dirt = Hex("#D9A55B");
        public static readonly Color Path = Hex("#C9A271");
        public static readonly Color Water = Hex("#4A9AD4");
        public static readonly Color Tilled = Hex("#9A6236");
        public static readonly Color TilledWet = Hex("#5E3A22");

        public static readonly Color Wood = Hex("#8A5A33");
        public static readonly Color WoodLight = Hex("#B07A45");
        public static readonly Color Roof = Hex("#B5432F");
        public static readonly Color Wall = Hex("#C98B4F");
        public static readonly Color Stone = Hex("#8F8C86");
        public static readonly Color Leaf = Hex("#3E8A3A");
        public static readonly Color LeafDark = Hex("#2E6E2E");
        public static readonly Color Sprout = Hex("#5FBF4A");

        public static readonly Color Cursor = Hex("#FFFFFF");
        public static readonly Color Selection = Hex("#FFD54A");
        public static readonly Color Pick = Hex("#4FC3F7");
        public static readonly Color Valid = Hex("#5BBF6A");
        public static readonly Color Invalid = Hex("#E5533D");

        // UI
        public static readonly Color Panel = new Color(1f, 0.98f, 0.94f, 0.95f);
        public static readonly Color PanelSunken = Hex("#F1E8D8");
        public static readonly Color Ink = Hex("#3B2A1E");
        public static readonly Color InkSoft = Hex("#7A6655");
        public static readonly Color Accent = Hex("#3E8A3A");
        public static readonly Color AccentSoft = Hex("#DCEFD3");
        public static readonly Color Button = Hex("#EFE4D2");
        public static readonly Color Warning = Hex("#E8A33A");
        public static readonly Color Error = Hex("#E5533D");
        public static readonly Color Good = Hex("#3FA65A");
        public static readonly Color Gold = Hex("#E8B931");

        public static Color For(Item i)
        {
            switch (i)
            {
                case Item.Water: return Hex("#4FA3E0");
                case Item.ParsnipSeeds: return Hex("#D8C79A");
                case Item.PotatoSeeds: return Hex("#B58B5A");
                case Item.CauliflowerSeeds: return Hex("#C7D9B5");
                case Item.Parsnip: return Hex("#F1E3B5");
                case Item.Potato: return Hex("#C1975B");
                case Item.Cauliflower: return Hex("#F4F2E8");
                default: return Color.white;
            }
        }

        public static Color For(CropType c) => c == null ? Color.white : For(c.Produce);

        static readonly Color[] s_Bots =
        {
            Hex("#E4572E"), Hex("#1FA2A6"), Hex("#8E5AD6"), Hex("#3B82F6"), Hex("#EC4899"), Hex("#F2A541"), Hex("#84CC16"), Hex("#0EA5E9"),
        };

        public static Color Bot(int id) => s_Bots[(Mathf.Max(1, id) - 1) % s_Bots.Length];

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
                case CommandType.Step: return Hex("#3B82F6");
                case CommandType.Grab: return Hex("#0EA5E9");
                case CommandType.Till: return Hex("#9A6236");
                case CommandType.Plant: return Hex("#5DA34A");
                case CommandType.Water: return Hex("#2F7FC1");
                case CommandType.Harvest: return Hex("#D08A2E");
                case CommandType.Ship: return Hex("#B5432F");
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
