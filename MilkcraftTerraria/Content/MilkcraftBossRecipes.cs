using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using MilkcraftTerraria.Content.Items;

namespace MilkcraftTerraria.Content;

public sealed class MilkcraftBossRecipeSystem : ModSystem
{
    public override void AddRecipeGroups()
    {
        RecipeGroup.RegisterGroup("MilkcraftTerraria:CorruptionMaterial", new RecipeGroup(
            () => $"{Lang.GetItemNameValue(ItemID.RottenChunk)} / {Lang.GetItemNameValue(ItemID.Vertebrae)}",
            ItemID.RottenChunk, ItemID.Vertebrae));
        RecipeGroup.RegisterGroup("MilkcraftTerraria:EarlyHardmodeBar", new RecipeGroup(
            () => $"{Lang.GetItemNameValue(ItemID.CobaltBar)} / {Lang.GetItemNameValue(ItemID.PalladiumBar)}",
            ItemID.CobaltBar, ItemID.PalladiumBar));
    }

    public override void AddRecipes()
    {
        // Summons use ordinary materials available before the corresponding boss.
        Recipe.Create(ModContent.ItemType<WraithWarriorSummon>())
            .AddRecipeGroup(RecipeGroupID.IronBar, 5).AddRecipeGroup("MilkcraftTerraria:CorruptionMaterial", 5)
            .AddIngredient(ItemID.Silk, 5).AddTile(TileID.DemonAltar).Register();
        Recipe.Create(ModContent.ItemType<JungleLordSummon>())
            .AddRecipeGroup(RecipeGroupID.IronBar, 5).AddIngredient(ItemID.JungleSpores, 5)
            .AddIngredient(ItemID.Stinger, 5).AddTile(TileID.DemonAltar).Register();
        Recipe.Create(ModContent.ItemType<BansheeSummon>())
            .AddRecipeGroup(RecipeGroupID.IronBar, 5).AddIngredient(ItemID.Lens, 3)
            .AddIngredient(ItemID.FallenStar, 3).AddTile(TileID.DemonAltar).Register();

        Recipe.Create(ModContent.ItemType<ObsidianGuardianSummon>())
            .AddIngredient(ItemID.Obsidian, 10).AddIngredient(ItemID.HellstoneBar, 5)
            .AddRecipeGroup("MilkcraftTerraria:EarlyHardmodeBar", 5).AddTile(TileID.MythrilAnvil).Register();
        Recipe.Create(ModContent.ItemType<SphinxSummon>())
            .AddIngredient(ItemID.Sandstone, 15).AddIngredient(ItemID.SoulofLight, 5)
            .AddRecipeGroup("MilkcraftTerraria:EarlyHardmodeBar", 5).AddTile(TileID.MythrilAnvil).Register();
        Recipe.Create(ModContent.ItemType<FrostWitchSummon>())
            .AddIngredient(ItemID.IceBlock, 20).AddIngredient(ItemID.SoulofLight, 5)
            .AddRecipeGroup("MilkcraftTerraria:EarlyHardmodeBar", 5).AddTile(TileID.MythrilAnvil).Register();
        Recipe.Create(ModContent.ItemType<CastleCoreSummon>())
            .AddIngredient(ItemID.HallowedBar, 6).AddIngredient(ItemID.StoneBlock, 20)
            .AddIngredient(ItemID.LihzahrdPowerCell, 1).AddTile(TileID.MythrilAnvil).Register();
    }
}
