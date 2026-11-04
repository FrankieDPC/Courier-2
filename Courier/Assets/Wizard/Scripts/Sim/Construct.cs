using System.Collections.Generic;

namespace Wizard.Sim
{
    /// <summary>Where in which routine a construct is: SpellId -1 means its own daily routine.</summary>
    public readonly struct ExecPoint
    {
        public readonly int SpellId;
        public readonly int Index;

        public ExecPoint(int spellId, int index)
        {
            SpellId = spellId;
            Index = index;
        }

        public bool IsMain => SpellId < 0;
    }

    /// <summary>A magic construct. Runs one command of its routine per tick; Casts step into spells.</summary>
    public sealed class Construct
    {
        sealed class Frame
        {
            public Routine Routine;
            public int SpellId;
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

        public Resource Cargo { get; internal set; }
        public int CargoCount { get; internal set; }
        public int Capacity { get; set; }

        /// <summary>The construct's daily routine.</summary>
        public Routine Routine { get; } = new Routine();

        /// <summary>Repeat the routine until the day ends. If false it idles after the last command.</summary>
        public bool Loop { get; set; } = true;

        public CommandResult LastResult { get; internal set; }

        /// <summary>
        /// The call path of the command run on the last tick, outermost first: [0] is the line in the
        /// construct's own routine, later entries are lines inside the spells it was casting.
        /// Empty if it was idle.
        /// </summary>
        public IReadOnlyList<ExecPoint> LastPath => _lastPath;

        public int DeliveredToday { get; internal set; }
        public int ProblemsToday { get; internal set; }

        public bool IsCarrying => CargoCount > 0;
        public int FreeCapacity => Capacity - CargoCount;

        internal Construct(int id, string name, Cell home, Facing homeFacing, int capacity)
        {
            Id = id;
            Name = name;
            Home = home;
            HomeFacing = homeFacing;
            Capacity = capacity;
            ReturnHome();
        }

        /// <summary>Index into <paramref name="spellId"/>'s routine on the last tick (deepest match), or -1.</summary>
        public int LastIndexIn(int spellId)
        {
            for (int i = _lastPath.Count - 1; i >= 0; i--)
                if (_lastPath[i].SpellId == spellId) return _lastPath[i].Index;
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
            _stack.Add(new Frame { Routine = Routine, SpellId = -1, Pc = 0 });
            _lastPath.Clear();
        }

        /// <summary>Copies everything, including the call stack, mapping spell frames onto <paramref name="book"/>.</summary>
        internal void CopyStateFrom(Construct o, Spellbook book)
        {
            Name = o.Name;
            Home = o.Home;
            HomeFacing = o.HomeFacing;
            Position = o.Position;
            Facing = o.Facing;
            PreviousPosition = o.PreviousPosition;
            PreviousFacing = o.PreviousFacing;
            Cargo = o.Cargo;
            CargoCount = o.CargoCount;
            Capacity = o.Capacity;
            Loop = o.Loop;
            LastResult = o.LastResult;
            DeliveredToday = o.DeliveredToday;
            ProblemsToday = o.ProblemsToday;
            Routine.SetAll(o.Routine);

            _stack.Clear();
            foreach (var f in o._stack)
            {
                Routine r;
                if (f.SpellId < 0) r = Routine;
                else
                {
                    var spell = book.Get(f.SpellId);
                    if (spell != null) r = spell.Routine;
                    else
                    {
                        r = new Routine(); // a deleted spell still running: keep a private copy
                        r.SetAll(f.Routine);
                    }
                }
                _stack.Add(new Frame { Routine = r, SpellId = f.SpellId, Pc = f.Pc });
            }
            if (_stack.Count == 0) ResetProgram();

            _lastPath.Clear();
            _lastPath.AddRange(o._lastPath);
        }

        internal void ResetDailyStats()
        {
            DeliveredToday = 0;
            ProblemsToday = 0;
        }

        internal void AddCargo(Resource r, int n)
        {
            if (n <= 0) return;
            Cargo = r;
            CargoCount += n;
        }

        internal void RemoveCargo(int n)
        {
            CargoCount -= n;
            if (CargoCount <= 0)
            {
                CargoCount = 0;
                Cargo = Resource.None;
            }
        }

        /// <summary>
        /// Walks the call stack to the next real command, stepping into Casts (free) and out of finished
        /// spells. Returns false if there is nothing to do this tick.
        /// </summary>
        internal bool TryFetchNext(Spellbook book, out Command command, out CommandResult error)
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
                        _stack.RemoveAt(_stack.Count - 1); // spell finished: back to the caller
                        continue;
                    }
                    if (!Loop || top.Routine.Count == 0) return false;
                    top.Pc = 0;
                    continue;
                }

                var cmd = top.Routine[top.Pc];
                top.Pc++;

                if (cmd.IsCast)
                {
                    var spell = book.Get(cmd.SpellId);
                    if (spell == null || _stack.Count > Spellbook.MaxDepth)
                    {
                        RecordPath();
                        error = spell == null ? CommandResult.UnknownSpell : CommandResult.SpellTooDeep;
                        if (error == CommandResult.SpellTooDeep) ResetProgram();
                        return false;
                    }
                    _stack.Add(new Frame { Routine = spell.Routine, SpellId = spell.Id, Pc = 0 });
                    continue;
                }

                RecordPath();
                command = cmd;
                return true;
            }

            // Only casts of empty spells: effectively idle.
            return false;
        }

        void RecordPath()
        {
            _lastPath.Clear();
            foreach (var f in _stack) _lastPath.Add(new ExecPoint(f.SpellId, f.Pc - 1));
        }

        public override string ToString() => $"{Name} at {Position} facing {Facing}";
    }
}
