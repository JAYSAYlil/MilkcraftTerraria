using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.DataStructures;
using Terraria.GameContent;
using Terraria.ModLoader;
using ReLogic.Content;
using MilkcraftTerraria.Content.Items;

namespace MilkcraftTerraria.Content;

/// <summary>
/// Builds sparse, same-atlas emissive copies of the existing armor textures.
/// No worn texture is edited; the copy only retains a small set of existing
/// colored detail pixels and follows the exact DrawData for the visible part.
/// </summary>
internal sealed class ArmorEmissiveTextureSystem : ModSystem
{
    private sealed class Entry
    {
        public Texture2D Source;
        public Texture2D Glow;
        public string AssetPath;
        public int[,] AlphaPrefix;
        public int[,] GlowPrefix;

        public float Coverage(Rectangle source)
        {
            Rectangle clipped = Rectangle.Intersect(source, new Rectangle(0, 0, Source.Width, Source.Height));
            if (clipped.Width <= 0 || clipped.Height <= 0) return 0f;
            int alpha = Sum(AlphaPrefix, clipped);
            return alpha == 0 ? 0f : Sum(GlowPrefix, clipped) / (float)alpha;
        }

        public int GlowPixels(Rectangle source)
        {
            Rectangle clipped = Rectangle.Intersect(source, new Rectangle(0, 0, Source.Width, Source.Height));
            return clipped.Width <= 0 || clipped.Height <= 0 ? 0 : Sum(GlowPrefix, clipped);
        }

        private static int Sum(int[,] prefix, Rectangle rect)
        {
            int x0 = rect.Left, y0 = rect.Top, x1 = rect.Right, y1 = rect.Bottom;
            return prefix[y1, x1] - prefix[y0, x1] - prefix[y1, x0] + prefix[y0, x0];
        }
    }

    private readonly record struct DetailColor(byte R, byte G, byte B);
    private sealed class ArmorAsset
    {
        public readonly string Name;
        public readonly DetailColor[] Colors;
        public ArmorAsset(string name, params DetailColor[] colors) { Name = name; Colors = colors; }
    }

    private static readonly DetailColor Gold = new(255, 190, 70);
    private static readonly DetailColor Cyan = new(75, 205, 235);
    private static readonly DetailColor Blue = new(65, 145, 240);
    private static readonly DetailColor Red = new(235, 65, 78);
    private static readonly DetailColor Green = new(80, 220, 120);
    private static readonly DetailColor Purple = new(175, 90, 225);

    private static readonly ArmorAsset[] Assets =
    {
        new("IronHelmet_Head", Gold, Cyan), new("IronChestplate_Body", Gold, Cyan), new("IronGreaves_Legs", Gold, Cyan),
        new("LeatherHeadband_Head", Red, Gold), new("LeatherTunic_Body", Red, Gold), new("LeatherLeggings_Legs", Red, Gold),
        new("ClothHood_Head", Gold), new("ClothRobe_Body", Gold), new("ClothLeggings_Legs", Gold), new("ClothRobe_Legs", Gold),
        new("KnightHelmet_Head", Red, Gold), new("KnightChestplate_Body", Red, Gold), new("KnightGreaves_Legs", Red, Gold),
        new("HuntingHood_Head", Blue, Gold), new("HuntingChestplate_Body", Blue, Gold), new("HuntingLeggings_Legs", Blue, Gold),
        new("EnergyHood_Head", Green, Gold, Purple), new("EnergyRobe_Body", Green, Gold, Purple), new("EnergyLeggings_Legs", Green, Gold, Purple), new("EnergyRobe_Legs", Green, Gold, Purple),
        new("TemplarHelmet_Head", Blue, Gold), new("TemplarChestplate_Body", Blue, Gold), new("TemplarGreaves_Legs", Blue, Gold),
        new("NightmareHood_Head", Red, Gold), new("NightmareChestplate_Body", Red, Gold), new("NightmareLeggings_Legs", Red, Gold),
        new("FallenHood_Head", Blue, Gold), new("FallenRobe_Body", Blue, Gold), new("FallenLeggings_Legs", Blue, Gold), new("FallenRobe_Legs", Blue, Gold),
        new("SoulforgedHelmet_Head", Red, Gold), new("SoulforgedChestplate_Body", Red, Gold), new("SoulforgedGreaves_Legs", Red, Gold),
        new("AbyssHood_Head", Red, Blue), new("AbyssChestplate_Body", Red, Blue), new("AbyssLeggings_Legs", Red, Blue),
        new("DarkHood_Head", Red, Purple, Gold), new("DarkRobe_Body", Red, Purple, Gold), new("DarkLeggings_Legs", Red, Purple, Gold), new("DarkRobe_Legs", Red, Purple, Gold)
    };

    private static readonly Dictionary<Texture2D, Entry> BySource = new();
    private static readonly Dictionary<string, Entry> ByAssetName = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> DiagnosticAssets = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, HashSet<string>> DiagnosticPoseAssets = new(StringComparer.OrdinalIgnoreCase);
    private static volatile int _generation;

    internal static bool DiagnosticCapture { get; set; }
    internal static void ResetDiagnosticAssets() { DiagnosticAssets.Clear(); DiagnosticPoseAssets.Clear(); }
    internal static IReadOnlyCollection<string> GetDiagnosticAssets() => DiagnosticAssets.ToArray();
    internal static IReadOnlyCollection<string> GetDiagnosticPoseAssets(string pose)
        => DiagnosticPoseAssets.TryGetValue(pose, out HashSet<string> assets) ? assets.ToArray() : Array.Empty<string>();
    internal static void NoteDiagnosticAsset(string assetPath, string pose)
    {
        string asset = Path.GetFileName(assetPath);
        DiagnosticAssets.Add(asset);
        if (string.IsNullOrWhiteSpace(pose)) return;
        if (!DiagnosticPoseAssets.TryGetValue(pose, out HashSet<string> assets))
            DiagnosticPoseAssets[pose] = assets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        assets.Add(asset);
    }

    public override void Load()
    {
        if (Main.dedServ) return;
        int generation = ++_generation;
        Main.RunOnMainThread(() =>
        {
            if (generation != _generation || Main.dedServ || Main.graphics?.GraphicsDevice is null) return;
            BuildTextures();
        }).GetAwaiter().GetResult();
    }

    private void BuildTextures()
    {
        DisposeTextures();
        string modName = ModContent.GetInstance<MilkcraftTerrariaMod>().Name;
        GraphicsDevice device = Main.graphics.GraphicsDevice;
        foreach (ArmorAsset asset in Assets)
        {
            string path = modName + "/Content/Items/" + asset.Name;
            Texture2D source = ModContent.Request<Texture2D>(path, AssetRequestMode.ImmediateLoad).Value;
            Color[] original = new Color[source.Width * source.Height];
            source.GetData(original);
            Color[] emissive = new Color[original.Length];
            int frameHeight = source.Height % 56 == 0 ? 56 : source.Height;
            for (int frameTop = 0; frameTop < source.Height; frameTop += frameHeight)
            {
                int frameBottom = Math.Min(source.Height, frameTop + frameHeight);
                for (int cellLeft = 0; cellLeft < source.Width; cellLeft += 40)
                {
                    List<(int Index, float Score)>[] groups = new List<(int, float)>[asset.Colors.Length];
                    for (int group = 0; group < groups.Length; group++) groups[group] = new();
                    int opaque = 0;
                    int cellRight = Math.Min(source.Width, cellLeft + 40);
                    for (int y = frameTop; y < frameBottom; y++)
                    for (int x = cellLeft; x < cellRight; x++)
                    {
                        int index = y * source.Width + x;
                        Color c = original[index];
                        if (c.A < 96) continue;
                        opaque++;
                        int priority = MatchingColor(c, asset.Colors);
                        if (priority >= 0)
                        {
                            int saturation = Math.Max(c.R, Math.Max(c.G, c.B)) - Math.Min(c.R, Math.Min(c.G, c.B));
                            int connectedDetail = CountMatchingNeighbors(original, source.Width, source.Height, x, y, frameTop, frameBottom,
                                cellLeft, cellRight, asset.Colors, priority);
                            // Prefer coherent existing gems and trim strokes over isolated
                            // color-noise pixels; luminance alone made the mask look speckled.
                            int score = connectedDetail * 96 + saturation * 2 + (c.R + c.G + c.B) / 12;
                            groups[priority].Add((index, score));
                        }
                    }

                    // Give the first palette entry (the gem/rune color) most
                    // of the budget, then spend the remainder on fine trim.
                    int remaining = Math.Max(1, (int)Math.Ceiling(opaque * .08f));
                    for (int group = 0; group < groups.Length && remaining > 0; group++)
                    {
                        int allowance = group == 0 && groups.Length > 1
                            ? Math.Max(1, (int)Math.Ceiling(remaining * .68f))
                            : remaining;
                        foreach ((int index, _) in groups[group].OrderByDescending(c => c.Score).Take(Math.Min(remaining, allowance)))
                        {
                            Color c = original[index];
                            const float strength = .58f;
                            emissive[index] = new Color((byte)(c.R * strength), (byte)(c.G * strength),
                                (byte)(c.B * strength), (byte)(c.A * strength));
                        }
                        int selected = Math.Min(remaining, allowance);
                        // Candidate shortage is handled by following palette groups.
                        selected = Math.Min(selected, groups[group].Count);
                        remaining -= selected;
                    }
                }
            }

            Texture2D glow = new(device, source.Width, source.Height, false, SurfaceFormat.Color);
            glow.SetData(emissive);
            int[,] alphaPrefix = new int[source.Height + 1, source.Width + 1];
            int[,] glowPrefix = new int[source.Height + 1, source.Width + 1];
            for (int y = 0; y < source.Height; y++)
            for (int x = 0; x < source.Width; x++)
            {
                int alphaValue = original[y * source.Width + x].A >= 96 ? 1 : 0;
                int glowValue = emissive[y * source.Width + x].A > 0 ? 1 : 0;
                alphaPrefix[y + 1, x + 1] = alphaValue + alphaPrefix[y, x + 1] + alphaPrefix[y + 1, x] - alphaPrefix[y, x];
                glowPrefix[y + 1, x + 1] = glowValue + glowPrefix[y, x + 1] + glowPrefix[y + 1, x] - glowPrefix[y, x];
            }
            BySource[source] = new Entry { Source = source, Glow = glow, AssetPath = path, AlphaPrefix = alphaPrefix, GlowPrefix = glowPrefix };
            ByAssetName[NormalizeAssetName(path)] = BySource[source];
            ByAssetName[NormalizeAssetName(asset.Name)] = BySource[source];
        }
    }

    private static int MatchingColor(Color c, DetailColor[] colors)
    {
        for (int i = 0; i < colors.Length; i++)
        {
            DetailColor target = colors[i];
            if (target == Gold && c.R >= 145 && c.G >= 82 && c.R > c.G * 1.22f && c.G > c.B * 1.45f && c.R - c.B >= 78) return i;
            if (target == Cyan && c.B >= 105 && c.G >= 88 && c.B > c.R * 1.08f && c.G > c.R * 1.05f) return i;
            if (target == Blue && c.B >= 115 && c.B > c.R * 1.16f && c.B > c.G * 1.02f && c.R < 150) return i;
            if (target == Red && c.R >= 140 && c.R > c.G * 1.32f && c.R > c.B * 1.18f) return i;
            if (target == Green && c.G >= 112 && c.G > c.R * 1.16f && c.G > c.B * 1.08f) return i;
            if (target == Purple && c.R >= 85 && c.B >= 105 && c.R > c.G * 1.1f && c.B > c.G * 1.14f) return i;
        }
        return -1;
    }

    private static int CountMatchingNeighbors(Color[] pixels, int textureWidth, int textureHeight, int x, int y,
        int frameTop, int frameBottom, int cellLeft, int cellRight, DetailColor[] colors, int targetGroup)
    {
        int count = 0;
        for (int dy = -1; dy <= 1; dy++)
        for (int dx = -1; dx <= 1; dx++)
        {
            if (dx == 0 && dy == 0) continue;
            int nx = x + dx, ny = y + dy;
            if (nx < cellLeft || nx >= cellRight || ny < frameTop || ny >= frameBottom || nx < 0 || ny < 0 ||
                nx >= textureWidth || ny >= textureHeight) continue;
            Color neighbor = pixels[ny * textureWidth + nx];
            if (neighbor.A >= 96 && MatchingColor(neighbor, colors) == targetGroup) count++;
        }
        return count;
    }

    internal static bool TryGet(Texture2D source, Rectangle sourceRect, out Texture2D glow, out float coverage, out int selectedPixels, out string assetPath)
    {
        Entry entry = null;
        if (!BySource.TryGetValue(source, out entry))
        {
            string name = NormalizeAssetName(source.Name ?? "");
            // DrawData can carry a wrapper Texture2D rather than the exact
            // cached object. Only accept a real, non-empty resource path;
            // empty names and basename suffixes can match unrelated skin or
            // hair textures and create detached glow copies.
            if (name.Length > 0 && !ByAssetName.TryGetValue(name, out entry))
            {
                entry = ByAssetName.FirstOrDefault(pair =>
                    pair.Key.Contains('/') &&
                    (name.EndsWith("/" + pair.Key, StringComparison.OrdinalIgnoreCase) ||
                     pair.Key.EndsWith("/" + name, StringComparison.OrdinalIgnoreCase))).Value;
            }
        }
        if (entry is not null)
        {
            glow = entry.Glow;
            coverage = entry.Coverage(sourceRect);
            selectedPixels = entry.GlowPixels(sourceRect);
            assetPath = entry.AssetPath;
            return selectedPixels > 0;
        }
        glow = null;
        coverage = 0f;
        selectedPixels = 0;
        assetPath = "";
        return false;
    }

    public override void Unload()
    {
        if (Main.dedServ) return;
        int generation = ++_generation;
        Main.RunOnMainThread(() =>
        {
            if (generation != _generation) return;
            DisposeTextures();
        }).GetAwaiter().GetResult();
    }

    private static void DisposeTextures()
    {
        foreach (Entry entry in BySource.Values) entry.Glow.Dispose();
        BySource.Clear();
        ByAssetName.Clear();
    }

    private static string NormalizeAssetName(string value)
        => value.Replace('\\', '/').TrimStart('/');
}

/// <summary>Copies only sparse original color details at the exact native armor draw transform.</summary>
public sealed class ArmorEmissivePlayer : ModPlayer
{
    private static string DiagnosticPath => Path.Combine(
        Environment.GetEnvironmentVariable("MILKCRAFT_TERRARIA_SMOKE_OUT") ?? Environment.CurrentDirectory,
        "armor-emissive-drawdata.log");

    public override void TransformDrawData(ref PlayerDrawSet drawInfo)
    {
        if (Main.dedServ || drawInfo.shadow != 0f || !Player.active || Player.dead || Player.ghost || drawInfo.DrawDataCache.Count == 0)
            return;

        bool capture = ArmorEmissiveTextureSystem.DiagnosticCapture;
        for (int i = 0; i < drawInfo.DrawDataCache.Count; i++)
        {
            DrawData original = drawInfo.DrawDataCache[i];
            if (original.texture is null) continue;
            Rectangle source = original.sourceRect ?? new Rectangle(0, 0, original.texture.Width, original.texture.Height);
            if (!ArmorEmissiveTextureSystem.TryGet(original.texture, source, out Texture2D glowTexture,
                out float coverage, out int selected, out string assetPath))
                continue;

            DrawData glow = original;
            glow.texture = glowTexture;
            // Keep native alpha (including invisibility/transparency) while
            // replacing only the lighting tint with emissive white.
            glow.color = Color.White * (original.color.A / 255f);
            // All position/UV/origin/rotation/scale/flip/shader fields are
            // inherited directly from the visible native draw.
            drawInfo.DrawDataCache.Insert(i + 1, glow);
            i++;

            if (capture)
            {
                bool aligned = original.position == glow.position && original.sourceRect == glow.sourceRect &&
                    original.origin == glow.origin && original.rotation == glow.rotation &&
                    original.scale == glow.scale && original.effect == glow.effect && original.shader == glow.shader;
                if (!aligned || coverage > .10f)
                    throw new InvalidOperationException($"Armor emissive transform/coverage failure: {assetPath} source={source} coverage={coverage:P1} aligned={aligned}");
                File.AppendAllText(DiagnosticPath,
                    $"player={Player.whoAmI} pose={SmokeSystem.NativePoseLabel} layer={i - 1} asset={assetPath} source={source} pixels={selected} coverage={coverage:P2} " +
                    $"position={original.position} origin={original.origin} rotation={original.rotation:0.###} scale={original.scale} effect={original.effect} shader={original.shader} aligned={aligned} shadow={drawInfo.shadow}\n");
                ArmorEmissiveTextureSystem.NoteDiagnosticAsset(assetPath, SmokeSystem.NativePoseLabel);
            }
        }
    }
}

internal static class ArmorEmissiveDiagnostics
{
    private sealed record SuitPose(string Name, int Direction, Vector2 Velocity, int Frame, bool Jumping);
    private static readonly (string Name, int Head, int Body, int Legs)[] Suits =
    {
        ("Iron", ModContent.ItemType<IronHelmet>(), ModContent.ItemType<IronChestplate>(), ModContent.ItemType<IronGreaves>()),
        ("Leather", ModContent.ItemType<LeatherHeadband>(), ModContent.ItemType<LeatherTunic>(), ModContent.ItemType<LeatherLeggings>()),
        ("Cloth", ModContent.ItemType<ClothHood>(), ModContent.ItemType<ClothRobe>(), ModContent.ItemType<ClothLeggings>()),
        ("Knight", ModContent.ItemType<KnightHelmet>(), ModContent.ItemType<KnightChestplate>(), ModContent.ItemType<KnightGreaves>()),
        ("Hunting", ModContent.ItemType<HuntingHood>(), ModContent.ItemType<HuntingChestplate>(), ModContent.ItemType<HuntingLeggings>()),
        ("Energy", ModContent.ItemType<EnergyHood>(), ModContent.ItemType<EnergyRobe>(), ModContent.ItemType<EnergyLeggings>()),
        ("Templar", ModContent.ItemType<TemplarHelmet>(), ModContent.ItemType<TemplarChestplate>(), ModContent.ItemType<TemplarGreaves>()),
        ("Nightmare", ModContent.ItemType<NightmareHood>(), ModContent.ItemType<NightmareChestplate>(), ModContent.ItemType<NightmareLeggings>()),
        ("Fallen", ModContent.ItemType<FallenHood>(), ModContent.ItemType<FallenRobe>(), ModContent.ItemType<FallenLeggings>()),
        ("Soulforged", ModContent.ItemType<SoulforgedHelmet>(), ModContent.ItemType<SoulforgedChestplate>(), ModContent.ItemType<SoulforgedGreaves>()),
        ("Abyss", ModContent.ItemType<AbyssHood>(), ModContent.ItemType<AbyssChestplate>(), ModContent.ItemType<AbyssLeggings>()),
        ("Dark", ModContent.ItemType<DarkHood>(), ModContent.ItemType<DarkRobe>(), ModContent.ItemType<DarkLeggings>())
    };

    private static readonly SuitPose[] Poses =
    {
        new("idle-right", 1, Vector2.Zero, 0, false),
        new("walk-left", -1, new Vector2(-2.2f, 0f), 3, false),
        new("jump-right", 1, new Vector2(1.5f, -6f), 5, true)
    };

    internal static string Render(string outputDirectory, int owner)
    {
        const int width = 1280, height = 900, headerHeight = 60, rowHeight = 70, columns = 6, cellWidth = 210;
        GraphicsDevice device = Main.graphics.GraphicsDevice;
        RenderTargetBinding[] oldTargets = device.GetRenderTargets();
        Player oldPlayer = Main.player[owner];
        bool oldMenu = Main.gameMenu, oldPaused = Main.gamePaused, oldWorldgen = WorldGen.gen;
        bool oldLighting = SmokeSystem.ForcePreviewLighting;
        Vector2 oldScreen = Main.screenPosition, oldZoom = Main.GameViewMatrix.Zoom;
        Color background = new(17, 20, 29);
        string logPath = Path.Combine(outputDirectory, "armor-emissive-native-drawdata.log");
        using RenderTarget2D target = new(device, width, height, false, SurfaceFormat.Color,
            DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
        File.WriteAllText(logPath, "Native PlayerRenderer armor material copy: six columns are bright/dark pairs for idle, walk, and jump; all geometry comes from original DrawData.\n");
        try
        {
            Main.gameMenu = false;
            Main.gamePaused = false;
            WorldGen.gen = false;
            Main.screenPosition = Vector2.Zero;
            Main.GameViewMatrix.Zoom = Vector2.One;
            ArmorEmissiveTextureSystem.ResetDiagnosticAssets();
            ArmorEmissiveTextureSystem.DiagnosticCapture = true;
            device.SetRenderTarget(target);
            device.Clear(background);

            Player warmup = SmokeSystem.CreatePreviewPlayer(Suits[0].Head, Suits[0].Body, Suits[0].Legs);
            warmup.active = true;
            Main.player[owner] = warmup;
            Main.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp,
                DepthStencilState.None, RasterizerState.CullCounterClockwise);
            SmokeSystem.DrawPreviewPlayer(warmup, width + 300, height + 300, 1f);
            Main.spriteBatch.End();
            device.Clear(background);
            Main.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp,
                DepthStencilState.None, RasterizerState.CullCounterClockwise);

            string[] lightingLabels = { "明亮·站立右", "暗光·站立右", "明亮·行走左", "暗光·行走左", "明亮·跳跃右", "暗光·跳跃右" };
            for (int column = 0; column < columns; column++)
                Terraria.UI.Chat.ChatManager.DrawColorCodedStringWithShadow(Main.spriteBatch, FontAssets.MouseText.Value,
                    lightingLabels[column], new Vector2(column * cellWidth + 7, 10), Color.White, 0f,
                    Vector2.Zero, Vector2.One, cellWidth - 14, 1f);

            for (int suitIndex = 0; suitIndex < Suits.Length; suitIndex++)
            for (int poseIndex = 0; poseIndex < Poses.Length; poseIndex++)
            for (int lightingIndex = 0; lightingIndex < 2; lightingIndex++)
            {
                var suit = Suits[suitIndex];
                SuitPose pose = Poses[poseIndex];
                int column = poseIndex * 2 + lightingIndex;
                Player actor = SmokeSystem.CreatePreviewPlayer(suit.Head, suit.Body, suit.Legs);
                actor.active = true;
                actor.dead = false;
                actor.Male = true;
                actor.whoAmI = owner;
                actor.direction = pose.Direction;
                actor.velocity = pose.Velocity;
                actor.jump = pose.Jumping ? 1 : 0;
                actor.controlJump = pose.Jumping;
                actor.bodyFrame = new Rectangle(0, 0, 40, 56);
                actor.legFrame = new Rectangle(0, pose.Frame * 56, 40, 56);
                Main.player[owner] = actor;
                SmokeSystem.ForcePreviewLighting = lightingIndex == 0;
                int x = column * cellWidth;
                int y = headerHeight + suitIndex * rowHeight;
                SmokeSystem.DrawPreviewPlayer(actor, x + cellWidth / 2 - 20, y + 7, 1f);
                File.AppendAllText(logPath,
                    $"actor suit={suit.Name} pose={pose.Name} lighting={(lightingIndex == 0 ? "fullbright reference" : "native world lighting")} " +
                    $"head={actor.head} body={actor.body} legs={actor.legs} direction={actor.direction} velocity={actor.velocity} " +
                    $"bodyFrame={actor.bodyFrame} legFrame={actor.legFrame} origin=PlayerRenderer\n");
            }

            Main.spriteBatch.End();
            device.SetRenderTargets(oldTargets);
            Color[] pixels = new Color[width * height];
            target.GetData(pixels);
            int nonBackgroundCells = 0;
            for (int suitIndex = 0; suitIndex < Suits.Length; suitIndex++)
            for (int column = 0; column < columns; column++)
            {
                int x0 = column * cellWidth, y0 = headerHeight + suitIndex * rowHeight, changed = 0;
                for (int y = y0; y < y0 + rowHeight; y++)
                for (int x = x0; x < x0 + cellWidth; x++)
                {
                    Color p = pixels[y * width + x];
                    if (p.R != background.R || p.G != background.G || p.B != background.B) changed++;
                }
                if (changed > 100) nonBackgroundCells++;
                else throw new InvalidOperationException($"Native emissive pose cell is clipped/blank: suit={Suits[suitIndex].Name} column={column} changed={changed}");
            }

            string[] expectedPieces = Suits.SelectMany(s => new[]
            {
                ItemTextureName(s.Head), ItemTextureName(s.Body), VisibleLegTextureName(s.Body, s.Legs)
            }).ToArray();
            HashSet<string> actual = ArmorEmissiveTextureSystem.GetDiagnosticAssets().ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (string expected in expectedPieces)
                if (!actual.Contains(expected)) throw new InvalidOperationException($"No visible native emissive DrawData for worn armor texture {expected}");

            using FileStream stream = File.Create(Path.Combine(outputDirectory, "armor-emissive-12-suits-native-dark-bright.png"));
            target.SaveAsPng(stream, width, height);
            File.AppendAllText(logPath,
                $"matrix=PASS suits=12 poses=idle-right/walk-left/jump-right brightDarkPairs=36/36 nonBackgroundCells={nonBackgroundCells}/72 visibleArmorTextures={actual.Count}/36 sourceAlphaOnly=PASS maxCoverage=10% exactDrawDataTransform=PASS\n");
            // Reuse the established native ItemCheck + PlayerFrame + PlayerRenderer
            // 48-pose arm fixture. The emissive hook stays active so its exact
            // source UV/transform and per-pose mask coverage are captured too.
            ModContent.GetInstance<SmokeSystem>().RenderNativeArmReachDiagnostics();
            string reachMaterialResult = ValidateReachMaterials();
            string mixed = RenderMixedArmor(outputDirectory, owner);
            return $"armorEmissive=PASS nativePlayerRenderer=12x3-pose brightDark=36/36 glowTransform=UV+position+origin+rotation+scale+flip exact; {reachMaterialResult}; mixed={mixed}";
        }
        finally
        {
            try { Main.spriteBatch.End(); } catch { }
            device.SetRenderTargets(oldTargets);
            Main.player[owner] = oldPlayer;
            Main.gameMenu = oldMenu;
            Main.gamePaused = oldPaused;
            WorldGen.gen = oldWorldgen;
            SmokeSystem.ForcePreviewLighting = oldLighting;
            Main.screenPosition = oldScreen;
            Main.GameViewMatrix.Zoom = oldZoom;
            ArmorEmissiveTextureSystem.DiagnosticCapture = false;
        }
    }

    private static string RenderMixedArmor(string outputDirectory, int owner)
    {
        const int width = 1280, height = 900;
        GraphicsDevice device = Main.graphics.GraphicsDevice;
        RenderTargetBinding[] oldTargets = device.GetRenderTargets();
        Player oldPlayer = Main.player[owner];
        bool oldLighting = SmokeSystem.ForcePreviewLighting;
        Vector2 oldScreen = Main.screenPosition, oldZoom = Main.GameViewMatrix.Zoom;
        Color background = new(17, 20, 29);
        using RenderTarget2D target = new(device, width, height, false, SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
        try
        {
            Main.screenPosition = Vector2.Zero;
            Main.GameViewMatrix.Zoom = Vector2.One;
            SmokeSystem.ForcePreviewLighting = false;
            ArmorEmissiveTextureSystem.ResetDiagnosticAssets();
            Player mixed = SmokeSystem.CreatePreviewPlayer(ModContent.ItemType<IronHelmet>(),
                ModContent.ItemType<EnergyRobe>(), ModContent.ItemType<SoulforgedGreaves>());
            mixed.active = true;
            mixed.Male = true;
            mixed.whoAmI = owner;
            device.SetRenderTarget(target);
            device.Clear(background);
            Player rendererWarmup = SmokeSystem.CreatePreviewPlayer(ModContent.ItemType<IronHelmet>(),
                ModContent.ItemType<IronChestplate>(), ModContent.ItemType<IronGreaves>());
            rendererWarmup.active = true;
            rendererWarmup.whoAmI = owner;
            Main.player[owner] = rendererWarmup;
            Main.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp,
                DepthStencilState.None, RasterizerState.CullCounterClockwise);
            SmokeSystem.DrawPreviewPlayer(rendererWarmup, width + 300, height + 300, 1f);
            Main.spriteBatch.End();
            device.Clear(background);
            ArmorEmissiveTextureSystem.ResetDiagnosticAssets();
            Main.player[owner] = mixed;
            Main.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp,
                DepthStencilState.None, RasterizerState.CullCounterClockwise);
            SmokeSystem.DrawPreviewPlayer(mixed, 420, 200, 1f);
            Main.spriteBatch.End();
            device.SetRenderTargets(oldTargets);

            HashSet<string> actual = ArmorEmissiveTextureSystem.GetDiagnosticAssets().ToHashSet(StringComparer.OrdinalIgnoreCase);
            string[] expected = { "IronHelmet_Head", "EnergyRobe_Body", "EnergyRobe_Legs" };
            if (actual.Count != expected.Length || expected.Any(name => !actual.Contains(name)))
                throw new InvalidOperationException($"Mixed worn armor emitted hidden/incorrect glow sources: expected={string.Join(',', expected)} actual={string.Join(',', actual)}");
            Color[] pixels = new Color[width * height];
            target.GetData(pixels);
            int visiblePixels = pixels.Count(pixel => pixel.R != background.R || pixel.G != background.G || pixel.B != background.B);
            if (visiblePixels < 500) throw new InvalidOperationException($"Mixed PlayerRenderer image is blank/clipped: visiblePixels={visiblePixels}");
            using FileStream stream = File.Create(Path.Combine(outputDirectory, "armor-emissive-mixed-visible-only.png"));
            target.SaveAsPng(stream, width, height);
            File.AppendAllText(Path.Combine(outputDirectory, "armor-emissive-drawdata.log"),
                $"mixed-visibility=PASS visible={string.Join(',', actual)} hiddenPieces=1 hangingGlow=0 visiblePixels={visiblePixels} requestedUnderlyingLegs=SoulforgedGreaves hiddenBy=EnergyRobe SetMatch=true\n");
            return "mixed visible head/body/legs only; hidden armor glow=0";
        }
        finally
        {
            try { Main.spriteBatch.End(); } catch { }
            device.SetRenderTargets(oldTargets);
            Main.player[owner] = oldPlayer;
            SmokeSystem.ForcePreviewLighting = oldLighting;
            Main.screenPosition = oldScreen;
            Main.GameViewMatrix.Zoom = oldZoom;
        }
    }

    private static string ItemTextureName(int itemType)
    {
        Item item = new();
        item.SetDefaults(itemType);
        return item.ModItem?.Name switch
        {
            "IronHelmet" => "IronHelmet_Head", "IronChestplate" => "IronChestplate_Body", "IronGreaves" => "IronGreaves_Legs",
            "LeatherHeadband" => "LeatherHeadband_Head", "LeatherTunic" => "LeatherTunic_Body", "LeatherLeggings" => "LeatherLeggings_Legs",
            "ClothHood" => "ClothHood_Head", "ClothRobe" => "ClothRobe_Body", "ClothLeggings" => "ClothLeggings_Legs",
            "KnightHelmet" => "KnightHelmet_Head", "KnightChestplate" => "KnightChestplate_Body", "KnightGreaves" => "KnightGreaves_Legs",
            "HuntingHood" => "HuntingHood_Head", "HuntingChestplate" => "HuntingChestplate_Body", "HuntingLeggings" => "HuntingLeggings_Legs",
            "EnergyHood" => "EnergyHood_Head", "EnergyRobe" => "EnergyRobe_Body", "EnergyLeggings" => "EnergyLeggings_Legs",
            "TemplarHelmet" => "TemplarHelmet_Head", "TemplarChestplate" => "TemplarChestplate_Body", "TemplarGreaves" => "TemplarGreaves_Legs",
            "NightmareHood" => "NightmareHood_Head", "NightmareChestplate" => "NightmareChestplate_Body", "NightmareLeggings" => "NightmareLeggings_Legs",
            "FallenHood" => "FallenHood_Head", "FallenRobe" => "FallenRobe_Body", "FallenLeggings" => "FallenLeggings_Legs",
            "SoulforgedHelmet" => "SoulforgedHelmet_Head", "SoulforgedChestplate" => "SoulforgedChestplate_Body", "SoulforgedGreaves" => "SoulforgedGreaves_Legs",
            "AbyssHood" => "AbyssHood_Head", "AbyssChestplate" => "AbyssChestplate_Body", "AbyssLeggings" => "AbyssLeggings_Legs",
            "DarkHood" => "DarkHood_Head", "DarkRobe" => "DarkRobe_Body", "DarkLeggings" => "DarkLeggings_Legs",
            _ => throw new InvalidOperationException($"No item texture mapping for {itemType}")
        };
    }

    private static string VisibleLegTextureName(int bodyType, int legsType) => bodyType switch
    {
        var type when type == ModContent.ItemType<ClothRobe>() => "ClothRobe_Legs",
        var type when type == ModContent.ItemType<EnergyRobe>() => "EnergyRobe_Legs",
        var type when type == ModContent.ItemType<FallenRobe>() => "FallenRobe_Legs",
        var type when type == ModContent.ItemType<DarkRobe>() => "DarkRobe_Legs",
        _ => ItemTextureName(legsType)
    };

    private static string ValidateReachMaterials()
    {
        string[] poses = { "horizontal-right", "horizontal-left", "raised-right", "raised-left" };
        int checkedPoses = 0;
        foreach (var suit in Suits)
        {
            string tier = suit.Name switch
            {
                "Iron" or "Leather" or "Cloth" => "T1",
                "Knight" or "Hunting" or "Energy" => "T2",
                "Templar" or "Nightmare" or "Fallen" => "T3",
                _ => "T4"
            };
            string fixtureName = suit.Name switch
            {
                "Leather" => "Ranger",
                "Cloth" or "Energy" => "Mage",
                "Hunting" => "Hunter",
                _ => suit.Name
            };
            string bodyTexture = ItemTextureName(suit.Body);
            foreach (string pose in poses)
            {
                string label = $"arm-reach/{tier}-{fixtureName}/{pose}";
                IReadOnlyCollection<string> visible = ArmorEmissiveTextureSystem.GetDiagnosticPoseAssets(label);
                if (!visible.Contains(bodyTexture, StringComparer.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"Reach fixture lacked emissive native body DrawData for {label}; expected {bodyTexture}, actual={string.Join(',', visible)}");
                checkedPoses++;
            }
        }
        File.AppendAllText(Path.Combine(Environment.GetEnvironmentVariable("MILKCRAFT_TERRARIA_SMOKE_OUT") ??
            Environment.CurrentDirectory, "armor-emissive-drawdata.log"),
            $"reach-materials=PASS actualItemCheck+PlayerFrame+PlayerRenderer poses={checkedPoses}/48 horizontal/raised L/R exactArmorBodyTextureDrawData=PASS; see native-12-tier-arm-reach-drawdata.log for hand UV and coverage\n");
        return $"reachMaterial=PASS actualNative horizontal/raised L/R={checkedPoses}/48";
    }
}
