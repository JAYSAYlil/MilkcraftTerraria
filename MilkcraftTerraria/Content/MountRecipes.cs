using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using MilkcraftTerraria.Content.Items;

namespace MilkcraftTerraria.Content;

/// <summary>Two deterministic mount recipes, gated to their intended progression stages.</summary>
public sealed class MountRecipeSystem : ModSystem
{
    public override void AddRecipes()
    {
        Recipe.Create(ModContent.ItemType<UndeadWarhorseSummon>())
            .AddIngredient(ModContent.ItemType<OriginBar>(), 8)
            .AddIngredient(ItemID.SoulofFright, 5)
            .AddIngredient(ItemID.Silk, 10)
            .AddTile(TileID.MythrilAnvil)
            .AddCondition(new Condition("Mods.MilkcraftTerraria.Conditions.T3WorldProgress", () => NPC.downedMechBossAny))
            .Register();

        Recipe.Create(ModContent.ItemType<CelestialTenguSummon>())
            .AddIngredient(ModContent.ItemType<SighWool>(), 8)
            .AddIngredient(ItemID.BeetleHusk, 5)
            .AddIngredient(ItemID.SoulofFlight, 10)
            .AddTile(TileID.MythrilAnvil)
            .AddCondition(new Condition("Mods.MilkcraftTerraria.Conditions.T4WorldProgress", () => NPC.downedGolemBoss))
            .Register();

        Recipe.Create(ModContent.ItemType<RhinoSummon>())
            .AddIngredient(ItemID.HallowedBar, 10)
            .AddIngredient(ModContent.ItemType<OriginBar>(), 8)
            .AddRecipeGroup(RecipeGroupID.IronBar, 6)
            .AddTile(TileID.MythrilAnvil)
            .AddCondition(new Condition("Mods.MilkcraftTerraria.Conditions.T3WorldProgress", () => NPC.downedMechBossAny))
            .Register();

        Recipe.Create(ModContent.ItemType<FlameWarhorseSummon>())
            .AddIngredient(ItemID.HallowedBar, 10)
            .AddIngredient(ItemID.HellstoneBar, 8)
            .AddIngredient(ItemID.SoulofFright, 5)
            .AddTile(TileID.MythrilAnvil)
            .AddCondition(new Condition("Mods.MilkcraftTerraria.Conditions.T3WorldProgress", () => NPC.downedMechBossAny))
            .Register();
    }
}
