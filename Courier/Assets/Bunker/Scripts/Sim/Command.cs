using System;
using System.Collections;
using System.Collections.Generic;

namespace Bunker.Sim
{
    /// <summary>
    /// The bot instruction set. Jobs act on the tile in front of the bot. Each command takes at least
    /// one tick; some take longer (see <see cref="Balance"/>; Go to takes one tick per tile walked,
    /// Clear depends on what's being cleared). Run is free.
    /// </summary>
    public enum CommandType
    {
        GoTo,
        Step,

        /// <summary>Turn on the spot.</summary>
        Face,

        /// <summary>Cut overgrowth, break rubble or strip old tech. What comes off goes in the load.</summary>
        Clear,
        Till,
        Plant,
        Water,
        Harvest,

        /// <summary>Fill up with water (facing the stream) or take seeds (facing the hatch).</summary>
        Grab,

        /// <summary>Hand food and materials in at the hatch.</summary>
        Deliver,

        /// <summary>Pour all the water out of the load, anywhere, to make room.</summary>
        Dump,
        Wait,

        /// <summary>Run a custom command (from the command library), then carry on.</summary>
        Run,
    }

    /// <summary>Where a Go to command heads. Stream and Hatch mean "the nearest tile beside it, facing it".</summary>
    public enum GoTarget
    {
        Stream,
        Hatch,
        Tile,
    }

    public enum CommandResult
    {
        None,
        Ok,
        Idle,

        /// <summary>Partway through a job that takes several ticks.</summary>
        Working,

        /// <summary>Walking: partway across a tile (bots take several ticks per tile).</summary>
        Moving,

        // Soft results: nothing wrong with the routine, there just wasn't anything to do.
        AlreadyDone,
        NotRipe,
        NothingHere,
        NothingToClear,
        NothingToDeliver,
        NothingToDump,
        OutOfStock,

        // Hard results: the routine is probably wrong.
        Blocked,
        NoPath,
        NotAtStream,
        NotAtHatch,
        NotSoil,
        Overgrown,
        NotTilled,
        NoSeeds,
        NoWater,
        LoadFull,
        UnknownCommand,
        TooDeep,

        /// <summary>Not a job this kind of bot can do (e.g. a water bot told to till).</summary>
        CantDo,
    }

    [Serializable]
    public struct Command : IEquatable<Command>
    {
        public CommandType Type;

        /// <summary>Go to: where.</summary>
        public GoTarget Target;

        /// <summary>Go to (Tile): the tile picked on the map.</summary>
        public Cell Tile;

        /// <summary>Step / Face: which way.</summary>
        public Facing Dir;

        /// <summary>Grab: what. Plant: which seed (None = any seed carried).</summary>
        public Item Item;

        /// <summary>Grab: how many.</summary>
        public int Count;

        /// <summary>Run: the custom command's id in the <see cref="CommandLibrary"/>.</summary>
        public int MacroId;

        public bool IsRun => Type == CommandType.Run;

        /// <summary>Commands with an option you cycle by clicking the block.</summary>
        public bool HasOptions =>
            Type == CommandType.GoTo || Type == CommandType.Step || Type == CommandType.Face ||
            Type == CommandType.Grab || Type == CommandType.Plant;

        public bool HasCount => Type == CommandType.Grab;
        public bool NeedsTile => Type == CommandType.GoTo && Target == GoTarget.Tile;

        static readonly Item[] s_GrabItems = { Item.Water, Item.RadishSeeds, Item.PotatoSeeds, Item.SquashSeeds };
        static readonly Item[] s_PlantItems = { Item.None, Item.RadishSeeds, Item.PotatoSeeds, Item.SquashSeeds };

        /// <summary>The same command with its next option (target, direction, item or seed).</summary>
        public Command WithNextOption()
        {
            var c = this;
            switch (Type)
            {
                case CommandType.GoTo: c.Target = (GoTarget)(((int)Target + 1) % 3); break;
                case CommandType.Step:
                case CommandType.Face: c.Dir = Dir.TurnRight(); break;
                case CommandType.Grab: c.Item = Next(s_GrabItems, Item); break;
                case CommandType.Plant: c.Item = Next(s_PlantItems, Item); break;
            }
            return c;
        }

        public Command WithCount(int count)
        {
            var c = this;
            c.Count = Math.Max(1, Math.Min(Balance.MaxGrab, count));
            return c;
        }

        public Command WithTile(Cell tile)
        {
            var c = this;
            c.Target = GoTarget.Tile;
            c.Tile = tile;
            return c;
        }

        static Item Next(Item[] cycle, Item current)
        {
            int i = Array.IndexOf(cycle, current);
            return cycle[(i + 1) % cycle.Length];
        }

        // Factories
        public static Command GoTo(GoTarget target) => new Command { Type = CommandType.GoTo, Target = target };
        public static Command GoToTile(int x, int y) => new Command { Type = CommandType.GoTo, Target = GoTarget.Tile, Tile = new Cell(x, y) };
        public static Command Step(Facing dir) => new Command { Type = CommandType.Step, Dir = dir };
        public static Command Face(Facing dir) => new Command { Type = CommandType.Face, Dir = dir };
        public static Command Clear => new Command { Type = CommandType.Clear };
        public static Command Till => new Command { Type = CommandType.Till };
        public static Command Plant(Item seed = Item.None) => new Command { Type = CommandType.Plant, Item = seed };
        public static Command Water => new Command { Type = CommandType.Water };
        public static Command Harvest => new Command { Type = CommandType.Harvest };
        public static Command Grab(Item item, int count) => new Command { Type = CommandType.Grab, Item = item, Count = count };
        public static Command Deliver => new Command { Type = CommandType.Deliver };
        public static Command Dump => new Command { Type = CommandType.Dump };
        public static Command Wait => new Command { Type = CommandType.Wait };
        public static Command Run(Macro macro) => new Command { Type = CommandType.Run, MacroId = macro.Id };
        public static Command Run(int macroId) => new Command { Type = CommandType.Run, MacroId = macroId };

        /// <summary>A fresh block as it comes out of the palette.</summary>
        public static Command Default(CommandType t)
        {
            switch (t)
            {
                case CommandType.GoTo: return GoTo(GoTarget.Stream);
                case CommandType.Step: return Step(Facing.East);
                case CommandType.Face: return Face(Facing.East);
                case CommandType.Grab: return Grab(Item.Water, 5);
                default: return new Command { Type = t };
            }
        }

        public bool Equals(Command o) =>
            Type == o.Type && Target == o.Target && Tile == o.Tile && Dir == o.Dir && Item == o.Item && Count == o.Count && MacroId == o.MacroId;

        public override bool Equals(object obj) => obj is Command c && Equals(c);

        public override int GetHashCode()
        {
            unchecked
            {
                int h = (int)Type;
                h = h * 31 + (int)Target;
                h = h * 31 + Tile.GetHashCode();
                h = h * 31 + (int)Dir;
                h = h * 31 + (int)Item;
                h = h * 31 + Count;
                h = h * 31 + MacroId;
                return h;
            }
        }

        public override string ToString() => CommandInfo.Label(this, null);
    }

    /// <summary>Display text and timings for commands and results. Shared by tests and UI.</summary>
    public static class CommandInfo
    {
        public static readonly CommandType[] PaletteOrder =
        {
            CommandType.GoTo, CommandType.Step,
            CommandType.Face, CommandType.Clear,
            CommandType.Till, CommandType.Plant,
            CommandType.Water, CommandType.Harvest,
            CommandType.Grab, CommandType.Deliver,
            CommandType.Dump, CommandType.Wait,
        };

        /// <summary>Ticks a job takes. Go to is one tick per tile walked; Clear depends on the debris (see <see cref="Balance.ClearTicks"/>).</summary>
        public static int Duration(CommandType t)
        {
            switch (t)
            {
                case CommandType.Till: return Balance.TillTicks;
                case CommandType.Plant: return Balance.PlantTicks;
                case CommandType.Water: return Balance.WaterTicks;
                case CommandType.Harvest: return Balance.HarvestTicks;
                case CommandType.Grab: return Balance.GrabTicks;
                case CommandType.Deliver: return Balance.DeliverTicks;
                case CommandType.Dump: return Balance.DumpTicks;
                case CommandType.Run: return 0;
                default: return 1;
            }
        }

        public static string Name(CommandType t)
        {
            switch (t)
            {
                case CommandType.GoTo: return "Go to";
                case CommandType.Step: return "Step";
                case CommandType.Face: return "Face";
                case CommandType.Clear: return "Clear";
                case CommandType.Till: return "Till soil";
                case CommandType.Plant: return "Plant";
                case CommandType.Water: return "Water";
                case CommandType.Harvest: return "Harvest";
                case CommandType.Grab: return "Grab";
                case CommandType.Deliver: return "Deliver";
                case CommandType.Dump: return "Dump water";
                case CommandType.Wait: return "Wait";
                case CommandType.Run: return "Run";
                default: return t.ToString();
            }
        }

        public static string TargetName(GoTarget t, Cell tile)
        {
            switch (t)
            {
                case GoTarget.Stream: return "stream";
                case GoTarget.Hatch: return "hatch";
                default: return $"tile {tile.X},{tile.Y}";
            }
        }

        /// <summary>The option part of a command, e.g. "stream", "east", "5 water", "any seed".</summary>
        public static string Option(Command c)
        {
            switch (c.Type)
            {
                case CommandType.GoTo: return TargetName(c.Target, c.Tile);
                case CommandType.Step:
                case CommandType.Face: return c.Dir.ToString().ToLowerInvariant();
                case CommandType.Grab: return $"{c.Count} {ItemInfo.Name(c.Item, c.Count)}";
                case CommandType.Plant: return c.Item == Item.None ? "any seed" : (Crops.ForSeed(c.Item)?.Name.ToLowerInvariant() ?? "?");
                default: return null;
            }
        }

        public static string Label(Command c, CommandLibrary library)
        {
            if (c.IsRun)
            {
                var m = library?.Get(c.MacroId);
                return m != null ? "Run: " + m.Name : "Run: (missing command)";
            }
            var opt = Option(c);
            return opt == null ? Name(c.Type) : $"{Name(c.Type)} {opt}";
        }

        public static bool IsProblem(CommandResult r) => r >= CommandResult.AlreadyDone;
        public static bool IsHardProblem(CommandResult r) => r >= CommandResult.Blocked;

        public static string Describe(CommandResult r)
        {
            switch (r)
            {
                case CommandResult.Ok: return "OK";
                case CommandResult.Idle: return "Idle";
                case CommandResult.Working: return "Working...";
                case CommandResult.Moving: return "Walking...";
                case CommandResult.AlreadyDone: return "Already done";
                case CommandResult.NotRipe: return "Not ripe yet";
                case CommandResult.NothingHere: return "Nothing growing there";
                case CommandResult.NothingToClear: return "Nothing to clear there";
                case CommandResult.NothingToDeliver: return "Nothing to deliver";
                case CommandResult.NothingToDump: return "No water to dump";
                case CommandResult.OutOfStock: return "The bunker is out of those seeds";
                case CommandResult.Blocked: return "Blocked";
                case CommandResult.NoPath: return "Can't get there";
                case CommandResult.NotAtStream: return "Not facing the stream";
                case CommandResult.NotAtHatch: return "Not facing the hatch";
                case CommandResult.NotSoil: return "Can't farm there";
                case CommandResult.Overgrown: return "Overgrown - clear it first";
                case CommandResult.NotTilled: return "Soil isn't tilled";
                case CommandResult.NoSeeds: return "Not carrying seeds";
                case CommandResult.NoWater: return "Out of water";
                case CommandResult.LoadFull: return "Load too heavy";
                case CommandResult.UnknownCommand: return "That custom command no longer exists";
                case CommandResult.TooDeep: return "Custom commands nested too deeply";
                case CommandResult.CantDo: return "This bot can't do that job";
                default: return "";
            }
        }
    }

    /// <summary>An ordered list of commands: a bot's daily routine or a custom command's body.</summary>
    public sealed class Routine : IReadOnlyList<Command>
    {
        /// <summary>Longest routine or custom command. See <see cref="Balance.MaxRoutineLength"/>.</summary>
        public const int MaxLength = Balance.MaxRoutineLength;

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
            if (index < 0 || index >= _commands.Count || _commands[index].Equals(c)) return;
            _commands[index] = c;
            Changed?.Invoke();
        }

        /// <summary>Move using a drag-and-drop "insertion point" measured before removal (0..Count).</summary>
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
            var copy = new List<Command>(commands);
            _commands.Clear();
            foreach (var c in copy)
            {
                if (_commands.Count >= MaxLength) break;
                _commands.Add(c);
            }
            Changed?.Invoke();
        }

        public bool Runs(int macroId)
        {
            foreach (var c in _commands)
                if (c.IsRun && c.MacroId == macroId) return true;
            return false;
        }

        public IEnumerator<Command> GetEnumerator() => _commands.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
