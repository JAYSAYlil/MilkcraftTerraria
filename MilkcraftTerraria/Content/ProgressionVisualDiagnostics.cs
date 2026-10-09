using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.DataStructures;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;
using Terraria.Utilities;
using Terraria.WorldBuilding;
using Terraria.GameContent.Generation;
using MilkcraftTerraria.Content.Items;
using MilkcraftTerraria.Content.Tiles;

namespace MilkcraftTerraria.Content;

/// <summary>Separate native visual and ore smoke so Boss/Enchant fixtures cannot block review images.</summary>
internal static class ProgressionVisualDiagnostics
{
    private sealed record Icon(string Name, int Type);
    private static Icon[] Icons => new[]
    {
        new Icon("黑铁矿石", ModContent.ItemType<BlackIronOre>()),
        new Icon("粉钻矿", ModContent.ItemType<PinkDiamondOre>()),
        new Icon("粉钻", ModContent.ItemType<PinkDiamond>()),
        new Icon("附魔之心", ModContent.ItemType<EnchantmentHeart>()),
        new Icon("真理之剑", ModContent.ItemType<TruthSword>()),
        new Icon("真理之弓", ModContent.ItemType<TruthBow>()),
        new Icon("真理之杖", ModContent.ItemType<TruthStaff>()),
        new Icon("水晶颅骨", ModContent.ItemType<CrystalSkull>()),
        new Icon("异种角质", ModContent.ItemType<AlienKeratin>()),
        new Icon("奥术核心", ModContent.ItemType<ArcaneCore>()),
        new Icon("破碎的剑柄", ModContent.ItemType<BrokenSwordHilt>()),
        new Icon("破碎的弓弦", ModContent.ItemType<BrokenBowstring>()),
        new Icon("破碎的杖柄", ModContent.ItemType<BrokenStaffHandle>()),
        new Icon("凋零之泪", ModContent.ItemType<WitherTear>()),
        new Icon("恶魔之翼", ModContent.ItemType<DemonWing>()),
        new Icon("附魔胫骨", ModContent.ItemType<EnchantedShinbone>()),
        new Icon("圣殿药水", ModContent.ItemType<TemplarPotion>()),
        new Icon("梦魇药水", ModContent.ItemType<NightmarePotion>()),
        new Icon("堕落药水", ModContent.ItemType<FallenPotion>()),
        new Icon("魂铸药水", ModContent.ItemType<SoulforgedPotion>()),
        new Icon("深渊药水", ModContent.ItemType<AbyssPotion>()),
        new Icon("黑暗药水", ModContent.ItemType<DarkPotion>())
    };

    public static string Render(string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        string iconResult = RenderItemSheets(outputDirectory);
        string atlasResult = RenderOreAtlases(outputDirectory);
        string tableResult = RenderAlchemyTables(outputDirectory);
        string uiResult = RenderEnchantUi(outputDirectory);
        string oreResult = TestNativeOreRunnerAndMining();
        return $"progression-visuals=PASS {iconResult} {atlasResult} {oreResult} {tableResult} {uiResult}";
    }

    internal static string RenderEnchantUiOnly(string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        return RenderEnchantUi(outputDirectory);
    }

    private static string RenderItemSheets(string outputDirectory)
    {
        const int width = 1280, cellW = 320, cellH = 154, rows = 6, height = cellH * rows;
        GraphicsDevice device = Main.graphics.GraphicsDevice;
        RenderTargetBinding[] oldTargets = device.GetRenderTargets();
        Vector2 oldScreen = Main.screenPosition;
        int invCount = 0, worldCount = 0;
        List<string> lines = new() { "22 progression item icons; native ItemLoader inventory/world hooks; nearest-neighbor 2x display." };
        try
        {
            Main.screenPosition = Vector2.Zero;
            using RenderTarget2D inventory = new(device, width, height, false, SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
            device.SetRenderTarget(inventory);
            device.Clear(new Color(18, 21, 30));
            Main.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, DepthStencilState.None, RasterizerState.CullNone);
            Icon[] icons = Icons;
            for (int i = 0; i < icons.Length; i++)
            {
                Icon icon = icons[i];
                Main.instance.LoadItem(icon.Type);
                Item item = new(); item.SetDefaults(icon.Type);
                Texture2D texture = TextureAssets.Item[icon.Type].Value;
                if (texture.Width <= 1 || texture.Height <= 1) throw new InvalidOperationException($"unloaded item texture {icon.Name}: {texture.Width}x{texture.Height}");
                int x = (i % 4) * cellW, y = (i / 4) * cellH;
                Vector2 center = new(x + cellW / 2f, y + 90f);
                DrawLabel($"{i + 1:00} · {icon.Name}", new Vector2(x + 12, y + 10), Color.White, cellW - 24);
                Rectangle source = new(0, 0, texture.Width, texture.Height);
                float scale = 2f * 32f / Math.Max(texture.Width, texture.Height);
                Vector2 origin = new(texture.Width * .5f, texture.Height * .5f);
                Color color = item.GetAlpha(Color.White), itemColor = item.GetColor(Color.White);
                bool draw = ItemLoader.PreDrawInInventory(item, Main.spriteBatch, center, source, color, itemColor, origin, scale);
                if (draw) Main.spriteBatch.Draw(texture, center, source, color, 0f, origin, scale, SpriteEffects.None, 0f);
                ItemLoader.PostDrawInInventory(item, Main.spriteBatch, center, source, color, itemColor, origin, scale);
                lines.Add($"inventory {icon.Name}: type={icon.Type} texture={texture.Width}x{texture.Height} normalized32px=yes preDraw={draw}");
                invCount++;
            }
            Main.spriteBatch.End();
            device.SetRenderTargets(oldTargets);
            using (FileStream stream = File.Create(Path.Combine(outputDirectory, "progression-items-inventory.png"))) inventory.SaveAsPng(stream, width, height);

            using RenderTarget2D world = new(device, width, height, false, SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
            device.SetRenderTarget(world); device.Clear(new Color(18, 21, 30));
            Main.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, DepthStencilState.None, RasterizerState.CullNone);
            for (int i = 0; i < icons.Length; i++)
            {
                Icon icon = icons[i]; Item item = new(); item.SetDefaults(icon.Type);
                Texture2D texture = TextureAssets.Item[icon.Type].Value;
                int x = (i % 4) * cellW, y = (i / 4) * cellH;
                Vector2 bottom = new(x + cellW / 2f, y + 118f);
                item.position = bottom - new Vector2(item.width * .5f, item.height);
                DrawLabel($"{i + 1:00} · {icon.Name} · 1x", new Vector2(x + 12, y + 10), Color.White, cellW - 24);
                float rotation = 0f, scale = item.scale;
                bool draw = ItemLoader.PreDrawInWorld(item, Main.spriteBatch, Color.White, Color.White, ref rotation, ref scale, i);
                if (draw)
                    Main.spriteBatch.Draw(texture, bottom, new Rectangle(0, 0, texture.Width, texture.Height), Color.White, rotation,
                        new Vector2(texture.Width * .5f, texture.Height), scale, SpriteEffects.None, 0f);
                lines.Add($"world {icon.Name}: preDraw={draw} nativeScale={scale:0.###} texture={texture.Width}x{texture.Height}; display=2x technical zoom"); worldCount++;
            }
            Main.spriteBatch.End(); device.SetRenderTargets(oldTargets);
            using (FileStream stream = File.Create(Path.Combine(outputDirectory, "progression-items-world-drop.png"))) world.SaveAsPng(stream, width, height);
            File.WriteAllLines(Path.Combine(outputDirectory, "progression-item-visual-audit.log"), lines);
        }
        finally
        {
            try { Main.spriteBatch.End(); } catch { }
            device.SetRenderTargets(oldTargets); Main.screenPosition = oldScreen;
        }
        if (invCount != 22 || worldCount != 22) throw new InvalidOperationException($"item visual count mismatch inventory={invCount} world={worldCount}");
        return $"items={invCount}/22-inventory+{worldCount}/22-world-hooks";

        static void DrawLabel(string text, Vector2 pos, Color color, float maxWidth)
            => Terraria.UI.Chat.ChatManager.DrawColorCodedStringWithShadow(Main.spriteBatch, FontAssets.MouseText.Value, text, pos, color, 0f, Vector2.Zero, Vector2.One, maxWidth, 1f);
    }

    private static string RenderOreAtlases(string outputDirectory)
    {
        const int width = 700, height = 360;
        int[] types = { ModContent.TileType<BlackIronOreTile>(), ModContent.TileType<PinkDiamondOreTile>() };
        string[] names = { "黑铁矿脉原生18px帧", "粉钻矿脉原生18px帧" };
        GraphicsDevice device = Main.graphics.GraphicsDevice; RenderTargetBinding[] old = device.GetRenderTargets();
        List<string> rows = new();
        using RenderTarget2D target = new(device, width, height, false, SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
        try
        {
            device.SetRenderTarget(target); device.Clear(new Color(18, 21, 30));
            Main.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, DepthStencilState.None, RasterizerState.CullNone);
            for (int i = 0; i < types.Length; i++)
            {
                Main.instance.LoadTiles(types[i]); Texture2D atlas = TextureAssets.Tile[types[i]].Value;
                if (atlas.Width < 288 || atlas.Height < 270) throw new InvalidOperationException($"ore atlas too small for native framing: {names[i]} {atlas.Width}x{atlas.Height}");
                Vector2 anchor = new(30 + i * 350, 56);
                DrawLabel($"{names[i]} · atlas {atlas.Width}×{atlas.Height}", new Vector2(anchor.X, 15), Color.White, 330);
                Main.spriteBatch.Draw(atlas, anchor, null, Color.White, 0f, Vector2.Zero, 1f, SpriteEffects.None, 0f);
                Rectangle[] samples = { new(0,0,16,16), new(18,18,16,16), new(36,36,16,16), new(54,54,16,16), new(72,72,16,16), new(90,90,16,16) };
                for (int j = 0; j < samples.Length; j++) Main.spriteBatch.Draw(atlas, new Vector2(anchor.X + j * 48, 320), samples[j], Color.White, 0f, Vector2.Zero, 2.5f, SpriteEffects.None, 0f);
                rows.Add($"{names[i]} type={types[i]} atlas={atlas.Width}x{atlas.Height} frameGrid=18x18 samples=6 nativeCoordinates=PASS");
            }
            Main.spriteBatch.End(); device.SetRenderTargets(old);
            using FileStream stream = File.Create(Path.Combine(outputDirectory, "progression-ore-native-atlases.png")); target.SaveAsPng(stream, width, height);
        }
        finally { try { Main.spriteBatch.End(); } catch { } device.SetRenderTargets(old); }
        File.WriteAllLines(Path.Combine(outputDirectory, "progression-ore-atlas.log"), rows);
        return "ore-atlases=PASS 2/2 native-288x270 18px-frame-grid";

        static void DrawLabel(string text, Vector2 pos, Color color, float maxWidth)
            => Terraria.UI.Chat.ChatManager.DrawColorCodedStringWithShadow(Main.spriteBatch, FontAssets.MouseText.Value, text, pos, color, 0f, Vector2.Zero, Vector2.One, maxWidth, 1f);
    }

    private static string TestNativeOreRunnerAndMining()
    {
        int tileA = ModContent.TileType<BlackIronOreTile>(), tileB = ModContent.TileType<PinkDiamondOreTile>();
        const int width = 420, height = 320;
        const int minDepth = 60, maxDepth = 280;
        const int minX = 100, maxX = 320;
        Tilemap oldTiles = Main.tile;
        int oldMaxX = Main.maxTilesX, oldMaxY = Main.maxTilesY, oldMode = Main.netMode;
        double oldSurface = Main.worldSurface, oldRock = Main.rockLayer;
        Item[] originalItems = Main.item;
        FieldInfo randomField = typeof(WorldGen).GetField("_genRand", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("WorldGen random field unavailable for isolated ore test");
        FieldInfo seedField = typeof(WorldGen).GetField("_genRandSeed", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("WorldGen random seed field unavailable for isolated ore test");
        object oldWorldGenRandom = randomField.GetValue(null);
        object oldWorldGenSeed = seedField.GetValue(null);
        bool oldGen = WorldGen.gen, oldGenerating = WorldGen.generatingWorld;
        string result = "";
        try
        {
            Main.tile = (Tilemap)Activator.CreateInstance(typeof(Tilemap), BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null, args: new object[] { (ushort)width, (ushort)height }, culture: null);
            Main.maxTilesX = width; Main.maxTilesY = height;
            Main.worldSurface = minDepth; Main.rockLayer = maxDepth;
            Main.netMode = NetmodeID.SinglePlayer;
            Main.item = Enumerable.Range(0, Math.Max(400, Main.maxItems + 1)).Select(_ => new Item()).ToArray();
            randomField.SetValue(null, new UnifiedRandom(2112026));
            WorldGen.gen = true; WorldGen.generatingWorld = true;
            // Supply stone host tiles in a temporary Tilemap and execute the
            // actual ModSystem-inserted PassLegacy, rather than approximating
            // its callback with hand-picked TileRunner calls.
            for (int x = minX; x < maxX; x++)
            for (int y = minDepth; y < maxDepth; y++)
                WorldGen.PlaceTile(x, y, TileID.Stone, true, true, -1, 0);
            var tasks = new List<GenPass> { new PassLegacy("Shinies", (progress, config) => { }) };
            double weight = 0;
            ModContent.GetInstance<ProgressionWorldGenSystem>().ModifyWorldGenTasks(tasks, ref weight);
            if (tasks.Count != 2 || tasks[1].Name != "Milkcraft Progression Ores")
                throw new InvalidOperationException("ore generation pass not registered immediately after Shinies");
            tasks[1].Apply(new GenerationProgress(), null);
            int countA = CountTiles(tileA), countB = CountTiles(tileB);
            if (countA < 1 || countB < 1)
                throw new InvalidOperationException($"actual inserted GenPass generated insufficient ore: blackIron={countA} pinkDiamond={countB}");
            Main.instance.LoadTiles(tileA); Main.instance.LoadTiles(tileB);
            Point blackCell = FindOreCell(tileA), pinkCell = FindOreCell(tileB);
            WorldGen.SquareTileFrame(blackCell.X, blackCell.Y, true); WorldGen.SquareTileFrame(pinkCell.X, pinkCell.Y, true);
            // Exit generation mode before invoking the real mining/drop path.
            WorldGen.gen = false; WorldGen.generatingWorld = false; WorldGen.noTileActions = false; WorldGen.destroyObject = false;
            WorldGen.KillTile(blackCell.X, blackCell.Y, false, false, false);
            WorldGen.KillTile(pinkCell.X, pinkCell.Y, false, false, false);
            int blackItem = ModContent.ItemType<BlackIronOre>(), pinkItem = ModContent.ItemType<PinkDiamond>();
            int blackDrops = Main.item.Where(i => i.active && i.type == blackItem).Sum(i => i.stack);
            int pinkDrops = Main.item.Where(i => i.active && i.type == pinkItem).Sum(i => i.stack);
            int wrongPinkOre = Main.item.Where(i => i.active && i.type == ModContent.ItemType<PinkDiamondOre>()).Sum(i => i.stack);
            if (blackDrops != 1 || pinkDrops != 1 || wrongPinkOre != 0)
                throw new InvalidOperationException($"native mining drops mismatch blackIron={blackDrops} pinkDiamond={pinkDrops} pinkOreItem={wrongPinkOre}");
            result = $"ore-worldgen=PASS actual-inserted-GenPass.Apply=PASS registered-after-Shinies=PASS blackIronTiles={countA} pinkDiamondTiles={countB} mining=PASS drops=BlackIronOre1/PinkDiamond1 pinkOreItemDropped=0";
        }
        finally
        {
            Main.tile = oldTiles; Main.maxTilesX = oldMaxX; Main.maxTilesY = oldMaxY;
            Main.worldSurface = oldSurface; Main.rockLayer = oldRock;
            Main.item = originalItems; Main.netMode = oldMode; WorldGen.gen = oldGen; WorldGen.generatingWorld = oldGenerating;
            randomField.SetValue(null, oldWorldGenRandom);
            seedField.SetValue(null, oldWorldGenSeed);
        }
        return result;

        int CountTiles(int type)
        {
            int count = 0;
            for (int x = minX; x < maxX; x++) for (int y = minDepth; y < maxDepth; y++) if (Main.tile[x,y].HasTile && Main.tile[x,y].TileType == type) count++;
            return count;
        }
        Point FindOreCell(int type)
        {
            for (int x = minX; x < maxX; x++) for (int y = minDepth; y < maxDepth; y++)
                if (Main.tile[x,y].HasTile && Main.tile[x,y].TileType == type) return new Point(x,y);
            throw new InvalidOperationException($"generated ore cell not found for tile {type}");
        }
    }

    private static string RenderAlchemyTables(string outputDirectory)
    {
        const int x0 = 420, y0 = 100;
        int type = ModContent.TileType<MilkcraftAlchemyTableTile>();
        MilkcraftAlchemyWorldSystem system = ModContent.GetInstance<MilkcraftAlchemyWorldSystem>();
        var oldData = new TagCompound(); system.SaveWorldData(oldData);
        GraphicsDevice device = Main.graphics.GraphicsDevice; RenderTargetBinding[] oldTargets = device.GetRenderTargets();
        Vector2 oldScreen = Main.screenPosition; bool oldDrawToScreen = Main.drawToScreen;
        try
        {
            Main.instance.LoadTiles(type);
            Texture2D nativeAtlas = TextureAssets.Tile[type].Value;
            if (nativeAtlas.Width < 54 || nativeAtlas.Height < 54)
                throw new InvalidOperationException($"alchemy table atlas failed native load: {nativeAtlas.Width}x{nativeAtlas.Height}");
            for (int x = 0; x < 6; x++) for (int y = 0; y < 3; y++)
            {
                if (Main.tile[x0+x,y0+y].HasTile || Main.tile[x0+x,y0+y].WallType != WallID.None || Main.tile[x0+x,y0+y].LiquidAmount > 0)
                    throw new InvalidOperationException("alchemy visual fixture position occupied; refusing to alter it");
                Tile t = Main.tile[x0+x,y0+y]; t.HasTile = true; t.TileType = (ushort)type; t.TileFrameX = (short)((x % 3) * 18); t.TileFrameY = (short)(y * 18);
            }
            Point16 dormant = new(x0,y0), active = new(x0+3,y0);
            MilkcraftAlchemyWorldSystem.ApplyActivationSync(dormant,false); MilkcraftAlchemyWorldSystem.ApplyActivationSync(active,true);
            Main.screenPosition = Vector2.Zero; Main.drawToScreen = true;
            using RenderTarget2D target = new(device, 1120, 420, false, SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
            device.SetRenderTarget(target); device.Clear(new Color(17,20,29));
            Main.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, DepthStencilState.None, RasterizerState.CullNone);
            DrawLabel("亮环境 · 未激活（原生图）", new Vector2(24,12), Color.White, 430);
            DrawLabel("亮环境 · 已激活（替换图＋局部发光）", new Vector2(560,12), Color.White, 510);
            DrawLabel("暗环境 · 未激活（同一暗光输入）", new Vector2(24,215), Color.White, 500);
            DrawLabel("暗环境 · 已激活（同一暗光输入＋局部发光）", new Vector2(560,215), Color.White, 540);
            for (int row = 0; row < 2; row++) for (int panel = 0; panel < 2; panel++) for (int dx = 0; dx < 3; dx++) for (int dy = 0; dy < 3; dy++)
            {
                int x = x0 + panel * 3 + dx, y = y0 + dy;
                int drawX = 60 + panel * 500 + dx * 16, drawY = row * 205 + 58 + dy * 16;
                Color light = row == 0 ? Color.White : new Color(34, 38, 50);
                Vector2 pos = new(drawX,drawY);
                if (panel == 1)
                {
                    Main.screenPosition = new Vector2(x * 16f - drawX, y * 16f - drawY);
                    if (!MilkcraftAlchemyWorldSystem.DrawActiveCellForDiagnostics(x,y,Main.spriteBatch,light)) throw new InvalidOperationException("active tile renderer did not claim its frame");
                }
                if (panel == 0)
                {
                    Texture2D baseAtlas = TextureAssets.Tile[type].Value;
                    Rectangle source = new(Main.tile[x,y].TileFrameX % 54, Main.tile[x,y].TileFrameY % 54, 16,16);
                    Main.spriteBatch.Draw(baseAtlas,pos,source,light,0f,Vector2.Zero,1f,SpriteEffects.None,0f);
                }
            }
            Main.spriteBatch.End(); device.SetRenderTargets(oldTargets);
            using FileStream stream = File.Create(Path.Combine(outputDirectory,"alchemy-table-dormant-active-native.png")); target.SaveAsPng(stream,1120,420);
            int emissionPixels = MilkcraftAlchemyWorldSystem.BuildEmissionMaskForDiagnostics();
            if (emissionPixels < 1) throw new InvalidOperationException("active alchemy glow mask empty");
            File.WriteAllText(Path.Combine(outputDirectory,"alchemy-table-visual.log"),$"tile={type} atlas=54x54 activeReplacement=PASS emissionPixels={emissionPixels} dormantNativeAtlas=PASS activeNativePreDraw=PASS brightnessRows=white/dark34,38,50\n");
            return $"alchemy-dormant-active=PASS nativeAtlas+activeReplacement=9cells×2lightLevels emissivePixels={emissionPixels}";
        }
        finally
        {
            try { Main.spriteBatch.End(); } catch { } device.SetRenderTargets(oldTargets); Main.screenPosition=oldScreen; Main.drawToScreen=oldDrawToScreen;
            for(int x=0;x<6;x++) for(int y=0;y<3;y++) Main.tile[x0+x,y0+y].ClearEverything();
            system.LoadWorldData(oldData);
        }
        static void DrawLabel(string text,Vector2 pos,Color c,float max)=>Terraria.UI.Chat.ChatManager.DrawColorCodedStringWithShadow(Main.spriteBatch,FontAssets.MouseText.Value,text,pos,c,0f,Vector2.Zero,Vector2.One,max,1f);
    }

    private static string RenderEnchantUi(string outputDirectory)
    {
        Player player = Main.LocalPlayer;
        Item[] oldInventory = player.inventory.Select(i => i.Clone()).ToArray();
        bool oldDiscountAvailable = player.discountAvailable;
GraphicsDevice device = Main.graphics.GraphicsDevice; RenderTargetBinding[] oldTargets = device.GetRenderTargets();
        int oldScreenWidth = Main.screenWidth, oldScreenHeight = Main.screenHeight;
        float oldUiScale = Main.UIScale;
        try
        {
            player.inventory[0].SetDefaults(ModContent.ItemType<SoulforgedSword>());
            player.inventory[1].SetDefaults(ModContent.ItemType<PinkDiamond>()); player.inventory[1].stack=2;
            player.inventory[2].SetDefaults(ItemID.FallenStar); player.inventory[2].stack=1;
            player.inventory[3].SetDefaults(ModContent.ItemType<SoulforgedShield>());
            player.discountAvailable = false;
            player.inventory[50].SetDefaults(ItemID.GoldCoin); player.inventory[50].stack = 50;
            List<string> images = new();
            int runtimeWidth = ReadNativeScreenDimension("RealScreenWidth", Math.Max(1, (int)MathF.Round(Main.screenWidth * Main.UIScale)));
            int runtimeHeight = ReadNativeScreenDimension("RealScreenHeight", Math.Max(1, (int)MathF.Round(Main.screenHeight * Main.UIScale)));
            var viewports = new (int Width, int Height, float Scale, string Name)[]
            {
                (runtimeWidth, runtimeHeight, Main.UIScale, "runtime"),
                (2560, 1494, 1.6296296f, "user-config"),
                (1280, 720, 1.5f, "720p")
            };
            foreach (var viewport in viewports)
            {
                Main.UIScale = viewport.Scale;
                foreach (bool emptyState in new[] { true, false })
                {
                    using RenderTarget2D target = new(device, viewport.Width, viewport.Height, false, SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
                    device.SetRenderTarget(target); device.Clear(new Color(18,20,29));
                    Main.screenWidth = viewport.Width; Main.screenHeight = viewport.Height;
                    MilkcraftEnchantingSystem.DiagnosticDrawPreview(viewport.Width, viewport.Height, viewport.Scale, emptyState);
                    device.SetRenderTargets(oldTargets);
                    string state = emptyState ? "empty" : "selected";
                    string path = Path.Combine(outputDirectory, $"enchanting-ui-native-{viewport.Name}-{viewport.Width}x{viewport.Height}-uiscale{viewport.Scale:0.####}-{state}.png");
                    using FileStream stream = File.Create(path); target.SaveAsPng(stream, viewport.Width, viewport.Height);
                    images.Add(path);
                }
            }
            return $"enchant-ui-preview=PASS actualGameInterfaceLayer.Draw=1 runtime={runtimeWidth}x{runtimeHeight}@{viewports[0].Scale:0.####}; user-config=2560x1494@1.6296296; 720p=1280x720@1.5 empty+selected screenshots={string.Join(";", images)}";
        }
        finally
        {
            try { Main.spriteBatch.End(); } catch { } device.SetRenderTargets(oldTargets);
            Main.screenWidth = oldScreenWidth; Main.screenHeight = oldScreenHeight;
            Main.UIScale = oldUiScale;
            for(int i=0;i<oldInventory.Length;i++) player.inventory[i]=oldInventory[i];
            player.discountAvailable = oldDiscountAvailable;
        }
    }

    private static int ReadNativeScreenDimension(string propertyName, int fallback)
    {
        const BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        PropertyInfo property = typeof(Terraria.GameInput.PlayerInput).GetProperty(propertyName, flags);
        object target = property?.GetMethod?.IsStatic == true ? null : Main.instance;
        object value = property?.GetValue(target);
        return value is int result && result > 0 ? result : fallback;
    }
}


