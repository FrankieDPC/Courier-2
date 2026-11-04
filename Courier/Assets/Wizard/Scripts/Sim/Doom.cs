using System.Collections.Generic;

namespace Wizard.Sim
{
    public sealed class MonsterType
    {
        public string Name { get; }
        public int Hp { get; }

        /// <summary>Damage dealt to the tower when it gets there (it's spent in the attack).</summary>
        public int Damage { get; }

        /// <summary>Ticks per cell moved. Constructs move 1 cell per tick.</summary>
        public int TicksPerMove { get; }

        public MonsterType(string name, int hp, int damage, int ticksPerMove)
        {
            Name = name;
            Hp = hp;
            Damage = damage;
            TicksPerMove = ticksPerMove;
        }

        public override string ToString() => Name;
    }

    public sealed class Monster
    {
        public int Id { get; }
        public MonsterType Type { get; }
        public Cell Cell { get; internal set; }
        public Cell PreviousCell { get; internal set; }
        public int Hp { get; internal set; }
        public bool IsDead => Hp <= 0;
        internal int MoveTimer;

        internal Monster(int id, MonsterType type, Cell cell)
        {
            Id = id;
            Type = type;
            Cell = PreviousCell = cell;
            Hp = type.Hp;
            MoveTimer = type.TicksPerMove;
        }

        public override string ToString() => $"{Type.Name} #{Id} {Cell} hp {Hp}";
    }

    /// <summary>One group of monsters leaving a gate. They emerge one every <see cref="Spacing"/> ticks.</summary>
    public readonly struct Wave
    {
        public readonly int Tick;
        public readonly Facing Gate;
        public readonly MonsterType Type;
        public readonly int Count;
        public readonly int Spacing;

        public Wave(int tick, Facing gate, MonsterType type, int count, int spacing = 2)
        {
            Tick = tick;
            Gate = gate;
            Type = type;
            Count = count;
            Spacing = spacing;
        }
    }

    /// <summary>What arrives on the final day, and from where. Visible to the player from day 1.</summary>
    public sealed class DoomPlan
    {
        public int Day { get; }
        public IReadOnlyList<Facing> Gates { get; }
        public IReadOnlyList<Wave> Waves { get; }

        public DoomPlan(int day, IReadOnlyList<Facing> gates, IReadOnlyList<Wave> waves)
        {
            Day = day;
            Gates = gates;
            Waves = waves;
        }

        public int TotalMonsters
        {
            get
            {
                int n = 0;
                foreach (var w in Waves) n += w.Count;
                return n;
            }
        }

        public int Count(MonsterType type)
        {
            int n = 0;
            foreach (var w in Waves)
                if (w.Type == type) n += w.Count;
            return n;
        }
    }

    public enum Outcome
    {
        None,
        Victory,
        Defeat,
    }
}
