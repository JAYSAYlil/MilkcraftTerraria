using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace MilkcraftTerraria.Content;

/// <summary>Small old-build reproduction for platform fire trails and native NPC player scaling.</summary>
internal static class PlatformMultiplayerDiagnostics
{
    internal static string Run()
    {
        var report = new StringBuilder("diagnostic=0.3.3-platform-multiplayer; scope=fire-support-and-native-boss-scaling\n");
        report.AppendLine(TestFlameTrailSupports());
        report.AppendLine(TestNativeFlamePatchLifecycle());
        report.AppendLine(TestNativePlayerScaling());
        report.AppendLine(TestJourneyPlayerScaling());
        return report.ToString();
    }

    private static string TestFlameTrailSupports()
    {
        const int cx = 120, floorY = 150, halfWidth = 10;
        Player[] oldPlayers = Main.player;
        Projectile[] oldProjectiles = Main.projectile;
        int oldMyPlayer = Main.myPlayer, oldNetMode = Main.netMode;
        bool oldMenu = Main.gameMenu, oldPaused = Main.gamePaused;
        var savedTiles = new List<(Point Point, Tile Tile)>();
        try
        {
            if (!WorldGen.InWorld(cx - halfWidth, floorY - 8, 10) || !WorldGen.InWorld(cx + halfWidth, floorY + 8, 10))
                throw new InvalidOperationException("fire repro region is outside the loaded world");
            for (int x = cx - halfWidth; x <= cx + halfWidth; x++)
            for (int y = floorY - 8; y <= floorY + 8; y++)
            {
                Tile snapshot = new(); snapshot.CopyFrom(Main.tile[x, y]);
                savedTiles.Add((new Point(x, y), snapshot));
                Main.tile[x, y].ClearEverything();
            }

            Main.gameMenu = false;
            Main.gamePaused = false;
            Main.myPlayer = Math.Clamp(Main.myPlayer, 0, Main.maxPlayers - 1);
            Main.netMode = NetmodeID.SinglePlayer;
            Main.player = (Player[])oldPlayers.Clone();
            Main.projectile = Enumerable.Range(0, Math.Max(oldProjectiles.Length, Main.maxProjectiles + 1))
                .Select(_ => new Projectile()).ToArray();
            MethodInfo setup = typeof(PlayerLoader).GetMethod("SetupPlayer", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("PlayerLoader.SetupPlayer missing");

            int patchType = ModContent.ProjectileType<FlameWarhorseTrailFire>();
            var rows = new List<string>();
            void TestSupport(string name, Action place, float surfaceOffset)
            {
                for (int x = cx - halfWidth; x <= cx + halfWidth; x++)
                {
                    Main.tile[x, floorY].ClearEverything();
                    Main.tile[x, floorY - 1].ClearEverything();
                }
                foreach (Projectile p in Main.projectile) p.active = false;
                place();
                if (name is "solid" or "platform" or "half-block" or "slope" or "actuated")
                {
                    Tile exemplar = Main.tile[cx, floorY];
                    for (int x = cx - halfWidth; x <= cx + halfWidth; x++)
                    {
                        Tile support = Main.tile[x, floorY];
                        support.CopyFrom(exemplar);
                    }
                }
                Player actor = oldPlayers[Main.myPlayer].SerializedClone();
                setup.Invoke(null, new object[] { actor });
                actor.active = true; actor.dead = false; actor.whoAmI = Main.myPlayer;
                actor.statLife = actor.statLifeMax = 500;
                actor.position = new Vector2((cx - 2) * 16f, floorY * 16f + surfaceOffset - actor.height);
                actor.velocity = Vector2.Zero;
                actor.mount.SetMount(ModContent.MountType<FlameWarhorseMount>(), actor, true);
                Main.player[actor.whoAmI] = actor;
                AdvancedMountAbilityPlayer ability = actor.GetModPlayer<AdvancedMountAbilityPlayer>();
                int before = Main.projectile.Count(p => p.active && p.type == patchType);
                bool solidProbe = Collision.SolidCollision(new Vector2(actor.Center.X - 2f, actor.Bottom.Y), 4, 3);
                for (int tick = 0; tick < 12; tick++)
                {
                    Vector2 requested = new(4f, 0f);
                    Vector2 resolved = Collision.TileCollision(actor.position, requested, actor.width, actor.height,
                        fallThrough: false, fall2: false, gravDir: (int)actor.gravDir);
                    actor.position += resolved;
                    actor.velocity = resolved;
                    ability.PostUpdate();
                }
                int after = Main.projectile.Count(p => p.active && p.type == patchType);
                rows.Add($"support={name},bottom={actor.Bottom.Y:0.0},solidCollision={solidProbe},resolvedDx={actor.position.X - (cx - 2) * 16f:0.0},patches={before}->{after}");
                actor.mount.Dismount(actor);
            }
            void TestSlopeWalk(string name, SlopeType slopeType, int direction)
            {
                const int rampTiles = 4;
                for (int x = cx - halfWidth; x <= cx + halfWidth; x++)
                for (int y = floorY - 2; y <= floorY + 7; y++) Main.tile[x, y].ClearEverything();

                // Build a continuous staircase slope from real sloped tiles:
                // 48px high plateau, four joined hammered slope tiles each
                // one row lower, then 48px low plateau. Fill the terrain under
                // every surface tile so the actor cannot fall through gaps.
                int slopeLeft = slopeType == SlopeType.SlopeDownRight ? cx : cx - rampTiles;
                int slopeRight = slopeLeft + rampTiles - 1;
                for (int x = cx - halfWidth; x <= cx + halfWidth; x++)
                {
                    int surfaceY;
                    bool isRamp = x >= slopeLeft && x <= slopeRight;
                    if (isRamp)
                    {
                        int index = slopeType == SlopeType.SlopeDownRight ? x - slopeLeft : slopeRight - x;
                        surfaceY = floorY + index;
                    }
                    else if (slopeType == SlopeType.SlopeDownRight)
                        surfaceY = x < slopeLeft ? floorY : floorY + rampTiles;
                    else
                        surfaceY = x > slopeRight ? floorY : floorY + rampTiles;
                    Tile surface = Main.tile[x, surfaceY];
                    surface.HasTile = true; surface.TileType = TileID.Stone;
                    if (isRamp) surface.Slope = slopeType;
                    for (int y = surfaceY + 1; y <= floorY + rampTiles + 2; y++)
                    {
                        Tile fill = Main.tile[x, y]; fill.HasTile = true; fill.TileType = TileID.Stone;
                    }
                }
                foreach (Projectile p in Main.projectile) p.active = false;
                Player actor = oldPlayers[Main.myPlayer].SerializedClone();
                setup.Invoke(null, new object[] { actor });
                actor.active = true; actor.dead = false; actor.whoAmI = Main.myPlayer; actor.isDisplayDollOrInanimate = false;
                actor.statLife = actor.statLifeMax = 500;
                float startX = direction > 0 ? (cx - 7) * 16f : (cx + 7) * 16f;
                int startSurfaceY = direction > 0
                    ? (slopeType == SlopeType.SlopeDownRight ? floorY : floorY + rampTiles)
                    : (slopeType == SlopeType.SlopeDownRight ? floorY + rampTiles : floorY);
                actor.mount.SetMount(ModContent.MountType<FlameWarhorseMount>(), actor, true);
                actor.position = new Vector2(startX, startSurfaceY * 16f - actor.height);
                actor.velocity = Vector2.Zero;
                Main.player[actor.whoAmI] = actor;
                AdvancedMountAbilityPlayer ability = actor.GetModPlayer<AdvancedMountAbilityPlayer>();
                int before = Main.projectile.Count(p => p.active && p.type == patchType);
                int slopePatchTicks = 0;
                for (int tick = 0; tick < 60; tick++)
                {
                    actor.velocity.X = direction * 4f;
                    actor.velocity.Y = Math.Min(actor.velocity.Y + 0.3f, 10f);
                    float stepSpeed = 0f;
                    float gfxOffY = 0f;
                    Collision.StepDown(ref actor.position, ref actor.velocity, actor.width, actor.height,
                        ref stepSpeed, ref gfxOffY, (int)actor.gravDir, false);
                    Collision.StepUp(ref actor.position, ref actor.velocity, actor.width, actor.height,
                        ref stepSpeed, ref gfxOffY, (int)actor.gravDir, false);
                    actor.DryCollision(false, false);
                    actor.SlopingCollision(false, false);
                    ability.PostUpdate();
                    float centerTileX = actor.Center.X / 16f;
                    if (centerTileX >= slopeLeft - 1 && centerTileX <= slopeRight + 2
                        && Main.projectile.Any(p => p.active && p.type == patchType
                            && p.Center.X >= (slopeLeft - 1) * 16f && p.Center.X <= (slopeRight + 2) * 16f))
                        slopePatchTicks++;
                }
                int after = Main.projectile.Count(p => p.active && p.type == patchType);
                float dx = actor.position.X - startX;
                float dy = actor.position.Y - (startSurfaceY * 16f - actor.height);
                rows.Add(FormattableString.Invariant($"slope-walk={name},dx={dx:0.0},dy={dy:0.0},vy={actor.velocity.Y:0.00},patches={before}->{after},rampPatchObserved={slopePatchTicks}"));
                if (Math.Abs(dx) < 80f || Math.Abs(dy) < 48f || after <= before || slopePatchTicks == 0)
                    throw new InvalidOperationException($"Slope fixture did not traverse a real ramp with fire patches: {name}; {string.Join(" | ", rows)}");
                actor.mount.Dismount(actor);
            }

            TestSupport("solid", () => { Tile t = Main.tile[cx, floorY]; t.HasTile = true; t.TileType = TileID.Stone; }, 0f);
            TestSupport("platform", () => { Tile t = Main.tile[cx, floorY]; t.HasTile = true; t.TileType = TileID.Platforms; }, 0f);
            TestSupport("half-block", () => { Tile t = Main.tile[cx, floorY]; t.HasTile = true; t.TileType = TileID.Stone; t.IsHalfBlock = true; }, 8f);
            TestSupport("actuated", () => { Tile t = Main.tile[cx, floorY]; t.HasTile = true; t.TileType = TileID.Stone; t.IsActuated = true; }, 0f);
            TestSupport("unsupported", () => { }, 0f);
            TestSupport("waterlogged-solid", () =>
            {
                for (int x = cx - halfWidth; x <= cx + halfWidth; x++)
                {
                    Tile support = Main.tile[x, floorY]; support.HasTile = true; support.TileType = TileID.Stone;
                }
                Tile water = Main.tile[cx, floorY - 1]; water.LiquidType = LiquidID.Water; water.LiquidAmount = byte.MaxValue;
            }, 0f);
            foreach (string expected in new[] { "solid", "platform", "half-block" })
                if (ParsePatchCount(rows.First(r => r.StartsWith($"support={expected},"))) < 1)
                    throw new InvalidOperationException($"Supported ground did not leave a fire trail: {string.Join(" | ", rows)}");
            foreach (string expected in new[] { "unsupported", "waterlogged-solid", "actuated" })
                if (ParsePatchCount(rows.First(r => r.StartsWith($"support={expected},"))) != 0)
                    throw new InvalidOperationException($"Unsupported/liquid case left a fire trail: {string.Join(" | ", rows)}");

            void TestAirborneOrDropThrough(string name, bool jump, bool dropThrough, bool pressDown = false, bool expectFire = false)
            {
                foreach (int x in Enumerable.Range(cx - halfWidth, halfWidth * 2 + 1))
                {
                    Tile support = Main.tile[x, floorY];
                    support.ClearEverything();
                    support.HasTile = true;
                    support.TileType = dropThrough ? TileID.Platforms : TileID.Stone;
                    Main.tile[x, floorY - 1].ClearEverything();
                }
                foreach (Projectile p in Main.projectile) p.active = false;
                Player actor = oldPlayers[Main.myPlayer].SerializedClone();
                setup.Invoke(null, new object[] { actor });
                actor.active = true; actor.dead = false; actor.whoAmI = Main.myPlayer;
                actor.statLife = actor.statLifeMax = 500;
                actor.mount.SetMount(ModContent.MountType<FlameWarhorseMount>(), actor, true);
                actor.position = new Vector2((cx - 2) * 16f, floorY * 16f - actor.height);
                actor.controlDown = dropThrough || pressDown;
                actor.justJumped = jump;
                actor.velocity = jump ? new Vector2(4f, -8f) : dropThrough ? new Vector2(4f, 2f) : pressDown ? new Vector2(4f, 0f) : Vector2.Zero;
                Main.player[actor.whoAmI] = actor;
                AdvancedMountAbilityPlayer ability = actor.GetModPlayer<AdvancedMountAbilityPlayer>();
                int before = Main.projectile.Count(p => p.active && p.type == patchType);
                for (int tick = 0; tick < 12; tick++)
                {
                    if (jump || dropThrough)
                    {
                        actor.velocity.Y = Math.Min(actor.velocity.Y + 0.3f, 10f);
                        Vector2 requested = actor.velocity;
                        Vector2 resolved = Collision.TileCollision(actor.position, requested, actor.width, actor.height,
                            fallThrough: dropThrough, fall2: dropThrough, gravDir: (int)actor.gravDir);
                        actor.position += resolved;
                        actor.velocity = resolved;
                        actor.SlopingCollision(false, false);
                    }
                    ability.PostUpdate();
                    if (jump) actor.justJumped = false;
                }
                int after = Main.projectile.Count(p => p.active && p.type == patchType);
                rows.Add($"non-ground-motion={name},bottom={actor.Bottom.Y:0.0},vy={actor.velocity.Y:0.0},patches={before}->{after}");
                if ((after > before) != expectFire)
                    throw new InvalidOperationException($"Flame trail expectation failed during {name}: expected={expectFire}, patches={before}->{after}.");
                actor.mount.Dismount(actor);
            }
            TestAirborneOrDropThrough("jump-takeoff", jump: true, dropThrough: false);
            TestAirborneOrDropThrough("platform-drop-through", jump: false, dropThrough: true);
            TestAirborneOrDropThrough("down-on-solid", jump: false, dropThrough: false, pressDown: true, expectFire: true);

            // Standing on supported ground without actual movement must not
            // leave a decorative fire patch.
            for (int x = cx - halfWidth; x <= cx + halfWidth; x++)
            {
                Tile support = Main.tile[x, floorY]; support.ClearEverything(); support.HasTile = true; support.TileType = TileID.Stone;
                Main.tile[x, floorY - 1].ClearEverything();
            }
            foreach (Projectile p in Main.projectile) p.active = false;
            Player stationary = oldPlayers[Main.myPlayer].SerializedClone();
            setup.Invoke(null, new object[] { stationary });
            stationary.active = true; stationary.dead = false; stationary.whoAmI = Main.myPlayer; stationary.isDisplayDollOrInanimate = false;
            stationary.statLife = stationary.statLifeMax = 500;
            stationary.mount.SetMount(ModContent.MountType<FlameWarhorseMount>(), stationary, true);
            stationary.position = new Vector2((cx - 2) * 16f, floorY * 16f - stationary.height);
            stationary.velocity = Vector2.Zero;
            Main.player[stationary.whoAmI] = stationary;
            AdvancedMountAbilityPlayer stationaryAbility = stationary.GetModPlayer<AdvancedMountAbilityPlayer>();
            for (int tick = 0; tick < 12; tick++) stationaryAbility.PostUpdate();
            int stationaryPatches = Main.projectile.Count(p => p.active && p.type == patchType);
            rows.Add($"stationary-ground patches={stationaryPatches}");
            if (stationaryPatches != 0) throw new InvalidOperationException("Stationary mounted player left a fire trail.");
            stationary.mount.Dismount(stationary);

            foreach ((string Name, int LiquidType) liquidCase in new[]
            {
                ("water", LiquidID.Water), ("lava", LiquidID.Lava),
                ("honey", LiquidID.Honey), ("shimmer", LiquidID.Shimmer)
            })
            {
                TestSupport($"{liquidCase.Name}-logged-solid", () =>
                {
                    for (int x = cx - halfWidth; x <= cx + halfWidth; x++)
                    {
                        Tile support = Main.tile[x, floorY]; support.HasTile = true; support.TileType = TileID.Stone;
                        Tile fluid = Main.tile[x, floorY - 1]; fluid.LiquidType = (byte)liquidCase.LiquidType; fluid.LiquidAmount = byte.MaxValue;
                    }
                }, 0f);
            }
            TestSlopeWalk("down-right", SlopeType.SlopeDownRight, 1);
            TestSlopeWalk("down-left", SlopeType.SlopeDownLeft, -1);
            TestSlopeWalk("up-right", SlopeType.SlopeDownRight, -1);
            TestSlopeWalk("up-left", SlopeType.SlopeDownLeft, 1);

            // Check each native liquid against both trail creation and an existing
            // fire patch with an overlapping hostile NPC. The patch must extinguish
            // before its native hit scan can damage that NPC.
            NPC[] oldNpcs = Main.npc;
            Main.npc = Enumerable.Range(0, Math.Max(oldNpcs.Length, Main.maxNPCs + 1)).Select(_ => new NPC()).ToArray();
            try
            {
                foreach ((string Name, int LiquidType) liquidCase in new[]
                {
                    ("water", LiquidID.Water), ("lava", LiquidID.Lava),
                    ("honey", LiquidID.Honey), ("shimmer", LiquidID.Shimmer)
                })
                {
                    foreach (int x in Enumerable.Range(cx - halfWidth, halfWidth * 2 + 1))
                    {
                        Tile support = Main.tile[x, floorY]; support.ClearEverything(); support.HasTile = true; support.TileType = TileID.Stone;
                        Tile fluid = Main.tile[x, floorY - 1]; fluid.LiquidType = (byte)liquidCase.LiquidType; fluid.LiquidAmount = byte.MaxValue;
                    }
                    foreach (Projectile p in Main.projectile) p.active = false;
                    NPC target = new(); target.SetDefaults(NPCID.BlueSlime); target.whoAmI = 0; target.active = true;
                    target.life = target.lifeMax = 1000; target.Center = new Vector2(cx * 16f, floorY * 16f - 1f); Main.npc[0] = target;
                    int lifeBefore = target.life;
                    Projectile liquidPatch = Projectile.NewProjectileDirect(Entity.GetSource_None(), target.Center,
                        Vector2.Zero, patchType, 30, 0f, Main.myPlayer);
                    liquidPatch.timeLeft = 180;
                    int liquidLife = liquidPatch.timeLeft;
                    liquidPatch.Update(liquidPatch.whoAmI);
                    rows.Add($"existing-patch-liquid={liquidCase.Name},active:{liquidPatch.active},timeLeft:{liquidLife}->{liquidPatch.timeLeft},targetLife:{lifeBefore}->{target.life}");
                    if (liquidPatch.active || target.life != lifeBefore)
                        throw new InvalidOperationException($"Existing fire patch survived or hit before extinguishing in {liquidCase.Name}.");
                }
            }
            finally { Main.npc = oldNpcs; }
            Tile shallowLiquid = Main.tile[cx, floorY - 1];
            shallowLiquid.LiquidType = LiquidID.Water;
            shallowLiquid.LiquidAmount = 16;
            Rectangle aboveShallowWater = new(cx * 16, (floorY - 1) * 16, 16, 10);
            Rectangle touchingShallowWater = new(cx * 16, floorY * 16 - 1, 16, 2);
            bool shallowAbove = AdvancedMountAbilityPlayer.HasLiquidAt(aboveShallowWater);
            bool shallowTouching = AdvancedMountAbilityPlayer.HasLiquidAt(touchingShallowWater);
            rows.Add($"liquid-surface=2px,above={shallowAbove},intersecting={shallowTouching}");
            if (shallowAbove || !shallowTouching)
                throw new InvalidOperationException("Liquid detection did not follow the actual shallow liquid surface.");
            return "flame-trail-native-collision-repro:\n" + string.Join("\n", rows);
        }
        finally
        {
            foreach ((Point point, Tile tile) in savedTiles) Main.tile[point.X, point.Y].CopyFrom(tile);
            Main.player = oldPlayers;
            Main.projectile = oldProjectiles;
            Main.myPlayer = oldMyPlayer;
            Main.netMode = oldNetMode;
            Main.gameMenu = oldMenu;
            Main.gamePaused = oldPaused;
        }
    }

    private static string TestNativeFlamePatchLifecycle()
    {
        int playerIndex = Math.Clamp(Main.myPlayer, 0, Main.maxPlayers - 1);
        MethodInfo nativeFixture = typeof(AdvancedMountExpansionDiagnostics).GetMethod("TestNewMountGroundAbilities", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Existing native flame patch damage/cooldown fixture missing.");
        return Convert.ToString(nativeFixture.Invoke(null, new object[] { playerIndex }))
            ?? throw new InvalidOperationException("Native flame patch fixture returned no report.");
    }

    private static string TestNativePlayerScaling()
    {
        Player[] oldPlayers = Main.player;
        int oldMode = Main.GameMode;
        bool oldMenu = Main.gameMenu;
        int oldNetMode = Main.netMode;
        try
        {
            Main.gameMenu = false;
            Main.netMode = NetmodeID.Server;
            var rows = new List<string>();
            var modLives = new Dictionary<(int NpcType, int Mode, int Players), int>();
            var modDamages = new Dictionary<(int NpcType, int Mode, int Players), int>();
            var vanillaLives = new Dictionary<(int Mode, int Players), int>();
            int[] bossTypes =
            {
                ModContent.NPCType<WraithWarriorBoss>(), ModContent.NPCType<JungleLordBoss>(),
                ModContent.NPCType<BansheeBoss>(), ModContent.NPCType<ObsidianGuardianBoss>(),
                ModContent.NPCType<SphinxBoss>(), ModContent.NPCType<FrostWitchBoss>(),
                ModContent.NPCType<CastleCoreBoss>()
            };
            foreach (int mode in new[] { GameModeID.Normal, GameModeID.Expert, GameModeID.Master })
            foreach (int activeCount in new[] { 1, 2, 4 })
            {
                Main.GameMode = mode;
                Main.player = Enumerable.Range(0, Math.Max(oldPlayers.Length, Main.maxPlayers))
                    .Select(_ => new Player()).ToArray();
                for (int i = 0; i < activeCount; i++)
                {
                    Main.player[i].whoAmI = i;
                    Main.player[i].active = true;
                    Main.player[i].dead = false;
                }
                NPC vanilla = new();
                vanilla.SetDefaults(NPCID.EyeofCthulhu);
                vanillaLives[(mode, activeCount)] = vanilla.lifeMax;
                foreach (int type in bossTypes)
                {
                    NPC npc = new();
                    npc.SetDefaults(type);
                    if (npc.life != npc.lifeMax)
                        throw new InvalidOperationException($"Boss did not start at full life after native scaling: type={type},mode={mode},players={activeCount},life={npc.life}/{npc.lifeMax}.");
                    rows.Add($"npc-setdefaults type={npc.ModNPC?.Name ?? type.ToString()} mode={mode} activePlayers={activeCount} life={npc.life}/{npc.lifeMax} damage={npc.damage} vanillaEocLife={vanilla.lifeMax} vanillaEocDamage={vanilla.damage}");
                    modLives[(type, mode, activeCount)] = npc.lifeMax;
                    modDamages[(type, mode, activeCount)] = npc.damage;
                }
            }
            foreach (int mode in new[] { GameModeID.Normal, GameModeID.Expert, GameModeID.Master })
            {
                foreach (int type in bossTypes)
                {
                    foreach (int activeCount in new[] { 2, 4 })
                    {
                        float vanillaRatio = vanillaLives[(mode, activeCount)] / (float)vanillaLives[(mode, 1)];
                        float modRatio = modLives[(type, mode, activeCount)] / (float)modLives[(type, mode, 1)];
                        if (Math.Abs(vanillaRatio - modRatio) > 0.04f)
                            throw new InvalidOperationException($"Boss life multiplayer ratio differs from native EoC balance type={type},mode={mode},players={activeCount}: mod={modRatio:0.000},vanilla={vanillaRatio:0.000}.");
                    }
                    int singlePlayerDamage = modDamages[(type, mode, 1)];
                    if (modDamages[(type, mode, 2)] != singlePlayerDamage || modDamages[(type, mode, 4)] != singlePlayerDamage)
                        throw new InvalidOperationException($"Boss damage was multiplied by player count type={type},mode={mode}: 1/2/4={singlePlayerDamage}/{modDamages[(type, mode, 2)]}/{modDamages[(type, mode, 4)]}.");
                }
            }
            return "native-npc-setdefaults-scaling:\n" + string.Join("\n", rows);
        }
        finally
        {
            Main.player = oldPlayers;
            Main.netMode = oldNetMode;
            Main.GameMode = oldMode;
            Main.gameMenu = oldMenu;
        }
    }

    private static string TestJourneyPlayerScaling()
    {
        Player[] oldPlayers = Main.player;
        int oldMode = Main.GameMode, oldNetMode = Main.netMode;
        bool oldMenu = Main.gameMenu;
        Terraria.IO.WorldFileData oldWorld = Main.ActiveWorldFileData;
        FieldInfo currentModeField = typeof(Main).GetField("_currentGameModeInfo", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Main._currentGameModeInfo missing for Journey scale probe");
        FieldInfo expertOverrideField = typeof(Main).GetField("_overrideForExpertMode", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Main expert override missing for Journey scale probe");
        FieldInfo masterOverrideField = typeof(Main).GetField("_overrideForMasterMode", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Main master override missing for Journey scale probe");
        object oldModeInfo = currentModeField.GetValue(null)!;
        object oldExpertOverride = expertOverrideField.GetValue(null);
        object oldMasterOverride = masterOverrideField.GetValue(null);
        Type nativeProbeType = typeof(ProgressionBossNativeDiagnostics);
        MethodInfo setSlider = nativeProbeType.GetMethod("SetJourneySliderValue", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("native Journey slider setter missing");
        MethodInfo getSlider = nativeProbeType.GetMethod("GetJourneySliderValue", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("native Journey slider getter missing");
        MethodInfo refresh = typeof(Main).GetMethod("UpdateCreativeGameModeOverride", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("native Journey override update missing");
        MethodInfo countPlayers = typeof(NPC).GetMethod("GetActivePlayerCount", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("NPC.GetActivePlayerCount missing");
        FieldInfo scaledForPlayers = typeof(NPC).GetField("statsAreScaledForThisManyPlayers", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("NPC.statsAreScaledForThisManyPlayers missing");
        float oldSlider = Convert.ToSingle(getSlider.Invoke(null, null));
        int[] bossTypes =
        {
            ModContent.NPCType<WraithWarriorBoss>(), ModContent.NPCType<JungleLordBoss>(),
            ModContent.NPCType<BansheeBoss>(), ModContent.NPCType<ObsidianGuardianBoss>(),
            ModContent.NPCType<SphinxBoss>(), ModContent.NPCType<FrostWitchBoss>(),
            ModContent.NPCType<CastleCoreBoss>()
        };
        var cases = new (float Slider, string Name, float Strength, bool Expert, bool Master)[]
        {
            (0.33f, "Journey-normal", 1f, false, false),
            (0.66f, "Journey-expert", 2f, true, false),
            (1f, "Journey-master", 3f, true, true)
        };
        try
        {
            Main.gameMenu = false;
            // Slider changes commit through the native single-player loopback
            // packet path; Server mode would leave only the pending UI value.
            Main.netMode = NetmodeID.SinglePlayer;
            Main.ActiveWorldFileData = new Terraria.IO.WorldFileData();
            var rows = new List<string>();
            foreach (var modeCase in cases)
            {
                Main.netMode = NetmodeID.SinglePlayer;
                Main.GameMode = GameModeID.Creative;
                PropertyInfo worldMode = Main.ActiveWorldFileData.GetType().GetProperty("GameMode", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (worldMode != null && worldMode.CanWrite)
                    worldMode.SetValue(Main.ActiveWorldFileData, Convert.ChangeType(GameModeID.Creative, worldMode.PropertyType));
                else
                {
                    FieldInfo worldModeField = Main.ActiveWorldFileData.GetType().GetField("_gameMode", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (worldModeField != null)
                        worldModeField.SetValue(Main.ActiveWorldFileData, Convert.ChangeType(GameModeID.Creative, worldModeField.FieldType));
                }
                setSlider.Invoke(null, new object[] { modeCase.Slider }); // native SetValueKeyboardForced + loopback commit
                refresh.Invoke(null, null);
                if (Main.expertMode != modeCase.Expert || Main.masterMode != modeCase.Master)
                    throw new InvalidOperationException($"Journey slider {modeCase.Name} did not set native difficulty flags: {Main.expertMode}/{Main.masterMode}.");
                var modLives = new Dictionary<(int Type, int Count), int>();
                var modDamage = new Dictionary<(int Type, int Count), int>();
                var vanillaLives = new Dictionary<int, int>();
                foreach (int activeCount in new[] { 1, 2, 4 })
                {
                    Main.netMode = NetmodeID.Server;
                    Main.player = Enumerable.Range(0, Math.Max(oldPlayers.Length, Main.maxPlayers)).Select(_ => new Player()).ToArray();
                    for (int i = 0; i < activeCount; i++)
                    {
                        Main.player[i].whoAmI = i;
                        Main.player[i].active = true;
                        Main.player[i].dead = false;
                        Main.player[i].difficulty = 3; // Creative character for a Journey world
                    }
                    int nativeCount = Convert.ToInt32(countPlayers.Invoke(null, null));
                    if (nativeCount != activeCount)
                        throw new InvalidOperationException($"Journey fixture did not expose requested active player count: expected={activeCount}, native={nativeCount}.");
                    NPC vanilla = new(); vanilla.SetDefaults(NPCID.EyeofCthulhu);
                    if ((modeCase.Expert || modeCase.Master) && Convert.ToInt32(scaledForPlayers.GetValue(vanilla)) != activeCount)
                        throw new InvalidOperationException($"Journey EoC did not record native player scaling: requested={activeCount}, scaledFor={scaledForPlayers.GetValue(vanilla)}.");
                    vanillaLives[activeCount] = vanilla.lifeMax;
                    foreach (int type in bossTypes)
                    {
                        NPC boss = new(); boss.SetDefaults(type);
                        if (boss.life != boss.lifeMax)
                            throw new InvalidOperationException($"Journey scaled Boss did not start at full life: {modeCase.Name},type={type},players={activeCount},life={boss.life}/{boss.lifeMax}.");
                        if ((modeCase.Expert || modeCase.Master) && Convert.ToInt32(scaledForPlayers.GetValue(boss)) != activeCount)
                            throw new InvalidOperationException($"Journey Boss did not record native player scaling: {modeCase.Name},type={type},requested={activeCount},scaledFor={scaledForPlayers.GetValue(boss)}.");
                        modLives[(type, activeCount)] = boss.lifeMax;
                        modDamage[(type, activeCount)] = boss.damage;
                        rows.Add($"Journey-slider={modeCase.Slider:0.##} strength={modeCase.Strength} flags={Main.expertMode}/{Main.masterMode} nativePlayers={nativeCount} type={boss.ModNPC?.Name ?? type.ToString()} players={activeCount} scaledFor={scaledForPlayers.GetValue(boss)} life={boss.life}/{boss.lifeMax} damage={boss.damage} EoC={vanilla.lifeMax}");
                    }
                }
                foreach (int type in bossTypes)
                {
                    foreach (int count in new[] { 2, 4 })
                    {
                        float nativeRatio = vanillaLives[count] / (float)vanillaLives[1];
                        float modRatio = modLives[(type, count)] / (float)modLives[(type, 1)];
                        if (Math.Abs(nativeRatio - modRatio) > 0.04f)
                            throw new InvalidOperationException($"Journey life balance differed from native EoC type={type},slider={modeCase.Name},players={count}: mod={modRatio:0.000},vanilla={nativeRatio:0.000}.");
                        if ((modeCase.Expert || modeCase.Master) && modLives[(type, count)] <= modLives[(type, 1)])
                            throw new InvalidOperationException($"Journey Boss did not increase life with active players: {modeCase.Name},type={type},1={modLives[(type, 1)]},players{count}={modLives[(type, count)]}.");
                    }
                    int baseDamage = modDamage[(type, 1)];
                    if (modDamage[(type, 2)] != baseDamage || modDamage[(type, 4)] != baseDamage)
                        throw new InvalidOperationException($"Journey Boss damage improperly changed with player count type={type},slider={modeCase.Name}: 1/2/4={baseDamage}/{modDamage[(type, 2)]}/{modDamage[(type, 4)]}.");
                }
            }
            return "native-journey-boss-scaling=PASS real slider commits .33/.66/1; 7 Bosses × 1/2/4; full-life initialization; life ratios match EoC balance; damage unchanged with player count\n" + string.Join("\n", rows);
        }
        finally
        {
            Main.GameMode = GameModeID.Creative;
            setSlider.Invoke(null, new object[] { oldSlider });
            refresh.Invoke(null, null);
            Main.player = oldPlayers;
            Main.netMode = oldNetMode;
            Main.ActiveWorldFileData = oldWorld;
            currentModeField.SetValue(null, oldModeInfo);
            expertOverrideField.SetValue(null, oldExpertOverride);
            masterOverrideField.SetValue(null, oldMasterOverride);
            Main.GameMode = oldMode;
            Main.gameMenu = oldMenu;
        }
    }

    private static int ParsePatchCount(string row)
    {
        int marker = row.LastIndexOf("patches=", StringComparison.Ordinal);
        if (marker < 0) return -1;
        string value = row[(marker + 8)..].Split('>').Last();
        return int.TryParse(value, out int count) ? count : -1;
    }
}
