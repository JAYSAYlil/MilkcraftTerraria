using System;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.ModLoader;

namespace MilkcraftTerraria.Content;

/// <summary>Opt-in mapping and true GlobalProjectile.PreDraw texture smoke checks.</summary>
internal static class PassiveSmokeDiagnostics
{
    private static readonly string[] Suits = { "Iron", "Leather", "Cloth", "Knight", "Hunting", "Energy", "Templar", "Nightmare", "Fallen", "Soulforged", "Abyss", "Dark" };
    private static readonly (string weapon, string accessory, bool homing)[] Weapons =
    {
        ("IronBow", "IronQuiver", false), ("HuntingBow", "HuntingQuiver", false),
        ("NightmareBow", "NightmareQuiver", true), ("AbyssBow", "AbyssQuiver", true),
        ("IronStaff", "BeginnerTome", false), ("EnergyStaff", "EnergyTome", false),
        ("FallenStaff", "FallenTome", true), ("DarkStaff", "DarkTome", true)
    };

    public static string Run(string outputDirectory)
    {
        int goodPairs = 0;
        for (int i = 0; i < Suits.Length; i++)
        {
            string accessory = MilkcraftPassivePlayer.AccessoryForSuit(Suits[i]);
            Require(MilkcraftPassivePlayer.SuitPairMatches(Suits[i], accessory), $"matching armor/accessory failed: {Suits[i]}/{accessory}");
            Require(!MilkcraftPassivePlayer.SuitPairMatches(Suits[i], MilkcraftPassivePlayer.AccessoryForSuit(Suits[(i + 1) % Suits.Length])), $"wrong-tier/set accessory paired with {Suits[i]}");
            goodPairs++;
        }

        int weaponPairs = 0;
        for (int i = 0; i < Weapons.Length; i++)
        {
            var expected = Weapons[i];
            var resolved = MilkcraftPairedProjectile.ResolveWeaponPair(expected.weapon);
            Require(resolved.Accessory == expected.accessory && resolved.Homing == expected.homing && !string.IsNullOrWhiteSpace(resolved.Texture), $"weapon mapping mismatch: {expected.weapon}");
            Require(MilkcraftPairedProjectile.AccessoryMatchesWeapon(expected.weapon, expected.accessory), $"functional accessory rejected: {expected.weapon}");
            Require(!MilkcraftPairedProjectile.AccessoryMatchesWeapon(expected.weapon, Weapons[(i + 1) % Weapons.Length].accessory), $"wrong accessory accepted: {expected.weapon}");
            Require((i < 2 || i is 4 or 5) ? !resolved.Homing : resolved.Homing, $"homing tier mismatch: {expected.weapon}");
            weaponPairs++;
        }

        string imagePath = Path.Combine(outputDirectory, "paired-projectiles-dark-preview.png");
        return $"passive-mapping-preflight=PASS armorPairs={goodPairs}/12 wrongPairsRejected=12 weaponPairs={weaponPairs}/8 wrongAccessoryRejected=8 expectedHomingMapping=T1T2-off/T3T4-on actualDarkPreview={imagePath}";
    }

    public static void RenderActualProjectilePreviews(string path, System.Collections.Generic.IReadOnlyList<Projectile> projectiles)
    {
        Require(projectiles.Count == Weapons.Length, $"expected 8 actual ItemCheck projectiles for dark preview, got {projectiles.Count}");
        string modName = ModContent.GetInstance<MilkcraftTerrariaMod>().Name;
        string tracePath = Path.ChangeExtension(path, ".log");
        (int Width, int Height)[] expectedSizes =
        {
            (50, 12), (50, 12), (54, 18), (54, 19),
            (46, 20), (46, 20), (46, 32), (46, 24)
        };
        var preloadedTextures = new Texture2D[Weapons.Length];
        var trace = new System.Collections.Generic.List<string> { "Actual paired ItemCheck projectile render trace:" };
        var projectileIdentities = new System.Collections.Generic.HashSet<int>();
        for (int i = 0; i < Weapons.Length; i++)
        {
            var pair = MilkcraftPairedProjectile.ResolveWeaponPair(Weapons[i].weapon);
            Require(!string.IsNullOrEmpty(pair.Texture), $"paired preview texture missing for {Weapons[i].weapon}");
            preloadedTextures[i] = ModContent.Request<Texture2D>(modName + "/Content/Projectiles/" + pair.Texture,
                ReLogic.Content.AssetRequestMode.ImmediateLoad).Value;
            Require(preloadedTextures[i].Width > 1 && preloadedTextures[i].Height > 1,
                $"projectile texture is still a placeholder: {pair.Texture} {preloadedTextures[i].Width}x{preloadedTextures[i].Height}");
            Require(preloadedTextures[i].Width == expectedSizes[i].Width && preloadedTextures[i].Height == expectedSizes[i].Height,
                $"projectile texture size mismatch for {pair.Texture}: expected={expectedSizes[i].Width}x{expectedSizes[i].Height} actual={preloadedTextures[i].Width}x{preloadedTextures[i].Height}");
            trace.Add($"preloaded={Weapons[i].weapon} texture={pair.Texture} size={preloadedTextures[i].Width}x{preloadedTextures[i].Height}");
        }
        File.WriteAllLines(tracePath, trace);
        GraphicsDevice device = Main.graphics.GraphicsDevice;
        RenderTargetBinding[] previous = device.GetRenderTargets();
        Vector2 oldScreen = Main.screenPosition;
        using RenderTarget2D target = new(device, 820, 420, false, SurfaceFormat.Color, DepthFormat.None, 0,
            RenderTargetUsage.PreserveContents);
        try
        {
            device.SetRenderTarget(target);
            Color background = new(8, 8, 18);
            device.Clear(background);
            Main.screenPosition = Vector2.Zero;
            for (int i = 0; i < projectiles.Count; i++)
            {
                Projectile projectile = projectiles[i];
                MilkcraftPairedProjectile global = projectile.GetGlobalProjectile<MilkcraftPairedProjectile>();
                Require(projectile.active && global.Paired, $"dark preview requires a live paired projectile from ItemCheck: {Weapons[i].weapon}");
                int identity = System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(projectile);
                Vector2 sourceCenter = projectile.Center;
                var historyField = typeof(MilkcraftPairedProjectile).GetField("_vfxCount", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                int trailSamples = historyField is null ? 0 : (int)historyField.GetValue(global);
                Require(trailSamples >= 8, $"{Weapons[i].weapon} dark preview is missing real trail history: {trailSamples}/8");
                trace.Add($"shot={Weapons[i].weapon} identity={identity} type={projectile.type} variant={global.TextureVariant} historySamples={trailSamples} sourceCenter=({sourceCenter.X:0.##},{sourceCenter.Y:0.##}) texture={MilkcraftPairedProjectile.ResolveWeaponPair(Weapons[i].weapon).Texture}");
                File.WriteAllLines(tracePath, trace);
                Require(projectileIdentities.Add(identity),
                    $"ItemCheck projectile reference reused in preview list at {Weapons[i].weapon}: identity={identity}");
                Require(global.TextureVariant == i + 1,
                    $"ItemCheck projectile variant mismatch for {Weapons[i].weapon}: expected={i + 1} actual={global.TextureVariant}");
                Vector2 expectedPreviewCenter = new Vector2(92 + (i % 4) * 205, 110 + (i / 4) * 185);
                Vector2 cameraOffset = projectile.Center - expectedPreviewCenter;
                Main.screenPosition = cameraOffset;
                trace.Add($"camera-follow={Weapons[i].weapon} screenPosition=({cameraOffset.X:0.##},{cameraOffset.Y:0.##}) preservesCenter=({projectile.Center.X:0.##},{projectile.Center.Y:0.##})");
                projectile.scale = 2.1f;
                Color light = Color.Black;
                Main.spriteBatch.Begin(SpriteSortMode.Immediate, BlendState.AlphaBlend, SamplerState.PointClamp,
                    DepthStencilState.None, RasterizerState.CullNone);
                Require(!global.PreDraw(projectile, ref light), $"actual paired texture did not draw: {Weapons[i].weapon}");
                Main.spriteBatch.End();
            }
            device.SetRenderTargets(previous);
            var pixels = new Color[target.Width * target.Height];
            target.GetData(pixels);
            using (FileStream stream = File.Create(path))
                target.SaveAsPng(stream, target.Width, target.Height);
            for (int i = 0; i < Weapons.Length; i++)
            {
                int x0 = (i % 4) * 205;
                int y0 = (i / 4) * 185;
                int coloredPixels = 0;
                for (int y = y0; y < Math.Min(y0 + 185, target.Height); y++)
                for (int x = x0; x < Math.Min(x0 + 205, target.Width); x++)
                {
                    Color pixel = pixels[y * target.Width + x];
                    if (pixel.R != background.R || pixel.G != background.G || pixel.B != background.B)
                        coloredPixels++;
                }
                Require(coloredPixels > 0,
                    $"dark preview region {i} for {Weapons[i].weapon} contains only clear color; likely viewport/scissor/target loss");
                trace.Add($"region={i} weapon={Weapons[i].weapon} roi=({x0},{y0},205,185) nonBackgroundPixels={coloredPixels}");
                File.WriteAllLines(tracePath, trace);
            }
            trace.Add("pixel-audit=PASS all 8 regions contain non-background rendered pixels");
            File.WriteAllLines(tracePath, trace);
        }
        finally
        {
            try { Main.spriteBatch.End(); } catch { }
            device.SetRenderTargets(previous);
            Main.screenPosition = oldScreen;
        }
    }

    private static void RenderActualPreDraws(string path)
    {
        GraphicsDevice device = Main.graphics.GraphicsDevice;
        RenderTargetBinding[] previous = device.GetRenderTargets();
        Vector2 oldScreen = Main.screenPosition;
        using RenderTarget2D target = new(device, 820, 420, false, SurfaceFormat.Color, DepthFormat.None);
        try
        {
            device.SetRenderTarget(target);
            device.Clear(new Color(8, 8, 18));
            Main.screenPosition = Vector2.Zero;
            Main.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, DepthStencilState.None, RasterizerState.CullNone);
            for (int i = 0; i < Weapons.Length; i++)
            {
                var pair = MilkcraftPairedProjectile.ResolveWeaponPair(Weapons[i].weapon);
                int variant = Array.IndexOf(new[] { "", "IronPairedArrow", "HuntingPairedArrow", "NightmarePairedArrow", "AbyssPairedArrow", "IronPairedBolt", "EnergyPairedBolt", "FallenPairedBolt", "DarkPairedBolt" }, pair.Texture);
                Require(variant > 0, $"texture variant missing for {Weapons[i].weapon}");
                string modName = ModContent.GetInstance<MilkcraftTerrariaMod>().Name;
                Texture2D texture = ModContent.Request<Texture2D>(modName + "/Content/Projectiles/" + pair.Texture).Value;
                Require(texture.Width > 0 && texture.Height > 0, $"empty projectile texture: {pair.Texture}");
                Projectile projectile = Main.projectile[0];
                projectile.Center = new Vector2(92 + (i % 4) * 205, 110 + (i / 4) * 185);
                projectile.velocity = Vector2.UnitX;
                projectile.scale = 2.1f;
                MilkcraftPairedProjectile renderer = new() { Paired = true, Homing = pair.Homing, TextureVariant = variant };
                Color tint = Color.Black;
                Require(!renderer.PreDraw(projectile, ref tint), $"special texture draw fell through: {pair.Texture}");
            }
            Main.spriteBatch.End();
            device.SetRenderTargets(previous);
            using FileStream stream = File.Create(path);
            target.SaveAsPng(stream, target.Width, target.Height);
        }
        finally
        {
            // The normal successful path ended the batch above. On a failed assertion,
            // best-effort end it before restoring the native viewport.
            try { Main.spriteBatch.End(); } catch { }
            device.SetRenderTargets(previous);
            Main.screenPosition = oldScreen;
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
