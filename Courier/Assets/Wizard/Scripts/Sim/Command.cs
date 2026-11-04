using System;
using System.Collections;
using System.Collections.Generic;

namespace Wizard.Sim
{
    /// <summary>The instruction set. Every command takes one tick, except Cast which is free.</summary>
    public enum CommandType
    {
        MoveForward,
        TurnLeft,
        TurnRight,
        TurnAround,
        PickUp,
        DropOff,
        Wait,

        /// <summary>Run a spell from the spellbook, then carry on with the next command.</summary>
        Cast,
    }

    /// <summary>Which neighbouring building a PickUp / DropOff talks to, relative to the construct's facing.</summary>
    public enum InteractSide
    {
        /// <summary>Try ahead, left, right, then behind; use the first building that can do the job.</summary>
        Auto,
        Ahead,
        Left,
        Right,
    }

    /// <summary>How far a Forward command goes. Each cell moved still takes one tick.</summary>
    public enum ForwardMode
    {
        /// <summary>One cell.</summary>
        Step,

        /// <summary>Keep going until the road ahead ends (or is blocked).</summary>
        TillStop,

        /// <summary>Keep going until reaching a junction (a road branching left or right), or the road ends.</summary>
        TillIntersection,
    }

    public enum CommandResult
    {
        None,
        Ok,
        Idle,

        // Soft results: the route is fine, there just wasn't anything to do.
        NothingToCollect,
        NotAccepted,
        EmptyHanded,

        // Hard results: the routine is probably wrong.
        Blocked,
        NoBuilding,
        CargoFull,
        UnknownSpell,
        SpellTooDeep,
    }

    [Serializable]
    public struct Command : IEquatable<Command>
    {
        public CommandType Type;
        public InteractSide Side;

        /// <summary>For <see cref="CommandType.Cast"/>: the spell's id in the <see cref="Spellbook"/>.</summary>
        public int SpellId;

        /// <summary>For <see cref="CommandType.MoveForward"/>: one cell, till the road stops, or till a junction.</summary>
        public ForwardMode Mode;

        public Command(CommandType type, InteractSide side = InteractSide.Auto, int spellId = 0, ForwardMode mode = ForwardMode.Step)
        {
            Type = type;
            Side = side;
            SpellId = spellId;
            Mode = mode;
        }

        public bool HasSide => Type == CommandType.PickUp || Type == CommandType.DropOff;
        public bool IsCast => Type == CommandType.Cast;

        /// <summary>Commands whose option can be cycled by clicking them (side or forward mode).</summary>
        public bool HasOptions => HasSide || Type == CommandType.MoveForward;

        /// <summary>The same command with its next option: sides for Pick Up/Drop Off, distance for Forward.</summary>
        public Command WithNextOption()
        {
            if (Type == CommandType.MoveForward)
                return new Command(Type, Side, SpellId, (ForwardMode)(((int)Mode + 1) % 3));
            if (HasSide)
                return new Command(Type, (InteractSide)(((int)Side + 1) % 4), SpellId, Mode);
            return this;
        }

        public static Command Move => new Command(CommandType.MoveForward);
        public static Command MoveTillStop => new Command(CommandType.MoveForward, mode: ForwardMode.TillStop);
        public static Command MoveTillIntersection => new Command(CommandType.MoveForward, mode: ForwardMode.TillIntersection);
        public static Command Left => new Command(CommandType.TurnLeft);
        public static Command Right => new Command(CommandType.TurnRight);
        public static Command UTurn => new Command(CommandType.TurnAround);
        public static Command Wait => new Command(CommandType.Wait);
        public static Command PickUp(InteractSide side = InteractSide.Auto) => new Command(CommandType.PickUp, side);
        public static Command DropOff(InteractSide side = InteractSide.Auto) => new Command(CommandType.DropOff, side);
        public static Command Cast(Spell spell) => new Command(CommandType.Cast, InteractSide.Auto, spell.Id);
        public static Command Cast(int spellId) => new Command(CommandType.Cast, InteractSide.Auto, spellId);

        public bool Equals(Command other) =>
            Type == other.Type && Side == other.Side && SpellId == other.SpellId && Mode == other.Mode;
        public override bool Equals(object obj) => obj is Command c && Equals(c);
        public override int GetHashCode() => (((int)Type * 8 + (int)Side) * 4 + (int)Mode) * 397 ^ SpellId;
        public override string ToString() => CommandInfo.Label(this, null);
    }

    /// <summary>Display text for commands and results. Kept in the sim so tests and UI agree.</summary>
    public static class CommandInfo
    {
        public static readonly CommandType[] PaletteOrder =
        {
            CommandType.MoveForward,
            CommandType.Wait,
            CommandType.TurnLeft,
            CommandType.TurnRight,
            CommandType.TurnAround,
            CommandType.PickUp,
            CommandType.DropOff,
        };

        public static string Name(CommandType t)
        {
            switch (t)
            {
                case CommandType.MoveForward: return "Forward";
                case CommandType.TurnLeft: return "Turn Left";
                case CommandType.TurnRight: return "Turn Right";
                case CommandType.TurnAround: return "U-Turn";
                case CommandType.PickUp: return "Pick Up";
                case CommandType.DropOff: return "Drop Off";
                case CommandType.Wait: return "Wait";
                case CommandType.Cast: return "Cast";
                default: return t.ToString();
            }
        }

        public static string Label(Command c, Spellbook book)
        {
            if (c.IsCast)
            {
                var spell = book?.Get(c.SpellId);
                return spell != null ? "Cast: " + spell.Name : "Cast: (missing spell)";
            }
            if (c.Type == CommandType.MoveForward && c.Mode != ForwardMode.Step) return $"{Name(c.Type)} ({ModeName(c.Mode)})";
            return c.HasSide && c.Side != InteractSide.Auto ? $"{Name(c.Type)} ({c.Side})" : Name(c.Type);
        }

        public static string ModeName(ForwardMode m)
        {
            switch (m)
            {
                case ForwardMode.TillStop: return "till stop";
                case ForwardMode.TillIntersection: return "till intersection";
                default: return "1 step";
            }
        }

        public static bool IsProblem(CommandResult r) => r >= CommandResult.NothingToCollect;
        public static bool IsHardProblem(CommandResult r) => r >= CommandResult.Blocked;

        public static string Describe(CommandResult r)
        {
            switch (r)
            {
                case CommandResult.Ok: return "OK";
                case CommandResult.Idle: return "Idle";
                case CommandResult.NothingToCollect: return "Nothing ready to collect";
                case CommandResult.NotAccepted: return "Nobody there wants this";
                case CommandResult.EmptyHanded: return "Nothing to drop off";
                case CommandResult.Blocked: return "Blocked - no road ahead";
                case CommandResult.NoBuilding: return "No building on that side";
                case CommandResult.CargoFull: return "Cargo full";
                case CommandResult.UnknownSpell: return "That spell no longer exists";
                case CommandResult.SpellTooDeep: return "Spells nested too deeply";
                default: return "";
            }
        }
    }

    /// <summary>An ordered list of commands: a construct's daily routine or a spell's body.</summary>
    public sealed class Routine : IReadOnlyList<Command>
    {
        public const int MaxLength = 48;

        readonly List<Command> _commands = new List<Command>();

        public event Action Changed;

        public int Count => _commands.Count;
        public Command this[int index] => _commands[index];
        public bool IsFull => _commands.Count >= MaxLength;

        public bool Insert(int index, Command c)
        {
            if (IsFull) return false;
            index = Math.Max(0, Math.Min(index, _commands.Count));
            _commands.Insert(index, c);
            Changed?.Invoke();
            return true;
        }

        public bool Add(Command c) => Insert(_commands.Count, c);

        public void RemoveAt(int index)
        {
            if (index < 0 || index >= _commands.Count) return;
            _commands.RemoveAt(index);
            Changed?.Invoke();
        }

        public void Set(int index, Command c)
        {
            if (index < 0 || index >= _commands.Count) return;
            _commands[index] = c;
            Changed?.Invoke();
        }

        /// <summary>
        /// Moves an item given a drop "insertion point" measured in the list *before* removal
        /// (0 = above the first row, Count = below the last row). This is what drag-and-drop produces.
        /// </summary>
        public void MoveToInsertionPoint(int from, int insertionPoint)
        {
            if (from < 0 || from >= _commands.Count) return;
            insertionPoint = Math.Max(0, Math.Min(insertionPoint, _commands.Count));
            int to = insertionPoint > from ? insertionPoint - 1 : insertionPoint;
            if (to == from) return;
            var c = _commands[from];
            _commands.RemoveAt(from);
            _commands.Insert(to, c);
            Changed?.Invoke();
        }

        /// <summary>Removes every command matching <paramref name="match"/>. Returns how many went.</summary>
        public int RemoveAll(Predicate<Command> match)
        {
            int n = _commands.RemoveAll(match);
            if (n > 0) Changed?.Invoke();
            return n;
        }

        public void Clear()
        {
            if (_commands.Count == 0) return;
            _commands.Clear();
            Changed?.Invoke();
        }

        public void SetAll(IEnumerable<Command> commands)
        {
            _commands.Clear();
            foreach (var c in commands)
            {
                if (_commands.Count >= MaxLength) break;
                _commands.Add(c);
            }
            Changed?.Invoke();
        }

        public bool Casts(int spellId)
        {
            foreach (var c in _commands)
                if (c.IsCast && c.SpellId == spellId) return true;
            return false;
        }

        public IEnumerator<Command> GetEnumerator() => _commands.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
