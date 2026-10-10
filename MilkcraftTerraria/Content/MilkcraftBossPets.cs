using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.DataStructures;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;

namespace MilkcraftTerraria.Content;

public enum MilkcraftBossPetKind : byte
{
    WraithWarrior, JungleLord, Banshee, ObsidianGuardian, Sphinx, FrostWitch, CastleCore
}

internal static class MilkcraftBossPetAssets
{
    internal static string Name(MilkcraftBossPetKind kind) => kind switch
    {
        MilkcraftBossPetKind.WraithWarrior => "WraithWarriorPet",
        MilkcraftBossPetKind.JungleLord => "JungleLordPet",
        MilkcraftBossPetKind.Banshee => "BansheePet",
        MilkcraftBossPetKind.ObsidianGuardian => "ObsidianGuardianPet",
        MilkcraftBossPetKind.Sphinx => "SphinxPet",
        MilkcraftBossPetKind.FrostWitch => "FrostWitchPet",
        _ => "CastleCorePet"
    };

    internal static bool IsLight(MilkcraftBossPetKind kind)
        => kind is MilkcraftBossPetKind.Banshee or MilkcraftBossPetKind.FrostWitch or MilkcraftBossPetKind.CastleCore;

    internal static int Item(MilkcraftBossPetKind kind) => kind switch
    {
        MilkcraftBossPetKind.WraithWarrior => ModContent.ItemType<WraithWarriorPetSummon>(),
        MilkcraftBossPetKind.JungleLord => ModContent.ItemType<JungleLordPetSummon>(),
        MilkcraftBossPetKind.Banshee => ModContent.ItemType<BansheePetSummon>(),
        MilkcraftBossPetKind.ObsidianGuardian => ModContent.ItemType<ObsidianGuardianPetSummon>(),
        MilkcraftBossPetKind.Sphinx => ModContent.ItemType<SphinxPetSummon>(),
        MilkcraftBossPetKind.FrostWitch => ModContent.ItemType<FrostWitchPetSummon>(),
        _ => ModContent.ItemType<CastleCorePetSummon>()
    };

    internal static int Buff(MilkcraftBossPetKind kind) => kind switch
    {
        MilkcraftBossPetKind.WraithWarrior => ModContent.BuffType<WraithWarriorPetBuff>(),
        MilkcraftBossPetKind.JungleLord => ModContent.BuffType<JungleLordPetBuff>(),
        MilkcraftBossPetKind.Banshee => ModContent.BuffType<BansheePetBuff>(),
        MilkcraftBossPetKind.ObsidianGuardian => ModContent.BuffType<ObsidianGuardianPetBuff>(),
        MilkcraftBossPetKind.Sphinx => ModContent.BuffType<SphinxPetBuff>(),
        MilkcraftBossPetKind.FrostWitch => ModContent.BuffType<FrostWitchPetBuff>(),
        _ => ModContent.BuffType<CastleCorePetBuff>()
    };

    internal static int Projectile(MilkcraftBossPetKind kind) => kind switch
    {
        MilkcraftBossPetKind.WraithWarrior => ModContent.ProjectileType<WraithWarriorPet>(),
        MilkcraftBossPetKind.JungleLord => ModContent.ProjectileType<JungleLordPet>(),
        MilkcraftBossPetKind.Banshee => ModContent.ProjectileType<BansheePet>(),
        MilkcraftBossPetKind.ObsidianGuardian => ModContent.ProjectileType<ObsidianGuardianPet>(),
        MilkcraftBossPetKind.Sphinx => ModContent.ProjectileType<SphinxPet>(),
        MilkcraftBossPetKind.FrostWitch => ModContent.ProjectileType<FrostWitchPet>(),
        _ => ModContent.ProjectileType<CastleCorePet>()
    };
}

public abstract class MilkcraftBossPetSummon : ModItem
{
    protected abstract MilkcraftBossPetKind Kind { get; }
    public override string Texture => $"{Mod.Name}/Content/Items/{MilkcraftBossPetAssets.Name(Kind)}Summon";

    public override void SetDefaults()
    {
        Item.width = 34;
        Item.height = 32;
        Item.rare = ItemRarityID.LightRed;
        Item.value = Item.buyPrice(gold: 2);
        Item.useStyle = ItemUseStyleID.HoldUp;
        Item.useTime = 20;
        Item.useAnimation = 20;
        Item.useTurn = true;
        Item.noMelee = true;
        Item.autoReuse = false;
        Item.shoot = MilkcraftBossPetAssets.Projectile(Kind);
        Item.buffType = MilkcraftBossPetAssets.Buff(Kind);
        // Native Player.UpdatePet/UpdatePetLight supplies a 3600 tick token for
        // misc pet slots. The pet buff is refreshed only while that slot remains
        // equipped, so the item itself has no finite-duration tooltip metadata.
        Item.buffTime = 0;
    }

    public override bool? UseItem(Player player)
    {
        player.AddBuff(Item.buffType, 18000);
        return true;
    }
}

public sealed class MilkcraftBossPetPlayerState : ModPlayer
{
    private readonly int[] _trackedSlotBuffs = new int[2];

    public override void PreUpdate()
    {
        for (int slot = 0; slot < _trackedSlotBuffs.Length; slot++)
        {
            Item equipped = Player.miscEquips[slot];
            int current = equipped.stack > 0 && !Player.hideMisc[slot] && IsMilkcraftPetBuff(equipped.buffType)
                ? equipped.buffType : 0;
            int previous = _trackedSlotBuffs[slot];
            if (previous > 0 && previous != current)
                Player.ClearBuff(previous);
            _trackedSlotBuffs[slot] = current;
        }
    }

    private static bool IsMilkcraftPetBuff(int buffType)
    {
        foreach (MilkcraftBossPetKind kind in Enum.GetValues(typeof(MilkcraftBossPetKind)))
            if (MilkcraftBossPetAssets.Buff(kind) == buffType)
                return true;
        return false;
    }

    public override void UpdateDead() => Array.Clear(_trackedSlotBuffs);
}

public abstract class MilkcraftBossPetBuff : ModBuff
{
    protected abstract MilkcraftBossPetKind Kind { get; }

    public override string Texture => $"{Mod.Name}/Content/Buffs/{MilkcraftBossPetAssets.Name(Kind)}Buff";

    public override void SetStaticDefaults()
    {
        Main.buffNoTimeDisplay[Type] = true;
        Main.buffNoSave[Type] = true;
        if (MilkcraftBossPetAssets.IsLight(Kind)) Main.lightPet[Type] = true;
        else Main.vanityPet[Type] = true;
    }

    public override void Update(Player player, ref int buffIndex)
    {
        if (player.whoAmI < 0 || player.whoAmI >= Main.maxPlayers
            || buffIndex < 0 || buffIndex >= player.buffTime.Length || buffIndex >= player.buffType.Length)
            return;
        player.buffTime[buffIndex] = 18000;
        int projectile = MilkcraftBossPetAssets.Projectile(Kind);
        if (projectile < 0 || projectile >= player.ownedProjectileCounts.Length)
            return;
        if (player.ownedProjectileCounts[projectile] <= 0 && player.whoAmI == Main.myPlayer)
            Projectile.NewProjectile(player.GetSource_Buff(buffIndex), player.Center, Vector2.Zero, projectile, 0, 0f, player.whoAmI);
    }
}

public abstract class MilkcraftBossPetProjectile : ModProjectile
{
    protected abstract MilkcraftBossPetKind Kind { get; }
    protected virtual bool Floating => MilkcraftBossPetAssets.IsLight(Kind);
    protected virtual Vector3 LightColor => Kind switch
    {
        MilkcraftBossPetKind.Banshee => new Vector3(0.55f, 0.72f, 1f),
        MilkcraftBossPetKind.FrostWitch => new Vector3(0.58f, 0.82f, 1f),
        MilkcraftBossPetKind.CastleCore => CastleLight(),
        MilkcraftBossPetKind.WraithWarrior => new Vector3(0.35f, 0.20f, 0.60f),
        MilkcraftBossPetKind.JungleLord => new Vector3(0.18f, 0.44f, 0.16f),
        MilkcraftBossPetKind.ObsidianGuardian => new Vector3(0.62f, 0.26f, 0.12f),
        _ => new Vector3(0.70f, 0.52f, 0.24f)
    };

    private Vector3 CastleLight()
    {
        int face = (int)(Main.GameUpdateCount / 90 % 3);
        return face == 1 ? new Vector3(0.30f, 0.50f, 0.72f) : face == 2 ? new Vector3(0.70f, 0.20f, 0.16f) : new Vector3(0.65f, 0.54f, 0.24f);
    }

    public override string Texture => $"{Mod.Name}/Content/Projectiles/{MilkcraftBossPetAssets.Name(Kind)}";

    public override void SetStaticDefaults()
    {
        Main.projFrames[Type] = 8;
        Main.projPet[Type] = true;
        ProjectileID.Sets.LightPet[Type] = MilkcraftBossPetAssets.IsLight(Kind);
        ProjectileID.Sets.MinionSacrificable[Type] = false;
        ProjectileID.Sets.MinionTargettingFeature[Type] = false;
    }

    public override void SetDefaults()
    {
        Projectile.width = 36;
        Projectile.height = 42;
        Projectile.friendly = true;
        Projectile.penetrate = -1;
        Projectile.tileCollide = !Floating;
        Projectile.ignoreWater = true;
        Projectile.netImportant = true;
        Projectile.timeLeft = 2;
        Projectile.aiStyle = -1;
    }

    public override bool? CanDamage() => false;

    public override void AI()
    {
        if (Projectile.owner < 0 || Projectile.owner >= Main.maxPlayers)
        {
            Projectile.Kill();
            return;
        }
        Player owner = Main.player[Projectile.owner];
        int buff = MilkcraftBossPetAssets.Buff(Kind);
        if (!owner.active || owner.dead || !owner.HasBuff(buff))
        {
            Projectile.Kill();
            return;
        }
        Projectile.timeLeft = 2;
        Vector2 rest = owner.Center + new Vector2(-owner.direction * 46f, Floating ? -52f : -4f);
        Vector2 delta = rest - Projectile.Center;
        float distance = delta.Length();
        if (distance > 1200f)
        {
            Projectile.Center = rest + new Vector2(-owner.direction * 24f, -14f);
            Projectile.velocity = owner.velocity;
            Projectile.netUpdate = true;
        }
        else if (Floating)
        {
            Vector2 desired = distance > 48f ? delta.SafeNormalize(Vector2.UnitX) * Math.Min(12f, distance * 0.08f) : Vector2.Zero;
            Projectile.velocity = (Projectile.velocity * 19f + desired) / 20f;
            if (distance < 28f) Projectile.velocity *= 0.90f;
        }
        else
        {
            bool catchup = Projectile.localAI[0] > 0f
                ? distance > 170f || Math.Abs(delta.Y) > 70f
                : distance > 450f || Math.Abs(delta.Y) > 160f;
            Projectile.localAI[0] = catchup ? 1f : 0f;
            Projectile.tileCollide = !catchup;
            if (catchup)
            {
                Vector2 desired = delta.SafeNormalize(Vector2.UnitY) * Math.Min(12f, Math.Max(5f, distance * 0.08f));
                Projectile.velocity = (Projectile.velocity * 17f + desired) / 18f;
                Projectile.frame = Kind == MilkcraftBossPetKind.Sphinx ? 5 + (int)(Main.GameUpdateCount / 8 % 3) : 5;
            }
            else
            {
                // At the start of this hook, zero vertical speed means the prior
                // projectile update landed. Gravity still runs every grounded tick
                // so stepping off a ledge or spawning in midair cannot hover forever.
                bool grounded = Projectile.velocity.Y == 0f;
                float desiredX = MathHelper.Clamp(delta.X * 0.08f, -8f, 8f);
                Projectile.velocity.X = (Projectile.velocity.X * 15f + desiredX) / 16f;
                Projectile.velocity.Y = Math.Min(10f, Projectile.velocity.Y + 0.35f);
                if (grounded && Math.Abs(delta.X) > 96f && Projectile.frameCounter % 90 == 0) Projectile.velocity.Y = -5.4f;
                if (grounded && Math.Abs(Projectile.velocity.X) > 0.5f)
                {
                    if (++Projectile.frameCounter % 8 == 0) Projectile.frame = Projectile.frame is >= 1 and <= 3 ? Projectile.frame + 1 : 1;
                    if (Projectile.frame > 4) Projectile.frame = 1;
                }
                else if (!grounded) Projectile.frame = 5;
                else
                {
                    Projectile.frameCounter++;
                    if (Projectile.localAI[1] > 0f)
                    {
                        Projectile.localAI[1]--;
                        if (Projectile.localAI[1] <= 0f) Projectile.frame = 0;
                    }
                    else if (Projectile.frameCounter % 220 == 0)
                    {
                        Projectile.frame = 6 + (int)(Main.GameUpdateCount % 2);
                        Projectile.localAI[1] = 16f;
                    }
                    else if (Projectile.frame > 0 && Projectile.frame < 6) Projectile.frame = 0;
                }
            }
        }

        if (Kind == MilkcraftBossPetKind.Sphinx && Projectile.localAI[0] > 0f)
            Projectile.frame = 5 + (int)(Main.GameUpdateCount / 8 % 3);
        else if (Kind == MilkcraftBossPetKind.CastleCore)
        {
            ulong cycle = Main.GameUpdateCount % 480;
            if (cycle < 240) Projectile.frame = (int)(cycle / 60); // normal face frames 0..3
            else if (cycle < 256) Projectile.frame = 7; // a visible, short upward glance
            else if (cycle < 300) Projectile.frame = 4;
            else if (cycle < 390) Projectile.frame = 5; // sorrow
            else Projectile.frame = 6; // anger
        }
        else if (Floating)
        {
            Projectile.frameCounter++;
            Projectile.frame = Projectile.frameCounter % 160 < 128 ? (Projectile.frameCounter / 12) % 5 : 6 + Projectile.frameCounter / 32 % 2;
        }
        Projectile.direction = Projectile.spriteDirection = Projectile.velocity.X < -0.1f ? -1 : Projectile.velocity.X > 0.1f ? 1 : owner.direction;
        if (Kind == MilkcraftBossPetKind.CastleCore)
        {
            Vector3 light = Projectile.frame == 5 ? new Vector3(0.30f, 0.50f, 0.72f) : Projectile.frame == 6 ? new Vector3(0.70f, 0.20f, 0.16f) : new Vector3(0.65f, 0.54f, 0.24f);
            Lighting.AddLight(Projectile.Center, light * 2.40f);
        }
        else if (Floating || Kind == MilkcraftBossPetKind.Sphinx && Projectile.localAI[0] > 0f)
            Lighting.AddLight(Projectile.Center, LightColor * 0.82f);
        else
            Lighting.AddLight(Projectile.Center, LightColor * 0.13f);
    }

    public override bool OnTileCollide(Vector2 oldVelocity)
    {
        if (!Floating)
        {
            if (oldVelocity.Y > 0f) Projectile.velocity.Y = 0f;
            if (oldVelocity.X != Projectile.velocity.X) Projectile.velocity.X = 0f;
        }
        return false;
    }

    public override bool PreDraw(ref Color lightColor)
    {
        Texture2D body = TextureAssets.Projectile[Type].Value;
        int frameHeight = body.Height / 8;
        Rectangle source = new(0, Projectile.frame * frameHeight, body.Width, frameHeight);
        bool bottomAnchored = !Floating && Projectile.localAI[0] <= 0f;
        Vector2 position = (bottomAnchored ? Projectile.Bottom : Projectile.Center) - Main.screenPosition;
        SpriteEffects effects = Projectile.spriteDirection < 0 ? SpriteEffects.FlipHorizontally : SpriteEffects.None;
        Vector2 origin = bottomAnchored ? new Vector2(32f, 56f) : new Vector2(32f, 32f);
        Main.EntitySpriteDraw(body, position, source, lightColor, Projectile.rotation, origin, Projectile.scale, effects);
        if (!Main.dedServ)
        {
            Texture2D glow = ModContent.Request<Texture2D>($"{Mod.Name}/Content/Projectiles/{MilkcraftBossPetAssets.Name(Kind)}_Glow", ReLogic.Content.AssetRequestMode.ImmediateLoad).Value;
            Main.EntitySpriteDraw(glow, position, source, Color.White, Projectile.rotation, origin, Projectile.scale, effects);
        }
        return false;
    }
}

public sealed class WraithWarriorPetSummon : MilkcraftBossPetSummon { protected override MilkcraftBossPetKind Kind => MilkcraftBossPetKind.WraithWarrior; }
public sealed class JungleLordPetSummon : MilkcraftBossPetSummon { protected override MilkcraftBossPetKind Kind => MilkcraftBossPetKind.JungleLord; }
public sealed class BansheePetSummon : MilkcraftBossPetSummon { protected override MilkcraftBossPetKind Kind => MilkcraftBossPetKind.Banshee; }
public sealed class ObsidianGuardianPetSummon : MilkcraftBossPetSummon { protected override MilkcraftBossPetKind Kind => MilkcraftBossPetKind.ObsidianGuardian; }
public sealed class SphinxPetSummon : MilkcraftBossPetSummon { protected override MilkcraftBossPetKind Kind => MilkcraftBossPetKind.Sphinx; }
public sealed class FrostWitchPetSummon : MilkcraftBossPetSummon { protected override MilkcraftBossPetKind Kind => MilkcraftBossPetKind.FrostWitch; }
public sealed class CastleCorePetSummon : MilkcraftBossPetSummon { protected override MilkcraftBossPetKind Kind => MilkcraftBossPetKind.CastleCore; }

public sealed class WraithWarriorPetBuff : MilkcraftBossPetBuff { protected override MilkcraftBossPetKind Kind => MilkcraftBossPetKind.WraithWarrior; }
public sealed class JungleLordPetBuff : MilkcraftBossPetBuff { protected override MilkcraftBossPetKind Kind => MilkcraftBossPetKind.JungleLord; }
public sealed class BansheePetBuff : MilkcraftBossPetBuff { protected override MilkcraftBossPetKind Kind => MilkcraftBossPetKind.Banshee; }
public sealed class ObsidianGuardianPetBuff : MilkcraftBossPetBuff { protected override MilkcraftBossPetKind Kind => MilkcraftBossPetKind.ObsidianGuardian; }
public sealed class SphinxPetBuff : MilkcraftBossPetBuff { protected override MilkcraftBossPetKind Kind => MilkcraftBossPetKind.Sphinx; }
public sealed class FrostWitchPetBuff : MilkcraftBossPetBuff { protected override MilkcraftBossPetKind Kind => MilkcraftBossPetKind.FrostWitch; }
public sealed class CastleCorePetBuff : MilkcraftBossPetBuff { protected override MilkcraftBossPetKind Kind => MilkcraftBossPetKind.CastleCore; }

public sealed class WraithWarriorPet : MilkcraftBossPetProjectile { protected override MilkcraftBossPetKind Kind => MilkcraftBossPetKind.WraithWarrior; }
public sealed class JungleLordPet : MilkcraftBossPetProjectile { protected override MilkcraftBossPetKind Kind => MilkcraftBossPetKind.JungleLord; }
public sealed class BansheePet : MilkcraftBossPetProjectile { protected override MilkcraftBossPetKind Kind => MilkcraftBossPetKind.Banshee; }
public sealed class ObsidianGuardianPet : MilkcraftBossPetProjectile { protected override MilkcraftBossPetKind Kind => MilkcraftBossPetKind.ObsidianGuardian; }
public sealed class SphinxPet : MilkcraftBossPetProjectile { protected override MilkcraftBossPetKind Kind => MilkcraftBossPetKind.Sphinx; }
public sealed class FrostWitchPet : MilkcraftBossPetProjectile { protected override MilkcraftBossPetKind Kind => MilkcraftBossPetKind.FrostWitch; }
public sealed class CastleCorePet : MilkcraftBossPetProjectile { protected override MilkcraftBossPetKind Kind => MilkcraftBossPetKind.CastleCore; }








