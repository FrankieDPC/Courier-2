using System.Collections.Generic;

namespace Bunker.Sim
{
    /// <summary>Things you can make in the bunker workshop from what's in the stores.</summary>
    public enum Recipe
    {
        WaterBot,
        Farmhand,

        /// <summary>Every bot (and you) can carry more.</summary>
        BiggerTanks,

        // Turn one stored crop into seeds, so a harvest can be replanted.
        SaveRadishSeeds,
        SavePotatoSeeds,
        SaveSquashSeeds,
    }

    // The workshop: build bots, upgrade carrying, save seeds. Everything is paid for from the stores.
    public sealed partial class World
    {
        public static readonly Recipe[] AllRecipes =
        {
            Recipe.WaterBot, Recipe.Farmhand, Recipe.BiggerTanks,
            Recipe.SaveRadishSeeds, Recipe.SavePotatoSeeds, Recipe.SaveSquashSeeds,
        };

        /// <summary>Raised after something is made in the workshop.</summary>
        public event System.Action<Recipe> Built;

        public static CropType CropFor(Recipe r)
        {
            switch (r)
            {
                case Recipe.SaveRadishSeeds: return Crops.Radish;
                case Recipe.SavePotatoSeeds: return Crops.Potato;
                case Recipe.SaveSquashSeeds: return Crops.Squash;
                default: return null;
            }
        }

        /// <summary>What a recipe costs right now (bots and upgrades get pricier).</summary>
        public List<ItemAmount> CostOf(Recipe r)
        {
            var list = new List<ItemAmount>();
            int extra = Balance.ExtraScrapPerBot * BotsBuilt;
            switch (r)
            {
                case Recipe.WaterBot:
                    list.Add(new ItemAmount(Item.Scrap, Balance.WaterBotScrap + extra));
                    list.Add(new ItemAmount(Item.Circuit, Balance.WaterBotCircuits));
                    list.Add(new ItemAmount(Item.Fibre, Balance.WaterBotFibre));
                    break;
                case Recipe.Farmhand:
                    list.Add(new ItemAmount(Item.Scrap, Balance.FarmhandScrap + extra));
                    list.Add(new ItemAmount(Item.Circuit, Balance.FarmhandCircuits));
                    list.Add(new ItemAmount(Item.Wood, Balance.FarmhandWood));
                    list.Add(new ItemAmount(Item.Fibre, Balance.FarmhandFibre));
                    break;
                case Recipe.BiggerTanks:
                    list.Add(new ItemAmount(Item.Scrap, Balance.TankScrap * (CapacityLevel + 1)));
                    list.Add(new ItemAmount(Item.Wood, Balance.TankWood * (CapacityLevel + 1)));
                    break;
                default:
                    var crop = CropFor(r);
                    if (crop != null) list.Add(new ItemAmount(crop.Produce, 1));
                    break;
            }
            return list;
        }

        public bool CanAfford(Recipe r)
        {
            if (IsGameOver) return false;
            foreach (var a in CostOf(r))
                if (Stores.Get(a.Item) < a.Amount) return false;
            return true;
        }

        /// <summary>
        /// Makes the recipe if the stores can pay for it. New bots appear at the hatch with an empty
        /// routine (<paramref name="newBot"/>); they start looping it as soon as time runs.
        /// </summary>
        public bool TryBuild(Recipe r, out Bot newBot)
        {
            newBot = null;
            if (!CanAfford(r)) return false;
            foreach (var a in CostOf(r)) Stores.Remove(a.Item, a.Amount);

            switch (r)
            {
                case Recipe.WaterBot:
                case Recipe.Farmhand:
                {
                    var kind = r == Recipe.WaterBot ? BotKind.WaterBot : BotKind.Farmhand;
                    int ofKind = 0;
                    foreach (var b in _bots)
                        if (b.Kind == kind) ofKind++;
                    BotsBuilt++;
                    newBot = AddBot(Balance.BotName(kind, ofKind), kind);
                    break;
                }
                case Recipe.BiggerTanks:
                    CapacityLevel++;
                    foreach (var b in _bots) b.Capacity += Balance.CapacityUpgrade;
                    break;
                default:
                    var crop = CropFor(r);
                    if (crop != null) Stores.Add(crop.Seed, Balance.SeedsSaved);
                    break;
            }
            Built?.Invoke(r);
            return true;
        }

        public bool TryBuild(Recipe r) => TryBuild(r, out _);
    }
}
