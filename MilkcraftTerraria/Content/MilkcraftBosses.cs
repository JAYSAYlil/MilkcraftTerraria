using System;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.Audio;
using Terraria.DataStructures;
using Terraria.GameContent;
using Terraria.GameContent.ItemDropRules;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;
using ReLogic.Content;
using MilkcraftTerraria.Content.Items;

namespace MilkcraftTerraria.Content;

public enum MilkcraftBossKind : byte
{
    WraithWarrior, JungleLord, Banshee, ObsidianGuardian, Sphinx, FrostWitch, CastleCore
}

public enum MilkcraftBossMinionKind : byte
{
    WraithArcher, SkeletonWarrior, SkeletonArcher, ExplosiveSlime, FrostNaga, FrostArcher
}

/// <summary>Server-authoritative, world-persisted flags for the seven new bosses.</summary>
public sealed class MilkcraftBossWorldSystem : ModSystem
{
    private const string SavePrefix = "milkcraftBoss_";
    internal static bool WraithWarriorDowned, JungleLordDowned, BansheeDowned;
    internal static bool ObsidianGuardianDowned, SphinxDowned, FrostWitchDowned, CastleCoreDowned;

    internal static bool DuskTriadDowned => WraithWarriorDowned && JungleLordDowned && BansheeDowned;

    public override void ClearWorld()
    {
        WraithWarriorDowned = JungleLordDowned = BansheeDowned = false;
        ObsidianGuardianDowned = SphinxDowned = FrostWitchDowned = CastleCoreDowned = false;
    }

    public override void SaveWorldData(Terraria.ModLoader.IO.TagCompound tag)
    {
        if (WraithWarriorDowned) tag[SavePrefix + "WraithWarrior"] = true;
        if (JungleLordDowned) tag[SavePrefix + "JungleLord"] = true;
        if (BansheeDowned) tag[SavePrefix + "Banshee"] = true;
        if (ObsidianGuardianDowned) tag[SavePrefix + "ObsidianGuardian"] = true;
        if (SphinxDowned) tag[SavePrefix + "Sphinx"] = true;
        if (FrostWitchDowned) tag[SavePrefix + "FrostWitch"] = true;
        if (CastleCoreDowned) tag[SavePrefix + "CastleCore"] = true;
    }

    public override void LoadWorldData(Terraria.ModLoader.IO.TagCompound tag)
    {
        WraithWarriorDowned = tag.GetBool(SavePrefix + "WraithWarrior");
        JungleLordDowned = tag.GetBool(SavePrefix + "JungleLord");
        BansheeDowned = tag.GetBool(SavePrefix + "Banshee");
        ObsidianGuardianDowned = tag.GetBool(SavePrefix + "ObsidianGuardian");
        SphinxDowned = tag.GetBool(SavePrefix + "Sphinx");
        FrostWitchDowned = tag.GetBool(SavePrefix + "FrostWitch");
        CastleCoreDowned = tag.GetBool(SavePrefix + "CastleCore");
    }

    public override void NetSend(BinaryWriter writer)
    {
        writer.Write(WraithWarriorDowned); writer.Write(JungleLordDowned); writer.Write(BansheeDowned);
        writer.Write(ObsidianGuardianDowned); writer.Write(SphinxDowned); writer.Write(FrostWitchDowned);
        writer.Write(CastleCoreDowned);
    }

    public override void NetReceive(BinaryReader reader)
    {
        WraithWarriorDowned = reader.ReadBoolean(); JungleLordDowned = reader.ReadBoolean(); BansheeDowned = reader.ReadBoolean();
        ObsidianGuardianDowned = reader.ReadBoolean(); SphinxDowned = reader.ReadBoolean(); FrostWitchDowned = reader.ReadBoolean();
        CastleCoreDowned = reader.ReadBoolean();
    }

    internal static void MarkDowned(MilkcraftBossKind kind)
    {
        if (Main.netMode == NetmodeID.MultiplayerClient) return;
        switch (kind)
        {
            case MilkcraftBossKind.WraithWarrior: WraithWarriorDowned = true; break;
            case MilkcraftBossKind.JungleLord: JungleLordDowned = true; break;
            case MilkcraftBossKind.Banshee: BansheeDowned = true; break;
            case MilkcraftBossKind.ObsidianGuardian: ObsidianGuardianDowned = true; break;
            case MilkcraftBossKind.Sphinx: SphinxDowned = true; break;
            case MilkcraftBossKind.FrostWitch: FrostWitchDowned = true; break;
            case MilkcraftBossKind.CastleCore: CastleCoreDowned = true; break;
        }
        if (Main.netMode == NetmodeID.Server)
            NetMessage.SendData(MessageID.WorldData);
    }
}

internal static class MilkcraftBossProjectileBudget
{
    internal static int CountForBoss(int bossIndex)
    {
        if (bossIndex < 0 || bossIndex >= Main.maxNPCs) return 0;
        int projectileType = ModContent.ProjectileType<MilkcraftBossProjectile>();
        int count = 0;
        for (int i = 0; i < Main.maxProjectiles; i++)
        {
            Projectile p = Main.projectile[i];
            if (p.active && p.type == projectileType && (int)p.ai[1] == bossIndex) count++;
        }
        return count;
    }

    internal static bool CanSpawn(int bossIndex) => CountForBoss(bossIndex) < 40;

    internal static int DamageForBoss(int bossIndex, float scale)
    {
        if (bossIndex < 0 || bossIndex >= Main.maxNPCs) return 1;
        NPC boss = Main.npc[bossIndex];
        if (!boss.active || boss.life <= 0 || boss.ModNPC is not MilkcraftBossNPC encounter) return 1;
        return Math.Max(1, (int)(encounter.AttackDamage(scale) * 0.72f));
    }
}

/// <summary>Shared rendering, target/despawn safety, difficulty scaling, and reward registration.</summary>
public abstract class MilkcraftBossNPC : ModNPC
{
    protected abstract MilkcraftBossKind Kind { get; }
    protected abstract string AssetName { get; }
    protected abstract int BagType { get; }
    protected abstract int NormalLife { get; }
    protected abstract int ContactDamage { get; }
    protected abstract int Defense { get; }
    protected abstract int Width { get; }
    protected abstract int Height { get; }
    protected abstract bool Floats { get; }
    protected virtual bool IsFinalCastle => false;
    protected virtual int BossMusic => MusicID.Boss1;
    protected virtual int PoseCount => 4;
    protected int CombatStage => (int)NPC.ai[0]; // 0 approach, 1 telegraph, 2 attack, 3 recovery
    protected int CombatMove => (int)NPC.ai[2];
    protected int CombatTick => (int)NPC.ai[1];
    protected Vector2 LockedPoint => _lockedPoint;
    protected Vector2 _lockedPoint, _castleAnchor;
    // _runFrame is a four-step gait phase (0..3); source frame mapping is
    // [1,3,2,3] to preserve the authored contact/pass stride cycle.
    protected int _warningTicks = 45, _nextMove, _runFrame, _groundedFlag, _phase = -1, _transitionTicks;
    protected int _soulCrystalSurvived;
    protected float _runProgress;
    // Multiplayer clients maintain a presentation-only stride phase. These are
    // intentionally excluded from ExtraAI so server-authored combat state stays
    // byte-for-byte unchanged.
    private float _clientRunProgress;
    private int _clientRunFrame;
    private bool _clientWasWalking;
    private static readonly int[] GroundStrideFrames = { 1, 3, 2, 3 };
    protected bool _castleAnchorSet;
    private bool _lowPhaseTransitionStarted;
    private int _lowPhaseTransitionTicks;
    private int _despawnTicks;

    // Attack lock/sequence data are server-authoritative and ride with NPC extra AI.
    public override void SendExtraAI(BinaryWriter writer)
    {
        writer.Write(_lockedPoint.X); writer.Write(_lockedPoint.Y);
        writer.Write(_castleAnchor.X); writer.Write(_castleAnchor.Y);
        writer.Write(_warningTicks); writer.Write(_nextMove); writer.Write(_runFrame);
        writer.Write(_groundedFlag); writer.Write(_phase); writer.Write(_transitionTicks);
        writer.Write(_soulCrystalSurvived); writer.Write(_runProgress); writer.Write(_castleAnchorSet);
        writer.Write(_lowPhaseTransitionStarted); writer.Write(_lowPhaseTransitionTicks);
        writer.Write(_despawnTicks);
    }

    public override void ReceiveExtraAI(BinaryReader reader)
    {
        _lockedPoint = new Vector2(reader.ReadSingle(), reader.ReadSingle());
        _castleAnchor = new Vector2(reader.ReadSingle(), reader.ReadSingle());
        _warningTicks = reader.ReadInt32(); _nextMove = reader.ReadInt32(); _runFrame = reader.ReadInt32();
        _groundedFlag = reader.ReadInt32(); _phase = reader.ReadInt32(); _transitionTicks = reader.ReadInt32();
        _soulCrystalSurvived = reader.ReadInt32(); _runProgress = reader.ReadSingle(); _castleAnchorSet = reader.ReadBoolean();
        _lowPhaseTransitionStarted = reader.ReadBoolean(); _lowPhaseTransitionTicks = reader.ReadInt32();
        _despawnTicks = reader.ReadInt32();
    }

    public override string Texture => $"{Mod.Name}/Content/NPCs/{AssetName}";
    public override string BossHeadTexture => $"{Mod.Name}/Content/NPCs/{AssetName}_Head_Boss";

    public override void SetStaticDefaults()
    {
        Music = BossMusic;
        Main.npcFrameCount[Type] = PoseCount;
        NPCID.Sets.MPAllowedEnemies[Type] = true;
    }

    public override void SetDefaults()
    {
        _despawnTicks = 0;
        NPC.width = Width;
        NPC.height = Height;
        NPC.damage = ContactDamage;
        NPC.defense = Defense;
        NPC.lifeMax = NormalLife;
        NPC.value = Item.buyPrice(gold: 5);
        NPC.knockBackResist = 0f;
        NPC.aiStyle = -1;
        NPC.boss = true;
        NPC.npcSlots = 5f;
        NPC.noGravity = Floats;
        NPC.noTileCollide = Floats;
        NPC.netAlways = true;
        NPC.HitSound = SoundID.NPCHit1;
        NPC.DeathSound = SoundID.NPCDeath1;
    }

    public override void ApplyDifficultyAndPlayerScaling(int numPlayers, float balance, float bossAdjustment)
    {
        // Terraria calls this from its native expert/master multiplayer scaling
        // pass. Keep the existing single-player mode multipliers and apply only
        // the native balance factor once; bossAdjustment is an extra native
        // tuning knob and is intentionally not layered onto our life values.
        if (numPlayers <= 1 || Math.Abs(balance - 1f) < 0.0001f) return;
        NPC.lifeMax = Math.Max(1, (int)(NPC.lifeMax * balance));
        NPC.life = Math.Min(NPC.life, NPC.lifeMax);
    }

    public override bool CanHitPlayer(Player target, ref int cooldownSlot)
    {
        if (_despawnTicks > 0 || _lowPhaseTransitionTicks > 0 || (IsFinalCastle && _transitionTicks > 0)) return false;
        if (CombatStage is 1 or 3) return false;
        if (!Floats) return CombatStage is 0 or 2;

        // Floating pursuit itself is harmless. Only the clearly telegraphed dash
        // window can deal contact damage; the anchored Castle Core never body-checks.
        return Kind switch
        {
            MilkcraftBossKind.Banshee => CombatStage == 2 && CombatMove == 3 && CombatTick < 30,
            MilkcraftBossKind.Sphinx => CombatStage == 2 && CombatMove == 0 && CombatTick < 30,
            MilkcraftBossKind.FrostWitch => CombatStage == 2 && CombatMove == 4 && CombatTick < 24,
            _ => false
        };
    }

    public override void ModifyNPCLoot(NPCLoot npcLoot)
    {
        npcLoot.Add(ItemDropRule.BossBag(BagType));
        npcLoot.Add(ItemDropRule.Common(BossItemTypes.Trophy(Kind), 10));
        npcLoot.Add(ItemDropRule.MasterModeCommonDrop(BossItemTypes.Relic(Kind)));
        npcLoot.Add(ItemDropRule.ByCondition(new Conditions.NotExpert(), BossItemTypes.Mask(Kind), 7));
        // Pet chance is one roll per normal kill or one roll from the expert/master bag.
        // Do not add a second Master-mode body drop: it duplicates the bag chance.
        npcLoot.Add(ItemDropRule.ByCondition(new Conditions.NotExpert(), BossItemTypes.Pet(Kind), 4));
        if (Kind is MilkcraftBossKind.WraithWarrior or MilkcraftBossKind.JungleLord or MilkcraftBossKind.Banshee)
            npcLoot.Add(ItemDropRule.ByCondition(new Conditions.NotExpert(), ModContent.ItemType<WhiteTigerSummon>(), 10));
        AddNormalFixedRewards(npcLoot);
    }

    private void AddNormalFixedRewards(NPCLoot loot)
    {
        var normal = new Conditions.NotExpert();
        foreach ((int type, int count) in BossItemTypes.FixedRewards(Kind))
            loot.Add(ItemDropRule.ByCondition(normal, type, 1, count, count));
        if (Kind == MilkcraftBossKind.FrostWitch)
            loot.Add(ItemDropRule.ByCondition(normal, ModContent.ItemType<WhiteDodoSummon>(), 4));
    }

    public override void AI()
    {
        Color light = Kind switch
        {
            MilkcraftBossKind.WraithWarrior => new Color(72, 35, 115),
            MilkcraftBossKind.JungleLord => new Color(33, 98, 34),
            MilkcraftBossKind.Banshee => new Color(38, 82, 150),
            MilkcraftBossKind.ObsidianGuardian => new Color(135, 58, 25),
            MilkcraftBossKind.Sphinx => new Color(143, 106, 38),
            MilkcraftBossKind.FrostWitch => new Color(64, 128, 172),
            _ => NPC.life * 100 > NPC.lifeMax * 80 ? new Color(73, 110, 81)
                : NPC.life * 100 > NPC.lifeMax * 50 ? new Color(58, 91, 164) : new Color(150, 48, 55)
        };
        float lightPower = IsFinalCastle && NPC.ai[3] > 0.5f ? 0.58f : IsFinalCastle && NPC.life * 100 <= NPC.lifeMax * 50 ? 0.52f : 0.48f;
        Lighting.AddLight(NPC.Center, light.ToVector3() * lightPower);
        if (!AcquireTarget(out Player target))
            return;
        if (Main.netMode == NetmodeID.MultiplayerClient)
        {
            // Encounter decisions, locked aim points, and attack creation belong to the
            // server. Clients advance only a local presentation clock so telegraphs and
            // poses remain visible between authoritative NPC sync packets.
            if (IsFinalCastle && _transitionTicks > 0)
            {
                _transitionTicks--;
                int face = NPC.life * 100 > NPC.lifeMax * 80 ? 0 : NPC.life * 100 > NPC.lifeMax * 50 ? 1 : 2;
                Pose(face);
                Telegraph(NPC.Center, face == 0 ? Color.LightGreen : face == 1 ? Color.LightSkyBlue : Color.OrangeRed, 58);
                return;
            }
            if (_lowPhaseTransitionTicks > 0)
            {
                _lowPhaseTransitionTicks--;
                Pose(2);
                return;
            }
            if (CombatStage == 1)
            {
                int face = NPC.life * 100 > NPC.lifeMax * 80 ? 0 : NPC.life * 100 > NPC.lifeMax * 50 ? 1 : 2;
                Pose(IsFinalCastle ? face : 2);
                NPC.ai[1]++;
                if (NPC.ai[1] >= _warningTicks) { NPC.ai[0] = 2f; NPC.ai[1] = 0f; Pose(IsFinalCastle && face < 2 ? face : 3); }
            }
            else if (CombatStage == 2)
            {
                int face = NPC.life * 100 > NPC.lifeMax * 80 ? 0 : NPC.life * 100 > NPC.lifeMax * 50 ? 1 : 3;
                Pose(IsFinalCastle ? face : 3);
                NPC.ai[1]++;
            }
            else if (CombatStage == 3)
            {
                int face = NPC.life * 100 > NPC.lifeMax * 80 ? 0 : NPC.life * 100 > NPC.lifeMax * 50 ? 1 : 2;
                Pose(IsFinalCastle ? face : 0);
                NPC.ai[1]++;
            }
            else if (!Floats)
                AdvanceClientGroundGait();
            else
                Pose(Math.Abs(NPC.velocity.X) > 0.6f ? 1 : 0);
            return;
        }
        NPC.dontTakeDamage = false;
        NPC.timeLeft = 600;
        if (!IsFinalCastle && !_lowPhaseTransitionStarted && NPC.life * 2 <= NPC.lifeMax)
        {
            _lowPhaseTransitionStarted = true;
            _lowPhaseTransitionTicks = 60;
            NPC.ai[0] = NPC.ai[1] = 0f;
            NPC.velocity = Vector2.Zero;
            NPC.dontTakeDamage = true;
            ClearOwnedProjectiles();
            NPC.netUpdate = true;
        }
        if (_lowPhaseTransitionTicks > 0)
        {
            NPC.velocity *= 0f;
            NPC.dontTakeDamage = true;
            Pose(2);
            Color phaseColor = Kind switch
            {
                MilkcraftBossKind.WraithWarrior => new Color(185, 110, 255),
                MilkcraftBossKind.JungleLord => new Color(125, 230, 78),
                MilkcraftBossKind.Banshee => new Color(110, 190, 255),
                MilkcraftBossKind.ObsidianGuardian => new Color(255, 106, 54),
                MilkcraftBossKind.Sphinx => new Color(255, 208, 112),
                MilkcraftBossKind.FrostWitch => new Color(130, 222, 255),
                _ => Color.White
            };
            Telegraph(NPC.Center, phaseColor, 52);
            if (--_lowPhaseTransitionTicks == 0)
            {
                NPC.dontTakeDamage = false;
                NPC.netUpdate = true;
            }
            return;
        }
        RunBossAI(target);
        if (Main.netMode != NetmodeID.Server && Main.rand.NextBool(6))
            EmitAmbientDust();
    }

    protected abstract void RunBossAI(Player target);

    private bool AcquireTarget(out Player target)
    {
        NPC.TargetClosest(false);
        target = NPC.target >= 0 && NPC.target < Main.maxPlayers ? Main.player[NPC.target] : null;
        if (target is null || !target.active || target.dead || target.ghost)
        {
            NPC.velocity.Y = Floats ? -3f : NPC.velocity.Y;
            NPC.timeLeft = Math.Min(NPC.timeLeft, 20);
            NPC.dontTakeDamage = true;
            AdvanceNoTargetDespawn();
            return false;
        }

        if (Vector2.DistanceSquared(NPC.Center, target.Center) > 3200f * 3200f)
        {
            NPC.timeLeft = Math.Min(NPC.timeLeft, 20);
            NPC.dontTakeDamage = true;
            AdvanceNoTargetDespawn();
            return false;
        }
        if (_despawnTicks > 0)
        {
            _despawnTicks = 0;
            NPC.netUpdate = true;
        }
        return true;
    }

    private void AdvanceNoTargetDespawn()
    {
        if (Main.netMode == NetmodeID.MultiplayerClient) return;
        if (_despawnTicks == 0)
        {
            CleanupEncounterHazards();
            NPC.netUpdate = true;
        }
        _despawnTicks++;
        if (_despawnTicks >= 20)
        {
            // Native CheckActive can refresh NPC.timeLeft while any active player
            // overlaps a boss-sized box, even after that player died. A separate
            // server counter guarantees lost/remote encounters terminate without
            // invoking CheckDead, rewards, downed flags, or a kill callback.
            NPC.active = false;
            NPC.netUpdate = true;
        }
    }

    private void CleanupEncounterHazards()
    {
        if (Main.netMode == NetmodeID.MultiplayerClient) return;
        ClearOwnedProjectiles();
        for (int i = 0; i < Main.maxNPCs; i++)
        {
            NPC child = Main.npc[i];
            bool escort = child.active && child.GetGlobalNPC<MilkcraftBossEscortGlobalNPC>().ParentBoss == NPC.whoAmI;
            bool crystal = child.active && child.type == ModContent.NPCType<CastleSoulCrystal>()
                && (int)child.ai[0] == NPC.whoAmI;
            if (escort || crystal) child.active = false;
        }
        NPC.netUpdate = true;
    }

    private void ClearOwnedProjectiles()
    {
        for (int i = 0; i < Main.maxProjectiles; i++)
        {
            Projectile p = Main.projectile[i];
            if (p.active && p.type == ModContent.ProjectileType<MilkcraftBossProjectile>() && (int)p.ai[1] == NPC.whoAmI)
                p.Kill();
        }
    }

    protected float Tick(int period)
    {
        float timer = NPC.ai[1]++;
        if (timer >= period)
        {
            NPC.ai[1] = 0f;
            NPC.ai[2]++;
            if (NPC.ai[2] >= 3f) NPC.ai[2] = 0f;
            NPC.netUpdate = true;
            return 0f;
        }
        return timer;
    }

    protected Vector2 LockPlayerPoint(Player target, float predictionFrames = 8f)
        => target.Center + target.velocity * MathHelper.Clamp(predictionFrames, 0f, 12f);

    protected void BeginMove(int move, Vector2 lockedPoint, int warningFrames)
    {
        NPC.ai[0] = 1f;
        NPC.ai[1] = 0f;
        NPC.ai[2] = move;
        _lockedPoint = lockedPoint;
        _warningTicks = warningFrames;
        if (Main.netMode != NetmodeID.Server)
            SoundEngine.PlaySound(SoundID.Roar, NPC.Center);
        NPC.netUpdate = true;
    }

    protected bool RunTelegraph(Color color, int radius = 28)
    {
        Pose(2);
        Telegraph(LockedPoint, color, radius);
        if (++NPC.ai[1] < _warningTicks) return false;
        NPC.ai[0] = 2f;
        NPC.ai[1] = 0f;
        NPC.netUpdate = true;
        return true;
    }

    protected bool RunAttackTimer(int activeFrames)
    {
        if (++NPC.ai[1] < activeFrames) return false;
        NPC.ai[0] = 3f;
        NPC.ai[1] = 0f;
        NPC.netUpdate = true;
        return true;
    }

    protected bool RunRecovery(int recoverFrames)
    {
        Pose(0);
        if (++NPC.ai[1] < recoverFrames) return false;
        NPC.ai[0] = 0f;
        NPC.ai[1] = 0f;
        NPC.netUpdate = true;
        return true;
    }

    protected bool ReadyMove(int cooldown, int moveCount, Player target, float predictionFrames = 8f, int warningFrames = 45)
    {
        if (++NPC.ai[1] < cooldown) return false;
        int move = _nextMove % moveCount;
        _nextMove = (move + 1) % moveCount;
        BeginMove(move, LockPlayerPoint(target, predictionFrames), warningFrames);
        return true;
    }

    protected bool ReadyLockedMove(int cooldown, int moveCount, Player target, int warningFrames)
    {
        if (++NPC.ai[1] < cooldown) return false;
        int move = _nextMove % moveCount;
        _nextMove = (move + 1) % moveCount;
        BeginMove(move, LockPlayerPoint(target, 8f), warningFrames);
        return true;
    }

    protected void FloatOrbit(Player target, float radiusX, float radiusY, float speed, float phase, float inertia = 28f)
    {
        float wobble = (float)Math.Sin(Main.GameUpdateCount * 0.027f + phase) * 24f;
        // Choose the orbit side from relative position, not the last steering
        // direction: using direction here makes the target swap sides after a
        // small overshoot and produces visible left/right oscillation.
        int side = NPC.Center.X < target.Center.X ? -1 : 1;
        Vector2 point = target.Center + new Vector2(side * radiusX, -radiusY + wobble);
        FloatToward(point, speed, inertia);
        NPC.direction = target.Center.X < NPC.Center.X ? -1 : 1;
        NPC.spriteDirection = NPC.direction;
        Pose(Math.Abs(NPC.velocity.X) > 1.5f ? 1 : 0);
    }

    protected void RingSector(BossProjectileStyle style, int count, float speed, float centerAngle, float gapRadians, float phase = 0f, float damageScale = 0.4f)
    {
        if (Main.netMode == NetmodeID.MultiplayerClient || !MilkcraftBossProjectileBudget.CanSpawn(NPC.whoAmI)) return;
        if (style is BossProjectileStyle.BansheeWave or BossProjectileStyle.FrostRing or BossProjectileStyle.ObsidianFlame or BossProjectileStyle.SandShot
            or BossProjectileStyle.CastleGreenRing or BossProjectileStyle.CastleBlueRing or BossProjectileStyle.CastleRedRing)
        {
            BossProjectileStyle ringStyle = style == BossProjectileStyle.ObsidianFlame ? BossProjectileStyle.ObsidianRing
                : style == BossProjectileStyle.SandShot ? BossProjectileStyle.SphinxRing : style;
            int slot = Projectile.NewProjectile(NPC.GetSource_FromAI(), NPC.Center, Vector2.Zero,
                ModContent.ProjectileType<MilkcraftBossProjectile>(), AttackDamage(damageScale), 0f, Main.maxPlayers,
                (float)ringStyle, NPC.whoAmI, centerAngle + phase);
            if (slot >= 0 && slot < Main.maxProjectiles)
            {
                Main.projectile[slot].ai[0] = (float)ringStyle;
                Main.projectile[slot].ai[1] = NPC.whoAmI;
                Main.projectile[slot].ai[2] = centerAngle + phase;
                Main.projectile[slot].hostile = true;
                Main.projectile[slot].friendly = false;
                Main.projectile[slot].tileCollide = false;
                Main.projectile[slot].timeLeft = 46;
                Main.projectile[slot].netUpdate = true;
            }
            return;
        }
        float step = MathHelper.TwoPi / count;
        for (int i = 0; i < count; i++)
        {
            float angle = centerAngle + phase + i * step;
            if (Math.Abs(MathHelper.WrapAngle(angle - centerAngle)) < gapRadians * 0.5f) continue;
            Fire(null, NPC.Center, new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle)) * speed, style, damageScale);
        }
    }

    protected void FireFan(BossProjectileStyle style, int count, float speed, float centerAngle, float arcRadians, float damageScale)
    {
        if (count <= 0 || Main.netMode == NetmodeID.MultiplayerClient) return;
        for (int i = 0; i < count; i++)
        {
            float t = count == 1 ? 0.5f : i / (float)(count - 1);
            float angle = centerAngle + MathHelper.Lerp(-arcRadians * 0.5f, arcRadians * 0.5f, t);
            Fire(null, NPC.Center, angle.ToRotationVector2() * speed, style, damageScale);
        }
    }

    protected void SpearSpin(float startAngle, float damageScale)
    {
        if (Main.netMode == NetmodeID.MultiplayerClient || !MilkcraftBossProjectileBudget.CanSpawn(NPC.whoAmI)) return;
        int slot = Projectile.NewProjectile(NPC.GetSource_FromAI(), NPC.Center, Vector2.Zero,
            ModContent.ProjectileType<MilkcraftBossProjectile>(), AttackDamage(damageScale), 0f,
            Main.maxPlayers, (float)BossProjectileStyle.WraithSpin, NPC.whoAmI, startAngle);
        if (slot < 0 || slot >= Main.maxProjectiles) return;
        Projectile spin = Main.projectile[slot];
        spin.ai[0] = (float)BossProjectileStyle.WraithSpin;
        spin.ai[1] = NPC.whoAmI;
        spin.ai[2] = startAngle;
        spin.width = spin.height = 20;
        spin.hostile = true;
        spin.friendly = false;
        spin.tileCollide = false;
        spin.timeLeft = 90;
        spin.netUpdate = true;
    }

    protected void LaunchLockedRain(Player target, BossProjectileStyle style, int count, float spacing, float height, int warningFrames, float fallSpeed, float scale)
    {
        // A centered lane is locked at telegraph start. Keep a clear gap around the predicted player point.
        float centerX = LockedPoint.X;
        int gap = count / 2;
        for (int i = 0; i < count; i++)
        {
            if (i == gap) continue;
            float x = centerX + (i - gap) * spacing;
            Fire(target, new Vector2(x, LockedPoint.Y - height), new Vector2(0f, fallSpeed), style, scale);
        }
    }

    protected int SpawnEscort(MilkcraftBossMinionKind kind, Vector2 desiredCenter, int cap = 2)
    {
        if (Main.netMode == NetmodeID.MultiplayerClient) return -1;
        int encounterCap = Kind switch
        {
            MilkcraftBossKind.WraithWarrior => 2,
            MilkcraftBossKind.Banshee => 3,
            MilkcraftBossKind.ObsidianGuardian => 4,
            MilkcraftBossKind.Sphinx => 2,
            MilkcraftBossKind.FrostWitch => 4,
            _ => 0
        };
        cap = Math.Min(cap, encounterCap);
        if (cap <= 0) return -1;
        int active = 0, sameKind = 0;
        for (int i = 0; i < Main.maxNPCs; i++)
        {
            NPC other = Main.npc[i];
            if (other.active && other.GetGlobalNPC<MilkcraftBossEscortGlobalNPC>().ParentBoss == NPC.whoAmI)
            {
                active++;
                if (other.type == GetMinionNpcType(kind)) sameKind++;
            }
        }
        if (active >= encounterCap || sameKind >= cap) return -1;

        int npcType = GetMinionNpcType(kind);
        int index = NPC.NewNPC(NPC.GetSource_FromAI(), (int)desiredCenter.X, (int)desiredCenter.Y, npcType);
        if (index < 0 || index >= Main.maxNPCs) return -1;
        NPC minion = Main.npc[index];
        Vector2 spawnCenter = desiredCenter;
        if (NPC.target >= 0 && NPC.target < Main.maxPlayers)
        {
            Player player = Main.player[NPC.target];
            if (player.active && !player.dead && Vector2.DistanceSquared(spawnCenter, player.Center) < 180f * 180f)
                spawnCenter = player.Center + new Vector2(spawnCenter.X < player.Center.X ? -180f : 180f, -24f);
        }
        if (!minion.noGravity && TryFindGroundSurface(spawnCenter.X, Math.Max(spawnCenter.Y, NPC.Bottom.Y), out Vector2 ground))
            spawnCenter.Y = ground.Y - minion.height * 0.5f;
        minion.Center = spawnCenter;
        minion.value = 0f;
        minion.npcSlots = 0.25f;
        minion.GetGlobalNPC<MilkcraftBossEscortGlobalNPC>().ParentBoss = NPC.whoAmI;
        minion.netUpdate = true;
        return index;
    }

    private static int GetMinionNpcType(MilkcraftBossMinionKind kind) => kind switch
        {
            MilkcraftBossMinionKind.WraithArcher => ModContent.NPCType<WraithArcherMinion>(),
            MilkcraftBossMinionKind.SkeletonWarrior => ModContent.NPCType<SkeletonWarriorMinion>(),
            MilkcraftBossMinionKind.SkeletonArcher => ModContent.NPCType<SkeletonArcherMinion>(),
            MilkcraftBossMinionKind.ExplosiveSlime => ModContent.NPCType<ExplosiveSlimeMinion>(),
            MilkcraftBossMinionKind.FrostNaga => ModContent.NPCType<FrostNagaMinion>(),
            _ => ModContent.NPCType<FrostArcherMinion>()
        };

    protected int SpawnSoulCrystal(Vector2 desiredCenter, float orbitPhase)
    {
        if (Main.netMode == NetmodeID.MultiplayerClient) return -1;
        int type = ModContent.NPCType<CastleSoulCrystal>();
        int owned = 0;
        for (int i = 0; i < Main.maxNPCs; i++)
            if (Main.npc[i].active && Main.npc[i].type == type && (int)Main.npc[i].ai[0] == NPC.whoAmI) owned++;
        if (owned >= 3) return -1;
        int index = NPC.NewNPC(NPC.GetSource_FromAI(), (int)desiredCenter.X, (int)desiredCenter.Y,
            type);
        if (index < 0 || index >= Main.maxNPCs) return -1;
        NPC crystal = Main.npc[index];
        crystal.Center = desiredCenter;
        crystal.ai[0] = NPC.whoAmI;
        crystal.ai[1] = orbitPhase;
        crystal.timeLeft = 150;
        crystal.value = 0f;
        crystal.netUpdate = true;
        return index;
    }

    protected void ClearOwnedHazards()
    {
        if (Main.netMode == NetmodeID.MultiplayerClient) return;
        int projectileType = ModContent.ProjectileType<MilkcraftBossProjectile>();
        for (int i = 0; i < Main.maxProjectiles; i++)
        {
            Projectile projectile = Main.projectile[i];
            if (projectile.active && projectile.type == projectileType && (int)projectile.ai[1] == NPC.whoAmI)
                projectile.Kill();
        }
        int crystalType = ModContent.NPCType<CastleSoulCrystal>();
        for (int i = 0; i < Main.maxNPCs; i++)
        {
            NPC crystal = Main.npc[i];
            if (crystal.active && crystal.type == crystalType && (int)crystal.ai[0] == NPC.whoAmI)
                crystal.active = false;
        }
    }

    protected bool Expert => Main.expertMode;
    internal int AttackDamage(float scale = 1f)
    {
        // NPC.damage is already difficulty-scaled by SetDefaults, while Terraria's
        // hostile Projectile -> Player.Hurt path applies the world difficulty damage
        // multiplier again. Normalize here so the native Hurt path contributes the
        // intended normal/expert/master 1x/2x/3x exactly once.
        (float min, float max) = Kind switch
        {
            MilkcraftBossKind.WraithWarrior or MilkcraftBossKind.JungleLord or MilkcraftBossKind.Banshee => (0.68f, 1.25f),
            MilkcraftBossKind.ObsidianGuardian or MilkcraftBossKind.Sphinx or MilkcraftBossKind.FrostWitch => (1.00f, 1.55f),
            _ => (1.05f, 1.70f)
        };
        float normalized = MathHelper.Clamp((scale - 0.30f) / 0.64f, 0f, 1f);
        float rawScale = MathHelper.Lerp(min, max, normalized);
        float empowered = IsFinalCastle && NPC.ai[3] > 0.5f ? 1.2f : 1f;
        float difficultyScale = Main.masterMode ? 3f : Main.expertMode ? 2f : 1f;
        float normalDamage = NPC.damage / difficultyScale;
        return Math.Max(1, (int)(normalDamage * rawScale * empowered));
    }
    protected void Pose(int frame)
    {
        if (PoseCount == 8)
        {
            // The expanded ground-boss atlases keep the old four poses at
            // 0/1/4/5 and reserve 2/3/6/7 for the stride, recovery and jump.
            // Combat stage determines semantic charge/attack/recovery frames;
            // no combat timer or attack transition is changed here.
            if (!Floats && Math.Abs(NPC.velocity.Y) > 0.35f)
                frame = 7;
            else if (CombatStage == 3)
                frame = 6;
            else if (frame == 2)
                frame = 4;
            else if (frame == 3)
                frame = 5;
        }
        NPC.localAI[0] = Math.Clamp(frame, 0, PoseCount - 1);
    }
    protected bool IsAttackCycle(int tick, int offset, int period) => tick % period == offset;

    protected void GroundMove(Player target, float speed, float acceleration)
    {
        float delta = target.Center.X - NPC.Center.X;
        NPC.direction = delta < 0 ? -1 : 1;
        NPC.spriteDirection = NPC.direction;
        NPC.velocity.X = MathHelper.Lerp(NPC.velocity.X, NPC.direction * speed, acceleration);

        float stepSpeed = 1.5f;
        Collision.StepUp(ref NPC.position, ref NPC.velocity, NPC.width, NPC.height,
            ref stepSpeed, ref NPC.gfxOffY, 1, false, 0);
        if (NPC.collideX && NPC.velocity.Y == 0f)
            NPC.velocity.Y = -6f;

        if (Math.Abs(NPC.velocity.X) > 0.6f)
        {
            _runProgress += Math.Abs(NPC.velocity.X);
            // These are large 92–132 px bosses with two run poses. Advancing
            // every 12 px made a fast boss switch frames almost every tick;
            // use a full stride so the animation stays readable at run speed.
            if (_runProgress >= 32f)
            {
                _runProgress = 0f;
                _runFrame = (_runFrame + 1) & 3;
            }
            NPC.localAI[0] = GroundStrideFrames[_runFrame];
        }
        else
        {
            _runProgress = 0f;
            _runFrame = 0;
            Pose(0);
        }
        // Keep the planted feet level. Tilting the entire large sprite with its
        // horizontal velocity made the ground stride look like a sliding wobble.
        NPC.rotation = 0f;
    }

    private void AdvanceClientGroundGait()
    {
        if (Math.Abs(NPC.velocity.Y) > 0.35f)
        {
            NPC.localAI[0] = 7f;
            return;
        }
        float horizontalSpeed = Math.Abs(NPC.velocity.X);
        if (horizontalSpeed <= 0.6f)
        {
            _clientRunProgress = 0f;
            _clientRunFrame = 0;
            _clientWasWalking = false;
            NPC.localAI[0] = 0f;
            return;
        }

        if (!_clientWasWalking)
        {
            _clientRunProgress = 0f;
            _clientRunFrame = 0;
            _clientWasWalking = true;
        }

        // Estimate stride from the synchronized horizontal velocity only. The
        // client does not run StepUp, alter velocity, lock targets, or advance
        // combat timers here; it only selects the same four authored gait poses.
        _clientRunProgress += horizontalSpeed;
        if (_clientRunProgress >= 32f)
        {
            _clientRunProgress %= 32f;
            _clientRunFrame = (_clientRunFrame + 1) & 3;
        }
        // These are already atlas indices, not semantic charge/attack poses;
        // bypass Pose() so 2/3 remain gait frames rather than mapping to 4/5.
        NPC.localAI[0] = GroundStrideFrames[_clientRunFrame];
    }

    protected bool TryFindGroundSurface(float worldX, float startY, out Vector2 surface)
    {
        int tileX = (int)MathHelper.Clamp(worldX / 16f, 2f, Main.maxTilesX - 3f);
        int tileY = (int)MathHelper.Clamp(startY / 16f, 1f, Main.maxTilesY - 3f);
        for (int y = tileY; y < Main.maxTilesY - 2; y++)
        {
            Tile tile = Main.tile[tileX, y];
            if (!tile.HasTile || tile.IsActuated || tile.IsHalfBlock || tile.Slope != SlopeType.Solid
                || !Main.tileSolid[tile.TileType] || Main.tileSolidTop[tile.TileType])
                continue;
            surface = new Vector2(worldX, y * 16f);
            return true;
        }
        surface = default;
        return false;
    }

    protected void SpawnVineRoot(float worldX, float startY, float damageScale = 0.46f)
    {
        if (Main.netMode == NetmodeID.MultiplayerClient || !MilkcraftBossProjectileBudget.CanSpawn(NPC.whoAmI)
            || !TryFindGroundSurface(worldX, startY, out Vector2 surface)) return;
        int slot = Projectile.NewProjectile(NPC.GetSource_FromAI(), new Vector2(worldX, surface.Y - 32f), Vector2.Zero,
            ModContent.ProjectileType<MilkcraftBossProjectile>(), AttackDamage(damageScale), 0f, Main.maxPlayers,
            (float)BossProjectileStyle.VineRoot, NPC.whoAmI);
        if (slot < 0 || slot >= Main.maxProjectiles) return;
        Projectile root = Main.projectile[slot];
        root.ai[0] = (float)BossProjectileStyle.VineRoot;
        root.ai[1] = NPC.whoAmI;
        root.width = 20;
        root.height = 64;
        root.Center = new Vector2(worldX, surface.Y - 32f);
        root.localAI[0] = 1f;
        root.timeLeft = 85;
        root.tileCollide = false;
        root.netUpdate = true;
    }

    protected void FloatToward(Vector2 destination, float speed, float inertia = 24f)
    {
        Vector2 delta = destination - NPC.Center;
        if (delta.LengthSquared() > 1f)
        {
            Vector2 desired = delta.SafeNormalize(Vector2.UnitY) * speed;
            NPC.velocity = (NPC.velocity * (inertia - 1f) + desired) / inertia;
            NPC.direction = delta.X < 0 ? -1 : 1;
            NPC.spriteDirection = NPC.direction;
        }
    }

    protected void Fire(Player target, Vector2 origin, Vector2 velocity, BossProjectileStyle style, float scale = 1f)
    {
        bool stationarySandnado = style == BossProjectileStyle.Sandnado && velocity.LengthSquared() < 0.01f;
        if (Main.netMode == NetmodeID.MultiplayerClient || (velocity.LengthSquared() < 0.01f && !stationarySandnado) || !MilkcraftBossProjectileBudget.CanSpawn(NPC.whoAmI)) return;
        int damage = AttackDamage(scale);
        int p = Projectile.NewProjectile(NPC.GetSource_FromAI(), origin, velocity,
            ModContent.ProjectileType<MilkcraftBossProjectile>(), damage, 0f, Main.maxPlayers, (float)style, NPC.whoAmI);
        if (p >= 0 && p < Main.maxProjectiles)
        {
            // Keep the style and owner explicit on the created entity as well as in
            // NewProjectile's ai arguments. This also protects this shared factory
            // from overload/default-AI differences in isolated native fixtures.
            Main.projectile[p].ai[0] = (float)style;
            Main.projectile[p].ai[1] = NPC.whoAmI;
            Main.projectile[p].hostile = true;
            Main.projectile[p].friendly = false;
            Main.projectile[p].netUpdate = true;
        }
    }

    protected void FireGroundWave(Player target, Vector2 floorCenter, Vector2 velocity, BossProjectileStyle style, float scale)
    {
        // Fire's ordinary origin is the initial 20x20 projectile center. Ground
        // waves need their full style-sized hitbox positioned above the floor from
        // frame zero, otherwise their first native tile-collision update can consume
        // them while they are still embedded in the support block.
        bool[] previous = new bool[Main.projectile.Length];
        for (int i = 0; i < Main.projectile.Length; i++)
            previous[i] = Main.projectile[i] != null && Main.projectile[i].active;
        Fire(target, floorCenter, velocity, style, scale);
        Projectile wave = null!;
        for (int i = 0; i < Main.projectile.Length; i++)
        {
            Projectile candidate = Main.projectile[i];
            if (candidate != null && candidate.active && !previous[i]
                && candidate.type == ModContent.ProjectileType<MilkcraftBossProjectile>()
                && candidate.ai[0] == (float)style && (int)candidate.ai[1] == NPC.whoAmI)
            {
                wave = candidate;
                break;
            }
        }
        if (wave == null) return;
        (wave.width, wave.height) = style switch
        {
            BossProjectileStyle.VineRoot => (20, 64),
            BossProjectileStyle.ObsidianFlame => (30, 40),
            _ => (wave.width, wave.height)
        };
        wave.Center = floorCenter;
        wave.localAI[0] = 1f;
        wave.tileCollide = true;
        wave.netUpdate = true;
    }

    protected void AimLocked(Player target, BossProjectileStyle style, float speed, float spread = 0f, int count = 1, float damageScale = 1f)
    {
        Vector2 origin = NPC.Center;
        Vector2 baseVelocity = (LockedPoint - origin).SafeNormalize(Vector2.UnitY) * speed;
        for (int i = 0; i < count; i++)
        {
            float offset = count == 1 ? 0f : MathHelper.Lerp(-spread, spread, i / (float)(count - 1));
            Fire(target, origin, baseVelocity.RotatedBy(offset), style, damageScale);
        }
    }

    protected void Radial(Player target, BossProjectileStyle style, int count, float speed, float phase = 0f, float damageScale = 0.55f)
    {
        for (int i = 0; i < count; i++)
        {
            float angle = MathHelper.TwoPi * i / count + phase;
            Fire(target, NPC.Center, new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle)) * speed, style, damageScale);
        }
    }

    protected void Telegraph(Vector2 point, Color color, int radius = 24)
    {
        if (Main.dedServ || Main.netMode == NetmodeID.Server) return;
        for (int i = 0; i < 3; i++)
        {
            float angle = MathHelper.TwoPi * (Main.GameUpdateCount % 48 / 48f + i / 3f);
            Vector2 pos = point + new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle)) * radius;
            Dust d = Dust.NewDustPerfect(pos, DustID.AncientLight, Vector2.Zero, 50, color, 0.9f);
            d.noGravity = true;
        }
    }

    private void EmitAmbientDust()
    {
        int type = Kind switch
        {
            MilkcraftBossKind.WraithWarrior => DustID.Shadowflame,
            MilkcraftBossKind.JungleLord => DustID.JungleGrass,
            MilkcraftBossKind.Banshee => DustID.BlueFairy,
            MilkcraftBossKind.ObsidianGuardian => DustID.Torch,
            MilkcraftBossKind.Sphinx => DustID.Sand,
            MilkcraftBossKind.FrostWitch => DustID.Ice,
            _ => DustID.AncientLight
        };
        int i = Dust.NewDust(NPC.position, NPC.width, NPC.height, type, 0f, -0.4f, 120, default, 0.8f);
        Main.dust[i].noGravity = true;
    }

    public override void FindFrame(int frameHeight)
    {
        int frame = (int)NPC.localAI[0];
        if (PoseCount == 8)
        {
            if (!Floats && Math.Abs(NPC.velocity.Y) > 0.35f) frame = 7;
            else if (CombatStage == 1) frame = 4;
            else if (CombatStage == 2) frame = 5;
            else if (CombatStage == 3) frame = 6;
        }
        if (IsFinalCastle)
        {
            frame = NPC.life * 100 > NPC.lifeMax * 80 ? 0 : NPC.life * 100 > NPC.lifeMax * 50 ? 1 : 2;
            if (NPC.localAI[0] >= 3f && frame == 2) frame = 3;
        }
        NPC.frame.Width = AssetWidth;
        NPC.frame.Height = frameHeight;
        NPC.frame.Y = Math.Clamp(frame, 0, PoseCount - 1) * frameHeight;
    }

    protected virtual int AssetWidth => Kind == MilkcraftBossKind.WraithWarrior ? 160 : Kind == MilkcraftBossKind.Sphinx || Kind == MilkcraftBossKind.CastleCore ? 192 : 128;

    public override bool PreDraw(SpriteBatch spriteBatch, Vector2 screenPos, Color drawColor)
    {
        Texture2D texture = TextureAssets.Npc[Type].Value;
        Rectangle source = NPC.frame;
        if (source.Width <= 0 || source.Height <= 0)
            source = new Rectangle(0, 0, texture.Width, texture.Height / PoseCount);
        Vector2 origin = new(source.Width * 0.5f, Floats ? source.Height * 0.5f : source.Height - 2f);
        Vector2 position = Floats ? NPC.Center : NPC.Bottom;
        position -= screenPos;
        SpriteEffects effects = NPC.spriteDirection < 0 ? SpriteEffects.FlipHorizontally : SpriteEffects.None;
        spriteBatch.Draw(texture, position, source, drawColor, NPC.rotation, origin, NPC.scale, effects, 0f);

        if (!Main.dedServ)
        {
            Texture2D glow = ModContent.Request<Texture2D>($"{Mod.Name}/Content/NPCs/{AssetName}_Glow", AssetRequestMode.ImmediateLoad).Value;
            spriteBatch.Draw(glow, position, source, Color.White, NPC.rotation, origin, NPC.scale, effects, 0f);
        }
        if (CombatStage == 1 || (Kind == MilkcraftBossKind.FrostWitch && CombatStage == 2 && CombatMove == 1 &&
            (CombatTick is >= 20 and < 48 or >= 84 and < 112 or >= 148 and < 176)))
            DrawMoveWarning(spriteBatch, screenPos);
        return false;
    }

    private void DrawMoveWarning(SpriteBatch spriteBatch, Vector2 screenPos)
    {
        Texture2D pixel = TextureAssets.MagicPixel.Value;
        Color color = Kind switch
        {
            MilkcraftBossKind.WraithWarrior => new Color(186, 122, 255, 230),
            MilkcraftBossKind.JungleLord => new Color(142, 232, 88, 230),
            MilkcraftBossKind.Banshee => new Color(110, 190, 255, 230),
            MilkcraftBossKind.ObsidianGuardian => new Color(255, 130, 65, 230),
            MilkcraftBossKind.Sphinx => new Color(255, 211, 116, 230),
            MilkcraftBossKind.FrostWitch => new Color(142, 234, 255, 230),
            _ => NPC.life * 100 > NPC.lifeMax * 80 ? new Color(140, 255, 160, 230)
                : NPC.life * 100 > NPC.lifeMax * 50 ? new Color(125, 195, 255, 230) : new Color(255, 100, 90, 230)
        };
        void Line(Vector2 a, Vector2 b, float thickness = 3f)
        {
            a -= screenPos; b -= screenPos;
            Vector2 delta = b - a;
            spriteBatch.Draw(pixel, a, new Rectangle(0, 0, 1, 1), color,
                delta.ToRotation(), new Vector2(0f, 0.5f), new Vector2(delta.Length(), thickness),
                SpriteEffects.None, 0f);
        }
        void Circle(Vector2 center, float radius, float gapAngle, float gapRadians)
        {
            const int segments = 72;
            Vector2 prior = center + new Vector2(radius, 0f);
            for (int i = 1; i <= segments; i++)
            {
                float angle = MathHelper.TwoPi * i / segments;
                if (Math.Abs(MathHelper.WrapAngle(angle - gapAngle)) >= gapRadians * 0.5f)
                    Line(prior, center + angle.ToRotationVector2() * radius, 3f);
                prior = center + angle.ToRotationVector2() * radius;
            }
        }
        Vector2 center = NPC.Center;
        Vector2 locked = LockedPoint;
        switch (Kind)
        {
            case MilkcraftBossKind.WraithWarrior when CombatMove == 0:
            {
                // The spear thrust is aimed from the locked target point. Derive the
                // preview from that same lock so a player crossing behind the boss
                // during the wind-up cannot make the warning point the wrong way.
                float facing = locked.X < center.X ? -1f : 1f;
                Line(center, center + new Vector2(facing * 240f, 0f), 4f);
                break;
            }
            case MilkcraftBossKind.WraithWarrior when CombatMove == 1:
                Circle(center, MilkcraftBossProjectile.GetWraithSweepReach(NPC), 0f, 0f);
                break;
            case MilkcraftBossKind.Banshee when CombatMove == 1:
            case MilkcraftBossKind.ObsidianGuardian when CombatMove == 1:
            case MilkcraftBossKind.Sphinx when CombatMove == 1:
            case MilkcraftBossKind.FrostWitch when CombatMove == 2:
                float ringRadius = Kind switch
                {
                    MilkcraftBossKind.ObsidianGuardian => 260f,
                    MilkcraftBossKind.Sphinx => 230f,
                    _ => 184f
                };
                float ringGap = Kind switch
                {
                    MilkcraftBossKind.ObsidianGuardian => 0.96f,
                    MilkcraftBossKind.Sphinx => 0.92f,
                    _ => 1.05f
                };
                Circle(center, ringRadius, MathHelper.Pi, ringGap);
                break;
            case MilkcraftBossKind.JungleLord when CombatMove == 1:
                foreach (float offset in new[] { -288f, -192f, -96f, 96f, 192f, 288f })
                    if (TryFindGroundSurface(locked.X + offset, locked.Y + 70f, out Vector2 ground))
                        Line(new Vector2(locked.X + offset, ground.Y - 112f), new Vector2(locked.X + offset, ground.Y), 4f);
                break;
            case MilkcraftBossKind.JungleLord when CombatMove == 2:
                foreach (int lane in new[] { -2, -1, 1, 2, 3 })
                {
                    float x = locked.X + lane * 132f;
                    Vector2 prior = new(x, locked.Y - 205f);
                    Vector2 velocity = new(lane * 1.1f, 6.4f - Math.Abs(lane) * 0.12f);
                    for (int tick = 0; tick < 28; tick++)
                    {
                        Vector2 next = prior + velocity;
                        Line(prior, next, 3f);
                        prior = next;
                        velocity.Y = Math.Min(12f, velocity.Y + 0.16f);
                    }
                    if (TryFindGroundSurface(x, locked.Y + 70f, out Vector2 ground))
                    {
                        Vector2 marker = new(x, ground.Y - 2f);
                        Line(marker - new Vector2(16f, 0f), marker + new Vector2(16f, 0f), 3f);
                    }
                }
                break;
            case MilkcraftBossKind.ObsidianGuardian when CombatMove == 0:
            {
                // Cracked fire-punch fan: outline the same forward 0.82-radian
                // spread used by FireFan, without warning the safe rear half-plane.
                float facing = locked.X < center.X ? -1f : 1f;
                float centerAngle = facing < 0f ? MathHelper.Pi : 0f;
                bool cracked = NPC.life * 2 < NPC.lifeMax;
                float halfArc = cracked ? 0.41f : MathF.Atan2(1.5f, 7.5f);
                const float reach = 310f;
                Vector2 leftEdge = center + (centerAngle - halfArc).ToRotationVector2() * reach;
                Vector2 rightEdge = center + (centerAngle + halfArc).ToRotationVector2() * reach;
                Line(center, leftEdge, 4f);
                Line(center, rightEdge, 4f);
                Line(leftEdge, rightEdge, 2f);
                break;
            }
            case MilkcraftBossKind.Sphinx when CombatMove == 3:
            {
                // Three tornado batches land outside the explicitly safe center lane.
                // These are the same X offsets as the attack spawns at ticks 0/50/100.
                float[] tornadoOffsets = { -320f, 320f, -350f };
                foreach (float offset in tornadoOffsets)
                {
                    float tornadoX = locked.X + offset;
                    Line(new Vector2(tornadoX, locked.Y - 270f), new Vector2(tornadoX, locked.Y + 24f), 4f);
                    Vector2 marker = new(tornadoX, locked.Y + 12f);
                    Line(marker - new Vector2(24f, 0f), marker + new Vector2(24f, 0f), 4f);
                    foreach (float side in new[] { -180f, 180f })
                    {
                        float shotX = tornadoX + side;
                        Line(new Vector2(shotX, locked.Y - 175f), new Vector2(shotX + Math.Sign(shotX - locked.X) * 22f, locked.Y - 30f), 3f);
                    }
                }
                // Parallel brackets mark the central |x - locked.X| < 96 escape corridor.
                foreach (float edge in new[] { -96f, 96f })
                {
                    float x = locked.X + edge;
                    for (int segment = 0; segment < 6; segment++)
                    {
                        float y0 = locked.Y - 250f + segment * 48f;
                        Line(new Vector2(x, y0), new Vector2(x, y0 + 24f), 2f);
                    }
                }
                Line(new Vector2(locked.X - 96f, locked.Y + 28f), new Vector2(locked.X + 96f, locked.Y + 28f), 2f);
                break;
            }
            case MilkcraftBossKind.FrostWitch when CombatMove == 1:
            {
                float centerOffset = CombatStage == 1 || CombatTick < 48 ? -120f
                    : CombatTick < 112 ? 100f : -20f;
                for (int i = -5; i <= 5; i++) if (i != 0)
                {
                    float x = locked.X + centerOffset + i * 96f;
                    Line(new Vector2(x, locked.Y - 500f), new Vector2(x, locked.Y + 12f), 3f);
                }
                break;
            }
            case MilkcraftBossKind.CastleCore:
            {
                int phase = NPC.life * 100 > NPC.lifeMax * 80 ? 0 : NPC.life * 100 > NPC.lifeMax * 50 ? 1 : 2;
                Vector2 aim = (locked - center).SafeNormalize(Vector2.UnitY);
                if (CombatMove == 0)
                {
                    Line(center, locked, 4f);
                    if (phase == 0)
                    {
                        for (int i = -1; i <= 1; i++)
                        {
                            Vector2 endpoint = locked + aim.RotatedBy(i * 0.12f) * 28f;
                            Line(endpoint - new Vector2(10f, 0f), endpoint + new Vector2(10f, 0f), 3f);
                        }
                    }
                    else if (phase == 1)
                    {
                        Vector2 bend = Vector2.Lerp(center, locked, 0.5f) + new Vector2(0f, -36f);
                        Line(center, bend, 4f); Line(bend, locked, 4f);
                    }
                    break;
                }
                if (CombatMove == 1)
                {
                    if (phase == 0) Line(center, locked, 6f);
                    else if (phase == 1)
                    {
                        Vector2 left = center + new Vector2(-130f, -68f);
                        Vector2 right = center + new Vector2(130f, -68f);
                        Line(left, locked, 5f); Line(right, locked, 5f);
                    }
                    else
                    {
                        foreach (int lane in new[] { -4, -3, -2, 2, 3, 4 })
                        {
                            float x = locked.X + lane * 40f;
                            Line(new Vector2(x, locked.Y - 320f), new Vector2(x, locked.Y + 20f), 3f);
                        }
                    }
                    break;
                }
                bool isGapRing = (CombatMove == 2 && phase != 1) || (CombatMove == 3 && phase == 1);
                if (isGapRing)
                {
                    const float radius = 190f;
                    const int segments = 72;
                    float gapAngle = aim.ToRotation();
                    Vector2 prior = center + new Vector2(radius, 0f);
                    for (int i = 1; i <= segments; i++)
                    {
                        float a = MathHelper.TwoPi * i / segments;
                        if (Math.Abs(MathHelper.WrapAngle(a - gapAngle)) > 0.72f)
                            Line(prior, center + a.ToRotationVector2() * radius, 3f);
                        prior = center + a.ToRotationVector2() * radius;
                    }
                    break;
                }
                if (CombatMove == 2 && phase == 1)
                {
                    for (int i = 0; i < 3; i++)
                    {
                        Vector2 crystal = center + (MathHelper.TwoPi * i / 3f).ToRotationVector2() * 155f;
                        Line(crystal - new Vector2(10f, 0f), crystal + new Vector2(10f, 0f), 3f);
                    }
                    Line(center, locked, 3f);
                    break;
                }
                if (phase == 0)
                {
                    foreach (int lane in new[] { -4, -3, -2, 2, 3, 4 })
                    {
                        float x = locked.X + lane * 40f;
                        Line(new Vector2(x, locked.Y - 320f), new Vector2(x, locked.Y + 20f), 3f);
                    }
                }
                else if (phase == 2)
                {
                    Line(center, locked, 4f);
                    Line(locked - new Vector2(18f, 0f), locked + new Vector2(18f, 0f), 3f);
                }
                break;
            }
            default:
                Line(center, locked, 3f);
                break;
        }
    }

    public override void OnKill()
    {
        if (Main.netMode == NetmodeID.MultiplayerClient) return;
        for (int i = 0; i < Main.maxProjectiles; i++)
        {
            Projectile projectile = Main.projectile[i];
            if (projectile.active && projectile.type == ModContent.ProjectileType<MilkcraftBossProjectile>() && (int)projectile.ai[1] == NPC.whoAmI)
                projectile.Kill();
        }
        for (int i = 0; i < Main.maxNPCs; i++)
        {
            NPC escort = Main.npc[i];
            bool linkedEscort = escort.active && escort.GetGlobalNPC<MilkcraftBossEscortGlobalNPC>().ParentBoss == NPC.whoAmI;
            bool linkedCrystal = escort.active && escort.type == ModContent.NPCType<CastleSoulCrystal>() && (int)escort.ai[0] == NPC.whoAmI;
            if (linkedEscort || linkedCrystal)
                escort.active = false;
        }
        MilkcraftBossWorldSystem.MarkDowned(Kind);
        if (IsFinalCastle && !Main.expertMode && (NPC.ai[3] > 0.5f || Main.rand.NextBool(4)))
            Item.NewItem(NPC.GetSource_Loot(), NPC.Hitbox, ModContent.ItemType<WhiteUnicornSummon>());
        if (IsFinalCastle && Main.netMode != NetmodeID.Server)
        {
            // Visual cue only. The empowered state was locked into ai[3] at summon time.
            Main.NewText(Language.GetTextValue(NPC.ai[3] > 0.5f
                ? "Mods.MilkcraftTerraria.Bosses.CastleCore.EmpoweredDefeated"
                : "Mods.MilkcraftTerraria.Bosses.CastleCore.Defeated"),
                NPC.ai[3] > 0.5f ? Color.Gold : Color.LightSteelBlue);
        }
    }
}

/// <summary>Tracks vanilla escort NPCs so they give no money/loot and vanish with their summoner.</summary>
public sealed class MilkcraftBossEscortGlobalNPC : GlobalNPC
{
    public override bool InstancePerEntity => true;
    internal int ParentBoss = -1;

    public override void AI(NPC npc)
    {
        if (ParentBoss < 0) return;
        if (ParentBoss >= Main.maxNPCs || !Main.npc[ParentBoss].active || Main.npc[ParentBoss].life <= 0
            || Main.npc[ParentBoss].ModNPC is not MilkcraftBossNPC)
        {
            npc.active = false;
            return;
        }
        npc.value = 0f;
    }

    public override void ModifyNPCLoot(NPC npc, NPCLoot npcLoot)
    {
        if (ParentBoss >= 0) npcLoot.RemoveWhere(_ => true);
    }

    public override void SendExtraAI(NPC npc, BitWriter bitWriter, BinaryWriter binaryWriter)
    {
        bitWriter.WriteBit(ParentBoss >= 0);
        if (ParentBoss >= 0) binaryWriter.Write((short)ParentBoss);
    }

    public override void ReceiveExtraAI(NPC npc, BitReader bitReader, BinaryReader binaryReader)
        => ParentBoss = bitReader.ReadBit() ? binaryReader.ReadInt16() : -1;
}

[AutoloadBossHead]
public sealed class WraithWarriorBoss : MilkcraftBossNPC
{
    protected override int PoseCount => 8;
    protected override int BossMusic => MusicID.Boss1;
    protected override MilkcraftBossKind Kind => MilkcraftBossKind.WraithWarrior;
    protected override string AssetName => "WraithWarrior";
    protected override int BagType => ModContent.ItemType<WraithWarriorBag>();
    protected override int NormalLife => 3900;
    protected override int ContactDamage => 34;
    protected override int Defense => 12;
    protected override int Width => 92;
    protected override int Height => 112;
    protected override bool Floats => false;

    protected override void RunBossAI(Player target)
    {
        bool enraged = NPC.life * 2 < NPC.lifeMax;
        if (CombatStage == 0)
        {
            GroundMove(target, enraged ? 4.8f : 4.1f, 0.08f);
            if (ReadyMove(enraged ? 76 : 96, 4, target, 8f, 45))
                _warningTicks = CombatMove switch { 0 => 50, 1 => 52, 2 => 45, _ => 45 };
            return;
        }
        if (CombatStage == 1)
        {
            RunTelegraph(CombatMove switch { 0 => Color.MediumPurple, 1 => Color.DarkViolet, 2 => Color.CornflowerBlue, _ => Color.MediumPurple },
                CombatMove == 1 ? 130 : 38);
            NPC.velocity.X *= CombatMove == 0 ? 0.90f : 0.94f;
            return;
        }
        if (CombatStage == 2)
        {
            int tick = CombatTick;
            switch (CombatMove)
            {
                case 0: // Locked spear thrust. It accelerates only during the attack window.
                    Pose(3);
                    if (tick == 0)
                    {
                        NPC.direction = LockedPoint.X < NPC.Center.X ? -1 : 1;
                        NPC.spriteDirection = NPC.direction;
                        NPC.velocity.X = NPC.direction * (enraged ? 8.5f : 7.2f);
                        Fire(target, NPC.Center + new Vector2(NPC.direction * 38f, -10f),
                            new Vector2(NPC.direction * 10f, 0f), BossProjectileStyle.WraithSpear, 0.62f);
                    }
                    if (RunAttackTimer(22)) NPC.velocity.X *= 0.45f;
                    break;
                case 1: // Two readable rotations of a held spear; the warning circle is the actual reach.
                    Pose(3);
                    if (tick == 0) SpearSpin(0f, enraged ? 0.46f : 0.40f);
                    if (RunAttackTimer(90)) NPC.velocity.X *= 0.5f;
                    break;
                case 2: // Half-deer archer signal and volley, with a single tracked escort slot.
                    Pose(3);
                    if (tick == 0) SpawnEscort(MilkcraftBossMinionKind.WraithArcher,
                        LockedPoint + new Vector2(-NPC.direction * 220f, -36f), 2);
                    if (tick == 12 || (enraged && tick == 38))
                        AimLocked(target, BossProjectileStyle.WraithArrow, 8.2f, 0.08f, 1, 0.43f);
                    if (RunAttackTimer(enraged ? 64 : 48)) NPC.velocity.X *= 0.75f;
                    break;
                default: // Retreat and throw two staggered spears; ordinary form throws one.
                    Pose(3);
                    if (tick == 0) NPC.velocity.X = -NPC.direction * 2.3f;
                    if (tick == 0 || (enraged && tick == 18))
                    {
                        Vector2 aim = (LockedPoint - NPC.Center).SafeNormalize(Vector2.UnitX) * 9.2f;
                        Fire(target, NPC.Center, aim.RotatedBy(tick == 0 ? -0.08f : 0.08f), BossProjectileStyle.WraithSpear, 0.48f);
                    }
                    if (RunAttackTimer(36)) NPC.velocity.X *= 0.6f;
                    break;
            }
            return;
        }
        RunRecovery(CombatMove switch { 0 => 32, 1 => 40, 2 => 58, _ => 60 });
    }
}

[AutoloadBossHead]
public sealed class JungleLordBoss : MilkcraftBossNPC
{
    protected override int PoseCount => 8;
    protected override int BossMusic => MusicID.Plantera;
    protected override MilkcraftBossKind Kind => MilkcraftBossKind.JungleLord;
    protected override string AssetName => "JungleLord";
    protected override int BagType => ModContent.ItemType<JungleLordBag>();
    protected override int NormalLife => 4700;
    protected override int ContactDamage => 38;
    protected override int Defense => 14;
    protected override int Width => 96;
    protected override int Height => 118;
    protected override bool Floats => false;

    protected override void RunBossAI(Player target)
    {
        bool enraged = NPC.life * 2 < NPC.lifeMax;
        if (CombatStage == 0)
        {
            GroundMove(target, enraged ? 3.8f : 3.1f, 0.055f);
            int upcoming = _nextMove % 4;
            ReadyLockedMove(enraged ? 82 : 104, 4, target, upcoming switch { 1 => 55, 2 => 60, 3 => 50, _ => 40 });
            return;
        }
        if (CombatStage == 1)
        {
            int warning = CombatMove switch { 0 => 40, 1 => 55, 2 => 60, _ => 50 };
            Color color = CombatMove == 1 ? Color.LimeGreen : new Color(135, 214, 74);
            if (RunTelegraph(color, CombatMove == 1 ? 112 : 34))
            {
                if (CombatMove == 3)
                {
                    _groundedFlag = 0;
                    // A -10.5 jump stays airborne for roughly sixty to seventy
                    // native ticks. Match horizontal speed to that flight time so
                    // the landing and the warning marker remain near each other.
                    NPC.velocity.X = MathHelper.Clamp((LockedPoint.X - NPC.Center.X) / 68f, -7.5f, 7.5f);
                    NPC.velocity.Y = -10.5f;
                }
                _warningTicks = warning;
            }
            return;
        }
        if (CombatStage == 2)
        {
            int tick = CombatTick;
            Pose(3);
            switch (CombatMove)
            {
                case 0: // Heavy fist and a low, jumpable ground shock.
                    if (tick == 0)
                    {
                        NPC.velocity.X = NPC.direction * 1.5f;
                        for (int side = -1; side <= 1; side += 2)
                            // Put the shockwave's 64px body fully above the floor. Fire keeps
                            // tile collision enabled for this moving root, so spawning it from
                            // inside the support tile would consume the wave before it travels.
                            FireGroundWave(target, NPC.Bottom + new Vector2(side * 16f, -33f), new Vector2(side * 8f, 0f), BossProjectileStyle.VineRoot, 0.5f);
                    }
                    RunAttackTimer(8);
                    break;
                case 1: // Six telegraphed root lanes, emitted in three paired beats.
                    if (tick is 0 or 12 or 24)
                    {
                        float[] lanes = tick == 0 ? new[] { -288f, -192f }
                            : tick == 12 ? new[] { -96f, 96f }
                            : new[] { 192f, 288f };
                        foreach (float lane in lanes)
                            SpawnVineRoot(LockedPoint.X + lane, LockedPoint.Y - 220f, 0.46f);
                    }
                    RunAttackTimer(36);
                    break;
                case 2: // Five arcing seeds with a deliberate middle lane left open.
                    if (tick is 0 or 12 or 24 or 36 or 48)
                    {
                        int[] safeGapLanes = { -2, -1, 1, 2, 3 };
                        int lane = safeGapLanes[tick / 12];
                        Vector2 v = new Vector2(lane * 1.1f, 6.4f - Math.Abs(lane) * 0.12f);
                        Fire(target, new Vector2(LockedPoint.X + lane * 132f, LockedPoint.Y - 205f), v, BossProjectileStyle.JungleSeed, 0.40f);
                    }
                    RunAttackTimer(60);
                    break;
                default: // Landing, rather than a fixed timer, releases the two shock lanes.
                    if (NPC.velocity.Y == 0f && tick > 8 && _groundedFlag == 0)
                    {
                        _groundedFlag = 1;
                        foreach (int side in new[] { -1, 1 })
                            FireGroundWave(target, NPC.Bottom + new Vector2(side * 14f, -33f), new Vector2(side * 6.5f, 0f), BossProjectileStyle.VineRoot, 0.58f);
                    }
                    if (tick >= 90 || (_groundedFlag > 0 && tick >= 32))
                    {
                        NPC.ai[0] = 3f; NPC.ai[1] = 0f; NPC.netUpdate = true;
                    }
                    else NPC.ai[1] = tick + 1;
                    break;
            }
            return;
        }
        RunRecovery(CombatMove switch { 0 => 40, 1 => 46, 2 => 44, _ => 60 });
    }
}

[AutoloadBossHead]
public sealed class BansheeBoss : MilkcraftBossNPC
{
    protected override int BossMusic => MusicID.Boss3;
    protected override MilkcraftBossKind Kind => MilkcraftBossKind.Banshee;
    protected override string AssetName => "Banshee";
    protected override int BagType => ModContent.ItemType<BansheeBag>();
    protected override int NormalLife => 3600;
    protected override int ContactDamage => 32;
    protected override int Defense => 9;
    protected override int Width => 84;
    protected override int Height => 116;
    protected override bool Floats => true;

    protected override void RunBossAI(Player target)
    {
        bool sorrow = NPC.life * 100 < NPC.lifeMax * 45;
        if (CombatStage == 0)
        {
            FloatOrbit(target, 210f, 155f, 5.2f, 0.4f);
            int upcoming = _nextMove % 4;
            ReadyLockedMove(sorrow ? 74 : 96, 4, target, upcoming switch { 0 => 32, 1 => 55, 2 => 45, _ => 44 });
            return;
        }
        if (CombatStage == 1)
        {
            int warning = CombatMove switch { 0 => 32, 1 => 55, 2 => 45, _ => 44 };
            NPC.velocity *= 0.92f;
            if (RunTelegraph(CombatMove == 1 ? Color.CornflowerBlue : Color.MediumPurple, CombatMove == 1 ? 56 : 35))
                _warningTicks = warning;
            return;
        }
        if (CombatStage == 2)
        {
            int tick = CombatTick;
            Pose(3);
            switch (CombatMove)
            {
                case 0: // Three separated, locked skull shots.
                    if (Main.expertMode && tick is >= 45 and < 68)
                    {
                        Pose(2);
                        Telegraph(LockedPoint, Color.CornflowerBlue, 30);
                    }
                    if (tick is 0 or 25 or 50)
                        Fire(target, NPC.Center, (LockedPoint - NPC.Center).SafeNormalize(Vector2.UnitX) * 8.6f, BossProjectileStyle.BansheeSkull, 0.46f);
                    if (Main.expertMode && tick == 68)
                        Fire(target, NPC.Center, (LockedPoint - NPC.Center).SafeNormalize(Vector2.UnitX) * 8.6f, BossProjectileStyle.BansheeSkull, 0.42f);
                    RunAttackTimer(Main.expertMode ? 82 : 75);
                    break;
                case 1: // Expanding ring with an opening, second ring only below half life.
                    if (tick == 0) RingSector(BossProjectileStyle.BansheeWave, 9, 4.3f, MathHelper.Pi, 1.05f, 0f, 0.36f);
                    if (sorrow && tick == 35) RingSector(BossProjectileStyle.BansheeWave, 9, 4.5f, MathHelper.Pi, 1.05f, 0.55f, 0.38f);
                    RunAttackTimer(sorrow ? 72 : 38);
                    break;
                case 2: // Real skeleton escort pair with a hard population cap.
                    if (tick == 0)
                    {
                        SpawnEscort(MilkcraftBossMinionKind.SkeletonWarrior, NPC.Bottom + new Vector2(-90f, -20f), 3);
                        SpawnEscort(MilkcraftBossMinionKind.SkeletonArcher, NPC.Bottom + new Vector2(110f, -20f), 3);
                    }
                    if (tick == 24) AimLocked(target, BossProjectileStyle.BansheeSkull, 7.8f, 0f, 1, 0.38f);
                    RunAttackTimer(50);
                    break;
                default: // Side-swipe stays active only during the visible dash window.
                    if (tick == 0)
                    {
                        NPC.direction = LockedPoint.X < NPC.Center.X ? -1 : 1;
                        NPC.spriteDirection = NPC.direction;
                        NPC.velocity = new Vector2(NPC.direction * 10.5f, -1.2f);
                    }
                    if (tick == 30)
                    {
                        NPC.velocity *= 0.35f;
                        RingSector(BossProjectileStyle.BansheeSkull, 7, 5.4f, MathHelper.Pi, 0.85f, 0f, 0.34f);
                    }
                    RunAttackTimer(sorrow ? 66 : 48);
                    break;
            }
            return;
        }
        NPC.velocity *= 0.88f;
        RunRecovery(CombatMove == 1 ? 48 : 38);
    }
}

[AutoloadBossHead]
public sealed class ObsidianGuardianBoss : MilkcraftBossNPC
{
    protected override int PoseCount => 8;
    protected override int BossMusic => MusicID.Boss4;
    protected override MilkcraftBossKind Kind => MilkcraftBossKind.ObsidianGuardian;
    protected override string AssetName => "ObsidianGuardian";
    protected override int BagType => ModContent.ItemType<ObsidianGuardianBag>();
    protected override int NormalLife => 17500;
    protected override int ContactDamage => 52;
    protected override int Defense => 22;
    protected override int Width => 88;
    protected override int Height => 112;
    protected override bool Floats => false;

    protected override void RunBossAI(Player target)
    {
        bool cracked = NPC.life * 2 < NPC.lifeMax;
        if (CombatStage == 0)
        {
            GroundMove(target, 2.45f, 0.045f);
            int upcoming = _nextMove % 5;
            ReadyLockedMove(cracked ? 72 : 92, 5, target, upcoming switch { 0 => 42, 1 => 60, 2 => 55, 3 => 50, _ => 60 });
            return;
        }
        if (CombatStage == 1)
        {
            Color glow = CombatMove == 3 ? Color.LightCyan : new Color(245, 112, 45);
            if (RunTelegraph(glow, CombatMove == 1 ? 120 : 45))
            {
                if (CombatMove == 4)
                {
                    _groundedFlag = 0;
                    NPC.velocity.Y = -12f;
                    NPC.velocity.X = MathHelper.Clamp((LockedPoint.X - NPC.Center.X) / 78f, -7f, 7f);
                }
            }
            return;
        }
        if (CombatStage == 2)
        {
            int tick = CombatTick;
            Pose(3);
            switch (CombatMove)
            {
                case 0: // A frontal punch fan; the rear half-plane stays open.
                    if (tick == 0)
                    {
                        NPC.velocity.X = NPC.direction * 1.2f;
                        for (int i = -1; i <= 1; i++)
                            Fire(target, NPC.Center + new Vector2(NPC.direction * 42f, -18f), new Vector2(NPC.direction * 7.5f, i * 1.5f), BossProjectileStyle.ObsidianFlame, 0.46f);
                    }
                    if (tick == 14 && cracked) FireFan(BossProjectileStyle.ObsidianFlame, 5, 4f,
                        NPC.direction < 0 ? MathHelper.Pi : 0f, 0.82f, 0.3f);
                    RunAttackTimer(26);
                    break;
                case 1: // Telegraph a gap, then release the visible radial fire ring.
                    if (tick == 0) RingSector(BossProjectileStyle.ObsidianFlame, 12, 4.8f, MathHelper.Pi, 0.96f, 0f, 0.43f);
                    if (cracked && tick == 38) RingSector(BossProjectileStyle.ObsidianFlame, 12, 4.6f, MathHelper.Pi, 0.96f, 0.5f, 0.4f);
                    RunAttackTimer(cracked ? 72 : 40);
                    break;
                case 2: // Falling fireballs mark three lanes; only grounded hits make an explosive slime.
                    if (tick is 0 or 12 or 24)
                    {
                        int lane = tick / 12 - 1;
                        float x = LockedPoint.X + lane * 150f;
                        Fire(target, new Vector2(x, LockedPoint.Y - 330f), new Vector2(lane * 1.1f, 7.0f), BossProjectileStyle.ObsidianMeteor, 0.44f);
                    }
                    RunAttackTimer(48);
                    break;
                case 3: // Timed frost naga escort, hard capped and removable by the player.
                    if (tick == 0) SpawnEscort(MilkcraftBossMinionKind.FrostNaga, NPC.Bottom + new Vector2(NPC.direction * 140f, -12f), 2);
                    if (tick == 24) Fire(target, NPC.Center, (LockedPoint - NPC.Center).SafeNormalize(Vector2.UnitX) * 7.5f, BossProjectileStyle.IceNaga, 0.42f);
                    RunAttackTimer(46);
                    break;
                default: // Ground contact triggers the shockwaves, not the timer.
                    if (NPC.velocity.Y == 0f && tick > 8 && _groundedFlag == 0)
                    {
                        _groundedFlag = 1;
                        for (int sign = -1; sign <= 1; sign += 2)
                            // ObsidianFlame's horizontal ground wave is 40px tall. Keep its
                            // bottom one pixel above the support tile so native tile collision
                            // does not delete it on its first update.
                            FireGroundWave(target, NPC.Bottom + new Vector2(sign * 30f, -21f), new Vector2(sign * 7f, 0f), BossProjectileStyle.ObsidianFlame, 0.54f);
                    }
                    if (tick >= 88 || (_groundedFlag > 0 && tick >= 38)) { NPC.ai[0] = 3f; NPC.ai[1] = 0f; }
                    else NPC.ai[1] = tick + 1;
                    break;
            }
            return;
        }
        RunRecovery(CombatMove switch { 0 => 40, 1 => 48, 2 => 54, 3 => 42, _ => 65 });
    }
}

[AutoloadBossHead]
public sealed class SphinxBoss : MilkcraftBossNPC
{
    protected override int BossMusic => MusicID.Boss2;
    protected override MilkcraftBossKind Kind => MilkcraftBossKind.Sphinx;
    protected override string AssetName => "Sphinx";
    protected override int BagType => ModContent.ItemType<SphinxBag>();
    protected override int NormalLife => 20500;
    protected override int ContactDamage => 54;
    protected override int Defense => 18;
    protected override int Width => 150;
    protected override int Height => 100;
    protected override bool Floats => true;

    protected override void RunBossAI(Player target)
    {
        bool storm = NPC.life * 2 < NPC.lifeMax;
        if (CombatStage == 0)
        {
            FloatOrbit(target, 185f, 165f, 5.2f, 1.5f);
            int upcoming = _nextMove % 4;
            ReadyLockedMove(storm ? 82 : 104, 4, target, upcoming switch { 0 => 40, 1 => 55, 2 => 45, _ => 72 });
            return;
        }
        if (CombatStage == 1)
        {
            NPC.velocity *= 0.92f;
            if (RunTelegraph(CombatMove == 1 ? Color.Goldenrod : Color.Wheat, CombatMove == 3 ? 155 : 44))
            {
                if (CombatMove == 0)
                {
                    NPC.direction = LockedPoint.X < NPC.Center.X ? -1 : 1;
                    NPC.spriteDirection = NPC.direction;
                    NPC.velocity = new Vector2(NPC.direction * 10.5f, -2.6f);
                }
            }
            return;
        }
        if (CombatStage == 2)
        {
            int tick = CombatTick;
            Pose(3);
            switch (CombatMove)
            {
                case 0: // Predator pounce, followed by a locked low sand volley.
                    if (tick == 0) AimLocked(target, BossProjectileStyle.SandShot, 8f, 0.18f, 3, 0.42f);
                    if (Main.expertMode && tick is >= 28 and < 42)
                    {
                        Pose(2);
                        Telegraph(LockedPoint, Color.Goldenrod, 42);
                    }
                    if (Main.expertMode && tick == 42)
                        AimLocked(target, BossProjectileStyle.SandShot, 8.4f, 0.12f, 2, 0.38f);
                    if (tick == 30) NPC.velocity.X *= 0.4f;
                    RunAttackTimer(Main.expertMode ? 62 : storm ? 62 : 38);
                    break;
                case 1: // Four gold arcs leave a slowly rotating gap.
                    if (tick == 0) RingSector(BossProjectileStyle.SandShot, 10, 4.5f, MathHelper.Pi, 0.92f, 0f, 0.38f);
                    if (storm && tick == 36) RingSector(BossProjectileStyle.SandShot, 10, 4.7f, MathHelper.Pi, 0.92f, 0.4f, 0.4f);
                    RunAttackTimer(storm ? 72 : 40);
                    break;
                case 2: // Real skeleton pair at separated, visible landing points.
                    if (tick == 0)
                    {
                        SpawnEscort(MilkcraftBossMinionKind.SkeletonWarrior, NPC.Bottom + new Vector2(-130f, -10f), 2);
                        SpawnEscort(MilkcraftBossMinionKind.SkeletonArcher, NPC.Bottom + new Vector2(140f, -10f), 2);
                    }
                    if (tick == 30) AimLocked(target, BossProjectileStyle.SandShot, 7.3f, 0.24f, 3, 0.36f);
                    RunAttackTimer(56);
                    break;
                default: // Three readable batches, preserving the central escape corridor.
                    if (tick is 0 or 50 or 100)
                    {
                        // Keep both the cyclone and its falling side shots outside the
                        // announced central escape lane (|x - LockedPoint.X| < 96).
                        float batchX = LockedPoint.X + (tick == 0 ? -320f : tick == 50 ? 320f : -350f);
                        Fire(target, new Vector2(batchX, LockedPoint.Y - 270f), Vector2.Zero, BossProjectileStyle.Sandnado, 0.38f);
                        for (int i = -1; i <= 1; i += 2)
                        {
                            float shotX = batchX + i * 180f;
                            float outward = Math.Sign(shotX - LockedPoint.X);
                            Fire(target, new Vector2(shotX, LockedPoint.Y - 175f), new Vector2(outward * 0.5f, 6.3f), BossProjectileStyle.SandShot, 0.31f);
                        }
                    }
                    RunAttackTimer(150);
                    break;
            }
            return;
        }
        NPC.velocity *= 0.88f;
        RunRecovery(CombatMove == 3 ? 70 : 38);
    }
}

[AutoloadBossHead]
public sealed class FrostWitchBoss : MilkcraftBossNPC
{
    protected override int BossMusic => MusicID.Boss2;
    protected override MilkcraftBossKind Kind => MilkcraftBossKind.FrostWitch;
    protected override string AssetName => "FrostWitch";
    protected override int BagType => ModContent.ItemType<FrostWitchBag>();
    protected override int NormalLife => 18200;
    protected override int ContactDamage => 50;
    protected override int Defense => 20;
    protected override int Width => 104;
    protected override int Height => 128;
    protected override bool Floats => true;

    protected override void RunBossAI(Player target)
    {
        bool blizzard = NPC.life * 2 < NPC.lifeMax;
        if (CombatStage == 0)
        {
            FloatOrbit(target, 170f, 190f, 4.9f, 2.3f);
            int upcoming = _nextMove % 5;
            ReadyLockedMove(blizzard ? 72 : 96, 5, target, upcoming switch { 0 => 32, 1 => 60, 2 => 55, 3 => 45, _ => 60 });
            return;
        }
        if (CombatStage == 1)
        {
            Color warningColor = CombatMove == 2 ? Color.LightCyan : Color.SkyBlue;
            NPC.velocity *= 0.92f;
            if (RunTelegraph(warningColor, CombatMove == 2 ? 92 : 36))
            {
                if (CombatMove == 4)
                    NPC.velocity = new Vector2(Math.Sign(LockedPoint.X - NPC.Center.X) * 7f, NPC.velocity.Y);
            }
            return;
        }
        if (CombatStage == 2)
        {
            int tick = CombatTick;
            Pose(3);
            switch (CombatMove)
            {
                case 0: // Three aimed ice spears, separated enough for a sidestep.
                    if (tick is 0 or 22 or 44)
                        Fire(target, NPC.Center, (LockedPoint - NPC.Center).SafeNormalize(Vector2.UnitY).RotatedBy((tick - 22) * 0.0022f) * 10.5f, BossProjectileStyle.FrostSpear, 0.43f);
                    RunAttackTimer(62);
                    break;
                case 1: // Three falling lanes with a marked 160px gap.
                    if (tick is 48 or 112 or 176)
                    {
                        float cx = LockedPoint.X + (tick == 48 ? -120f : tick == 112 ? 100f : -20f);
                        for (int i = -5; i <= 5; i++)
                        {
                            if (i == 0) continue;
                            float x = cx + i * 96f;
                            Fire(target, new Vector2(x, LockedPoint.Y - 600f), new Vector2(0f, 9f), BossProjectileStyle.FrostPillar, 0.34f);
                        }
                    }
                    RunAttackTimer(205);
                    break;
                case 2: // Cold ring with opening; the optional slimes are visible and capped.
                    if (tick == 0)
                    {
                        RingSector(BossProjectileStyle.FrostRing, 10, 4.1f, MathHelper.Pi, 1.05f, 0f, 0.40f);
                        SpawnEscort(MilkcraftBossMinionKind.ExplosiveSlime, NPC.Bottom + new Vector2(-110f, -8f), 3);
                        SpawnEscort(MilkcraftBossMinionKind.ExplosiveSlime, NPC.Bottom + new Vector2(120f, -8f), 3);
                    }
                    if (blizzard && tick == 36) RingSector(BossProjectileStyle.FrostRing, 10, 4.3f, MathHelper.Pi, 1.05f, 0.46f, 0.42f);
                    RunAttackTimer(blizzard ? 78 : 44);
                    break;
                case 3: // Two frost archers, one ordinary volley and a second expert volley.
                    if (tick == 0)
                    {
                        SpawnEscort(MilkcraftBossMinionKind.FrostArcher, NPC.Center + new Vector2(-150f, -25f), 4);
                        SpawnEscort(MilkcraftBossMinionKind.FrostArcher, NPC.Center + new Vector2(150f, -25f), 4);
                    }
                    if (tick == 34) AimLocked(target, BossProjectileStyle.FrostSpear, 8.3f, 0.08f, 1, 0.36f);
                    if (blizzard && tick == 58) AimLocked(target, BossProjectileStyle.FrostSpear, 8.3f, 0.12f, 3, 0.33f);
                    RunAttackTimer(blizzard ? 82 : 54);
                    break;
                default: // Predictable lateral glide; three delayed bolts leave the lower lane clear.
                    if (tick == 0) NPC.velocity.X = Math.Sign(LockedPoint.X - NPC.Center.X) * 6.8f;
                    if (tick is 12 or 32 or 52)
                        Fire(target, NPC.Center, new Vector2(Math.Sign(LockedPoint.X - NPC.Center.X) * 7.8f, 1.4f), BossProjectileStyle.FrostSpear, 0.32f);
                    RunAttackTimer(70);
                    break;
            }
            return;
        }
        NPC.velocity *= 0.88f;
        RunRecovery(CombatMove == 1 ? 52 : 40);
    }
}

[AutoloadBossHead]
public sealed class CastleCoreBoss : MilkcraftBossNPC
{
    protected override int BossMusic => MusicID.Boss3;
    protected override MilkcraftBossKind Kind => MilkcraftBossKind.CastleCore;
    protected override string AssetName => "CastleCore";
    protected override int BagType => ModContent.ItemType<CastleCoreBag>();
    protected override int NormalLife => 36500;
    protected override int ContactDamage => 62;
    protected override int Defense => 28;
    protected override int Width => 132;
    protected override int Height => 132;
    protected override bool Floats => true;
    protected override bool IsFinalCastle => true;

    protected override void RunBossAI(Player target)
    {
        bool empowered = NPC.ai[3] > 0.5f;
        int phase = NPC.life * 100 > NPC.lifeMax * 80 ? 0 : NPC.life * 100 > NPC.lifeMax * 50 ? 1 : 2;
        if (_phase < 0) _phase = phase;
        else if (_phase != phase && _transitionTicks <= 0)
        {
            ClearOwnedHazards();
            _transitionTicks = 60;
            _nextMove = 0;
            NPC.ai[0] = 0f; NPC.ai[1] = 0f; NPC.ai[2] = 0f; NPC.velocity = Vector2.Zero;
            NPC.dontTakeDamage = true;
            NPC.netUpdate = true;
        }
        if (_transitionTicks > 0)
        {
            _transitionTicks--;
            NPC.dontTakeDamage = true;
            NPC.velocity *= 0f;
            Pose(phase);
            Telegraph(NPC.Center, phase == 0 ? new Color(140, 255, 160) : phase == 1 ? new Color(125, 195, 255) : new Color(255, 105, 90), 58);
            if (_transitionTicks <= 0)
            {
                _phase = phase;
                NPC.ai[0] = NPC.ai[1] = NPC.ai[2] = 0f;
                NPC.dontTakeDamage = false;
                NPC.netUpdate = true;
            }
            return;
        }

        NPC.dontTakeDamage = false;
        if (!_castleAnchorSet)
        {
            Vector2 requested = target.Center + new Vector2(-NPC.direction * 320f, -160f);
            Vector2 candidate = requested;
            bool foundAir = false;
            for (int rise = 0; rise <= 12 && !foundAir; rise++)
            {
                foreach (int side in new[] { 0, 1, -1, 2, -2 })
                {
                    Vector2 test = requested + new Vector2(side * 48f, -rise * 16f);
                    if (!Collision.SolidCollision(test - NPC.Size * 0.5f, NPC.width, NPC.height))
                    {
                        candidate = test; foundAir = true; break;
                    }
                }
            }
            if (!foundAir) candidate = target.Center + new Vector2(-NPC.direction * 320f, -352f);
            _castleAnchor = candidate;
            _castleAnchorSet = true;
            NPC.netUpdate = true;
        }
        Vector2 lockedAnchor = _castleAnchor;
        if (Vector2.DistanceSquared(NPC.Center, lockedAnchor) > 28f * 28f)
            FloatToward(lockedAnchor, 5f, 24f);
        else NPC.velocity *= 0.88f;

        if (CombatStage == 0)
        {
            Pose(phase);
            // Rage accelerates only to a 150-tick floor; the daytime empowered
            // multiplier remains in AttackDamage and is independent of cadence.
            int cadence = empowered ? 150 : phase == 2 ? 150 : phase == 1 ? 166 : 180;
            int windup = phase == 0 ? 42 : phase == 1 ? 46 : 40;
            ReadyLockedMove(cadence, 4, target, windup);
            return;
        }
        if (CombatStage == 1)
        {
            Color warn = phase == 0 ? new Color(140, 255, 160) : phase == 1 ? new Color(125, 195, 255) : new Color(255, 105, 90);
            RunTelegraph(warn, _warningTicks);
            return;
        }
        if (CombatStage == 2)
        {
            int tick = CombatTick;
            Pose(phase == 2 ? 3 : phase);
            Vector2 aim = (LockedPoint - NPC.Center).SafeNormalize(Vector2.UnitY);
            float gapAngle = aim.ToRotation(); // the warned lane points toward the locked player position
            switch (phase)
            {
                case 0: // Verdant face: three-point volley, forbidden healing ray, open blast, and safe-lane crossfire.
                    switch (CombatMove)
                    {
                        case 0:
                            if (tick is 0 or 16 or 32) AimLocked(target, BossProjectileStyle.CastleGreen, 8.0f, 0.04f, 1, 0.78f);
                            RunAttackTimer(48);
                            break;
                        case 1:
                            if (tick is 0 or 12 or 24) AimLocked(target, BossProjectileStyle.CastleGreen, 9.2f, 0.025f, 1, 0.94f);
                            RunAttackTimer(38);
                            break;
                        case 2:
                            if (tick == 0) RingSector(BossProjectileStyle.CastleGreenRing, 0, 0f, gapAngle, 1.44f, 0f, 0.86f);
                            RunAttackTimer(54);
                            break;
                        default:
                            if (tick is 0 or 22) FireCastleCrossfire(BossProjectileStyle.CastleGreen, tick, 0.76f);
                            RunAttackTimer(48);
                            break;
                    }
                    break;
                case 1: // Azure face: locking chain, paired beams, harvestable souls, and a gap blast.
                    switch (CombatMove)
                    {
                        case 0:
                            if (tick is 0 or 10 or 20) AimLocked(target, BossProjectileStyle.CastleBlue, 8.3f, 0.20f, 3, 0.84f);
                            RunAttackTimer(42);
                            break;
                        case 1:
                            if (tick is 0 or 22)
                            {
                                Vector2 left = NPC.Center + new Vector2(-130f, -68f);
                                Vector2 right = NPC.Center + new Vector2(130f, -68f);
                                Fire(target, left, (LockedPoint - left).SafeNormalize(aim) * 9.0f, BossProjectileStyle.CastleBlue, 0.94f);
                                Fire(target, right, (LockedPoint - right).SafeNormalize(aim) * 9.0f, BossProjectileStyle.CastleBlue, 0.94f);
                            }
                            RunAttackTimer(50);
                            break;
                        case 2:
                            if (tick == 0)
                            {
                                _soulCrystalSurvived = 0;
                                for (int i = 0; i < 3; i++)
                                {
                                    float a = MathHelper.TwoPi * i / 3f;
                                    SpawnSoulCrystal(NPC.Center + a.ToRotationVector2() * 155f, a);
                                }
                            }
                            if (tick == 100)
                            {
                                _soulCrystalSurvived = 0;
                                foreach (NPC other in Main.npc)
                                    if (other.active && other.type == ModContent.NPCType<CastleSoulCrystal>() && (int)other.ai[0] == NPC.whoAmI)
                                        _soulCrystalSurvived++;
                                _soulCrystalSurvived = Math.Min(3, _soulCrystalSurvived);
                                NPC.netUpdate = true;
                            }
                            if (tick == 108 && _soulCrystalSurvived > 0)
                            {
                                for (int i = 0; i < _soulCrystalSurvived; i++)
                                {
                                    float offset = (i - (_soulCrystalSurvived - 1) * 0.5f) * 0.15f;
                                    Fire(target, NPC.Center, aim.RotatedBy(offset) * 8f, BossProjectileStyle.CastleBlue, 0.70f);
                                }
                            }
                            RunAttackTimer(128);
                            break;
                        default:
                            if (tick == 0) RingSector(BossProjectileStyle.CastleBlueRing, 0, 0f, gapAngle, 1.44f, 0f, 0.88f);
                            RunAttackTimer(54);
                            break;
                    }
                    break;
                default: // Crimson face: direct fire, a 160-pixel safe channel, gap blast, and bounded pursuit.
                    switch (CombatMove)
                    {
                        case 0:
                            if (tick is 0 or 11 or 22) AimLocked(target, BossProjectileStyle.CastleRed, 9.0f, 0.10f, 3, 0.96f);
                            RunAttackTimer(42);
                            break;
                        case 1:
                            if (tick is 0 or 24) FireCastleCrossfire(BossProjectileStyle.CastleRed, tick, 0.94f);
                            RunAttackTimer(50);
                            break;
                        case 2:
                            if (tick == 0) RingSector(BossProjectileStyle.CastleRedRing, 0, 0f, gapAngle, 1.28f, 0f, 0.98f);
                            RunAttackTimer(54);
                            break;
                        default:
                            if (tick is 0 or 16) FireCastleTracking(target, 9.0f, 0.92f);
                            RunAttackTimer(38);
                            break;
                    }
                    break;
            }
            return;
        }
        RunRecovery(CombatMove == 2 ? 58 : 42);
    }

    private void FireCastleTracking(Player target, float speed, float scale)
    {
        if (Main.netMode == NetmodeID.MultiplayerClient || !MilkcraftBossProjectileBudget.CanSpawn(NPC.whoAmI)) return;
        Vector2 velocity = (LockedPoint - NPC.Center).SafeNormalize(Vector2.UnitY) * speed;
        int p = Projectile.NewProjectile(NPC.GetSource_FromAI(), NPC.Center, velocity,
            ModContent.ProjectileType<MilkcraftBossProjectile>(), AttackDamage(scale), 0f, Main.maxPlayers,
            (float)BossProjectileStyle.CastleRedTracking, NPC.whoAmI, target.whoAmI);
        if (p >= 0 && p < Main.maxProjectiles)
        {
            Main.projectile[p].ai[0] = (float)BossProjectileStyle.CastleRedTracking;
            Main.projectile[p].ai[1] = NPC.whoAmI;
            Main.projectile[p].ai[2] = target.whoAmI;
            Main.projectile[p].hostile = true; Main.projectile[p].friendly = false;
            Main.projectile[p].timeLeft = 210; Main.projectile[p].netUpdate = true;
        }
    }

    private void FireCastleCrossfire(BossProjectileStyle style, int wave, float scale)
    {
        // Vertical lanes leave a centered 160 px corridor. The second wave is
        // horizontally offset by 40 px so the player can dodge into the same gap.
        float shiftY = wave == 0 ? 0f : 64f;
        foreach (int lane in new[] { -4, -3, -2, 2, 3, 4 })
        {
            float x = LockedPoint.X + lane * 40f;
            Fire(null, new Vector2(x, LockedPoint.Y - 320f - shiftY), new Vector2(0f, 8.0f), style, scale);
        }
    }

}

public enum BossProjectileStyle : byte
{
    WraithSpear, WraithArrow, JungleSeed, VineRoot, BansheeWave, BansheeSkull, ObsidianFlame,
    IceNaga, SandShot, Sandnado, FrostPillar, FrostRing, FrostSpear, CastleGreen, CastleBlue, CastleRed,
    WraithSpin,
    ObsidianRing, SphinxRing, ObsidianMeteor, MinionSweep,
    CastleGreenRing, CastleBlueRing, CastleRedRing, CastleRedTracking
}

public sealed class CastleWeakPullPlayer : ModPlayer
{
    private int _ticks;
    private Vector2 _source;
    internal int ActiveTicks => _ticks;

    internal void Apply(Vector2 source, int ticks)
    {
        _source = source;
        _ticks = Math.Max(_ticks, ticks);
    }

    public override void PreUpdateMovement()
    {
        if (_ticks <= 0) return;
        _ticks--;
        Vector2 toward = _source - Player.Center;
        if (toward.LengthSquared() < 1f) return;
        Player.velocity += toward.SafeNormalize(Vector2.Zero) * 0.10f;
    }

    public override void UpdateDead() => _ticks = 0;
}

/// <summary>Small, dodgeable hostile attacks shared by bosses; visual shape/color follows the owning move.</summary>
public sealed class MilkcraftBossProjectile : ModProjectile
{
    public override string Texture => $"{Mod.Name}/Content/Projectiles/WraithSpear";

    public override void SetDefaults()
    {
        Projectile.width = 20;
        Projectile.height = 58;
        Projectile.hostile = true;
        Projectile.friendly = false;
        Projectile.tileCollide = true;
        Projectile.ignoreWater = true;
        Projectile.penetrate = 1;
        Projectile.timeLeft = 240;
    }

    public override void AI()
    {
        int sourceSlot = (int)Projectile.ai[1];
        if (sourceSlot < 0 || sourceSlot >= Main.maxNPCs || !Main.npc[sourceSlot].active
            || Main.npc[sourceSlot].life <= 0 || Main.npc[sourceSlot].ModNPC is not MilkcraftBossNPC)
        {
            Projectile.Kill();
            return;
        }
        BossProjectileStyle activeStyle = (BossProjectileStyle)(byte)Projectile.ai[0];
        if (activeStyle == BossProjectileStyle.CastleRedTracking && Projectile.timeLeft > 180)
        {
            int targetIndex = (int)Projectile.ai[2];
            if (targetIndex >= 0 && targetIndex < Main.maxPlayers)
            {
                Player target = Main.player[targetIndex];
                if (target.active && !target.dead)
                {
                    Vector2 desired = (target.Center - Projectile.Center).SafeNormalize(Vector2.UnitY) * 9f;
                    Projectile.velocity = Vector2.Lerp(Projectile.velocity, desired, 0.065f);
                }
            }
        }
        if (activeStyle == BossProjectileStyle.MinionSweep)
        {
            int minionIndex = (int)Projectile.ai[2];
            if (minionIndex < 0 || minionIndex >= Main.maxNPCs || !Main.npc[minionIndex].active
                || Main.npc[minionIndex].life <= 0 || Main.npc[minionIndex].GetGlobalNPC<MilkcraftBossEscortGlobalNPC>().ParentBoss != sourceSlot)
            {
                Projectile.Kill();
                return;
            }
            NPC minion = Main.npc[minionIndex];
            float age = Math.Clamp(12f - Projectile.timeLeft, 0f, 12f);
            float facing = minion.direction < 0 ? MathHelper.Pi : 0f;
            Projectile.Center = minion.Center;
            Projectile.rotation = facing + MathHelper.Lerp(-1.05f, 1.05f, age / 12f) * minion.direction;
            Projectile.velocity = Vector2.Zero;
            return;
        }
        if (activeStyle == BossProjectileStyle.WraithSpin)
        {
            float age = Math.Clamp(90f - Projectile.timeLeft, 0f, 90f);
            float angle = Projectile.ai[2] + age * (MathHelper.TwoPi * 2f / 90f);
            Projectile.Center = Main.npc[sourceSlot].Center;
            Projectile.velocity = Vector2.Zero;
            Projectile.rotation = angle;
            Lighting.AddLight(Projectile.Center, Tint(activeStyle).ToVector3() * 0.24f);
            return;
        }
        if (IsRingStyle(activeStyle))
        {
            Vector2 center = Projectile.Center;
            float radius = GetRingRadius(activeStyle, Projectile.timeLeft);
            int diameter = Math.Max(40, (int)(radius * 2f));
            Projectile.width = diameter;
            Projectile.height = diameter;
            Projectile.Center = center;
            Projectile.velocity = Vector2.Zero;
            Color ringLight = Tint(activeStyle);
            Lighting.AddLight(center, ringLight.ToVector3() * 0.38f);
            if (Main.netMode != NetmodeID.Server && Main.GameUpdateCount % 5 == 0)
            {
                float angle = Projectile.ai[2] + Main.rand.NextFloat(-0.32f, 0.32f);
                Vector2 pos = center + angle.ToRotationVector2() * radius;
                Dust dust = Dust.NewDustPerfect(pos, DustID.AncientLight, angle.ToRotationVector2() * 0.4f, 90, ringLight, 0.9f);
                dust.noGravity = true;
            }
            return;
        }
        Projectile.rotation = Projectile.velocity.ToRotation() + MathHelper.PiOver2;
        Color light = Tint((BossProjectileStyle)(byte)Projectile.ai[0]);
        Lighting.AddLight(Projectile.Center, light.ToVector3() * 0.32f);
        if (Projectile.localAI[0] == 0f)
        {
            BossProjectileStyle style = (BossProjectileStyle)(byte)Projectile.ai[0];
            Vector2 center = Projectile.Center;
            (Projectile.width, Projectile.height) = style switch
            {
                BossProjectileStyle.WraithSpear => (20, 58), BossProjectileStyle.WraithArrow => (12, 42),
                BossProjectileStyle.JungleSeed => (18, 26), BossProjectileStyle.VineRoot => (20, 64),
                BossProjectileStyle.BansheeWave => (46, 46), BossProjectileStyle.BansheeSkull => (22, 26),
                BossProjectileStyle.ObsidianFlame => (30, 40), BossProjectileStyle.IceNaga => (26, 42),
                BossProjectileStyle.SandShot => (24, 32), BossProjectileStyle.Sandnado => (32, 64),
                BossProjectileStyle.FrostPillar => (24, 72), BossProjectileStyle.FrostRing => (48, 48),
                BossProjectileStyle.FrostSpear => (14, 64), BossProjectileStyle.CastleGreen => (22, 28),
                BossProjectileStyle.CastleBlue => (30, 30), _ => (30, 38)
            };
            Projectile.Center = center;
            Projectile.localAI[0] = 1f;
        }
        if (activeStyle == BossProjectileStyle.JungleSeed)
            Projectile.velocity.Y = Math.Min(12f, Projectile.velocity.Y + 0.16f);
        if (activeStyle == BossProjectileStyle.ObsidianMeteor)
            Projectile.velocity.Y = Math.Min(13f, Projectile.velocity.Y + 0.20f);
        if (activeStyle == BossProjectileStyle.Sandnado)
        {
            Projectile.tileCollide = false;
            Projectile.scale = 1.25f;
            Projectile.timeLeft = Math.Min(Projectile.timeLeft, 180);
            float phase = Main.GameUpdateCount * 0.035f + Projectile.identity;
            Projectile.velocity.X = (float)Math.Sin(phase) * 1.05f;
            Projectile.velocity.Y = (float)Math.Sin(phase * 0.6f) * 0.72f;
        }
        if (Main.netMode != NetmodeID.Server && Main.GameUpdateCount % 7 == 0)
        {
            Color tint = Tint((BossProjectileStyle)(byte)Projectile.ai[0]);
            int dust = Dust.NewDust(Projectile.position, Projectile.width, Projectile.height, DustID.AncientLight,
                Projectile.velocity.X * -0.12f, Projectile.velocity.Y * -0.12f, 130, tint, 0.75f);
            Main.dust[dust].noGravity = true;
        }
    }

    public override void OnHitPlayer(Player target, Player.HurtInfo info)
    {
        BossProjectileStyle style = (BossProjectileStyle)(byte)Projectile.ai[0];
        if (style == BossProjectileStyle.MinionSweep)
        {
            int minion = (int)Projectile.ai[2];
            if (minion >= 0 && minion < Main.maxNPCs && Main.npc[minion].active
                && Main.npc[minion].ModNPC is FrostNagaMinion)
                target.AddBuff(BuffID.Chilled, 90);
            return;
        }
        if (style is BossProjectileStyle.FrostPillar or BossProjectileStyle.FrostRing or BossProjectileStyle.FrostSpear or BossProjectileStyle.IceNaga)
            target.AddBuff(BuffID.Chilled, 90);
        else if (style is BossProjectileStyle.ObsidianFlame or BossProjectileStyle.ObsidianRing or BossProjectileStyle.ObsidianMeteor or BossProjectileStyle.CastleRed or BossProjectileStyle.CastleRedRing)
            target.AddBuff(BuffID.OnFire3, 180);
        else if (style is BossProjectileStyle.CastleGreen or BossProjectileStyle.CastleGreenRing)
            target.AddBuff(BuffID.PotionSickness, 360);
        else if (style is BossProjectileStyle.CastleBlue or BossProjectileStyle.CastleBlueRing)
        {
            target.AddBuff(BuffID.BrokenArmor, 120);
            int sourceSlot = (int)Projectile.ai[1];
            if (sourceSlot >= 0 && sourceSlot < Main.maxNPCs && Main.npc[sourceSlot].active)
                target.GetModPlayer<CastleWeakPullPlayer>().Apply(Main.npc[sourceSlot].Center, 20);
        }
        else if (style == BossProjectileStyle.WraithArrow)
            target.AddBuff(BuffID.ShadowFlame, 120);
    }

    public override bool OnTileCollide(Vector2 oldVelocity)
    {
        BossProjectileStyle style = (BossProjectileStyle)(byte)Projectile.ai[0];
        if (style == BossProjectileStyle.ObsidianMeteor && oldVelocity.Y > 2f && Projectile.velocity.Y == 0f && Main.netMode != NetmodeID.MultiplayerClient)
        {
            int parent = (int)Projectile.ai[1];
            if (parent >= 0 && parent < Main.maxNPCs && Main.npc[parent].active && Main.npc[parent].type == ModContent.NPCType<ObsidianGuardianBoss>())
            {
                int escorts = 0;
                foreach (NPC other in Main.npc)
                    if (other.active && other.GetGlobalNPC<MilkcraftBossEscortGlobalNPC>().ParentBoss == parent)
                        escorts++;
                if (escorts < 3)
                {
                    int index = NPC.NewNPC(Projectile.GetSource_FromThis(), (int)Projectile.Center.X, (int)Projectile.Bottom.Y,
                        ModContent.NPCType<ExplosiveSlimeMinion>());
                    if (index >= 0 && index < Main.maxNPCs)
                    {
                        NPC slime = Main.npc[index];
                        slime.Center = new Vector2(Projectile.Center.X, Projectile.Bottom.Y - slime.height * 0.5f);
                        slime.GetGlobalNPC<MilkcraftBossEscortGlobalNPC>().ParentBoss = parent;
                        slime.netUpdate = true;
                    }
                }
            }
        }
        return true;
    }

    private static Color Tint(BossProjectileStyle style) => style switch
    {
        BossProjectileStyle.WraithSpear or BossProjectileStyle.WraithArrow or BossProjectileStyle.WraithSpin => new Color(170, 95, 245),
        BossProjectileStyle.JungleSeed or BossProjectileStyle.VineRoot => new Color(92, 230, 85),
        BossProjectileStyle.BansheeWave or BossProjectileStyle.BansheeSkull => new Color(105, 175, 255),
        BossProjectileStyle.ObsidianFlame or BossProjectileStyle.ObsidianRing or BossProjectileStyle.ObsidianMeteor => new Color(255, 105, 52),
        BossProjectileStyle.SphinxRing => new Color(255, 211, 116),
        BossProjectileStyle.IceNaga or BossProjectileStyle.FrostPillar or BossProjectileStyle.FrostRing or BossProjectileStyle.FrostSpear => new Color(115, 225, 255),
        BossProjectileStyle.SandShot or BossProjectileStyle.Sandnado => new Color(236, 190, 91),
        BossProjectileStyle.CastleGreen or BossProjectileStyle.CastleGreenRing => new Color(111, 225, 145),
        BossProjectileStyle.CastleBlue or BossProjectileStyle.CastleBlueRing => new Color(100, 175, 255),
        BossProjectileStyle.CastleRedRing or BossProjectileStyle.CastleRedTracking => new Color(255, 88, 75),
        _ => new Color(255, 88, 75)
    };

    private static bool IsRingStyle(BossProjectileStyle style)
        => style is BossProjectileStyle.BansheeWave or BossProjectileStyle.FrostRing or BossProjectileStyle.ObsidianRing or BossProjectileStyle.SphinxRing
            or BossProjectileStyle.CastleGreenRing or BossProjectileStyle.CastleBlueRing or BossProjectileStyle.CastleRedRing;

    internal static float GetWraithSweepReach(NPC boss)
        => boss.life * 2 < boss.lifeMax ? 150f : 130f;

    private static float GetRingRadius(BossProjectileStyle style, int timeLeft)
    {
        float maxRadius = style switch
        {
            BossProjectileStyle.ObsidianRing => 260f,
            BossProjectileStyle.SphinxRing => 230f,
            BossProjectileStyle.CastleGreenRing or BossProjectileStyle.CastleBlueRing or BossProjectileStyle.CastleRedRing => 190f,
            _ => 184f
        };
        float expansion = style switch
        {
            BossProjectileStyle.BansheeWave => 3.6f,
            BossProjectileStyle.ObsidianRing => 5.4f,
            BossProjectileStyle.SphinxRing => 4.8f,
            BossProjectileStyle.CastleGreenRing or BossProjectileStyle.CastleBlueRing or BossProjectileStyle.CastleRedRing => 4.0f,
            _ => 3.2f
        };
        int age = Math.Clamp(46 - timeLeft, 0, 46);
        return Math.Min(maxRadius, 18f + age * expansion);
    }

    private float RingGapHalfAngle => ((BossProjectileStyle)(byte)Projectile.ai[0]) switch
    {
        BossProjectileStyle.ObsidianRing => 0.48f,
        BossProjectileStyle.SphinxRing => 0.46f,
        BossProjectileStyle.CastleGreenRing or BossProjectileStyle.CastleBlueRing or BossProjectileStyle.CastleRedRing => 0.72f,
        _ => 0.525f
    };

    private static string TextureName(BossProjectileStyle style) => style switch
    {
        BossProjectileStyle.WraithSpin => nameof(BossProjectileStyle.WraithSpear),
        BossProjectileStyle.ObsidianRing => nameof(BossProjectileStyle.ObsidianFlame),
        BossProjectileStyle.ObsidianMeteor => nameof(BossProjectileStyle.ObsidianFlame),
        BossProjectileStyle.SphinxRing => nameof(BossProjectileStyle.SandShot),
        BossProjectileStyle.CastleGreenRing => nameof(BossProjectileStyle.CastleGreen),
        BossProjectileStyle.CastleBlueRing => nameof(BossProjectileStyle.CastleBlue),
        BossProjectileStyle.CastleRedRing or BossProjectileStyle.CastleRedTracking => nameof(BossProjectileStyle.CastleRed),
        _ => style.ToString()
    };

    public override bool? Colliding(Rectangle projHitbox, Rectangle targetHitbox)
    {
        BossProjectileStyle style = (BossProjectileStyle)(byte)Projectile.ai[0];
        if (style == BossProjectileStyle.WraithSpin)
        {
            int parent = (int)Projectile.ai[1];
            if (parent < 0 || parent >= Main.maxNPCs || !Main.npc[parent].active) return false;
            NPC boss = Main.npc[parent];
            Vector2 bossCenter = boss.Center;
            Vector2 direction = Projectile.rotation.ToRotationVector2();
            float point = 0f;
            return Collision.CheckAABBvLineCollision(targetHitbox.TopLeft(), targetHitbox.Size(),
                bossCenter + direction * 38f, bossCenter + direction * GetWraithSweepReach(boss), 18f, ref point);
        }
        if (style == BossProjectileStyle.MinionSweep)
        {
            int boss = (int)Projectile.ai[1];
            int minion = (int)Projectile.ai[2];
            if (boss < 0 || boss >= Main.maxNPCs || minion < 0 || minion >= Main.maxNPCs
                || !Main.npc[minion].active) return false;
            Vector2 sweepCenter = Main.npc[minion].Center;
            Vector2 axis = Projectile.rotation.ToRotationVector2();
            Vector2 a = sweepCenter + axis * 12f;
            Vector2 b = sweepCenter + axis * 84f;
            float collisionPoint = 0f;
            return Collision.CheckAABBvLineCollision(targetHitbox.TopLeft(), targetHitbox.Size(), a, b, 22f, ref collisionPoint);
        }
        if (style is BossProjectileStyle.WraithSpear or BossProjectileStyle.WraithArrow or BossProjectileStyle.FrostSpear)
        {
            Vector2 direction = Projectile.velocity.SafeNormalize(Vector2.UnitY);
            float halfLength = style == BossProjectileStyle.WraithSpear ? 30f : style == BossProjectileStyle.WraithArrow ? 22f : 32f;
            float thickness = style == BossProjectileStyle.WraithArrow ? 6f : 9f;
            float collisionPoint = 0f;
            return Collision.CheckAABBvLineCollision(targetHitbox.TopLeft(), targetHitbox.Size(),
                Projectile.Center - direction * halfLength, Projectile.Center + direction * halfLength,
                thickness, ref collisionPoint);
        }
        if (!IsRingStyle(style))
            return null;
        Vector2 center = Projectile.Center;
        Vector2 closest = Vector2.Clamp(center, targetHitbox.TopLeft(), targetHitbox.BottomRight());
        float nearestDistance = Vector2.Distance(closest, center);
        float farthestDistance = 0f;
        foreach (Vector2 corner in new[] { targetHitbox.TopLeft(), targetHitbox.TopRight(), targetHitbox.BottomLeft(), targetHitbox.BottomRight() })
            farthestDistance = Math.Max(farthestDistance, Vector2.Distance(corner, center));
        float radius = GetRingRadius(style, Projectile.timeLeft);
        float ringThickness = 8f;
        if (nearestDistance > radius + ringThickness || farthestDistance < radius - ringThickness)
            return false;
        float gapAngle = Projectile.ai[2];
        Vector2 toTarget = targetHitbox.Center.ToVector2() - center;
        if (toTarget.LengthSquared() > 1f && Math.Abs(MathHelper.WrapAngle(toTarget.ToRotation() - gapAngle)) < RingGapHalfAngle)
            return false;
        return true;
    }

    public override bool PreDraw(ref Color lightColor)
    {
        BossProjectileStyle style = (BossProjectileStyle)(byte)Projectile.ai[0];
        if (style == BossProjectileStyle.MinionSweep)
        {
            Vector2 center = Projectile.Center - Main.screenPosition;
            Vector2 axis = Projectile.rotation.ToRotationVector2();
            Vector2 lineStart = center + axis * 12f;
            Vector2 lineEnd = center + axis * 84f;
            Vector2 mainDelta = lineEnd - lineStart;
            Main.spriteBatch.Draw(TextureAssets.MagicPixel.Value, lineStart, new Rectangle(0, 0, 1, 1),
                new Color(225, 245, 255, 245), mainDelta.ToRotation(), new Vector2(0f, 0.5f),
                new Vector2(mainDelta.Length(), 7f), SpriteEffects.None, 0f);
            Color slash = new Color(125, 190, 235, 90);
            Vector2 previous = center + axis * 16f;
            for (int i = 1; i <= 5; i++)
            {
                float angle = Projectile.rotation + i * MathHelper.ToRadians(18f) * (Projectile.rotation < MathHelper.Pi ? 1f : -1f);
                Vector2 next = center + angle.ToRotationVector2() * (16f + i * 13f);
                Vector2 delta = next - previous;
                Main.spriteBatch.Draw(TextureAssets.MagicPixel.Value, previous, new Rectangle(0, 0, 1, 1), slash,
                    delta.ToRotation(), new Vector2(0f, 0.5f), new Vector2(delta.Length(), 5f), SpriteEffects.None, 0f);
                previous = next;
            }
            return false;
        }
        Texture2D texture = ModContent.Request<Texture2D>($"{Mod.Name}/Content/Projectiles/{TextureName(style)}", AssetRequestMode.ImmediateLoad).Value;
        if (style == BossProjectileStyle.WraithSpin)
        {
            int parent = (int)Projectile.ai[1];
            float reach = parent >= 0 && parent < Main.maxNPCs && Main.npc[parent].active
                ? GetWraithSweepReach(Main.npc[parent]) : 130f;
            float length = reach - 38f;
            Vector2 direction = Projectile.rotation.ToRotationVector2();
            Vector2 spearCenter = Projectile.Center + direction * (38f + length * 0.5f) - Main.screenPosition;
            Main.spriteBatch.Draw(texture, spearCenter, null, Color.White,
                Projectile.rotation + MathHelper.PiOver2, new Vector2(texture.Width * 0.5f, texture.Height * 0.5f),
                length / texture.Height, SpriteEffects.None, 0f);
            return false;
        }
        Vector2 screen = Projectile.Center - Main.screenPosition;
        if (IsRingStyle(style))
        {
            float gapAngle = Projectile.ai[2];
            float radius = GetRingRadius(style, Projectile.timeLeft);
            const int segments = 72;
            Color ringColor = Color.Lerp(Color.White, Tint(style), 0.48f) * 0.92f;
            for (int i = 0; i < segments; i++)
            {
                float a0 = MathHelper.TwoPi * i / segments;
                float a1 = MathHelper.TwoPi * (i + 1) / segments;
                float mid = MathHelper.WrapAngle((a0 + a1) * 0.5f - gapAngle);
                if (Math.Abs(mid) < RingGapHalfAngle) continue;
                Vector2 start = screen + a0.ToRotationVector2() * radius;
                Vector2 end = screen + a1.ToRotationVector2() * radius;
                Vector2 delta = end - start;
                Main.spriteBatch.Draw(TextureAssets.MagicPixel.Value, start, new Rectangle(0, 0, 1, 1),
                    ringColor, delta.ToRotation(), new Vector2(0f, 0.5f), new Vector2(delta.Length() + 1f, 16f), SpriteEffects.None, 0f);
            }
            return false;
        }
        float rotation = style is BossProjectileStyle.VineRoot or BossProjectileStyle.Sandnado
            or BossProjectileStyle.FrostPillar or BossProjectileStyle.BansheeWave or BossProjectileStyle.FrostRing
            ? 0f : Projectile.velocity.ToRotation() + MathHelper.PiOver2;
        Vector2 origin = new(texture.Width * 0.5f, texture.Height * 0.5f);
        Main.spriteBatch.Draw(texture, screen, null, Color.White, rotation, origin, Projectile.scale, SpriteEffects.None, 0f);
        return false;
    }
}

internal static class BossItemTypes
{
    internal static int Pet(MilkcraftBossKind k) => k switch
    {
        MilkcraftBossKind.WraithWarrior => ModContent.ItemType<WraithWarriorPetSummon>(),
        MilkcraftBossKind.JungleLord => ModContent.ItemType<JungleLordPetSummon>(),
        MilkcraftBossKind.Banshee => ModContent.ItemType<BansheePetSummon>(),
        MilkcraftBossKind.ObsidianGuardian => ModContent.ItemType<ObsidianGuardianPetSummon>(),
        MilkcraftBossKind.Sphinx => ModContent.ItemType<SphinxPetSummon>(),
        MilkcraftBossKind.FrostWitch => ModContent.ItemType<FrostWitchPetSummon>(),
        _ => ModContent.ItemType<CastleCorePetSummon>()
    };

    internal static int NpcType(MilkcraftBossKind k) => k switch
    {
        MilkcraftBossKind.WraithWarrior => ModContent.NPCType<WraithWarriorBoss>(),
        MilkcraftBossKind.JungleLord => ModContent.NPCType<JungleLordBoss>(),
        MilkcraftBossKind.Banshee => ModContent.NPCType<BansheeBoss>(),
        MilkcraftBossKind.ObsidianGuardian => ModContent.NPCType<ObsidianGuardianBoss>(),
        MilkcraftBossKind.Sphinx => ModContent.NPCType<SphinxBoss>(),
        MilkcraftBossKind.FrostWitch => ModContent.NPCType<FrostWitchBoss>(),
        _ => ModContent.NPCType<CastleCoreBoss>()
    };

    internal static int Bag(MilkcraftBossKind k) => k switch
    {
        MilkcraftBossKind.WraithWarrior => ModContent.ItemType<WraithWarriorBag>(),
        MilkcraftBossKind.JungleLord => ModContent.ItemType<JungleLordBag>(),
        MilkcraftBossKind.Banshee => ModContent.ItemType<BansheeBag>(),
        MilkcraftBossKind.ObsidianGuardian => ModContent.ItemType<ObsidianGuardianBag>(),
        MilkcraftBossKind.Sphinx => ModContent.ItemType<SphinxBag>(),
        MilkcraftBossKind.FrostWitch => ModContent.ItemType<FrostWitchBag>(),
        _ => ModContent.ItemType<CastleCoreBag>()
    };
    internal static int Trophy(MilkcraftBossKind k) => k switch
    {
        MilkcraftBossKind.WraithWarrior => ModContent.ItemType<WraithWarriorTrophy>(),
        MilkcraftBossKind.JungleLord => ModContent.ItemType<JungleLordTrophy>(),
        MilkcraftBossKind.Banshee => ModContent.ItemType<BansheeTrophy>(),
        MilkcraftBossKind.ObsidianGuardian => ModContent.ItemType<ObsidianGuardianTrophy>(),
        MilkcraftBossKind.Sphinx => ModContent.ItemType<SphinxTrophy>(),
        MilkcraftBossKind.FrostWitch => ModContent.ItemType<FrostWitchTrophy>(),
        _ => ModContent.ItemType<CastleCoreTrophy>()
    };
    internal static int Relic(MilkcraftBossKind k) => k switch
    {
        MilkcraftBossKind.WraithWarrior => ModContent.ItemType<WraithWarriorRelic>(),
        MilkcraftBossKind.JungleLord => ModContent.ItemType<JungleLordRelic>(),
        MilkcraftBossKind.Banshee => ModContent.ItemType<BansheeRelic>(),
        MilkcraftBossKind.ObsidianGuardian => ModContent.ItemType<ObsidianGuardianRelic>(),
        MilkcraftBossKind.Sphinx => ModContent.ItemType<SphinxRelic>(),
        MilkcraftBossKind.FrostWitch => ModContent.ItemType<FrostWitchRelic>(),
        _ => ModContent.ItemType<CastleCoreRelic>()
    };
    internal static int Mask(MilkcraftBossKind k) => k switch
    {
        MilkcraftBossKind.WraithWarrior => ModContent.ItemType<WraithWarriorMask>(),
        MilkcraftBossKind.JungleLord => ModContent.ItemType<JungleLordMask>(),
        MilkcraftBossKind.Banshee => ModContent.ItemType<BansheeMask>(),
        MilkcraftBossKind.ObsidianGuardian => ModContent.ItemType<ObsidianGuardianMask>(),
        MilkcraftBossKind.Sphinx => ModContent.ItemType<SphinxMask>(),
        MilkcraftBossKind.FrostWitch => ModContent.ItemType<FrostWitchMask>(),
        _ => ModContent.ItemType<CastleCoreMask>()
    };

    internal static (int Type, int Count)[] FixedRewards(MilkcraftBossKind k) => k switch
    {
        MilkcraftBossKind.WraithWarrior => new[] { (ModContent.ItemType<BlackIronOre>(), 120), (ModContent.ItemType<PinkDiamond>(), 8), (ModContent.ItemType<WraithWarriorProof>(), 1) },
        MilkcraftBossKind.JungleLord => new[] { (ModContent.ItemType<BlackIronOre>(), 120), (ItemID.Leather, 20), (ItemID.JungleSpores, 5), (ModContent.ItemType<PinkDiamond>(), 8), (ModContent.ItemType<JungleLordProof>(), 1) },
        MilkcraftBossKind.Banshee => new[] { (ModContent.ItemType<BlackIronOre>(), 120), (ModContent.ItemType<CoarseWool>(), 20), (ItemID.FallenStar, 5), (ModContent.ItemType<PinkDiamond>(), 8), (ModContent.ItemType<BansheeProof>(), 1) },
        MilkcraftBossKind.ObsidianGuardian => new[] { (ModContent.ItemType<TruthSword>(), 1), (ModContent.ItemType<CrystalSkull>(), 2), (ModContent.ItemType<EnchantmentHeart>(), 1) },
        MilkcraftBossKind.Sphinx => new[] { (ModContent.ItemType<TruthBow>(), 1), (ModContent.ItemType<AlienKeratin>(), 2), (ModContent.ItemType<EnchantmentHeart>(), 1) },
        MilkcraftBossKind.FrostWitch => new[] { (ModContent.ItemType<TruthStaff>(), 1), (ModContent.ItemType<ArcaneCore>(), 2), (ModContent.ItemType<EnchantmentHeart>(), 1) },
        _ => new[] {
            (ModContent.ItemType<BrokenSwordHilt>(), 1), (ModContent.ItemType<WitherTear>(), 2),
            (ModContent.ItemType<BrokenBowstring>(), 1), (ModContent.ItemType<DemonWing>(), 2),
            (ModContent.ItemType<BrokenStaffHandle>(), 1), (ModContent.ItemType<EnchantedShinbone>(), 2),
            (ModContent.ItemType<CastleCoreClearSeal>(), 1)
        }
    };
}

