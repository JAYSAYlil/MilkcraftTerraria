using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.Localization;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace MilkcraftTerraria.Content.Items;

public abstract class MilkcraftArmor : ModItem
{
    protected abstract int Defense { get; }
    public override string Texture => $"{Mod.Name}/Content/Items/{GetType().Name}";

    public override bool PreDrawInInventory(SpriteBatch spriteBatch, Vector2 position, Rectangle frame,
        Color drawColor, Color itemColor, Vector2 origin, float scale)
        => ArmorInventoryIconComposer.PreDrawInInventory(Item, spriteBatch, position, frame, drawColor, scale);

    public override bool PreDrawInWorld(SpriteBatch spriteBatch, Color lightColor, Color alphaColor,
        ref float rotation, ref float scale, int whoAmI)
        => ArmorInventoryIconComposer.PreDrawInWorld(Item, spriteBatch, alphaColor, ref rotation, ref scale);

    public override void ModifyTooltips(System.Collections.Generic.List<TooltipLine> tooltips)
    {
        string name = GetType().Name;
        string suit = name.StartsWith("Iron", System.StringComparison.Ordinal) ? "Iron"
            : name.StartsWith("Leather", System.StringComparison.Ordinal) ? "Leather"
            : name.StartsWith("Cloth", System.StringComparison.Ordinal) ? "Cloth"
            : name.StartsWith("Knight", System.StringComparison.Ordinal) ? "Knight"
            : name.StartsWith("Hunting", System.StringComparison.Ordinal) ? "Hunting"
            : name.StartsWith("Energy", System.StringComparison.Ordinal) ? "Energy" : "";
        if (suit.Length > 0) MilkcraftPassiveVfx.TintArmorTooltip(tooltips, Mod, suit);
    }

    public override void SetStaticDefaults()
    {
        if (Item.headSlot >= 0)
        {
            if (GetType().Name == "LeatherHeadband")
            {
                ArmorIDs.Head.Sets.DrawFullHair[Item.headSlot] = true;
                ArmorIDs.Head.Sets.DrawHatHair[Item.headSlot] = false;
                ArmorIDs.Head.Sets.DrawsBackHairWithoutHeadgear[Item.headSlot] = true;
            }
            else if (GetType().Name is "IronHelmet" or "KnightHelmet")
            {
                // The T1/T2 warrior helmets retain their original fit and
                // keep the native face visible below the brow.
                ArmorIDs.Head.Sets.DrawHead[Item.headSlot] = true;
                ArmorIDs.Head.Sets.DrawFullHair[Item.headSlot] = false;
                ArmorIDs.Head.Sets.DrawHatHair[Item.headSlot] = false;
            }
            else if (GetType().Name is "ClothHood" or "HuntingHood" or "EnergyHood")
            {
                // Restore the original 0.2.0 hood behavior: keep the native
                // face visible and draw hat hair over the hood opening.
                ArmorIDs.Head.Sets.DrawHatHair[Item.headSlot] = true;
            }
        }
        if (Item.bodySlot >= 0)
        {
            // The body atlas supplies segmented armor for both arms and the
            // gauntlets. Hide bare skin so no hand pixels bleed through its
            // transparent regions during native composite-arm poses.
            ArmorIDs.Body.Sets.HidesArms[Item.bodySlot] = true;
            ArmorIDs.Body.Sets.HidesHands[Item.bodySlot] = true;
            ArmorIDs.Body.Sets.DisableHandOnAndOffAccDraw[Item.bodySlot] = false;

            // Robe hem sheets are the native leg-equipment texture selected
            // by SetMatch. Hide bare leg skin behind their transparent sides;
            // the equipped shoe/boot layer remains independently drawable.
            if (GetType().Name is "ClothRobe" or "EnergyRobe")
                ArmorIDs.Body.Sets.HidesBottomSkin[Item.bodySlot] = true;
        }
    }

    public override void SetDefaults()
    {
        Item.width = 24;
        Item.height = 24;
        Item.defense = Defense;
        Item.rare = GetType().Name.StartsWith("Knight") || GetType().Name.StartsWith("Hunting") || GetType().Name.StartsWith("Energy")
            ? ItemRarityID.Orange
            : ItemRarityID.Blue;
        Item.value = Item.sellPrice(silver: 25);
    }

    public override void UpdateEquip(Player player)
    {
        switch (GetType().Name)
        {
            case "ClothHood":
            case "ClothRobe":
                player.statManaMax2 += 20;
                break;
            case "KnightHelmet":
                player.GetDamage(DamageClass.Melee) += 0.05f;
                break;
            case "KnightChestplate":
                player.GetCritChance(DamageClass.Melee) += 3f;
                break;
            case "HuntingHood":
                player.GetDamage(DamageClass.Ranged) += 0.05f;
                break;
            case "HuntingChestplate":
                player.GetCritChance(DamageClass.Ranged) += 3f;
                break;
            case "HuntingLeggings":
                player.moveSpeed += 0.05f;
                break;
            case "EnergyHood":
                player.statManaMax2 += 20;
                player.GetDamage(DamageClass.Magic) += 0.05f;
                break;
            case "EnergyRobe":
                player.statManaMax2 += 40;
                player.GetDamage(DamageClass.Magic) += 0.05f;
                break;
        }
    }

    public override bool IsArmorSet(Item head, Item body, Item legs)
    {
        string prefix = GetType().Name switch
        {
            "IronHelmet" or "IronChestplate" or "IronGreaves" => "Iron",
            "LeatherHeadband" or "LeatherTunic" or "LeatherLeggings" => "Leather",
            "ClothHood" or "ClothRobe" or "ClothLeggings" => "Cloth",
            "KnightHelmet" or "KnightChestplate" or "KnightGreaves" => "Knight",
            "HuntingHood" or "HuntingChestplate" or "HuntingLeggings" => "Hunting",
            "EnergyHood" or "EnergyRobe" or "EnergyLeggings" => "Energy",
            _ => string.Empty
        };

        return prefix.Length > 0
            && head.ModItem?.GetType().Name == prefix + (prefix == "Iron" ? "Helmet" : prefix == "Leather" ? "Headband" : prefix == "Cloth" ? "Hood" : prefix == "Knight" ? "Helmet" : prefix == "Hunting" ? "Hood" : "Hood")
            && body.ModItem?.GetType().Name == prefix + (prefix == "Iron" ? "Chestplate" : prefix == "Leather" ? "Tunic" : prefix == "Cloth" ? "Robe" : prefix == "Knight" ? "Chestplate" : prefix == "Hunting" ? "Chestplate" : "Robe")
            && legs.ModItem?.GetType().Name == prefix + (prefix == "Iron" ? "Greaves" : prefix == "Leather" ? "Leggings" : prefix == "Cloth" ? "Leggings" : prefix == "Knight" ? "Greaves" : prefix == "Hunting" ? "Leggings" : "Leggings");
    }

    public override void UpdateArmorSet(Player player)
    {
        switch (GetType().Name)
        {
            case "IronHelmet":
                player.setBonus = Language.GetTextValue("Mods.MilkcraftTerraria.SetBonuses.Iron");
                player.GetDamage(DamageClass.Melee) += 0.05f;
                break;
            case "LeatherHeadband":
                player.setBonus = Language.GetTextValue("Mods.MilkcraftTerraria.SetBonuses.Leather");
                player.GetDamage(DamageClass.Ranged) += 0.05f;
                player.moveSpeed += 0.05f;
                break;
            case "ClothHood":
                player.setBonus = Language.GetTextValue("Mods.MilkcraftTerraria.SetBonuses.Cloth");
                player.GetDamage(DamageClass.Magic) += 0.05f;
                player.manaCost -= 0.05f;
                break;
            case "KnightHelmet":
                player.setBonus = Language.GetTextValue("Mods.MilkcraftTerraria.SetBonuses.Knight");
                player.GetDamage(DamageClass.Melee) += 0.08f;
                player.endurance += 0.05f;
                break;
            case "HuntingHood":
                player.setBonus = Language.GetTextValue("Mods.MilkcraftTerraria.SetBonuses.Hunting");
                player.GetDamage(DamageClass.Ranged) += 0.08f;
                break;
            case "EnergyHood":
                player.setBonus = Language.GetTextValue("Mods.MilkcraftTerraria.SetBonuses.Energy");
                player.manaCost -= 0.10f;
                player.manaRegenBonus += 3;
                break;
        }
    }
}

[AutoloadEquip(EquipType.Head)] public sealed class IronHelmet : MilkcraftArmor { protected override int Defense => 3; }
[AutoloadEquip(EquipType.Body)] public sealed class IronChestplate : MilkcraftArmor { protected override int Defense => 5; }
[AutoloadEquip(EquipType.Legs)] public sealed class IronGreaves : MilkcraftArmor { protected override int Defense => 4; }
[AutoloadEquip(EquipType.Head)] public sealed class LeatherHeadband : MilkcraftArmor { protected override int Defense => 2; }
[AutoloadEquip(EquipType.Body)] public sealed class LeatherTunic : MilkcraftArmor { protected override int Defense => 4; }
[AutoloadEquip(EquipType.Legs)] public sealed class LeatherLeggings : MilkcraftArmor { protected override int Defense => 2; }
[AutoloadEquip(EquipType.Head)] public sealed class ClothHood : MilkcraftArmor { protected override int Defense => 2; }
[AutoloadEquip(EquipType.Body)] public sealed class ClothRobe : MilkcraftArmor
{
    protected override int Defense => 3;
    public override void Load()
    {
        if (Main.netMode != NetmodeID.Server)
            EquipLoader.AddEquipTexture(Mod, $"{Texture}_{EquipType.Legs}", EquipType.Legs, this);
    }
    public override void SetMatch(bool male, ref int equipSlot, ref bool robes)
    {
        robes = true;
        equipSlot = EquipLoader.GetEquipSlot(Mod, nameof(ClothRobe), EquipType.Legs);
    }
}
[AutoloadEquip(EquipType.Legs)] public sealed class ClothLeggings : MilkcraftArmor { protected override int Defense => 2; }
[AutoloadEquip(EquipType.Head)] public sealed class KnightHelmet : MilkcraftArmor { protected override int Defense => 6; }
[AutoloadEquip(EquipType.Body)] public sealed class KnightChestplate : MilkcraftArmor { protected override int Defense => 9; }
[AutoloadEquip(EquipType.Legs)] public sealed class KnightGreaves : MilkcraftArmor { protected override int Defense => 6; }
[AutoloadEquip(EquipType.Head)] public sealed class HuntingHood : MilkcraftArmor { protected override int Defense => 4; }
[AutoloadEquip(EquipType.Body)] public sealed class HuntingChestplate : MilkcraftArmor { protected override int Defense => 7; }
[AutoloadEquip(EquipType.Legs)] public sealed class HuntingLeggings : MilkcraftArmor { protected override int Defense => 5; }
[AutoloadEquip(EquipType.Head)] public sealed class EnergyHood : MilkcraftArmor { protected override int Defense => 4; }
[AutoloadEquip(EquipType.Body)] public sealed class EnergyRobe : MilkcraftArmor
{
    protected override int Defense => 6;
    public override void Load()
    {
        if (Main.netMode != NetmodeID.Server)
            EquipLoader.AddEquipTexture(Mod, $"{Texture}_{EquipType.Legs}", EquipType.Legs, this);
    }
    public override void SetMatch(bool male, ref int equipSlot, ref bool robes)
    {
        robes = true;
        equipSlot = EquipLoader.GetEquipSlot(Mod, nameof(EnergyRobe), EquipType.Legs);
    }
}
[AutoloadEquip(EquipType.Legs)] public sealed class EnergyLeggings : MilkcraftArmor { protected override int Defense => 4; }
