using System;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.DataStructures;
using Terraria.GameContent.ItemDropRules;
using Terraria.ID;
using Terraria.ModLoader;
using MilkcraftTerraria.Content.Items;

namespace MilkcraftTerraria.Content;

internal static class AdvancedMountDefaults
{
    private static readonly FieldInfo MountFrameField = typeof(Mount).GetField("_frame",
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
        ?? throw new MissingFieldException(typeof(Mount).FullName, "_frame");
    private static readonly FieldInfo MountFrameStateField = typeof(Mount).GetField("_frameState",
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
        ?? throw new MissingFieldException(typeof(Mount).FullName, "_frameState");
    private static readonly FieldInfo MountFrameCounterField = typeof(Mount).GetField("_frameCounter",
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
        ?? throw new MissingFieldException(typeof(Mount).FullName, "_frameCounter");

    internal static void SetAtlas(Mount.MountData data, ModMount mount, string name)
    {
        data.textureWidth = 128;
        data.textureHeight = 1536;
        if (Main.dedServ) return;
        var back = ModContent.Request<Microsoft.Xna.Framework.Graphics.Texture2D>(
            $"{mount.Mod.Name}/Content/Mounts/{name}_Back", ReLogic.Content.AssetRequestMode.ImmediateLoad);
        data.backTexture = back;
        data.textureWidth = back.Value.Width;
        data.textureHeight = back.Value.Height;
    }

    internal static void SetGroundFrames(Mount.MountData data, int runFrames = 8, int runningDelay = 62)
    {
        data.totalFrames = 16;
        data.playerYOffsets = new int[16];
        Array.Fill(data.playerYOffsets, 24);
        data.standingFrameStart = 0;
        data.standingFrameCount = 1;
        data.standingFrameDelay = 12;
        data.runningFrameStart = 1;
        data.runningFrameCount = runFrames;
        data.runningFrameDelay = runningDelay;
        data.inAirFrameStart = 9;
        data.inAirFrameCount = 1;
        data.inAirFrameDelay = 8;
        data.flyingFrameStart = 9;
        data.flyingFrameCount = 1;
        data.flyingFrameDelay = 8;
        data.swimFrameStart = 9;
        data.swimFrameCount = 1;
        data.swimFrameDelay = 8;
        data.flightTimeMax = 0;
        data.usesHover = false;
    }

    internal static void SetCurrentFrame(Mount mount, int frame) => MountFrameField.SetValue(mount, frame);

    internal static void SetNativeFrameState(Mount mount, int state)
    {
        MountFrameStateField.SetValue(mount, state);
        MountFrameCounterField.SetValue(mount, 0f);
    }

    internal static void AddMountedLight(Player player, float r, float g, float b, int dustType, float dustChance = 0f)
    {
        Vector2 lightAt = player.MountedCenter;
        Lighting.AddLight(lightAt, r, g, b);
        if (Main.dedServ || dustChance <= 0f || !Main.rand.NextBool((int)MathF.Round(1f / dustChance))) return;
        int dust = Dust.NewDust(player.Bottom - new Vector2(8f, 5f), 16, 7, dustType, 0f, 0.25f, 120);
        Main.dust[dust].noGravity = true;
        Main.dust[dust].noLight = true;
    }

    internal static bool IsHallowBossGenuinelyEnraged(NPC npc)
        => npc.type == NPCID.HallowBoss && (npc.ai[3] == 2f || npc.ai[3] == 3f);
}

public sealed class WhiteTigerMount : ModMount
{
    public override string Texture => $"{Mod.Name}/Content/Mounts/WhiteTiger";
    public override void SetStaticDefaults()
    {
        AdvancedMountDefaults.SetAtlas(MountData, this, "WhiteTiger");
        MountData.buff = ModContent.BuffType<WhiteTigerMountBuff>();
        AdvancedMountDefaults.SetGroundFrames(MountData, runningDelay: 96);
        Array.Fill(MountData.playerYOffsets, 33);
        MountData.heightBoost = 30;
        MountData.runSpeed = 7.5f;
        MountData.dashSpeed = 16f;
        MountData.acceleration = 0.38f;
        MountData.jumpHeight = 12;
        MountData.jumpSpeed = 6f;
        MountData.fallDamage = 0.8f;
        MountData.xOffset = 0;
        MountData.yOffset = -5;
        MountData.playerHeadOffset = 16;
        MountData.spawnDust = DustID.Silver;
        MountData.spawnDustNoGravity = true;
    }

    public override bool UpdateFrame(Player player, int state, Vector2 velocity)
    {
        if (player.mount.Type == Type && player.GetModPlayer<AdvancedMountAbilityPlayer>().TryGetDashFrame(out int frame))
        {
            AdvancedMountDefaults.SetCurrentFrame(player.mount, frame);
            return false;
        }
        return true;
    }

    public override void UpdateEffects(Player player)
    {
        if (player.mount.Active && player.mount.Type == Type)
            AdvancedMountDefaults.AddMountedLight(player, 0.12f, 0.13f, 0.16f, DustID.Silver,
                player.velocity.X == 0f ? 0f : 0.035f);
    }
}

public sealed class WhiteNineTailedFoxMount : ModMount
{
    public override string Texture => $"{Mod.Name}/Content/Mounts/WhiteNineTailedFox";
    public override void SetStaticDefaults()
    {
        AdvancedMountDefaults.SetAtlas(MountData, this, "WhiteNineTailedFox");
        MountData.buff = ModContent.BuffType<WhiteNineTailedFoxMountBuff>();
        AdvancedMountDefaults.SetGroundFrames(MountData, runningDelay: 108);
        MountData.heightBoost = 34;
        MountData.runSpeed = 8.5f;
        MountData.dashSpeed = 18f;
        MountData.acceleration = 0.42f;
        MountData.jumpHeight = 14;
        MountData.jumpSpeed = 6.5f;
        MountData.fallDamage = 0.75f;
        MountData.xOffset = 0;
        MountData.yOffset = 4;
        MountData.playerHeadOffset = 18;
        MountData.spawnDust = DustID.WhiteTorch;
        MountData.spawnDustNoGravity = true;
    }

    public override bool UpdateFrame(Player player, int state, Vector2 velocity)
    {
        if (player.mount.Type == Type && player.GetModPlayer<AdvancedMountAbilityPlayer>().TryGetDashFrame(out int frame))
        {
            AdvancedMountDefaults.SetCurrentFrame(player.mount, frame);
            return false;
        }
        return true;
    }

    public override void UpdateEffects(Player player)
    {
        if (player.mount.Active && player.mount.Type == Type)
            AdvancedMountDefaults.AddMountedLight(player, 0.10f, 0.17f, 0.23f, DustID.Ice,
                player.velocity.X == 0f ? 0f : 0.045f);
    }
}

public sealed class RhinoMount : ModMount
{
    public override string Texture => $"{Mod.Name}/Content/Mounts/Rhino";
    public override void SetStaticDefaults()
    {
        AdvancedMountDefaults.SetAtlas(MountData, this, "Rhino");
        MountData.buff = ModContent.BuffType<RhinoMountBuff>();
        AdvancedMountDefaults.SetGroundFrames(MountData, runningDelay: 70);
        Array.Fill(MountData.playerYOffsets, 30);
        MountData.heightBoost = 34;
        MountData.runSpeed = 6.5f;
        MountData.dashSpeed = 11f;
        MountData.acceleration = 0.3f;
        MountData.jumpHeight = 10;
        MountData.jumpSpeed = 5.6f;
        MountData.fallDamage = 0.7f;
        MountData.xOffset = 0;
        MountData.yOffset = -1;
        MountData.playerHeadOffset = 16;
        MountData.spawnDust = DustID.Stone;
    }

    public override void UpdateEffects(Player player)
    {
        if (player.mount.Active && player.mount.Type == Type)
        {
            player.statDefense += 8;
            AdvancedMountDefaults.AddMountedLight(player, 0.15f, 0.07f, 0.18f, DustID.Stone,
                Math.Abs(player.velocity.X) > 0.5f ? 0.035f : 0f);
        }
    }
}

public sealed class FlameWarhorseMount : ModMount
{
    public override string Texture => $"{Mod.Name}/Content/Mounts/FlameWarhorse";
    public override void SetStaticDefaults()
    {
        AdvancedMountDefaults.SetAtlas(MountData, this, "FlameWarhorse");
        MountData.buff = ModContent.BuffType<FlameWarhorseMountBuff>();
        AdvancedMountDefaults.SetGroundFrames(MountData, runningDelay: 78);
        Array.Fill(MountData.playerYOffsets, 32);
        MountData.heightBoost = 34;
        MountData.runSpeed = 7.5f;
        MountData.dashSpeed = 13f;
        MountData.acceleration = 0.34f;
        MountData.jumpHeight = 12;
        MountData.jumpSpeed = 6.1f;
        MountData.fallDamage = 0.75f;
        MountData.xOffset = 0;
        MountData.yOffset = -4;
        MountData.playerHeadOffset = 17;
        MountData.spawnDust = DustID.Torch;
        MountData.spawnDustNoGravity = true;
    }

    public override void UpdateEffects(Player player)
    {
        if (player.mount.Active && player.mount.Type == Type)
            AdvancedMountDefaults.AddMountedLight(player, 0.24f, 0.08f, 0.025f, DustID.Torch,
                Math.Abs(player.velocity.X) > 0.5f ? 0.07f : 0.025f);
    }
}

public sealed class WhiteUnicornMount : ModMount
{
    public override string Texture => $"{Mod.Name}/Content/Mounts/WhiteUnicorn";
    public override void SetStaticDefaults()
    {
        AdvancedMountDefaults.SetAtlas(MountData, this, "WhiteUnicorn");
        MountData.buff = ModContent.BuffType<WhiteUnicornMountBuff>();
        MountData.totalFrames = 16;
        MountData.playerYOffsets = new int[16];
        Array.Fill(MountData.playerYOffsets, 34);
        MountData.standingFrameStart = 0;
        MountData.standingFrameCount = 1;
        MountData.standingFrameDelay = 12;
        MountData.runningFrameStart = 1;
        MountData.runningFrameCount = 4;
        MountData.runningFrameDelay = 96;
        MountData.inAirFrameStart = 6;
        MountData.inAirFrameCount = 8;
        MountData.inAirFrameDelay = 6;
        MountData.flyingFrameStart = 6;
        MountData.flyingFrameCount = 8;
        MountData.flyingFrameDelay = 6;
        MountData.swimFrameStart = 14;
        MountData.swimFrameCount = 2;
        MountData.swimFrameDelay = 6;
        MountData.heightBoost = 34;
        MountData.runSpeed = 7f;
        MountData.dashSpeed = 12f;
        MountData.acceleration = 0.32f;
        MountData.jumpHeight = 10;
        MountData.jumpSpeed = 5.5f;
        MountData.flightTimeMax = 320;
        MountData.usesHover = true;
        MountData.fatigueMax = 1;
        MountData.fallDamage = 0;
        MountData.xOffset = 0;
        MountData.yOffset = 0;
        MountData.playerHeadOffset = 16;
        MountData.spawnDust = DustID.WhiteTorch;
        MountData.spawnDustNoGravity = true;
    }

    public override bool UpdateFrame(Player player, int state, Vector2 velocity)
    {
        if (player.mount.Type != Type || state != 2) return true;
        // Mount.UpdateFrame returns immediately when this hook returns false,
        // before writing its private _frameState. Keep that native movement
        // state synchronized so Mount.Hover recognizes state 2 instead of
        // applying ordinary downward gravity forever.
        AdvancedMountDefaults.SetNativeFrameState(player.mount, state);
        int frame = player.GetModPlayer<AdvancedMountAbilityPlayer>().GetUnicornAirFrame(player);
        AdvancedMountDefaults.SetCurrentFrame(player.mount, frame);
        return false;
    }

    public override void UpdateEffects(Player player)
    {
        if (!player.mount.Active || player.mount.Type != Type) return;
        player.mount.ResetFlightTime(0f);
        Lighting.AddLight(player.MountedCenter, 0.18f, 0.18f, 0.20f);
        if (player.whoAmI == Main.myPlayer && Main.GameUpdateCount % 5 == 0)
        {
            int dust = Dust.NewDust(player.Bottom - new Vector2(8f, 4f), 16, 5, DustID.WhiteTorch, 0f, 1.2f, 90);
            Main.dust[dust].noGravity = true;
        }
    }
}

public abstract class ExpansionMountBuff : ModBuff
{
    protected abstract int MountType { get; }
    public override string Texture => $"{Mod.Name}/Content/Items/{GetType().Name.Replace("MountBuff", "Summon")}";
    public override void SetStaticDefaults()
    {
        Main.buffNoTimeDisplay[Type] = true;
        Main.buffNoSave[Type] = true;
        BuffID.Sets.BasicMountData[Type] = new BuffID.Sets.BuffMountData { mountID = MountType, faceLeft = false };
    }
}

public sealed class WhiteTigerMountBuff : ExpansionMountBuff { protected override int MountType => ModContent.MountType<WhiteTigerMount>(); }
public sealed class WhiteNineTailedFoxMountBuff : ExpansionMountBuff { protected override int MountType => ModContent.MountType<WhiteNineTailedFoxMount>(); }
public sealed class RhinoMountBuff : ExpansionMountBuff { protected override int MountType => ModContent.MountType<RhinoMount>(); }
public sealed class FlameWarhorseMountBuff : ExpansionMountBuff { protected override int MountType => ModContent.MountType<FlameWarhorseMount>(); }
public sealed class WhiteUnicornMountBuff : ExpansionMountBuff { protected override int MountType => ModContent.MountType<WhiteUnicornMount>(); }

/// <summary>Player-specific dash/contact state. All NPC strikes originate from the local owner.</summary>
public sealed class AdvancedMountAbilityPlayer : ModPlayer
{
    private bool _leftWasDown, _rightWasDown;
    private int _leftTapWindow, _rightTapWindow;
    private int _dashTicks, _dashCooldown, _dashDirection, _dashDamage, _dashImmunity;
    private readonly HashSet<int> _dashHit = new();
    private int[] _rhinoCooldown = Array.Empty<int>();
    private int _flameTick;
    private Vector2 _lastFlamePosition;
    private ulong _lastUnicornFrameTick = ulong.MaxValue;
    private int _unicornFrameTimer;
    private int _unicornFlightFrame = 6;
    private int _unicornGlideFrame = 14;

    internal int GetUnicornAirFrame(Player player)
    {
        if (player.justJumped)
            return 5;

        if (_lastUnicornFrameTick != Main.GameUpdateCount)
        {
            _lastUnicornFrameTick = Main.GameUpdateCount;
            if (++_unicornFrameTimer >= 6)
            {
                _unicornFrameTimer = 0;
                _unicornFlightFrame = 6 + (_unicornFlightFrame - 5) % 8;
                _unicornGlideFrame = _unicornGlideFrame == 14 ? 15 : 14;
            }
        }

        if (player.controlDown)
            return _unicornGlideFrame;
        return _unicornFlightFrame;
    }

    internal bool TryGetDashFrame(out int frame)
    {
        if (_dashTicks <= 0 || !Player.mount.Active
            || (Player.mount.Type != ModContent.MountType<WhiteTigerMount>()
                && Player.mount.Type != ModContent.MountType<WhiteNineTailedFoxMount>()))
        {
            frame = 0;
            return false;
        }
        int total = Player.mount.Type == ModContent.MountType<WhiteNineTailedFoxMount>() ? 14 : 12;
        int elapsed = total - _dashTicks;
        frame = 10 + Math.Min(5, elapsed * 6 / total);
        return true;
    }

    public override void Initialize() => _rhinoCooldown = new int[Main.maxNPCs + 1];

    public override void PreUpdateMovement()
    {
        _dashCooldown = Math.Max(0, _dashCooldown - 1);
        bool tiger = Player.mount.Active && Player.mount.Type == ModContent.MountType<WhiteTigerMount>();
        bool fox = Player.mount.Active && Player.mount.Type == ModContent.MountType<WhiteNineTailedFoxMount>();
        if (!tiger && !fox)
        {
            _dashTicks = 0;
            _leftTapWindow = _rightTapWindow = 0;
            _leftWasDown = Player.controlLeft;
            _rightWasDown = Player.controlRight;
            return;
        }

        TickTap(Player.controlLeft, ref _leftWasDown, ref _leftTapWindow, -1, fox);
        TickTap(Player.controlRight, ref _rightWasDown, ref _rightTapWindow, 1, fox);
        if (_dashTicks > 0)
        {
            Player.direction = _dashDirection;
            Player.velocity.X = _dashDirection * (fox ? 18f : 16f);
        }
    }

    public override void PostUpdate()
    {
        bool tiger = Player.mount.Active && Player.mount.Type == ModContent.MountType<WhiteTigerMount>();
        bool fox = Player.mount.Active && Player.mount.Type == ModContent.MountType<WhiteNineTailedFoxMount>();
        if (!tiger && !fox)
        {
            _dashTicks = 0;
        }
        else if (_dashTicks > 0)
        {
            DealDashHits();
            _dashTicks--;
        }

        bool rhino = Player.mount.Active && Player.mount.Type == ModContent.MountType<RhinoMount>();
        bool flameHorse = Player.mount.Active && Player.mount.Type == ModContent.MountType<FlameWarhorseMount>();
        if (_rhinoCooldown.Length != Main.maxNPCs + 1) _rhinoCooldown = new int[Main.maxNPCs + 1];
        for (int i = 0; i < _rhinoCooldown.Length; i++)
            if (_rhinoCooldown[i] > 0) _rhinoCooldown[i]--;
        if (rhino) DealRhinoHits();
        if (flameHorse) LeaveFireTrail();
        else { _flameTick = 0; _lastFlamePosition = Vector2.Zero; }
    }

    private void TickTap(bool down, ref bool wasDown, ref int tapWindow, int direction, bool fox)
    {
        if (tapWindow > 0) tapWindow--;
        if (down && !wasDown)
        {
            if (tapWindow > 0 && _dashCooldown == 0)
                StartDash(direction, fox);
            else
                tapWindow = 15; // Mirrors the vanilla double-tap cardinal window.
        }
        wasDown = down;
    }

    private void StartDash(int direction, bool fox)
    {
        _dashDirection = direction;
        _dashTicks = fox ? 14 : 12;
        _dashCooldown = 90;
        _dashDamage = fox ? 125 : 45;
        _dashImmunity = fox ? 30 : 18;
        _dashHit.Clear();
        Player.direction = direction;
        Player.immune = true;
        Player.immuneTime = Math.Max(Player.immuneTime, _dashImmunity);
        if (Player.hurtCooldowns is not null)
            for (int i = 0; i < Player.hurtCooldowns.Length; i++)
                Player.hurtCooldowns[i] = Math.Max(Player.hurtCooldowns[i], _dashImmunity);
    }

    private bool CanDealNpcDamage(NPC npc)
        => npc.active && npc.life > 0 && !npc.friendly && !npc.townNPC && !npc.dontTakeDamage && !npc.immortal
           && npc.type != NPCID.TargetDummy;

    private Rectangle GetMountStrikeHitbox()
    {
        const int width = 86;
        const int height = 42;
        Vector2 bottom = Player.Bottom;
        return new Rectangle((int)MathF.Round(Player.Center.X - width * 0.5f),
            (int)MathF.Round(bottom.Y - height), width, height);
    }

    private bool CanReachMountHitbox(NPC npc)
    {
        if (!GetMountStrikeHitbox().Intersects(npc.Hitbox)) return false;
        // Keep enlarged head/body reach from striking through a solid wall.
        // Both endpoints are above the ground tile to avoid treating the floor as a wall.
        Vector2 from = new(Player.Center.X, Player.Bottom.Y - 23f);
        Vector2 to = new(npc.Center.X, npc.position.Y + Math.Min(npc.height * 0.5f, 12f));
        return Collision.CanHitLine(from, 1, 1, to, 1, 1);
    }

    private void Strike(NPC npc, int damage)
    {
        int direction = Math.Sign(npc.Center.X - Player.Center.X);
        if (direction == 0) direction = Player.direction;
        npc.SimpleStrikeNPC(damage, direction, false, 2f, DamageClass.Melee, true, Player.luck);
    }

    private void DealDashHits()
    {
        if (Player.whoAmI != Main.myPlayer || Main.gamePaused || Math.Abs(Player.velocity.X) <= 0.5f) return;
        for (int i = 0; i < Main.npc.Length; i++)
        {
            NPC npc = Main.npc[i];
            if (_dashHit.Contains(i) || !CanDealNpcDamage(npc) || !CanReachMountHitbox(npc)) continue;
            _dashHit.Add(i);
            Strike(npc, _dashDamage);
        }
    }

    private void DealRhinoHits()
    {
        if (Player.whoAmI != Main.myPlayer || Main.gamePaused || Math.Abs(Player.velocity.X) <= 0.5f) return;
        int damage = 70 + (int)MathF.Round(20f * MathHelper.Clamp(Math.Abs(Player.velocity.X) / 11f, 0f, 1f));
        for (int i = 0; i < Main.npc.Length; i++)
        {
            NPC npc = Main.npc[i];
            if (_rhinoCooldown[i] > 0 || !CanDealNpcDamage(npc) || !CanReachMountHitbox(npc)) continue;
            _rhinoCooldown[i] = 30;
            Strike(npc, damage);
        }
    }

    private void LeaveFireTrail()
    {
        _flameTick++;
        Vector2 groundProbe = new(Player.Center.X - 2f, Player.Bottom.Y);
        if (Player.whoAmI != Main.myPlayer || Math.Abs(Player.velocity.X) <= 0.5f || Player.velocity.Y != 0f
            || !Collision.SolidCollision(groundProbe, 4, 3) || _flameTick < 8) return;
        Vector2 position = new(Player.Center.X, Player.Bottom.Y - 3f);
        if (_lastFlamePosition != Vector2.Zero && Vector2.Distance(position, _lastFlamePosition) < 24f) return;
        _flameTick = 0;
        _lastFlamePosition = position;
        Projectile.NewProjectile(Player.GetSource_Misc("MilkcraftFlameWarhorseTrail"), position, Vector2.Zero,
            ModContent.ProjectileType<FlameWarhorseTrailFire>(), 30, 0f, Player.whoAmI);
    }
}

/// <summary>Damage-only, non-terrain fire patch left by the flame horse.</summary>
public sealed class FlameWarhorseTrailFire : ModProjectile
{
    private int _particleTimer;
    public override string Texture => "Terraria/Images/Projectile_0";
    public override void SetDefaults()
    {
        Projectile.width = 28;
        Projectile.height = 10;
        Projectile.friendly = true;
        Projectile.hostile = false;
        Projectile.penetrate = -1;
        Projectile.timeLeft = 180;
        Projectile.tileCollide = false;
        Projectile.ignoreWater = true;
        Projectile.damage = 30;
        Projectile.usesLocalNPCImmunity = true;
        Projectile.localNPCHitCooldown = 30;
        Projectile.hide = true;
    }
    public override bool? CanDamage() => false;
    public override bool PreDraw(ref Color lightColor) => false;
    public override void AI()
    {
        Projectile.velocity = Vector2.Zero;
        Lighting.AddLight(Projectile.Center, 0.42f, 0.12f, 0.025f);
        if (Projectile.owner == Main.myPlayer)
        {
            Rectangle hitbox = Projectile.Hitbox;
            foreach (NPC npc in Main.npc)
            {
                if (!npc.active || npc.life <= 0 || npc.friendly || npc.townNPC || npc.dontTakeDamage || npc.immortal
                    || npc.type == NPCID.TargetDummy || !hitbox.Intersects(npc.Hitbox)
                    || npc.whoAmI >= Projectile.localNPCImmunity.Length
                    || Projectile.localNPCImmunity[npc.whoAmI] > 0) continue;
                int dir = Math.Sign(npc.Center.X - Projectile.Center.X);
                if (dir == 0) dir = 1;
                npc.SimpleStrikeNPC(Projectile.damage, dir, false, 0f, DamageClass.Melee);
                npc.AddBuff(BuffID.OnFire3, 180);
                Projectile.localNPCImmunity[npc.whoAmI] = 30;
            }
        }
        if (++_particleTimer >= 8 && !Main.dedServ)
        {
            _particleTimer = 0;
            int d = Dust.NewDust(Projectile.position, Projectile.width, Projectile.height, DustID.Torch, 0f, -1.5f, 100);
            Main.dust[d].noGravity = true;
            Main.dust[d].noLight = true;
        }
    }
}

public sealed class AdvancedMountWorldLoot : ModSystem
{
    public override void PostWorldGen()
    {
        // Add, never replace, loot in underground standard forest wood/gold
        // and living-wood chests. Existing chest contents are preserved.
        foreach (Chest chest in Main.chest)
        {
            if (chest is null || chest.y <= Main.worldSurface || chest.y >= Main.maxTilesY - 200) continue;
            Tile tile = Main.tile[chest.x, chest.y];
            if (!tile.HasTile) continue;
            int chestItemType = ResolveChestItemType(tile);
            bool jungle = IsJungleArea(chest.x, chest.y);
            if (chestItemType <= 0)
                continue;

            bool targetForestChest = !jungle && IsForestCave(chest.x, chest.y)
                && (chestItemType == ItemID.Chest || chestItemType == ItemID.GoldChest
                    || chestItemType == ItemID.LivingWoodChest);
            bool targetJungleChest = jungle && chestItemType == ItemID.IvyChest;
            if (!targetForestChest && !targetJungleChest) continue;
            if (!Main.rand.NextBool(10)) continue;
            AddToFirstEmptySlot(chest, ModContent.ItemType<WhiteTigerSummon>());
        }
    }

    private static int ResolveChestItemType(Tile tile)
    {
        if (tile.TileType != TileID.Containers && tile.TileType != TileID.Containers2) return 0;
        int style = tile.TileFrameX / 36;

        // The native chestSpawn arrays omit some placed chest items (notably
        // Ivy Chest in this runtime). Resolve the important vanilla variants
        // through their actual Item.createTile/placeStyle data first.
        foreach (int itemType in new[] { ItemID.Chest, ItemID.GoldChest, ItemID.LivingWoodChest, ItemID.IvyChest })
        {
            Item item = new(itemType);
            if (item.createTile == tile.TileType && item.placeStyle == style)
                return itemType;
        }

        int[] styles = tile.TileType == TileID.Containers ? Chest.chestItemSpawn : Chest.chestItemSpawn2;
        return style >= 0 && style < styles.Length ? styles[style] : 0;
    }

    private static bool IsJungleArea(int x, int y)
    {
        int jungleTiles = 0;
        for (int dx = -6; dx <= 6; dx++)
        for (int dy = -6; dy <= 6; dy++)
        {
            int tx = x + dx, ty = y + dy;
            if (!WorldGen.InWorld(tx, ty, 1)) continue;
            Tile near = Main.tile[tx, ty];
            if (near.HasTile && near.TileType is TileID.JungleGrass or TileID.Mud or TileID.JunglePlants
                or TileID.JunglePlants2 or TileID.JungleVines)
                jungleTiles++;
        }
        return jungleTiles >= 20 || Main.tile[x, y].WallType == WallID.JungleUnsafe;
    }

    internal static bool IsForestCave(int x, int y)
    {
        // Standard underground ocean beaches extend several hundred tiles
        // inward on each side. Do not treat their ordinary wood/gold chests
        // as forest loot.
        // Use the same beach test as Player.ZoneBeach/world biomes so the
        // forest filter tracks the generated beach band and ocean level.
        if (WorldGen.oceanDepths(x, y))
            return false;

        int otherBiomeTiles = 0;
        for (int dx = -5; dx <= 5; dx++)
        for (int dy = -5; dy <= 5; dy++)
        {
            int tx = x + dx, ty = y + dy;
            if (!WorldGen.InWorld(tx, ty, 1)) continue;
            Tile near = Main.tile[tx, ty];
            if (near.HasTile && near.TileType < Main.tileDungeon.Length && Main.tileDungeon[near.TileType])
                return false;
            if (IsDungeonWall(near.WallType))
                return false;
            if (!near.HasTile) continue;
            if (near.TileType is TileID.SnowBlock or TileID.IceBlock or TileID.Sand or TileID.HardenedSand
                or TileID.Sandstone or TileID.Ebonstone or TileID.Crimstone or TileID.Pearlstone
                or TileID.Ebonsand or TileID.Crimsand or TileID.Pearlsand or TileID.CorruptGrass or TileID.CrimsonGrass)
                otherBiomeTiles++;
        }
        return otherBiomeTiles < 20;
    }

    private static bool IsDungeonWall(ushort wallType)
    {
        if (wallType < Main.wallDungeon.Length && Main.wallDungeon[wallType]) return true;
        if (wallType >= WallID.Sets.BlendType.Length) return false;
        int baseWall = WallID.Sets.BlendType[wallType];
        return wallType == WallID.BlueDungeon || wallType == WallID.GreenDungeon || wallType == WallID.PinkDungeon
            || baseWall == WallID.BlueDungeon || baseWall == WallID.GreenDungeon || baseWall == WallID.PinkDungeon;
    }

    internal static bool AddToFirstEmptySlot(Chest chest, int type)
    {
        for (int i = 0; i < chest.item.Length; i++)
            if (chest.item[i] is null || chest.item[i].IsAir)
            {
                chest.item[i] = new Item(type);
                return true;
            }
        return false;
    }
}

public sealed class AdvancedMountBossLoot : GlobalNPC
{
    public override void ModifyNPCLoot(NPC npc, NPCLoot npcLoot)
    {
        if (npc.type == NPCID.Plantera)
            npcLoot.Add(ItemDropRule.ByCondition(new Conditions.NotExpert(),
                ModContent.ItemType<WhiteNineTailedFoxSummon>(), 4));
    }

    public override void OnKill(NPC npc)
    {
        if (Main.netMode == NetmodeID.MultiplayerClient || npc.type != NPCID.HallowBoss || Main.expertMode || !NPC.downedGolemBoss)
            return;
        bool enragedAtDeath = AdvancedMountDefaults.IsHallowBossGenuinelyEnraged(npc);
        if (enragedAtDeath || Main.rand.NextBool(4))
            Item.NewItem(npc.GetSource_Loot(), npc.Hitbox, ModContent.ItemType<WhiteUnicornSummon>());
    }
}

public sealed class AdvancedMountBagLoot : GlobalItem
{
    public override bool InstancePerEntity => true;
    private bool _empressEnragedAtKill;
    private bool _empressEligibleAtKill;

    public override void OnSpawn(Item item, IEntitySource source)
    {
        if (item.type == ItemID.FairyQueenBossBag && source is EntitySource_Loot loot
            && loot.Entity is NPC npc && npc.type == NPCID.HallowBoss)
        {
            _empressEnragedAtKill = AdvancedMountDefaults.IsHallowBossGenuinelyEnraged(npc);
            _empressEligibleAtKill = NPC.downedGolemBoss;
        }
    }

    public override GlobalItem Clone(Item from, Item to)
    {
        var clone = (AdvancedMountBagLoot)base.Clone(from, to);
        clone._empressEnragedAtKill = _empressEnragedAtKill;
        clone._empressEligibleAtKill = _empressEligibleAtKill;
        return clone;
    }

    public override void SaveData(Item item, Terraria.ModLoader.IO.TagCompound tag)
    {
        if (item.type == ItemID.FairyQueenBossBag)
        {
            if (_empressEnragedAtKill) tag["MilkcraftEmpressEnragedMountDrop"] = true;
            if (_empressEligibleAtKill) tag["MilkcraftEmpressMountEligible"] = true;
        }
    }

    public override void LoadData(Item item, Terraria.ModLoader.IO.TagCompound tag)
    {
        _empressEnragedAtKill = item.type == ItemID.FairyQueenBossBag && tag.GetBool("MilkcraftEmpressEnragedMountDrop");
        _empressEligibleAtKill = item.type == ItemID.FairyQueenBossBag && tag.GetBool("MilkcraftEmpressMountEligible");
    }

    public override void NetSend(Item item, System.IO.BinaryWriter writer)
    {
        writer.Write(_empressEnragedAtKill);
        writer.Write(_empressEligibleAtKill);
    }

    public override void NetReceive(Item item, System.IO.BinaryReader reader)
    {
        _empressEnragedAtKill = reader.ReadBoolean();
        _empressEligibleAtKill = reader.ReadBoolean();
    }

    public override bool CanStack(Item destination, Item source)
        => destination.type != ItemID.FairyQueenBossBag || (destination.GetGlobalItem<AdvancedMountBagLoot>()._empressEnragedAtKill
            == source.GetGlobalItem<AdvancedMountBagLoot>()._empressEnragedAtKill
            && destination.GetGlobalItem<AdvancedMountBagLoot>()._empressEligibleAtKill
            == source.GetGlobalItem<AdvancedMountBagLoot>()._empressEligibleAtKill);

    public override bool CanStackInWorld(Item destination, Item source) => CanStack(destination, source);

    public override void ModifyItemLoot(Item item, ItemLoot itemLoot)
    {
        if (item.type == ItemID.PlanteraBossBag)
            itemLoot.Add(ItemDropRule.Common(ModContent.ItemType<WhiteNineTailedFoxSummon>(), 4));
    }

    public override bool CanRightClick(Item item)
        => item.type == ItemID.FairyQueenBossBag;

    public override void RightClick(Item item, Player player)
    {
        if (item.type != ItemID.FairyQueenBossBag || !_empressEligibleAtKill) return;
        if (_empressEnragedAtKill || Main.rand.NextBool(4))
            player.QuickSpawnItem(player.GetSource_OpenItem(item.type), ModContent.ItemType<WhiteUnicornSummon>());
    }
}

public sealed class AdvancedMountCrateLoot : GlobalItem
{
    public override void ModifyItemLoot(Item item, ItemLoot itemLoot)
    {
        if (item.type > 0 && item.type < ItemID.Count && ItemID.Sets.IsFishingCrate[item.type])
            itemLoot.Add(ItemDropRule.Common(ModContent.ItemType<WhiteTigerSummon>(), 10));
    }
}
