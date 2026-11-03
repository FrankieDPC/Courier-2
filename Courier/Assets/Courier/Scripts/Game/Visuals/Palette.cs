using Courier.Sim;
using UnityEngine;

namespace Courier.Game
{
    /// <summary>All placeholder colours in one place.</summary>
    public static class Palette
    {
        public static readonly Color Background = Hex("#EFEBE1");
        public static readonly Color Ground = Hex("#E2DDCF");
        public static readonly Color Road = Hex("#5E6573");
        public static readonly Color RoadLocked = Hex("#CDC8BB");
        public static readonly Color Lot = Hex("#C9DEAE");
        public static readonly Color LotLocked = Hex("#DBD6C8");

        public static readonly Color Cursor = Hex("#FFFFFF");
        public static readonly Color Selection = Hex("#2F6FD6");
        public static readonly Color Valid = Hex("#5BBF6A");
        public static readonly Color Invalid = Hex("#E5533D");

        public static readonly Color Working = Hex("#5BBF6A");
        public static readonly Color NeedsInput = Hex("#F2B33D");
        public static readonly Color OutputFull = Hex("#E5533D");
        public static readonly Color Idle = Hex("#A3A8B0");

        // UI
        public static readonly Color Panel = new Color(1f, 1f, 1f, 0.94f);
        public static readonly Color PanelSunken = Hex("#EEF0F3");
        public static readonly Color Ink = Hex("#2B2F38");
        public static readonly Color InkSoft = Hex("#6B7280");
        public static readonly Color Accent = Hex("#2F6FD6");
        public static readonly Color Button = Hex("#E7EAEE");
        public static readonly Color Warning = Hex("#E8A33A");
        public static readonly Color Error = Hex("#E5533D");

        public static Color For(Resource r)
        {
            switch (r)
            {
                case Resource.Logs: return Hex("#8B5A2B");
                case Resource.Planks: return Hex("#E0A960");
                case Resource.Grain: return Hex("#E9C84A");
                case Resource.Bread: return Hex("#C27434");
                case Resource.Stone: return Hex("#9AA0A8");
                default: return Color.white;
            }
        }

        public static Color For(BuildingType t)
        {
            switch (t.Id)
            {
                case "town_hall": return Hex("#3E6DB5");
                case "woodcutter": return Hex("#4E8A3E");
                case "sawmill": return Hex("#B8743F");
                case "farm": return Hex("#D9B23B");
                case "bakery": return Hex("#D0884A");
                case "quarry": return Hex("#7D8590");
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

        static readonly Color[] s_Bots =
        {
            Hex("#E4572E"), Hex("#1FA2A6"), Hex("#8E5AD6"), Hex("#F2A541"), Hex("#3B82F6"), Hex("#EC4899"),
        };

        public static Color Bot(int id) => s_Bots[(Mathf.Max(1, id) - 1) % s_Bots.Length];

        public static Color For(CommandType t)
        {
            switch (t)
            {
                case CommandType.MoveForward: return Hex("#3B82F6");
                case CommandType.TurnLeft:
                case CommandType.TurnRight:
                case CommandType.TurnAround: return Hex("#8E5AD6");
                case CommandType.PickUp: return Hex("#2E9E5B");
                case CommandType.DropOff: return Hex("#E08A1E");
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
