using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using MilkcraftTerraria.Content.Tiles;

namespace MilkcraftTerraria.Content.Items;

/// <summary>Ore chunks can be placed as decorative blocks and mined back as one item.</summary>
public sealed class BlackIronOre : MilkcraftMaterial
{
    public override void SetDefaults()
    {
        base.SetDefaults();
        Item.createTile = ModContent.TileType<BlackIronOreTile>();
        Item.useStyle = ItemUseStyleID.Swing;
        Item.useTime = Item.useAnimation = 15;
        Item.consumable = true;
        Item.autoReuse = true;
        Item.useTurn = true;
        Item.rare = ItemRarityID.Blue;
        Item.value = Item.sellPrice(copper: 25);
    }
}

public sealed class PinkDiamondOre : MilkcraftMaterial
{
    public override void SetDefaults()
    {
        base.SetDefaults();
        Item.createTile = ModContent.TileType<PinkDiamondOreTile>();
        Item.useStyle = ItemUseStyleID.Swing;
        Item.useTime = Item.useAnimation = 15;
        Item.consumable = true;
        Item.autoReuse = true;
        Item.useTurn = true;
        Item.rare = ItemRarityID.Pink;
        Item.value = Item.sellPrice(silver: 1);
    }
}

public sealed class PinkDiamond : MilkcraftMaterial
{
    public override void SetDefaults()
    {
        base.SetDefaults();
        Item.rare = ItemRarityID.Pink;
        Item.value = Item.sellPrice(silver: 1);
    }
}

public sealed class EnchantmentHeart : MilkcraftMaterial
{
    public override void SetDefaults()
    {
        base.SetDefaults();
        Item.rare = ItemRarityID.LightPurple;
        Item.value = Item.sellPrice(gold: 1);
    }
}

public abstract class ProgressionMaterial : MilkcraftMaterial
{
    protected virtual int Rarity => ItemRarityID.Orange;
    protected virtual int Value => Item.sellPrice(silver: 20);
    public override void SetDefaults()
    {
        base.SetDefaults();
        Item.rare = Rarity;
        Item.value = Value;
    }
}

public sealed class TruthSword : ProgressionMaterial { }
public sealed class TruthBow : ProgressionMaterial { }
public sealed class TruthStaff : ProgressionMaterial { }
public sealed class CrystalSkull : ProgressionMaterial { }
public sealed class AlienKeratin : ProgressionMaterial { }
public sealed class ArcaneCore : ProgressionMaterial { }
public sealed class BrokenSwordHilt : ProgressionMaterial { }
public sealed class BrokenBowstring : ProgressionMaterial { }
public sealed class BrokenStaffHandle : ProgressionMaterial { }
public sealed class WitherTear : ProgressionMaterial { }
public sealed class DemonWing : ProgressionMaterial { }
public sealed class EnchantedShinbone : ProgressionMaterial { }

public abstract class MilkcraftRecipePotion : MilkcraftMaterial
{
    public override void SetDefaults()
    {
        base.SetDefaults();
        Item.width = 20;
        Item.height = 26;
        Item.maxStack = Item.CommonMaxStack;
        Item.rare = ItemRarityID.Orange;
        Item.value = Item.sellPrice(silver: 50);
        // These are alchemical crafting ingredients, not drinkable buffs.
        Item.useStyle = ItemUseStyleID.None;
        Item.consumable = false;
        Item.buffType = 0;
    }
}

public sealed class TemplarPotion : MilkcraftRecipePotion { }
public sealed class NightmarePotion : MilkcraftRecipePotion { }
public sealed class FallenPotion : MilkcraftRecipePotion { }
public sealed class SoulforgedPotion : MilkcraftRecipePotion { }
public sealed class AbyssPotion : MilkcraftRecipePotion { }
public sealed class DarkPotion : MilkcraftRecipePotion { }
