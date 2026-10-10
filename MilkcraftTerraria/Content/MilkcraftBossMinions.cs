using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.GameContent;

namespace MilkcraftTerraria.Content;

/// <summary>Named, killable support NPCs used by the boss attacks. Each archetype has its own movement and attack loop.</summary>
public abstract class MilkcraftBossMinion : ModNPC
{
    protected abstract MilkcraftBossMinionKind Kind { get; }
    protected abstract string Asset { get; }
    protected virtual bool Floating => false;
    protected virtual bool Ranged => Kind is MilkcraftBossMinionKind.WraithArcher or MilkcraftBossMinionKind.SkeletonArcher or MilkcraftBossMinionKind.FrostArcher;

    public override string Texture => $"{Mod.Name}/Content/NPCs/{Asset}";

    public override void SetStaticDefaults()
    {
        Main.npcFrameCount[Type] = 6;
        NPCID.Sets.MPAllowedEnemies[Type] = true;
    }

    public override void SetDefaults()
    {
        NPC.width = Kind == MilkcraftBossMinionKind.FrostNaga ? 26 : Kind == MilkcraftBossMinionKind.ExplosiveSlime ? 30 : 28;
        NPC.height = Kind == MilkcraftBossMinionKind.FrostNaga ? 30 : Kind == MilkcraftBossMinionKind.ExplosiveSlime ? 26 : 42;
        NPC.damage = Kind switch
        {
            MilkcraftBossMinionKind.SkeletonWarrior => 24,
            MilkcraftBossMinionKind.FrostNaga => 27,
            MilkcraftBossMinionKind.ExplosiveSlime => 22,
            _ => 18
        };
        NPC.defense = Kind == MilkcraftBossMinionKind.FrostNaga ? 10 : 6;
        NPC.lifeMax = Kind == MilkcraftBossMinionKind.FrostNaga ? 190 : Kind == MilkcraftBossMinionKind.SkeletonWarrior ? 150 : 105;
        NPC.knockBackResist = 0.45f;
        NPC.value = 0f;
        NPC.npcSlots = 0f;
        NPC.aiStyle = -1;
        NPC.noGravity = Floating;
        NPC.noTileCollide = Floating;
        NPC.netAlways = true;
        NPC.HitSound = SoundID.NPCHit2;
        NPC.DeathSound = Kind == MilkcraftBossMinionKind.ExplosiveSlime ? SoundID.Item14 : SoundID.NPCDeath2;
    }

    public override void OnSpawn(Terraria.DataStructures.IEntitySource source)
    {
        NPC.ai[0] = Kind == MilkcraftBossMinionKind.ExplosiveSlime ? 0f : 55f;
        NPC.ai[1] = 0f;
        NPC.ai[2] = 0f;
        NPC.localAI[1] = 0f;
        NPC.localAI[2] = 0f;
        NPC.netUpdate = true;
    }

    public override void AI()
    {
        if (Ranged && NPC.ai[2] > 0f)
            NPC.ai[2]--;
        else if (Kind != MilkcraftBossMinionKind.ExplosiveSlime && NPC.localAI[2] > 0f)
            NPC.localAI[2]--;
        MilkcraftBossEscortGlobalNPC link = NPC.GetGlobalNPC<MilkcraftBossEscortGlobalNPC>();
        int parent = link.ParentBoss;
        if (parent < 0 || parent >= Main.maxNPCs || !Main.npc[parent].active || Main.npc[parent].life <= 0)
        {
            NPC.active = false;
            return;
        }

        NPC.TargetClosest(false);
        if (NPC.target < 0 || NPC.target >= Main.maxPlayers || !Main.player[NPC.target].active || Main.player[NPC.target].dead)
        {
            NPC.velocity.X *= 0.9f;
            Animate(false, false, false);
            return;
        }

        Player target = Main.player[NPC.target];
        float dx = target.Center.X - NPC.Center.X;
        bool meleeLocked = (Kind is MilkcraftBossMinionKind.FrostNaga or MilkcraftBossMinionKind.SkeletonWarrior)
            && (NPC.ai[1] > 0f || NPC.ai[2] > 0f);
        bool rangedLocked = Ranged && NPC.ai[0] is > 0f and <= 18f;
        if (meleeLocked)
            NPC.direction = NPC.ai[3] < 0f ? -1 : 1;
        else if (rangedLocked)
            NPC.direction = Math.Cos(NPC.ai[3]) < 0f ? -1 : 1;
        else
            NPC.direction = dx < 0f ? -1 : 1;
        NPC.spriteDirection = NPC.direction;
        bool grounded = NPC.velocity.Y == 0f;
        float distance = Math.Abs(dx);

        bool charging = false;
        bool striking = false;
        if (Floating)
        {
            Vector2 desired = target.Center + new Vector2(-NPC.direction * 150f, -75f) - NPC.Center;
            NPC.velocity = (NPC.velocity * 17f + desired.SafeNormalize(Vector2.UnitX) * 5.5f) / 18f;
        }
        else if (Ranged)
        {
            float desiredX = distance < 210f ? -NPC.direction * 3.2f : distance > 360f ? NPC.direction * 3.4f : 0f;
            NPC.velocity.X = MathHelper.Lerp(NPC.velocity.X, desiredX, 0.08f);
            if (grounded && Math.Abs(dx) > 115f && NPC.collideX) NPC.velocity.Y = -6f;
        }
        else
        {
            float speed = Kind == MilkcraftBossMinionKind.ExplosiveSlime ? 3.6f : Kind == MilkcraftBossMinionKind.FrostNaga ? 2.9f : 3.2f;
            NPC.velocity.X = MathHelper.Lerp(NPC.velocity.X, NPC.direction * speed, Kind == MilkcraftBossMinionKind.FrostNaga ? 0.045f : 0.075f);
            if (grounded && distance > 92f && (NPC.collideX || NPC.localAI[1]++ >= 110f))
            {
                NPC.velocity.Y = Kind == MilkcraftBossMinionKind.ExplosiveSlime ? -7.1f : -6.7f;
                NPC.localAI[1] = 0f;
            }
        }

        if (Kind is MilkcraftBossMinionKind.FrostNaga or MilkcraftBossMinionKind.SkeletonWarrior)
        {
            float range = Kind == MilkcraftBossMinionKind.FrostNaga ? 118f : 78f;
            if (NPC.ai[0] > 0f) NPC.ai[0]--;
            if (NPC.ai[1] > 0f)
            {
                charging = true;
                NPC.ai[1]--;
                if (NPC.ai[1] <= 0f)
                {
                    NPC.ai[2] = Kind == MilkcraftBossMinionKind.FrostNaga ? 11f : 8f;
                    if (Main.netMode != NetmodeID.MultiplayerClient && MilkcraftBossProjectileBudget.CanSpawn(parent))
                    {
                        int sweepSlot = Projectile.NewProjectile(NPC.GetSource_FromAI(), NPC.Center, Vector2.Zero,
                            ModContent.ProjectileType<MilkcraftBossProjectile>(),
                            MilkcraftBossProjectileBudget.DamageForBoss(parent, Kind == MilkcraftBossMinionKind.FrostNaga ? 0.52f : 0.44f),
                            0f, Main.maxPlayers, (float)BossProjectileStyle.MinionSweep, parent, NPC.whoAmI);
                        if (sweepSlot >= 0 && sweepSlot < Main.maxProjectiles)
                        {
                            Projectile sweep = Main.projectile[sweepSlot];
                            sweep.ai[0] = (float)BossProjectileStyle.MinionSweep;
                            sweep.ai[1] = parent;
                            sweep.ai[2] = NPC.whoAmI;
                            sweep.hostile = true;
                            sweep.friendly = false;
                            sweep.tileCollide = false;
                            sweep.timeLeft = 12;
                            sweep.netUpdate = true;
                        }
                    }
                    NPC.netUpdate = true;
                }
            }
            if (NPC.ai[2] > 0f)
            {
                striking = true;
                NPC.ai[2]--;
                NPC.damage = Kind == MilkcraftBossMinionKind.FrostNaga ? 27 : 24;
            }
            else
            {
                NPC.damage = 24;
                int idleWidth = Kind == MilkcraftBossMinionKind.FrostNaga ? 26 : 28;
                float centerX = NPC.Center.X;
                NPC.width = idleWidth;
                NPC.position.X = centerX - idleWidth * 0.5f;
                if (NPC.ai[1] <= 0f && NPC.ai[0] <= 0f && distance < range)
                {
                    NPC.ai[1] = Kind == MilkcraftBossMinionKind.FrostNaga ? 30f : 25f;
                    NPC.ai[0] = Kind == MilkcraftBossMinionKind.FrostNaga ? 135f : 110f;
                    NPC.ai[3] = NPC.direction;
                    charging = true;
                    NPC.netUpdate = true;
                }
            }
        }

        if (Ranged)
        {
            if (NPC.ai[0] > 0f) NPC.ai[0]--;
            charging = NPC.ai[0] is > 0f and <= 18f;
            if (NPC.ai[0] is > 0f and <= 18f)
            {
                NPC.velocity.X *= 0.68f;
                if (NPC.ai[3] != 0f)
                {
                    NPC.direction = Math.Cos(NPC.ai[3]) < 0f ? -1 : 1;
                    NPC.spriteDirection = NPC.direction;
                }
            }
            if (NPC.ai[0] == 18f && Main.netMode != NetmodeID.MultiplayerClient)
            {
                Vector2 aimOrigin = NPC.Center + new Vector2(NPC.direction * 10f, -6f);
                NPC.ai[3] = (target.Center + target.velocity * 8f - aimOrigin).ToRotation();
                NPC.direction = Math.Cos(NPC.ai[3]) < 0f ? -1 : 1;
                NPC.spriteDirection = NPC.direction;
                NPC.netUpdate = true;
            }
            if (NPC.ai[0] <= 0f)
            {
                charging = true;
                // This synchronized pose timer advances on clients too; only the
                // hostile projectile itself is created by the server/single-player authority.
                NPC.ai[2] = 6f;
                NPC.netUpdate = true;
                if (Main.netMode != NetmodeID.MultiplayerClient && MilkcraftBossProjectileBudget.CanSpawn(parent))
                {
                    BossProjectileStyle style = Kind == MilkcraftBossMinionKind.FrostNaga ? BossProjectileStyle.IceNaga
                        : Kind == MilkcraftBossMinionKind.FrostArcher ? BossProjectileStyle.FrostSpear
                        : Kind == MilkcraftBossMinionKind.SkeletonArcher ? BossProjectileStyle.BansheeSkull : BossProjectileStyle.WraithArrow;
                    Vector2 origin = NPC.Center + new Vector2(NPC.direction * 10f, -6f);
                    Vector2 velocity = NPC.ai[3].ToRotationVector2() * (Kind == MilkcraftBossMinionKind.FrostNaga ? 7.1f : 7.7f);
                    int damage = MilkcraftBossProjectileBudget.DamageForBoss(parent, 0.72f);
                    int slot = Projectile.NewProjectile(NPC.GetSource_FromAI(), origin, velocity,
                        ModContent.ProjectileType<MilkcraftBossProjectile>(), damage, 0f, Main.maxPlayers,
                        (float)style, parent);
                    if (slot >= 0 && slot < Main.maxProjectiles)
                    {
                        Projectile arrow = Main.projectile[slot];
                        arrow.ai[0] = (float)style;
                        arrow.ai[1] = parent;
                        arrow.ai[2] = NPC.whoAmI;
                        arrow.hostile = true;
                        arrow.friendly = false;
                        arrow.netUpdate = true;
                    }
                }
                NPC.ai[0] = Kind == MilkcraftBossMinionKind.FrostNaga ? 112f : 96f;
                NPC.netUpdate = true;
            }
        }

        if (Kind == MilkcraftBossMinionKind.ExplosiveSlime)
        {
            // ai[0] is reserved for this archetype's hard lifetime. Proximity uses
            // full 2D distance so a player high above cannot prematurely detonate it.
            if (Main.netMode != NetmodeID.MultiplayerClient)
            {
                NPC.ai[0] = Math.Min(180f, NPC.ai[0] + 1f);
                bool nearby = Vector2.DistanceSquared(NPC.Center, target.Center) <= 74f * 74f;
                if (NPC.ai[2] <= 0f && (nearby || NPC.ai[0] >= 140f))
                {
                    float remaining = Math.Max(1f, 180f - NPC.ai[0]);
                    // The timer also ticks on this AI call; add one so a deadline
                    // selected at age 140 detonates at the 180-tick cap, not 179.
                    NPC.ai[1] = Math.Min(43f, remaining + 1f);
                    NPC.ai[2] = 1f;
                    NPC.netUpdate = true;
                }
            }
            if (NPC.ai[1] > 0f) NPC.ai[1]--;
            Animate(grounded, NPC.ai[1] > 6f, NPC.ai[1] is > 0f and <= 6f);
            if ((NPC.ai[2] > 0f && NPC.ai[1] <= 0f || NPC.ai[0] >= 180f) && Main.netMode != NetmodeID.MultiplayerClient)
            {
                for (int i = 0; i < 8; i++)
                {
                    // The summoned explosive slime shares its summoner's projectile budget.
                    // If the boss already has forty live attacks, the slime still detonates
                    // visually but cannot add an unbounded extra wave.
                    if (!MilkcraftBossProjectileBudget.CanSpawn(link.ParentBoss)) break;
                    float angle = MathHelper.TwoPi * i / 8f;
                    int slot = Projectile.NewProjectile(NPC.GetSource_FromAI(), NPC.Center,
                        new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle)) * 5.4f,
                        ModContent.ProjectileType<MilkcraftBossProjectile>(),
                        MilkcraftBossProjectileBudget.DamageForBoss(link.ParentBoss, 0.34f), 0f, Main.maxPlayers,
                        (float)BossProjectileStyle.ObsidianFlame,
                        Math.Clamp(link.ParentBoss, 0, Main.maxNPCs - 1));
                    if (slot >= 0 && slot < Main.maxProjectiles)
                    {
                        Projectile flame = Main.projectile[slot];
                        flame.ai[0] = (float)BossProjectileStyle.ObsidianFlame;
                        flame.ai[1] = link.ParentBoss;
                        flame.ai[2] = NPC.whoAmI;
                        flame.hostile = true;
                        flame.friendly = false;
                        flame.netUpdate = true;
                    }
                }
                NPC.netUpdate = true;
                NPC.active = false;
            }
        }
        if (Kind != MilkcraftBossMinionKind.ExplosiveSlime)
            Animate(grounded, charging || NPC.ai[1] > 0f, striking || NPC.ai[2] > 0f || NPC.localAI[2] > 0f);
    }

    public override bool CanHitPlayer(Player target, ref int cooldownSlot)
    {
        if (Kind is MilkcraftBossMinionKind.FrostNaga or MilkcraftBossMinionKind.SkeletonWarrior)
            return false; // The attached sweep projectile has the attack hitbox; idle body cannot hit.
        return true;
    }

    private void Animate(bool grounded, bool charging, bool striking)
    {
        if (!grounded && !Floating) NPC.localAI[3] = 5f;
        else if (striking) NPC.localAI[3] = 4f;
        else if (charging) NPC.localAI[3] = 3f;
        else if (Math.Abs(NPC.velocity.X) > 0.55f)
        {
            NPC.frameCounter += Math.Abs(NPC.velocity.X) * 0.35f;
            NPC.localAI[3] = NPC.frameCounter % 20f < 10f ? 1f : 2f;
        }
        else NPC.localAI[3] = 0f;
    }

    public override void FindFrame(int frameHeight)
    {
        NPC.frame.Width = 64;
        NPC.frame.Height = frameHeight;
        NPC.frame.Y = Math.Clamp((int)NPC.localAI[3], 0, 5) * frameHeight;
    }

    public override bool PreDraw(SpriteBatch spriteBatch, Vector2 screenPos, Color drawColor)
    {
        Texture2D texture = TextureAssets.Npc[Type].Value;
        Rectangle source = new(0, (int)NPC.localAI[3] * NPC.frame.Height, 64, NPC.frame.Height);
        Vector2 origin = new(32f, 60f);
        spriteBatch.Draw(texture, NPC.Bottom - screenPos, source, drawColor, NPC.rotation, origin, NPC.scale,
            NPC.spriteDirection < 0 ? SpriteEffects.FlipHorizontally : SpriteEffects.None, 0f);
        if (!Main.dedServ)
        {
            Texture2D glow = ModContent.Request<Texture2D>($"{Mod.Name}/Content/NPCs/{Asset}_Glow", ReLogic.Content.AssetRequestMode.ImmediateLoad).Value;
            spriteBatch.Draw(glow, NPC.Bottom - screenPos, source, Color.White, NPC.rotation, origin, NPC.scale,
                NPC.spriteDirection < 0 ? SpriteEffects.FlipHorizontally : SpriteEffects.None, 0f);
            Vector3 light = Kind switch
            {
                MilkcraftBossMinionKind.WraithArcher => new Vector3(0.22f, 0.12f, 0.38f),
                MilkcraftBossMinionKind.ExplosiveSlime => new Vector3(0.38f, 0.28f, 0.08f),
                MilkcraftBossMinionKind.FrostNaga => new Vector3(0.18f, 0.34f, 0.42f),
                MilkcraftBossMinionKind.FrostArcher => new Vector3(0.28f, 0.18f, 0.45f),
                _ => Vector3.Zero
            };
            Lighting.AddLight(NPC.Center, light * 0.35f);
        }
        return false;
    }

    public override void OnKill()
    {
        // Already-fired escort arrows remain independent; the parent boss owns their budget and clears them on despawn.
    }
}

public sealed class WraithArcherMinion : MilkcraftBossMinion { protected override MilkcraftBossMinionKind Kind => MilkcraftBossMinionKind.WraithArcher; protected override string Asset => "WraithArcher"; }
public sealed class SkeletonWarriorMinion : MilkcraftBossMinion { protected override MilkcraftBossMinionKind Kind => MilkcraftBossMinionKind.SkeletonWarrior; protected override string Asset => "SkeletonWarrior"; }
public sealed class SkeletonArcherMinion : MilkcraftBossMinion { protected override MilkcraftBossMinionKind Kind => MilkcraftBossMinionKind.SkeletonArcher; protected override string Asset => "SkeletonArcher"; }
public sealed class ExplosiveSlimeMinion : MilkcraftBossMinion { protected override MilkcraftBossMinionKind Kind => MilkcraftBossMinionKind.ExplosiveSlime; protected override string Asset => "ExplosiveSlime"; }
public sealed class FrostNagaMinion : MilkcraftBossMinion { protected override MilkcraftBossMinionKind Kind => MilkcraftBossMinionKind.FrostNaga; protected override string Asset => "FrostNaga"; }
public sealed class FrostArcherMinion : MilkcraftBossMinion { protected override MilkcraftBossMinionKind Kind => MilkcraftBossMinionKind.FrostArcher; protected override string Asset => "FrostArcher"; }

/// <summary>Breakable orbiting Castle Core crystal; its survival controls the follow-up volley.</summary>
public sealed class CastleSoulCrystal : ModNPC
{
    public override string Texture => $"{Mod.Name}/Content/Projectiles/CastleBlue";
    public override void SetDefaults()
    {
        NPC.width = NPC.height = 30;
        NPC.lifeMax = 180;
        NPC.defense = 8;
        NPC.damage = 0;
        NPC.noGravity = NPC.noTileCollide = true;
        NPC.aiStyle = -1;
        NPC.knockBackResist = 0.75f;
        NPC.value = 0f;
        NPC.npcSlots = 0f;
        NPC.dontCountMe = true;
        NPC.netAlways = true;
    }
    public override void AI()
    {
        NPC.ai[2]++;
        if (NPC.ai[2] >= 150f)
        {
            NPC.active = false;
            NPC.netUpdate = true;
            return;
        }
        int parent = (int)NPC.ai[0];
        if (parent < 0 || parent >= Main.maxNPCs || !Main.npc[parent].active)
        {
            NPC.active = false;
            return;
        }
        NPC boss = Main.npc[parent];
        float angle = NPC.ai[1] + (float)Main.GameUpdateCount * 0.035f;
        Vector2 desired = boss.Center + new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle)) * 190f;
        NPC.velocity = (NPC.velocity * 11f + (desired - NPC.Center) * 0.12f) / 12f;
        NPC.rotation += 0.035f * (NPC.ai[1] < MathHelper.Pi ? 1f : -1f);
        Lighting.AddLight(NPC.Center, 0.08f, 0.28f, 0.55f);
        if (Main.netMode != NetmodeID.MultiplayerClient && NPC.ai[2] % 15f == 0f)
            NPC.netUpdate = true;
    }
    public override bool PreDraw(SpriteBatch spriteBatch, Vector2 screenPos, Color drawColor)
    {
        Texture2D texture = ModContent.Request<Texture2D>($"{Mod.Name}/Content/Projectiles/CastleBlue", ReLogic.Content.AssetRequestMode.ImmediateLoad).Value;
        Vector2 center = NPC.Center - screenPos;
        spriteBatch.Draw(texture, center, null, Color.White, NPC.rotation, texture.Size() * 0.5f, 0.9f, SpriteEffects.None, 0f);
        return false;
    }
    public override void HitEffect(NPC.HitInfo hit)
    {
        if (NPC.life <= 0 && Main.netMode != NetmodeID.Server)
            for (int i = 0; i < 8; i++) Dust.NewDust(NPC.position, NPC.width, NPC.height, DustID.BlueFairy, Main.rand.NextFloat(-2f, 2f), Main.rand.NextFloat(-2f, 2f));
    }
}
