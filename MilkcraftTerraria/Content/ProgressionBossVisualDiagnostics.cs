using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.DataStructures;
using Terraria.UI.Chat;
using Terraria.Utilities;
using Terraria.Graphics.Light;
using ReLogic.Content;
using MilkcraftTerraria.Content.Items;
using MilkcraftTerraria.Content.Tiles;

namespace MilkcraftTerraria.Content;

/// <summary>Isolated, native SpriteBatch rendering of the seven boss atlases and their collectibles.</summary>
internal static class ProgressionBossVisualDiagnostics
{
    private sealed class BrightFixtureLightingEngine : ILightingEngine
    {
        private static readonly Vector3 FixtureLight = new(0.86f, 0.79f, 0.65f);
        public void Rebuild() { }
        public void AddLight(int x, int y, Vector3 color) { }
        public void ProcessArea(Rectangle area) { }
        public Vector3 GetColor(int x, int y) => FixtureLight;
        public void Clear() { }
    }

    private static readonly (string Name, int Npc, int Mask, int Trophy, int Relic, int TrophyTile, int RelicTile)[] Entries =
    {
        ("wraith-warrior", ModContent.NPCType<WraithWarriorBoss>(), ModContent.ItemType<WraithWarriorMask>(), ModContent.ItemType<WraithWarriorTrophy>(), ModContent.ItemType<WraithWarriorRelic>(), ModContent.TileType<WraithWarriorTrophyTile>(), ModContent.TileType<WraithWarriorRelicTile>()),
        ("jungle-lord", ModContent.NPCType<JungleLordBoss>(), ModContent.ItemType<JungleLordMask>(), ModContent.ItemType<JungleLordTrophy>(), ModContent.ItemType<JungleLordRelic>(), ModContent.TileType<JungleLordTrophyTile>(), ModContent.TileType<JungleLordRelicTile>()),
        ("banshee", ModContent.NPCType<BansheeBoss>(), ModContent.ItemType<BansheeMask>(), ModContent.ItemType<BansheeTrophy>(), ModContent.ItemType<BansheeRelic>(), ModContent.TileType<BansheeTrophyTile>(), ModContent.TileType<BansheeRelicTile>()),
        ("obsidian-guardian", ModContent.NPCType<ObsidianGuardianBoss>(), ModContent.ItemType<ObsidianGuardianMask>(), ModContent.ItemType<ObsidianGuardianTrophy>(), ModContent.ItemType<ObsidianGuardianRelic>(), ModContent.TileType<ObsidianGuardianTrophyTile>(), ModContent.TileType<ObsidianGuardianRelicTile>()),
        ("sphinx", ModContent.NPCType<SphinxBoss>(), ModContent.ItemType<SphinxMask>(), ModContent.ItemType<SphinxTrophy>(), ModContent.ItemType<SphinxRelic>(), ModContent.TileType<SphinxTrophyTile>(), ModContent.TileType<SphinxRelicTile>()),
        ("frost-witch", ModContent.NPCType<FrostWitchBoss>(), ModContent.ItemType<FrostWitchMask>(), ModContent.ItemType<FrostWitchTrophy>(), ModContent.ItemType<FrostWitchRelic>(), ModContent.TileType<FrostWitchTrophyTile>(), ModContent.TileType<FrostWitchRelicTile>()),
        ("castle-core", ModContent.NPCType<CastleCoreBoss>(), ModContent.ItemType<CastleCoreMask>(), ModContent.ItemType<CastleCoreTrophy>(), ModContent.ItemType<CastleCoreRelic>(), ModContent.TileType<CastleCoreTrophyTile>(), ModContent.TileType<CastleCoreRelicTile>())
    };

    internal static string Render(string outputDirectory)
    {
        if (Main.dedServ || Main.graphics?.GraphicsDevice is null)
            throw new InvalidOperationException("Boss visual diagnostic requires a client graphics device.");
        Directory.CreateDirectory(outputDirectory);
        string bright = RenderBossSheet(outputDirectory, dark: false, hitboxes: false);
        string dark = RenderBossSheet(outputDirectory, dark: true, hitboxes: false);
        string hitboxes = RenderBossSheet(outputDirectory, dark: false, hitboxes: true);
        string collectibles = RenderCollectibles(outputDirectory);
        string masks = RenderMasksWithNativePlayerRenderer(outputDirectory);
        string maskNeckAb = RenderMaskHeadLayerAB(outputDirectory);
        string maskArmorMatrix = RenderMaskArmorNeckMatrix(outputDirectory);
        string maskFullArmorMatrix = RenderMaskFullArmorPoseMatrix(outputDirectory);
        string maskReferences = ExportMaskFrameReferences(outputDirectory);
        string warnings = RenderBossWarningGeometry(outputDirectory);
        string attacks = RenderBossProjectileShowcase(outputDirectory);
        string combat = RenderNativeCombatScenes(outputDirectory);
        string pets = RenderBossPetShowcase(outputDirectory);
        string minions = RenderNativeMinionPoses(outputDirectory);
        string expandedGroundBosses = RenderExpandedGroundBossFrames(outputDirectory);
        string enchantingPedestal = RenderEnchantingPedestalSupports(outputDirectory);
        // RenderNativeDisplayTiles temporarily resizes TileData's global backing
        // storage. Keep all native-world fixtures ahead of it.
        string nativeDisplays = RenderNativeDisplayTiles(outputDirectory);
        return $"boss-native-frames=PASS {bright}; {dark}; {hitboxes}; {collectibles}; {masks}; {maskNeckAb}; {maskArmorMatrix}; {maskFullArmorMatrix}; {maskReferences}; {enchantingPedestal}; {nativeDisplays}; {warnings}; {attacks}; {combat}; {pets}; {minions}; {expandedGroundBosses}";
    }

    private static string RenderExpandedGroundBossFrames(string outputDirectory)
    {
        var entries = new (string Name, int Type, int Width)[]
        {
            ("Wraith Warrior", ModContent.NPCType<WraithWarriorBoss>(), 160),
            ("Jungle Lord", ModContent.NPCType<JungleLordBoss>(), 128),
            ("Obsidian Guardian", ModContent.NPCType<ObsidianGuardianBoss>(), 128)
        };
        string[] frames = { "idle", "walk-base", "walk-contact", "walk-pass", "charge", "attack", "recover", "jump" };
        const int cellWidth = 176, rowHeight = 198, header = 28;
        int width = frames.Length * 2 * cellWidth;
        int height = header + entries.Length * rowHeight;
        GraphicsDevice device = Main.graphics.GraphicsDevice;
        RenderTargetBinding[] oldTargets = device.GetRenderTargets();
        Viewport oldViewport = device.Viewport;
        NPC[] oldNpcs = Main.npc;
        Vector2 oldScreen = Main.screenPosition;
        int oldW = Main.screenWidth, oldH = Main.screenHeight;
        int oldNet = Main.netMode, oldMode = Main.GameMode;
        bool oldMenu = Main.gameMenu;
        var rows = new List<string>();
        using RenderTarget2D target = new(device, width, height, false, SurfaceFormat.Color,
            DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
        try
        {
            var viewport = new Viewport(0, 0, width, height);
            device.SetRenderTarget(target);
            device.Viewport = viewport;
            Main.GameViewMatrix.SetViewportOverride(viewport);
            Main.screenWidth = width; Main.screenHeight = height; Main.screenPosition = Vector2.Zero;
            Main.netMode = NetmodeID.SinglePlayer; Main.GameMode = 0; Main.gameMenu = false;
            Main.npc = Enumerable.Range(0, Math.Max(oldNpcs.Length, Main.maxNPCs + 1)).Select(_ => new NPC()).ToArray();
            device.Clear(new Color(27, 31, 41));
            Main.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp,
                DepthStencilState.None, RasterizerState.CullCounterClockwise);
            ChatManager.DrawColorCodedStringWithShadow(Main.spriteBatch, FontAssets.MouseText.Value,
                "Ground bosses: authored 8-frame atlas · native PreDraw · actual left/right orientation",
                new Vector2(8, 4), Color.White, 0f, Vector2.Zero, Vector2.One, width - 16, 1f);
            Texture2D pixel = TextureAssets.MagicPixel.Value;
            Rectangle pixelSource = new(0, 0, 1, 1);
            for (int row = 0; row < entries.Length; row++)
            {
                var entry = entries[row];
                NPC npc = new() { whoAmI = 40 + row };
                npc.SetDefaults(entry.Type);
                npc.whoAmI = 40 + row;
                npc.active = true;
                npc.life = npc.lifeMax;
                npc.direction = npc.spriteDirection = 1;
                npc.ai[0] = 0f;
                npc.Bottom = new Vector2(0f, header + row * rowHeight + rowHeight - 12);
                Main.npc[npc.whoAmI] = npc;
                int sourceHeight = TextureAssets.Npc[entry.Type].Value.Height / 8;
                for (int side = 0; side < 2; side++)
                for (int frame = 0; frame < 8; frame++)
                {
                    int direction = side == 0 ? -1 : 1;
                    int col = side * 8 + frame;
                    float x = col * cellWidth + cellWidth * 0.5f;
                    float baseline = header + row * rowHeight + rowHeight - 12;
                    npc.direction = npc.spriteDirection = direction;
                    npc.Bottom = new Vector2(x, baseline);
                    npc.frame = new Rectangle(0, frame * sourceHeight, entry.Width, sourceHeight);
                    Main.spriteBatch.Draw(pixel, new Rectangle(col * cellWidth + 8, (int)baseline, cellWidth - 16, 1),
                        pixelSource, new Color(87, 105, 119));
                    ChatManager.DrawColorCodedStringWithShadow(Main.spriteBatch, FontAssets.MouseText.Value,
                        $"{(direction < 0 ? "L" : "R")} {frames[frame]}",
                        new Vector2(col * cellWidth + 5, header + row * rowHeight + 4), Color.White,
                        0f, Vector2.Zero, Vector2.One, cellWidth - 10, 0.82f);
                    npc.ModNPC.PreDraw(Main.spriteBatch, Vector2.Zero, Color.White);
                }
                ChatManager.DrawColorCodedStringWithShadow(Main.spriteBatch, FontAssets.MouseText.Value,
                    entry.Name, new Vector2(8, header + row * rowHeight + 8), new Color(244, 198, 124),
                    0f, Vector2.Zero, Vector2.One, 150, 0.72f);
                rows.Add($"{entry.Name}: native PreDraw frames=[{string.Join(',', frames)}] L/R; source={entry.Width}x{sourceHeight}");
            }
            Main.spriteBatch.End();
            device.SetRenderTargets(oldTargets);
            using FileStream stream = File.Create(Path.Combine(outputDirectory, "ground-bosses-native-8frames.png"));
            target.SaveAsPng(stream, width, height);
            string report = $"expanded-ground-boss-native-frames=PASS {entries.Length} bosses x 8 authored frames x 2 directions; actual ModNPC.PreDraw and glow; {width}x{height}\n" + string.Join("\n", rows);
            File.WriteAllText(Path.Combine(outputDirectory, "ground-bosses-native-8frames.txt"), report + "\n");
            return report;
        }
        finally
        {
            try { Main.spriteBatch.End(); } catch { }
            device.SetRenderTargets(oldTargets);
            device.Viewport = oldViewport;
            Main.GameViewMatrix.ClearViewportOverride();
            Main.npc = oldNpcs;
            Main.screenPosition = oldScreen; Main.screenWidth = oldW; Main.screenHeight = oldH;
            Main.netMode = oldNet; Main.GameMode = oldMode; Main.gameMenu = oldMenu;
        }
    }

    private static string RenderNativeMinionPoses(string outputDirectory)
    {
        if (Main.dedServ || Main.graphics?.GraphicsDevice is null)
            throw new InvalidOperationException("Native minion pose rendering requires a client graphics device.");

        var entries = new (string Name, int Type)[]
        {
            ("Wraith archer", ModContent.NPCType<WraithArcherMinion>()),
            ("Skeleton warrior", ModContent.NPCType<SkeletonWarriorMinion>()),
            ("Skeleton archer", ModContent.NPCType<SkeletonArcherMinion>()),
            ("Explosive slime", ModContent.NPCType<ExplosiveSlimeMinion>()),
            ("Frost naga", ModContent.NPCType<FrostNagaMinion>()),
            ("Frost archer", ModContent.NPCType<FrostArcherMinion>())
        };
        string[] poseNames = { "idle", "walk-a", "walk-b", "charge", "release/swing", "jump" };
        int columns = poseNames.Length * 2;
        const int cellWidth = 128, rowHeight = 132;
        int width = columns * cellWidth, height = entries.Length * rowHeight;
        GraphicsDevice device = Main.graphics.GraphicsDevice;
        RenderTargetBinding[] oldTargets = device.GetRenderTargets();
        Vector2 oldScreen = Main.screenPosition;
        int oldW = Main.screenWidth, oldH = Main.screenHeight;
        bool oldMenu = Main.gameMenu;
        int oldMode = Main.GameMode, oldNet = Main.netMode, oldMyPlayer = Main.myPlayer;
        NPC[] oldNpcs = Main.npc;
        UnifiedRandom oldRandom = Main.rand;
        RasterizerState oldRasterizer = device.RasterizerState;
        bool begun = false;
        using var target = new RenderTarget2D(device, width, height, false, SurfaceFormat.Color,
            DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
        using var raster = new RasterizerState { CullMode = CullMode.None, ScissorTestEnable = false };
        try
        {
            Main.gameMenu = false; Main.netMode = NetmodeID.SinglePlayer; Main.GameMode = 0;
            Main.screenWidth = width; Main.screenHeight = height; Main.screenPosition = Vector2.Zero;
            Main.rand = new UnifiedRandom(0x031031);
            Main.npc = Enumerable.Range(0, Math.Max(oldNpcs.Length, Main.maxNPCs + 1)).Select(_ => new NPC()).ToArray();
            device.SetRenderTarget(target);
            device.Clear(new Color(19, 24, 33));
            Main.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp,
                DepthStencilState.None, raster, null, Matrix.Identity);
            begun = true;

            const int poseCount = 6;
            for (int row = 0; row < entries.Length; row++)
            {
                var entry = entries[row];
                NPC minion = new() { whoAmI = row + 20 };
                minion.SetDefaults(entry.Type);
                minion.whoAmI = row + 20;
                minion.active = true;
                minion.life = minion.lifeMax;
                minion.direction = minion.spriteDirection = -1;
                minion.Bottom = new Vector2(52f, row * rowHeight + 111f);
                minion.localAI[3] = 0f;
                minion.ModNPC.FindFrame(64);
                Main.npc[minion.whoAmI] = minion;

                for (int directionIndex = 0; directionIndex < 2; directionIndex++)
                for (int pose = 0; pose < poseCount; pose++)
                {
                    int column = directionIndex * poseCount + pose;
                    int direction = directionIndex == 0 ? -1 : 1;
                    minion.direction = minion.spriteDirection = direction;
                    minion.localAI[3] = pose;
                    minion.frameCounter = pose is 1 or 2 ? 8f : 0f;
                    minion.ModNPC.FindFrame(64);
                    minion.Bottom = new Vector2(column * cellWidth + cellWidth / 2f, row * rowHeight + 111f);
                    Main.screenPosition = Vector2.Zero;
                    Main.spriteBatch.Draw(TextureAssets.MagicPixel.Value,
                        new Rectangle(column * cellWidth + 8, row * rowHeight + 111, cellWidth - 16, 1),
                        new Rectangle(0, 0, 1, 1), new Color(73, 92, 111));
                    Utils.DrawBorderString(Main.spriteBatch,
                        $"{(direction < 0 ? "L" : "R")} {poseNames[pose]}",
                        new Vector2(column * cellWidth + 7, row * rowHeight + 7), new Color(219, 225, 235), 0.58f);
                    minion.ModNPC.PreDraw(Main.spriteBatch, Vector2.Zero, Color.White);
                }
                Utils.DrawBorderString(Main.spriteBatch, entry.Name,
                    new Vector2(8, row * rowHeight + 89), new Color(244, 198, 124), 0.62f);
            }

            if (begun) { Main.spriteBatch.End(); begun = false; }
            device.SetRenderTarget(null);
            string image = Path.Combine(outputDirectory, "boss-minions-native-poses.png");
            using (FileStream stream = File.Create(image)) target.SaveAsPng(stream, width, height);
            string report = "six-boss-minions-native-poses=PASS actual ModNPC.FindFrame+PreDraw at 1x; six frames (idle/walk-a/walk-b/charge/release-or-swing/jump) x left/right; includes each native glow overlay; ground baseline shown";
            File.WriteAllText(Path.Combine(outputDirectory, "boss-minions-native-poses.log"), report + "\n");
            return $"{report}; {Path.GetFileName(image)} {width}x{height}";
        }
        finally
        {
            if (begun) Main.spriteBatch.End();
            device.SetRenderTargets(oldTargets);
            Main.screenPosition = oldScreen; Main.screenWidth = oldW; Main.screenHeight = oldH;
            Main.gameMenu = oldMenu; Main.GameMode = oldMode; Main.netMode = oldNet; Main.myPlayer = oldMyPlayer;
            Main.npc = oldNpcs; Main.rand = oldRandom;
            device.RasterizerState = oldRasterizer;
        }
    }

    private static string RenderNativeCombatScenes(string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        string tracePath = Path.Combine(outputDirectory, "boss-native-combat-scenes-trace.txt");
        File.WriteAllText(tracePath, "combat scene trace entered\n");
        const int width = 1920, rowHeight = 360, height = rowHeight * 7;
        File.AppendAllText(tracePath, "stage=graphics\n");
        GraphicsDevice device = Main.graphics.GraphicsDevice;
        RenderTargetBinding[] oldTargets = device.GetRenderTargets();
        Vector2 oldScreen = Main.screenPosition;
        int oldW = Main.screenWidth, oldH = Main.screenHeight;
        NPC[] oldNpcs = Main.npc;
        Projectile[] oldProjectiles = Main.projectile;
        Player[] oldPlayers = (Player[])Main.player.Clone();
        int owner = Math.Clamp(Main.myPlayer, 0, Main.maxPlayers - 1);
        File.AppendAllText(tracePath, $"stage=player-setup owner={owner} playerCount={Main.player.Length} maxPlayers={Main.maxPlayers}\n");
        Player actor = SmokeSystem.CreatePreviewPlayer(ItemID.IronHelmet, ItemID.IronChainmail, ItemID.IronGreaves);
        actor.whoAmI = owner;
        actor.isDisplayDollOrInanimate = false;
        Main.player[owner] = actor;
        File.AppendAllText(tracePath, $"stage=player-created width={actor.width} height={actor.height} armorLen={actor.armor.Length}\n");
        File.AppendAllText(tracePath, "stage=player-equipped\n");
        bool oldActorImmune = actor.immune;
        int oldActorImmuneTime = actor.immuneTime, oldMode = Main.GameMode, oldNet = Main.netMode, oldMyPlayer = Main.myPlayer;
        bool oldMenu = Main.gameMenu;
        Rectangle oldScissor = device.ScissorRectangle;
        RasterizerState oldRasterizer = device.RasterizerState;
        UnifiedRandom oldRandom = Main.rand;
        ulong oldUpdateCount = Main.GameUpdateCount;
        int tileX = Math.Clamp(Main.maxTilesX / 2, 100, Main.maxTilesX - 100);
        int tileY = Math.Clamp(Main.maxTilesY / 2, 80, Main.maxTilesY - 40);
        const int floorRadius = 90, floorDepth = 12;
        File.AppendAllText(tracePath, $"stage=tile-snapshot-start max={Main.maxTilesX}x{Main.maxTilesY} tile=({tileX},{tileY})\n");
        Tile[,] oldTiles = new Tile[floorRadius * 2 + 1, floorDepth + 1];
        for (int x = -floorRadius; x <= floorRadius; x++)
        for (int y = -floorDepth; y <= 0; y++)
        {
            Tile copy = new(); copy.CopyFrom(Main.tile[tileX + x, tileY + y]);
            oldTiles[x + floorRadius, y + floorDepth] = copy;
        }
        File.AppendAllText(tracePath, "stage=tile-snapshot-done\n");
        for (int x = -floorRadius; x <= floorRadius; x++)
        for (int y = -floorDepth; y <= 0; y++)
        {
            Tile tile = Main.tile[tileX + x, tileY + y];
            tile.HasTile = y == 0;
            tile.TileType = TileID.Stone;
            tile.LiquidAmount = 0;
            tile.IsActuated = false;
        }
        File.AppendAllText(tracePath, "stage=tile-patch-done\n");
        float fixtureFloorY = tileY * 16f;
        File.AppendAllText(tracePath, $"stage=tiles-ready tile=({tileX},{tileY}) floor={fixtureFloorY}\n");

        MethodInfo updateNpc = typeof(NPC).GetMethod("UpdateNPC", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null, new[] { typeof(int) }, null) ?? throw new InvalidOperationException("NPC.UpdateNPC missing for combat screenshot");
        MethodInfo updateProjectile = typeof(Projectile).GetMethod("Update", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null, new[] { typeof(int) }, null) ?? throw new InvalidOperationException("Projectile.Update missing for combat screenshot");
        FieldInfo nextMove = typeof(MilkcraftBossNPC).GetField("_nextMove", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Boss move selector unavailable for combat screenshot");
        using RenderTarget2D target = new(device, width, height, false, SurfaceFormat.Color,
            DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
        using RasterizerState cellScissor = new() { CullMode = CullMode.None, ScissorTestEnable = true };
        var cases = new (string Name, int NpcType, int Move, BossProjectileStyle Style, bool Low)[]
        {
            ("Wraith Warrior · spear spin", ModContent.NPCType<WraithWarriorBoss>(), 1, BossProjectileStyle.WraithSpin, true),
            ("Jungle Lord · grounded vine wave", ModContent.NPCType<JungleLordBoss>(), 0, BossProjectileStyle.VineRoot, true),
            ("Banshee · expanding wave", ModContent.NPCType<BansheeBoss>(), 1, BossProjectileStyle.BansheeWave, true),
            ("Obsidian Guardian · broken ring", ModContent.NPCType<ObsidianGuardianBoss>(), 1, BossProjectileStyle.ObsidianRing, true),
            ("Sphinx · outer sand tornadoes", ModContent.NPCType<SphinxBoss>(), 3, BossProjectileStyle.Sandnado, true),
            ("Frost Witch · three ice rains", ModContent.NPCType<FrostWitchBoss>(), 1, BossProjectileStyle.FrostPillar, true),
            ("Castle Core · red tracking fire", ModContent.NPCType<CastleCoreBoss>(), 3, BossProjectileStyle.CastleRedTracking, true)
        };
        int[] attackTicks = new int[cases.Length];
        int[] warningTicks = new int[cases.Length];
        bool begun = false;
        try
        {
            Main.netMode = NetmodeID.SinglePlayer; Main.myPlayer = actor.whoAmI; Main.GameMode = 0; Main.gameMenu = false;
            Main.screenPosition = Vector2.Zero; Main.screenWidth = width; Main.screenHeight = height;
            actor.active = true; actor.dead = false; actor.immune = true; actor.immuneTime = 600;
            actor.isDisplayDollOrInanimate = false;
            actor.statLife = actor.statLifeMax = 100;
            device.SetRenderTarget(target);
            device.Clear(new Color(15, 20, 29));

            for (int scene = 0; scene < cases.Length; scene++)
            {
                var test = cases[scene];
                File.AppendAllText(tracePath, $"scene={scene} setup-start {test.Name}\n");
                Main.rand = new UnifiedRandom(0xB05531 + scene * 733);
                Main.npc = Enumerable.Range(0, oldNpcs.Length).Select(_ => new NPC()).ToArray();
                Main.projectile = Enumerable.Range(0, oldProjectiles.Length).Select(_ => new Projectile()).ToArray();
                actor.velocity = Vector2.Zero;
                actor.Center = new Vector2((tileX + 17) * 16f, fixtureFloorY - actor.height * 0.5f);
                actor.direction = -1;
                NPC boss = new() { whoAmI = 60 };
                boss.SetDefaults(test.NpcType);
                boss.whoAmI = 60; boss.active = true; boss.timeLeft = 600; boss.target = actor.whoAmI;
                boss.Center = new Vector2((tileX - 12) * 16f, fixtureFloorY - boss.height * 0.5f);
                boss.life = test.Low ? Math.Max(1, boss.lifeMax * 42 / 100) : boss.lifeMax * 90 / 100;
                boss.ai[0] = 0f; boss.ai[1] = 1000f; boss.ai[2] = 0f;
                Main.npc[boss.whoAmI] = boss;
                nextMove.SetValue(boss.ModNPC, test.Move);
                File.AppendAllText(tracePath, $"scene={scene} setup-done bossType={boss.type} bossMod={boss.ModNPC?.GetType().Name} pos={boss.position} hitbox={boss.width}x{boss.height}\n");

                bool warningCaptured = false, attackCaptured = false;
                int firstStyleTick = -1;
                File.AppendAllText(tracePath, $"scene={scene} begin {test.Name} boss={boss.Center} player={actor.Center} floor={fixtureFloorY}\n");
                for (int tick = 0; tick < 900 && !attackCaptured; tick++)
                {
                    SetDiagnosticGameUpdateCount(Main.GameUpdateCount + 1);
                    for (int n = 0; n < Main.npc.Length; n++)
                        if (Main.npc[n] is { active: true }) updateNpc.Invoke(Main.npc[n], new object[] { n });
                    for (int p = 0; p < Main.projectile.Length; p++)
                        if (Main.projectile[p] is { active: true }) updateProjectile.Invoke(Main.projectile[p], new object[] { p });

                    if (!warningCaptured && (int)boss.ai[2] == test.Move && (int)boss.ai[0] == 1)
                    {
                        warningTicks[scene] = tick;
                        File.AppendAllText(tracePath, $"scene={scene} warning tick={tick} boss={boss.Center} ai=({boss.ai[0]},{boss.ai[1]},{boss.ai[2]}) frame={boss.frame}\n");
                        DrawSnapshot(scene, warning: true, tick, boss, fixtureFloorY);
                        warningCaptured = true;
                        actor.Center = new Vector2((tileX + 17) * 16f, fixtureFloorY - actor.height * 0.5f);
                    }

                    Projectile[] relevant = Main.projectile.Where(p => p != null && p.active
                        && p.type == ModContent.ProjectileType<MilkcraftBossProjectile>()
                        && (int)p.ai[1] == boss.whoAmI
                        && (BossProjectileStyle)(int)p.ai[0] == test.Style).ToArray();
                    bool relevantMinion = test.Style == BossProjectileStyle.MinionSweep && Main.npc.Any(npc =>
                        npc is { active: true } && npc.ModNPC is MilkcraftBossMinion
                        && npc.GetGlobalNPC<MilkcraftBossEscortGlobalNPC>().ParentBoss == boss.whoAmI);
                    if (relevant.Length > 0 && firstStyleTick < 0) firstStyleTick = tick;
                    int settleDelay = test.Style == BossProjectileStyle.FrostPillar ? 60 : 10;
                    if (!attackCaptured && warningCaptured && tick >= firstStyleTick + settleDelay && (relevant.Length > 0 || relevantMinion))
                    {
                        attackTicks[scene] = tick;
                        File.AppendAllText(tracePath, $"scene={scene} attack tick={tick} boss={boss.Center} entities={Main.projectile.Count(p => p is { active: true })} styles={string.Join(',', relevant.Select(p => p.ai[0]))}\n");
                        DrawSnapshot(scene, warning: false, tick, boss, fixtureFloorY);
                        attackCaptured = true;
                    }
                }
                if (!warningCaptured || !attackCaptured)
                    throw new InvalidOperationException($"Native combat screenshot did not capture {test.Name}: warning={warningCaptured}@{warningTicks[scene]} attack={attackCaptured}@{attackTicks[scene]} style={test.Style}");
            }

            if (begun) { Main.spriteBatch.End(); begun = false; }
            device.SetRenderTarget(null);
            string fileName = "boss-native-combat-scenes.png";
            File.AppendAllText(tracePath, "stage=save-png\n");
            using FileStream file = File.Create(Path.Combine(outputDirectory, fileName));
            target.SaveAsPng(file, width, height);
            var trace = string.Join("\n", cases.Select((entry, i) => $"{entry.Name}: warningUpdate={warningTicks[i]}, activeAttackUpdate={attackTicks[i]}, requestedStyle={entry.Style}, lowPhase={entry.Low}; native NPC.UpdateNPC+Projectile.Update"));
            File.WriteAllText(Path.Combine(outputDirectory, "boss-native-combat-scenes.txt"), trace + "\n");
            return $"actual-boss-combat-scenes=PASS 7 encounter snapshots after native NPC.UpdateNPC+Projectile.Update warning+attack; {fileName}; {trace}";
        }
        finally
        {
            if (begun) Main.spriteBatch.End();
            device.SetRenderTargets(oldTargets);
            Main.screenPosition = oldScreen; Main.screenWidth = oldW; Main.screenHeight = oldH;
            Main.npc = oldNpcs; Main.projectile = oldProjectiles; Main.player = oldPlayers;
            device.ScissorRectangle = oldScissor;
            device.RasterizerState = oldRasterizer;
            actor.immune = oldActorImmune; actor.immuneTime = oldActorImmuneTime;
            Main.GameMode = oldMode; Main.netMode = oldNet; Main.myPlayer = oldMyPlayer; Main.rand = oldRandom;
            Main.gameMenu = oldMenu;
            SetDiagnosticGameUpdateCount(oldUpdateCount);
            for (int x = -floorRadius; x <= floorRadius; x++)
            for (int y = -floorDepth; y <= 0; y++)
                Main.tile[tileX + x, tileY + y].CopyFrom(oldTiles[x + floorRadius, y + floorDepth]);
        }

        void DrawSnapshot(int scene, bool warning, int tick, NPC sceneBoss, float floorWorldY)
        {
            var test = cases[scene];
            int column = warning ? 0 : 1;
            int cellX = column * 960;
            int rowY = scene * rowHeight;
            float horizontalFocus = warning ? 330f
                : test.Style is BossProjectileStyle.FrostPillar or BossProjectileStyle.Sandnado ? 480f : 330f;
            Vector2 bossScreen = new(cellX + horizontalFocus, rowY + 148f);
            Main.screenPosition = sceneBoss.Center - bossScreen;
            File.AppendAllText(tracePath, $"scene={scene} draw begin warning={warning} tick={tick} cam={Main.screenPosition} boss={sceneBoss.Center} cameraBoss={sceneBoss.Center-Main.screenPosition} floor={floorWorldY-Main.screenPosition.Y} cell=({cellX},{rowY},{948},{rowHeight-12})\n");
            device.ScissorRectangle = new Rectangle(cellX + 6, rowY + 6, 948, rowHeight - 12);
            Main.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp,
                DepthStencilState.None, cellScissor);
            begun = true;
            Rectangle bg = new(cellX + 6, rowY + 6, 948, rowHeight - 12);
            Rectangle pixelSource = new(0, 0, 1, 1);
            Main.spriteBatch.Draw(TextureAssets.MagicPixel.Value, bg, pixelSource, warning ? new Color(29, 39, 51) : new Color(37, 33, 39));
            Utils.DrawBorderString(Main.spriteBatch, $"{test.Name} · {(warning ? "warning" : "attack")}",
                new Vector2(cellX + 18, rowY + 12), Color.White, 0.72f);
            float floorScreenY = floorWorldY - Main.screenPosition.Y;
            Main.spriteBatch.Draw(TextureAssets.MagicPixel.Value,
                new Rectangle(cellX + 10, (int)floorScreenY, 930, Math.Max(1, rowY + rowHeight - (int)floorScreenY - 8)), pixelSource,
                new Color(73, 77, 84, 220));
            Main.spriteBatch.Draw(TextureAssets.MagicPixel.Value,
                new Rectangle(cellX + 10, (int)floorScreenY, 930, 3), pixelSource, new Color(142, 150, 160));
            foreach (NPC npc in Main.npc)
            {
                if (npc == null || !npc.active || npc.ModNPC is null) continue;
                if (npc.ModNPC is MilkcraftBossNPC or MilkcraftBossMinion or CastleSoulCrystal)
                {
                    File.AppendAllText(tracePath, $"scene={scene} draw-npc type={npc.type} center={npc.Center} pos={npc.position} frame={npc.frame} rot={npc.rotation} scale={npc.scale}\n");
                    npc.ModNPC.PreDraw(Main.spriteBatch, Main.screenPosition, Color.White);
                }
            }
            foreach (Projectile p in Main.projectile)
            {
                if (p == null || !p.active || p.ModProjectile is not MilkcraftBossProjectile) continue;
                File.AppendAllText(tracePath, $"scene={scene} draw-projectile type={p.type} style={p.ai[0]} center={p.Center} pos={p.position} vel={p.velocity} rot={p.rotation} scale={p.scale} time={p.timeLeft}\n");
                Color light = Color.White;
                p.ModProjectile.PreDraw(ref light);
            }
            actor.direction = actor.Center.X > sceneBoss.Center.X ? -1 : 1;
            bool wasImmune = actor.immune;
            int priorImmuneTime = actor.immuneTime;
            Vector2 worldPosition = actor.position;
            Vector2 worldScreen = Main.screenPosition;
            actor.immune = false; actor.immuneTime = 0; // Avoid vanilla immunity blinking in the reference snapshot.
            // PlayerRenderer uses Main.Camera's native matrix, which is not
            // centered on our synthetic high world coordinates. Render the
            // fixture actor in panel-local coordinates while boss/projectiles
            // continue to use the native world camera above.
            Main.screenPosition = Vector2.Zero;
            SmokeSystem.DrawPreviewPlayer(actor,
                (int)(actor.Center.X - worldScreen.X - actor.width * 0.5f),
                (int)(actor.Center.Y - worldScreen.Y - actor.height * 0.5f), 1f);
            Main.screenPosition = worldScreen;
            actor.position = worldPosition;
            actor.immune = wasImmune; actor.immuneTime = priorImmuneTime;
            Vector2 playerScreen = actor.Center - worldScreen;
            DrawPlayerTargetMarker(playerScreen, actor.width, actor.height);
            File.AppendAllText(tracePath, $"scene={scene} draw-player-renderer+target-marker pos={actor.position} center={actor.Center} screen={playerScreen}\n");
            Utils.DrawBorderString(Main.spriteBatch, $"native update {tick}", new Vector2(cellX + 18, rowY + rowHeight - 26), new Color(211, 216, 224), 0.62f);
            Main.spriteBatch.End(); begun = false;
            File.AppendAllText(tracePath, $"scene={scene} draw end warning={warning}\n");
            Main.screenPosition = Vector2.Zero;
        }

        void DrawPlayerTargetMarker(Vector2 center, int playerWidth, int playerHeight)
        {
            Rectangle pixel = new(0, 0, 1, 1);
            int x = (int)MathF.Round(center.X), y = (int)MathF.Round(center.Y);
            Main.spriteBatch.Draw(TextureAssets.MagicPixel.Value, new Rectangle(x - 4, y - 19, 8, 8), pixel, new Color(105, 208, 255));
            Main.spriteBatch.Draw(TextureAssets.MagicPixel.Value, new Rectangle(x - 6, y - 10, 12, 17), pixel, new Color(53, 155, 218));
            Main.spriteBatch.Draw(TextureAssets.MagicPixel.Value, new Rectangle(x - 10, y - 9, 4, 13), pixel, new Color(81, 185, 239));
            Main.spriteBatch.Draw(TextureAssets.MagicPixel.Value, new Rectangle(x + 6, y - 9, 4, 13), pixel, new Color(81, 185, 239));
            Main.spriteBatch.Draw(TextureAssets.MagicPixel.Value, new Rectangle(x - 5, y + 7, 4, 12), pixel, new Color(55, 113, 172));
            Main.spriteBatch.Draw(TextureAssets.MagicPixel.Value, new Rectangle(x + 1, y + 7, 4, 12), pixel, new Color(55, 113, 172));
            Rectangle hitbox = new(x - playerWidth / 2, y - playerHeight / 2, playerWidth, playerHeight);
            Color outline = new(128, 225, 255, 220);
            Main.spriteBatch.Draw(TextureAssets.MagicPixel.Value, new Rectangle(hitbox.X, hitbox.Y, hitbox.Width, 1), pixel, outline);
            Main.spriteBatch.Draw(TextureAssets.MagicPixel.Value, new Rectangle(hitbox.X, hitbox.Bottom - 1, hitbox.Width, 1), pixel, outline);
            Main.spriteBatch.Draw(TextureAssets.MagicPixel.Value, new Rectangle(hitbox.X, hitbox.Y, 1, hitbox.Height), pixel, outline);
            Main.spriteBatch.Draw(TextureAssets.MagicPixel.Value, new Rectangle(hitbox.Right - 1, hitbox.Y, 1, hitbox.Height), pixel, outline);
        }
    }

    private static void SetDiagnosticGameUpdateCount(ulong value)
    {
        PropertyInfo property = typeof(Main).GetProperty("GameUpdateCount",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new MissingMemberException(typeof(Main).FullName, "GameUpdateCount");
        MethodInfo setter = property.GetSetMethod(true);
        if (setter is not null)
        {
            setter.Invoke(null, new object[] { value });
            return;
        }
        FieldInfo backing = typeof(Main).GetField("<GameUpdateCount>k__BackingField",
            BindingFlags.Static | BindingFlags.NonPublic)
            ?? typeof(Main).GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                .FirstOrDefault(field => field.Name.Contains("GameUpdateCount", StringComparison.OrdinalIgnoreCase));
        if (backing is null) throw new MissingMemberException(typeof(Main).FullName, "GameUpdateCount backing storage");
        backing.SetValue(null, Convert.ChangeType(value, backing.FieldType));
    }

    private static string RenderBossPetShowcase(string outputDirectory)
    {
        const int width = 1540, height = 820;
        GraphicsDevice device = Main.graphics.GraphicsDevice;
        RenderTargetBinding[] oldTargets = device.GetRenderTargets();
        Vector2 oldScreen = Main.screenPosition;
        int oldW = Main.screenWidth, oldH = Main.screenHeight;
        Projectile[] oldProjectiles = Main.projectile;
        bool begun = false;
        using RenderTarget2D target = new(device, width, height, false, SurfaceFormat.Color,
            DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
        var pets = new (MilkcraftBossPetKind Kind, string Label, int Idle, int Motion, int Face)[ ]
        {
            (MilkcraftBossPetKind.WraithWarrior, "Wraith Warrior · ground", 0, 2, 5),
            (MilkcraftBossPetKind.JungleLord, "Jungle Lord · ground", 0, 2, 5),
            (MilkcraftBossPetKind.Banshee, "Banshee · light pet", 0, 2, 5),
            (MilkcraftBossPetKind.ObsidianGuardian, "Obsidian Guardian · ground", 0, 2, 5),
            (MilkcraftBossPetKind.Sphinx, "Sphinx · ground / flight", 0, 2, 6),
            (MilkcraftBossPetKind.FrostWitch, "Frost Witch · light pet", 0, 2, 5),
            (MilkcraftBossPetKind.CastleCore, "Castle Core · face-linked light", 0, 5, 6)
        };
        try
        {
            Main.screenPosition = Vector2.Zero;
            Main.screenWidth = width; Main.screenHeight = height;
            Main.projectile = Enumerable.Range(0, oldProjectiles.Length).Select(_ => new Projectile()).ToArray();
            device.SetRenderTarget(target);
            device.Clear(new Color(18, 22, 31));
            Main.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp,
                DepthStencilState.None, RasterizerState.CullCounterClockwise);
            begun = true;
            for (int row = 0; row < pets.Length; row++)
            {
                var entry = pets[row];
                int y = 14 + row * 112;
                Main.spriteBatch.Draw(TextureAssets.MagicPixel.Value, new Rectangle(10, y, width - 20, 104),
                    row % 2 == 0 ? new Color(27, 33, 45) : new Color(23, 29, 40));
                Utils.DrawBorderString(Main.spriteBatch, entry.Label, new Vector2(22, y + 8), Color.White, 0.78f);
                DrawPet(entry.Kind, entry.Idle, new Vector2(490, y + 76), -1);
                DrawPet(entry.Kind, entry.Motion, new Vector2(810, y + 76), 1);
                DrawPet(entry.Kind, entry.Face, new Vector2(1130, y + 76), 1);
                Utils.DrawBorderString(Main.spriteBatch, "idle", new Vector2(450, y + 79), new Color(206, 212, 225), 0.55f);
                Utils.DrawBorderString(Main.spriteBatch, entry.Kind == MilkcraftBossPetKind.Sphinx ? "flight" : "motion", new Vector2(770, y + 79), new Color(206, 212, 225), 0.55f);
                Utils.DrawBorderString(Main.spriteBatch, entry.Kind == MilkcraftBossPetKind.CastleCore ? "face/light" : "jump / mood", new Vector2(1090, y + 79), new Color(206, 212, 225), 0.55f);
            }
            Main.spriteBatch.End(); begun = false;
            device.SetRenderTarget(null);
            string fileName = "boss-pets-native-frames.png";
            using FileStream file = File.Create(Path.Combine(outputDirectory, fileName));
            target.SaveAsPng(file, width, height);
            return $"boss-pet-projectile-predraw=PASS native entities=7 idle+motion+special frames; {fileName}";
        }
        finally
        {
            if (begun) Main.spriteBatch.End();
            device.SetRenderTargets(oldTargets);
            Main.screenPosition = oldScreen;
            Main.screenWidth = oldW; Main.screenHeight = oldH;
            Main.projectile = oldProjectiles;
        }

        static void DrawPet(MilkcraftBossPetKind kind, int frame, Vector2 pos, int direction)
        {
            int type = MilkcraftBossPetAssets.Projectile(kind);
            Projectile pet = new();
            pet.SetDefaults(type);
            pet.whoAmI = 40 + (int)kind;
            pet.owner = Math.Clamp(Main.myPlayer, 0, Main.maxPlayers - 1);
            pet.active = true;
            pet.frame = frame;
            pet.spriteDirection = pet.direction = direction;
            pet.Center = pos;
            Color light = new(178, 178, 185, 255);
            pet.ModProjectile.PreDraw(ref light);
        }
    }

    private static string RenderBossProjectileShowcase(string outputDirectory)
    {
        const int width = 1920, height = 1080;
        GraphicsDevice device = Main.graphics.GraphicsDevice;
        RenderTargetBinding[] oldTargets = device.GetRenderTargets();
        Vector2 oldScreen = Main.screenPosition;
        int oldW = Main.screenWidth, oldH = Main.screenHeight;
        NPC[] oldNpcs = Main.npc;
        Projectile[] oldProjectiles = Main.projectile;
        bool begun = false;
        using RenderTarget2D target = new(device, width, height, false, SurfaceFormat.Color,
            DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
        var showcase = new (string Label, int ParentType, BossProjectileStyle Style, Vector2 Velocity, float Gap)[]
        {
            ("Wraith Warrior · twin spear orbit", ModContent.NPCType<WraithWarriorBoss>(), BossProjectileStyle.WraithSpin, Vector2.Zero, 0f),
            ("Jungle Lord · ground vine root", ModContent.NPCType<JungleLordBoss>(), BossProjectileStyle.VineRoot, new Vector2(7f, 0f), 0f),
            ("Banshee · expanding sound wave", ModContent.NPCType<BansheeBoss>(), BossProjectileStyle.BansheeWave, new Vector2(5f, 0f), 0f),
            ("Obsidian Guardian · cracked ring", ModContent.NPCType<ObsidianGuardianBoss>(), BossProjectileStyle.ObsidianRing, Vector2.Zero, 2.2f),
            ("Sphinx · side tornado", ModContent.NPCType<SphinxBoss>(), BossProjectileStyle.Sandnado, Vector2.Zero, 0f),
            ("Sphinx · outer sand volley", ModContent.NPCType<SphinxBoss>(), BossProjectileStyle.SandShot, new Vector2(5f, 3f), 0f),
            ("Frost Witch · delayed ice pillar", ModContent.NPCType<FrostWitchBoss>(), BossProjectileStyle.FrostPillar, new Vector2(0f, 7f), 0f),
            ("Frost Witch · expanding frost ring", ModContent.NPCType<FrostWitchBoss>(), BossProjectileStyle.FrostRing, Vector2.Zero, 2.2f),
            ("Castle Core · blue chain wave", ModContent.NPCType<CastleCoreBoss>(), BossProjectileStyle.CastleBlueRing, Vector2.Zero, 3.1f),
            ("Castle Core · red tracking flame", ModContent.NPCType<CastleCoreBoss>(), BossProjectileStyle.CastleRedTracking, new Vector2(-7f, 1.5f), 0f),
            ("Wraith Archer · locked arrow", ModContent.NPCType<WraithWarriorBoss>(), BossProjectileStyle.WraithArrow, new Vector2(7f, 0f), 0f),
            ("Frost Naga · attached blade sweep", ModContent.NPCType<FrostWitchBoss>(), BossProjectileStyle.MinionSweep, Vector2.Zero, 0f)
        };
        try
        {
            Main.screenPosition = Vector2.Zero;
            Main.screenWidth = width; Main.screenHeight = height;
            Main.npc = Enumerable.Range(0, oldNpcs.Length).Select(_ => new NPC()).ToArray();
            Main.projectile = Enumerable.Range(0, oldProjectiles.Length).Select(_ => new Projectile()).ToArray();
            device.SetRenderTarget(target);
            device.Clear(new Color(17, 21, 30));
            Main.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp,
                DepthStencilState.None, RasterizerState.CullCounterClockwise);
            begun = true;
            for (int index = 0; index < showcase.Length; index++)
            {
                int col = index % 3, row = index / 3;
                Vector2 cellOrigin = new(24 + col * 640, 14 + row * 268);
                Vector2 center = cellOrigin + new Vector2(290f, 156f);
                DrawLabel(showcase[index].Label, cellOrigin + new Vector2(16f, 8f));
                int parentSlot = 70 + index;
                NPC parent = new();
                parent.SetDefaults(showcase[index].ParentType);
                parent.whoAmI = parentSlot;
                parent.active = true; parent.life = parent.lifeMax; parent.target = Main.myPlayer;
                parent.Center = center + new Vector2(-170f, 0f);
                parent.direction = 1; parent.spriteDirection = 1;
                Main.npc[parentSlot] = parent;

                Projectile attack = new();
                attack.SetDefaults(ModContent.ProjectileType<MilkcraftBossProjectile>());
                attack.whoAmI = index;
                attack.active = true;
                attack.hostile = true; attack.friendly = false; attack.tileCollide = false;
                attack.ai[0] = (float)showcase[index].Style;
                attack.ai[1] = parentSlot;
                attack.ai[2] = showcase[index].Gap;
                attack.Center = center;
                attack.velocity = showcase[index].Velocity;
                attack.timeLeft = 95;
                attack.rotation = showcase[index].Style == BossProjectileStyle.WraithSpin ? 0.58f
                    : showcase[index].Style == BossProjectileStyle.MinionSweep ? -0.35f
                    : showcase[index].Velocity.LengthSquared() > 0f ? showcase[index].Velocity.ToRotation() : 0f;
                if (showcase[index].Style is BossProjectileStyle.ObsidianRing or BossProjectileStyle.SphinxRing
                    or BossProjectileStyle.FrostRing or BossProjectileStyle.CastleBlueRing)
                    attack.timeLeft = 80;
                Main.projectile[index] = attack;
                Color lighting = Color.White;
                attack.ModProjectile.PreDraw(ref lighting);
                Rectangle cell = new((int)cellOrigin.X, (int)cellOrigin.Y, 620, 245);
                Main.spriteBatch.Draw(TextureAssets.MagicPixel.Value, new Rectangle(cell.X, cell.Bottom, cell.Width, 1),
                    new Color(84, 102, 130, 140));
            }
            Main.spriteBatch.End(); begun = false;
            device.SetRenderTarget(null);
            using FileStream file = File.Create(Path.Combine(outputDirectory, "boss-attacks-native-projectile-showcase.png"));
            target.SaveAsPng(file, width, height);
            return $"attack-projectile-visuals=PASS native MilkcraftBossProjectile.PreDraw {showcase.Length} attack styles across all 7 bosses; boss-attacks-native-projectile-showcase.png";
        }
        finally
        {
            if (begun) Main.spriteBatch.End();
            device.SetRenderTargets(oldTargets);
            Main.screenPosition = oldScreen;
            Main.screenWidth = oldW; Main.screenHeight = oldH;
            Main.npc = oldNpcs;
            Main.projectile = oldProjectiles;
        }

        static void DrawLabel(string value, Vector2 position)
            => Utils.DrawBorderString(Main.spriteBatch, value, position, Color.White, 0.8f);
    }

    private static string RenderBossWarningGeometry(string outputDirectory)
    {
        const int width = 1920, height = 650;
        GraphicsDevice device = Main.graphics.GraphicsDevice;
        RenderTargetBinding[] oldTargets = device.GetRenderTargets();
        Vector2 oldScreen = Main.screenPosition;
        int oldW = Main.screenWidth, oldH = Main.screenHeight;
        bool begun = false;
        using RenderTarget2D target = new(device, width, height, false, SurfaceFormat.Color,
            DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
        try
        {
            Main.screenPosition = Vector2.Zero;
            Main.screenWidth = width; Main.screenHeight = height;
            device.SetRenderTarget(target);
            device.Clear(new Color(22, 25, 34));
            Main.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp,
                DepthStencilState.None, RasterizerState.CullCounterClockwise);
            begun = true;
            DrawLabel("Wraith thrust: locked facing", new Vector2(72, 36));
            DrawLabel("Guardian cracked-punch fan: front only", new Vector2(690, 36));
            DrawLabel("Sphinx side tornadoes; center ±96 safe lane", new Vector2(70, 350));
            DrawBossWarning(ModContent.NPCType<WraithWarriorBoss>(), new Vector2(220, 190), new Vector2(560, 190), move: 0);
            DrawBossWarning(ModContent.NPCType<ObsidianGuardianBoss>(), new Vector2(980, 190), new Vector2(1330, 190), move: 0, cracked: true);
            DrawBossWarning(ModContent.NPCType<SphinxBoss>(), new Vector2(960, 470), new Vector2(960, 470), move: 3);
            Main.spriteBatch.End(); begun = false;
            device.SetRenderTarget(null);
            using FileStream file = File.Create(Path.Combine(outputDirectory, "boss-warning-geometry-native.png"));
            target.SaveAsPng(file, width, height);
            return "warning-geometry=PASS native ModNPC.PreDraw: Wraith locked-facing thrust, Guardian forward fan, Sphinx three side batches with central ±96 corridor; boss-warning-geometry-native.png";
        }
        finally
        {
            if (begun) Main.spriteBatch.End();
            device.SetRenderTargets(oldTargets);
            Main.screenPosition = oldScreen;
            Main.screenWidth = oldW; Main.screenHeight = oldH;
        }

        void DrawBossWarning(int npcType, Vector2 center, Vector2 locked, int move, bool cracked = false)
        {
            NPC npc = new();
            npc.SetDefaults(npcType);
            npc.Center = center;
            npc.direction = locked.X < center.X ? -1 : 1;
            npc.spriteDirection = npc.direction;
            npc.frame = new Rectangle(0, 0, TextureAssets.Npc[npcType].Value.Width, TextureAssets.Npc[npcType].Value.Height / 4);
            npc.ai[0] = 1f; // native wind-up stage; warning is drawn by the actual PreDraw hook.
            npc.ai[2] = move;
            if (cracked) npc.life = Math.Max(1, npc.lifeMax / 2 - 1);
            FieldInfo lockedField = typeof(MilkcraftBossNPC).GetField("_lockedPoint", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingFieldException(typeof(MilkcraftBossNPC).FullName, "_lockedPoint");
            lockedField.SetValue(npc.ModNPC, locked);
            npc.ModNPC.PreDraw(Main.spriteBatch, Vector2.Zero, Color.White);
        }

        void DrawLabel(string value, Vector2 position)
        {
            Utils.DrawBorderString(Main.spriteBatch, value, position, Color.White, 0.8f);
        }
    }

    private static string ExportMaskFrameReferences(string outputDirectory)
    {
        if (Main.dedServ) throw new InvalidOperationException("Mask frame references require a graphics client.");
        int vanillaType = ItemID.SkeletronMask;
        int ironType = ModContent.ItemType<IronHelmet>();
        Main.instance.LoadItem(vanillaType);
        Main.instance.LoadItem(ironType);
        Item vanilla = new(); vanilla.SetDefaults(vanillaType);
        Item iron = new(); iron.SetDefaults(ironType);
        const int frameHeight = 56;
        Texture2D vanillaHead = TextureAssets.ArmorHead[vanilla.headSlot].Value;
        if (vanillaHead.Width <= 1 || vanillaHead.Height <= 1)
            vanillaHead = Main.Assets.Request<Texture2D>($"Images/Armor_Head_{vanilla.headSlot}", AssetRequestMode.ImmediateLoad).Value;
        Texture2D ironHead = TextureAssets.ArmorHead[iron.headSlot].Value;
        Rectangle vanillaFrame = new(0, 0, vanillaHead.Width, frameHeight);
        Rectangle ironFrame = new(0, 0, ironHead.Width, frameHeight);
        if (vanilla.headSlot < 0 || iron.headSlot < 0 || vanillaHead.Width != 40 || ironHead.Width != 40
            || vanillaHead.Height < frameHeight || ironHead.Height < frameHeight)
            throw new InvalidOperationException($"Unexpected native head sheet: Skeletron slot={vanilla.headSlot} {vanillaHead.Width}x{vanillaHead.Height}; Iron slot={iron.headSlot} {ironHead.Width}x{ironHead.Height}");
        SaveFrame(vanillaHead, vanillaFrame, Path.Combine(outputDirectory, "reference-skeletron-mask-head-40x56.png"));
        SaveFrame(ironHead, ironFrame, Path.Combine(outputDirectory, "reference-iron-helmet-head-40x56.png"));
        using (FileStream output = File.Create(Path.Combine(outputDirectory, "reference-mask-head-manifest.txt")))
        using (StreamWriter writer = new(output))
        {
            writer.WriteLine($"SkeletronMask item={vanillaType} headSlot={vanilla.headSlot} sheet={vanillaHead.Width}x{vanillaHead.Height} firstFrame={vanillaFrame}");
            writer.WriteLine($"IronHelmet item={ironType} headSlot={iron.headSlot} sheet={ironHead.Width}x{ironHead.Height} firstFrame={ironFrame}");
            writer.WriteLine("First frame is the 40x56 equipment-head frame at source (0,0); actual facing/origin alignment is recorded by boss-masks-worn-native-player-renderer.csv.");
        }
        return $"mask-reference=PASS SkeletronMask/IronHelmet head frames 40x56 exported; slots={vanilla.headSlot}/{iron.headSlot}";

        static void SaveFrame(Texture2D texture, Rectangle source, string path)
        {
            Color[] pixels = new Color[source.Width * source.Height];
            texture.GetData(0, source, pixels, 0, pixels.Length);
            using Texture2D frame = new(texture.GraphicsDevice, source.Width, source.Height, false, SurfaceFormat.Color);
            frame.SetData(pixels);
            using FileStream file = File.Create(path);
            frame.SaveAsPng(file, source.Width, source.Height);
        }
    }

    private static string RenderBossSheet(string outputDirectory, bool dark, bool hitboxes)
    {
        const int width = 1920, height = 1470;
        GraphicsDevice device = Main.graphics.GraphicsDevice;
        RenderTargetBinding[] oldTargets = device.GetRenderTargets();
        Vector2 oldScreen = Main.screenPosition;
        int oldW = Main.screenWidth, oldH = Main.screenHeight;
        bool begun = false;
        using RenderTarget2D target = new(device, width, height, false, SurfaceFormat.Color,
            DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
        var manifest = new StringBuilder("boss,pose,direction,sourceFrame,drawAnchor,scale,npcHitbox,drawColor,hitboxOverlay\n");
        try
        {
            Main.screenPosition = Vector2.Zero;
            Main.screenWidth = width; Main.screenHeight = height;
            device.SetRenderTarget(target);
            device.Clear(dark ? new Color(18, 21, 30) : new Color(62, 66, 71));
            Main.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp,
                DepthStencilState.None, RasterizerState.CullCounterClockwise);
            begun = true;
            for (int row = 0; row < Entries.Length; row++)
            {
                var entry = Entries[row];
                NPC npc = new();
                npc.SetDefaults(entry.Npc);
                Texture2D npcTexture = TextureAssets.Npc[entry.Npc].Value;
                int frameHeight = npcTexture.Height / 4;
                for (int pose = 0; pose < 4; pose++)
                for (int side = 0; side < 2; side++)
                {
                    int col = pose * 2 + side;
                    int direction = side == 0 ? 1 : -1;
                    npc.spriteDirection = direction;
                    npc.direction = direction;
                    npc.rotation = 0f;
                    npc.scale = 1f;
                    npc.frame = new Rectangle(0, pose * frameHeight, npcTexture.Width, frameHeight);
                    Vector2 anchor = new(130 + col * 240, (npc.noGravity ? 110 : 185) + row * 210);
                    npc.position = npc.noGravity
                        ? anchor - new Vector2(npc.width * 0.5f, npc.height * 0.5f)
                        : anchor - new Vector2(npc.width * 0.5f, npc.height);
                    npc.Center = npc.noGravity ? anchor : npc.Center;
                    Color drawColor = dark ? new Color(112, 116, 126, 255) : Color.White;
                    npc.ModNPC.PreDraw(Main.spriteBatch, Vector2.Zero, drawColor);
                    Rectangle npcBox = npc.Hitbox;
                    if (hitboxes)
                        DrawOutline(npcBox, new Color(70, 205, 255, 230), 2);
                    manifest.AppendLine($"{entry.Name},{pose},{direction},{pose},{(npc.noGravity ? "center" : "bottom")},1,{npcBox.X}:{npcBox.Y}:{npcBox.Width}:{npcBox.Height},{drawColor.R}:{drawColor.G}:{drawColor.B},{hitboxes}");
                }
            }
            Main.spriteBatch.End(); begun = false;
            device.SetRenderTargets(oldTargets);
            string suffix = hitboxes ? "hitboxes" : dark ? "dark" : "bright";
            using FileStream file = File.Create(Path.Combine(outputDirectory, $"boss-atlases-{suffix}.png"));
            target.SaveAsPng(file, width, height);
            File.WriteAllText(Path.Combine(outputDirectory, $"boss-atlases-{suffix}.csv"), manifest.ToString());
            return $"{suffix}=7 bosses×4 native poses×2 directions scale=1 cell=240x210";
        }

        finally
        {
            if (begun) Main.spriteBatch.End();
            device.SetRenderTargets(oldTargets);
            Main.screenPosition = oldScreen;
            Main.screenWidth = oldW; Main.screenHeight = oldH;
        }
    }

    private static void DrawOutline(Rectangle rect, Color color, int thickness)
    {
        Texture2D pixel = TextureAssets.MagicPixel.Value;
        Rectangle source = new(0, 0, 1, 1);
        Main.spriteBatch.Draw(pixel, new Rectangle(rect.Left, rect.Top, rect.Width, thickness), source, color);
        Main.spriteBatch.Draw(pixel, new Rectangle(rect.Left, rect.Bottom - thickness, rect.Width, thickness), source, color);
        Main.spriteBatch.Draw(pixel, new Rectangle(rect.Left, rect.Top, thickness, rect.Height), source, color);
        Main.spriteBatch.Draw(pixel, new Rectangle(rect.Right - thickness, rect.Top, thickness, rect.Height), source, color);
    }

    private static string RenderCollectibles(string outputDirectory)
    {
        const int width = 1180, height = 520;
        GraphicsDevice device = Main.graphics.GraphicsDevice;
        RenderTargetBinding[] oldTargets = device.GetRenderTargets();
        using RenderTarget2D target = new(device, width, height, false, SurfaceFormat.Color,
            DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
        var rows = new StringBuilder("kind,boss,itemType,textureSize\n");
        try
        {
            device.SetRenderTarget(target); device.Clear(new Color(25, 28, 36));
            Main.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp,
                DepthStencilState.None, RasterizerState.CullCounterClockwise);
            for (int row = 0; row < Entries.Length; row++)
            {
                var entry = Entries[row];
                DrawItem(entry.Mask, 82, 48 + row * 62, "mask", entry.Name);
                DrawItem(entry.Trophy, 430, 48 + row * 62, "trophy", entry.Name);
                DrawItem(entry.Relic, 780, 48 + row * 62, "relic", entry.Name);
            }
            Main.spriteBatch.End(); device.SetRenderTargets(oldTargets);
            using FileStream file = File.Create(Path.Combine(outputDirectory, "boss-collectibles-native-icons.png"));
            target.SaveAsPng(file, width, height);
            File.WriteAllText(Path.Combine(outputDirectory, "boss-collectibles-native-icons.csv"), rows.ToString());
            return "collectibles=7 masks+7 trophies+7 relics via native item texture assets";
        }
        finally
        {
            try { Main.spriteBatch.End(); } catch { }
            device.SetRenderTargets(oldTargets);
        }

        void DrawItem(int type, float x, float y, string kind, string name)
        {
            Texture2D texture = TextureAssets.Item[type].Value;
            float scale = Math.Min(1f, 42f / Math.Max(texture.Width, texture.Height));
            Rectangle source = new(0, 0, texture.Width, texture.Height);
            Main.spriteBatch.Draw(texture, new Vector2(x, y), source, Color.White, 0f,
                new Vector2(texture.Width, texture.Height) * 0.5f, scale, SpriteEffects.None, 0f);
            rows.AppendLine($"{kind},{name},{type},{texture.Width}x{texture.Height}");
        }
    }

    private static string RenderMasksWithNativePlayerRenderer(string outputDirectory)
    {
        const int width = 1600, height = 1870;
        GraphicsDevice device = Main.graphics.GraphicsDevice;
        Viewport oldViewport = device.Viewport;
        RenderTargetBinding[] oldTargets = device.GetRenderTargets();
        Player[] oldPlayers = (Player[])Main.player.Clone();
        int owner = Math.Clamp(Main.myPlayer, 0, Main.player.Length - 1);
        bool oldForceLighting = SmokeSystem.ForcePreviewLighting;
        bool oldMenu = Main.gameMenu;
        float oldUiScale = Main.UIScale;
        Vector2 oldZoom = Main.GameViewMatrix.Zoom;
        using RenderTarget2D target = new(device, width, height, false, SurfaceFormat.Color,
            DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
        var rows = new StringBuilder("boss,maskItem,headSlot,direction,pose,bodyFrame,renderer,armor\n");
        try
        {
            Main.gameMenu = false;
            SmokeSystem.ForcePreviewLighting = true;
            Main.UIScale = 1f;
            Main.GameViewMatrix.Zoom = Vector2.One;
            device.SetRenderTarget(target);
            var previewViewport = new Viewport(0, 0, width, height);
            device.Viewport = previewViewport;
            Main.GameViewMatrix.SetViewportOverride(previewViewport);
            device.Clear(new Color(35, 39, 48));
            Main.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp,
                DepthStencilState.None, RasterizerState.CullCounterClockwise);
            Player warm = SmokeSystem.CreatePreviewPlayer(ItemID.IronHelmet, ItemID.IronChainmail, ItemID.IronGreaves);
            warm.active = true; warm.dead = false; warm.whoAmI = owner;
            Main.player[owner] = warm;
            SmokeSystem.DrawPreviewPlayer(warm, -400, -400, 1f);
            Main.spriteBatch.End();
            device.Clear(new Color(35, 39, 48));
            Main.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp,
                DepthStencilState.None, RasterizerState.CullCounterClockwise);

            string[] poseNames = { "STAND", "RUN", "JUMP" };
            for (int pose = 0; pose < 3; pose++)
            for (int side = 0; side < 2; side++)
                ChatManager.DrawColorCodedStringWithShadow(Main.spriteBatch, FontAssets.MouseText.Value,
                    $"{poseNames[pose]} {(side == 0 ? "RIGHT" : "LEFT")}", new Vector2(145 + (pose * 2 + side) * 240, 8),
                    Color.White, 0f, Vector2.Zero, Vector2.One, 220f, 1f);
            for (int row = 0; row < Entries.Length; row++)
            {
                var entry = Entries[row];
                for (int pose = 0; pose < 3; pose++)
                for (int facingIndex = 0; facingIndex < 2; facingIndex++)
                {
                    int direction = facingIndex == 0 ? 1 : -1;
                    int col = pose * 2 + facingIndex;
                    Player actor = SmokeSystem.CreatePreviewPlayer(ItemID.None, ItemID.IronChainmail, ItemID.IronGreaves);
                    actor.active = true; actor.dead = false; actor.whoAmI = owner;
                    actor.direction = direction;
                    actor.velocity = pose == 1 ? new Vector2(direction * 4.6f, 0f)
                        : pose == 2 ? new Vector2(direction * 1.4f, -5f) : Vector2.Zero;
                    actor.controlJump = pose == 2;
                    actor.justJumped = pose == 2;
                    actor.armor[0].SetDefaults(entry.Mask);
                    actor.head = actor.armor[0].headSlot;
                    if (actor.head < 0)
                        throw new InvalidOperationException($"{entry.Name} mask has no registered head equip slot");
                    int bodyFrame = pose switch { 1 => 3, 2 => 5, _ => 0 };
                    actor.bodyFrame = new Rectangle(0, bodyFrame * 56, 40, 56);
                    actor.legFrame = new Rectangle(0, bodyFrame * 56, 40, 56);
                    Main.player[owner] = actor;
                    int x = 175 + col * 240;
                    int y = 36 + row * 260;
                    ChatManager.DrawColorCodedStringWithShadow(Main.spriteBatch, FontAssets.MouseText.Value,
                        entry.Name, new Vector2(x - 54, y), Color.White, 0f, Vector2.Zero, Vector2.One, 170f, 1f);
                    // The player renderer places the head well above its position
                    // anchor. Keep labels in a dedicated band above the full body.
                    SmokeSystem.DrawPreviewPlayer(actor, x, y + 145, 2f);
                    rows.AppendLine($"{entry.Name},{entry.Mask},{actor.head},{direction},{poseNames[pose]},{actor.bodyFrame.Y},native-PlayerRenderer,IronChest+Greaves,scale=2");
                }
            }
            Main.spriteBatch.End();
            device.SetRenderTargets(oldTargets);
            using FileStream file = File.Create(Path.Combine(outputDirectory, "boss-masks-worn-native-player-renderer.png"));
            target.SaveAsPng(file, width, height);
            File.WriteAllText(Path.Combine(outputDirectory, "boss-masks-worn-native-player-renderer.csv"), rows.ToString());
            return "masks=7 actually equipped in native PlayerRenderer, stand/run/jump x left/right=42/42, Iron chest/greaves, labels above heads";
        }
        finally
        {
            try { Main.spriteBatch.End(); } catch { }
            device.SetRenderTargets(oldTargets);
            device.Viewport = oldViewport;
            Main.GameViewMatrix.ClearViewportOverride();
            Main.player = oldPlayers;
            SmokeSystem.ForcePreviewLighting = oldForceLighting;
            Main.gameMenu = oldMenu;
            Main.UIScale = oldUiScale;
            Main.GameViewMatrix.Zoom = oldZoom;
        }
    }

    private static string RenderMaskHeadLayerAB(string outputDirectory)
    {
        const int width = 980, rowHeight = 220;
        int height = rowHeight * Entries.Length;
        GraphicsDevice device = Main.graphics.GraphicsDevice;
        Viewport oldViewport = device.Viewport;
        RenderTargetBinding[] oldTargets = device.GetRenderTargets();
        Player[] oldPlayers = (Player[])Main.player.Clone();
        int owner = Math.Clamp(Main.myPlayer, 0, Main.maxPlayers - 1);
        bool oldForceLighting = SmokeSystem.ForcePreviewLighting;
        bool oldMenu = Main.gameMenu;
        float oldUiScale = Main.UIScale;
        Vector2 oldZoom = Main.GameViewMatrix.Zoom;
        var changed = new List<(int Slot, bool Value)>();
        using RenderTarget2D target = new(device, width, height, false, SurfaceFormat.Color,
            DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
        var rows = new StringBuilder("boss,mask,DrawHead,armor,direction,pose\n");
        try
        {
            Main.gameMenu = false;
            SmokeSystem.ForcePreviewLighting = true;
            Main.UIScale = 1f;
            Main.GameViewMatrix.Zoom = Vector2.One;
            device.SetRenderTarget(target);
            var previewViewport = new Viewport(0, 0, width, height);
            device.Viewport = previewViewport;
            Main.GameViewMatrix.SetViewportOverride(previewViewport);
            device.Clear(new Color(35, 39, 48));
            Main.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp,
                DepthStencilState.None, RasterizerState.CullCounterClockwise);
            for (int row = 0; row < Entries.Length; row++)
            {
                var entry = Entries[row];
                Item mask = new();
                mask.SetDefaults(entry.Mask);
                int headSlot = mask.headSlot;
                bool initial = ArmorIDs.Head.Sets.DrawHead[headSlot];
                changed.Add((headSlot, initial));
                for (int side = 0; side < 2; side++)
                {
                    bool drawHead = side == 1;
                    ArmorIDs.Head.Sets.DrawHead[headSlot] = drawHead;
                    Player actor = SmokeSystem.CreatePreviewPlayer(ItemID.None, ItemID.IronChainmail, ItemID.IronGreaves);
                    actor.active = true; actor.dead = false; actor.whoAmI = owner;
                    actor.direction = 1;
                    actor.armor[0].SetDefaults(entry.Mask);
                    actor.head = headSlot;
                    actor.bodyFrame = new Rectangle(0, 0, 40, 56);
                    actor.legFrame = new Rectangle(0, 0, 40, 56);
                    Main.player[owner] = actor;
                    int anchorX = side == 0 ? 245 : 725;
                    int anchorY = row * rowHeight + 8;
                    string label = $"{entry.Name} · DrawHead={(drawHead ? "true" : "false")}";
                    ChatManager.DrawColorCodedStringWithShadow(Main.spriteBatch, FontAssets.MouseText.Value,
                        label, new Vector2(anchorX - 96, anchorY), Color.White, 0f, Vector2.Zero, Vector2.One, 320f, 1f);
                    SmokeSystem.DrawPreviewPlayer(actor, anchorX, anchorY + 142, 2f);
                    rows.AppendLine($"{entry.Name},{entry.Mask},{drawHead},IronChainmail+IronGreaves,right,stand");
                }
            }
            Main.spriteBatch.End();
            device.SetRenderTargets(oldTargets);
            using FileStream file = File.Create(Path.Combine(outputDirectory, "boss-mask-drawhead-neck-ab.png"));
            target.SaveAsPng(file, width, height);
            File.WriteAllText(Path.Combine(outputDirectory, "boss-mask-drawhead-neck-ab.csv"), rows.ToString());
            return "mask neck A/B=all seven masks in native PlayerRenderer with DrawHead false/true, same Iron body/legs";
        }
        finally
        {
            try { Main.spriteBatch.End(); } catch { }
            device.SetRenderTargets(oldTargets);
            device.Viewport = oldViewport;
            Main.GameViewMatrix.ClearViewportOverride();
            foreach ((int slot, bool value) in changed)
                ArmorIDs.Head.Sets.DrawHead[slot] = value;
            Main.player = oldPlayers;
            SmokeSystem.ForcePreviewLighting = oldForceLighting;
            Main.gameMenu = oldMenu;
            Main.UIScale = oldUiScale;
            Main.GameViewMatrix.Zoom = oldZoom;
        }
    }

    private static string RenderMaskArmorNeckMatrix(string outputDirectory)
    {
        const int leftMargin = 185, cellWidth = 110, rowHeight = 142;
        int width = leftMargin + 7 * 2 * cellWidth;
        int height = 15 * rowHeight;
        var armors = new (string Name, int Body, int Legs)[]
        {
            ("Milkcraft Iron", ModContent.ItemType<IronChestplate>(), ModContent.ItemType<IronGreaves>()),
            ("Milkcraft Leather", ModContent.ItemType<LeatherTunic>(), ModContent.ItemType<LeatherLeggings>()),
            ("Milkcraft Cloth", ModContent.ItemType<ClothRobe>(), ModContent.ItemType<ClothLeggings>()),
            ("Milkcraft Knight", ModContent.ItemType<KnightChestplate>(), ModContent.ItemType<KnightGreaves>()),
            ("Milkcraft Hunter", ModContent.ItemType<HuntingChestplate>(), ModContent.ItemType<HuntingLeggings>()),
            ("Milkcraft Energy", ModContent.ItemType<EnergyRobe>(), ModContent.ItemType<EnergyLeggings>()),
            ("Milkcraft Templar", ModContent.ItemType<TemplarChestplate>(), ModContent.ItemType<TemplarGreaves>()),
            ("Milkcraft Nightmare", ModContent.ItemType<NightmareChestplate>(), ModContent.ItemType<NightmareLeggings>()),
            ("Milkcraft Fallen", ModContent.ItemType<FallenRobe>(), ModContent.ItemType<FallenLeggings>()),
            ("Milkcraft Soulforged", ModContent.ItemType<SoulforgedChestplate>(), ModContent.ItemType<SoulforgedGreaves>()),
            ("Milkcraft Abyss", ModContent.ItemType<AbyssChestplate>(), ModContent.ItemType<AbyssLeggings>()),
            ("Milkcraft Dark", ModContent.ItemType<DarkRobe>(), ModContent.ItemType<DarkLeggings>()),
            ("Vanilla Iron", ItemID.IronChainmail, ItemID.IronGreaves),
            ("Vanilla Hallowed", ItemID.HallowedPlateMail, ItemID.HallowedGreaves),
            ("Vanilla Beetle", ItemID.BeetleScaleMail, ItemID.BeetleLeggings)
        };
        GraphicsDevice device = Main.graphics.GraphicsDevice;
        Viewport oldViewport = device.Viewport;
        RenderTargetBinding[] oldTargets = device.GetRenderTargets();
        Player[] oldPlayers = (Player[])Main.player.Clone();
        int owner = Math.Clamp(Main.myPlayer, 0, Main.maxPlayers - 1);
        bool oldForceLighting = SmokeSystem.ForcePreviewLighting;
        bool oldMenu = Main.gameMenu;
        float oldUiScale = Main.UIScale;
        Vector2 oldZoom = Main.GameViewMatrix.Zoom;
        using RenderTarget2D target = new(device, width, height, false, SurfaceFormat.Color,
            DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
        var rows = new StringBuilder("armor,bossMask,direction,DrawHead,bodyItem,legItem,native-PlayerRenderer\n");
        try
        {
            Main.gameMenu = false;
            SmokeSystem.ForcePreviewLighting = true;
            Main.UIScale = 1f;
            Main.GameViewMatrix.Zoom = Vector2.One;
            device.SetRenderTarget(target);
            var previewViewport = new Viewport(0, 0, width, height);
            device.Viewport = previewViewport;
            Main.GameViewMatrix.SetViewportOverride(previewViewport);
            device.Clear(new Color(35, 39, 48));
            Main.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp,
                DepthStencilState.None, RasterizerState.CullCounterClockwise);
            for (int armorIndex = 0; armorIndex < armors.Length; armorIndex++)
            {
                var armor = armors[armorIndex];
                int top = armorIndex * rowHeight;
                ChatManager.DrawColorCodedStringWithShadow(Main.spriteBatch, FontAssets.MouseText.Value,
                    armor.Name, new Vector2(4, top + 8), Color.White, 0f, Vector2.Zero, Vector2.One, leftMargin - 8, 1f);
                for (int maskIndex = 0; maskIndex < Entries.Length; maskIndex++)
                for (int side = 0; side < 2; side++)
                {
                    var maskEntry = Entries[maskIndex];
                    int direction = side == 0 ? 1 : -1;
                    Item mask = new();
                    mask.SetDefaults(maskEntry.Mask);
                    int headSlot = mask.headSlot;
                    if (headSlot < 0 || !ArmorIDs.Head.Sets.DrawHead[headSlot])
                        throw new InvalidOperationException($"{maskEntry.Name} did not enable the native head/neck layer for production");
                    Player actor = SmokeSystem.CreatePreviewPlayer(ItemID.None, armor.Body, armor.Legs);
                    actor.active = true; actor.dead = false; actor.whoAmI = owner; actor.direction = direction;
                    actor.armor[0].SetDefaults(maskEntry.Mask);
                    actor.head = headSlot;
                    actor.bodyFrame = new Rectangle(0, 0, 40, 56);
                    actor.legFrame = new Rectangle(0, 0, 40, 56);
                    Main.player[owner] = actor;
                    int col = maskIndex * 2 + side;
                    int anchorX = leftMargin + col * cellWidth + cellWidth / 2;
                    SmokeSystem.DrawPreviewPlayer(actor, anchorX, top + 132, 1.5f);
                    rows.AppendLine($"{armor.Name},{maskEntry.Name},{(direction > 0 ? "right" : "left")},true,{armor.Body},{armor.Legs},PASS");
                }
            }
            Main.spriteBatch.End();
            device.SetRenderTargets(oldTargets);
            using FileStream file = File.Create(Path.Combine(outputDirectory, "boss-mask-armor-neck-matrix.png"));
            target.SaveAsPng(file, width, height);
            File.WriteAllText(Path.Combine(outputDirectory, "boss-mask-armor-neck-matrix.csv"), rows.ToString());
            return $"mask-armor-neck-matrix=PASS {armors.Length} armor sets x {Entries.Length} masks x 2 directions={armors.Length * Entries.Length * 2}; DrawHead=true; native PlayerRenderer";
        }
        finally
        {
            try { Main.spriteBatch.End(); } catch { }
            device.SetRenderTargets(oldTargets);
            device.Viewport = oldViewport;
            Main.GameViewMatrix.ClearViewportOverride();
            Main.player = oldPlayers;
            SmokeSystem.ForcePreviewLighting = oldForceLighting;
            Main.gameMenu = oldMenu;
            Main.UIScale = oldUiScale;
            Main.GameViewMatrix.Zoom = oldZoom;
        }
    }

    private static string RenderMaskFullArmorPoseMatrix(string outputDirectory)
    {
        const int header = 44, leftMargin = 152, cellWidth = 112, rowHeight = 148;
        int width = leftMargin + 8 * cellWidth;
        int height = header + 15 * rowHeight;
        var armors = new (string Name, int Head, int Body, int Legs)[]
        {
            ("Milkcraft Iron", ModContent.ItemType<IronHelmet>(), ModContent.ItemType<IronChestplate>(), ModContent.ItemType<IronGreaves>()),
            ("Milkcraft Leather", ModContent.ItemType<LeatherHeadband>(), ModContent.ItemType<LeatherTunic>(), ModContent.ItemType<LeatherLeggings>()),
            ("Milkcraft Cloth", ModContent.ItemType<ClothHood>(), ModContent.ItemType<ClothRobe>(), ModContent.ItemType<ClothLeggings>()),
            ("Milkcraft Knight", ModContent.ItemType<KnightHelmet>(), ModContent.ItemType<KnightChestplate>(), ModContent.ItemType<KnightGreaves>()),
            ("Milkcraft Hunter", ModContent.ItemType<HuntingHood>(), ModContent.ItemType<HuntingChestplate>(), ModContent.ItemType<HuntingLeggings>()),
            ("Milkcraft Energy", ModContent.ItemType<EnergyHood>(), ModContent.ItemType<EnergyRobe>(), ModContent.ItemType<EnergyLeggings>()),
            ("Milkcraft Templar", ModContent.ItemType<TemplarHelmet>(), ModContent.ItemType<TemplarChestplate>(), ModContent.ItemType<TemplarGreaves>()),
            ("Milkcraft Nightmare", ModContent.ItemType<NightmareHood>(), ModContent.ItemType<NightmareChestplate>(), ModContent.ItemType<NightmareLeggings>()),
            ("Milkcraft Fallen", ModContent.ItemType<FallenHood>(), ModContent.ItemType<FallenRobe>(), ModContent.ItemType<FallenLeggings>()),
            ("Milkcraft Soulforged", ModContent.ItemType<SoulforgedHelmet>(), ModContent.ItemType<SoulforgedChestplate>(), ModContent.ItemType<SoulforgedGreaves>()),
            ("Milkcraft Abyss", ModContent.ItemType<AbyssHood>(), ModContent.ItemType<AbyssChestplate>(), ModContent.ItemType<AbyssLeggings>()),
            ("Milkcraft Dark", ModContent.ItemType<DarkHood>(), ModContent.ItemType<DarkRobe>(), ModContent.ItemType<DarkLeggings>()),
            ("Vanilla Iron", ItemID.IronHelmet, ItemID.IronChainmail, ItemID.IronGreaves),
            ("Vanilla Hallowed", ItemID.HallowedMask, ItemID.HallowedPlateMail, ItemID.HallowedGreaves),
            ("Vanilla Beetle", ItemID.BeetleHelmet, ItemID.BeetleScaleMail, ItemID.BeetleLeggings)
        };
        string[] poses = { "stand", "run", "jump", "staff-use" };
        GraphicsDevice device = Main.graphics.GraphicsDevice;
        Viewport oldViewport = device.Viewport;
        RenderTargetBinding[] oldTargets = device.GetRenderTargets();
        Player[] oldPlayers = (Player[])Main.player.Clone();
        int owner = Math.Clamp(Main.myPlayer, 0, Main.maxPlayers - 1);
        bool oldForceLighting = SmokeSystem.ForcePreviewLighting;
        bool oldMenu = Main.gameMenu;
        float oldUiScale = Main.UIScale;
        Vector2 oldZoom = Main.GameViewMatrix.Zoom;
        var totalCases = 0;
        try
        {
            Main.gameMenu = false;
            SmokeSystem.ForcePreviewLighting = true;
            Main.UIScale = 1f;
            Main.GameViewMatrix.Zoom = Vector2.One;
            for (int maskIndex = 0; maskIndex < Entries.Length; maskIndex++)
            {
                var maskEntry = Entries[maskIndex];
                Item mask = new(); mask.SetDefaults(maskEntry.Mask);
                int maskSlot = mask.headSlot;
                if (maskSlot < 0 || !ArmorIDs.Head.Sets.DrawHead[maskSlot])
                    throw new InvalidOperationException($"{maskEntry.Name} did not enable native head/neck rendering");
                using RenderTarget2D target = new(device, width, height, false, SurfaceFormat.Color,
                    DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
                var csv = new StringBuilder("armor,bossMask,direction,pose,actualHead,vanityMask,body,legs\n");
                device.SetRenderTarget(target);
                var previewViewport = new Viewport(0, 0, width, height);
                device.Viewport = previewViewport;
                Main.GameViewMatrix.SetViewportOverride(previewViewport);
                device.Clear(new Color(35, 39, 48));
                // SpriteDrawBuffer uses the native pixel-shader viewport transform.
                // Flush a native player draw once for each newly bound target so the
                // first armor page does not inherit the prior target's projection.
                Main.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp,
                    DepthStencilState.None, RasterizerState.CullCounterClockwise);
                Player warm = SmokeSystem.CreatePreviewPlayer(armors[0].Head, armors[0].Body, armors[0].Legs);
                warm.active = true; warm.dead = false; warm.whoAmI = owner; warm.Male = true;
                Main.player[owner] = warm;
                SmokeSystem.DrawPreviewPlayer(warm, -400, -400, 1f);
                Main.spriteBatch.End();
                device.Clear(new Color(35, 39, 48));
                Main.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp,
                    DepthStencilState.None, RasterizerState.CullCounterClockwise);
                ChatManager.DrawColorCodedStringWithShadow(Main.spriteBatch, FontAssets.MouseText.Value,
                    $"{maskEntry.Name}: complete armor + vanity mask", new Vector2(6, 5), Color.White,
                    0f, Vector2.Zero, Vector2.One, width - 12, 1f);
                for (int col = 0; col < 8; col++)
                {
                    int side = col / poses.Length, pose = col % poses.Length;
                    ChatManager.DrawColorCodedStringWithShadow(Main.spriteBatch, FontAssets.MouseText.Value,
                        $"{(side == 0 ? "R" : "L")} {poses[pose]}",
                        new Vector2(leftMargin + col * cellWidth + 4, 25), Color.White,
                        0f, Vector2.Zero, Vector2.One, cellWidth - 6, 1f);
                }
                for (int row = 0; row < armors.Length; row++)
                {
                    var armor = armors[row];
                    int top = header + row * rowHeight;
                    ChatManager.DrawColorCodedStringWithShadow(Main.spriteBatch, FontAssets.MouseText.Value,
                        armor.Name, new Vector2(4, top + 16), Color.White, 0f, Vector2.Zero, Vector2.One, leftMargin - 8, 1f);
                    for (int side = 0; side < 2; side++)
                    for (int pose = 0; pose < poses.Length; pose++)
                    {
                        int direction = side == 0 ? 1 : -1;
                        Player actor = pose == 3
                            ? SmokeSystem.CreateActionPlayer(armor.Head, armor.Body, armor.Legs,
                                ItemID.AmethystStaff, direction, direction > 0 ? 0.1f : MathHelper.Pi - 0.1f, true, pose: 3)
                            : SmokeSystem.CreatePreviewPlayer(armor.Head, armor.Body, armor.Legs,
                                walk: pose == 1, jump: pose == 2);
                        actor.active = true; actor.dead = false; actor.whoAmI = owner; actor.Male = true;
                        actor.direction = direction;
                        actor.isDisplayDollOrInanimate = false;
                        if (pose == 1) actor.velocity = new Vector2(direction * 2.2f, 0f);
                        if (pose == 2)
                        {
                            actor.velocity = new Vector2(direction, -5f);
                            actor.controlJump = true; actor.justJumped = true; actor.jump = 1;
                        }
                        actor.armor[10].SetDefaults(maskEntry.Mask);
                        actor.head = maskSlot;
                        actor.oldVelocity = actor.velocity;
                        actor.PlayerFrame();
                        Main.player[owner] = actor;
                        int col = side * poses.Length + pose;
                        int x = leftMargin + col * cellWidth + cellWidth / 2;
                        // Leave room for the full native sprite (including held-item offsets)
                        // inside this armor row; the old +140 anchor clipped the final row.
                        SmokeSystem.DrawPreviewPlayer(actor, x, top + 85, 1.5f);
                        csv.AppendLine($"{armor.Name},{maskEntry.Name},{(direction > 0 ? "right" : "left")},{poses[pose]},{armor.Head},{maskEntry.Mask},{armor.Body},{armor.Legs}");
                        totalCases++;
                    }
                }
                Main.spriteBatch.End();
                device.SetRenderTargets(oldTargets);
                using FileStream file = File.Create(Path.Combine(outputDirectory, $"boss-mask-full-armor-poses-{maskEntry.Name}.png"));
                target.SaveAsPng(file, width, height);
                File.WriteAllText(Path.Combine(outputDirectory, $"boss-mask-full-armor-poses-{maskEntry.Name}.csv"), csv.ToString());
            }
            return $"mask-full-armor-poses=PASS {Entries.Length} masks x {armors.Length} complete head/body/legs sets x 4 poses x 2 directions={totalCases}; masks in vanity armor[10]";
        }
        finally
        {
            try { Main.spriteBatch.End(); } catch { }
            device.SetRenderTargets(oldTargets);
            device.Viewport = oldViewport;
            Main.GameViewMatrix.ClearViewportOverride();
            Main.player = oldPlayers;
            SmokeSystem.ForcePreviewLighting = oldForceLighting;
            Main.gameMenu = oldMenu;
            Main.UIScale = oldUiScale;
            Main.GameViewMatrix.Zoom = oldZoom;
        }
    }

    private static string RenderNativeDisplayTiles(string outputDirectory)
    {
        const int mapWidth = 380, mapHeight = 82;
        const int width = 960, height = 760, outputWidth = 1920, outputHeight = 1520;
        const int trophyY = 12, relicY = 52, floorY = 56;
        Tilemap oldTiles = Main.tile;
        int oldMaxX = Main.maxTilesX, oldMaxY = Main.maxTilesY;
        bool oldGen = WorldGen.gen, oldGenerating = WorldGen.generatingWorld, oldNoActions = WorldGen.noTileActions, oldDestroy = WorldGen.destroyObject;
        bool oldMenu = Main.gameMenu;
        int oldNetMode = Main.netMode;
        Item[] oldItems = Main.item;
        UnifiedRandom oldRandom = Main.rand;
        bool oldPreviewLighting = SmokeSystem.ForcePreviewLighting;
        FieldInfo engineField = typeof(Lighting).GetField("_activeEngine", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Lighting._activeEngine unavailable for scoped tile-light fixture");
        object oldEngine = engineField.GetValue(null)!;
        Vector2 oldScreenPosition = Main.screenPosition;
        bool oldDrawToScreen = Main.drawToScreen;
        int oldOffscreenRange = Main.offScreenRange;
        GraphicsDevice device = Main.graphics.GraphicsDevice;
        RenderTargetBinding[] oldTargets = device.GetRenderTargets();
        using RenderTarget2D target = new(device, width, height, false, SurfaceFormat.Color,
            DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
        using RenderTarget2D outputTarget = new(device, outputWidth, outputHeight, false, SurfaceFormat.Color,
            DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
        var log = new StringBuilder("Seven native trophies and fourteen relics: each relic is placed on solid stone and platform supports, then all objects are broken with native WorldGen methods in a disposable Tilemap; rendered at native 1x and enlarged together 2x.\n");
        try
        {
            Main.tile = (Tilemap)Activator.CreateInstance(typeof(Tilemap),
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null,
                new object[] { (ushort)mapWidth, (ushort)mapHeight }, null)!;
            Main.maxTilesX = mapWidth; Main.maxTilesY = mapHeight;
            WorldGen.gen = false; WorldGen.generatingWorld = false; WorldGen.noTileActions = false; WorldGen.destroyObject = false;
            Main.gameMenu = false; Main.netMode = NetmodeID.SinglePlayer;
            SmokeSystem.ForcePreviewLighting = true;
            engineField.SetValue(null, new BrightFixtureLightingEngine());
            Main.screenPosition = Vector2.Zero;
            Main.drawToScreen = true;
            Main.offScreenRange = 0;

            for (int index = 0; index < Entries.Length; index++)
            {
                var entry = Entries[index];
                int x = 10 + index * 50;
                Main.instance.LoadTiles(entry.TrophyTile);
                Main.instance.LoadTiles(entry.RelicTile);
                for (int dx = 0; dx < 3; dx++)
                for (int dy = 0; dy < 3; dy++)
                {
                    Tile backing = Main.tile[x + dx, trophyY + dy];
                    backing.WallType = WallID.Wood;
                }
                Main.instance.LoadTiles(TileID.Stone);
                Main.instance.LoadTiles(TileID.Platforms);
                for (int dx = 0; dx < 3; dx++)
                {
                    WorldGen.PlaceTile(x + dx, floorY, TileID.Stone, true, true);
                    WorldGen.PlaceTile(x + 10 + dx, floorY, TileID.Platforms, true, true);
                    Tile stoneSupport = Main.tile[x + dx, floorY];
                    Tile platformSupport = Main.tile[x + 10 + dx, floorY];
                    if (!stoneSupport.HasTile || stoneSupport.TileType != TileID.Stone
                        || !platformSupport.HasTile || platformSupport.TileType != TileID.Platforms)
                        throw new InvalidOperationException($"native relic support placement failed for {entry.Name} at {x + dx}/{x + 10 + dx},{floorY}");
                }
                if (!WorldGen.PlaceObject(x + 1, trophyY + 2, entry.TrophyTile, true, 0))
                    throw new InvalidOperationException($"native trophy WorldGen.PlaceObject failed: {entry.Name}");
                if (!WorldGen.PlaceObject(x + 1, relicY + 3, entry.RelicTile, true, 0))
                    throw new InvalidOperationException($"native stone-supported relic WorldGen.PlaceObject failed: {entry.Name}");
                if (!WorldGen.PlaceObject(x + 11, relicY + 3, entry.RelicTile, true, 0))
                    throw new InvalidOperationException($"native platform-supported relic WorldGen.PlaceObject failed: {entry.Name}");
                int trophyTopX = -1, trophyTopY = -1, relicTopX = -1, relicTopY = -1, platformRelicTopX = -1, platformRelicTopY = -1;
                for (int dx = 0; dx < 3; dx++)
                for (int dy = 0; dy < 4; dy++)
                {
                    Tile trophyCell = Main.tile[x + dx, trophyY + dy];
                    if (trophyCell.HasTile && trophyCell.TileType == entry.TrophyTile)
                    { trophyTopX = trophyTopX < 0 ? x + dx : Math.Min(trophyTopX, x + dx); trophyTopY = trophyTopY < 0 ? trophyY + dy : Math.Min(trophyTopY, trophyY + dy); }
                    Tile relicCell = Main.tile[x + dx, relicY + dy];
                    if (relicCell.HasTile && relicCell.TileType == entry.RelicTile)
                    { relicTopX = relicTopX < 0 ? x + dx : Math.Min(relicTopX, x + dx); relicTopY = relicTopY < 0 ? relicY + dy : Math.Min(relicTopY, relicY + dy); }
                    Tile platformRelicCell = Main.tile[x + 10 + dx, relicY + dy];
                    if (platformRelicCell.HasTile && platformRelicCell.TileType == entry.RelicTile)
                    { platformRelicTopX = platformRelicTopX < 0 ? x + 10 + dx : Math.Min(platformRelicTopX, x + 10 + dx); platformRelicTopY = platformRelicTopY < 0 ? relicY + dy : Math.Min(platformRelicTopY, relicY + dy); }
                }
                if (trophyTopX < 0 || relicTopX < 0 || platformRelicTopX < 0)
                    throw new InvalidOperationException($"placed display cells not found for {entry.Name}");
                log.AppendLine($"{entry.Name} trophy={trophyTopX},{trophyTopY} 3x3 wall; stoneRelic={relicTopX},{relicTopY} platformRelic={platformRelicTopX},{platformRelicTopY} 3x4; supportY={floorY}, statueBottomY={relicTopY + 4}; native-place=PASS");
            }

            // The disposable Tilemap has no world light cache. A scoped native lighting
            // engine supplies a neutral bright value through Lighting.GetColor only for
            // this diagnostic; production PostDraw remains untouched.
            Color sampleLight = Lighting.GetColor(11, 55);
            if (Math.Max(sampleLight.R, Math.Max(sampleLight.G, sampleLight.B)) < 100)
                throw new InvalidOperationException($"native bright tile-light fixture stayed dark: {sampleLight}");
            log.AppendLine($"native-lighting=Lighting.GetColor scoped diagnostic light sample={sampleLight}");

            device.SetRenderTarget(target);
            device.Clear(new Color(29, 33, 42));
            Main.drawToScreen = true;
            Main.offScreenRange = 0;
            Main.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp,
                DepthStencilState.None, RasterizerState.CullCounterClockwise);
            for (int index = 0; index < Entries.Length; index++)
            {
                var entry = Entries[index];
                int x = 10 + index * 50;
                int trophyTopX = x, trophyTopY = 12, relicTopX = x, relicTopY = 52;
                int dstTrophyX = 25 + index * 130, dstTrophyY = 28;
                int dstRelicX = dstTrophyX, stoneY = 130, platformY = 310;
                DrawPlacedObject(entry.TrophyTile, trophyTopX, trophyTopY, 3, 3, dstTrophyX, dstTrophyY);
                DrawPlacedObject(entry.RelicTile, relicTopX, relicTopY, 3, 4, dstRelicX, stoneY);
                DrawPlacedObject(entry.RelicTile, x + 10, relicTopY, 3, 4, dstRelicX, platformY);
                ChatManager.DrawColorCodedStringWithShadow(Main.spriteBatch, FontAssets.MouseText.Value,
                    entry.Name, new Vector2(dstTrophyX, 9), Color.White, 0f, Vector2.Zero, Vector2.One, 115f, 1f);

                // Run the actual relic ModTile.PostDraw for the top-left tile.
                Main.screenPosition = new Vector2(relicTopX * 16f + 24f - (dstRelicX + 24f),
                    relicTopY * 16f + 48f - (stoneY + 48f));
                TileLoader.GetTile(entry.RelicTile)!.PostDraw(relicTopX, relicTopY, Main.spriteBatch);
                DrawSupportRow(x, floorY, dstRelicX, stoneY + 64);
                Main.screenPosition = new Vector2((x + 10) * 16f + 24f - (dstRelicX + 24f),
                    relicTopY * 16f + 48f - (platformY + 48f));
                TileLoader.GetTile(entry.RelicTile)!.PostDraw(x + 10, relicTopY, Main.spriteBatch);
                DrawSupportRow(x + 10, floorY, dstRelicX, platformY + 64);

                // Render the same native PostDraw under the normal offscreen tile pass.
                const int offscreenY = 490;
                int offscreenRange = 40;
                Main.drawToScreen = false;
                Main.offScreenRange = offscreenRange;
                DrawPlacedObject(entry.RelicTile, relicTopX, relicTopY, 3, 4, dstRelicX, offscreenY);
                Main.screenPosition = new Vector2(relicTopX * 16f + 24f - (dstRelicX + 24f) + offscreenRange,
                    relicTopY * 16f + 48f - (offscreenY + 48f) + offscreenRange);
                TileLoader.GetTile(entry.RelicTile)!.PostDraw(relicTopX, relicTopY, Main.spriteBatch);
                DrawSupportRow(x, floorY, dstRelicX, offscreenY + 64);
                const int offscreenPlatformY = 670;
                DrawPlacedObject(entry.RelicTile, x + 10, relicTopY, 3, 4, dstRelicX, offscreenPlatformY);
                Main.screenPosition = new Vector2((x + 10) * 16f + 24f - (dstRelicX + 24f) + offscreenRange,
                    relicTopY * 16f + 48f - (offscreenPlatformY + 48f) + offscreenRange);
                TileLoader.GetTile(entry.RelicTile)!.PostDraw(x + 10, relicTopY, Main.spriteBatch);
                DrawSupportRow(x + 10, floorY, dstRelicX, offscreenPlatformY + 64);
                Main.drawToScreen = true;
                Main.offScreenRange = 0;
                Main.screenPosition = Vector2.Zero;
            }
            Main.spriteBatch.End();

            device.SetRenderTarget(outputTarget);
            device.Clear(new Color(29, 33, 42));
            Main.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp,
                DepthStencilState.None, RasterizerState.CullCounterClockwise);
            Main.spriteBatch.Draw(target, new Rectangle(0, 0, outputWidth, outputHeight), Color.White);
            Main.spriteBatch.End();
            device.SetRenderTargets(oldTargets);
            using (FileStream file = File.Create(Path.Combine(outputDirectory, "boss-trophies-relics-native-placed-tiles.png")))
                outputTarget.SaveAsPng(file, outputWidth, outputHeight);

            // Compare the statue pixels drawn with and without tile offscreen compensation.
            int comparedPixels = 0;
            for (int index = 0; index < Entries.Length; index++)
            {
                int dstX = 25 + index * 130;
                foreach ((int onY, int offY, string supportName) in new[] { (130, 490, "stone"), (310, 670, "platform") })
                {
                    // Compare the complete native PostDraw sprite, atlas base and
                    // support row. Each render row has enough vertical clearance to
                    // avoid overlap with the neighboring statue.
                    Rectangle onscreen = new(dstX - 12, onY - 16, 72, 96);
                    Rectangle offscreen = new(dstX - 12, offY - 16, 72, 96);
                    Color[] onPixels = new Color[onscreen.Width * onscreen.Height];
                    Color[] offPixels = new Color[offscreen.Width * offscreen.Height];
                    target.GetData(0, onscreen, onPixels, 0, onPixels.Length);
                    target.GetData(0, offscreen, offPixels, 0, offPixels.Length);
                    if (!onPixels.SequenceEqual(offPixels))
                    {
                        int firstDiff = Enumerable.Range(0, onPixels.Length).First(pixel => onPixels[pixel] != offPixels[pixel]);
                        log.AppendLine($"relic-display-diff name={Entries[index].Name}/{supportName} first=({firstDiff % onscreen.Width},{firstDiff / onscreen.Width}) on={onPixels[firstDiff]} off={offPixels[firstDiff]} total={onPixels.Zip(offPixels).Count(pair => pair.First != pair.Second)}");
                        throw new InvalidOperationException($"relic {Entries[index].Name}/{supportName} PostDraw differs between drawToScreen and offscreen-range passes; first=({firstDiff % onscreen.Width},{firstDiff / onscreen.Width}) on={onPixels[firstDiff]} off={offPixels[firstDiff]} total={onPixels.Zip(offPixels).Count(pair => pair.First != pair.Second)}");
                    }
                    int illuminated = onPixels.Count(pixel => pixel.R > 100 && pixel.G > 90 && pixel.B > 60);
                    if (illuminated < 100)
                        throw new InvalidOperationException($"relic {Entries[index].Name}/{supportName} statue pixels are not visibly lit in fixture: illuminated={illuminated}");
                    comparedPixels += onPixels.Length;
                }
            }
            Main.item = Enumerable.Range(0, Math.Max(Main.maxItems + 1, oldItems.Length)).Select(_ => new Item()).ToArray();
            int broken = 0;
            for (int index = 0; index < Entries.Length; index++)
            {
                var entry = Entries[index];
                int x = 10 + index * 50;
                WorldGen.KillTile(x + 1, trophyY + 1, false, false, false);
                WorldGen.KillTile(x + 1, relicY + 2, false, false, false);
                WorldGen.KillTile(x + 11, relicY + 2, false, false, false);
                int trophyItems = Main.item.Where(item => item != null && item.active && item.type == entry.Trophy).Sum(item => item.stack);
                int relicItems = Main.item.Where(item => item != null && item.active && item.type == entry.Relic).Sum(item => item.stack);
                if (trophyItems != 1 || relicItems != 2)
                    throw new InvalidOperationException($"native break {entry.Name} trophy/relic drops={trophyItems}/{relicItems}, expected 1/2");
                for (int dx = 0; dx < 3; dx++)
                for (int dy = 0; dy < 4; dy++)
                {
                    if (Main.tile[x + dx, trophyY + dy].HasTile && Main.tile[x + dx, trophyY + dy].TileType == entry.TrophyTile)
                        throw new InvalidOperationException($"native trophy break left tile footprint: {entry.Name}");
                    if (Main.tile[x + dx, relicY + dy].HasTile && Main.tile[x + dx, relicY + dy].TileType == entry.RelicTile)
                        throw new InvalidOperationException($"native relic break left tile footprint: {entry.Name}");
                    if (Main.tile[x + 10 + dx, relicY + dy].HasTile && Main.tile[x + 10 + dx, relicY + dy].TileType == entry.RelicTile)
                        throw new InvalidOperationException($"native platform relic break left tile footprint: {entry.Name}");
                }
                for (int dx = 0; dx < 3; dx++)
                {
                    Tile stoneSupport = Main.tile[x + dx, floorY];
                    Tile platformSupport = Main.tile[x + 10 + dx, floorY];
                    if (!stoneSupport.HasTile || stoneSupport.TileType != TileID.Stone
                        || !platformSupport.HasTile || platformSupport.TileType != TileID.Platforms)
                        throw new InvalidOperationException($"native relic break damaged stone/platform support for {entry.Name}");
                }
                broken += 3;
                log.AppendLine($"{entry.Name} native-break=PASS trophy×1 relic×2 stone+platform footprints cleared; both supports preserved");
            }
            File.WriteAllText(Path.Combine(outputDirectory, "boss-trophies-relics-native-placed-tiles.log"), log.ToString());
            return $"displayTiles=WorldGen.PlaceObject+KillTile native trophies=7/7 relics=14/14 (each on stone+platform); relic PostDraw drawToScreen/offscreen equal pixels={comparedPixels}; native-break drops={broken}/21";

            void DrawPlacedObject(int type, int startX, int startY, int cellW, int cellH, int dstX, int dstY)
            {
                Texture2D texture = TextureAssets.Tile[type].Value;
                for (int dy = 0; dy < cellH; dy++)
                for (int dx = 0; dx < cellW; dx++)
                {
                    Tile placed = Main.tile[startX + dx, startY + dy];
                    if (!placed.HasTile || placed.TileType != type)
                        throw new InvalidOperationException($"tile footprint hole {type} at {startX + dx},{startY + dy}");
                    Rectangle source = new(placed.TileFrameX, placed.TileFrameY, 16, 16);
                    if (source.Right > texture.Width || source.Bottom > texture.Height)
                        throw new InvalidOperationException($"native tile frame out of atlas {type}: {source} in {texture.Width}x{texture.Height}");
                    Rectangle dest = new(dstX + dx * 16, dstY + dy * 16, 16, 16);
                    Main.spriteBatch.Draw(texture, dest, source, Color.White);
                }
            }

            void DrawSupportRow(int startX, int tileY, int dstX, int dstY)
            {
                for (int dx = 0; dx < 3; dx++)
                {
                    Tile support = Main.tile[startX + dx, tileY];
                    if (!support.HasTile)
                        throw new InvalidOperationException($"missing relic support tile at {startX + dx},{tileY}");
                    Texture2D texture = TextureAssets.Tile[support.TileType].Value;
                    Rectangle source = new(support.TileFrameX, support.TileFrameY, 16, 16);
                    if (source.Right > texture.Width || source.Bottom > texture.Height)
                        throw new InvalidOperationException($"support frame out of atlas tile={support.TileType} frame={source} atlas={texture.Width}x{texture.Height}");
                    if (dx == 0)
                    {
                        Color[] sample = new Color[16 * 16];
                        texture.GetData(0, source, sample, 0, sample.Length);
                        log.AppendLine($"support-sample tile={support.TileType} frame={source} atlas={texture.Width}x{texture.Height} opaque={sample.Count(pixel => pixel.A > 0)} first={sample.FirstOrDefault(pixel => pixel.A > 0)}");
                    }
                    Rectangle destination = new(dstX + dx * 16, dstY, 16, 16);
                    Main.spriteBatch.Draw(texture, destination, source, Color.White);
                }
            }
        }
        finally
        {
            try { Main.spriteBatch.End(); } catch { }
            device.SetRenderTargets(oldTargets);
            Main.tile = oldTiles; Main.maxTilesX = oldMaxX; Main.maxTilesY = oldMaxY;
            WorldGen.gen = oldGen; WorldGen.generatingWorld = oldGenerating; WorldGen.noTileActions = oldNoActions; WorldGen.destroyObject = oldDestroy;
            Main.gameMenu = oldMenu; Main.netMode = oldNetMode; Main.item = oldItems; Main.rand = oldRandom;
            engineField.SetValue(null, oldEngine);
            SmokeSystem.ForcePreviewLighting = oldPreviewLighting;
            Main.screenPosition = oldScreenPosition; Main.drawToScreen = oldDrawToScreen; Main.offScreenRange = oldOffscreenRange;
        }
    }

    private static string RenderEnchantingPedestalSupports(string outputDirectory)
    {
        const int mapWidth = 96, mapHeight = 64, targetWidth = 512, targetHeight = 190;
        const int leftX = 12, rightX = 48, topY = 28, floorY = topY + 3;
        int tileType = ModContent.TileType<MilkcraftEnchantingTableTile>();
        Tilemap oldTiles = Main.tile;
        int oldMaxX = Main.maxTilesX, oldMaxY = Main.maxTilesY;
        bool oldGen = WorldGen.gen, oldGenerating = WorldGen.generatingWorld, oldNoActions = WorldGen.noTileActions;
        bool oldDestroy = WorldGen.destroyObject, oldMenu = Main.gameMenu, oldDrawToScreen = Main.drawToScreen;
        int oldNet = Main.netMode, oldOffscreen = Main.offScreenRange;
        Vector2 oldScreen = Main.screenPosition;
        bool oldPreviewLighting = SmokeSystem.ForcePreviewLighting;
        FieldInfo engineField = typeof(Lighting).GetField("_activeEngine", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Lighting._activeEngine unavailable for enchanting pedestal preview");
        object oldEngine = engineField.GetValue(null)!;
        GraphicsDevice device = Main.graphics.GraphicsDevice;
        RenderTargetBinding[] oldTargets = device.GetRenderTargets();
        using RenderTarget2D target = new(device, targetWidth, targetHeight, false, SurfaceFormat.Color, DepthFormat.None);
        try
        {
            Main.tile = (Tilemap)Activator.CreateInstance(typeof(Tilemap), BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null, new object[] { (ushort)mapWidth, (ushort)mapHeight }, null)!;
            Main.maxTilesX = mapWidth; Main.maxTilesY = mapHeight;
            WorldGen.gen = false; WorldGen.generatingWorld = false; WorldGen.noTileActions = false; WorldGen.destroyObject = false;
            Main.gameMenu = false; Main.netMode = NetmodeID.SinglePlayer; Main.drawToScreen = true; Main.offScreenRange = 0;
            SmokeSystem.ForcePreviewLighting = true;
            engineField.SetValue(null, new BrightFixtureLightingEngine());
            Main.instance.LoadTiles(tileType);
            Main.instance.LoadTiles(TileID.Stone);
            Main.instance.LoadTiles(TileID.Platforms);

            foreach ((int x, int support) in new[] { (leftX, TileID.Stone), (rightX, TileID.Platforms) })
            {
                for (int dx = 0; dx < 3; dx++)
                    if (!WorldGen.PlaceTile(x + dx, floorY, support, true, true))
                        throw new InvalidOperationException($"enchanting table support placement failed tile={support} at {x + dx},{floorY}");
                // The tile origin is the bottom-centre cell, immediately above the support row.
                if (!WorldGen.PlaceObject(x + 1, floorY - 1, tileType, true, 0))
                    throw new InvalidOperationException($"native enchanting-table PlaceObject failed on support tile={support}");
                for (int dx = 0; dx < 3; dx++)
                for (int dy = 0; dy < 3; dy++)
                {
                    Tile placed = Main.tile[x + dx, topY + dy];
                    if (!placed.HasTile || placed.TileType != tileType)
                        throw new InvalidOperationException($"native enchanting-table footprint missing at {x + dx},{topY + dy}");
                }
            }

            device.SetRenderTarget(target);
            device.Clear(new Color(29, 33, 42));
            Main.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp,
                DepthStencilState.None, RasterizerState.CullCounterClockwise);
            DrawStation(leftX, 46, "STONE FLOOR");
            DrawStation(rightX, 266, "PLATFORM FLOOR");
            Main.spriteBatch.End();
            device.SetRenderTargets(oldTargets);
            using (FileStream file = File.Create(Path.Combine(outputDirectory, "enchanting-pedestal-native-supports.png")))
                target.SaveAsPng(file, targetWidth, targetHeight);
            File.WriteAllText(Path.Combine(outputDirectory, "enchanting-pedestal-native-supports.log"),
                $"WorldGen.PlaceObject native 3x3 tile; real support rows at y={floorY}; lifted cube drawY=-12; pedestal drawY=39..48; stone and platform supports verified.\n");
            return "enchanting-pedestal=WorldGen.PlaceObject native 3x3 on stone/platform; lifted cube+46x9 base rendered at game scale";

            void DrawStation(int worldX, int screenX, string label)
            {
                Main.screenPosition = new Vector2(worldX * 16f - screenX, topY * 16f - 64f);
                // PreDraw replaces the native nine-cell art with one lifted cube atlas.
                TileLoader.GetTile(tileType)!.PreDraw(worldX, topY, Main.spriteBatch);
                TileLoader.GetTile(tileType)!.PostDraw(worldX, topY, Main.spriteBatch);
                Texture2D supportTexture = TextureAssets.Tile[Main.tile[worldX, floorY].TileType].Value;
                for (int dx = 0; dx < 3; dx++)
                {
                    Tile support = Main.tile[worldX + dx, floorY];
                    Rectangle source = new(support.TileFrameX, support.TileFrameY, 16, 16);
                    Main.spriteBatch.Draw(supportTexture, new Rectangle(screenX + dx * 16, 112, 16, 16), source, Color.White);
                }
                ChatManager.DrawColorCodedStringWithShadow(Main.spriteBatch, FontAssets.MouseText.Value,
                    label, new Vector2(screenX - 4, 2), Color.White, 0f, Vector2.Zero, Vector2.One, 210f, 0.72f);
            }
        }
        finally
        {
            try { Main.spriteBatch.End(); } catch { }
            device.SetRenderTargets(oldTargets);
            Main.tile = oldTiles; Main.maxTilesX = oldMaxX; Main.maxTilesY = oldMaxY;
            WorldGen.gen = oldGen; WorldGen.generatingWorld = oldGenerating; WorldGen.noTileActions = oldNoActions;
            WorldGen.destroyObject = oldDestroy; Main.gameMenu = oldMenu; Main.netMode = oldNet;
            Main.drawToScreen = oldDrawToScreen; Main.offScreenRange = oldOffscreen; Main.screenPosition = oldScreen;
            SmokeSystem.ForcePreviewLighting = oldPreviewLighting; engineField.SetValue(null, oldEngine);
        }
    }
}
