using System;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Microsoft.Xna.Framework;
using ReLogic.Content;
using Microsoft.Xna.Framework.Graphics;

namespace MilkcraftTerraria.Content;

/// <summary>Mechanical-stage mount: quick ground travel and a short, recoverable flight reserve.</summary>
public sealed class UndeadWarhorseMount : ModMount
{
    public override string Texture => $"{Mod.Name}/Content/Mounts/UndeadWarhorse";
    public const int Frames = 16;

    public override void SetStaticDefaults()
    {
        MountData.buff = ModContent.BuffType<UndeadWarhorseMountBuff>();
        MountData.textureWidth = 112;
        MountData.textureHeight = 1280;
        if (!Main.dedServ)
        {
            Asset<Texture2D> back = ModContent.Request<Texture2D>($"{Mod.Name}/Content/Mounts/UndeadWarhorse_Back", AssetRequestMode.ImmediateLoad);
            MountData.backTexture = back;
            MountData.textureWidth = back.Value.Width;
            MountData.textureHeight = back.Value.Height;
        }
        MountData.heightBoost = 34;
        MountData.fallDamage = 0.75f;
        MountData.runSpeed = 6.5f;
        MountData.dashSpeed = 13f;
        MountData.acceleration = 0.32f;
        MountData.jumpHeight = 12;
        MountData.jumpSpeed = 6.2f;
        MountData.flightTimeMax = 120;
        MountData.totalFrames = Frames;
        MountData.playerYOffsets = new int[Frames];
        Array.Fill(MountData.playerYOffsets, 29);
        // The atlas saddle is (49,33) relative to a centered 112x80 frame.
        // Native Mount.Draw anchors the frame at player center + offsets.
        MountData.xOffset = 7;
        MountData.yOffset = 4;
        MountData.playerHeadOffset = 16;
        MountData.bodyFrame = 3;
        MountData.standingFrameStart = 0;
        MountData.standingFrameCount = 1;
        MountData.standingFrameDelay = 12;
        MountData.runningFrameStart = 1;
        MountData.runningFrameCount = 8;
        // UpdateFrame accumulates abs(velocity.X) each game tick and advances
        // at most one frame per threshold crossing. Scale the delay to the
        // mount's dash velocity so the 8-frame gallop does not race at speed.
        MountData.runningFrameDelay = 70;
        MountData.inAirFrameStart = 9;
        MountData.inAirFrameCount = 1;
        MountData.inAirFrameDelay = 8;
        MountData.flyingFrameStart = 10;
        MountData.flyingFrameCount = 6;
        MountData.flyingFrameDelay = 6;
        MountData.swimFrameStart = 9;
        MountData.swimFrameCount = 1;
        MountData.swimFrameDelay = 8;
        MountData.spawnDust = DustID.Shadowflame;
        MountData.spawnDustNoGravity = true;
    }

}

/// <summary>Post-Golem mount: sustained flight with responsive horizontal acceleration.</summary>
public sealed class CelestialTenguMount : ModMount
{
    public override string Texture => $"{Mod.Name}/Content/Mounts/CelestialTengu";
    public const int Frames = 16;

    public override void SetStaticDefaults()
    {
        MountData.buff = ModContent.BuffType<CelestialTenguMountBuff>();
        MountData.textureWidth = 112;
        MountData.textureHeight = 1280;
        if (!Main.dedServ)
        {
            Asset<Texture2D> back = ModContent.Request<Texture2D>($"{Mod.Name}/Content/Mounts/CelestialTengu_Back", AssetRequestMode.ImmediateLoad);
            MountData.backTexture = back;
            MountData.textureWidth = back.Value.Width;
            MountData.textureHeight = back.Value.Height;
        }
        MountData.heightBoost = 34;
        MountData.fallDamage = 0f;
        MountData.runSpeed = 5.5f;
        MountData.dashSpeed = 9f;
        MountData.acceleration = 0.28f;
        MountData.jumpHeight = 10;
        MountData.jumpSpeed = 5.4f;
        // Native hover normally consumes finite mounts' flight reserve. This
        // mount refreshes the native reserve from UpdateEffects each active
        // tick, retaining Mount.Hover movement and allowing indefinite flight.
        MountData.flightTimeMax = 320;
        MountData.usesHover = true;
        // Mount.Hover always divides fatigue by this value, even for hovering
        // mounts. Keep a valid positive denominator; flight is replenished in
        // UpdateEffects and fatigue remains at zero during normal use.
        MountData.fatigueMax = 1;
        MountData.totalFrames = Frames;
        MountData.playerYOffsets = new int[Frames];
        Array.Fill(MountData.playerYOffsets, 22);
        // The atlas saddle is centered at (56,35); align it with the rider's
        // pelvis, about six pixels below the player's center.
        MountData.xOffset = 0;
        MountData.yOffset = 10;
        MountData.playerHeadOffset = 14;
        MountData.bodyFrame = 3;
        MountData.standingFrameStart = 0;
        MountData.standingFrameCount = 1;
        MountData.standingFrameDelay = 12;
        MountData.runningFrameStart = 1;
        MountData.runningFrameCount = 4;
        // Four ground frames at the full dash speed should cycle about 2.25
        // times per second, while ordinary run speed remains visibly calmer.
        MountData.runningFrameDelay = 60;
        // Mounts using native Hover always animate through state 2 (in-air),
        // so this band must use the winged rows as well as flyingFrameStart.
        MountData.inAirFrameStart = 6;
        MountData.inAirFrameCount = 8;
        MountData.inAirFrameDelay = 5;
        MountData.flyingFrameStart = 6;
        MountData.flyingFrameCount = 8;
        MountData.flyingFrameDelay = 5;
        MountData.swimFrameStart = 14;
        MountData.swimFrameCount = 2;
        MountData.swimFrameDelay = 8;
        MountData.spawnDust = DustID.Ice;
        MountData.spawnDustNoGravity = true;
    }

    public override void UpdateEffects(Player player)
    {
        if (player.mount.Active && player.mount.Type == Type)
            player.mount.ResetFlightTime(0f);
    }
}

public sealed class UndeadWarhorseMountBuff : ModBuff
{
    public override string Texture => $"{Mod.Name}/Content/Items/UndeadWarhorseSummon";

    public override void SetStaticDefaults()
    {
        Main.buffNoTimeDisplay[Type] = true;
        Main.buffNoSave[Type] = true;
        BuffID.Sets.BasicMountData[Type] = new BuffID.Sets.BuffMountData {
            mountID = ModContent.MountType<UndeadWarhorseMount>(),
            faceLeft = false
        };
    }
}

public sealed class CelestialTenguMountBuff : ModBuff
{
    public override string Texture => $"{Mod.Name}/Content/Items/CelestialTenguSummon";

    public override void SetStaticDefaults()
    {
        Main.buffNoTimeDisplay[Type] = true;
        Main.buffNoSave[Type] = true;
        BuffID.Sets.BasicMountData[Type] = new BuffID.Sets.BuffMountData {
            mountID = ModContent.MountType<CelestialTenguMount>(),
            faceLeft = false
        };
    }
}
