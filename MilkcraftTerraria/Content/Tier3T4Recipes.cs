using Terraria;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;
using MilkcraftTerraria.Content.Items;
using MilkcraftTerraria.Content.Tiles;

namespace MilkcraftTerraria.Content;

/// <summary>Deterministic T3/T4 recipes, gated by vanilla world progression.</summary>
public sealed class Tier3T4RecipeSystem : ModSystem
{
    public override void AddRecipes()
    {
        // T3 recipes retain the mechanical-boss stage gate; their fixed proofs
        // and class materials now come from the three Dusk bosses.
        AddStageRecipe<OriginBar>(8, Tile<MilkcraftFurnaceTile>(), 3,
            M<MagicIronBar>(4), V(ItemID.HallowedBar, 1), M<TemplarPotion>(1));
        AddStageRecipe<OrnateLeather>(8, Tile<MilkcraftLoomTile>(), 3,
            M<WildLeather>(4), V(ItemID.Silk, 2), M<NightmarePotion>(1));
        AddStageRecipe<RunicWool>(8, Tile<MilkcraftLoomTile>(), 3,
            M<MagicWool>(4), V(ItemID.FallenStar, 1), M<FallenPotion>(1));

        // T4 recipes retain the Golem-stage gate and use the fixed three-role
        // reward cycle from Castle Core.
        AddStageRecipe<CinderBar>(8, Tile<MilkcraftFurnaceTile>(), 4,
            M<OriginBar>(4), V(ItemID.ChlorophyteBar, 1), M<SoulforgedPotion>(1));
        AddStageRecipe<BaneboundLeather>(8, Tile<MilkcraftLoomTile>(), 4,
            M<OrnateLeather>(4), V(ItemID.Silk, 1), M<AbyssPotion>(1));
        AddStageRecipe<SighWool>(8, Tile<MilkcraftLoomTile>(), 4,
            M<RunicWool>(4), V(ItemID.CrystalShard, 1), M<DarkPotion>(1));

        // Each character activates one permanent, tier-wide license from one
        // material of each class family. The item cannot be re-used afterward.
        AddStageRecipe<T3EquipmentLicense>(1, TileID.MythrilAnvil, 3,
            M<WraithWarriorProof>(1), M<JungleLordProof>(1), M<BansheeProof>(1));
        AddStageRecipe<T4EquipmentLicense>(1, TileID.MythrilAnvil, 4,
            M<CastleCoreClearSeal>(1));

        AddAlchemyRecipe<TemplarPotion>(3, ItemID.SoulofFright, ItemID.Daybloom, ModContent.ItemType<CrystalSkull>());
        AddAlchemyRecipe<NightmarePotion>(3, ItemID.SoulofMight, ItemID.Moonglow, ModContent.ItemType<AlienKeratin>());
        AddAlchemyRecipe<FallenPotion>(3, ItemID.SoulofSight, ItemID.Deathweed, ModContent.ItemType<ArcaneCore>());
        AddAlchemyRecipe<SoulforgedPotion>(4, ItemID.LunarTabletFragment, ItemID.Fireblossom, ModContent.ItemType<WitherTear>());
        AddAlchemyRecipe<AbyssPotion>(4, ItemID.Ectoplasm, ItemID.Moonglow, ModContent.ItemType<DemonWing>());
        AddAlchemyRecipe<DarkPotion>(4, ItemID.CrystalShard, ItemID.Deathweed, ModContent.ItemType<EnchantedShinbone>());

        AddStageRecipe<TemplarHelmet>(1, TileID.MythrilAnvil, 3, M<OriginBar>(5));
        AddStageRecipe<TemplarChestplate>(1, TileID.MythrilAnvil, 3, M<OriginBar>(8));
        AddStageRecipe<TemplarGreaves>(1, TileID.MythrilAnvil, 3, M<OriginBar>(11));
        AddStageRecipe<NightmareHood>(1, TileID.MythrilAnvil, 3, M<OrnateLeather>(5));
        AddStageRecipe<NightmareChestplate>(1, TileID.MythrilAnvil, 3, M<OrnateLeather>(8));
        AddStageRecipe<NightmareLeggings>(1, TileID.MythrilAnvil, 3, M<OrnateLeather>(11));
        AddStageRecipe<FallenHood>(1, TileID.MythrilAnvil, 3, M<RunicWool>(5));
        AddStageRecipe<FallenRobe>(1, TileID.MythrilAnvil, 3, M<RunicWool>(8));
        AddStageRecipe<FallenLeggings>(1, TileID.MythrilAnvil, 3, M<RunicWool>(11));

        AddStageRecipe<TemplarSword>(1, TileID.MythrilAnvil, 3, M<TruthSword>(1), M<OriginBar>(6));
        AddStageRecipe<NightmareBow>(1, TileID.MythrilAnvil, 3, M<TruthBow>(1), M<OrnateLeather>(6), M<OriginBar>(4));
        AddStageRecipe<FallenStaff>(1, TileID.MythrilAnvil, 3, M<TruthStaff>(1), M<RunicWool>(6), M<OriginBar>(4));
        AddStageAccessory<TemplarShield>(3, V(ModContent.ItemType<KnightBulwark>(), 1), M<OriginBar>(6));
        AddStageAccessory<NightmareQuiver>(3, V(ModContent.ItemType<HuntingQuiver>(), 1), M<OrnateLeather>(6));
        AddStageAccessory<FallenTome>(3, V(ModContent.ItemType<EnergyTome>(), 1), M<RunicWool>(6));

        AddStageRecipe<SoulforgedHelmet>(1, TileID.MythrilAnvil, 4, M<CinderBar>(5));
        AddStageRecipe<SoulforgedChestplate>(1, TileID.MythrilAnvil, 4, M<CinderBar>(8));
        AddStageRecipe<SoulforgedGreaves>(1, TileID.MythrilAnvil, 4, M<CinderBar>(11));
        AddStageRecipe<AbyssHood>(1, TileID.MythrilAnvil, 4, M<BaneboundLeather>(5));
        AddStageRecipe<AbyssChestplate>(1, TileID.MythrilAnvil, 4, M<BaneboundLeather>(8));
        AddStageRecipe<AbyssLeggings>(1, TileID.MythrilAnvil, 4, M<BaneboundLeather>(11));
        AddStageRecipe<DarkHood>(1, TileID.MythrilAnvil, 4, M<SighWool>(5));
        AddStageRecipe<DarkRobe>(1, TileID.MythrilAnvil, 4, M<SighWool>(8));
        AddStageRecipe<DarkLeggings>(1, TileID.MythrilAnvil, 4, M<SighWool>(11));

        AddStageRecipe<SoulforgedSword>(1, TileID.MythrilAnvil, 4, M<BrokenSwordHilt>(1), M<CinderBar>(6));
        AddStageRecipe<AbyssBow>(1, TileID.MythrilAnvil, 4, M<BrokenBowstring>(1), M<BaneboundLeather>(6), M<CinderBar>(4));
        AddStageRecipe<DarkStaff>(1, TileID.MythrilAnvil, 4, M<BrokenStaffHandle>(1), M<SighWool>(6), M<CinderBar>(4));
        AddStageAccessory<SoulforgedShield>(4, V(ModContent.ItemType<TemplarShield>(), 1), M<CinderBar>(6));
        AddStageAccessory<AbyssQuiver>(4, V(ModContent.ItemType<NightmareQuiver>(), 1), M<BaneboundLeather>(6));
        AddStageAccessory<DarkTome>(4, V(ModContent.ItemType<FallenTome>(), 1), M<SighWool>(6));
    }

    private static int Tile<T>() where T : ModTile => ModContent.TileType<T>();
    private static RecipeIngredient V(int type, int stack) => RecipeIngredient.Vanilla(type, stack);
    private static RecipeIngredient M<T>(int stack) where T : ModItem => RecipeIngredient.Mod(ModContent.ItemType<T>(), stack);

    private static void AddStageRecipe<T>(int stack, int tile, int tier, params RecipeIngredient[] ingredients) where T : ModItem
    {
        Recipe recipe = Recipe.Create(ModContent.ItemType<T>(), stack);
        foreach (RecipeIngredient ingredient in ingredients)
            recipe.AddIngredient(ingredient.Type, ingredient.Stack);
        recipe.AddTile(tile);
        if (tier == 3)
            recipe.AddCondition(new Condition("Mods.MilkcraftTerraria.Conditions.T3WorldProgress", () => NPC.downedMechBossAny));
        else
            recipe.AddCondition(new Condition("Mods.MilkcraftTerraria.Conditions.T4WorldProgress", () => NPC.downedGolemBoss));
        recipe.Register();
    }

    private static void AddStageAccessory<T>(int tier, params RecipeIngredient[] ingredients) where T : ModItem
    {
        Recipe recipe = Recipe.Create(ModContent.ItemType<T>());
        foreach (RecipeIngredient ingredient in ingredients)
            recipe.AddIngredient(ingredient.Type, ingredient.Stack);
        recipe.AddTile(Tile<MilkcraftForgeTile>());
        if (tier == 3)
            recipe.AddCondition(new Condition("Mods.MilkcraftTerraria.Conditions.T3WorldProgress", () => NPC.downedMechBossAny));
        else
            recipe.AddCondition(new Condition("Mods.MilkcraftTerraria.Conditions.T4WorldProgress", () => NPC.downedGolemBoss));
        recipe.Register();
    }

    private static void AddAlchemyRecipe<T>(int tier, int soulType, int herbType, int coreType) where T : ModItem
    {
        Recipe recipe = Recipe.Create(ModContent.ItemType<T>(), 6);
        recipe.AddIngredient(ItemID.BottledWater, 2);
        recipe.AddIngredient(ModContent.ItemType<Alcohol>(), 1);
        recipe.AddIngredient(ModContent.ItemType<PinkDiamond>(), 1);
        recipe.AddIngredient(coreType, 1);
        recipe.AddIngredient(soulType, 2);
        recipe.AddIngredient(herbType, 1);
        recipe.AddTile(Tile<MilkcraftAlchemyTableTile>());
        recipe.AddCondition(new Condition("Mods.MilkcraftTerraria.Conditions.ActiveAlchemyTable", () => MilkcraftAlchemyWorldSystem.PlayerNearActiveTable(Main.LocalPlayer)));
        if (tier == 3)
            recipe.AddCondition(new Condition("Mods.MilkcraftTerraria.Conditions.T3WorldProgress", () => NPC.downedMechBossAny));
        else
            recipe.AddCondition(new Condition("Mods.MilkcraftTerraria.Conditions.T4WorldProgress", () => NPC.downedGolemBoss));
        recipe.Register();
    }
}
