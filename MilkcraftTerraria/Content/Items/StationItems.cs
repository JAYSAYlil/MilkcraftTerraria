using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using MilkcraftTerraria.Content.Tiles;

namespace MilkcraftTerraria.Content.Items;

public abstract class MilkcraftStationItem : ModItem
{
    protected abstract int TileType { get; }

    public override string Texture => $"{Mod.Name}/Content/Items/{GetType().Name}";

    public override void SetDefaults()
    {
        Item.DefaultToPlaceableTile(TileType);
        Item.width = 48;
        Item.height = 32;
        Item.rare = ItemRarityID.Blue;
        Item.value = Item.sellPrice(silver: 10);
    }

    public override void AddRecipes() => MilkcraftRecipes.AddStationRecipe(this);
}

public sealed class MilkcraftWorkbench : MilkcraftStationItem { protected override int TileType => ModContent.TileType<MilkcraftWorkbenchTile>(); }
public sealed class MilkcraftFurnace : MilkcraftStationItem { protected override int TileType => ModContent.TileType<MilkcraftFurnaceTile>(); }
public sealed class MilkcraftLoom : MilkcraftStationItem { protected override int TileType => ModContent.TileType<MilkcraftLoomTile>(); }
public sealed class MilkcraftForge : MilkcraftStationItem { protected override int TileType => ModContent.TileType<MilkcraftForgeTile>(); }
public sealed class MilkcraftDistillery : MilkcraftStationItem { protected override int TileType => ModContent.TileType<MilkcraftDistilleryTile>(); }
public sealed class MilkcraftAlchemyTable : MilkcraftStationItem { protected override int TileType => ModContent.TileType<MilkcraftAlchemyTableTile>(); }
public sealed class MilkcraftEnchantingTable : MilkcraftStationItem { protected override int TileType => ModContent.TileType<MilkcraftEnchantingTableTile>(); }
