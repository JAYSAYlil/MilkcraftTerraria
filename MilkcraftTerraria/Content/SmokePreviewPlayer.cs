using System;
using Microsoft.Xna.Framework;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using Terraria;
using Terraria.DataStructures;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;
using Microsoft.Xna.Framework.Graphics;

namespace MilkcraftTerraria.Content;

/// <summary>White test lighting for synthetic action mannequins only.</summary>
public sealed class SmokePreviewPlayer : ModPlayer
{
    private static Dictionary<Texture2D, string> _assetPaths;
    private static readonly HashSet<string> _savedHeadwearFrames = new();
    public override void DrawEffects(PlayerDrawSet drawInfo, ref float r, ref float g,
        ref float b, ref float a, ref bool fullBright)
    {
        if (SmokeSystem.ForcePreviewLighting && drawInfo.drawPlayer.whoAmI == Main.myPlayer)
            fullBright = true;
    }

    public override void ModifyDrawInfo(ref PlayerDrawSet drawInfo)
    {
        if (!SmokeSystem.ForcePreviewLighting || drawInfo.drawPlayer.whoAmI != Main.myPlayer)
            return;

        File.AppendAllText(GetLogPath(),
            $"ModifyDrawInfo fixture={SmokeSystem.NativePoseLabel} selected={drawInfo.drawPlayer.selectedItem} inv0={drawInfo.drawPlayer.inventory[0].type} " +
            $"held={drawInfo.heldItem.type} anim={drawInfo.drawPlayer.itemAnimation} time={drawInfo.drawPlayer.itemTime} " +
            $"useStyle={drawInfo.heldItem.useStyle} noUseGraphic={drawInfo.heldItem.noUseGraphic} " +
            $"isDisplay={drawInfo.drawPlayer.isDisplayDollOrInanimate} shadow={drawInfo.shadow} " +
            $"itemLocation={drawInfo.drawPlayer.itemLocation} itemColor={drawInfo.itemColor}\n");
    }

    public override void TransformDrawData(ref PlayerDrawSet drawInfo)
    {
        if (!SmokeSystem.ForcePreviewLighting || drawInfo.drawPlayer.whoAmI != Main.myPlayer)
            return;

        if (SmokeSystem.CaptureArmReachData)
        {
            _assetPaths ??= BuildTextureAssetPathMap();
            int bodySlot = drawInfo.drawPlayer.body;
            var nativeHands = new List<(int Layer, DrawData Data)>();
            var armorBodyDraws = new List<(int Layer, DrawData Data)>();
            for (int i = 0; i < drawInfo.DrawDataCache.Count; i++)
            {
                DrawData data = drawInfo.DrawDataCache[i];
                string texturePath = data.texture is not null && _assetPaths.TryGetValue(data.texture, out string knownPath)
                    ? knownPath
                    : data.texture?.Name ?? "<null>";
                bool nativeBodyPart = texturePath.Contains("Images\\Player_0_", StringComparison.OrdinalIgnoreCase);
                bool armorBody = texturePath.Contains("_Body", StringComparison.OrdinalIgnoreCase);
                // The vanilla rear hand is Player_0_5; the front palm is
                // Player_0_9. The composite arm atlases can supply the hand
                // in item-specific poses as well, so capture those UVs here
                // rather than treating every Player_0_5 cell as populated.
                bool nativeHandCandidate = texturePath.EndsWith("Player_0_5", StringComparison.OrdinalIgnoreCase) ||
                    texturePath.EndsWith("Player_0_9", StringComparison.OrdinalIgnoreCase) ||
                    texturePath.EndsWith("Player_0_11", StringComparison.OrdinalIgnoreCase) ||
                    texturePath.EndsWith("Player_0_12", StringComparison.OrdinalIgnoreCase) ||
                    texturePath.EndsWith("Player_0_13", StringComparison.OrdinalIgnoreCase);
                if (!nativeBodyPart && !armorBody)
                    continue;

                SmokeSystem.ArmReachCaptureDrawCount++;
                Rectangle source = data.sourceRect ?? new Rectangle(0, 0, data.texture.Width, data.texture.Height);
                Rectangle clipped = Rectangle.Intersect(source, new Rectangle(0, 0, data.texture.Width, data.texture.Height));
                int alphaPixels = 0;
                if (clipped.Width > 0 && clipped.Height > 0)
                {
                    var pixels = new Color[clipped.Width * clipped.Height];
                    data.texture.GetData(0, clipped, pixels, 0, pixels.Length);
                    alphaPixels = pixels.Count(pixel => pixel.A > 0);
                }
                if (nativeHandCandidate && alphaPixels > 0)
                {
                    SmokeSystem.ArmReachNativeHandDrawCount++;
                    nativeHands.Add((i, data));
                }
                if (armorBody && alphaPixels > 0)
                    armorBodyDraws.Add((i, data));
                File.AppendAllText(Path.Combine(GetOutputDirectory(), "native-12-tier-arm-reach-drawdata.log"),
                    $"layer={i} actor={SmokeSystem.NativePoseLabel} kind={(nativeBodyPart ? "native-player" : "armor-body")} nativeHandCandidate={nativeHandCandidate} " +
                    $"texture={texturePath} source={source} sourceInBounds={clipped == source} alphaPixels={alphaPixels} " +
                    $"pos={data.position} origin={data.origin} rot={data.rotation} scale={data.scale} effect={data.effect} color={data.color} " +
                    $"bodyFrame={drawInfo.drawPlayer.bodyFrame} frontArmFrame={drawInfo.compFrontArmFrame} " +
                    $"backArmFrame={GetDrawSetField(drawInfo, "compBackArmFrame")} " +
                    $"frontArm={drawInfo.drawPlayer.compositeFrontArm.enabled}/{drawInfo.drawPlayer.compositeFrontArm.stretch}/{drawInfo.compositeFrontArmRotation} " +
                    $"backArm={drawInfo.drawPlayer.compositeBackArm.enabled}/{drawInfo.drawPlayer.compositeBackArm.stretch}/{GetDrawSetField(drawInfo, "compositeBackArmRotation")} " +
                    $"bodySlot={bodySlot} HidesArms={ArmorIDs.Body.Sets.HidesArms[bodySlot]} HidesHands={ArmorIDs.Body.Sets.HidesHands[bodySlot]}\n");
            }

            if (SmokeSystem.CaptureArmReachReferenceOnly)
                AuditArmorHandUvCoverage(nativeHands, armorBodyDraws);
        }

        if (SmokeSystem.CaptureTier3T4ArmorData)
        {
            _assetPaths = BuildTextureAssetPathMap();
            string[] suitTextureTokens =
            {
                "IronHelmet_Head", "IronChestplate_Body", "IronGreaves_Legs",
                "LeatherHeadband_Head", "LeatherTunic_Body", "LeatherLeggings_Legs",
                "ClothHood_Head", "ClothRobe_Body", "ClothRobe_Legs", "ClothLeggings_Legs",
                "KnightHelmet_Head", "KnightChestplate_Body", "KnightGreaves_Legs",
                "HuntingHood_Head", "HuntingChestplate_Body", "HuntingLeggings_Legs",
                "EnergyHood_Head", "EnergyRobe_Body", "EnergyRobe_Legs", "EnergyLeggings_Legs",
                "TemplarHelmet_Head", "TemplarChestplate_Body", "TemplarGreaves_Legs",
                "NightmareHood_Head", "NightmareChestplate_Body", "NightmareLeggings_Legs",
                "FallenHood_Head", "FallenRobe_Body", "FallenRobe_Legs", "FallenLeggings_Legs",
                "SoulforgedHelmet_Head", "SoulforgedChestplate_Body", "SoulforgedGreaves_Legs",
                "AbyssHood_Head", "AbyssChestplate_Body", "AbyssLeggings_Legs",
                "DarkHood_Head", "DarkRobe_Body", "DarkRobe_Legs", "DarkLeggings_Legs"
            };
            for (int i = 0; i < drawInfo.DrawDataCache.Count; i++)
            {
                DrawData data = drawInfo.DrawDataCache[i];
                string texturePath = data.texture is not null && _assetPaths.TryGetValue(data.texture, out string knownPath)
                    ? knownPath
                    : data.texture?.Name ?? "<null>";
                bool isArmorTexture = texturePath.Contains("Armor", StringComparison.OrdinalIgnoreCase) ||
                    suitTextureTokens.Any(token => texturePath.Contains(token, StringComparison.OrdinalIgnoreCase));
                if (!isArmorTexture)
                    continue;

                SmokeSystem.ArmorCaptureDrawCount++;

                Rectangle source = data.sourceRect ?? new Rectangle(0, 0, data.texture.Width, data.texture.Height);
                Rectangle clipped = Rectangle.Intersect(source, new Rectangle(0, 0, data.texture.Width, data.texture.Height));
                int alphaPixels = 0;
                if (clipped.Width > 0 && clipped.Height > 0)
                {
                    var pixels = new Color[clipped.Width * clipped.Height];
                    data.texture.GetData(0, clipped, pixels, 0, pixels.Length);
                    alphaPixels = pixels.Count(pixel => pixel.A > 0);
                }
                File.AppendAllText(Path.Combine(GetOutputDirectory(), "native-12-tier-armor-drawdata.log"),
                    $"actor={SmokeSystem.NativePoseLabel} cacheIndex={i} texture={texturePath} textureSize={data.texture.Width}x{data.texture.Height} " +
                    $"source={source} sourceInBounds={clipped == source} alphaPixels={alphaPixels} " +
                    $"bodyFrame={drawInfo.drawPlayer.bodyFrame} legFrame={drawInfo.drawPlayer.legFrame} " +
                    $"frontArmFrame={drawInfo.compFrontArmFrame} backArmFrame={GetDrawSetField(drawInfo, "compBackArmFrame")} " +
                    $"usesCompositeTorso={drawInfo.usesCompositeTorso} " +
                    $"head={drawInfo.drawPlayer.head} body={drawInfo.drawPlayer.body} legs={drawInfo.drawPlayer.legs} " +
                    $"pos={data.position} origin={data.origin} rot={data.rotation} effect={data.effect}\n");
            }
        }

        if (SmokeSystem.CaptureHeadwearNativeData)
        {
            _assetPaths ??= BuildTextureAssetPathMap();
            int drawDataEntries = 0;
            for (int i = 0; i < drawInfo.DrawDataCache.Count; i++)
            {
                DrawData data = drawInfo.DrawDataCache[i];
                string texturePath = data.texture is not null && _assetPaths.TryGetValue(data.texture, out string knownPath)
                    ? knownPath
                    : data.texture?.Name ?? "<null>";
                drawDataEntries++;
                if (SmokeSystem.HeadwearCaptureLabel == "bare/right")
                    SaveNativeHeadwearFrame(data.texture, texturePath);
                if (SmokeSystem.HeadwearCaptureLabel == "bare/right" ||
                    texturePath.Contains("Player", StringComparison.OrdinalIgnoreCase))
                    File.AppendAllText(GetHeadwearLogPath(),
                        $"actor={SmokeSystem.HeadwearCaptureLabel} texture={texturePath} src={data.sourceRect} " +
                        $"pos={data.position} origin={data.origin} rot={data.rotation} scale={data.scale} color={data.color}\n");
            }
            if (drawDataEntries > 0)
                SmokeSystem.HeadwearCaptureDrawCount++;
            File.AppendAllText(GetHeadwearLogPath(),
                $"actor={SmokeSystem.HeadwearCaptureLabel} drawDataEntries={drawDataEntries} " +
                $"skinVariant={drawInfo.drawPlayer.skinVariant} hair={drawInfo.drawPlayer.hair} " +
                $"direction={drawInfo.drawPlayer.direction} headSlot={drawInfo.drawPlayer.head}\n");
        }

        if (SmokeSystem.CaptureAccessoryDraws)
        {
            // Accessory texture assets can be requested lazily during the same
            // PlayerRenderer pass, after headwear rendering populated the map.
            _assetPaths = BuildTextureAssetPathMap();
            for (int i = 0; i < drawInfo.DrawDataCache.Count; i++)
            {
                DrawData data = drawInfo.DrawDataCache[i];
                string texturePath = data.texture is not null && _assetPaths.TryGetValue(data.texture, out string knownPath)
                    ? knownPath
                    : data.texture?.Name ?? "<null>";
                SmokeSystem.LogAccessoryDraw(texturePath, data);
            }
        }

        if (SmokeSystem.CaptureRobeLegDraws)
        {
            _assetPaths ??= BuildTextureAssetPathMap();
            for (int i = 0; i < drawInfo.DrawDataCache.Count; i++)
            {
                DrawData data = drawInfo.DrawDataCache[i];
                string texturePath = data.texture is not null && _assetPaths.TryGetValue(data.texture, out string knownPath)
                    ? knownPath
                    : data.texture?.Name ?? "<null>";
                SmokeSystem.LogRobeLegDraw(texturePath, data);
            }
        }

        Item held = drawInfo.heldItem;
        int heldEntries = 0;
        string heldGeometry = "none";
        if (!held.IsAir)
        {
            Texture2D heldTexture = TextureAssets.Item[held.type].Value;
            for (int i = 0; i < drawInfo.DrawDataCache.Count; i++)
            {
                DrawData data = drawInfo.DrawDataCache[i];
                if (!ReferenceEquals(data.texture, heldTexture))
                    continue;
                heldEntries++;
                heldGeometry = $"pos={data.position} src={data.sourceRect} origin={data.origin} rot={data.rotation} scale={data.scale} effect={data.effect}";
                // This changes only lighting on the already queued vanilla
                // held-item draw; geometry/texture/pose remain native.
                data.color = Color.White;
                drawInfo.DrawDataCache[i] = data;
            }
        }
        bool nativeTextureQueued = heldEntries > 0;
        if (nativeTextureQueued)
            SmokeSystem.NativeHeldDrawCount++;
        if (!string.IsNullOrEmpty(SmokeSystem.NativePoseLabel))
        {
            _assetPaths = BuildTextureAssetPathMap();
            string poseLog = Path.Combine(GetOutputDirectory(), "native-mage-hands-ab-drawdata.log");
            for (int i = 0; i < drawInfo.DrawDataCache.Count; i++)
            {
                DrawData data = drawInfo.DrawDataCache[i];
                string texturePath = data.texture is not null && _assetPaths.TryGetValue(data.texture, out string knownPath)
                    ? knownPath
                    : data.texture?.Name ?? "<null>";
                if (!texturePath.Contains("Player", StringComparison.OrdinalIgnoreCase) &&
                    !texturePath.Contains("Armor", StringComparison.OrdinalIgnoreCase) &&
                    !texturePath.Contains("ClothRobe", StringComparison.OrdinalIgnoreCase) &&
                    !texturePath.Contains("EnergyRobe", StringComparison.OrdinalIgnoreCase))
                    continue;
                Rectangle source = data.sourceRect ?? new Rectangle(0, 0, data.texture.Width, data.texture.Height);
                Rectangle clipped = Rectangle.Intersect(source, new Rectangle(0, 0, data.texture.Width, data.texture.Height));
                int alphaPixels = 0;
                if (clipped.Width > 0 && clipped.Height > 0)
                {
                    var pixels = new Color[clipped.Width * clipped.Height];
                    data.texture.GetData(0, clipped, pixels, 0, pixels.Length);
                    alphaPixels = pixels.Count(pixel => pixel.A > 0);
                }
                File.AppendAllText(poseLog,
                    $"actor={SmokeSystem.NativePoseLabel} texture={texturePath} src={source} " +
                    $"inBounds={clipped == source} alphaPixels={alphaPixels} pos={data.position} origin={data.origin} rot={data.rotation}\n");
            }
        }
        File.AppendAllText(GetLogPath(),
            $"Transform fixture={SmokeSystem.NativePoseLabel} selected={drawInfo.drawPlayer.selectedItem} inv0={drawInfo.drawPlayer.inventory[0].type} " +
            $"held={held.type} anim={drawInfo.drawPlayer.itemAnimation} time={drawInfo.drawPlayer.itemTime} " +
            $"useStyle={held.useStyle} noUseGraphic={held.noUseGraphic} isDisplay={drawInfo.drawPlayer.isDisplayDollOrInanimate} " +
            $"itemLocation={drawInfo.drawPlayer.itemLocation} nativeTextureQueued={nativeTextureQueued} itemColor={drawInfo.itemColor} " +
            $"heldEntries={heldEntries} {heldGeometry} native-tint=white-fixture " +
            $"usesCompositeTorso={drawInfo.usesCompositeTorso} frontFrame={drawInfo.compFrontArmFrame} " +
            $"frontArm={drawInfo.drawPlayer.compositeFrontArm.enabled}/{drawInfo.drawPlayer.compositeFrontArm.stretch} " +
            $"frontRot={drawInfo.compositeFrontArmRotation} " +
            $"bodyFrame={drawInfo.drawPlayer.bodyFrame} itemRot={drawInfo.drawPlayer.itemRotation} direction={drawInfo.drawPlayer.direction} " +
            $"bodySlot={drawInfo.drawPlayer.body} hidesArms={ArmorIDs.Body.Sets.HidesArms[drawInfo.drawPlayer.body]} " +
            $"hidesHands={ArmorIDs.Body.Sets.HidesHands[drawInfo.drawPlayer.body]} topSkinHidden={drawInfo.hidesTopSkin}\n");
    }

    private static void AuditArmorHandUvCoverage(
        List<(int Layer, DrawData Data)> nativeHands,
        List<(int Layer, DrawData Data)> armorBodyDraws)
    {
        foreach ((int handLayer, DrawData hand) in nativeHands)
        {
            Rectangle handSource = hand.sourceRect ?? new Rectangle(0, 0, hand.texture.Width, hand.texture.Height);
            Rectangle clippedHand = Rectangle.Intersect(handSource, new Rectangle(0, 0, hand.texture.Width, hand.texture.Height));
            if (clippedHand != handSource || clippedHand.Width <= 0 || clippedHand.Height <= 0)
                continue;

            Color[] handPixels = new Color[clippedHand.Width * clippedHand.Height];
            hand.texture.GetData(0, clippedHand, handPixels, 0, handPixels.Length);
            var matchingArmor = armorBodyDraws.Where(candidate =>
            {
                DrawData armor = candidate.Data;
                Rectangle armorSource = armor.sourceRect ?? new Rectangle(0, 0, armor.texture.Width, armor.texture.Height);
                return candidate.Layer > handLayer && armorSource == handSource &&
                    armor.position == hand.position && armor.origin == hand.origin &&
                    armor.rotation == hand.rotation && armor.scale == hand.scale && armor.effect == hand.effect &&
                    armor.texture.Width >= handSource.Right && armor.texture.Height >= handSource.Bottom;
            }).ToArray();

            var armorPixels = new List<Color[]>(matchingArmor.Length);
            foreach (var match in matchingArmor)
            {
                Color[] pixels = new Color[clippedHand.Width * clippedHand.Height];
                match.Data.texture.GetData(0, clippedHand, pixels, 0, pixels.Length);
                armorPixels.Add(pixels);
            }

            int nativePixels = 0;
            int coveredPixels = 0;
            for (int pixelIndex = 0; pixelIndex < handPixels.Length; pixelIndex++)
            {
                if (handPixels[pixelIndex].A == 0)
                    continue;
                nativePixels++;
                if (armorPixels.Any(pixels => pixels[pixelIndex].A > 0))
                    coveredPixels++;
            }

            SmokeSystem.ArmReachReferenceHandPixels += nativePixels;
            SmokeSystem.ArmReachCoveredHandPixels += coveredPixels;
            SmokeSystem.ArmReachCoverageDrawPairs += matchingArmor.Length;
            File.AppendAllText(Path.Combine(GetOutputDirectory(), "native-12-tier-arm-reach-drawdata.log"),
                $"hand-uv-audit actor={SmokeSystem.NativePoseLabel} nativeLayer={handLayer} source={handSource} " +
                $"nativeAlphaPixels={nativePixels} armorDrawsAfterHand={matchingArmor.Length} " +
                $"coveredAlphaPixels={coveredPixels} PASS={(nativePixels > 0 && nativePixels == coveredPixels)}\n");
        }
    }

    private static Dictionary<Texture2D, string> BuildTextureAssetPathMap()
    {
        var result = new Dictionary<Texture2D, string>();
        foreach (FieldInfo field in typeof(TextureAssets).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            object value;
            try { value = field.GetValue(null); }
            catch { continue; }
            if (value is Array array)
            {
                foreach (object element in array)
                    AddTextureAsset(result, element);
            }
            else
                AddTextureAsset(result, value);
        }
        return result;
    }

    private static string GetDrawSetField(PlayerDrawSet drawInfo, string fieldName)
    {
        FieldInfo field = typeof(PlayerDrawSet).GetField(fieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        return field?.GetValue(drawInfo)?.ToString() ?? "<field-unavailable>";
    }

    private static void AddTextureAsset(Dictionary<Texture2D, string> result, object asset)
    {
        if (asset is null)
            return;
        Type type = asset.GetType();
        PropertyInfo valueProperty = type.GetProperty("Value");
        PropertyInfo nameProperty = type.GetProperty("Name");
        PropertyInfo loadedProperty = type.GetProperty("IsLoaded");
        if (valueProperty is null || nameProperty is null)
            return;
        try
        {
            if (loadedProperty?.GetValue(asset) is bool loaded && !loaded)
                return;
            if (valueProperty.GetValue(asset) is Texture2D texture && nameProperty.GetValue(asset) is string name)
                result.TryAdd(texture, name);
        }
        catch { /* Some TextureAssets are intentionally not loaded in menu smoke. */ }
    }

    private static void SaveNativeHeadwearFrame(Texture2D texture, string texturePath)
    {
        if (texture is null)
            return;
        string normalized = texturePath.Replace('/', '\\');
        string fileName = normalized.EndsWith("Player_0_0", StringComparison.OrdinalIgnoreCase) ? "face-base" :
            normalized.EndsWith("Player_0_1", StringComparison.OrdinalIgnoreCase) ? "eye-whites" :
            normalized.EndsWith("Player_0_2", StringComparison.OrdinalIgnoreCase) ? "eye-iris" : null;
        if (fileName is null || !_savedHeadwearFrames.Add(fileName))
            return;

        string directory = Path.Combine(
            Environment.GetEnvironmentVariable("MILKCRAFT_TERRARIA_SMOKE_OUT") ?? Environment.CurrentDirectory,
            "native-headwear-raw");
        Directory.CreateDirectory(directory);
        using (var atlasStream = File.Create(Path.Combine(directory, fileName + "-atlas.png")))
            texture.SaveAsPng(atlasStream, texture.Width, texture.Height);

        const int frameWidth = 40;
        const int frameHeight = 56;
        var frame = new Rectangle(0, 0, frameWidth, frameHeight);
        var pixels = new Color[frameWidth * frameHeight];
        texture.GetData(0, frame, pixels, 0, pixels.Length);
        using var cropped = new Texture2D(texture.GraphicsDevice, frameWidth, frameHeight);
        cropped.SetData(pixels);
        using var stream = File.Create(Path.Combine(directory, fileName + "-frame0.png"));
        cropped.SaveAsPng(stream, frameWidth, frameHeight);
    }

    private static string GetLogPath()
    {
        string output = Environment.GetEnvironmentVariable("MILKCRAFT_TERRARIA_SMOKE_OUT")
            ?? Environment.CurrentDirectory;
        Directory.CreateDirectory(output);
        return Path.Combine(output, "native-held-layer.log");
    }

    private static string GetHeadwearLogPath()
    {
        string output = Environment.GetEnvironmentVariable("MILKCRAFT_TERRARIA_SMOKE_OUT")
            ?? Environment.CurrentDirectory;
        Directory.CreateDirectory(output);
        return Path.Combine(output, "native-headwear-drawdata.log");
    }

    private static string GetOutputDirectory() =>
        Environment.GetEnvironmentVariable("MILKCRAFT_TERRARIA_SMOKE_OUT") ?? Environment.CurrentDirectory;

}

