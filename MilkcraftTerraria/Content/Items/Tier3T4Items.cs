using System;
using System.IO;
using System.Reflection;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoMod.RuntimeDetour;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;

namespace MilkcraftTerraria.Content.Items;

public enum LateTierWeaponKind { Sword, Bow, Staff }

/// <summary>New armor content is isolated here so the accepted T1/T2 classes remain unchanged.</summary>
public abstract class Tier3T4ArmorItem : ModItem
{
    public abstract int Tier { get; }
    protected abstract string Suit { get; }
    protected abstract int Defense { get; }
    public override string Texture => $"{Mod.Name}/Content/Items/{GetType().Name}";

    public override bool PreDrawInInventory(SpriteBatch spriteBatch, Vector2 position, Rectangle frame,
        Color drawColor, Color itemColor, Vector2 origin, float scale)
        => ArmorInventoryIconComposer.PreDrawInInventory(Item, spriteBatch, position, frame, drawColor, scale);

    public override bool PreDrawInWorld(SpriteBatch spriteBatch, Color lightColor, Color alphaColor,
        ref float rotation, ref float scale, int whoAmI)
        => ArmorInventoryIconComposer.PreDrawInWorld(Item, spriteBatch, alphaColor, ref rotation, ref scale);

    public override void ModifyTooltips(System.Collections.Generic.List<TooltipLine> tooltips)
        => MilkcraftPassiveVfx.TintArmorTooltip(tooltips, Mod, Suit);

    public override void SetStaticDefaults()
    {
        if (Item.bodySlot < 0)
            return;

        // The body atlas contains segmented sleeves and gauntlets. Hide the
        // native skin layers so uncovered UV pixels cannot show bare hands.
        ArmorIDs.Body.Sets.HidesArms[Item.bodySlot] = true;
        ArmorIDs.Body.Sets.HidesHands[Item.bodySlot] = true;
        ArmorIDs.Body.Sets.DisableHandOnAndOffAccDraw[Item.bodySlot] = false;

        // Keep the two new robe sets consistent with the accepted T1/T2
        // robe layering: transparent hem pixels reveal boots, not bare skin.
        if (GetType().Name is "FallenRobe" or "DarkRobe")
        {
            ArmorIDs.Body.Sets.HidesBottomSkin[Item.bodySlot] = true;
        }
    }

    public override void SetDefaults()
    {
        Item.width = 24;
        Item.height = 24;
        Item.defense = Defense;
        Item.rare = Tier == 3 ? ItemRarityID.Yellow : ItemRarityID.Lime;
        Item.value = Item.sellPrice(gold: Tier == 3 ? 1 : 2);
    }

    private bool Licensed(Player player) => player.GetModPlayer<Tier3T4ProgressPlayer>().HasLicense(Tier);

    public override void UpdateEquip(Player player)
    {
        if (!Licensed(player)) return;

        switch (GetType().Name)
        {
            case "TemplarHelmet": player.GetDamage(DamageClass.Melee) += 0.04f; break;
            case "TemplarChestplate": player.GetDamage(DamageClass.Melee) += 0.08f; break;
            case "TemplarGreaves": player.GetDamage(DamageClass.Melee) += 0.04f; break;
            case "NightmareHood": player.GetDamage(DamageClass.Ranged) += 0.04f; break;
            case "NightmareChestplate": player.GetDamage(DamageClass.Ranged) += 0.08f; break;
            case "NightmareLeggings": player.GetDamage(DamageClass.Ranged) += 0.04f; break;
            case "FallenHood": player.GetDamage(DamageClass.Magic) += 0.05f; player.statManaMax2 += 20; break;
            case "FallenRobe": player.GetDamage(DamageClass.Magic) += 0.07f; player.statManaMax2 += 20; break;
            case "FallenLeggings": player.GetDamage(DamageClass.Magic) += 0.04f; player.statManaMax2 += 20; break;
            case "SoulforgedHelmet": player.GetDamage(DamageClass.Melee) += 0.06f; break;
            case "SoulforgedChestplate": player.GetDamage(DamageClass.Melee) += 0.10f; break;
            case "SoulforgedGreaves": player.GetDamage(DamageClass.Melee) += 0.05f; break;
            case "AbyssHood": player.GetDamage(DamageClass.Ranged) += 0.06f; break;
            case "AbyssChestplate": player.GetDamage(DamageClass.Ranged) += 0.10f; break;
            case "AbyssLeggings": player.GetDamage(DamageClass.Ranged) += 0.05f; break;
            case "DarkHood": player.GetDamage(DamageClass.Magic) += 0.06f; player.statManaMax2 += 30; break;
            case "DarkRobe": player.GetDamage(DamageClass.Magic) += 0.10f; player.statManaMax2 += 25; break;
            case "DarkLeggings": player.GetDamage(DamageClass.Magic) += 0.05f; player.statManaMax2 += 25; break;
        }

        // Small stage-appropriate improvements authorized for the 0.2.10
        // balance pass. These are intrinsic piece bonuses and do not alter
        // armor defense or full-set passives.
        switch (GetType().Name)
        {
            case "TemplarHelmet": player.GetCritChance(DamageClass.Melee) += 8f; break;
            case "NightmareHood": player.GetCritChance(DamageClass.Ranged) += 10f; break;
            case "FallenHood": player.GetCritChance(DamageClass.Magic) += 10f; break;
            case "SoulforgedHelmet": player.GetCritChance(DamageClass.Melee) += 8f; break;
            case "AbyssHood": player.GetCritChance(DamageClass.Ranged) += 12f; break;
            case "DarkHood": player.GetCritChance(DamageClass.Magic) += 12f; break;
            case "TemplarGreaves": player.GetAttackSpeed(DamageClass.Melee) += 0.06f; break;
            case "NightmareLeggings": player.GetAttackSpeed(DamageClass.Ranged) += 0.06f; break;
            case "FallenLeggings": player.GetAttackSpeed(DamageClass.Magic) += 0.06f; break;
            case "SoulforgedGreaves": player.GetAttackSpeed(DamageClass.Melee) += 0.06f; break;
            case "AbyssLeggings": player.GetAttackSpeed(DamageClass.Ranged) += 0.06f; break;
            case "DarkLeggings": player.GetAttackSpeed(DamageClass.Magic) += 0.08f; break;
        }
    }

    private static (string Head, string Body, string Legs) Parts(string suit) => suit switch
    {
        "Templar" => ("TemplarHelmet", "TemplarChestplate", "TemplarGreaves"),
        "Nightmare" => ("NightmareHood", "NightmareChestplate", "NightmareLeggings"),
        "Fallen" => ("FallenHood", "FallenRobe", "FallenLeggings"),
        "Soulforged" => ("SoulforgedHelmet", "SoulforgedChestplate", "SoulforgedGreaves"),
        "Abyss" => ("AbyssHood", "AbyssChestplate", "AbyssLeggings"),
        "Dark" => ("DarkHood", "DarkRobe", "DarkLeggings"),
        _ => default
    };

    public override bool IsArmorSet(Item head, Item body, Item legs)
    {
        var parts = Parts(Suit);
        return head.ModItem?.Name == parts.Head && body.ModItem?.Name == parts.Body && legs.ModItem?.Name == parts.Legs;
    }

    public override void UpdateArmorSet(Player player)
    {
        if (!Licensed(player) || GetType().Name != Parts(Suit).Head) return;

        player.setBonus = Language.GetTextValue($"Mods.{Mod.Name}.SetBonuses.{Suit}");
        switch (Suit)
        {
            case "Templar": player.GetDamage(DamageClass.Melee) += 0.02f; break;
            case "Nightmare": player.GetDamage(DamageClass.Ranged) += 0.02f; break;
            case "Fallen": player.GetDamage(DamageClass.Magic) += 0.02f; player.manaCost -= 0.10f; break;
            case "Soulforged": player.GetDamage(DamageClass.Melee) += 0.04f; player.endurance += 0.08f; break;
            case "Abyss": player.GetDamage(DamageClass.Ranged) += 0.04f; break;
            case "Dark": player.GetDamage(DamageClass.Magic) += 0.04f; player.manaCost -= 0.10f; break;
        }
    }
}

[AutoloadEquip(EquipType.Head)] public sealed class TemplarHelmet : Tier3T4ArmorItem { public override int Tier => 3; protected override string Suit => "Templar"; protected override int Defense => 12; }
[AutoloadEquip(EquipType.Body)] public sealed class TemplarChestplate : Tier3T4ArmorItem { public override int Tier => 3; protected override string Suit => "Templar"; protected override int Defense => 20; }
[AutoloadEquip(EquipType.Legs)] public sealed class TemplarGreaves : Tier3T4ArmorItem { public override int Tier => 3; protected override string Suit => "Templar"; protected override int Defense => 18; }
[AutoloadEquip(EquipType.Head)] public sealed class NightmareHood : Tier3T4ArmorItem { public override int Tier => 3; protected override string Suit => "Nightmare"; protected override int Defense => 9; }
[AutoloadEquip(EquipType.Body)] public sealed class NightmareChestplate : Tier3T4ArmorItem { public override int Tier => 3; protected override string Suit => "Nightmare"; protected override int Defense => 16; }
[AutoloadEquip(EquipType.Legs)] public sealed class NightmareLeggings : Tier3T4ArmorItem { public override int Tier => 3; protected override string Suit => "Nightmare"; protected override int Defense => 10; }
[AutoloadEquip(EquipType.Head)] public sealed class FallenHood : Tier3T4ArmorItem { public override int Tier => 3; protected override string Suit => "Fallen"; protected override int Defense => 7; }
[AutoloadEquip(EquipType.Body)] public sealed class FallenRobe : Tier3T4ArmorItem
{
    public override int Tier => 3;
    protected override string Suit => "Fallen";
    protected override int Defense => 14;
    public override void Load() { if (!Main.dedServ) EquipLoader.AddEquipTexture(Mod, $"{Texture}_{EquipType.Legs}", EquipType.Legs, this); }
    public override void SetMatch(bool male, ref int equipSlot, ref bool robes) { robes = true; equipSlot = EquipLoader.GetEquipSlot(Mod, nameof(FallenRobe), EquipType.Legs); }
}
[AutoloadEquip(EquipType.Legs)] public sealed class FallenLeggings : Tier3T4ArmorItem { public override int Tier => 3; protected override string Suit => "Fallen"; protected override int Defense => 10; }
[AutoloadEquip(EquipType.Head)] public sealed class SoulforgedHelmet : Tier3T4ArmorItem { public override int Tier => 4; protected override string Suit => "Soulforged"; protected override int Defense => 23; }
[AutoloadEquip(EquipType.Body)] public sealed class SoulforgedChestplate : Tier3T4ArmorItem { public override int Tier => 4; protected override string Suit => "Soulforged"; protected override int Defense => 32; }
[AutoloadEquip(EquipType.Legs)] public sealed class SoulforgedGreaves : Tier3T4ArmorItem { public override int Tier => 4; protected override string Suit => "Soulforged"; protected override int Defense => 18; }
[AutoloadEquip(EquipType.Head)] public sealed class AbyssHood : Tier3T4ArmorItem { public override int Tier => 4; protected override string Suit => "Abyss"; protected override int Defense => 11; }
[AutoloadEquip(EquipType.Body)] public sealed class AbyssChestplate : Tier3T4ArmorItem { public override int Tier => 4; protected override string Suit => "Abyss"; protected override int Defense => 24; }
[AutoloadEquip(EquipType.Legs)] public sealed class AbyssLeggings : Tier3T4ArmorItem { public override int Tier => 4; protected override string Suit => "Abyss"; protected override int Defense => 16; }
[AutoloadEquip(EquipType.Head)] public sealed class DarkHood : Tier3T4ArmorItem { public override int Tier => 4; protected override string Suit => "Dark"; protected override int Defense => 12; }
[AutoloadEquip(EquipType.Body)] public sealed class DarkRobe : Tier3T4ArmorItem
{
    public override int Tier => 4;
    protected override string Suit => "Dark";
    protected override int Defense => 14;
    public override void Load() { if (!Main.dedServ) EquipLoader.AddEquipTexture(Mod, $"{Texture}_{EquipType.Legs}", EquipType.Legs, this); }
    public override void SetMatch(bool male, ref int equipSlot, ref bool robes) { robes = true; equipSlot = EquipLoader.GetEquipSlot(Mod, nameof(DarkRobe), EquipType.Legs); }
}
[AutoloadEquip(EquipType.Legs)] public sealed class DarkLeggings : Tier3T4ArmorItem { public override int Tier => 4; protected override string Suit => "Dark"; protected override int Defense => 16; }

public abstract class Tier3T4WeaponItem : ModItem
{
    protected abstract int Tier { get; }
    protected abstract LateTierWeaponKind Kind { get; }
    protected abstract int Damage { get; }
    protected abstract int UseTime { get; }
    protected abstract float Knockback { get; }
    protected virtual float ShootSpeed => 0f;
    protected virtual int Mana => 0;
    public override string Texture => $"{Mod.Name}/Content/Items/{GetType().Name}";

    public override void SetStaticDefaults()
    {
        if (GetType() == typeof(SoulforgedSword))
            ItemID.Sets.BonusAttackSpeedMultiplier[Type] =
                ItemID.Sets.BonusAttackSpeedMultiplier[ItemID.TerraBlade];
    }

    public override void SetDefaults()
    {
        Item.width = Kind == LateTierWeaponKind.Sword ? 34 : Kind == LateTierWeaponKind.Bow ? 24 : 26;
        Item.height = Kind == LateTierWeaponKind.Sword ? 42 : Kind == LateTierWeaponKind.Bow ? 46 : 50;
        Item.damage = Damage;
        Item.knockBack = Knockback;
        Item.useTime = UseTime;
        Item.useAnimation = UseTime;
        Item.autoReuse = true;
        Item.value = Item.sellPrice(gold: Tier == 3 ? 1 : 2);
        Item.rare = Tier == 3 ? ItemRarityID.Yellow : ItemRarityID.Lime;
        switch (Kind)
        {
            case LateTierWeaponKind.Sword:
                Item.DamageType = DamageClass.Melee;
                Item.useStyle = ItemUseStyleID.Swing;
                Item.UseSound = SoundID.Item1;
                break;
            case LateTierWeaponKind.Bow:
                Item.DamageType = DamageClass.Ranged;
                Item.useStyle = ItemUseStyleID.Shoot;
                Item.useAmmo = AmmoID.Arrow;
                Item.shoot = ProjectileID.WoodenArrowFriendly;
                Item.shootSpeed = ShootSpeed;
                Item.noMelee = true;
                Item.UseSound = SoundID.Item5;
                break;
            default:
                Item.DamageType = DamageClass.Magic;
                Item.useStyle = ItemUseStyleID.Shoot;
                Item.noMelee = true;
                Item.staff[Type] = true;
                Item.shoot = Tier == 3
                    ? ModContent.ProjectileType<FallenBolt>()
                    : ModContent.ProjectileType<DarkBolt>();
                Item.shootSpeed = ShootSpeed;
                Item.mana = Mana;
                Item.UseSound = SoundID.Item20;
                break;
        }

        if (Kind == LateTierWeaponKind.Sword)
        {
            Item.shoot = Tier == 3
                ? ModContent.ProjectileType<TemplarWave>()
                : ModContent.ProjectileType<SoulforgedWave>();
            Item.shootSpeed = ShootSpeed;
        }

        if (GetType() == typeof(SoulforgedSword))
        {
            // Read cadence and launch speed from this installed Terraria build.
            Item terraBlade = new();
            terraBlade.SetDefaults(ItemID.TerraBlade);
            Item.useTime = terraBlade.useTime;
            Item.useAnimation = terraBlade.useAnimation;
            Item.shootSpeed = terraBlade.shootSpeed;
            Item.autoReuse = terraBlade.autoReuse;
            Item.reuseDelay = terraBlade.reuseDelay;
            Item.useTurn = terraBlade.useTurn;
            Item.attackSpeedOnlyAffectsWeaponAnimation = terraBlade.attackSpeedOnlyAffectsWeaponAnimation;
        }
    }

    public override Vector2? HoldoutOrigin() => null;

    public override bool CanUseItem(Player player) => player.GetModPlayer<Tier3T4ProgressPlayer>().HasLicense(Tier);

    public override bool ModifyItemDraw(ref PlayerDrawSet drawInfo, ref DrawData drawData,
        ref DrawData? coloredDrawData, ref DrawData? glowMaskDrawData)
    {
        string weaponName = GetType().Name;
        if (weaponName is "TemplarSword" or "SoulforgedSword" or "FallenStaff" or "DarkStaff")
        {
            (float x, float y) = weaponName switch
            {
                "TemplarSword" => (14f, 52f),
                "SoulforgedSword" => (14f, 55f),
                "FallenStaff" => (17f, 51f),
                _ => (14f, 51f)
            };
            if (drawInfo.drawPlayer.direction < 0)
                x = drawData.texture.Width - x;
            void ApplyGrip(ref DrawData data)
            {
                // Both swords share the same native diagonal presentation.
                // In particular, Soulforged is symmetric: do not treat an
                // icon perspective as a reason to reflect its long axis.
                data.origin = new Vector2(x, y);
            }

            ApplyGrip(ref drawData);
            if (SmokeSystem.ForcePreviewLighting && drawInfo.drawPlayer.whoAmI == Main.myPlayer)
                SmokeSystem.LogGripOriginDraw(weaponName, drawInfo, drawData);
            if (coloredDrawData.HasValue)
            {
                DrawData colored = coloredDrawData.Value;
                ApplyGrip(ref colored);
                coloredDrawData = colored;
            }
            if (glowMaskDrawData.HasValue)
            {
                DrawData glow = glowMaskDrawData.Value;
                ApplyGrip(ref glow);
                glowMaskDrawData = glow;
            }
        }

        if (Kind != LateTierWeaponKind.Bow)
            return true;

        // The T3/T4 source art has the string on the left and bow limbs on
        // the right. Normalize the horizontal effect explicitly: right-facing
        // keeps the source orientation, left-facing mirrors it toward the
        // player. Preserve any vertical effect supplied by the engine.
        SpriteEffects nativeBowEffect = drawData.effect;
        bool desiredFlipX = drawInfo.drawPlayer.direction < 0;
        void AimBowStringTowardPlayer(ref DrawData data)
        {
            bool actualFlipX = (data.effect & SpriteEffects.FlipHorizontally) != 0;
            if (actualFlipX != desiredFlipX)
                data.effect ^= SpriteEffects.FlipHorizontally;
        }
        AimBowStringTowardPlayer(ref drawData);
        if (coloredDrawData.HasValue)
        {
            DrawData colored = coloredDrawData.Value;
            AimBowStringTowardPlayer(ref colored);
            coloredDrawData = colored;
        }
        if (glowMaskDrawData.HasValue)
        {
            DrawData glow = glowMaskDrawData.Value;
            AimBowStringTowardPlayer(ref glow);
            glowMaskDrawData = glow;
        }
        if (SmokeSystem.ForcePreviewLighting && drawInfo.drawPlayer.whoAmI == Main.myPlayer)
            SmokeSystem.LogTier3T4BowDraw(GetType().Name, drawInfo, drawData, nativeBowEffect);
        return true;
    }

    public override void ModifyShootStats(Player player, ref Vector2 position, ref Vector2 velocity,
        ref int type, ref int damage, ref float knockback)
    {
        // Keep all non-wooden ammo projectiles untouched.
        if (Kind == LateTierWeaponKind.Bow && type == ProjectileID.WoodenArrowFriendly)
            type = ModContent.ProjectileType<HuntingArrow>();
    }

    public override bool Shoot(Player player, EntitySource_ItemUse_WithAmmo source, Vector2 position,
        Vector2 velocity, int type, int damage, float knockback)
    {
        if (GetType().Name == "AbyssBow")
        {
            // One ItemCheck callback consumes exactly one ammo item. Split the
            // shot here so both projectiles retain the real ammo projectile
            // type, source, damage, knockback, and downstream ammo behavior.
            Vector2 left = velocity.RotatedBy(-0.022f);
            Vector2 right = velocity.RotatedBy(0.022f);
            Projectile.NewProjectile(source, position, left, type, damage, knockback, player.whoAmI);
            Projectile.NewProjectile(source, position, right, type, damage, knockback, player.whoAmI);
            return false;
        }
        if (Kind != LateTierWeaponKind.Sword)
            return base.Shoot(player, source, position, velocity, type, damage, knockback);

        // One engine Shoot callback corresponds to one swing animation. Spawn
        // a single tier-specific wave and suppress the default duplicate.
        float fraction = Tier == 3 ? 0.70f : GetType().Name == "SoulforgedSword" ? 0.85f : 0.72f;
        int waveDamage = Math.Max(1, (int)MathF.Round(damage * fraction));
        if (GetType().Name == "SoulforgedSword")
        {
            // ItemCheck_Shoot has a special TerraBlade branch which supplies
            // these AI inputs to ProjectileID.TerraBlade2Shot (985). Preserve
            // the native attack-frame and slash variant data on our projectile
            // so its native AIStyle 191 path receives the same state.
            // Native ItemCheck supplies AI velocity at five times the sword's
            // configured shootSpeed. At attackSpeed boosts the custom Shoot
            // callback receives an already-scaled aim vector, while the
            // native TerraBlade special branch keeps its 5x base projectile
            // speed. Copy that branch's fixed magnitude and retain its aim.
            Vector2 nativeVelocity = velocity.SafeNormalize(Vector2.UnitX) * Item.shootSpeed * 5f;
            Projectile.NewProjectile(source, position, nativeVelocity, type, waveDamage, knockback,
                player.whoAmI, player.direction, Item.useAnimation, 1f);
        }
        else
            Projectile.NewProjectile(source, position, velocity, type, waveDamage, knockback, player.whoAmI);
        return false;
    }
}

public sealed class TemplarSword : Tier3T4WeaponItem { protected override int Tier => 3; protected override LateTierWeaponKind Kind => LateTierWeaponKind.Sword; protected override int Damage => 72; protected override int UseTime => 24; protected override float Knockback => 5f; protected override float ShootSpeed => 10.5f; }
public sealed class NightmareBow : Tier3T4WeaponItem { protected override int Tier => 3; protected override LateTierWeaponKind Kind => LateTierWeaponKind.Bow; protected override int Damage => 52; protected override int UseTime => 17; protected override float Knockback => 3f; protected override float ShootSpeed => 10f; }
public sealed class FallenStaff : Tier3T4WeaponItem { protected override int Tier => 3; protected override LateTierWeaponKind Kind => LateTierWeaponKind.Staff; protected override int Damage => 64; protected override int UseTime => 20; protected override float Knockback => 3f; protected override float ShootSpeed => 10f; protected override int Mana => 10; }
public sealed class SoulforgedSword : Tier3T4WeaponItem { protected override int Tier => 4; protected override LateTierWeaponKind Kind => LateTierWeaponKind.Sword; protected override int Damage => 110; protected override int UseTime => 27; protected override float Knockback => 6f; protected override float ShootSpeed => 12f; }
public sealed class AbyssBow : Tier3T4WeaponItem { protected override int Tier => 4; protected override LateTierWeaponKind Kind => LateTierWeaponKind.Bow; protected override int Damage => 65; protected override int UseTime => 18; protected override float Knockback => 4f; protected override float ShootSpeed => 12f; }
public sealed class DarkStaff : Tier3T4WeaponItem { protected override int Tier => 4; protected override LateTierWeaponKind Kind => LateTierWeaponKind.Staff; protected override int Damage => 100; protected override int UseTime => 20; protected override float Knockback => 4f; protected override float ShootSpeed => 12f; protected override int Mana => 14; }

public abstract class Tier3T4SwordWave : ModProjectile
{
    private bool[] _soulDustWasActive;
    private int[] _soulDustTypeBefore;
    protected abstract int WaveWidth { get; }
    protected abstract int WaveHeight { get; }
    protected abstract int Penetration { get; }
    protected virtual int WaveTimeLeft => 48;
    protected abstract float LightLevel { get; }
    protected abstract Vector3 GlowColor { get; }
    protected abstract Vector3 GlowAccentColor { get; }
    protected virtual bool IsTemplarHoming => false;
    protected virtual bool IsSoulforgedCrescent => false;
    public override string Texture => $"{Mod.Name}/Content/Items/{GetType().Name}";

    public override void SetStaticDefaults()
    {
        ProjectileID.Sets.TrailCacheLength[Type] = 8;
        ProjectileID.Sets.TrailingMode[Type] = 2;
    }

    public override void SetDefaults()
    {
        if (IsSoulforgedCrescent)
        {
            Projectile.CloneDefaults(ProjectileID.TerraBlade2Shot);
            AIType = ProjectileID.TerraBlade2Shot;
            if (!Main.dedServ)
            {
                _soulDustWasActive = new bool[Main.maxDust];
                _soulDustTypeBefore = new int[Main.maxDust];
            }
        }
        Projectile.width = WaveWidth;
        Projectile.height = WaveHeight;
        Projectile.friendly = true;
        Projectile.hostile = false;
        Projectile.DamageType = DamageClass.Melee;
        Projectile.timeLeft = WaveTimeLeft;
        Projectile.penetrate = Penetration;
        Projectile.tileCollide = true;
        Projectile.ignoreWater = true;
        if (!IsSoulforgedCrescent)
        {
            Projectile.ownerHitCheck = true;
            Projectile.usesLocalNPCImmunity = true;
            Projectile.localNPCHitCooldown = 16;
        }
        Projectile.light = 0f;
        Projectile.aiStyle = IsSoulforgedCrescent ? 191 : -1;
    }

    public override bool PreAI()
    {
        if (IsSoulforgedCrescent && !Main.dedServ)
        {
            SoulforgedNativeColorScope.Begin(Projectile);
            if (_soulDustWasActive is not null && _soulDustTypeBefore is not null)
            {
                int count = Math.Min(Main.dust.Length, _soulDustWasActive.Length);
                for (int i = 0; i < count; i++)
                {
                    Dust dust = Main.dust[i];
                    _soulDustWasActive[i] = dust.active;
                    _soulDustTypeBefore[i] = dust.type;
                }
            }
        }
        return true;
    }

    public override void PostAI()
    {
        if (!IsSoulforgedCrescent || Main.dedServ)
            return;

        try
        {
            // AIStyle 191 creates DustID 107 (green torch) directly. Recolor
            // only dust slots that became active during this projectile's
            // own native AI call, leaving every other player's particles alone.
            if (_soulDustWasActive is not null && _soulDustTypeBefore is not null)
            {
                int count = Math.Min(Main.dust.Length, _soulDustWasActive.Length);
                Color[] replacements = { new(210, 52, 48), new(227, 175, 54) };
                int recolored = 0;
                for (int i = 0; i < count; i++)
                {
                    Dust dust = Main.dust[i];
                    if (!dust.active || dust.type != 107 ||
                        (_soulDustWasActive[i] && _soulDustTypeBefore[i] == dust.type))
                        continue;

                    Color tint = replacements[(Projectile.numUpdates + recolored++) & 1];
                    Vector2 position = dust.position;
                    Vector2 velocity = dust.velocity;
                    float scale = dust.scale;
                    float rotation = dust.rotation;
                    float fadeIn = dust.fadeIn;
                    int alpha = dust.alpha;
                    bool noGravity = dust.noGravity;
                    dust.active = false;

                    // DustID 107 has a permanently green sprite, so changing
                    // only Dust.color still leaves visible green pixels. Make
                    // a real frame-selected GoldFlame with the native particle
                    // motion/size/opacity, then retire the green sprite.
                    Dust replacementDust = Dust.NewDustPerfect(position, DustID.GoldFlame,
                        velocity, alpha, tint, scale);
                    replacementDust.rotation = rotation;
                    replacementDust.fadeIn = fadeIn;
                    replacementDust.noGravity = noGravity;
                    replacementDust.noLight = true;
                    replacementDust.noLightEmittence = true;
                }
            }
        }
        finally
        {
            SoulforgedNativeColorScope.End(Projectile);
        }
    }

    public override void AI()
    {
        if (IsTemplarHoming)
            UpdateTemplarHoming();
        else if (!IsSoulforgedCrescent)
            Projectile.rotation = Projectile.velocity.ToRotation();

        SmokeSystem.AddWeaponLight(Projectile, GlowColor);
        SmokeSystem.AddWeaponLight(Projectile, GlowAccentColor * .16f);
        if ((IsTemplarHoming || IsSoulforgedCrescent) && Main.rand.NextBool(IsTemplarHoming ? 2 : 3))
        {
            Color tint = IsTemplarHoming ? new Color(255, 202, 83) : new Color(255, 112, 48);
            Dust dust = Dust.NewDustPerfect(Projectile.Center - Projectile.velocity * 0.35f,
                DustID.GoldFlame, -Projectile.velocity * 0.05f, 90, tint, IsTemplarHoming ? 0.8f : 0.72f);
            dust.noGravity = true;
            dust.noLight = true;
        }
    }

    private void UpdateTemplarHoming()
    {
        const float maxRange = 640f;
        int targetIndex = (int)Projectile.ai[0] - 1;
        bool ownerControlsTarget = HasTargetSelectionAuthority(Projectile.owner, Main.myPlayer, Main.netMode);

        if (ownerControlsTarget && !IsValidTemplarTarget(targetIndex, maxRange))
        {
            int nextTarget = FindTemplarTarget(maxRange);
            float nextAi = nextTarget < 0 ? 0f : nextTarget + 1f;
            if (Projectile.ai[0] != nextAi)
            {
                Projectile.ai[0] = nextAi;
                Projectile.netUpdate = true;
                SmokeSystem.LogTemplarTarget(Projectile, nextTarget,
                    targetIndex >= 0 ? "owner-reselect-invalid-target" : "owner-select");
                Projectile.localAI[1] = 1f;
            }
            else if (Projectile.localAI[1] == 0f)
            {
                SmokeSystem.LogTemplarTarget(Projectile, nextTarget, "owner-no-valid-target");
                Projectile.localAI[1] = 1f;
            }
            targetIndex = nextTarget;
        }

        if (IsValidTemplarTarget(targetIndex, maxRange))
        {
            Vector2 toTarget = Main.npc[targetIndex].Center - Projectile.Center;
            if (toTarget.LengthSquared() > 1f)
            {
                float speed = Math.Max(Projectile.velocity.Length(), 8f);
                float current = Projectile.velocity.ToRotation();
                float desired = toTarget.ToRotation();
                float delta = MathHelper.WrapAngle(desired - current);
                float turn = MathHelper.Clamp(delta, -0.045f, 0.045f);
                Projectile.velocity = new Vector2((float)Math.Cos(current + turn),
                    (float)Math.Sin(current + turn)) * speed;
                if (Projectile.timeLeft % 6 == 0)
                {
                    Projectile.netUpdate = true;
                    SmokeSystem.LogTemplarTurn(Projectile, turn);
                }
            }
        }
        Projectile.rotation = Projectile.velocity.ToRotation() + MathHelper.PiOver4;
    }

    private bool IsValidTemplarTarget(int index, float maxRange)
    {
        if (index < 0 || index >= Main.maxNPCs)
            return false;
        NPC npc = Main.npc[index];
        return npc.active && npc.CanBeChasedBy(Projectile) &&
            Vector2.DistanceSquared(Projectile.Center, npc.Center) <= maxRange * maxRange &&
            Collision.CanHitLine(Projectile.Center, 1, 1, npc.Center, 1, 1);
    }

    private int FindTemplarTarget(float maxRange)
    {
        int selected = -1;
        float bestDistance = maxRange * maxRange;
        for (int i = 0; i < Main.maxNPCs; i++)
        {
            if (!IsValidTemplarTarget(i, maxRange))
                continue;
            float distance = Vector2.DistanceSquared(Projectile.Center, Main.npc[i].Center);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                selected = i;
            }
        }
        return selected;
    }

    public static bool HasTargetSelectionAuthority(int projectileOwner, int localPlayer, int netMode) =>
        netMode == NetmodeID.SinglePlayer ||
        (netMode == NetmodeID.MultiplayerClient && projectileOwner == localPlayer);

    public override Color? GetAlpha(Color lightColor)
    {
        if (IsTemplarHoming || IsSoulforgedCrescent)
            return Color.White * (1f - Projectile.alpha / 255f);
        return base.GetAlpha(lightColor);
    }

    public override bool TileCollideStyle(ref int width, ref int height, ref bool fallThrough,
        ref Vector2 hitboxCenterFrac)
    {
        if (IsTemplarHoming)
        {
            width = 16;
            height = 16;
            hitboxCenterFrac = new Vector2(0.5f, 0.5f);
        }
        return true;
    }

    public override bool? Colliding(Rectangle projHitbox, Rectangle targetHitbox)
    {
        if (!IsSoulforgedCrescent)
            return null;

        // Match the installed TerraBlade2Shot (985) type-specific collision
        // branch: its fast, intentionally approximate cone test is followed
        // by the same line-of-sight check. Keeping both calls here also keeps
        // the native first-hit tick and damage falloff aligned.
        bool nativeConeAndSight = Terraria.Utils.IntersectsConeFastInaccurate(targetHitbox,
            Projectile.Center, 90f * Projectile.scale, Projectile.rotation, MathHelper.PiOver4) &&
            Collision.CanHit(Projectile.Center, 0, 0, targetHitbox.Center.ToVector2(), 0, 0);
        // The installed type-985 branch falls through to the ordinary
        // projectile/target rectangle intersection when its cone path misses.
        return nativeConeAndSight || projHitbox.Intersects(targetHitbox);
    }

    public override void OnHitNPC(NPC target, NPC.HitInfo hit, int damageDone)
    {
        if (IsSoulforgedCrescent)
        {
            // TerraBlade2Shot reduces its stored damage to 75% after each
            // successful NPC hit, while retaining infinite penetration.
            Projectile.damage = Math.Max(1, (int)(Projectile.damage * 0.75f));
        }
    }

    public override bool PreDraw(ref Color lightColor)
    {
        Texture2D texture = Terraria.GameContent.TextureAssets.Projectile[Projectile.type].Value;
        Color color = GetAlpha(lightColor) ?? lightColor;
        Vector2 origin = texture.Size() * 0.5f;
        Vector2 halfHitbox = new(Projectile.width * 0.5f, Projectile.height * 0.5f);
        Color mainGlow = IsTemplarHoming ? new Color(253, 201, 59) : new Color(242, 56, 50);
        Color accentGlow = IsTemplarHoming ? new Color(51, 203, 232) : new Color(227, 175, 54);
        float haloScale = Math.Max(texture.Width, texture.Height) / 48f * Projectile.scale;
        float glowFade = 1f - Projectile.alpha / 255f;
        for (int i = Projectile.oldPos.Length - 1; i >= 0; i--)
        {
            if (Projectile.oldPos[i] == Vector2.Zero)
                continue;
            float fade = 0.42f * (1f - i / (float)Projectile.oldPos.Length);
            Vector2 oldCenter = Projectile.oldPos[i] + halfHitbox - Main.screenPosition;
            PassiveVfxTextureSystem.DrawProjectileGlow(Main.spriteBatch, oldCenter, mainGlow, haloScale * 1.25f, fade * .30f);
            Main.spriteBatch.Draw(texture, oldCenter,
                null, color * fade, Projectile.oldRot[i], origin, Projectile.scale,
                SpriteEffects.None, 0f);
        }
        Vector2 drawCenter = Projectile.Center - Main.screenPosition;
        PassiveVfxTextureSystem.DrawProjectileGlow(Main.spriteBatch, drawCenter, mainGlow, haloScale * 1.52f, .42f * glowFade);
        PassiveVfxTextureSystem.DrawProjectileGlow(Main.spriteBatch, drawCenter, accentGlow, haloScale * .86f, .24f * glowFade);
        Main.spriteBatch.Draw(texture, drawCenter, null, color,
            Projectile.rotation, origin, Projectile.scale, SpriteEffects.None, 0f);
        return false;
    }

    public static bool TestArcCollision(Vector2 center, float rotation, float scale, Rectangle targetHitbox)
    {
        // Damage follows the thick visible crescent path, not the transparent
        // corners of the 192px art canvas.
        Vector2[] arc =
        {
            new(50f, -70f), new(66f, -48f), new(78f, 0f), new(66f, 48f), new(50f, 70f)
        };
        Rectangle padded = new(targetHitbox.X - 16, targetHitbox.Y - 16,
            targetHitbox.Width + 32, targetHitbox.Height + 32);
        float cos = (float)Math.Cos(rotation), sin = (float)Math.Sin(rotation);
        for (int i = 0; i < arc.Length - 1; i++)
        {
            Vector2 a = arc[i] * scale;
            Vector2 b = arc[i + 1] * scale;
            Vector2 start = center + new Vector2(a.X * cos - a.Y * sin, a.X * sin + a.Y * cos);
            Vector2 end = center + new Vector2(b.X * cos - b.Y * sin, b.X * sin + b.Y * cos);
            if (SegmentIntersectsRectangle(start, end, padded))
                return true;
        }
        return false;
    }

    public static bool TestTerraBladeCone(Vector2 center, float rotation, float scale, Rectangle targetHitbox)
    {
        bool nativeConeAndSight = Terraria.Utils.IntersectsConeFastInaccurate(targetHitbox,
            center, 90f * scale, rotation, MathHelper.PiOver4) &&
            Collision.CanHit(center, 0, 0, targetHitbox.Center.ToVector2(), 0, 0);
        Rectangle projectileHitbox = new((int)(center.X - 4), (int)(center.Y - 4), 8, 8);
        return nativeConeAndSight || projectileHitbox.Intersects(targetHitbox);
    }

    private static bool SegmentIntersectsRectangle(Vector2 start, Vector2 end, Rectangle rect)
    {
        float t0 = 0f, t1 = 1f;
        float dx = end.X - start.X, dy = end.Y - start.Y;
        return Clip(-dx, start.X - rect.Left, ref t0, ref t1) &&
            Clip(dx, rect.Right - start.X, ref t0, ref t1) &&
            Clip(-dy, start.Y - rect.Top, ref t0, ref t1) &&
            Clip(dy, rect.Bottom - start.Y, ref t0, ref t1);
    }

    private static bool Clip(float p, float q, ref float t0, ref float t1)
    {
        if (Math.Abs(p) < 0.0001f)
            return q >= 0f;
        float r = q / p;
        if (p < 0f)
        {
            if (r > t1) return false;
            if (r > t0) t0 = r;
        }
        else
        {
            if (r < t0) return false;
            if (r < t1) t1 = r;
        }
        return true;
    }
}

/// <summary>
/// AIStyle 191 emits its own green point light. This narrowly scoped hook
/// recolors only that exact native light while SoulforgedWave is in its AI
/// callbacks; all other world lighting calls pass through unchanged.
/// </summary>
internal sealed class SoulforgedNativeColorScope : ModSystem
{
    private static int _activeProjectile = -1;
    private static int _remappedLightCount;
    private Hook _lightingHook;

    private delegate void OrigAddLight(Vector2 position, Vector3 rgb);
    private delegate void AddLightDetour(OrigAddLight orig, Vector2 position, Vector3 rgb);

    public override void Load()
    {
        if (!Main.dedServ)
        {
            MethodInfo method = typeof(Lighting).GetMethod(nameof(Lighting.AddLight),
                new[] { typeof(Vector2), typeof(Vector3) })
                ?? throw new InvalidOperationException("Could not locate Lighting.AddLight(Vector2, Vector3).");
            _lightingHook = new Hook(method, (AddLightDetour)Lighting_AddLight);
        }
    }

    public override void Unload()
    {
        _lightingHook?.Dispose();
        _lightingHook = null;
        _activeProjectile = -1;
    }

    internal static void Begin(Projectile projectile) => _activeProjectile = projectile.whoAmI;
    internal static int RemappedLightCount => _remappedLightCount;
    internal static void ResetDiagnosticCount() => _remappedLightCount = 0;

    internal static void End(Projectile projectile)
    {
        if (_activeProjectile == projectile.whoAmI)
            _activeProjectile = -1;
    }

    private static void Lighting_AddLight(OrigAddLight orig, Vector2 position, Vector3 rgb)
    {
        if (_activeProjectile >= 0 &&
            Math.Abs(rgb.X - 64f / 255f) < 0.002f &&
            Math.Abs(rgb.Y - 220f / 255f) < 0.002f &&
            Math.Abs(rgb.Z - 96f / 255f) < 0.002f)
        {
            // Preserve the native call's approximate strength while changing
            // only its hue to the established red/gold Soulforged palette.
            rgb = new Vector3(0.62f, 0.17f, 0.08f);
            _remappedLightCount++;
            if (_remappedLightCount <= 3)
            {
                string output = Environment.GetEnvironmentVariable("MILKCRAFT_TERRARIA_SMOKE_OUT")
                    ?? Environment.CurrentDirectory;
                Directory.CreateDirectory(output);
                File.AppendAllText(Path.Combine(output, "weapon-diagnostics.log"),
                    $"native-ai-light-remap projectileSlot={_activeProjectile} position={position} " +
                    $"source=0.251,0.863,0.376 target={rgb}\n");
            }
        }
        orig(position, rgb);
    }
}

public sealed class TemplarWave : Tier3T4SwordWave
{
    protected override int WaveWidth => 66;
    protected override int WaveHeight => 66;
    protected override int Penetration => 2;
    protected override int WaveTimeLeft => 60;
    protected override float LightLevel => 0.75f;
    protected override Vector3 GlowColor => new(0.99f, 0.79f, 0.23f);
    protected override Vector3 GlowAccentColor => new(0.20f, 0.80f, 0.91f);
    protected override bool IsTemplarHoming => true;
}

public sealed class SoulforgedWave : Tier3T4SwordWave
{
    protected override int WaveWidth => 8;
    protected override int WaveHeight => 8;
    protected override int Penetration => -1;
    protected override int WaveTimeLeft => 90;
    protected override float LightLevel => 0.72f;
    protected override Vector3 GlowColor => new(0.95f, 0.22f, 0.20f);
    protected override Vector3 GlowAccentColor => new(0.89f, 0.69f, 0.21f);
    protected override bool IsSoulforgedCrescent => true;

    public override void SetDefaults()
    {
        base.SetDefaults();
    }
}

public abstract class Tier3T4MagicBolt : ModProjectile
{
    protected abstract int BoltSize { get; }
    protected abstract int Penetration { get; }
    protected abstract float LightLevel { get; }
    protected abstract Vector3 GlowColor { get; }
    protected abstract Vector3 GlowAccentColor { get; }
    public override string Texture => $"{Mod.Name}/Content/Items/{GetType().Name}";

    public override void SetDefaults()
    {
        Projectile.width = BoltSize;
        Projectile.height = BoltSize;
        Projectile.friendly = true;
        Projectile.hostile = false;
        Projectile.DamageType = DamageClass.Magic;
        Projectile.timeLeft = 180;
        Projectile.penetrate = Penetration;
        Projectile.tileCollide = true;
        Projectile.ignoreWater = true;
        Projectile.light = 0f;
        Projectile.aiStyle = 1;
        AIType = ProjectileID.Bullet;
    }

    public override void AI()
    {
        Projectile.rotation = Projectile.velocity.ToRotation();
        SmokeSystem.AddWeaponLight(Projectile, GlowColor);
        SmokeSystem.AddWeaponLight(Projectile, GlowAccentColor * .16f);
    }
}

public sealed class FallenBolt : Tier3T4MagicBolt
{
    protected override int BoltSize => 14;
    protected override int Penetration => 2;
    protected override float LightLevel => 0.55f;
    protected override Vector3 GlowColor => new(0.92f, 0.73f, 0.22f);
    protected override Vector3 GlowAccentColor => new(0.20f, 0.80f, 0.91f);
}

public sealed class DarkBolt : Tier3T4MagicBolt
{
    protected override int BoltSize => 16;
    protected override int Penetration => 3;
    protected override float LightLevel => 0.65f;
    protected override Vector3 GlowColor => new(0.86f, 0.16f, 0.27f);
    protected override Vector3 GlowAccentColor => new(0.38f, 0.25f, 0.70f);
}

public abstract class Tier3T4AccessoryItem : MilkcraftAccessory
{
    protected abstract int Tier { get; }
    protected abstract string AccessoryKind { get; }
    public override string Texture => $"{Mod.Name}/Content/Items/{GetType().Name}";
    protected override string Family => AccessoryKind switch { "Shield" => "shield", "Quiver" => "quiver", _ => "tome" };

    public override void SetDefaults()
    {
        base.SetDefaults();
        Item.rare = Tier == 3 ? ItemRarityID.Yellow : ItemRarityID.Lime;
        Item.value = Item.sellPrice(gold: Tier == 3 ? 1 : 2);
    }

    public override bool CanEquipAccessory(Player player, int slot, bool modded) =>
        player.GetModPlayer<Tier3T4ProgressPlayer>().HasLicense(Tier) && base.CanEquipAccessory(player, slot, modded);

    public override void UpdateAccessory(Player player, bool hideVisual)
    {
        if (!player.GetModPlayer<Tier3T4ProgressPlayer>().HasLicense(Tier)) return;
        player.GetModPlayer<MilkcraftPassivePlayer>().RegisterFunctionalAccessory(GetType().Name);
        switch (GetType().Name)
        {
            case "TemplarShield": player.statDefense += 6; player.noKnockback = true; player.endurance += 0.06f; break;
            case "NightmareQuiver": player.GetDamage(DamageClass.Ranged) += 0.12f; player.ammoCost80 = true; break;
            case "FallenTome": player.statManaMax2 += 40; player.GetDamage(DamageClass.Magic) += 0.12f; player.manaCost -= 0.08f; player.manaFlower = true; break;
            case "SoulforgedShield": player.statDefense += 8; player.noKnockback = true; player.endurance += 0.10f; break;
            case "AbyssQuiver": player.GetDamage(DamageClass.Ranged) += 0.15f; player.GetCritChance(DamageClass.Ranged) += 5f; player.ammoCost80 = true; break;
            case "DarkTome": player.statManaMax2 += 60; player.GetDamage(DamageClass.Magic) += 0.15f; player.manaCost -= 0.10f; player.manaFlower = true; break;
        }
    }
}

[AutoloadEquip(EquipType.Shield)] public sealed class TemplarShield : Tier3T4AccessoryItem { protected override int Tier => 3; protected override string AccessoryKind => "Shield"; }
[AutoloadEquip(EquipType.Back)] public sealed class NightmareQuiver : Tier3T4AccessoryItem { protected override int Tier => 3; protected override string AccessoryKind => "Quiver"; }
[AutoloadEquip(EquipType.Waist)] public sealed class FallenTome : Tier3T4AccessoryItem { protected override int Tier => 3; protected override string AccessoryKind => "Tome"; }
[AutoloadEquip(EquipType.Shield)] public sealed class SoulforgedShield : Tier3T4AccessoryItem { protected override int Tier => 4; protected override string AccessoryKind => "Shield"; }
[AutoloadEquip(EquipType.Back)] public sealed class AbyssQuiver : Tier3T4AccessoryItem { protected override int Tier => 4; protected override string AccessoryKind => "Quiver"; }
[AutoloadEquip(EquipType.Waist)] public sealed class DarkTome : Tier3T4AccessoryItem { protected override int Tier => 4; protected override string AccessoryKind => "Tome"; }

public abstract class Tier3T4MaterialItem : ModItem
{
    protected abstract int Tier { get; }
    public override string Texture => $"{Mod.Name}/Content/Items/{GetType().Name}";
    public override void SetDefaults()
    {
        Item.width = 20;
        Item.height = 20;
        Item.maxStack = Item.CommonMaxStack;
        Item.rare = Tier == 3 ? ItemRarityID.Yellow : ItemRarityID.Lime;
        Item.value = Item.sellPrice(silver: 20);
    }
}

public sealed class OriginBar : Tier3T4MaterialItem { protected override int Tier => 3; }
public sealed class OrnateLeather : Tier3T4MaterialItem { protected override int Tier => 3; }
public sealed class RunicWool : Tier3T4MaterialItem { protected override int Tier => 3; }
public sealed class CinderBar : Tier3T4MaterialItem { protected override int Tier => 4; }
public sealed class BaneboundLeather : Tier3T4MaterialItem { protected override int Tier => 4; }
public sealed class SighWool : Tier3T4MaterialItem { protected override int Tier => 4; }

public abstract class TierEquipmentLicenseItem : ModItem
{
    protected abstract int Tier { get; }
    public override string Texture => $"{Mod.Name}/Content/Items/{GetType().Name}";
    public override void SetDefaults()
    {
        Item.width = 24;
        Item.height = 24;
        Item.maxStack = 1;
        Item.consumable = true;
        Item.useStyle = ItemUseStyleID.HoldUp;
        Item.useTime = 20;
        Item.useAnimation = 20;
        Item.UseSound = SoundID.Item4;
        Item.noMelee = true;
        Item.rare = Tier == 3 ? ItemRarityID.Yellow : ItemRarityID.Lime;
        Item.value = Item.sellPrice(gold: Tier == 3 ? 2 : 4);
    }

    public override bool CanUseItem(Player player)
    {
        Tier3T4ProgressPlayer progress = player.GetModPlayer<Tier3T4ProgressPlayer>();
        return !progress.HasLicense(Tier) && Tier3T4ProgressPlayer.WorldAllowsTier(Tier);
    }

    public override void ModifyTooltips(System.Collections.Generic.List<TooltipLine> tooltips)
    {
        Tier3T4ProgressPlayer progress = Main.LocalPlayer.GetModPlayer<Tier3T4ProgressPlayer>();
        bool active = progress.HasLicense(Tier);
        string statusKey = active
            ? $"Mods.{Mod.Name}.Progression.T{Tier}LicenseActive"
            : Tier3T4ProgressPlayer.WorldAllowsTier(Tier)
                ? $"Mods.{Mod.Name}.Progression.T{Tier}LicenseReady"
                : $"Mods.{Mod.Name}.Progression.T{Tier}LicenseLocked";
        tooltips.Add(new TooltipLine(Mod, "LicenseStatus", Language.GetTextValue(statusKey)));
        if (progress.HasT3ChampionTitle)
            tooltips.Add(new TooltipLine(Mod, "T3ChampionTitle", Language.GetTextValue($"Mods.{Mod.Name}.Progression.T3ChampionTitle")));
    }

    public override bool? UseItem(Player player)
    {
        if (player.GetModPlayer<Tier3T4ProgressPlayer>().ActivateLicense(Tier) && player.whoAmI == Main.myPlayer)
            Main.NewText(Terraria.Localization.Language.GetTextValue($"Mods.{Mod.Name}.Progression.T{Tier}LicenseActivated"), 144, 238, 144);
        return true;
    }
}

public sealed class T3EquipmentLicense : TierEquipmentLicenseItem { protected override int Tier => 3; }
public sealed class T4EquipmentLicense : TierEquipmentLicenseItem { protected override int Tier => 4; }
