using System;
using System.Collections.Generic;

namespace Wizard.Sim
{
    /// <summary>A named, reusable chunk of commands. Any construct (or other spell) can Cast it.</summary>
    public sealed class Spell
    {
        public int Id { get; }
        public string Name { get; internal set; }
        public Routine Routine { get; } = new Routine();

        internal Spell(int id, string name)
        {
            Id = id;
            Name = name;
        }

        public override string ToString() => Name;
    }

    /// <summary>
    /// The wizard's shared spells. Constructs reference spells by id, so editing a spell changes
    /// every routine that casts it.
    /// </summary>
    public sealed class Spellbook
    {
        public const int MaxSpells = 8;
        public const int MaxNameLength = 18;

        /// <summary>How many spells deep a cast chain may go before it's treated as runaway magic.</summary>
        public const int MaxDepth = 8;

        static readonly string[] s_DefaultNames =
        {
            "Errand", "Shuffle", "Fetch", "Round Trip", "Loop-de-loop", "Hop", "Ramble", "Detour",
        };

        readonly List<Spell> _spells = new List<Spell>();
        int _nextId = 1;

        public IReadOnlyList<Spell> Spells => _spells;
        public bool IsFull => _spells.Count >= MaxSpells;

        /// <summary>Raised when a spell is added, removed or renamed (not when its commands change).</summary>
        public event Action Changed;

        /// <summary>Raised when any spell's commands change.</summary>
        public event Action<Spell> SpellEdited;

        public Spell Get(int id)
        {
            foreach (var s in _spells)
                if (s.Id == id) return s;
            return null;
        }

        public Spell Create(string name = null)
        {
            if (IsFull) return null;
            var spell = new Spell(_nextId++, Clean(name) ?? NextDefaultName());
            spell.Routine.Changed += () => SpellEdited?.Invoke(spell);
            _spells.Add(spell);
            Changed?.Invoke();
            return spell;
        }

        public void Rename(Spell spell, string name)
        {
            var clean = Clean(name);
            if (spell == null || clean == null || clean == spell.Name) return;
            spell.Name = clean;
            Changed?.Invoke();
        }

        /// <summary>Removes the spell from the book. Use World.DeleteSpell to also strip casts of it.</summary>
        internal bool Remove(Spell spell)
        {
            if (!_spells.Remove(spell)) return false;
            foreach (var s in _spells) s.Routine.RemoveAll(c => c.IsCast && c.SpellId == spell.Id);
            Changed?.Invoke();
            return true;
        }

        /// <summary>True if running <paramref name="from"/> would (directly or indirectly) cast <paramref name="targetId"/>.</summary>
        public bool Reaches(Spell from, int targetId)
        {
            if (from == null) return false;
            var visited = new HashSet<int>();
            var stack = new Stack<Spell>();
            stack.Push(from);
            while (stack.Count > 0)
            {
                var s = stack.Pop();
                if (!visited.Add(s.Id)) continue;
                foreach (var c in s.Routine)
                {
                    if (!c.IsCast) continue;
                    if (c.SpellId == targetId) return true;
                    var next = Get(c.SpellId);
                    if (next != null) stack.Push(next);
                }
            }
            return false;
        }

        /// <summary>
        /// Can a Cast of <paramref name="spellId"/> be added to a routine? <paramref name="owner"/> is the
        /// spell being edited, or null for a construct's main routine. Prevents a spell casting itself.
        /// </summary>
        public bool CanCastFrom(Spell owner, int spellId)
        {
            var target = Get(spellId);
            if (target == null) return false;
            if (owner == null) return true;
            return target.Id != owner.Id && !Reaches(target, owner.Id);
        }

        /// <summary>Replaces this book's contents with a copy of <paramref name="other"/>, keeping spell ids.</summary>
        internal void CopyFrom(Spellbook other)
        {
            _spells.Clear();
            foreach (var s in other._spells)
            {
                var copy = new Spell(s.Id, s.Name);
                copy.Routine.SetAll(s.Routine);
                copy.Routine.Changed += () => SpellEdited?.Invoke(copy);
                _spells.Add(copy);
            }
            _nextId = other._nextId;
            Changed?.Invoke();
        }

        string NextDefaultName()
        {
            foreach (var n in s_DefaultNames)
            {
                bool used = false;
                foreach (var s in _spells)
                    if (s.Name == n) used = true;
                if (!used) return n;
            }
            return "Spell " + _nextId;
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
