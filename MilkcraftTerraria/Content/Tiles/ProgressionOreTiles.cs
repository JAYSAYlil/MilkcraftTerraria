using Microsoft.Xna.Framework;
using System.Collections.Generic;
using Terraria;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;
using MilkcraftTerraria.Content.Items;

namespace MilkcraftTerraria.Content.Tiles;

public abstract class MilkcraftOreTile : ModTile
{
    protected abstract int ItemType { get; }
    protected abstract int PickPower { get; }
    protected abstract Color MapColor { get; }
    public override string Texture => $"{Mod.Name}/Content/Tiles/{GetType().Name}";

    public override void SetStaticDefaults()
    {
        // These are ordinary, framable solid ores like Demonite and
        // Crimtane, not frame-important furniture/objects. Their 18px atlas
        // cells are selected by the vanilla tile framing system.
        Main.tileFrameImportant[Type] = false;
        Main.tileSolid[Type] = true;
        Main.tileBlockLight[Type] = true;
        Main.tileMergeDirt[Type] = true;
        Main.tileSpelunker[Type] = true;
        Main.tileShine[Type] = 700;
        MinPick = PickPower;
        MineResist = 1.25f;
        DustType = DustID.Stone;
        HitSound = SoundID.Tink;
        AddMapEntry(MapColor, Language.GetText("Mods.MilkcraftTerraria.MapObject." + GetType().Name));
        RegisterItemDrop(ItemType);
    }
}

public sealed class BlackIronOreTile : MilkcraftOreTile
{
    protected override int ItemType => ModContent.ItemType<BlackIronOre>();
    protected override int PickPower => 55;
    protected override Color MapColor => new(74, 66, 74);
}

public sealed class PinkDiamondOreTile : MilkcraftOreTile
{
    protected override int ItemType => ModContent.ItemType<PinkDiamond>();
    protected override int PickPower => 65;
    protected override Color MapColor => new(232, 102, 169);

    // The corresponding PinkDiamondOre item has createTile set, so the base
    // automatic drop resolver prefers that placeable item. This ore block is
    // intentionally harvested as one crafting PinkDiamond instead.
    public override IEnumerable<Item> GetItemDrops(int i, int j)
    {
        yield return new Item(ModContent.ItemType<PinkDiamond>(), 1);
    }
}
