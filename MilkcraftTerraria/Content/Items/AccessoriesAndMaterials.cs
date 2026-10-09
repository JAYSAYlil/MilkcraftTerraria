using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace MilkcraftTerraria.Content.Items;

public abstract class MilkcraftAccessory : ModItem
{
    protected abstract string Family { get; }
    public override string Texture => $"{Mod.Name}/Content/Items/{GetType().Name}";

    public override void ModifyTooltips(System.Collections.Generic.List<TooltipLine> tooltips)
        => MilkcraftPassiveVfx.TintAccessoryTooltip(tooltips, Mod, GetType().Name);

    public override void SetDefaults()
    {
        Item.width = 24;
        Item.height = 24;
        Item.accessory = true;
        Item.rare = GetType().Name.StartsWith("Knight") || GetType().Name.StartsWith("Hunting") || GetType().Name.StartsWith("Energy")
            ? ItemRarityID.Orange
            : ItemRarityID.Blue;
        Item.value = Item.sellPrice(silver: 40);
    }

    public override bool CanAccessoryBeEquippedWith(Item equippedItem, Item incomingItem, Player player)
    {
        return equippedItem.ModItem is not MilkcraftAccessory current
            || incomingItem.ModItem is not MilkcraftAccessory incoming
            || current.Family != incoming.Family;
    }

    public override void UpdateAccessory(Player player, bool hideVisual)
    {
        player.GetModPlayer<MilkcraftPassivePlayer>().RegisterFunctionalAccessory(GetType().Name);
        switch (GetType().Name)
        {
            case "IronShield": player.statDefense += 3; break;
            case "IronQuiver": player.GetDamage(DamageClass.Ranged) += 0.05f; break;
            case "KnightBulwark": player.statDefense += 4; player.noKnockback = true; player.endurance += 0.03f; break;
            case "HuntingQuiver":
                player.GetDamage(DamageClass.Ranged) += 0.08f;
                player.ammoCost80 = true;
                break;
            case "BeginnerTome":
                player.GetDamage(DamageClass.Magic) += 0.04f;
                player.statManaMax2 += 20; player.manaCost -= 0.03f;
                break;
            case "EnergyTome":
                player.statManaMax2 += 40;
                player.GetDamage(DamageClass.Magic) += 0.08f;
                player.manaCost -= 0.06f;
                break;
        }
    }

    public override void AddRecipes() => MilkcraftRecipes.AddGearRecipe(this);
}

[AutoloadEquip(EquipType.Shield)] public sealed class IronShield : MilkcraftAccessory { protected override string Family => "shield"; }
[AutoloadEquip(EquipType.Back)] public sealed class IronQuiver : MilkcraftAccessory { protected override string Family => "quiver"; }
[AutoloadEquip(EquipType.Waist)] public sealed class BeginnerTome : MilkcraftAccessory { protected override string Family => "tome"; }
[AutoloadEquip(EquipType.Shield)] public sealed class KnightBulwark : MilkcraftAccessory { protected override string Family => "shield"; }
[AutoloadEquip(EquipType.Back)] public sealed class HuntingQuiver : MilkcraftAccessory { protected override string Family => "quiver"; }
[AutoloadEquip(EquipType.Waist)] public sealed class EnergyTome : MilkcraftAccessory { protected override string Family => "tome"; }

public abstract class MilkcraftMaterial : ModItem
{
    public override string Texture => $"{Mod.Name}/Content/Items/{GetType().Name}";
    public override void SetDefaults()
    {
        Item.width = 20;
        Item.height = 20;
        Item.maxStack = Item.CommonMaxStack;
        Item.value = Item.sellPrice(copper: 5);
        Item.rare = ItemRarityID.White;
    }

    public override void AddRecipes() => MilkcraftRecipes.AddMaterialRecipe(this);
}

public sealed class CoarseWool : MilkcraftMaterial { }
public sealed class BlackIronBar : MilkcraftMaterial { }
public sealed class Alcohol : MilkcraftMaterial
{
    public override void SetDefaults()
    {
        base.SetDefaults();
        Item.useStyle = ItemUseStyleID.HoldUp;
        Item.useTime = 20;
        Item.useAnimation = 20;
        Item.noMelee = true;
        Item.consumable = false; // The server consumes it only after validating a targeted alchemy table.
        Item.autoReuse = false;
    }

    public override bool CanUseItem(Player player)
    {
        if (player is null || !player.active || player.dead || player.selectedItem < 0
            || player.selectedItem >= player.inventory.Length
            || player.inventory[player.selectedItem].type != Type
            || player.inventory[player.selectedItem].stack <= 0)
            return false;

        Point target = Terraria.Main.MouseWorld.ToTileCoordinates();
        return MilkcraftAlchemyWorldSystem.CanUseAlcoholOnTable(player, target.X, target.Y);
    }

    public override bool? UseItem(Player player)
    {
        Point target = Terraria.Main.MouseWorld.ToTileCoordinates();
        return MilkcraftAlchemyWorldSystem.TryActivateFromAlcoholUse(player, target.X, target.Y);
    }
}
public sealed class MagicIronBar : MilkcraftMaterial { }
public sealed class WildLeather : MilkcraftMaterial { }
public sealed class MagicWool : MilkcraftMaterial { }

