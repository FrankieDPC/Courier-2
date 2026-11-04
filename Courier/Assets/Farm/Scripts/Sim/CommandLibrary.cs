using System;
using System.Collections.Generic;

namespace Farm.Sim
{
    /// <summary>A custom command: a named, reusable chunk of commands. Any bot (or other custom command) can Run it.</summary>
    public sealed class Macro
    {
        public int Id { get; }
        public string Name { get; internal set; }
        public Routine Routine { get; } = new Routine();

        internal Macro(int id, string name)
        {
            Id = id;
            Name = name;
        }

        public override string ToString() => Name;
    }

    /// <summary>
    /// The farm's shared custom commands. Bots reference them by id, so editing one changes every
    /// routine that runs it.
    /// </summary>
    public sealed class CommandLibrary
    {
        public const int MaxMacros = 8;
        public const int MaxNameLength = 18;

        /// <summary>How deep custom commands may nest before it's treated as a runaway loop.</summary>
        public const int MaxDepth = 8;

        static readonly string[] s_DefaultNames =
        {
            "Chores", "Water Row", "Fetch Water", "Round Trip", "Harvest Row", "Plant Row", "Errand", "Detour",
        };

        readonly List<Macro> _macros = new List<Macro>();
        int _nextId = 1;

        public IReadOnlyList<Macro> Macros => _macros;
        public bool IsFull => _macros.Count >= MaxMacros;

        /// <summary>Raised when a custom command is added, removed or renamed (not when its contents change).</summary>
        public event Action Changed;

        /// <summary>Raised when any custom command's contents change.</summary>
        public event Action<Macro> MacroEdited;

        public Macro Get(int id)
        {
            foreach (var s in _macros)
                if (s.Id == id) return s;
            return null;
        }

        public Macro Create(string name = null)
        {
            if (IsFull) return null;
            var macro = new Macro(_nextId++, Clean(name) ?? NextDefaultName());
            macro.Routine.Changed += () => MacroEdited?.Invoke(macro);
            _macros.Add(macro);
            Changed?.Invoke();
            return macro;
        }

        /// <summary>Creates a custom command holding a copy of <paramref name="commands"/> ("save as command").</summary>
        public Macro Create(string name, IEnumerable<Command> commands)
        {
            var macro = Create(name);
            if (macro != null && commands != null) macro.Routine.SetAll(new List<Command>(commands));
            return macro;
        }

        public void Rename(Macro macro, string name)
        {
            var clean = Clean(name);
            if (macro == null || clean == null || clean == macro.Name) return;
            macro.Name = clean;
            Changed?.Invoke();
        }

        /// <summary>Removes the custom command. Use World.DeleteMacro to also strip runs of it from bots.</summary>
        internal bool Remove(Macro macro)
        {
            if (!_macros.Remove(macro)) return false;
            foreach (var s in _macros) s.Routine.RemoveAll(c => c.IsRun && c.MacroId == macro.Id);
            Changed?.Invoke();
            return true;
        }

        /// <summary>True if running <paramref name="from"/> would (directly or indirectly) run <paramref name="targetId"/>.</summary>
        public bool Reaches(Macro from, int targetId)
        {
            if (from == null) return false;
            var visited = new HashSet<int>();
            var stack = new Stack<Macro>();
            stack.Push(from);
            while (stack.Count > 0)
            {
                var s = stack.Pop();
                if (!visited.Add(s.Id)) continue;
                foreach (var c in s.Routine)
                {
                    if (!c.IsRun) continue;
                    if (c.MacroId == targetId) return true;
                    var next = Get(c.MacroId);
                    if (next != null) stack.Push(next);
                }
            }
            return false;
        }

        /// <summary>
        /// Can a Run of <paramref name="macroId"/> be added to a routine? <paramref name="owner"/> is the
        /// custom command being edited, or null for a bot's own routine. Prevents a command running itself.
        /// </summary>
        public bool CanRunFrom(Macro owner, int macroId)
        {
            var target = Get(macroId);
            if (target == null) return false;
            if (owner == null) return true;
            return target.Id != owner.Id && !Reaches(target, owner.Id);
        }

        /// <summary>Replaces this library's contents with a copy of <paramref name="other"/>, keeping ids.</summary>
        internal void CopyFrom(CommandLibrary other)
        {
            _macros.Clear();
            foreach (var s in other._macros)
            {
                var copy = new Macro(s.Id, s.Name);
                copy.Routine.SetAll(s.Routine);
                copy.Routine.Changed += () => MacroEdited?.Invoke(copy);
                _macros.Add(copy);
            }
            _nextId = other._nextId;
            Changed?.Invoke();
        }

        string NextDefaultName()
        {
            foreach (var n in s_DefaultNames)
            {
                bool used = false;
                foreach (var s in _macros)
                    if (s.Name == n) used = true;
                if (!used) return n;
            }
            return "Macro " + _nextId;
        }

        static string Clean(string name)
        {
            if (name == null) return null;
            name = name.Trim();
            if (name.Length == 0) return null;
            return name.Length > MaxNameLength ? name.Substring(0, MaxNameLength) : name;
        }
    }
}
