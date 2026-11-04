using System.Collections.Generic;

namespace Farm.Sim
{
    /// <summary>Where in which routine a bot is: MacroId -1 means its own daily routine.</summary>
    public readonly struct ExecPoint
    {
        public readonly int MacroId;
        public readonly int Index;

        public ExecPoint(int macroId, int index)
        {
            MacroId = macroId;
            Index = index;
        }

        public bool IsMain => MacroId < 0;
    }

    /// <summary>
    /// A farm bot. Runs its routine one tick at a time; Run steps into custom commands. Carries a load
    /// limited by weight, and tasks like tilling take several ticks.
    /// </summary>
    public sealed class Bot
    {
        sealed class Frame
        {
            public Routine Routine;
            public int MacroId;
            public int Pc;
        }

        readonly List<Frame> _stack = new List<Frame>();
        readonly List<ExecPoint> _lastPath = new List<ExecPoint>();

        public int Id { get; }
        public string Name { get; set; }

        public Cell Home { get; internal set; }
        public Facing HomeFacing { get; internal set; }
        public Cell Position { get; internal set; }
        public Facing Facing { get; internal set; }
        public Cell PreviousPosition { get; internal set; }
        public Facing PreviousFacing { get; internal set; }

        /// <summary>What the bot is carrying. Total weight can't exceed <see cref="Capacity"/>.</summary>
        public Inventory Load { get; } = new Inventory();

        /// <summary>Maximum carry weight.</summary>
        public int Capacity { get; set; }

        /// <summary>Ticks left on the task in progress (0 = free to start the next command).</summary>
        public int WorkLeft { get; internal set; }

        /// <summary>The bot's daily routine.</summary>
        public Routine Routine { get; } = new Routine();

        /// <summary>Repeat the routine until the day ends. If false (the default) it idles after the last command.</summary>
        public bool Loop { get; set; }

        public CommandResult LastResult { get; internal set; }

        /// <summary>
        /// The call path of the command run on the last tick, outermost first: [0] is the line in the
        /// bot's own routine, later entries are lines inside the custom commands it was running.
        /// Empty if it was idle.
        /// </summary>
        public IReadOnlyList<ExecPoint> LastPath => _lastPath;

        public int ShippedToday { get; internal set; }
        public int ProblemsToday { get; internal set; }

        public bool IsCarrying => !Load.IsEmpty;
        public int FreeWeight => Capacity - Load.Weight;

        internal Bot(int id, string name, Cell home, Facing homeFacing, int capacity)
        {
            Id = id;
            Name = name;
            Home = home;
            HomeFacing = homeFacing;
            Capacity = capacity;
            ReturnHome();
        }

        /// <summary>Index into <paramref name="macroId"/>'s routine on the last tick (deepest match), or -1.</summary>
        public int LastIndexIn(int macroId)
        {
            for (int i = _lastPath.Count - 1; i >= 0; i--)
                if (_lastPath[i].MacroId == macroId) return _lastPath[i].Index;
            return -1;
        }

        internal void ReturnHome()
        {
            Position = PreviousPosition = Home;
            Facing = PreviousFacing = HomeFacing;
            ResetProgram();
            LastResult = CommandResult.None;
        }

        internal void ResetProgram()
        {
            _stack.Clear();
            _stack.Add(new Frame { Routine = Routine, MacroId = -1, Pc = 0 });
            _lastPath.Clear();
            WorkLeft = 0;
        }

        /// <summary>Copies everything, including the call stack, mapping custom-command frames onto <paramref name="library"/>.</summary>
        internal void CopyStateFrom(Bot o, CommandLibrary library)
        {
            Name = o.Name;
            Home = o.Home;
            HomeFacing = o.HomeFacing;
            Position = o.Position;
            Facing = o.Facing;
            PreviousPosition = o.PreviousPosition;
            PreviousFacing = o.PreviousFacing;
            Load.CopyFrom(o.Load);
            Capacity = o.Capacity;
            Loop = o.Loop;
            LastResult = o.LastResult;
            ShippedToday = o.ShippedToday;
            ProblemsToday = o.ProblemsToday;
            Routine.SetAll(o.Routine);

            _stack.Clear();
            foreach (var f in o._stack)
            {
                Routine r;
                if (f.MacroId < 0) r = Routine;
                else
                {
                    var macro = library.Get(f.MacroId);
                    if (macro != null) r = macro.Routine;
                    else
                    {
                        r = new Routine(); // a deleted custom command still running: keep a private copy
                        r.SetAll(f.Routine);
                    }
                }
                _stack.Add(new Frame { Routine = r, MacroId = f.MacroId, Pc = f.Pc });
            }
            if (_stack.Count == 0) ResetProgram();
            WorkLeft = o.WorkLeft;

            _lastPath.Clear();
            _lastPath.AddRange(o._lastPath);
        }

        internal void ResetDailyStats()
        {
            ShippedToday = 0;
            ProblemsToday = 0;
        }

        /// <summary>
        /// Walks the call stack to the next real command, stepping into Runs (free) and out of finished
        /// custom commands. Returns false if there is nothing to do this tick.
        /// </summary>
        internal bool TryFetchNext(CommandLibrary library, out Command command, out CommandResult error)
        {
            command = default;
            error = CommandResult.None;
            _lastPath.Clear();

            for (int guard = 0; guard < 256; guard++)
            {
                var top = _stack[_stack.Count - 1];
                if (top.Pc >= top.Routine.Count)
                {
                    if (_stack.Count > 1)
                    {
                        _stack.RemoveAt(_stack.Count - 1); // custom command finished: back to the caller
                        continue;
                    }
                    if (!Loop || top.Routine.Count == 0) return false;
                    top.Pc = 0;
                    continue;
                }

                var cmd = top.Routine[top.Pc];
                top.Pc++;

                if (cmd.IsRun)
                {
                    var macro = library.Get(cmd.MacroId);
                    if (macro == null || _stack.Count > CommandLibrary.MaxDepth)
                    {
                        RecordPath();
                        error = macro == null ? CommandResult.UnknownCommand : CommandResult.TooDeep;
                        if (error == CommandResult.TooDeep) ResetProgram();
                        return false;
                    }
                    _stack.Add(new Frame { Routine = macro.Routine, MacroId = macro.Id, Pc = 0 });
                    continue;
                }

                RecordPath();
                command = cmd;
                return true;
            }

            // Only runs of empty custom commands: effectively idle.
            return false;
        }

        /// <summary>Run the command just fetched again next tick (multi-tick tasks and Go to).</summary>
        internal void RepeatCurrent()
        {
            var top = _stack[_stack.Count - 1];
            if (top.Pc > 0) top.Pc--;
        }

        void RecordPath()
        {
            _lastPath.Clear();
            foreach (var f in _stack) _lastPath.Add(new ExecPoint(f.MacroId, f.Pc - 1));
        }

        public override string ToString() => $"{Name} at {Position} facing {Facing}, carrying {Load}";
    }
}
