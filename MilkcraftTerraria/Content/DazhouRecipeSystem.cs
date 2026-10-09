using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using MilkcraftTerraria.Content.Items;

namespace MilkcraftTerraria.Content;

/// <summary>The new ultimate recipe is isolated from all frozen 0.2.8 recipes.</summary>
public sealed class DazhouRecipeSystem : ModSystem
{
    public override void AddRecipes()
    {
        Recipe.Create(ModContent.ItemType<DazhouIronSword>())
            .AddIngredient(ModContent.ItemType<IronSword>(), 1)
            .AddIngredient(ModContent.ItemType<KnightSword>(), 1)
            .AddIngredient(ModContent.ItemType<TemplarSword>(), 1)
            .AddIngredient(ModContent.ItemType<SoulforgedSword>(), 1)
            .AddIngredient(ModContent.ItemType<HiddenIronSword>(), 1)
            .AddTile(TileID.MythrilAnvil)
            .Register();
    }
}
