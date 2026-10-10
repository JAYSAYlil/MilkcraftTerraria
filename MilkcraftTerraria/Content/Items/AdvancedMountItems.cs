using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using MilkcraftTerraria.Content;

namespace MilkcraftTerraria.Content.Items;

public sealed class UndeadWarhorseSummon : ModItem
{
    public override void SetDefaults()
    {
        Item.width = 28;
        Item.height = 28;
        Item.rare = ItemRarityID.Yellow;
        Item.value = Item.sellPrice(gold: 2);
        Item.useStyle = ItemUseStyleID.HoldUp;
        Item.useTime = 20;
        Item.useAnimation = 20;
        Item.noUseGraphic = true;
        Item.noMelee = true;
        Item.mountType = ModContent.MountType<UndeadWarhorseMount>();
        Item.buffType = ModContent.BuffType<UndeadWarhorseMountBuff>();
    }
}

public sealed class CelestialTenguSummon : ModItem
{
    public override void SetDefaults()
    {
        Item.width = 28;
        Item.height = 28;
        Item.rare = ItemRarityID.Red;
        Item.value = Item.sellPrice(gold: 3);
        Item.useStyle = ItemUseStyleID.HoldUp;
        Item.useTime = 20;
        Item.useAnimation = 20;
        Item.noUseGraphic = true;
        Item.noMelee = true;
        Item.mountType = ModContent.MountType<CelestialTenguMount>();
        Item.buffType = ModContent.BuffType<CelestialTenguMountBuff>();
    }
}

public abstract class ExpansionMountSummon : ModItem
{
    protected abstract int MountType { get; }
    protected abstract int BuffType { get; }
    protected abstract int Rarity { get; }
    public override void SetDefaults()
    {
        Item.width = 28;
        Item.height = 28;
        Item.rare = Rarity;
        Item.value = Item.sellPrice(gold: 2);
        Item.useStyle = ItemUseStyleID.HoldUp;
        Item.useTime = 20;
        Item.useAnimation = 20;
        Item.noUseGraphic = true;
        Item.noMelee = true;
        Item.mountType = MountType;
        Item.buffType = BuffType;
    }
}

public sealed class WhiteTigerSummon : ExpansionMountSummon
{
    public override string Texture => $"{Mod.Name}/Content/Items/WhiteTigerSummon";
    protected override int MountType => ModContent.MountType<WhiteTigerMount>();
    protected override int BuffType => ModContent.BuffType<WhiteTigerMountBuff>();
    protected override int Rarity => ItemRarityID.Orange;
}

public sealed class WhiteNineTailedFoxSummon : ExpansionMountSummon
{
    public override string Texture => $"{Mod.Name}/Content/Items/WhiteNineTailedFoxSummon";
    protected override int MountType => ModContent.MountType<WhiteNineTailedFoxMount>();
    protected override int BuffType => ModContent.BuffType<WhiteNineTailedFoxMountBuff>();
    protected override int Rarity => ItemRarityID.Yellow;
}

public sealed class RhinoSummon : ExpansionMountSummon
{
    public override string Texture => $"{Mod.Name}/Content/Items/RhinoSummon";
    protected override int MountType => ModContent.MountType<RhinoMount>();
    protected override int BuffType => ModContent.BuffType<RhinoMountBuff>();
    protected override int Rarity => ItemRarityID.Yellow;
}

public sealed class FlameWarhorseSummon : ExpansionMountSummon
{
    public override string Texture => $"{Mod.Name}/Content/Items/FlameWarhorseSummon";
    protected override int MountType => ModContent.MountType<FlameWarhorseMount>();
    protected override int BuffType => ModContent.BuffType<FlameWarhorseMountBuff>();
    protected override int Rarity => ItemRarityID.Yellow;
}

public sealed class WhiteUnicornSummon : ExpansionMountSummon
{
    public override string Texture => $"{Mod.Name}/Content/Items/WhiteUnicornSummon";
    protected override int MountType => ModContent.MountType<WhiteUnicornMount>();
    protected override int BuffType => ModContent.BuffType<WhiteUnicornMountBuff>();
    protected override int Rarity => ItemRarityID.Red;
}

public sealed class WhiteDodoSummon : ExpansionMountSummon
{
    public override string Texture => $"{Mod.Name}/Content/Items/WhiteDodoSummon";
    protected override int MountType => ModContent.MountType<WhiteDodoMount>();
    protected override int BuffType => ModContent.BuffType<WhiteDodoMountBuff>();
    protected override int Rarity => ItemRarityID.Pink;
}
