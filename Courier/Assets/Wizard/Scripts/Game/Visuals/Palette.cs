using Wizard.Sim;
using UnityEngine;

namespace Wizard.Game
{
    /// <summary>All placeholder colours in one place.</summary>
    public static class Palette
    {
        public static readonly Color Background = Hex("#E9E4F0");
        public static readonly Color DoomSky = Hex("#3A2E4F");
        public static readonly Color Ground = Hex("#DCD6CC");
        public static readonly Color Road = Hex("#6A6476");
        public static readonly Color RoadLocked = Hex("#C8C2BC");
        public static readonly Color Lot = Hex("#BFD9A8");
        public static readonly Color LotLocked = Hex("#D6D1C6");

        public static readonly Color Cursor = Hex("#FFFFFF");
        public static readonly Color Selection = Hex("#7C4DFF");
        public static readonly Color Valid = Hex("#5BBF6A");
        public static readonly Color Invalid = Hex("#E5533D");

        public static readonly Color Working = Hex("#5BBF6A");
        public static readonly Color NeedsInput = Hex("#F2B33D");
        public static readonly Color OutputFull = Hex("#E5533D");
        public static readonly Color Idle = Hex("#A3A8B0");

        public static readonly Color Doom = Hex("#6B2FA0");
        public static readonly Color DoomGlow = Hex("#B65CFF");
        public static readonly Color Beam = Hex("#5CE1FF");
        public static readonly Color Job = Hex("#F2B33D");

        // UI
        public static readonly Color Panel = new Color(1f, 1f, 1f, 0.94f);
        public static readonly Color PanelSunken = Hex("#EFECF4");
        public static readonly Color Ink = Hex("#2B2838");
        public static readonly Color InkSoft = Hex("#6E6A7C");
        public static readonly Color Accent = Hex("#6A3FD6");
        public static readonly Color AccentSoft = Hex("#E2D8FB");
        public static readonly Color Button = Hex("#E8E5EE");
        public static readonly Color Warning = Hex("#E8A33A");
        public static readonly Color Error = Hex("#E5533D");
        public static readonly Color Good = Hex("#3FA65A");

        public static Color For(Resource r)
        {
            switch (r)
            {
                case Resource.Gold: return Hex("#E8B931");
                case Resource.Stone: return Hex("#9AA0A8");
                case Resource.Grain: return Hex("#E3CB72");
                case Resource.Bread: return Hex("#C27434");
                case Resource.Herbs: return Hex("#5DA34A");
                case Resource.Potion: return Hex("#E0457B");
                case Resource.Crystal: return Hex("#6FC3E8");
                case Resource.Charge: return Hex("#5CE1FF");
                case Resource.Parcel: return Hex("#A57B52");
                default: return Color.white;
            }
        }

        public static Color For(BuildingType t)
        {
            switch (t.Id)
            {
                case "tower": return Hex("#5B3D9C");
                case "cottage": return Hex("#D9A27A");
                case "farm": return Hex("#D9B23B");
                case "bakery": return Hex("#D0884A");
                case "quarry": return Hex("#7D8590");
                case "herb_garden": return Hex("#4E9A44");
                case "alchemist": return Hex("#C04A7A");
                case "crystal_mine": return Hex("#4F8FB8");
                case "arcane_forge": return Hex("#3A6FD0");
                case "ward": return Hex("#8C7AB8");
                default: return Hex("#A0A0A0");
            }
        }

        public static Color For(BuildingStatus s)
        {
            switch (s)
            {
                case BuildingStatus.Working: return Working;
                case BuildingStatus.NeedsInput: return NeedsInput;
                case BuildingStatus.OutputFull: return OutputFull;
                default: return Idle;
            }
        }

        static readonly Color[] s_Constructs =
        {
            Hex("#E4572E"), Hex("#1FA2A6"), Hex("#F2A541"), Hex("#3B82F6"), Hex("#EC4899"), Hex("#84CC16"), Hex("#A16207"), Hex("#0EA5E9"),
        };

        public static Color Construct(int id) => s_Constructs[(Mathf.Max(1, id) - 1) % s_Constructs.Length];

        static readonly Color[] s_Spells =
        {
            Hex("#7C4DFF"), Hex("#D946EF"), Hex("#0D9488"), Hex("#DB2777"), Hex("#4F46E5"), Hex("#B45309"), Hex("#0891B2"), Hex("#9333EA"),
        };

        public static Color Spell(int id) => s_Spells[(Mathf.Max(1, id) - 1) % s_Spells.Length];

        public static Color For(MonsterType t) => t == Monsters.Brute ? Hex("#2A1838") : Hex("#4B2A6B");

        public static Color For(Command c)
        {
            switch (c.Type)
            {
                case CommandType.MoveForward: return Hex("#3B82F6");
                case CommandType.TurnLeft:
                case CommandType.TurnRight:
                case CommandType.TurnAround: return Hex("#8E5AD6");
                case CommandType.PickUp: return Hex("#2E9E5B");
                case CommandType.DropOff: return Hex("#E08A1E");
                case CommandType.Cast: return Spell(c.SpellId);
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
