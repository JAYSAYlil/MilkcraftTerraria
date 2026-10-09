using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.ModLoader;
using MilkcraftTerraria.Content;

namespace MilkcraftTerraria.Content.Items;

public abstract class MilkcraftWeapon : ModItem
{
    public override string Texture => $"{Mod.Name}/Content/Items/{GetType().Name}";
    protected abstract int Damage { get; }
    protected abstract int UseTime { get; }
    protected abstract float Knockback { get; }
    protected virtual float ShootSpeed => 0f;
    protected virtual int Mana => 0;

    public override void SetDefaults()
    {
        // Collision dimensions follow the native grip/shaft footprint rather
        // than a generic square. Projectile weapons do not receive this melee
        // hitbox because noMelee is enabled below.
        string weaponName = GetType().Name;
        if (weaponName is "IronSword" or "KnightSword")
        {
            Item.width = 28;
            Item.height = 36;
            // Keep the reviewed sprite proportions while slightly enlarging
            // the native held sprite and its melee reach.
            Item.scale = 1.2f;
        }
        else if (weaponName is "IronBow" or "HuntingBow")
        {
            Item.width = 18;
            Item.height = 42;
        }
        else
        {
            Item.width = 18;
            Item.height = 52;
        }
        Item.damage = Damage;
        Item.knockBack = Knockback;
        Item.useTime = UseTime;
        Item.useAnimation = UseTime;
        Item.autoReuse = true;
        Item.value = Item.sellPrice(silver: 60);
        Item.rare = GetType().Name.StartsWith("Knight") || GetType().Name.StartsWith("Hunting") || GetType().Name.StartsWith("Energy")
            ? ItemRarityID.Orange
            : ItemRarityID.Blue;

        switch (GetType().Name)
        {
            case "IronSword":
                Item.DamageType = DamageClass.Melee;
                Item.useStyle = ItemUseStyleID.Swing;
                Item.UseSound = SoundID.Item1;
                break;
            case "KnightSword":
                Item.DamageType = DamageClass.Melee;
                Item.useStyle = ItemUseStyleID.Swing;
                Item.UseSound = SoundID.Item1;
                Item.shoot = ModContent.ProjectileType<KnightSwordWave>();
                Item.shootSpeed = 9f;
                break;
            case "IronBow":
            case "HuntingBow":
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
                Item.shoot = GetType().Name == "EnergyStaff"
                    ? ModContent.ProjectileType<EnergyBolt>()
                    : ModContent.ProjectileType<EmberBolt>();
                Item.shootSpeed = ShootSpeed;
                Item.mana = Mana;
                Item.UseSound = SoundID.Item20;
                break;
        }
    }

    // Staff sprites are authored diagonally with the hand grip near the lower
    // left. The native default uses a zero origin, which pivots around an
    // arbitrary corner and makes the staff head sweep through the wrist.
    public override Vector2? HoldoutOrigin()
    {
        // Native ItemCheck position is the hand anchor. Grip offsets belong
        // in the draw origin, not in this hook (which shifts both position
        // and origin away from the hand).
        return null;
    }

    public override bool ModifyItemDraw(ref PlayerDrawSet drawInfo, ref DrawData drawData,
        ref DrawData? coloredDrawData, ref DrawData? glowMaskDrawData)
    {
        if (GetType().Name is "IronStaff" or "EnergyStaff" or "IronSword" or "KnightSword")
        {
            // Keep ItemCheck's native hand position and rotation, pivoting the
            // accepted sprite at the visible grip point.
            (float gripX, float gripY) = GetType().Name switch
            {
                "EnergyStaff" => (13f, 41f),
                "IronStaff" => (11f, 37f),
                "KnightSword" => (10f, 37f),
                _ => (10f, 36f)
            };
            if (drawInfo.drawPlayer.direction < 0)
                gripX = drawData.texture.Width - gripX;
            bool reflectKnightBlade = GetType().Name == "KnightSword" &&
                !SmokeSystem.UseOriginalKnightSwordDrawForPreview;
            int actorDirection = drawInfo.drawPlayer.direction;
            void ApplyGripAndBladeReflection(ref DrawData data)
            {
                data.origin = new Vector2(gripX, gripY);
                if (!reflectKnightBlade)
                    return;

                // Mirror the sprite across its own long axis: the sword is
                // authored diagonally, so compensate the horizontal source
                // flip with a quarter-turn. This changes which bevel faces out
                // without reversing the hand-to-tip direction.
                data.origin = new Vector2(data.texture.Width - gripX, gripY);
                data.effect ^= SpriteEffects.FlipHorizontally;
                data.rotation += actorDirection * MathHelper.PiOver2;
            }

            ApplyGripAndBladeReflection(ref drawData);
            if (coloredDrawData.HasValue)
            {
                DrawData colored = coloredDrawData.Value;
                ApplyGripAndBladeReflection(ref colored);
                coloredDrawData = colored;
            }
            if (glowMaskDrawData.HasValue)
            {
                DrawData glow = glowMaskDrawData.Value;
                ApplyGripAndBladeReflection(ref glow);
                glowMaskDrawData = glow;
            }
            if (SmokeSystem.ForcePreviewLighting && drawInfo.drawPlayer.whoAmI == Main.myPlayer)
                SmokeSystem.LogGripOriginDraw(GetType().Name, drawInfo, drawData);
        }

        if (GetType().Name is not ("IronBow" or "HuntingBow"))
            return true;

        // The accepted bow artwork has its string on the wrong side for the
        // native shooting grip. Mirror the existing native draw call; keep its
        // texture, origin, position, and rotation supplied by Terraria.
        drawData.effect ^= SpriteEffects.FlipHorizontally;
        if (coloredDrawData.HasValue)
        {
            DrawData colored = coloredDrawData.Value;
            colored.effect ^= SpriteEffects.FlipHorizontally;
            coloredDrawData = colored;
        }
        if (glowMaskDrawData.HasValue)
        {
            DrawData glow = glowMaskDrawData.Value;
            glow.effect ^= SpriteEffects.FlipHorizontally;
            glowMaskDrawData = glow;
        }

        if (MilkcraftTerraria.Content.SmokeSystem.ForcePreviewLighting && drawInfo.drawPlayer.whoAmI == Main.myPlayer)
            MilkcraftTerraria.Content.SmokeSystem.LogBowDraw(GetType().Name, drawInfo, drawData);
        return true;
    }

    public override void AddRecipes() => MilkcraftRecipes.AddGearRecipe(this);

    public override bool Shoot(Player player, EntitySource_ItemUse_WithAmmo source, Vector2 position,
        Vector2 velocity, int type, int damage, float knockback)
    {
        if (GetType().Name != "KnightSword")
            return base.Shoot(player, source, position, velocity, type, damage, knockback);

        if (MilkcraftTerraria.Content.SmokeSystem.ForcePreviewLighting && player.whoAmI == Main.myPlayer)
            MilkcraftTerraria.Content.SmokeSystem.LogKnightWaveShot(player, position, velocity);

        // The engine calls Shoot once for each sword-use animation. Spawn one
        // deterministic melee-class wave here and suppress its default second
        // spawn; no proc chance or per-update projectile loop is involved.
        Projectile.NewProjectile(source, position, velocity, type, damage, knockback, player.whoAmI);
        return false;
    }

    public override void ModifyShootStats(Player player, ref Vector2 position, ref Vector2 velocity, ref int type, ref int damage, ref float knockback)
    {
        if (GetType().Name == "HuntingBow" && type == ProjectileID.WoodenArrowFriendly)
            type = ModContent.ProjectileType<HuntingArrow>();
    }
}

public sealed class IronSword : MilkcraftWeapon { protected override int Damage => 15; protected override int UseTime => 25; protected override float Knockback => 4f; }
public sealed class IronBow : MilkcraftWeapon { protected override int Damage => 14; protected override int UseTime => 27; protected override float Knockback => 2f; protected override float ShootSpeed => 7f; }
public sealed class IronStaff : MilkcraftWeapon { protected override int Damage => 16; protected override int UseTime => 25; protected override float Knockback => 2f; protected override float ShootSpeed => 8f; protected override int Mana => 5; }
public sealed class KnightSword : MilkcraftWeapon { protected override int Damage => 29; protected override int UseTime => 23; protected override float Knockback => 5f; }
public sealed class HuntingBow : MilkcraftWeapon { protected override int Damage => 32; protected override int UseTime => 22; protected override float Knockback => 3f; protected override float ShootSpeed => 9f; }
public sealed class EnergyStaff : MilkcraftWeapon { protected override int Damage => 31; protected override int UseTime => 23; protected override float Knockback => 3f; protected override float ShootSpeed => 10f; protected override int Mana => 7; }

public sealed class EmberBolt : ModProjectile
{
    public override string Texture => $"{Mod.Name}/Content/Projectiles/EmberBolt";
    public override void SetDefaults()
    {
        Projectile.width = 12;
        Projectile.height = 12;
        Projectile.friendly = true;
        Projectile.hostile = false;
        Projectile.DamageType = DamageClass.Magic;
        Projectile.timeLeft = 180;
        Projectile.aiStyle = 1;
        AIType = ProjectileID.Bullet;
        Projectile.penetrate = 1;
        Projectile.light = 0f;
    }
}

public sealed class EnergyBolt : ModProjectile
{
    public override string Texture => $"{Mod.Name}/Content/Projectiles/EnergyBolt";
    public override void SetDefaults()
    {
        Projectile.width = 14;
        Projectile.height = 14;
        Projectile.friendly = true;
        Projectile.hostile = false;
        Projectile.DamageType = DamageClass.Magic;
        Projectile.timeLeft = 180;
        Projectile.aiStyle = 1;
        AIType = ProjectileID.Bullet;
        Projectile.penetrate = 2;
        Projectile.light = 0f;
    }
}

public sealed class KnightSwordWave : ModProjectile
{
    public override string Texture => $"{Mod.Name}/Content/Projectiles/KnightSwordWave";

    public override void SetStaticDefaults()
    {
        ProjectileID.Sets.TrailCacheLength[Type] = 8;
        ProjectileID.Sets.TrailingMode[Type] = 2;
    }

    public override void SetDefaults()
    {
        Projectile.width = 18;
        Projectile.height = 10;
        Projectile.friendly = true;
        Projectile.hostile = false;
        Projectile.DamageType = DamageClass.Melee;
        Projectile.timeLeft = 50;
        Projectile.penetrate = 2;
        Projectile.tileCollide = true;
        Projectile.ignoreWater = true;
        Projectile.ownerHitCheck = true;
        Projectile.usesLocalNPCImmunity = true;
        Projectile.localNPCHitCooldown = 16;
        Projectile.light = 0f;
        Projectile.aiStyle = 1;
        AIType = ProjectileID.Bullet;
    }

    public override void AI()
    {
        Projectile.rotation = Projectile.velocity.ToRotation();
        SmokeSystem.AddWeaponLight(Projectile, new Vector3(0.91f, 0.22f, 0.22f));
        SmokeSystem.AddWeaponLight(Projectile, new Vector3(0.96f, 0.72f, 0.18f) * .16f);
        if (Projectile.timeLeft % 3 == 0 && Main.rand.NextBool(2))
        {
            Dust dust = Dust.NewDustPerfect(Projectile.Center - Projectile.velocity * 0.25f,
                DustID.RedTorch, -Projectile.velocity * 0.035f, 80, new Color(255, 68, 44), 0.72f);
            dust.noGravity = true;
            dust.noLight = true;
        }
    }

    public override Color? GetAlpha(Color lightColor) => Color.White;

    public override bool PreDraw(ref Color lightColor)
    {
        Texture2D texture = Terraria.GameContent.TextureAssets.Projectile[Projectile.type].Value;
        Vector2 origin = texture.Size() * 0.5f;
        Vector2 halfHitbox = new(Projectile.width * 0.5f, Projectile.height * 0.5f);
        Color mainGlow = new(233, 56, 56);
        Color accentGlow = new(245, 183, 47);
        float haloScale = texture.Width / 48f * Projectile.scale;
        for (int i = Projectile.oldPos.Length - 1; i >= 0; i--)
        {
            if (Projectile.oldPos[i] == Vector2.Zero)
                continue;
            float fade = 0.4f * (1f - i / (float)Projectile.oldPos.Length);
            Vector2 oldCenter = Projectile.oldPos[i] + halfHitbox - Main.screenPosition;
            PassiveVfxTextureSystem.DrawProjectileGlow(Main.spriteBatch, oldCenter, mainGlow, haloScale * 1.35f, fade * .30f);
            Main.spriteBatch.Draw(texture, oldCenter,
                null, Color.White * fade, Projectile.oldRot[i], origin, Projectile.scale,
                SpriteEffects.None, 0f);
        }
        Vector2 drawCenter = Projectile.Center - Main.screenPosition;
        PassiveVfxTextureSystem.DrawProjectileGlow(Main.spriteBatch, drawCenter, mainGlow, haloScale * 1.65f, .46f);
        PassiveVfxTextureSystem.DrawProjectileGlow(Main.spriteBatch, drawCenter, accentGlow, haloScale * .82f, .25f);
        Main.spriteBatch.Draw(texture, drawCenter, null, Color.White,
            Projectile.rotation, origin, Projectile.scale, SpriteEffects.None, 0f);
        return false;
    }
}

public sealed class HuntingArrow : ModProjectile
{
    public override string Texture => $"Terraria/Images/Projectile_{ProjectileID.WoodenArrowFriendly}";
    public override void SetDefaults()
    {
        Projectile.CloneDefaults(ProjectileID.WoodenArrowFriendly);
        AIType = ProjectileID.WoodenArrowFriendly;
        Projectile.damage = 0;
    }
}
