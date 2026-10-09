using Terraria;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;
using MilkcraftTerraria.Content.Items;
using MilkcraftTerraria.Content.Tiles;

namespace MilkcraftTerraria.Content;

internal readonly record struct RecipeIngredient(int Type, int Stack, string Group = null)
{
    public static RecipeIngredient Vanilla(int type, int stack) => new(type, stack);
    public static RecipeIngredient Mod(int type, int stack) => new(type, stack);
    public static RecipeIngredient Grouped(string group, int stack) => new(0, stack, group);
}

public sealed class MilkcraftRecipeSystem : ModSystem
{
    public override void AddRecipeGroups()
    {
        RecipeGroup.RegisterGroup("MilkcraftTerraria:GoldBarGroup", new RecipeGroup(() => $"{Lang.GetItemNameValue(ItemID.GoldBar)} / {Lang.GetItemNameValue(ItemID.PlatinumBar)}", ItemID.GoldBar, ItemID.PlatinumBar));
    }

    public override void AddRecipes()
    {
        AddGear(ModContent.ItemType<IronHelmet>(), 1, TileID.Anvils, G("IronBarGroup", 5), G("GoldBarGroup", 1));
        AddGear(ModContent.ItemType<IronChestplate>(), 1, TileID.Anvils, G("IronBarGroup", 8), G("GoldBarGroup", 1));
        AddGear(ModContent.ItemType<IronGreaves>(), 1, TileID.Anvils, G("IronBarGroup", 11), G("GoldBarGroup", 1));
        AddGear(ModContent.ItemType<LeatherHeadband>(), 1, TileID.Anvils, V(ItemID.Leather, 5));
        AddGear(ModContent.ItemType<LeatherTunic>(), 1, TileID.Anvils, V(ItemID.Leather, 8));
        AddGear(ModContent.ItemType<LeatherLeggings>(), 1, TileID.Anvils, V(ItemID.Leather, 11));
        AddGear(ModContent.ItemType<ClothHood>(), 1, TileID.Anvils, M<CoarseWool>(5));
        AddGear(ModContent.ItemType<ClothRobe>(), 1, TileID.Anvils, M<CoarseWool>(8));
        AddGear(ModContent.ItemType<ClothLeggings>(), 1, TileID.Anvils, M<CoarseWool>(11));
        AddGear(ModContent.ItemType<KnightHelmet>(), 1, TileID.Anvils, M<MagicIronBar>(5));
        AddGear(ModContent.ItemType<KnightChestplate>(), 1, TileID.Anvils, M<MagicIronBar>(8));
        AddGear(ModContent.ItemType<KnightGreaves>(), 1, TileID.Anvils, M<MagicIronBar>(11));
        AddGear(ModContent.ItemType<HuntingHood>(), 1, TileID.Anvils, M<WildLeather>(5));
        AddGear(ModContent.ItemType<HuntingChestplate>(), 1, TileID.Anvils, M<WildLeather>(8));
        AddGear(ModContent.ItemType<HuntingLeggings>(), 1, TileID.Anvils, M<WildLeather>(11));
        AddGear(ModContent.ItemType<EnergyHood>(), 1, TileID.Anvils, M<MagicWool>(5));
        AddGear(ModContent.ItemType<EnergyRobe>(), 1, TileID.Anvils, M<MagicWool>(8));
        AddGear(ModContent.ItemType<EnergyLeggings>(), 1, TileID.Anvils, M<MagicWool>(11));

        AddGear(ModContent.ItemType<IronSword>(), 1, TileID.Anvils, G("IronBarGroup", 7), G("GoldBarGroup", 1), G("WoodGroup", 2));
        AddGear(ModContent.ItemType<IronBow>(), 1, TileID.Anvils, G("IronBarGroup", 3), G("WoodGroup", 5), V(ItemID.Cobweb, 6));
        AddGear(ModContent.ItemType<IronStaff>(), 1, TileID.Anvils, M<CoarseWool>(2), G("IronBarGroup", 3), V(ItemID.FallenStar, 1));
        AddGear(ModContent.ItemType<KnightSword>(), 1, TileID.Anvils, M<IronSword>(1), M<MagicIronBar>(6));
        AddGear(ModContent.ItemType<HuntingBow>(), 1, TileID.Anvils, M<IronBow>(1), M<MagicIronBar>(4), M<WildLeather>(4));
        AddGear(ModContent.ItemType<EnergyStaff>(), 1, TileID.Anvils, M<IronStaff>(1), M<MagicIronBar>(4), M<MagicWool>(4));

        AddAccessory(ModContent.ItemType<IronShield>(), G("IronBarGroup", 6), G("GoldBarGroup", 1), G("WoodGroup", 4));
        AddAccessory(ModContent.ItemType<IronQuiver>(), V(ItemID.Leather, 2), V(ItemID.Cobweb, 1));
        AddAccessory(ModContent.ItemType<BeginnerTome>(), M<CoarseWool>(3), V(ItemID.Leather, 1), V(ItemID.FallenStar, 1));
        AddAccessory(ModContent.ItemType<KnightBulwark>(), M<IronShield>(1), M<MagicIronBar>(8));
        AddAccessory(ModContent.ItemType<HuntingQuiver>(), M<WildLeather>(6), V(ItemID.WoodenArrow, 50));
        AddAccessory(ModContent.ItemType<EnergyTome>(), M<BeginnerTome>(1), M<MagicWool>(4), M<WildLeather>(2));

        AddGear(ModContent.ItemType<CoarseWool>(), 4, Tile<MilkcraftLoomTile>(), V(ItemID.Silk, 1));
        AddGear(ModContent.ItemType<BlackIronBar>(), 1, Tile<MilkcraftFurnaceTile>(), M<BlackIronOre>(3));
        AddGear(ModContent.ItemType<Alcohol>(), 3, Tile<MilkcraftDistilleryTile>(), V(ItemID.BottledWater, 1), V(ItemID.Mushroom, 1), V(ItemID.Gel, 1));
        AddGatedGear(ModContent.ItemType<MagicIronBar>(), 8, Tile<MilkcraftFurnaceTile>(), M<BlackIronBar>(4), M<PinkDiamond>(1));
        AddGatedGear(ModContent.ItemType<WildLeather>(), 8, Tile<MilkcraftLoomTile>(), V(ItemID.Leather, 4), M<Alcohol>(1), V(ItemID.JungleSpores, 1));
        AddGatedGear(ModContent.ItemType<MagicWool>(), 8, Tile<MilkcraftLoomTile>(), M<CoarseWool>(4), V(ItemID.FallenStar, 1), M<PinkDiamond>(1));

        AddGear(ModContent.ItemType<MilkcraftWorkbench>(), 1, -1, G("WoodGroup", 12));
        AddGear(ModContent.ItemType<MilkcraftFurnace>(), 1, Tile<MilkcraftWorkbenchTile>(), V(ItemID.StoneBlock, 20), G("WoodGroup", 4), V(ItemID.Torch, 3));
        AddGear(ModContent.ItemType<MilkcraftLoom>(), 1, Tile<MilkcraftWorkbenchTile>(), G("WoodGroup", 12), G("IronBarGroup", 1));
        AddGear(ModContent.ItemType<MilkcraftForge>(), 1, Tile<MilkcraftWorkbenchTile>(), G("IronBarGroup", 5), V(ItemID.StoneBlock, 6), G("WoodGroup", 4), V(ItemID.TinkerersWorkshop, 1));
        AddGear(ModContent.ItemType<MilkcraftDistillery>(), 1, Tile<MilkcraftWorkbenchTile>(), V(ItemID.Glass, 6), G("IronBarGroup", 2), V(ItemID.GrayBrick, 6));
        AddGear(ModContent.ItemType<MilkcraftAlchemyTable>(), 1, Tile<MilkcraftWorkbenchTile>(), V(ItemID.Glass, 6), G("IronBarGroup", 2), G("WoodGroup", 8), M<PinkDiamond>(1));
        AddGear(ModContent.ItemType<MilkcraftEnchantingTable>(), 1, Tile<MilkcraftWorkbenchTile>(), V(ItemID.StoneBlock, 8), G("IronBarGroup", 2), V(ItemID.FallenStar, 1), M<PinkDiamond>(1), M<EnchantmentHeart>(1));

        AddGear(ItemID.Leather, 4, Tile<MilkcraftLoomTile>(), V(ItemID.Silk, 1), V(ItemID.Gel, 2));
        AddGear(ModContent.ItemType<PinkDiamondOre>(), 1, -1, M<PinkDiamond>(1));
    }

    private static int Tile<T>() where T : ModTile => ModContent.TileType<T>();
    private static RecipeIngredient V(int type, int stack) => RecipeIngredient.Vanilla(type, stack);
    private static RecipeIngredient G(string group, int stack) => RecipeIngredient.Grouped(group switch
    {
        "IronBarGroup" => "Terraria:IronBar",
        "GoldBarGroup" => "MilkcraftTerraria:GoldBarGroup",
        "WoodGroup" => "Terraria:Wood",
        _ => "MilkcraftTerraria:" + group
    }, stack);
    private static RecipeIngredient M<T>(int stack) where T : ModItem => RecipeIngredient.Mod(ModContent.ItemType<T>(), stack);

    private static void AddGear(int output, int stack, int tile, params RecipeIngredient[] ingredients)
    {
        AddGearAtTiles(output, stack, tile >= 0 ? new[] { tile } : System.Array.Empty<int>(), ingredients);
    }

    private static void AddAccessory(int output, params RecipeIngredient[] ingredients)
    {
        AddGearAtTiles(output, 1, new[] { Tile<MilkcraftForgeTile>() }, ingredients);
    }

    private static void AddGearAtTiles(int output, int stack, int[] tiles, params RecipeIngredient[] ingredients)
    {
        var recipe = Recipe.Create(output, stack);
        foreach (var ingredient in ingredients)
        {
            if (ingredient.Group is not null)
                AddRecipeGroup(recipe, ingredient);
            else
                recipe.AddIngredient(ingredient.Type, ingredient.Stack);
        }
        foreach (int tile in tiles)
            recipe.AddTile(tile);
        recipe.Register();
    }

    private static void AddGatedGear(int output, int stack, int tile, params RecipeIngredient[] ingredients)
    {
        var recipe = Recipe.Create(output, stack);
        foreach (var ingredient in ingredients)
        {
            if (ingredient.Group is not null)
                AddRecipeGroup(recipe, ingredient);
            else
                recipe.AddIngredient(ingredient.Type, ingredient.Stack);
        }
        recipe.AddTile(tile);
        recipe.AddCondition(new Condition("Mods.MilkcraftTerraria.Conditions.DownedEvilBoss", () => NPC.downedBoss2));
        recipe.Register();
    }

    private static void AddRecipeGroup(Recipe recipe, RecipeIngredient ingredient)
    {
        if (ingredient.Group == "Terraria:IronBar")
            recipe.AddRecipeGroup(RecipeGroupID.IronBar, ingredient.Stack);
        else if (ingredient.Group == "Terraria:Wood")
            recipe.AddRecipeGroup(RecipeGroupID.Wood, ingredient.Stack);
        else
            recipe.AddRecipeGroup(ingredient.Group, ingredient.Stack);
    }
}

internal static class MilkcraftRecipes
{
    public static void AddGearRecipe(ModItem item) { }
    public static void AddMaterialRecipe(ModItem item) { }
    public static void AddStationRecipe(ModItem item) { }
}
