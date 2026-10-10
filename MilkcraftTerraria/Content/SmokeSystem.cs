using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using System.Text.Json;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ObjectData;
using Terraria.DataStructures;
using MilkcraftTerraria.Content.Items;
using MilkcraftTerraria.Content.Tiles;

namespace MilkcraftTerraria.Content;

/// <summary>
/// Opt-in integration check used only by the isolated test profile. The normal
/// game never writes files or creates preview render targets.
/// Set MILKCRAFT_TERRARIA_SMOKE=1 and MILKCRAFT_TERRARIA_SMOKE_OUT to enable.
/// </summary>
public sealed class SmokeSystem : ModSystem
{
    private static bool _weaponDiagnosticsEnabled;
    private static bool _vfxLightCapture;
    private static readonly Dictionary<int, List<Vector3>> _vfxLightSamples = new();

    internal static void ResetVfxLightCapture()
    {
        _vfxLightSamples.Clear();
        _vfxLightCapture = true;
    }

    internal static IReadOnlyList<Vector3> GetVfxLightSamples(int projectileType)
        => _vfxLightSamples.TryGetValue(projectileType, out List<Vector3> samples) ? samples : Array.Empty<Vector3>();
    private static readonly System.Collections.Generic.Dictionary<int, int> _weaponLightLogCounts = new();
    private readonly System.Collections.Generic.List<Projectile> _weaponDiagnosticProjectiles = new();
    public static bool ForcePreviewLighting { get; set; }
    public static bool UseGripOriginForPreview { get; set; }
    public static bool UseOriginalKnightSwordDrawForPreview { get; set; }
    public static int NativeHeldDrawCount { get; set; }
    public static bool CaptureHeadwearNativeData { get; private set; }
    public static bool CaptureNativeEyeMap { get; set; }
    public static string NativeEyeMapLabel { get; set; } = "";
    public static bool CaptureHeadGlowDraws { get; set; }
    public static string HeadGlowCaptureLabel { get; set; } = "";
    public static int HeadGlowDrawCount { get; set; }
    public static bool CaptureTier3T4ArmorData { get; set; }
    public static int ArmorCaptureDrawCount { get; set; }
    public static bool CaptureArmReachData { get; set; }
    public static bool CaptureArmReachReferenceOnly { get; set; }
    public static int ArmReachCaptureDrawCount { get; set; }
    public static int ArmReachNativeHandDrawCount { get; set; }
    public static int ArmReachReferenceHandPixels { get; set; }
    public static int ArmReachCoveredHandPixels { get; set; }
    public static int ArmReachCoverageDrawPairs { get; set; }
    public static string HeadwearCaptureLabel { get; private set; } = "";
    public static int HeadwearCaptureDrawCount { get; set; }
    public static bool CaptureAccessoryDraws { get; set; }
    public static bool CaptureRobeLegDraws { get; set; }
    public static string RobeLegCaptureLabel { get; set; } = "";
    public static int RobeLegCaptureDrawCount { get; set; }
    public static int RobeLegSourceAlphaPixels { get; set; }
    public static bool RobeLegSourceInsideTexture { get; set; }
    public static string NativePoseLabel { get; set; } = "";
    public static string AccessoryCaptureLabel { get; set; } = "";
    public static string AccessoryCaptureSearch { get; set; } = "";
    public static int AccessoryCaptureDrawCount { get; set; }
    public static int AccessorySourceAlphaPixels { get; set; }
    public static bool AccessorySourceInsideTexture { get; set; }
    public static int AccessorySourceFrameY { get; set; }
    public static int NativeBowDrawCount { get; set; }
    private bool _ran;
    private bool _recipesReady;
    private readonly bool _enabled = Environment.GetEnvironmentVariable("MILKCRAFT_TERRARIA_SMOKE") == "1";
    private string OutputDirectory => Environment.GetEnvironmentVariable("MILKCRAFT_TERRARIA_SMOKE_OUT")
        ?? Path.Combine(Environment.CurrentDirectory, "smoke-output");

    public static string GetNativeEyeMapLogPath() => Path.Combine(
        Environment.GetEnvironmentVariable("MILKCRAFT_TERRARIA_SMOKE_OUT") ?? Environment.CurrentDirectory,
        "native-eye-map-reference.log");

    public static string GetHeadGlowLogPath() => Path.Combine(
        Environment.GetEnvironmentVariable("MILKCRAFT_TERRARIA_SMOKE_OUT") ?? Environment.CurrentDirectory,
        "native-head-glow-drawdata.log");

    public override void Load()
    {
        if (_enabled && !Main.dedServ)
            Main.OnPostDraw += Main_OnPostDraw;
    }

    public override void Unload()
    {
        if (_enabled && !Main.dedServ)
            Main.OnPostDraw -= Main_OnPostDraw;
    }

    public override void PostAddRecipes() => _recipesReady = true;

    private void Main_OnPostDraw(GameTime gameTime)
    {
        bool oreReferenceRequested = Environment.GetEnvironmentVariable("MILKCRAFT_TERRARIA_PROGRESSION_ORE_REFERENCE") == "1";
        if (!_enabled || (!_recipesReady && !oreReferenceRequested) || _ran || Main.dedServ || Main.graphics?.GraphicsDevice is null)
            return;

        _ran = true;
        Directory.CreateDirectory(OutputDirectory);
        string logPath = Path.Combine(OutputDirectory, "milkcraft-client-render.log");
        try
        {
            if (Environment.GetEnvironmentVariable("MILKCRAFT_TERRARIA_PLATFORM_MULTIPLAYER_DIAGNOSTIC") == "1")
            {
                string result = PlatformMultiplayerDiagnostics.Run();
                File.WriteAllText(Path.Combine(OutputDirectory, "platform-multiplayer-reproduction.log"), result + "\n");
                File.WriteAllText(logPath, "MILKCRAFT TERRARIA CLIENT RENDER PASS\n" + result + "\n");
                return;
            }

            if (Environment.GetEnvironmentVariable("MILKCRAFT_TERRARIA_BOSS_VISUAL_DIAGNOSTIC") == "1")
            {
                string result = ProgressionBossVisualDiagnostics.Render(OutputDirectory);
                File.WriteAllText(Path.Combine(OutputDirectory, "boss-visual-diagnostics.log"), result + "\n");
                File.WriteAllText(logPath, "MILKCRAFT TERRARIA CLIENT RENDER PASS\n" + result + "\n");
                return;
            }

            if (Environment.GetEnvironmentVariable("MILKCRAFT_TERRARIA_BOSS_PET_DIAGNOSTIC") == "1")
            {
                string result = MilkcraftBossPetDiagnostics.Run();
                string marker = "candidate-build=0.3.2-world-feedback\n";
                File.WriteAllText(Path.Combine(OutputDirectory, "boss-pet-diagnostics.log"), marker + result + "\n");
                File.WriteAllText(logPath, "MILKCRAFT TERRARIA CLIENT RENDER PASS\n" + marker + result + "\n");
                return;
            }

            if (Environment.GetEnvironmentVariable("MILKCRAFT_TERRARIA_BOSS_NATIVE_DIAGNOSTIC") == "1")
            {
                string result = ProgressionBossNativeDiagnostics.Run();
                File.WriteAllText(Path.Combine(OutputDirectory, "boss-native-drops-and-damage.log"), result + "\n");
                File.WriteAllText(logPath, "MILKCRAFT TERRARIA CLIENT RENDER PASS\n" + result + "\n");
                return;
            }

            if (Environment.GetEnvironmentVariable("MILKCRAFT_TERRARIA_BOSS_SUMMON_DIAGNOSTIC") == "1")
            {
                string result = ProgressionBossSummonDiagnostics.Run();
                File.WriteAllText(Path.Combine(OutputDirectory, "boss-summon-itemcheck.log"), result + "\n");
                File.WriteAllText(logPath, "MILKCRAFT TERRARIA CLIENT RENDER PASS\n" + result + "\n");
                return;
            }

            if (Environment.GetEnvironmentVariable("MILKCRAFT_TERRARIA_MOUNT_DIAGNOSTIC") == "1")
            {
                string expansionMountResult = AdvancedMountExpansionDiagnostics.RenderStandingRiders(OutputDirectory);
                string legacyMountResult = Environment.GetEnvironmentVariable("MILKCRAFT_TERRARIA_MOUNT_LEGACY_DIAGNOSTIC") == "1"
                    ? MountDiagnostics.Run()
                    : "legacy-mount-regression=not-run (preserved prior 0.2.12 result)\n";
                File.WriteAllText(Path.Combine(OutputDirectory, "mount-diagnostics.log"), legacyMountResult);
                File.WriteAllText(Path.Combine(OutputDirectory, "mount-expansion-diagnostics.log"), expansionMountResult);
                File.WriteAllText(logPath, "MILKCRAFT TERRARIA CLIENT RENDER PASS\n" + legacyMountResult + expansionMountResult);
                return;
            }

            if (Environment.GetEnvironmentVariable("MILKCRAFT_TERRARIA_PREFIX_STACK_DIAGNOSTIC") == "1")
            {
                string result = ProgressionEnchantPrefixDiagnostics.Run();
                File.WriteAllText(Path.Combine(OutputDirectory, "enchant-prefix-stack.log"), result + "\n");
                File.WriteAllText(logPath, "MILKCRAFT TERRARIA CLIENT RENDER PASS\n" + result + "\n");
                return;
            }

            if (Environment.GetEnvironmentVariable("MILKCRAFT_TERRARIA_SPECTRE_HOOD_REFERENCE") == "1")
            {
                SpectreHoodReference.Export(OutputDirectory);
                File.WriteAllText(logPath,
                    "MILKCRAFT TERRARIA CLIENT RENDER PASS\n" +
                    $"vanilla-spectre-hood-reference=PASS itemType={ItemID.SpectreHood} atlas=native-spectre-hood-armorhead.png " +
                    "frame0=native-spectre-hood-frame0.png maleDirections=native-spectre-hood-idle-directions.png\n");
                return;
            }

            if (Environment.GetEnvironmentVariable("MILKCRAFT_TERRARIA_INVENTORY_ICON_DIAGNOSTIC") == "1")
            {
                string iconResult = InventoryIconDiagnostics.Render(OutputDirectory);
                File.WriteAllText(logPath, "MILKCRAFT TERRARIA CLIENT RENDER PASS\n" + iconResult + "\n");
                return;
            }

            if (Environment.GetEnvironmentVariable("MILKCRAFT_TERRARIA_DAZHOU_REFERENCE") == "1")
            {
                Main.instance.LoadItem(ItemID.BrokenHeroSword);
                Texture2D brokenHeroSword = TextureAssets.Item[ItemID.BrokenHeroSword].Value;
                if (brokenHeroSword.Width <= 1 || brokenHeroSword.Height <= 1)
                    throw new InvalidOperationException($"BrokenHeroSword texture did not load; got {brokenHeroSword.Width}x{brokenHeroSword.Height}.");
                string referenceDirectory = Environment.GetEnvironmentVariable("MILKCRAFT_TERRARIA_DAZHOU_REFERENCE_OUT")
                    ?? OutputDirectory;
                Directory.CreateDirectory(referenceDirectory);
                string referencePath = Path.Combine(referenceDirectory, "BrokenHeroSword-native.png");
                using (FileStream stream = File.Create(referencePath))
                    brokenHeroSword.SaveAsPng(stream, brokenHeroSword.Width, brokenHeroSword.Height);
                File.WriteAllText(logPath,
                    "MILKCRAFT TERRARIA CLIENT RENDER PASS\n" +
                    $"vanilla-broken-hero-sword=PASS itemType={ItemID.BrokenHeroSword} dimensions={brokenHeroSword.Width}x{brokenHeroSword.Height} png={referencePath}\n");
                return;
            }

            if (Environment.GetEnvironmentVariable("MILKCRAFT_TERRARIA_PROGRESSION_ORE_REFERENCE") == "1")
            {
                string referenceDirectory = Environment.GetEnvironmentVariable("MILKCRAFT_TERRARIA_PROGRESSION_ORE_REFERENCE_OUT")
                    ?? OutputDirectory;
                string result = ProgressionReferenceDiagnostics.ExportNativeEvilOreTiles(referenceDirectory);
                File.WriteAllText(logPath, "MILKCRAFT TERRARIA CLIENT RENDER PASS\n" + result + "\n");
                return;
            }

            if (Environment.GetEnvironmentVariable("MILKCRAFT_TERRARIA_PROGRESSION_VISUALS") == "1")
            {
                string visualResult = ProgressionVisualDiagnostics.Render(OutputDirectory);
                File.WriteAllText(Path.Combine(OutputDirectory, "progression-visuals.log"), visualResult + "\n");
                File.WriteAllText(logPath, "MILKCRAFT TERRARIA CLIENT RENDER PASS\n" + visualResult + "\n");
                return;
            }

            if (Environment.GetEnvironmentVariable("MILKCRAFT_TERRARIA_ENCHANT_STATS_ONLY") == "1")
            {
                string enchantStats = ProgressionEnchantDiagnostics.RunStableOnly();
                File.WriteAllText(Path.Combine(OutputDirectory, "progression-enchant-stats.log"), enchantStats + "\n");
                File.WriteAllText(logPath, "MILKCRAFT TERRARIA CLIENT RENDER PASS\n" + enchantStats + "\n");
                return;
            }

            if (Environment.GetEnvironmentVariable("MILKCRAFT_TERRARIA_ENCHANT_HIT_DIAGNOSTIC") == "1")
            {
                string enchantBehavior = ProgressionEnchantDiagnostics.RunEffectsOnly();
                string hitResult = ProgressionEnchantAuditDiagnostics.RunFocusedGroundedSplash(OutputDirectory);
                string report = enchantBehavior + "\n" + hitResult;
                File.WriteAllText(Path.Combine(OutputDirectory, "enchant-hit-diagnostics.log"), report + "\n");
                File.WriteAllText(logPath, "MILKCRAFT TERRARIA CLIENT RENDER PASS\n" + report + "\n");
                return;
            }

            if (Environment.GetEnvironmentVariable("MILKCRAFT_TERRARIA_PROGRESSION_DIAGNOSTIC") == "1")
            {
                string alchemyResult = ProgressionAlchemyDiagnostics.Run();
                string bossResult = ProgressionBossDiagnostics.Run();
                string enchantResult = ProgressionEnchantDiagnostics.Run();
                string result = alchemyResult + "\n" + bossResult + "\n" + enchantResult;
                File.WriteAllText(Path.Combine(OutputDirectory, "progression-diagnostics.log"), result + "\n");
                File.WriteAllText(logPath, "MILKCRAFT TERRARIA CLIENT RENDER PASS\n" + result + "\n");
                return;
            }

            if (Environment.GetEnvironmentVariable("MILKCRAFT_TERRARIA_BOSS_RELEASE_DIAGNOSTIC") == "1")
            {
                string result = ProgressionBossReleaseDiagnostics.Run();
                File.WriteAllText(Path.Combine(OutputDirectory, "boss-release-diagnostics.log"), result + "\n");
                File.WriteAllText(logPath, "MILKCRAFT TERRARIA CLIENT RENDER PASS\n" + result + "\n");
                return;
            }

            if (Environment.GetEnvironmentVariable("MILKCRAFT_TERRARIA_ENCHANT_PAYMENT_DIAGNOSTIC") == "1")
            {
                string result = ProgressionEnchantDiagnostics.Run();
                string uiResult = ProgressionVisualDiagnostics.RenderEnchantUiOnly(OutputDirectory);
                File.WriteAllText(Path.Combine(OutputDirectory, "enchant-payment-diagnostics.log"), result + "\n" + uiResult + "\n");
                File.WriteAllText(logPath, "MILKCRAFT TERRARIA CLIENT RENDER PASS\n" + result + "\n" + uiResult + "\n");
                return;
            }

            if (Environment.GetEnvironmentVariable("STATION_UI_DIAGNOSTIC") == "1")
            {
                TestStationPlacementAndDrops();
                string stationRecipeResult = TestDedicatedStationRecipes();
                string alchemyResult = ProgressionAlchemyDiagnostics.Run();
                string result = ProgressionEnchantDiagnostics.Run();
                string uiResult = ProgressionVisualDiagnostics.RenderEnchantUiOnly(OutputDirectory);
                string report = alchemyResult + "\n" + result + "\n" + uiResult + "\n" +
                    "native-StationPlacementBreak=PASS 7/7 workbench/furnace/loom/forge/distillery/alchemy/enchanting; enchanting-only-custom-overlay=PASS\n" +
                    stationRecipeResult + "\n";
                File.WriteAllText(Path.Combine(OutputDirectory, "station-ui-diagnostics.log"), report);
                File.WriteAllText(logPath, "MILKCRAFT TERRARIA CLIENT RENDER PASS\n" + report);
                return;
            }

            if (Environment.GetEnvironmentVariable("MILKCRAFT_TERRARIA_BALANCE_DIAGNOSTIC") == "1")
            {
                string balanceResult = BalanceRuntimeDiagnostics.Export(OutputDirectory);
                File.WriteAllText(logPath, "MILKCRAFT TERRARIA CLIENT RENDER PASS\n" + balanceResult + "\n");
                return;
            }

            if (Environment.GetEnvironmentVariable("MILKCRAFT_TERRARIA_PASSIVE_DIAGNOSTIC") == "1")
            {
                string result = PassiveSmokeDiagnostics.Run(OutputDirectory);
                string suitStateResult = RunPassiveSuitStateFixture();
                string hurtResult = RunPassiveShieldHurtFixture();
                string manaResult = RunPassiveManaCycleFixture();
                var weaponState = RunPassiveWeaponNativeFixture();
                string procResult = RunPassiveProcHookFixture();
                PassiveSmokeDiagnostics.RenderActualProjectilePreviews(
                    Path.Combine(OutputDirectory, "paired-projectiles-dark-preview.png"), weaponState.Projectiles);
                string vfxVisualResult = VfxSmokeDiagnostics.Render(OutputDirectory, weaponState.Projectiles);
                RenderAccessoryPreviews();
                File.WriteAllText(Path.Combine(OutputDirectory, "passive-focused-diagnostics.log"), result + "\n" + suitStateResult + "\n" + hurtResult + "\n" + manaResult + "\n" + weaponState.Report + "\n" + procResult + "\n" + vfxVisualResult + "\n" +
                    "Native accessory PlayerRenderer preview: native-accessory-visibility.png (including the IronQuiver back slot).\n");
                File.WriteAllText(logPath, "MILKCRAFT TERRARIA CLIENT RENDER PASS\n" + result + "\n" + suitStateResult + "\n" + hurtResult + "\n" + manaResult + "\n" + weaponState.Report + "\n" + procResult + "\n" + vfxVisualResult + "\n");
                return;
            }

            if (Environment.GetEnvironmentVariable("MILKCRAFT_TERRARIA_FIT_REPAIR_DIAGNOSTIC") == "1")
            {
                File.WriteAllText(Path.Combine(OutputDirectory, "native-12-tier-arm-reach-drawdata.log"), string.Empty);
                RenderTier3T4ArmorFrames();
                RenderNativeArmReachDiagnostics();
                File.WriteAllText(logPath,
                    "MILKCRAFT TERRARIA CLIENT RENDER PASS\n" +
                    $"native-12-tier-armor-frames=PASS right/left x stand/walk/jump: {Path.Combine(OutputDirectory, "native-12-tier-armor-frames-right.png")}; {Path.Combine(OutputDirectory, "native-12-tier-armor-frames-left.png")}\n" +
                    $"native-12-tier-arm-reach=PASS 48 cases, segmented armor alpha covers native hand UV, native skin hands suppressed: {Path.Combine(OutputDirectory, "native-12-tier-arm-reach-T1.png")} ... T4.png\n");
                return;
            }

            if (Environment.GetEnvironmentVariable("MILKCRAFT_TERRARIA_WEAPON_DIAGNOSTIC") == "1")
            {
                _weaponDiagnosticsEnabled = true;
                _weaponLightLogCounts.Clear();
                _weaponDiagnosticProjectiles.Clear();
                File.WriteAllText(Path.Combine(OutputDirectory, "weapon-diagnostics.log"),
                    "0.2.4 local weapon probes; outputs are smoke-only and use runtime Terraria defaults.\n");
                TestTerraBladeRuntimeDefaults();
                TestSoulforgedTerraBladeRuntimeParity();
                TestKnightSwordWave();
                TestTier3T4SwordWaves();
                RenderWeaponDiagnosticProjectiles();
                File.WriteAllText(logPath,
                    "MILKCRAFT TERRARIA CLIENT RENDER PASS\n" +
                    $"weapon-focused-diagnostic=PASS defaults/cadence/collision/light/swing cycles; image={Path.Combine(OutputDirectory, "native-weapon-diagnostics.png")} log={Path.Combine(OutputDirectory, "weapon-diagnostics.log")}\n");
                _weaponDiagnosticsEnabled = false;
                return;
            }

            if (Environment.GetEnvironmentVariable("MILKCRAFT_TERRARIA_DAZHOU_DIAGNOSTIC") == "1")
            {
                string dazhouResult = DazhouSwordDiagnostics.Run(OutputDirectory);
                File.WriteAllText(logPath, "MILKCRAFT TERRARIA CLIENT RENDER PASS\n" + dazhouResult + "\n");
                return;
            }

            if (Environment.GetEnvironmentVariable("MILKCRAFT_TERRARIA_RENDER_DIAGNOSTIC") == "1")
            {
                // Focused native rendering only. This avoids unrelated recipe
                // and station fixtures while reviewing the full T1-T4 armor
                // and weapon matrix.
                File.WriteAllText(Path.Combine(OutputDirectory, "native-held-layer.log"), string.Empty);
                File.WriteAllText(Path.Combine(OutputDirectory, "native-12-tier-armor-drawdata.log"), string.Empty);
                ExportVanillaHallowedBodyComposite();
                ExportNativePlayerCompositeArmTextures();
                RenderTier3T4ArmorFrames();
                RenderTier3T4BowDirections();
                RenderTier3T4SwordSwingFrames();
                RenderActionPreviews();
                RenderNativeArmReachDiagnostics();
                RenderTier3T4HeadwearReference();
                File.WriteAllText(logPath,
                    "MILKCRAFT TERRARIA CLIENT RENDER PASS\n" +
                    $"focused-native-render-diagnostic=PASS armorDrawData={Path.Combine(OutputDirectory, "native-12-tier-armor-drawdata.log")} " +
                    $"bowDrawData={Path.Combine(OutputDirectory, "native-held-layer.log")} " +
                    $"armorRight={Path.Combine(OutputDirectory, "native-12-tier-armor-frames-right.png")} " +
                    $"armorLeft={Path.Combine(OutputDirectory, "native-12-tier-armor-frames-left.png")} " +
                    $"arrowPreview={Path.Combine(OutputDirectory, "native-four-tier-bow-directions.png")} " +
                    $"swordPreview={Path.Combine(OutputDirectory, "native-four-tier-sword-swing-frames.png")} " +
                    $"staffPreview={Path.Combine(OutputDirectory, "milkcraft-mage-held-directions.png")} " +
                    $"armReach={Path.Combine(OutputDirectory, "native-12-tier-arm-reach-T1.png")} " +
                    $"headgearFlags={Path.Combine(OutputDirectory, "native-headgear-flags.log")}\n");
                return;
            }

            var modItemTypes = Mod.GetContent<ModItem>().Select(item => (int)item.Type).ToHashSet();
            var modTileTypes = Mod.GetContent<ModTile>().Select(tile => (int)tile.Type).ToArray();
            int[] outputs = Main.recipe.Take(Recipe.numRecipes)
                .Where(recipe => recipe is not null && modItemTypes.Contains(recipe.createItem.type))
                .Select(recipe => recipe.createItem.type).ToArray();
            Require(modItemTypes.Count == 159, $"expected 159 registered items, found {modItemTypes.Count}");
            Require(modTileTypes.Length == 23, $"expected 23 registered tiles, found {modTileTypes.Length}");
            Require(outputs.Length == 100, $"expected 100 mod-output recipes, found {outputs.Length}; total recipes={Recipe.numRecipes}");
            Require(outputs.Count(type => type == ModContent.ItemType<DazhouIronSword>()) == 1,
                "DazhouIronSword must have exactly one registered recipe");
            Require(!outputs.Contains(ModContent.ItemType<HiddenIronSword>()),
                "HiddenIronSword is a drop-only material and must not have a recipe");
            Require(Main.recipe.Take(Recipe.numRecipes).Any(recipe => recipe.createItem.type == ItemID.Leather),
                "expected the fourth supplemental recipe to add Leather");
            TestTier3T4Contracts(modItemTypes, outputs);
            WriteRecipeDump(modItemTypes);
            string stationFailure = null;
            try { TestStationPlacementAndDrops(); }
            catch (Exception ex) { stationFailure = ex.ToString(); }
            RenderPreviews();
            RenderTier3T4ArmorFrames();
            File.WriteAllText(Path.Combine(OutputDirectory, "native-held-layer.log"), string.Empty);
            RenderActionPreviews();
            RenderNativeArmReachDiagnostics();
            RenderWeaponOrientationPreviews();
            RenderTier3T4WeaponPreviews();
            TestKnightSwordWave();
            TestTier3T4SwordWaves();
            RenderAccessoryPreviews();
            RenderTier3T4AccessoryPreviews();
            RenderShieldCobaltComparison();
            RenderRobeVisibilityABPreviews();
            RenderHeadwearPreview();
            if (stationFailure != null)
                throw new InvalidOperationException("Station placement/break smoke failed; preview was still rendered.\n" + stationFailure);
            File.WriteAllText(Path.Combine(OutputDirectory, "milkcraft-client-render.log"),
                $"MILKCRAFT TERRARIA CLIENT RENDER PASS\nitems=159 tiles=23 mod-output-recipes=100 leather-recipe=PASS total-recipes={Recipe.numRecipes} recipe-dump=recipe-dump-159.json\n" +
                "tier3-tier4=38 new items/recipes; armor progression totals 50/35/31 and 73/51/42\n" +
                "tier-license=one-time per tier, character-saved, current-world stage checked; old-world flags supported\n" +
                $"7-stations-place-break-drop-once=PASS\n" +
                "native-armor=12 sets x stand/walk/jump x left/right; swords=4 x left/right x first/middle/last; bows=4 x left/right with arrow; staffs=4 x horizontal/raised x left/right\n" +
                "native-arm-reach=12 sets x horizontal/raised x left/right with class-appropriate sword/bow/staff and captured native hand/sleeve DrawData\n" +
                "headgear-flags=0.2.0 native face/hair behavior for hoods, warrior helmets, and LeatherHeadband\n" +
                $"native-held-item-draws={NativeHeldDrawCount}; native-tint=smoke-only-white\n" +
                $"headwear-render-fixture=PASS actors=8 player-layer-drawdata-actors={HeadwearCaptureDrawCount}; face rules checked separately\n" +
                "knight-wave-directions=4; successive-autoReuse-cycles=2 at 1.0x and 1.5x melee speed\n" +
                "t3-t4-sword-wave-cycles=PASS; Templar/Soulforged, left/right, 1.0x/1.5x, one wave per swing\n" +
                "shield-side-by-side=iron/knight/vanilla-Cobalt; robe-HidesBottomSkin-AB=cloth/energy idle+walk+jump\n");
        }
        catch (Exception ex)
        {
            _weaponDiagnosticsEnabled = false;
            File.WriteAllText(logPath, "MILKCRAFT TERRARIA CLIENT RENDER FAIL\n" + ex);
            throw;
        }
    }

    /// <summary>Applies real world lighting and records actual calls during the opt-in weapon fixture.</summary>
    public static void AddWeaponLight(Projectile projectile, Vector3 color)
    {
        Lighting.AddLight(projectile.Center, color);
        if (_vfxLightCapture)
        {
            if (!_vfxLightSamples.TryGetValue(projectile.type, out List<Vector3> samples))
                _vfxLightSamples[projectile.type] = samples = new List<Vector3>(16);
            if (samples.Count < 64)
                samples.Add(color);
        }
        if (!_weaponDiagnosticsEnabled)
            return;
        int count = _weaponLightLogCounts.TryGetValue(projectile.type, out int current) ? current : 0;
        if (count >= 3)
            return;
        _weaponLightLogCounts[projectile.type] = count + 1;
        File.AppendAllText(Path.Combine(Environment.GetEnvironmentVariable("MILKCRAFT_TERRARIA_SMOKE_OUT") ??
            Environment.CurrentDirectory, "weapon-diagnostics.log"),
            $"lighting.AddLight=called type={projectile.type} center={projectile.Center} rgb={color} sample={count + 1}\n");
    }

    public static void LogTemplarTarget(Projectile projectile, int targetIndex, string action)
    {
        if (!_weaponDiagnosticsEnabled)
            return;
        string target = targetIndex >= 0 && targetIndex < Main.maxNPCs
            ? $"npc={Main.npc[targetIndex].whoAmI} center={Main.npc[targetIndex].Center} chaseable={Main.npc[targetIndex].CanBeChasedBy(projectile)} distance={Vector2.Distance(projectile.Center, Main.npc[targetIndex].Center):0.0} los={Collision.CanHitLine(projectile.Center, 1, 1, Main.npc[targetIndex].Center, 1, 1)}"
            : "target=none";
        File.AppendAllText(Path.Combine(Environment.GetEnvironmentVariable("MILKCRAFT_TERRARIA_SMOKE_OUT") ??
            Environment.CurrentDirectory, "weapon-diagnostics.log"),
            $"templar-target action={action} owner={projectile.owner} ownerAuthority={(Main.netMode == NetmodeID.SinglePlayer || (Main.netMode == NetmodeID.MultiplayerClient && projectile.owner == Main.myPlayer))} ai0={projectile.ai[0]} {target}\n");
    }

    public static void LogTemplarTurn(Projectile projectile, float turnRadians)
    {
        if (!_weaponDiagnosticsEnabled || projectile.timeLeft % 6 != 0)
            return;
        File.AppendAllText(Path.Combine(Environment.GetEnvironmentVariable("MILKCRAFT_TERRARIA_SMOKE_OUT") ??
            Environment.CurrentDirectory, "weapon-diagnostics.log"),
            $"templar-turn owner={projectile.owner} targetIndex={(int)projectile.ai[0] - 1} turnRad={turnRadians:0.000} velocity={projectile.velocity}\n");
    }

    private void TestTerraBladeRuntimeDefaults()
    {
        Item terra = new();
        terra.SetDefaults(ItemID.TerraBlade);
        Projectile terraProjectile = new();
        terraProjectile.SetDefaults(terra.shoot);
        Item soul = ContentSamples.ItemsByType[ModContent.ItemType<SoulforgedSword>()];
        Require(soul.useTime == terra.useTime && soul.useAnimation == terra.useAnimation &&
            Math.Abs(soul.shootSpeed - terra.shootSpeed) < 0.001f && soul.autoReuse == terra.autoReuse &&
            soul.reuseDelay == terra.reuseDelay && soul.useTurn == terra.useTurn,
            "SoulforgedSword cadence/speed must match locally loaded TerraBlade defaults");
        bool singlePlayerOwner = TemplarWave.HasTargetSelectionAuthority(0, 0, NetmodeID.SinglePlayer);
        bool owningClient = TemplarWave.HasTargetSelectionAuthority(3, 3, NetmodeID.MultiplayerClient);
        bool remoteClient = TemplarWave.HasTargetSelectionAuthority(3, 4, NetmodeID.MultiplayerClient);
        bool dedicatedServer = TemplarWave.HasTargetSelectionAuthority(3, 3, NetmodeID.Server);
        Require(singlePlayerOwner && owningClient && !remoteClient && !dedicatedServer,
            "Templar target selection must be single-player/owning-client only");
        File.AppendAllText(Path.Combine(OutputDirectory, "weapon-diagnostics.log"),
            $"terraBlade-item type={ItemID.TerraBlade} useTime={terra.useTime} useAnimation={terra.useAnimation} shootSpeed={terra.shootSpeed:0.###} autoReuse={terra.autoReuse} reuseDelay={terra.reuseDelay} useTurn={terra.useTurn}\n" +
            $"terraBlade-projectile type={terra.shoot} size={terraProjectile.width}x{terraProjectile.height} aiStyle={terraProjectile.aiStyle} timeLeft={terraProjectile.timeLeft} extraUpdates={terraProjectile.extraUpdates} scale={terraProjectile.scale:0.###} penetrate={terraProjectile.penetrate} tileCollide={terraProjectile.tileCollide}\n" +
            $"soulforged-item useTime={soul.useTime} useAnimation={soul.useAnimation} shootSpeed={soul.shootSpeed:0.###} autoReuse={soul.autoReuse} reuseDelay={soul.reuseDelay} useTurn={soul.useTurn} attackSpeedOnlyAffectsWeaponAnimation={soul.attackSpeedOnlyAffectsWeaponAnimation} bonusAttackSpeedMultiplier={ItemID.Sets.BonusAttackSpeedMultiplier[ModContent.ItemType<SoulforgedSword>()]:0.###} projectile={ModContent.ProjectileType<SoulforgedWave>()} size=8x8 penetrate=-1 tileCollide=true AIType=985 AIStyle=191 (TerraBlade penetrate={terraProjectile.penetrate}, tileCollide={terraProjectile.tileCollide})\n" +
            $"terraBlade-attack-speed attackSpeedOnlyAffectsWeaponAnimation={terra.attackSpeedOnlyAffectsWeaponAnimation} bonusAttackSpeedMultiplier={ItemID.Sets.BonusAttackSpeedMultiplier[ItemID.TerraBlade]:0.###}\n" +
            $"templar-target-authority singlePlayer={singlePlayerOwner} owningClient={owningClient} remoteClient={remoteClient} server={dedicatedServer}\n");

        foreach (float speed in new[] { 1f, 1.5f })
        {
            var terraCycle = MeasureTwoSwingInterval(ItemID.TerraBlade, terra.shoot, speed);
            var soulCycle = MeasureTwoSwingInterval(ModContent.ItemType<SoulforgedSword>(),
                ModContent.ProjectileType<SoulforgedWave>(), speed);
            Require(terraCycle.Interval == soulCycle.Interval && terraCycle.Animation == soulCycle.Animation,
                $"Soulforged cadence must match TerraBlade at {speed:0.0}x: Terra={terraCycle.Interval}/{terraCycle.Animation}, Soul={soulCycle.Interval}/{soulCycle.Animation}");
            File.AppendAllText(Path.Combine(OutputDirectory, "weapon-diagnostics.log"),
                $"itemcheck-cadence speed={speed:0.0}x terraBlade={terraCycle.Interval}ticks/{terraCycle.Animation}anim/{terraCycle.ProjectileCount}waves soulforged={soulCycle.Interval}ticks/{soulCycle.Animation}anim/{soulCycle.ProjectileCount}waves result=PASS\n");
        }
        TestTemplarExtendedRange();
    }

    private void TestTemplarExtendedRange()
    {
        int playerIndex = Math.Clamp(Main.myPlayer, 0, Main.player.Length - 1);
        Player previousPlayer = Main.player[playerIndex];
        Projectile[] previousProjectiles = Main.projectile;
        NPC[] previousNpcs = Main.npc;
        Dust[] previousDust = Main.dust;
        bool previousMenu = Main.gameMenu, previousPaused = Main.gamePaused;
        Vector2 previousScreen = Main.screenPosition;
        try
        {
            Main.gameMenu = false;
            Main.gamePaused = false;
            Main.screenPosition = Vector2.Zero;
            Main.projectile = Enumerable.Range(0, previousProjectiles.Length).Select(_ => new Projectile()).ToArray();
            Main.npc = Enumerable.Range(0, previousNpcs.Length).Select(_ => new NPC()).ToArray();
            Main.dust = Enumerable.Range(0, previousDust.Length).Select(_ => new Dust()).ToArray();

            Projectile wave = FireParityItemCheck(playerIndex, ModContent.ItemType<TemplarSword>(),
                ModContent.ProjectileType<TemplarWave>(), ItemID.IronHelmet, ItemID.IronChainmail, ItemID.IronGreaves);
            Require(wave.timeLeft == 60 && Math.Abs(wave.velocity.Length() - 10.5f) < .01f,
                $"Templar range upgrade should retain speed 10.5 and set 60 frame life; got {wave.velocity.Length():0.###}/{wave.timeLeft}");
            wave.Center = new Vector2(1400f, 180f);
            wave.velocity = Vector2.UnitX * 10.5f;
            wave.tileCollide = false;
            Vector2 start = wave.Center;
            for (int tick = 0; tick < 60; tick++)
            {
                wave.Update(wave.whoAmI);
                if (tick < 59)
                    Require(wave.active, $"Templar should remain alive through range tick {tick + 1}/60");
            }
            Vector2 rangeVector = wave.Center - start;
            Require(!wave.active && Math.Abs(rangeVector.Length() - 630f) < 1f,
                $"Templar 60-frame straight-line range should be 630px at 10.5 speed, got {rangeVector.Length():0.###}");

            Main.projectile = Enumerable.Range(0, previousProjectiles.Length).Select(_ => new Projectile()).ToArray();
            Main.npc = Enumerable.Range(0, previousNpcs.Length).Select(_ => new NPC()).ToArray();
            wave = FireParityItemCheck(playerIndex, ModContent.ItemType<TemplarSword>(),
                ModContent.ProjectileType<TemplarWave>(), ItemID.IronHelmet, ItemID.IronChainmail, ItemID.IronGreaves);
            wave.Center = new Vector2(1400f, 180f);
            wave.velocity = Vector2.UnitX * 10.5f;
            wave.tileCollide = false;
            NPC target = Main.npc[0];
            target.SetDefaults(NPCID.BlueSlime);
            target.whoAmI = 0;
            target.active = true;
            target.friendly = false;
            target.dontTakeDamage = false;
            target.Center = wave.Center + new Vector2(600f, 0f);
            bool lineAt600 = Collision.CanHitLine(wave.Center, 1, 1, target.Center, 1, 1);
            Require(lineAt600, "Templar 600px fixture lane must have clear native line of sight");
            wave.Update(wave.whoAmI);
            Require((int)wave.ai[0] == 1,
                $"Templar should acquire a visible chaseable NPC 600px away under the 640px radius; ai0={wave.ai[0]}");
            target.active = false;
            target = Main.npc[1];
            target.SetDefaults(NPCID.BlueSlime);
            target.whoAmI = 1;
            target.active = true;
            target.friendly = false;
            target.dontTakeDamage = false;
            target.Center = wave.Center + new Vector2(641f, 0f);
            wave.ai[0] = 0f;
            wave.localAI[1] = 0f;
            wave.Update(wave.whoAmI);
            Require((int)wave.ai[0] == 0,
                $"Templar should not acquire a target beyond 640px; ai0={wave.ai[0]}");
            File.AppendAllText(Path.Combine(OutputDirectory, "weapon-diagnostics.log"),
                $"templar-range=PASS speed={wave.velocity.Length():0.###} lifetime=60 straightTravel={rangeVector.Length():0.###} " +
                $"target600={lineAt600}/acquired target641=not-acquired maxRange=640\n");
        }
        finally
        {
            Main.player[playerIndex] = previousPlayer;
            Main.projectile = previousProjectiles;
            Main.npc = previousNpcs;
            Main.dust = previousDust;
            Main.gameMenu = previousMenu;
            Main.gamePaused = previousPaused;
            Main.screenPosition = previousScreen;
        }
    }

    private (int Interval, int Animation, int ProjectileCount) MeasureTwoSwingInterval(
        int itemType, int projectileType, float meleeSpeed)
    {
        int localPlayerIndex = Math.Clamp(Main.myPlayer, 0, Main.player.Length - 1);
        Player previousPlayer = Main.player[localPlayerIndex];
        Projectile[] previousProjectiles = Main.projectile;
        bool previousMenu = Main.gameMenu;
        int previousMouseX = Main.mouseX;
        int previousMouseY = Main.mouseY;
        try
        {
            Main.gameMenu = false;
            Main.projectile = Enumerable.Range(0, previousProjectiles.Length).Select(_ => new Projectile()).ToArray();
            Player actor = CreateActionPlayer(ModContent.ItemType<KnightHelmet>(),
                ModContent.ItemType<KnightChestplate>(), ModContent.ItemType<KnightGreaves>(),
                itemType, 1, 0f, male: true, pose: 0);
            actor.whoAmI = localPlayerIndex;
            actor.GetModPlayer<Tier3T4ProgressPlayer>().ApplyNetworkState(true, true, false);
            actor.GetAttackSpeed(DamageClass.Melee) = meleeSpeed;
            actor.position = new Vector2(250f, 120f) + Main.screenPosition;
            actor.controlUseItem = true;
            actor.releaseUseItem = false;
            Main.player[localPlayerIndex] = actor;
            Vector2 center = actor.position - Main.screenPosition +
                new Vector2(actor.width * 0.5f, actor.height * 0.5f);
            Main.mouseX = (int)(center.X + 150f);
            Main.mouseY = (int)center.Y;
            var starts = new System.Collections.Generic.List<(int Tick, int Animation)>();
            int lastCount = 0;
            for (int tick = 0; tick < 360 && starts.Count < 2; tick++)
            {
                actor.ItemCheck();
                int count = Main.projectile.Count(projectile => projectile.active &&
                    projectile.owner == localPlayerIndex && projectile.type == projectileType);
                if (count > lastCount)
                {
                    Require(count == lastCount + 1,
                        $"item type {itemType} emitted {count - lastCount} projectile(s) for one swing at {meleeSpeed:0.0}x");
                    starts.Add((tick, actor.itemAnimation));
                    lastCount = count;
                }
            }
            Require(starts.Count == 2, $"item type {itemType} did not complete two ItemCheck swings at {meleeSpeed:0.0}x");
            return (starts[1].Tick - starts[0].Tick, starts[0].Animation, lastCount);
        }
        finally
        {
            Main.player[localPlayerIndex] = previousPlayer;
            Main.projectile = previousProjectiles;
            Main.gameMenu = previousMenu;
            Main.mouseX = previousMouseX;
            Main.mouseY = previousMouseY;
        }
    }

    private void TestSoulforgedTerraBladeRuntimeParity()
    {
        int localPlayerIndex = Math.Clamp(Main.myPlayer, 0, Main.player.Length - 1);
        Player previousPlayer = Main.player[localPlayerIndex];
        Projectile[] previousProjectiles = Main.projectile;
        NPC[] previousNpcs = Main.npc;
        Dust[] previousDust = Main.dust;
        bool previousMenu = Main.gameMenu;
        bool previousPaused = Main.gamePaused;
        bool previousWorldGen = WorldGen.gen;
        Vector2 previousScreen = Main.screenPosition;
        Terraria.Utilities.UnifiedRandom previousRandom = Main.rand;
        try
        {
            Require(Main.tile.Width > 100 && Main.tile.Height > 100,
                "TerraBlade parity requires a loaded native world for Projectile.Colliding LOS checks");
            Main.gameMenu = false;
            Main.gamePaused = false;
            WorldGen.gen = false;
            Main.screenPosition = Vector2.Zero;
            Main.projectile = Enumerable.Range(0, previousProjectiles.Length).Select(_ => new Projectile()).ToArray();
            Main.npc = Enumerable.Range(0, previousNpcs.Length).Select(_ => new NPC()).ToArray();
            Main.dust = Enumerable.Range(0, previousDust.Length).Select(_ => new Dust()).ToArray();
            Main.rand = new Terraria.Utilities.UnifiedRandom(20261008);
            SoulforgedNativeColorScope.ResetDiagnosticCount();

            Item terraItem = new();
            terraItem.SetDefaults(ItemID.TerraBlade);
            Projectile native = FireParityItemCheck(localPlayerIndex, ItemID.TerraBlade,
                terraItem.shoot, ItemID.IronHelmet, ItemID.IronChainmail, ItemID.IronGreaves);
            Projectile soul = FireParityItemCheck(localPlayerIndex, ModContent.ItemType<SoulforgedSword>(),
                ModContent.ProjectileType<SoulforgedWave>(), ItemID.IronHelmet,
                ItemID.IronChainmail, ItemID.IronGreaves);
            Vector2 nativeSpawnCenter = native.Center;
            Vector2 soulSpawnCenter = soul.Center;
            Vector2 nativeSpawnVelocity = native.velocity;
            Vector2 soulSpawnVelocity = soul.velocity;
            float nativeSpawnAi0 = native.ai[0], nativeSpawnAi1 = native.ai[1], nativeSpawnAi2 = native.ai[2];
            float soulSpawnAi0 = soul.ai[0], soulSpawnAi1 = soul.ai[1], soulSpawnAi2 = soul.ai[2];
            File.AppendAllText(Path.Combine(OutputDirectory, "weapon-diagnostics.log"),
                $"itemcheck-spawn native type={native.type} owner={native.owner} ai={nativeSpawnAi0:0.###}/{nativeSpawnAi1:0.###}/{nativeSpawnAi2:0.###} " +
                $"scale={native.scale:0.###} velocity={nativeSpawnVelocity} center={nativeSpawnCenter} timeLeft={native.timeLeft} damage={native.damage}; " +
                $"soul type={soul.type} owner={soul.owner} ai={soulSpawnAi0:0.###}/{soulSpawnAi1:0.###}/{soulSpawnAi2:0.###} " +
                $"scale={soul.scale:0.###} velocity={soulSpawnVelocity} center={soulSpawnCenter} timeLeft={soul.timeLeft} damage={soul.damage}\n");
            Require(Vector2.Distance(nativeSpawnVelocity, soulSpawnVelocity) < 0.01f,
                $"Soulforged real ItemCheck velocity must match TerraBlade; native={nativeSpawnVelocity} soul={soulSpawnVelocity}");
            Require(nativeSpawnAi0 == soulSpawnAi0 && nativeSpawnAi1 == soulSpawnAi1 && nativeSpawnAi2 == soulSpawnAi2,
                $"Soulforged real ItemCheck AI parameters must match TerraBlade; native={nativeSpawnAi0}/{nativeSpawnAi1}/{nativeSpawnAi2} " +
                $"soul={soulSpawnAi0}/{soulSpawnAi1}/{soulSpawnAi2}");

            foreach ((int direction, float attackSpeed) in new[] { (-1, 1f), (1, 1.5f) })
            {
                Main.projectile = Enumerable.Range(0, previousProjectiles.Length).Select(_ => new Projectile()).ToArray();
                Projectile nativeSample = FireParityItemCheck(localPlayerIndex, ItemID.TerraBlade,
                    terraItem.shoot, ItemID.IronHelmet, ItemID.IronChainmail, ItemID.IronGreaves, direction, attackSpeed);
                Vector2 nativeVelocity = nativeSample.velocity;
                Vector2 nativeCenter = nativeSample.Center;
                float nativeAi0 = nativeSample.ai[0], nativeAi1 = nativeSample.ai[1], nativeAi2 = nativeSample.ai[2];
                Main.projectile = Enumerable.Range(0, previousProjectiles.Length).Select(_ => new Projectile()).ToArray();
                Projectile soulSample = FireParityItemCheck(localPlayerIndex, ModContent.ItemType<SoulforgedSword>(),
                    ModContent.ProjectileType<SoulforgedWave>(), ItemID.IronHelmet, ItemID.IronChainmail,
                    ItemID.IronGreaves, direction, attackSpeed);
                Require(Vector2.Distance(nativeVelocity, soulSample.velocity) < 0.01f &&
                        Vector2.Distance(nativeCenter, soulSample.Center) < 0.01f &&
                        nativeAi0 == soulSample.ai[0] && nativeAi1 == soulSample.ai[1] && nativeAi2 == soulSample.ai[2],
                    $"Soulforged real ItemCheck must mirror TerraBlade left/right and attack speed {attackSpeed}: " +
                    $"native v={nativeVelocity} center={nativeCenter} ai={nativeAi0}/{nativeAi1}/{nativeAi2}; " +
                    $"soul v={soulSample.velocity} center={soulSample.Center} ai={soulSample.ai[0]}/{soulSample.ai[1]}/{soulSample.ai[2]}");
                File.AppendAllText(Path.Combine(OutputDirectory, "weapon-diagnostics.log"),
                    $"itemcheck-parity direction={direction} speed={attackSpeed:0.0}x velocity={nativeVelocity} center={nativeCenter} " +
                    $"ai={nativeAi0:0.###}/{nativeAi1:0.###}/{nativeAi2:0.###} result=PASS\n");
            }

            Main.projectile = Enumerable.Range(0, previousProjectiles.Length).Select(_ => new Projectile()).ToArray();
            // Recreate the initial right-facing 1.0x pair after the direction/speed fixtures.
            native = FireParityItemCheck(localPlayerIndex, ItemID.TerraBlade, terraItem.shoot,
                ItemID.IronHelmet, ItemID.IronChainmail, ItemID.IronGreaves);
            soul = FireParityItemCheck(localPlayerIndex, ModContent.ItemType<SoulforgedSword>(),
                ModContent.ProjectileType<SoulforgedWave>(), ItemID.IronHelmet,
                ItemID.IronChainmail, ItemID.IronGreaves);
            nativeSpawnCenter = native.Center;
            soulSpawnCenter = soul.Center;
            Require(native.type == ProjectileID.TerraBlade2Shot && native.aiStyle == 191,
                $"local TerraBlade must use native projectile 985/AI191, got {native.type}/{native.aiStyle}");
            int nativeLifeDefault = native.timeLeft;
            int soulLifeDefault = soul.timeLeft;
            bool nativeTileCollision = native.tileCollide;
            bool soulTileCollision = soul.tileCollide;
            // Keep the AI/lifetime comparison in an empty-air lane; actual
            // SetDefaults tile collision is logged and asserted separately so
            // nearby sandbox world terrain cannot prematurely terminate 985.
            native.tileCollide = soul.tileCollide = false;
            Require(nativeTileCollision && soulTileCollision,
                $"both native TerraBlade and Soulforged must retain tile collision defaults; got {nativeTileCollision}/{soulTileCollision}");

            var samples = new List<string>();
            int[] sampleTicks = { 0, 1, 2, 5, 10, 20, 30, 45, 60, 75, 89 };
            File.AppendAllText(Path.Combine(OutputDirectory, "weapon-diagnostics.log"),
                $"soulforged-parity-trace-begin defaults=timeLeft:{nativeLifeDefault}/{soulLifeDefault},size:{native.width}x{native.height}/{soul.width}x{soul.height},tileCollide:{nativeTileCollision}/{soulTileCollision}; harness tileCollide=false\n");
            File.AppendAllText(Path.Combine(OutputDirectory, "weapon-diagnostics.log"),
                $"itemcheck-spawn native type={native.type} owner={native.owner} ai={native.ai[0]:0.###}/{native.ai[1]:0.###}/{native.ai[2]:0.###} " +
                $"scale={native.scale:0.###} velocity={native.velocity} timeLeft={native.timeLeft}; " +
                $"soul type={soul.type} owner={soul.owner} ai={soul.ai[0]:0.###}/{soul.ai[1]:0.###}/{soul.ai[2]:0.###} " +
                $"scale={soul.scale:0.###} velocity={soul.velocity} timeLeft={soul.timeLeft}\n");
            for (int tick = 0; tick < 90; tick++)
            {
                if (tick < 24)
                    Main.dust = Enumerable.Range(0, previousDust.Length).Select(_ => new Dust()).ToArray();
                native.Update(0);
                if (tick < 24)
                    LogNewProjectileDust("TerraBlade985", tick + 1, native);
                if (tick < 24)
                    Main.dust = Enumerable.Range(0, previousDust.Length).Select(_ => new Dust()).ToArray();
                soul.Update(1);
                if (tick < 24)
                    LogNewProjectileDust("Soulforged", tick + 1, soul);
                if (sampleTicks.Contains(tick))
                {
                    samples.Add($"tick={tick + 1} native(time={native.timeLeft},scale={native.scale:0.###},damage={native.damage},center={native.Center},velocity={native.velocity}) " +
                        $"soul(time={soul.timeLeft},scale={soul.scale:0.###},damage={soul.damage},center={soul.Center},velocity={soul.velocity}) radius={90f * soul.scale:0.0}");
                    File.AppendAllText(Path.Combine(OutputDirectory, "weapon-diagnostics.log"), samples[^1] + "\n");
                }
                Require(native.timeLeft == soul.timeLeft,
                    $"Soulforged/TerraBlade lifespan diverged at tick {tick + 1}: native={native.timeLeft} soul={soul.timeLeft}");
                Vector2 nativeTravel = native.Center - nativeSpawnCenter;
                Vector2 soulTravel = soul.Center - soulSpawnCenter;
                Require(Vector2.Distance(nativeTravel, soulTravel) < 0.01f,
                    $"Soulforged/TerraBlade movement diverged at tick {tick + 1}: nativeDelta={nativeTravel} soulDelta={soulTravel}");
            }
            Require(!native.active && !soul.active, "both 90-tick TerraBlade/Soulforged beams should expire together");
            Require(SoulforgedNativeColorScope.RemappedLightCount > 0,
                "AIStyle 191 native green AddLight must be intercepted and recolored during Soulforged updates");
            File.AppendAllText(Path.Combine(OutputDirectory, "weapon-diagnostics.log"),
                $"soulforged-native-light-remap=PASS intercepted={SoulforgedNativeColorScope.RemappedLightCount} " +
                "source=RGB(64,220,96) target=red-gold scoped-to-this-projectile=true\n");

            Vector2 center = new(1000f, 400f);
            native.Center = soul.Center = center;
            native.velocity = soul.velocity = Vector2.UnitX * 12f;
            native.rotation = soul.rotation = 0f;
            native.scale = soul.scale = 1f;
            var probes = new (string Name, Rectangle Target)[]
            {
                ("centerline", new Rectangle(1050, 391, 18, 18)),
                ("inside-45deg", new Rectangle(1042, 442, 18, 18)),
                ("bbox-overlaps-45deg-edge", new Rectangle(1042, 452, 18, 18)),
                ("outside-45deg", new Rectangle(1030, 470, 18, 18)),
                ("outside-90px-radius", new Rectangle(1095, 391, 18, 18))
            };
            var outcomes = new List<string>();
            foreach (var probe in probes)
            {
                bool vanillaHit = native.Colliding(native.Hitbox, probe.Target) == true;
                bool soulHit = soul.ModProjectile.Colliding(soul.Hitbox, probe.Target) == true;
                Require(vanillaHit == soulHit,
                    $"Soulforged cone collision mismatch for {probe.Name}: native={vanillaHit}, soul={soulHit}");
                if (probe.Name == "centerline" || probe.Name == "inside-45deg")
                    Require(vanillaHit, $"native TerraBlade should hit {probe.Name} sample");
                if (probe.Name == "bbox-overlaps-45deg-edge")
                    Require(vanillaHit, "native TerraBlade AABB should hit a target whose box overlaps the 45-degree edge");
                if (probe.Name == "outside-45deg" || probe.Name == "outside-90px-radius")
                    Require(!vanillaHit, $"native TerraBlade should reject {probe.Name} sample");
                outcomes.Add($"{probe.Name}=native:{vanillaHit}/soul:{soulHit}");
            }
            int coneSamples = 0;
            int coneDisagreements = 0;
            float[] coneAngles = { 0f, 0.17f, MathHelper.PiOver4, 1.31f, MathHelper.Pi, -MathHelper.PiOver2 };
            float[] coneScales = { 0.55f, 0.8f, 1f, 1.25f, 1.5f };
            foreach (float testScale in coneScales)
            foreach (float testAngle in coneAngles)
            {
                native.Center = soul.Center = center;
                native.scale = soul.scale = testScale;
                native.rotation = soul.rotation = testAngle;
                native.velocity = soul.velocity = new Vector2((float)Math.Cos(testAngle), (float)Math.Sin(testAngle)) * 12f;
                for (int y = -112; y <= 112; y += 8)
                for (int x = -112; x <= 112; x += 8)
                {
                    Rectangle target = new((int)center.X + x, (int)center.Y + y, 16, 24);
                    bool vanillaHit = native.Colliding(native.Hitbox, target) == true;
                    bool soulHit = soul.ModProjectile.Colliding(soul.Hitbox, target) == true;
                    coneSamples++;
                    if (vanillaHit != soulHit)
                    {
                        coneDisagreements++;
                        if (coneDisagreements <= 12)
                            File.AppendAllText(Path.Combine(OutputDirectory, "weapon-diagnostics.log"),
                                $"cone-grid-mismatch scale={testScale:0.00} angle={testAngle:0.000} box={target} native={vanillaHit} soul={soulHit}\n");
                    }
                }
            }
            Require(coneSamples >= 15000 && coneDisagreements == 0,
                $"Soulforged cone must agree with native TerraBlade collision grid: samples={coneSamples}, mismatches={coneDisagreements}");
            File.AppendAllText(Path.Combine(OutputDirectory, "weapon-diagnostics.log"),
                $"native-cone-grid=PASS samples={coneSamples} mismatches={coneDisagreements} scales=0.55,0.8,1,1.25,1.5 angleCount={coneAngles.Length} targetGridStep=8\n");
            File.AppendAllText(Path.Combine(OutputDirectory, "weapon-diagnostics.log"),
                "soulforged-native-terra-parity=PASS AI191/type985 scale/movement/lifetime checked every tick; " +
                $"defaultTimeLeft={nativeLifeDefault}/{soulLifeDefault} " +
                $"projectileDefaults pen={native.penetrate}/{soul.penetrate} tileCollide={nativeTileCollision}/{soulTileCollision} " +
                $"ownerHitCheck={native.ownerHitCheck}/{soul.ownerHitCheck} localImmunity={native.usesLocalNPCImmunity}/{soul.usesLocalNPCImmunity} " +
                $"idStaticImmunity={native.usesIDStaticNPCImmunity}/{soul.usesIDStaticNPCImmunity}\n" +
                string.Join("\n", samples) + "\ncollision=" + string.Join("; ", outcomes) + "\n");

            TestSoulforgedActualDamage(localPlayerIndex);
        }
        finally
        {
            Main.player[localPlayerIndex] = previousPlayer;
            Main.projectile = previousProjectiles;
            Main.npc = previousNpcs;
            Main.dust = previousDust;
            Main.gameMenu = previousMenu;
            Main.gamePaused = previousPaused;
            WorldGen.gen = previousWorldGen;
            Main.screenPosition = previousScreen;
            Main.rand = previousRandom;
        }
    }

    private void LogNewProjectileDust(string label, int tick, Projectile projectile)
    {
        string[] nearby = Main.dust.Select((dust, index) => (dust, index))
            .Where(entry => entry.dust.active)
            .Select(entry => $"slot={entry.index} dust={entry.dust.type} color={entry.dust.color} alpha={entry.dust.alpha} " +
                $"pos=({entry.dust.position.X:0.#},{entry.dust.position.Y:0.#}) noLight={entry.dust.noLight} scale={entry.dust.scale:0.##}")
            .ToArray();
        File.AppendAllText(Path.Combine(OutputDirectory, "weapon-dust-ai191-trace.log"),
            $"projectile={label} tick={tick} center={projectile.Center} velocity={projectile.velocity} " +
            $"newDust={nearby.Length} {string.Join(" | ", nearby)}\n");
    }

    private Projectile FireParityItemCheck(int playerIndex, int itemType, int projectileType,
        int head, int body, int legs, int direction = 1, float attackSpeed = 1f)
    {
        Player actor = CreateActionPlayer(head, body, legs, itemType, direction,
            direction < 0 ? MathHelper.Pi : 0f, male: true, pose: 0);
        actor.whoAmI = playerIndex;
        actor.active = true;
        actor.position = new Vector2(900f, 300f);
        actor.velocity = Vector2.Zero;
        actor.controlUseItem = true;
        actor.releaseUseItem = false;
        if (itemType == ItemID.TerraBlade || itemType == ModContent.ItemType<SoulforgedSword>())
            actor.GetAttackSpeed(DamageClass.Melee) = attackSpeed;
        actor.itemAnimation = 0;
        actor.itemTime = 0;
        actor.GetModPlayer<Tier3T4ProgressPlayer>().ApplyNetworkState(true, true, false);
        Main.player[playerIndex] = actor;
        Vector2 aim = actor.MountedCenter + Vector2.UnitX * (direction * 160f);
        Main.mouseX = (int)aim.X;
        Main.mouseY = (int)aim.Y;
        int before = Main.projectile.Count(projectile => projectile.active &&
            projectile.owner == playerIndex && projectile.type == projectileType);
        actor.ItemCheck();
        Projectile[] matches = Main.projectile.Where(projectile => projectile.active &&
            projectile.owner == playerIndex && projectile.type == projectileType).ToArray();
        Require(matches.Length == before + 1,
            $"actual ItemCheck for item {itemType} should spawn one projectile type {projectileType}, found {matches.Length - before}");
        Projectile spawned = matches[^1];
        Require(spawned.damage > 0 && spawned.owner == playerIndex && spawned.type == projectileType,
            $"ItemCheck projectile metadata invalid for item {itemType}: type={spawned.type},damage={spawned.damage},owner={spawned.owner}");
        return spawned;
    }

    private void TestSoulforgedActualDamage(int playerIndex)
    {
        var samples = new (string Name, Vector2 Offset)[]
        {
            ("near-center", new Vector2(80f, 0f)),
            ("near-cone-edge", new Vector2(150f, 85f)),
            ("far-terminal", new Vector2(840f, 0f))
        };
        int damageCase = 0;
        Dictionary<string, int> terraHitTicks = new(StringComparer.Ordinal);
        Dictionary<string, int> soulHitTicks = new(StringComparer.Ordinal);
        foreach (var sample in samples)
        foreach (bool isSoulforged in new[] { false, true })
        {
            Main.rand = new Terraria.Utilities.UnifiedRandom(20261008 + damageCase++ / 2);
            Main.projectile = Enumerable.Range(0, Main.projectile.Length).Select(_ => new Projectile()).ToArray();
            Main.npc = Enumerable.Range(0, Main.npc.Length).Select(_ => new NPC()).ToArray();
            Projectile projectile = isSoulforged
                ? FireParityItemCheck(playerIndex, ModContent.ItemType<SoulforgedSword>(),
                    ModContent.ProjectileType<SoulforgedWave>(), ItemID.IronHelmet, ItemID.IronChainmail, ItemID.IronGreaves)
                : FireParityItemCheck(playerIndex, ItemID.TerraBlade, ProjectileID.TerraBlade2Shot,
                    ItemID.IronHelmet, ItemID.IronChainmail, ItemID.IronGreaves);
            int slot = Array.IndexOf(Main.projectile, projectile);
            Require(slot >= 0, $"real projectile not registered for damage case {sample.Name}");
            projectile.tileCollide = false; // Isolate NPC damage from arbitrary saved-world tiles.

            NPC target = Main.npc[0];
            target.SetDefaults(NPCID.BlueSlime);
            target.whoAmI = 0;
            target.active = true;
            target.friendly = false;
            target.dontTakeDamage = false;
            target.immortal = false;
            target.defense = 0;
            target.lifeMax = target.life = 10000;
            target.Center = projectile.Center + sample.Offset;
            int startLife = target.life;
            int tickHit = -1;
            int hitDamage = 0;
            int projectileDamageAtStart = projectile.damage;
            int projectileDamageAfterFirstHit = projectileDamageAtStart;
            for (int tick = 0; tick < 90 && projectile.active && target.life == startLife; tick++)
            {
                projectile.Update(slot);
                if (target.life < startLife)
                {
                    tickHit = tick + 1;
                    hitDamage = startLife - target.life;
                    projectileDamageAfterFirstHit = projectile.damage;
                }
            }
            int finalLife = target.life;
            if (isSoulforged) soulHitTicks[sample.Name] = tickHit;
            else terraHitTicks[sample.Name] = tickHit;
            File.AppendAllText(Path.Combine(OutputDirectory, "weapon-diagnostics.log"),
                $"projectile-damage name={(isSoulforged ? "Soulforged" : "TerraBlade")} sample={sample.Name} " +
                $"spawnDamage={projectileDamageAtStart} npcLife={startLife}->{finalLife} actualDamage={startLife - finalLife} " +
                $"firstHitTick={tickHit} projectileDamageAfter={projectile.damage} penetrate={projectile.penetrate} " +
                $"localCooldown={projectile.localNPCHitCooldown} ownerHitCheck={projectile.ownerHitCheck}\n");
            Require(tickHit > 0 && hitDamage > 0,
                $"actual Projectile.Update/Damage did not hurt the NPC in {sample.Name} for {(isSoulforged ? "Soulforged" : "TerraBlade")}");
            int expectedReducedDamage = Math.Max(1, (int)(projectileDamageAtStart * 0.75f));
            Require(projectileDamageAfterFirstHit == expectedReducedDamage,
                $"TerraBlade-family hit falloff did not apply after first real NPC hit: name={(isSoulforged ? "Soulforged" : "TerraBlade")} " +
                $"start={projectileDamageAtStart}, after={projectileDamageAfterFirstHit}, expected={expectedReducedDamage}");
        }
        foreach (var sample in samples)
        {
            int nativeTick = terraHitTicks[sample.Name];
            int soulTick = soulHitTicks[sample.Name];
            Require(nativeTick == soulTick,
                $"Soulforged real hit geometry/timing diverged from TerraBlade at {sample.Name}: Terra tick={nativeTick}, Soul tick={soulTick}");
        }
        File.AppendAllText(Path.Combine(OutputDirectory, "weapon-diagnostics.log"),
            "real-hit-parity=PASS identical NPC type/size/positions/random seeds; near-center, cone-edge, and far-terminal first-hit frames match; " +
            "both types apply 25% stored-damage falloff after first real hit\n");
    }

    private void RenderWeaponDiagnosticProjectiles()
    {
        GraphicsDevice device = Main.graphics.GraphicsDevice;
        const int width = 900, height = 330, scale = 2;
        Projectile[] previousProjectiles = Main.projectile;
        NPC[] previousNpcs = Main.npc;
        Dust[] previousDust = Main.dust;
        int localPlayerIndex = Math.Clamp(Main.myPlayer, 0, Main.player.Length - 1);
        Player previousPlayer = Main.player[localPlayerIndex];
        bool previousMenu = Main.gameMenu;
        bool previousPaused = Main.gamePaused;
        bool previousWorldGen = WorldGen.gen;
        bool previousLighting = ForcePreviewLighting;
        Vector2 previousScreenPosition = Main.screenPosition;
        Terraria.Utilities.UnifiedRandom previousRandom = Main.rand;
        using var target = new RenderTarget2D(device, width, height, false,
            SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
        try
        {
            ForcePreviewLighting = true;
            Main.gameMenu = false;
            Main.gamePaused = false;
            WorldGen.gen = false;
            Main.screenPosition = Vector2.Zero;
            Main.rand = new Terraria.Utilities.UnifiedRandom(20261007);
            _weaponLightLogCounts.Clear();
            Require(Main.tile.Width > 100 && Main.tile.Height > 100,
                "weapon homing fixture requires a loaded native tile world for LOS checks");
            Main.projectile = Enumerable.Range(0, previousProjectiles.Length).Select(_ => new Projectile()).ToArray();
            Main.dust = Enumerable.Range(0, previousDust.Length).Select(_ => new Dust()).ToArray();
            Main.npc = Enumerable.Range(0, previousNpcs.Length).Select(_ => new NPC()).ToArray();

            Player diagnosticOwner = CreateActionPlayer(ModContent.ItemType<KnightHelmet>(),
                ModContent.ItemType<KnightChestplate>(), ModContent.ItemType<KnightGreaves>(),
                ModContent.ItemType<TemplarSword>(), 1, 0f, male: true, pose: 0);
            // Keep simulated actors and projectiles safely inside the loaded
            // map even when the smoke profile was launched from the menu.
            diagnosticOwner.position = new Vector2(400f, 400f);
            diagnosticOwner.velocity = Vector2.Zero;
            diagnosticOwner.active = true;
            diagnosticOwner.whoAmI = localPlayerIndex;
            diagnosticOwner.itemAnimation = diagnosticOwner.itemTime = 18;
            Main.player[localPlayerIndex] = diagnosticOwner;
            Vector2 worldCenter = Main.LocalPlayer.Center;
            NPC firstTarget = Main.npc[0];
            firstTarget.SetDefaults(NPCID.BlueSlime);
            firstTarget.whoAmI = 0;
            firstTarget.active = true;
            firstTarget.friendly = false;
            firstTarget.dontTakeDamage = false;
            firstTarget.life = firstTarget.lifeMax = 100;
            firstTarget.Center = worldCenter + new Vector2(42f, -24f);
            NPC secondTarget = Main.npc[1];
            secondTarget.SetDefaults(NPCID.BlueSlime);
            secondTarget.whoAmI = 1;
            secondTarget.active = false;
            secondTarget.friendly = false;
            secondTarget.dontTakeDamage = false;
            secondTarget.life = secondTarget.lifeMax = 100;
            secondTarget.Center = worldCenter + new Vector2(32f, 34f);

            var cases = new[]
            {
                (Name: "Templar homing sword", Type: ModContent.ProjectileType<TemplarWave>(), Center: new Vector2(150, 165), Velocity: new Vector2(9f, 0f)),
                (Name: "Knight red wave", Type: ModContent.ProjectileType<KnightSwordWave>(), Center: new Vector2(450, 165), Velocity: new Vector2(9f, -1.5f)),
                (Name: "Soulforged crescent", Type: ModContent.ProjectileType<SoulforgedWave>(), Center: new Vector2(750, 165), Velocity: new Vector2(60f, 0f))
            };
            device.SetRenderTarget(target);
            device.Clear(new Color(8, 9, 15));
            foreach (var item in cases)
            {
                Main.rand = new Terraria.Utilities.UnifiedRandom(20261007 + Array.IndexOf(cases, item));
                Main.dust = Enumerable.Range(0, previousDust.Length).Select(_ => new Dust()).ToArray();
                Projectile projectile = _weaponDiagnosticProjectiles.FirstOrDefault(candidate => candidate.type == item.Type);
                Require(projectile is not null && projectile.ModProjectile is not null,
                    $"a real ItemCheck projectile/ModProjectile was not captured for {item.Name}");
                int slot = Main.projectile.Length - 1 - Array.IndexOf(cases, item);
                Main.projectile[slot] = projectile;
                projectile.whoAmI = slot;
                projectile.owner = localPlayerIndex;
                projectile.active = true;
                projectile.friendly = false; // Run native AI and trail capture without changing NPC life.
                if (item.Type != ModContent.ProjectileType<SoulforgedWave>())
                    projectile.damage = 0;
                projectile.tileCollide = false;
                projectile.velocity = item.Velocity;
                if (item.Type != ModContent.ProjectileType<SoulforgedWave>())
                    projectile.timeLeft = 50;
                projectile.alpha = 0;
                if (item.Type != ModContent.ProjectileType<SoulforgedWave>())
                    projectile.scale = 1f;
                Vector2 desiredCenter = Main.screenPosition + item.Center;
                projectile.Center = desiredCenter - item.Velocity * 8f;
                if (item.Type == ModContent.ProjectileType<TemplarWave>())
                    projectile.Center = worldCenter + new Vector2(-75f, 8f);
                int initialUpdates = item.Type == ModContent.ProjectileType<TemplarWave>() ? 8 : 16;
                for (int tick = 0; tick < initialUpdates; tick++)
                {
                    projectile.Update(slot);
                    Require(projectile.active,
                        $"{item.Name} projectile became inactive during isolated AI update {tick + 1}/{initialUpdates}");
                }
                int cachedPositions = projectile.oldPos.Count(position => position != Vector2.Zero);
                Require(cachedPositions > 0,
                    $"{item.Name} must populate oldPos/oldRot through native Projectile.Update");
                File.AppendAllText(Path.Combine(OutputDirectory, "weapon-diagnostics.log"),
                    $"projectile-update name={item.Name} updates={initialUpdates} active={projectile.active} oldPosCached={cachedPositions}/{projectile.oldPos.Length} velocity={projectile.velocity} rotation={projectile.rotation:0.###}\n");

                if (item.Type == ModContent.ProjectileType<TemplarWave>())
                {
                    Require((int)projectile.ai[0] == 1f,
                        $"Templar should acquire first chaseable target; ai0={projectile.ai[0]}");
                    float acquiredTurn = projectile.velocity.ToRotation();
                    firstTarget.active = false;
                    secondTarget.active = true;
                    for (int tick = 0; tick < 2; tick++) projectile.Update(slot);
                    Require((int)projectile.ai[0] == 2f,
                        $"Templar should replace a dead target with the next valid target; ai0={projectile.ai[0]}");
                    secondTarget.active = false;
                    projectile.Update(slot);
                    Require((int)projectile.ai[0] == 0,
                        $"Templar should clear target when no chaseable NPC remains; ai0={projectile.ai[0]}");
                    File.AppendAllText(Path.Combine(OutputDirectory, "weapon-diagnostics.log"),
                        $"templar-live-ai acquired=1 reselected=2 cleared={(int)projectile.ai[0] == 0} finalRotation={projectile.rotation:0.###} velocityTurned={Math.Abs(acquiredTurn) > 0.001f} owner={projectile.owner}\n");
                }

                for (int settle = 0; settle < 2; settle++)
                {
                    projectile.Update(slot);
                    Require(projectile.active, $"{item.Name} projectile became inactive during trail settling");
                }
                Vector2 drawOffset = Main.screenPosition + item.Center - projectile.Center;
                for (int i = 0; i < projectile.oldPos.Length; i++)
                    if (projectile.oldPos[i] != Vector2.Zero)
                        projectile.oldPos[i] += drawOffset;
                foreach (Dust dust in Main.dust)
                    if (dust.active && Vector2.DistanceSquared(dust.position, projectile.Center) < 180f * 180f)
                        dust.position += drawOffset;
                projectile.Center = Main.screenPosition + item.Center;
                Main.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp,
                    DepthStencilState.None, RasterizerState.CullCounterClockwise);
                // Isolate each row so actual projectile coordinates/oldPos render at their native scale.
                Vector2 rowCenter = projectile.Center;
                Color light = Color.Black;
                projectile.ModProjectile.PreDraw(ref light);
                Main.spriteBatch.End();
                Texture2D texture = TextureAssets.Projectile[item.Type].Value;
                int dustBeforeDraw = 0;
                for (int dustIndex = 0; dustIndex < Main.dust.Length; dustIndex++)
                {
                    Dust dust = Main.dust[dustIndex];
                    if (!dust.active || Vector2.DistanceSquared(dust.position, rowCenter) > 180f * 180f)
                        continue;
                    Texture2D dustTexture = TextureAssets.Dust.Value;
                    Rectangle source = dust.frame.Width > 0 && dust.frame.Height > 0
                        ? dust.frame : new Rectangle(0, 0, dustTexture.Width, dustTexture.Height);
                    Main.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp,
                        DepthStencilState.None, RasterizerState.CullCounterClockwise);
                    Main.spriteBatch.Draw(dustTexture, dust.position - Main.screenPosition, source,
                        dust.GetAlpha(Color.White), dust.rotation,
                        new Vector2(source.Width * 0.5f, source.Height * 0.5f), dust.scale,
                        SpriteEffects.None, 0f);
                    Main.spriteBatch.End();
                    dustBeforeDraw++;
                }
                Require(dustBeforeDraw > 0,
                    $"{item.Name} did not produce any real Dust entities in its AI trail fixture");
                File.AppendAllText(Path.Combine(OutputDirectory, "weapon-diagnostics.log"),
                    $"preview name={item.Name} texture={texture.Width}x{texture.Height} oldPos={projectile.oldPos.Length} " +
                    $"dustRendered={dustBeforeDraw} darkBackground=true renderedBy=ModProjectile.PreDraw\n");
            }
            using var enlarged = new RenderTarget2D(device, width * scale, height * scale, false,
                SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
            device.SetRenderTarget(enlarged);
            device.Clear(Color.Black);
            Main.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp,
                DepthStencilState.None, RasterizerState.CullCounterClockwise);
            Main.spriteBatch.Draw(target, new Rectangle(0, 0, width * scale, height * scale), Color.White);
            Main.spriteBatch.End();
            device.SetRenderTarget(null);
            using FileStream stream = File.Create(Path.Combine(OutputDirectory, "native-weapon-diagnostics.png"));
            enlarged.SaveAsPng(stream, width * scale, height * scale);
        }
        finally
        {
            device.SetRenderTarget(null);
            Main.projectile = previousProjectiles;
            Main.npc = previousNpcs;
            Main.dust = previousDust;
            Main.player[localPlayerIndex] = previousPlayer;
            Main.gameMenu = previousMenu;
            Main.gamePaused = previousPaused;
            WorldGen.gen = previousWorldGen;
            Main.screenPosition = previousScreenPosition;
            Main.rand = previousRandom;
            ForcePreviewLighting = previousLighting;
        }
    }


    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException("Milkcraft smoke assertion failed: " + message);
    }

    private static void ValidateHeadgearFaceFlags()
    {
        string report = Path.Combine(Environment.GetEnvironmentVariable("MILKCRAFT_TERRARIA_SMOKE_OUT") ?? Environment.CurrentDirectory,
            "native-headgear-flags.log");
        File.WriteAllText(report, "0.2.0 native-face rules: Cloth/Hunting/Energy use hat hair; warrior helmets preserve face; no added fake-eye overlays.\n");

        var hatHairHoods = new (string Name, int Type)[]
        {
            ("Cloth", ModContent.ItemType<ClothHood>()),
            ("Hunting", ModContent.ItemType<HuntingHood>()),
            ("Energy", ModContent.ItemType<EnergyHood>())
        };
        foreach (var entry in hatHairHoods)
        {
            Item item = new();
            item.SetDefaults(entry.Type);
            int slot = item.headSlot;
            Require(slot >= 0 && ArmorIDs.Head.Sets.DrawHead[slot] && ArmorIDs.Head.Sets.DrawHatHair[slot],
                $"{entry.Name} hood must preserve the native face and original hat-hair rule at headSlot={slot}");
            File.AppendAllText(report,
                $"{entry.Name}: headSlot={slot} DrawHead={ArmorIDs.Head.Sets.DrawHead[slot]} " +
                $"DrawFullHair={ArmorIDs.Head.Sets.DrawFullHair[slot]} DrawHatHair={ArmorIDs.Head.Sets.DrawHatHair[slot]} " +
                $"DrawsBackHairWithoutHeadgear={ArmorIDs.Head.Sets.DrawsBackHairWithoutHeadgear[slot]} PASS\n");
        }

        foreach (var entry in new (string Name, int Type)[]
        {
            ("Iron", ModContent.ItemType<IronHelmet>()),
            ("Knight", ModContent.ItemType<KnightHelmet>())
        })
        {
            Item item = new();
            item.SetDefaults(entry.Type);
            int slot = item.headSlot;
            Require(slot >= 0 && ArmorIDs.Head.Sets.DrawHead[slot] &&
                    !ArmorIDs.Head.Sets.DrawFullHair[slot] && !ArmorIDs.Head.Sets.DrawHatHair[slot],
                $"{entry.Name} helmet must retain the 0.2.0 face/hair rule at headSlot={slot}");
            File.AppendAllText(report,
                $"{entry.Name}: headSlot={slot} DrawHead={ArmorIDs.Head.Sets.DrawHead[slot]} " +
                $"DrawFullHair={ArmorIDs.Head.Sets.DrawFullHair[slot]} DrawHatHair={ArmorIDs.Head.Sets.DrawHatHair[slot]} PASS\n");
        }

        Item headband = new();
        headband.SetDefaults(ModContent.ItemType<LeatherHeadband>());
        int headbandSlot = headband.headSlot;
        Require(headbandSlot >= 0 && ArmorIDs.Head.Sets.DrawFullHair[headbandSlot] &&
                !ArmorIDs.Head.Sets.DrawHatHair[headbandSlot] &&
                ArmorIDs.Head.Sets.DrawsBackHairWithoutHeadgear[headbandSlot],
            "Leather Headband must retain its original hair rules");
        File.AppendAllText(report, $"LeatherHeadband: headSlot={headbandSlot} original hair rules PASS\n");
    }
    private void ExportVanillaHallowedBodyComposite()
    {
        Item hallowed = new();
        hallowed.SetDefaults(ItemID.HallowedPlateMail);
        MethodInfo loadBody = typeof(Main).GetMethod("LoadArmorBody",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        loadBody?.Invoke(Main.instance, new object[] { hallowed.bodySlot });

        FieldInfo field = typeof(TextureAssets).GetField("ArmorBodyComposite",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
        Array textures = field?.GetValue(null) as Array;
        object asset = textures is not null && hallowed.bodySlot >= 0 && hallowed.bodySlot < textures.Length
            ? textures.GetValue(hallowed.bodySlot)
            : null;
        Texture2D texture = asset?.GetType().GetProperty("Value")?.GetValue(asset) as Texture2D;
        string assetName = asset?.GetType().GetProperty("Name")?.GetValue(asset)?.ToString() ?? "<unknown>";
        if (texture is null)
            throw new InvalidOperationException($"Could not resolve native ArmorBodyComposite for HallowedPlateMail bodySlot={hallowed.bodySlot}; fieldPresent={field is not null}, arrayLength={textures?.Length ?? 0}");

        string path = Path.Combine(OutputDirectory, "vanilla-hallowed-body.png");
        using (FileStream stream = File.Create(path))
            texture.SaveAsPng(stream, texture.Width, texture.Height);
        File.WriteAllText(Path.Combine(OutputDirectory, "vanilla-hallowed-body.log"),
            $"item=HallowedPlateMail itemType={ItemID.HallowedPlateMail} bodySlot={hallowed.bodySlot} " +
            $"field={field?.Name ?? "<missing>"} asset={assetName} size={texture.Width}x{texture.Height}\n");
    }

    private void ExportNativePlayerCompositeArmTextures()
    {
        string directory = Path.Combine(OutputDirectory, "native-player-arm-textures");
        Directory.CreateDirectory(directory);
        var exported = new System.Collections.Generic.HashSet<Texture2D>();
        var lines = new System.Collections.Generic.List<string>();

        foreach (FieldInfo field in typeof(TextureAssets).GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
        {
            object value;
            try { value = field.GetValue(null); }
            catch { continue; }

            if (value is Array array)
            {
                foreach (object element in array)
                    TryExport(field.Name, element);
            }
            else
                TryExport(field.Name, value);
        }

        File.WriteAllLines(Path.Combine(directory, "manifest.txt"), lines);
        if (!lines.Any(line => line.Contains("360x224", StringComparison.Ordinal)))
            throw new InvalidOperationException("No 360x224 native player-composite texture was found in TextureAssets; see native-player-arm-textures/manifest.txt.");

        void TryExport(string fieldName, object asset)
        {
            if (asset is null) return;
            Type type = asset.GetType();
            PropertyInfo valueProperty = type.GetProperty("Value");
            PropertyInfo nameProperty = type.GetProperty("Name");
            PropertyInfo loadedProperty = type.GetProperty("IsLoaded");
            if (valueProperty is null || nameProperty is null) return;

            try
            {
                if (loadedProperty?.GetValue(asset) is bool loaded && !loaded) return;
                if (valueProperty.GetValue(asset) is not Texture2D texture || !exported.Add(texture)) return;
                string name = nameProperty.GetValue(asset)?.ToString() ?? "unknown";
                if (!name.Contains("Player", StringComparison.OrdinalIgnoreCase)) return;

                lines.Add($"field={fieldName} asset={name} size={texture.Width}x{texture.Height}");
                string normalizedName = name.Replace('/', '\\');
                bool isVanillaFaceAtlas = normalizedName.EndsWith("Player_0_0", StringComparison.OrdinalIgnoreCase) ||
                    normalizedName.EndsWith("Player_0_1", StringComparison.OrdinalIgnoreCase) ||
                    normalizedName.EndsWith("Player_0_2", StringComparison.OrdinalIgnoreCase);
                if ((texture.Width != 360 || texture.Height != 224) && !isVanillaFaceAtlas) return;

                string safeName = string.Concat(name.Select(character =>
                    char.IsLetterOrDigit(character) ? character : '_'));
                using FileStream stream = File.Create(Path.Combine(directory, safeName + ".png"));
                texture.SaveAsPng(stream, texture.Width, texture.Height);
            }
            catch { /* Assets not ready in this menu pass are listed only when loaded. */ }
        }
    }

    private void WriteRecipeDump(System.Collections.Generic.HashSet<int> modItemTypes)
    {
        string ItemName(int type) => ContentSamples.ItemsByType.TryGetValue(type, out Item sample)
            ? sample.Name
            : $"ItemType:{type}";
        string ItemKey(int type)
        {
            if (ContentSamples.ItemsByType.TryGetValue(type, out Item sample) && sample.ModItem is ModItem modItem)
                return $"{modItem.Mod.Name}/{modItem.Name}";
            return $"Terraria/{StaticName(typeof(ItemID), type)}";
        }
        string StaticName(Type owner, int value) => owner.GetFields(BindingFlags.Public | BindingFlags.Static)
            .FirstOrDefault(field => (field.FieldType == typeof(int) || field.FieldType == typeof(short) ||
                field.FieldType == typeof(ushort)) && Convert.ToInt32(field.GetValue(null)) == value)?.Name
            ?? value.ToString();
        string TileName(int tile) => TileLoader.GetTile(tile)?.FullName
            ?? StaticName(typeof(TileID), tile);

        Recipe[] recipes = Main.recipe.Take(Recipe.numRecipes)
            .Where(recipe => recipe is not null && (modItemTypes.Contains(recipe.createItem.type) ||
                (recipe.createItem.type == ItemID.Leather &&
                    recipe.requiredTile.Contains(ModContent.TileType<MilkcraftLoomTile>()))))
            .ToArray();
        object[] recipeRows = recipes.Select(recipe => new
        {
            resultKey = ItemKey(recipe.createItem.type),
            resultStack = recipe.createItem.stack,
            result = new { type = recipe.createItem.type, name = ItemName(recipe.createItem.type) },
            ingredients = recipe.requiredItem.Where(item => item is not null && item.type > ItemID.None && item.stack > 0)
                .Select(item => new { key = ItemKey(item.type), stack = item.stack,
                    kind = modItemTypes.Contains(item.type) ? "mod-item" : "vanilla-item",
                    type = item.type, name = ItemName(item.type) }).ToArray(),
            requiredTiles = recipe.requiredTile.Select(tile => new { id = tile, key = TileName(tile), name = TileName(tile) }).ToArray(),
            recipeGroups = recipe.acceptedGroups.Select(groupId =>
            {
                RecipeGroup group = RecipeGroup.recipeGroups[groupId];
                return new
                {
                    id = groupId,
                    name = RecipeGroup.recipeGroupIDs.FirstOrDefault(pair => pair.Value == groupId).Key ?? $"RecipeGroup:{groupId}",
                    validItems = group.ValidItems.OrderBy(type => type).Select(type => new
                        { key = ItemKey(type), type, name = ItemName(type) }).ToArray()
                };
            }).ToArray(),
            conditions = recipe.Conditions.Select(condition => new
                { key = condition.Description?.Key ?? string.Empty, text = condition.Description?.Value ?? string.Empty }).ToArray()
        }).ToArray();

        object[] itemRows = modItemTypes.OrderBy(type => type).Select(type => new
        {
            type,
            key = ItemKey(type),
            internalName = ContentSamples.ItemsByType[type].ModItem?.Name ?? ItemName(type),
            name = ItemName(type),
            recipes = recipes.Count(recipe => recipe.createItem.type == type)
        }).ToArray();

        File.WriteAllText(Path.Combine(OutputDirectory, "recipe-dump-159.json"),
            JsonSerializer.Serialize(new { modItemCount = itemRows.Length, registeredRecipeCount = Recipe.numRecipes,
                exportedRecipeCount = recipes.Length,
                modOutputRecipeCount = recipes.Count(recipe => modItemTypes.Contains(recipe.createItem.type)),
                items = itemRows, recipes = recipeRows }, new JsonSerializerOptions { WriteIndented = true }));
    }

    private void TestTier3T4Contracts(System.Collections.Generic.HashSet<int> allItemTypes, int[] outputs)
    {
        int[] lateTierItems = ModContent.GetContent<ModItem>()
            .Where(item => item is Tier3T4ArmorItem or Tier3T4WeaponItem or Tier3T4AccessoryItem or Tier3T4MaterialItem or TierEquipmentLicenseItem)
            .Select(item => (int)item.Type).ToArray();
        Require(lateTierItems.Length == 38, $"expected 38 T3/T4 items, found {lateTierItems.Length}");
        Require(lateTierItems.All(allItemTypes.Contains), "a T3/T4 item is missing from the mod registry");
        Require(lateTierItems.All(type => outputs.Contains(type)), "a T3/T4 item is missing its fixed recipe");

        Require(ArmorDefense<TemplarHelmet>() == 12 && ArmorDefense<TemplarChestplate>() == 20 && ArmorDefense<TemplarGreaves>() == 18,
            "Templar defense must match the 50-defense T3 melee target");
        Require(ArmorDefense<NightmareHood>() == 9 && ArmorDefense<NightmareChestplate>() == 16 && ArmorDefense<NightmareLeggings>() == 10,
            "Nightmare defense must match the 35-defense T3 ranged target");
        Require(ArmorDefense<FallenHood>() == 7 && ArmorDefense<FallenRobe>() == 14 && ArmorDefense<FallenLeggings>() == 10,
            "Fallen defense must match the 31-defense T3 magic target");
        Require(ArmorDefense<SoulforgedHelmet>() == 23 && ArmorDefense<SoulforgedChestplate>() == 32 && ArmorDefense<SoulforgedGreaves>() == 18,
            "Soulforged defense must match the 73-defense post-Golem melee target");
        Require(ArmorDefense<AbyssHood>() == 11 && ArmorDefense<AbyssChestplate>() == 24 && ArmorDefense<AbyssLeggings>() == 16,
            "Abyss defense must match the 51-defense post-Golem ranged target");
        Require(ArmorDefense<DarkHood>() == 12 && ArmorDefense<DarkRobe>() == 14 && ArmorDefense<DarkLeggings>() == 16,
            "Dark defense must match the 42-defense post-Golem magic target");
        Require(WeaponDamage<TemplarSword>() == 72 && WeaponDamage<NightmareBow>() == 52 && WeaponDamage<FallenStaff>() == 64,
            "T3 approved balance damage values changed");
        Require(WeaponDamage<SoulforgedSword>() == 110 && WeaponDamage<AbyssBow>() == 65 && WeaponDamage<DarkStaff>() == 100,
            "T4 approved balance damage values changed");
        Require(ModContent.GetInstance<HuntingBow>().Item.damage == 32 &&
                ModContent.GetInstance<HuntingBow>().Item.useTime == 22,
            "approved HuntingBow damage/cadence values changed");
        Require(ModContent.GetInstance<NightmareBow>().Item.useTime == 17 &&
                ModContent.GetInstance<FallenStaff>().Item.useTime == 20 &&
                ModContent.GetInstance<DarkStaff>().Item.useTime == 20 &&
                ModContent.GetInstance<HuntingBow>().Item.useTime == 22,
            "approved weak-weapon cadence values changed");

        Item hallowedRepeater = new();
        hallowedRepeater.SetDefaults(ItemID.HallowedRepeater);
        Item chlorophyteShotbow = new();
        chlorophyteShotbow.SetDefaults(ItemID.ChlorophyteShotbow);
        File.WriteAllText(Path.Combine(OutputDirectory, "tier3-t4-bow-balance-reference.log"),
            $"Local tML item defaults (1.4.4): HallowedRepeater damage={hallowedRepeater.damage}, useTime={hallowedRepeater.useTime}, useAnimation={hallowedRepeater.useAnimation}; " +
            $"ChlorophyteShotbow damage={chlorophyteShotbow.damage}, useTime={chlorophyteShotbow.useTime}, useAnimation={chlorophyteShotbow.useAnimation}.\n" +
            $"Milkcraft T3 NightmareBow damage={ModContent.GetInstance<NightmareBow>().Item.damage}, useTime={ModContent.GetInstance<NightmareBow>().Item.useTime}; " +
            $"T4 AbyssBow damage={ModContent.GetInstance<AbyssBow>().Item.damage}, useTime={ModContent.GetInstance<AbyssBow>().Item.useTime}.\n");
        Require(ModContent.GetInstance<NightmareBow>().Item.useTime == 17 &&
                ModContent.GetInstance<AbyssBow>().Item.useTime == 18,
            "T3/T4 bows should retain their approved stage fire cadence");
        TestTier3T4ArmorBonusContracts();
        TestAbyssBowAmmoAndProjectileCount();
        Require(ArmorIDs.Body.Sets.HidesBottomSkin[ModContent.GetInstance<FallenRobe>().Item.bodySlot] &&
                ArmorIDs.Body.Sets.HidesBottomSkin[ModContent.GetInstance<DarkRobe>().Item.bodySlot],
            "T3/T4 robes must hide bare leg skin while their native leg-slot hems render");

        TestLateTierPlayKitUpgrade();
        TestBalancePlayKitMigration();

        int templarWave = ModContent.ProjectileType<TemplarWave>();
        int soulforgedWave = ModContent.ProjectileType<SoulforgedWave>();
        int fallenBolt = ModContent.ProjectileType<FallenBolt>();
        int darkBolt = ModContent.ProjectileType<DarkBolt>();
        Require(templarWave != ModContent.ProjectileType<KnightSwordWave>() &&
                soulforgedWave != ModContent.ProjectileType<KnightSwordWave>() &&
                templarWave != soulforgedWave,
            "T3/T4 swords must use independent wave projectile types, separate from T2");
        Require(fallenBolt != ModContent.ProjectileType<EnergyBolt>() &&
                darkBolt != ModContent.ProjectileType<EnergyBolt>() && fallenBolt != darkBolt,
            "T3/T4 staves must use independent magic-bolt projectile types");
        Require(ModContent.GetInstance<TemplarSword>().Item.shoot == templarWave &&
                ModContent.GetInstance<SoulforgedSword>().Item.shoot == soulforgedWave &&
                ModContent.GetInstance<FallenStaff>().Item.shoot == fallenBolt &&
                ModContent.GetInstance<DarkStaff>().Item.shoot == darkBolt,
            "T3/T4 weapons do not point at their dedicated projectile types");
        Require(ModContent.GetInstance<T3EquipmentLicense>().Item.consumable &&
                ModContent.GetInstance<T3EquipmentLicense>().Item.maxStack == 1 &&
                ModContent.GetInstance<T4EquipmentLicense>().Item.consumable &&
                ModContent.GetInstance<T4EquipmentLicense>().Item.maxStack == 1,
            "tier licenses must be single-use and non-stackable");

        TestCharacterLicensePersistenceAndWorldGates();
    }

    private static int ArmorDefense<T>() where T : Tier3T4ArmorItem => ModContent.GetInstance<T>().Item.defense;
    private static int WeaponDamage<T>() where T : Tier3T4WeaponItem => ModContent.GetInstance<T>().Item.damage;

    private void TestTier3T4ArmorBonusContracts()
    {
        int playerIndex = Math.Clamp(Main.myPlayer, 0, Main.player.Length - 1);
        Player previous = Main.player[playerIndex];
        try
        {
            var cases = new (string Suit, int Head, int Body, int Legs, DamageClass Class, float Crit, float Speed)[]
            {
                ("Templar", ModContent.ItemType<TemplarHelmet>(), ModContent.ItemType<TemplarChestplate>(), ModContent.ItemType<TemplarGreaves>(), DamageClass.Melee, 8f, .06f),
                ("Nightmare", ModContent.ItemType<NightmareHood>(), ModContent.ItemType<NightmareChestplate>(), ModContent.ItemType<NightmareLeggings>(), DamageClass.Ranged, 10f, .06f),
                ("Fallen", ModContent.ItemType<FallenHood>(), ModContent.ItemType<FallenRobe>(), ModContent.ItemType<FallenLeggings>(), DamageClass.Magic, 10f, .06f),
                ("Soulforged", ModContent.ItemType<SoulforgedHelmet>(), ModContent.ItemType<SoulforgedChestplate>(), ModContent.ItemType<SoulforgedGreaves>(), DamageClass.Melee, 8f, .06f),
                ("Abyss", ModContent.ItemType<AbyssHood>(), ModContent.ItemType<AbyssChestplate>(), ModContent.ItemType<AbyssLeggings>(), DamageClass.Ranged, 12f, .06f),
                ("Dark", ModContent.ItemType<DarkHood>(), ModContent.ItemType<DarkRobe>(), ModContent.ItemType<DarkLeggings>(), DamageClass.Magic, 12f, .08f)
            };
            foreach (var test in cases)
            {
                Player actor = CreateActionPlayer(ItemID.IronHelmet, ItemID.IronChainmail, ItemID.IronGreaves,
                    ItemID.TerraBlade, 1, 0f, male: true, pose: 0);
                actor.whoAmI = playerIndex;
                actor.active = true;
                actor.GetModPlayer<Tier3T4ProgressPlayer>().ApplyNetworkState(true, true, false);
                Main.player[playerIndex] = actor;
                UpdateTestActor(actor);
                float baseCrit = actor.GetTotalCritChance(test.Class);
                float baseSpeed = actor.GetTotalAttackSpeed(test.Class);

                actor.armor[0].SetDefaults(test.Head);
                actor.armor[1].SetDefaults(test.Body);
                actor.armor[2].SetDefaults(test.Legs);
                actor.head = actor.armor[0].headSlot;
                actor.body = actor.armor[1].bodySlot;
                actor.legs = actor.armor[2].legSlot;
                bool robes = false;
                actor.armor[1].ModItem?.SetMatch(actor.Male, ref actor.legs, ref robes);
                UpdateTestActor(actor);

                float actualCrit = actor.GetTotalCritChance(test.Class) - baseCrit;
                float actualSpeed = actor.GetTotalAttackSpeed(test.Class) - baseSpeed;
                Require(Math.Abs(actualCrit - test.Crit) < .01f && Math.Abs(actualSpeed - test.Speed) < .001f,
                    $"{test.Suit} intrinsic armor bonuses mismatch: expected crit={test.Crit},speed={test.Speed}; actual={actualCrit:0.###},{actualSpeed:0.###}");
                File.AppendAllText(Path.Combine(OutputDirectory, "tier3-t4-armor-bonus-checks.log"),
                    $"suit={test.Suit} class={test.Class.Name} fullArmor critDelta={actualCrit:0.###} attackSpeedDelta={actualSpeed:0.###} expected={test.Crit:0.###}/{test.Speed:0.###} PASS\n");
            }
            Require(cases.Length == 6, "all six T3/T4 armor sets must be covered by the intrinsic bonus test");
        }
        finally
        {
            Main.player[playerIndex] = previous;
        }
    }

    private static void UpdateTestActor(Player actor)
    {
        actor.ResetEffects();
        actor.UpdateEquips(actor.whoAmI);
        actor.UpdateArmorSets(actor.whoAmI);
        PlayerLoader.PostUpdateEquips(actor);
    }

    private void TestAbyssBowAmmoAndProjectileCount()
    {
        int playerIndex = Math.Clamp(Main.myPlayer, 0, Main.player.Length - 1);
        Player previousPlayer = Main.player[playerIndex];
        Projectile[] previousProjectiles = Main.projectile;
        bool previousMenu = Main.gameMenu;
        int previousMouseX = Main.mouseX, previousMouseY = Main.mouseY;
        Terraria.Utilities.UnifiedRandom previousRandom = Main.rand;
        try
        {
            Main.gameMenu = false;
            Main.rand = new Terraria.Utilities.UnifiedRandom(20261010);
            foreach (int ammoItem in new[] { ItemID.IchorArrow, ItemID.WoodenArrow })
            {
                Main.projectile = Enumerable.Range(0, previousProjectiles.Length).Select(_ => new Projectile()).ToArray();
                Player actor = CreateActionPlayer(ItemID.IronHelmet, ItemID.IronChainmail, ItemID.IronGreaves,
                    ModContent.ItemType<AbyssBow>(), 1, 0f, male: true, pose: 0);
                actor.whoAmI = playerIndex;
                actor.active = true;
                actor.position = new Vector2(900f, 300f);
                actor.inventory[0].SetDefaults(ModContent.ItemType<AbyssBow>());
                actor.inventory[1].SetDefaults(ammoItem);
                actor.inventory[1].stack = 25;
                actor.selectedItem = 0;
                actor.lastVisualizedSelectedItem = actor.inventory[0];
                actor.itemAnimation = actor.itemTime = 0;
                actor.controlUseItem = true;
                actor.releaseUseItem = false;
                actor.GetModPlayer<Tier3T4ProgressPlayer>().ApplyNetworkState(true, true, false);
                Main.player[playerIndex] = actor;
                Main.mouseX = (int)(actor.Center.X + 200f);
                Main.mouseY = (int)actor.Center.Y;
                int ammoBefore = actor.inventory[1].stack;
                actor.ItemCheck();
                Projectile[] shots = Main.projectile.Where(p => p.active && p.owner == playerIndex).ToArray();
                int ammoAfter = actor.inventory[1].stack;
                Require(shots.Length == 2,
                    $"AbyssBow real ItemCheck must emit exactly two projectiles from one Shoot callback; ammo={ammoItem} count={shots.Length}");
                Require(ammoBefore - ammoAfter == 1,
                    $"AbyssBow two-arrow shot must consume exactly one ammo item; ammo={ammoItem} before={ammoBefore} after={ammoAfter}");
                int expectedProjectile = ammoItem == ItemID.IchorArrow ? ProjectileID.IchorArrow : ModContent.ProjectileType<HuntingArrow>();
                Require(shots.All(p => p.type == expectedProjectile),
                    $"AbyssBow must preserve/transform native ammo projectile type {expectedProjectile}; actual={string.Join(',', shots.Select(p => p.type))}");
                float angleDelta = Math.Abs(MathHelper.WrapAngle(shots[0].velocity.ToRotation() - shots[1].velocity.ToRotation()));
                Require(Math.Abs(angleDelta - .044f) < .004f,
                    $"AbyssBow twin arrow spread mismatch: expected .044 radians, actual={angleDelta:0.0000}");
                File.AppendAllText(Path.Combine(OutputDirectory, "abyssbow-real-itemcheck.log"),
                    $"ammo={ContentSamples.ItemsByType[ammoItem].Name} projectileType={expectedProjectile} shotCount={shots.Length} ammoConsumed={ammoBefore - ammoAfter} " +
                    $"damage={shots[0].damage}/{shots[1].damage} owner={shots[0].owner}/{shots[1].owner} spread={angleDelta:0.0000} PASS\n");
            }

            // Record actual vanilla multi-shot behavior rather than infer it
            // from the item damage field. Chlorophyte Shotbow is sampled with
            // several fixed RNG seeds; Tsunami gets its own real ItemCheck row.
            foreach (int vanillaBow in new[] { ItemID.ChlorophyteShotbow, ItemID.Tsunami })
            {
                int trialCount = vanillaBow == ItemID.ChlorophyteShotbow ? 5 : 1;
                List<int> counts = new();
                List<int> ammoCosts = new();
                for (int trial = 0; trial < trialCount; trial++)
                {
                    Main.projectile = Enumerable.Range(0, previousProjectiles.Length).Select(_ => new Projectile()).ToArray();
                    Main.rand = new Terraria.Utilities.UnifiedRandom(20261020 + trial);
                    Player vanillaActor = CreateActionPlayer(ItemID.IronHelmet, ItemID.IronChainmail,
                        ItemID.IronGreaves, vanillaBow, 1, 0f, male: true, pose: 0);
                    vanillaActor.whoAmI = playerIndex;
                    vanillaActor.active = true;
                    vanillaActor.position = new Vector2(900f, 300f);
                    vanillaActor.inventory[0].SetDefaults(vanillaBow);
                    vanillaActor.inventory[1].SetDefaults(ItemID.WoodenArrow);
                    vanillaActor.inventory[1].stack = 25;
                    vanillaActor.selectedItem = 0;
                    vanillaActor.lastVisualizedSelectedItem = vanillaActor.inventory[0];
                    vanillaActor.itemAnimation = vanillaActor.itemTime = 0;
                    vanillaActor.controlUseItem = true;
                    vanillaActor.releaseUseItem = false;
                    Main.player[playerIndex] = vanillaActor;
                    Main.mouseX = (int)(vanillaActor.Center.X + 200f);
                    Main.mouseY = (int)vanillaActor.Center.Y;
                    int ammoBefore = vanillaActor.inventory[1].stack;
                    vanillaActor.ItemCheck();
                    Projectile[] vanillaShots = Main.projectile.Where(p => p.active && p.owner == playerIndex).ToArray();
                    int ammoSpent = ammoBefore - vanillaActor.inventory[1].stack;
                    Require(vanillaShots.Length > 0 && ammoSpent == 1,
                        $"native bow ItemCheck must fire and spend exactly one wooden arrow: item={vanillaBow} " +
                        $"shots={vanillaShots.Length} ammo={ammoSpent}");
                    counts.Add(vanillaShots.Length);
                    ammoCosts.Add(ammoSpent);
                    File.AppendAllText(Path.Combine(OutputDirectory, "abyssbow-real-itemcheck.log"),
                        $"vanilla-itemcheck item={ContentSamples.ItemsByType[vanillaBow].Name} trial={trial + 1}/{trialCount} " +
                        $"seed={20261020 + trial} shots={vanillaShots.Length} ammoConsumed={ammoSpent} " +
                        $"types={string.Join(',', vanillaShots.Select(p => p.type))}\n");
                }
                double average = counts.Average();
                if (vanillaBow == ItemID.ChlorophyteShotbow)
                    Require(average > 1d, $"fixed-seed native Chlorophyte Shotbow sample should demonstrate multishot, counts={string.Join(',', counts)}");
                else
                    Require(counts[0] >= 2, $"native Tsunami should emit its multi-arrow volley; count={counts[0]}");
                File.AppendAllText(Path.Combine(OutputDirectory, "abyssbow-real-itemcheck.log"),
                    $"vanilla-multishot-summary item={ContentSamples.ItemsByType[vanillaBow].Name} " +
                    $"trials={counts.Count} counts={string.Join(',', counts)} average={average:0.###} " +
                    $"ammoCosts={string.Join(',', ammoCosts)} result=PASS\n");
            }

            // Verify both projectiles receive the actual same-tier paired
            // visual/homing marker when the real full set and quiver are worn.
            Main.projectile = Enumerable.Range(0, previousProjectiles.Length).Select(_ => new Projectile()).ToArray();
            Player paired = CreateActionPlayer(ModContent.ItemType<AbyssHood>(), ModContent.ItemType<AbyssChestplate>(),
                ModContent.ItemType<AbyssLeggings>(), ModContent.ItemType<AbyssBow>(), 1, 0f, male: true, pose: 0);
            paired.whoAmI = playerIndex;
            paired.active = true;
            paired.position = new Vector2(900f, 300f);
            paired.inventory[0].SetDefaults(ModContent.ItemType<AbyssBow>());
            paired.inventory[1].SetDefaults(ItemID.IchorArrow);
            paired.inventory[1].stack = 25;
            paired.armor[3].SetDefaults(ModContent.ItemType<AbyssQuiver>());
            paired.selectedItem = 0;
            paired.lastVisualizedSelectedItem = paired.inventory[0];
            paired.itemAnimation = paired.itemTime = 0;
            paired.controlUseItem = true;
            paired.releaseUseItem = false;
            paired.GetModPlayer<Tier3T4ProgressPlayer>().ApplyNetworkState(true, true, false);
            Main.player[playerIndex] = paired;
            UpdateTestActor(paired);
            Main.mouseX = (int)(paired.Center.X + 200f);
            Main.mouseY = (int)paired.Center.Y;
            paired.ItemCheck();
            Projectile[] pairedShots = Main.projectile.Where(p => p.active && p.owner == playerIndex).ToArray();
            Require(pairedShots.Length == 2 && pairedShots.All(p =>
                p.type == ProjectileID.IchorArrow && p.GetGlobalProjectile<MilkcraftPairedProjectile>().Paired &&
                p.GetGlobalProjectile<MilkcraftPairedProjectile>().TextureVariant == 4 &&
                p.GetGlobalProjectile<MilkcraftPairedProjectile>().Homing),
                $"Abyss set/quiver must mark exactly two actual Ichor projectiles with the paired homing variant; " +
                $"shots={pairedShots.Length}, types={string.Join(',', pairedShots.Select(p => p.type))}, " +
                $"variants={string.Join(',', pairedShots.Select(p => p.GetGlobalProjectile<MilkcraftPairedProjectile>().TextureVariant))}");
            NPC pairTarget = Main.npc[0];
            pairTarget.SetDefaults(NPCID.BlueSlime);
            pairTarget.whoAmI = 0;
            pairTarget.active = true;
            pairTarget.friendly = false;
            pairTarget.dontTakeDamage = false;
            pairTarget.life = pairTarget.lifeMax = 1000;
            MilkcraftPassivePlayer passive = paired.GetModPlayer<MilkcraftPassivePlayer>();
            passive.OnHitNPCWithProj(pairedShots[0], pairTarget, default, pairedShots[0].damage);
            passive.OnHitNPCWithProj(pairedShots[1], pairTarget, default, pairedShots[1].damage);
            int extraProcs = Main.projectile.Count(p => p.active && p.owner == playerIndex &&
                p.type == ModContent.ProjectileType<SuitPassiveProjectile>());
            Require(extraProcs == 1 && passive.VfxSharedCooldown == 60,
                $"two actual Abyss arrows should share one paired extra-proc cooldown: extra={extraProcs}, cooldown={passive.VfxSharedCooldown}");
            File.AppendAllText(Path.Combine(OutputDirectory, "abyssbow-real-itemcheck.log"),
                $"matched full-Abyss+Quiver ItemCheck projectiles={pairedShots.Length} nativeIchorType={pairedShots[0].type} " +
                $"paired={pairedShots.All(p => p.GetGlobalProjectile<MilkcraftPairedProjectile>().Paired)} " +
                $"homing={pairedShots.All(p => p.GetGlobalProjectile<MilkcraftPairedProjectile>().Homing)} " +
                $"two-arrow-hit extraProcCount={extraProcs} sharedCooldown={passive.VfxSharedCooldown} PASS\n");
        }
        finally
        {
            Main.player[playerIndex] = previousPlayer;
            Main.projectile = previousProjectiles;
            Main.gameMenu = previousMenu;
            Main.mouseX = previousMouseX;
            Main.mouseY = previousMouseY;
            Main.rand = previousRandom;
        }
    }

    private static void TestLateTierPlayKitUpgrade()
    {
        string oldKitEnv = Environment.GetEnvironmentVariable("MILKCRAFT_TERRARIA_PLAY_KIT");
        int playerIndex = Math.Clamp(Main.myPlayer, 0, Main.player.Length - 1);
        Player previousPlayer = Main.player[playerIndex];
        Item[] previousWorldItems = Main.item;
        int previousNetMode = Main.netMode;
        int previousMyPlayer = Main.myPlayer;
        bool previousMenu = Main.gameMenu;
        try
        {
            Environment.SetEnvironmentVariable("MILKCRAFT_TERRARIA_PLAY_KIT", "1");
            // QuickSpawnItem resolves the local player through Main.player and
            // intentionally will not behave like a loose unregistered Player.
            // Install this synthetic character only for the fixture, then put
            // all global game state back in finally.
            Main.myPlayer = playerIndex;
            Main.netMode = NetmodeID.SinglePlayer;
            Main.gameMenu = false;
            Player existingCharacter = new() { active = true, whoAmI = playerIndex };
            Main.player[playerIndex] = existingCharacter;
            // QuickSpawnItem uses world-item slots before the normal player
            // update picks them up. Isolate that array so the test can verify
            // actual item drops without touching a loaded world or inventory.
            Main.item = Enumerable.Range(0, previousWorldItems.Length)
                .Select(_ => new Item()).ToArray();
            PlayKitPlayer kit = existingCharacter.GetModPlayer<PlayKitPlayer>();
            kit.LoadData(new Terraria.ModLoader.IO.TagCompound { ["milkcraftPlayKitGranted"] = true });
            kit.OnEnterWorld();

            int[] expectedNewItems = ModContent.GetContent<ModItem>()
                .Where(item => item is Tier3T4ArmorItem or Tier3T4WeaponItem or Tier3T4AccessoryItem or
                    Tier3T4MaterialItem or TierEquipmentLicenseItem)
                .Select(item => (int)item.Type).ToArray();
            System.Collections.Generic.Dictionary<int, int> dropped = Main.item
                .Where(item => item is not null && item.active && !item.IsAir)
                .Select(item => item.type)
                .Where(expectedNewItems.Contains)
                .GroupBy(type => type)
                .ToDictionary(group => group.Key, group => group.Count());
            Require(dropped.Keys.ToHashSet().SetEquals(expectedNewItems) && dropped.Values.All(count => count == 1),
                $"an existing play-kit character should spawn all 38 T3/T4 items once: distinct={dropped.Count}, duplicates={dropped.Count(pair => pair.Value != 1)}");
            Tier3T4ProgressPlayer progress = existingCharacter.GetModPlayer<Tier3T4ProgressPlayer>();
            Require(progress.HasLicense(3) && progress.HasLicense(4),
                "the isolated play kit should unlock both tiers without changing world boss flags");
            var saved = new Terraria.ModLoader.IO.TagCompound();
            kit.SaveData(saved);
            Require(saved.GetBool("milkcraftPlayKitGranted") && saved.GetBool("milkcraftT3T4PlayKitGranted"),
                "the play-kit migration must save its original and T3/T4 grant markers");
            int droppedBeforeRepeat = Main.item.Count(item => item is not null && item.active &&
                !item.IsAir && expectedNewItems.Contains(item.type));
            kit.LoadData(saved);
            kit.OnEnterWorld();
            int droppedAfterRepeat = Main.item.Count(item => item is not null && item.active &&
                !item.IsAir && expectedNewItems.Contains(item.type));
            Require(droppedAfterRepeat == droppedBeforeRepeat,
                $"re-entering a migrated play-kit character should not duplicate T3/T4 drops: before={droppedBeforeRepeat} after={droppedAfterRepeat}");
            File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("MILKCRAFT_TERRARIA_SMOKE_OUT")
                    ?? Environment.CurrentDirectory, "tier3-t4-play-kit-upgrade.log"),
                "existing character had original playKitGranted=true, tier3Tier4KitGranted=false; " +
                $"world drops returned {dropped.Count}/38 distinct items with no duplicates, T3/T4 license=true, " +
                $"markers saved; repeated entry kept drop count {droppedBeforeRepeat}.\n");
        }
        finally
        {
            Main.player[playerIndex] = previousPlayer;
            Main.item = previousWorldItems;
            Main.netMode = previousNetMode;
            Main.myPlayer = previousMyPlayer;
            Main.gameMenu = previousMenu;
            Environment.SetEnvironmentVariable("MILKCRAFT_TERRARIA_PLAY_KIT", oldKitEnv);
        }
    }

    private void TestBalancePlayKitMigration()
    {
        string previousBalanceEnv = Environment.GetEnvironmentVariable("MILKCRAFT_TERRARIA_BALANCE_PLAY_KIT");
        string previousStarterEnv = Environment.GetEnvironmentVariable("MILKCRAFT_TERRARIA_PLAY_KIT");
        int playerIndex = Math.Clamp(Main.myPlayer, 0, Main.player.Length - 1);
        Player previousPlayer = Main.player[playerIndex];
        Item[] previousWorldItems = Main.item;
        int previousNetMode = Main.netMode, previousMyPlayer = Main.myPlayer;
        bool previousMenu = Main.gameMenu;
        try
        {
            Environment.SetEnvironmentVariable("MILKCRAFT_TERRARIA_BALANCE_PLAY_KIT", "1");
            Environment.SetEnvironmentVariable("MILKCRAFT_TERRARIA_PLAY_KIT", null);
            Main.myPlayer = playerIndex;
            Main.netMode = NetmodeID.SinglePlayer;
            Main.gameMenu = false;
            Main.item = Enumerable.Range(0, previousWorldItems.Length).Select(_ => new Item()).ToArray();
            Player actor = new() { active = true, whoAmI = playerIndex };
            for (int slot = 0; slot < actor.inventory.Length; slot++)
            {
                actor.inventory[slot].SetDefaults(ItemID.DirtBlock);
                actor.inventory[slot].stack = actor.inventory[slot].maxStack;
            }
            Main.player[playerIndex] = actor;
            PlayKitPlayer kit = actor.GetModPlayer<PlayKitPlayer>();
            kit.LoadData(new Terraria.ModLoader.IO.TagCompound
            {
                ["milkcraftPlayKitGranted"] = true,
                ["milkcraftT3T4PlayKitGranted"] = true,
                ["milkcraft025PassiveKitGranted"] = true
            });
            kit.OnEnterWorld();
            int chestCount = Main.item.Where(item => item is not null && item.active && item.type == ItemID.Chest).Sum(item => item.stack);
            int crownCount = Main.item.Where(item => item is not null && item.active && item.type == ItemID.SlimeCrown).Sum(item => item.stack);
            Require(chestCount == 20 && crownCount == 20,
                $"0.2.10 migration kit must deliver 20 chests and 20 Slime Crowns; got {chestCount}/{crownCount}");
            Require(actor.inventory.All(item => item.type == ItemID.DirtBlock && item.stack == item.maxStack),
                "balance play kit must not replace or consume existing inventory items");
            Terraria.ModLoader.IO.TagCompound saved = new();
            kit.SaveData(saved);
            Require(saved.GetBool("milkcraft0210BalanceKitGranted"), "balance kit marker was not persisted");
            kit.LoadData(saved);
            kit.OnEnterWorld();
            int chestAfterRepeat = Main.item.Where(item => item is not null && item.active && item.type == ItemID.Chest).Sum(item => item.stack);
            int crownAfterRepeat = Main.item.Where(item => item is not null && item.active && item.type == ItemID.SlimeCrown).Sum(item => item.stack);
            Require(chestAfterRepeat == 20 && crownAfterRepeat == 20,
                $"0.2.10 balance migration duplicated on re-entry: chest={chestAfterRepeat}, crown={crownAfterRepeat}");
            File.WriteAllText(Path.Combine(OutputDirectory, "balance-play-kit-migration.log"),
                $"old profile flags preserved; chest={chestCount}, SlimeCrown={crownCount}, inventoryUnchanged=true, " +
                $"persistedBalanceMarker={saved.GetBool("milkcraft0210BalanceKitGranted")}, reentry={chestAfterRepeat}/{crownAfterRepeat} PASS\n");
        }
        finally
        {
            Main.player[playerIndex] = previousPlayer;
            Main.item = previousWorldItems;
            Main.netMode = previousNetMode;
            Main.myPlayer = previousMyPlayer;
            Main.gameMenu = previousMenu;
            Environment.SetEnvironmentVariable("MILKCRAFT_TERRARIA_BALANCE_PLAY_KIT", previousBalanceEnv);
            Environment.SetEnvironmentVariable("MILKCRAFT_TERRARIA_PLAY_KIT", previousStarterEnv);
        }
    }

    private static void TestCharacterLicensePersistenceAndWorldGates()
    {
        var saved = new Terraria.ModLoader.IO.TagCompound
        {
            ["tier3EquipmentLicense"] = true,
            ["tier4EquipmentLicense"] = true,
            ["tier3ChampionTitle"] = true
        };
        var restored = new Tier3T4ProgressPlayer();
        restored.LoadData(saved);
        Require(restored.HasLicense(3) && restored.HasLicense(4) && restored.HasT3ChampionTitle,
            "tier licenses and T3 title must restore from character save data");
        var roundTrip = new Terraria.ModLoader.IO.TagCompound();
        restored.SaveData(roundTrip);
        var secondLoad = new Tier3T4ProgressPlayer();
        secondLoad.LoadData(roundTrip);
        Require(secondLoad.HasLicense(3) && secondLoad.HasLicense(4) && secondLoad.HasT3ChampionTitle,
            "tier license and title save/load round-trip failed");

        bool oldMechAny = NPC.downedMechBossAny;
        bool oldGolem = NPC.downedGolemBoss;
        bool oldMech1 = NPC.downedMechBoss1;
        bool oldMech2 = NPC.downedMechBoss2;
        bool oldMech3 = NPC.downedMechBoss3;
        bool oldObsidianGuardian = MilkcraftBossWorldSystem.ObsidianGuardianDowned;
        bool oldSphinx = MilkcraftBossWorldSystem.SphinxDowned;
        bool oldFrostWitch = MilkcraftBossWorldSystem.FrostWitchDowned;
        try
        {
            NPC.downedMechBossAny = false;
            NPC.downedGolemBoss = false;
            NPC.downedMechBoss1 = NPC.downedMechBoss2 = NPC.downedMechBoss3 = false;
            Require(!Tier3T4ProgressPlayer.WorldAllowsTier(3) && !Tier3T4ProgressPlayer.WorldAllowsTier(4),
                "new licenses must not activate before current-world progression");
            NPC.downedMechBossAny = true;
            Require(Tier3T4ProgressPlayer.WorldAllowsTier(3) && !Tier3T4ProgressPlayer.WorldAllowsTier(4),
                "any mechanical boss unlocks T3 only");
            var fresh = new Tier3T4ProgressPlayer();
            Require(fresh.ActivateLicense(3) && !fresh.ActivateLicense(3), "T3 license must activate exactly once");
            NPC.downedGolemBoss = true;
            Require(Tier3T4ProgressPlayer.WorldAllowsTier(4), "Golem unlocks T4");
            Require(fresh.ActivateLicense(4) && !fresh.ActivateLicense(4), "T4 license must activate exactly once");
            NPC.downedMechBoss1 = NPC.downedMechBoss2 = NPC.downedMechBoss3 = true;
            Require(!Tier3T4ProgressPlayer.WorldGrantsT3ChampionTitle,
                "mechanical boss flags no longer grant the T3 champion title");
            MilkcraftBossWorldSystem.ObsidianGuardianDowned = true;
            MilkcraftBossWorldSystem.SphinxDowned = true;
            MilkcraftBossWorldSystem.FrostWitchDowned = true;
            Require(Tier3T4ProgressPlayer.WorldGrantsT3ChampionTitle,
                "the Obsidian Guardian, Sphinx, and Frost Witch grant the T3 champion title");
        }
        finally
        {
            NPC.downedMechBossAny = oldMechAny;
            NPC.downedGolemBoss = oldGolem;
            NPC.downedMechBoss1 = oldMech1;
            NPC.downedMechBoss2 = oldMech2;
            NPC.downedMechBoss3 = oldMech3;
            MilkcraftBossWorldSystem.ObsidianGuardianDowned = oldObsidianGuardian;
            MilkcraftBossWorldSystem.SphinxDowned = oldSphinx;
            MilkcraftBossWorldSystem.FrostWitchDowned = oldFrostWitch;
        }
    }

    public static void LogBowDraw(string itemName, PlayerDrawSet drawInfo, DrawData data)
    {
        NativeBowDrawCount++;
        string output = Environment.GetEnvironmentVariable("MILKCRAFT_TERRARIA_SMOKE_OUT")
            ?? Environment.CurrentDirectory;
        File.AppendAllText(Path.Combine(output, "native-held-layer.log"),
            $"bow-native item={itemName} direction={drawInfo.drawPlayer.direction} effect={data.effect} " +
            $"src={data.sourceRect} pos={data.position} origin={data.origin} rot={data.rotation} scale={data.scale}\n");
    }

    public static void LogTier3T4BowDraw(string itemName, PlayerDrawSet drawInfo, DrawData data, SpriteEffects nativeEffect)
    {
        NativeBowDrawCount++;
        bool flipped = (data.effect & SpriteEffects.FlipHorizontally) != 0;
        bool expectedFlip = drawInfo.drawPlayer.direction < 0;
        Require(flipped == expectedFlip,
            $"{itemName} should preserve native string-left orientation: direction={drawInfo.drawPlayer.direction}, effect={data.effect}");
        string output = Environment.GetEnvironmentVariable("MILKCRAFT_TERRARIA_SMOKE_OUT")
            ?? Environment.CurrentDirectory;
        File.AppendAllText(Path.Combine(output, "native-held-layer.log"),
            $"bow-native-t3t4 item={itemName} direction={drawInfo.drawPlayer.direction} entryEffect={nativeEffect} finalEffect={data.effect} " +
            $"stringSide=left normalizedFlip={flipped} src={data.sourceRect} pos={data.position} origin={data.origin} rot={data.rotation} scale={data.scale}\n");
    }

    public static void LogGripOriginDraw(string itemName, PlayerDrawSet drawInfo, DrawData data)
    {
        string output = Environment.GetEnvironmentVariable("MILKCRAFT_TERRARIA_SMOKE_OUT")
            ?? Environment.CurrentDirectory;
        File.AppendAllText(Path.Combine(output, "native-held-layer.log"),
            $"grip-origin item={itemName} fixture={NativePoseLabel} direction={drawInfo.drawPlayer.direction} " +
            $"texture={data.texture.Width}x{data.texture.Height} pos={data.position} origin={data.origin} " +
            $"rot={data.rotation} scale={data.scale} effect={data.effect}\n");
    }

    public static void LogKnightWaveShot(Player player, Vector2 position, Vector2 velocity)
    {
        string output = Environment.GetEnvironmentVariable("MILKCRAFT_TERRARIA_SMOKE_OUT")
            ?? Environment.CurrentDirectory;
        File.AppendAllText(Path.Combine(output, "knight-sword-wave.log"),
            $"shoot-callback tick={Main.GameUpdateCount} itemAnimation={player.itemAnimation} " +
            $"itemTime={player.itemTime} position={position} velocity={velocity}\n");
    }

    public static void LogAccessoryDraw(string texturePath, DrawData data)
    {
        if (!CaptureAccessoryDraws)
            return;
        if (AccessoryCaptureSearch.Length > 0 &&
            !texturePath.Contains(AccessoryCaptureSearch, StringComparison.OrdinalIgnoreCase))
            return;
        AccessoryCaptureDrawCount++;
        string output = Environment.GetEnvironmentVariable("MILKCRAFT_TERRARIA_SMOKE_OUT")
            ?? Environment.CurrentDirectory;
        Rectangle source = data.sourceRect ?? new Rectangle(0, 0, data.texture.Width, data.texture.Height);
        Rectangle clipped = Rectangle.Intersect(source,
            new Rectangle(0, 0, data.texture.Width, data.texture.Height));
        Color[] sourcePixels = clipped.Width > 0 && clipped.Height > 0
            ? new Color[clipped.Width * clipped.Height] : Array.Empty<Color>();
        if (sourcePixels.Length > 0)
            data.texture.GetData(0, clipped, sourcePixels, 0, sourcePixels.Length);
        AccessorySourceInsideTexture = clipped == source;
        AccessorySourceFrameY = source.Y;
        AccessorySourceAlphaPixels = sourcePixels.Count(pixel => pixel.A > 0);
        File.AppendAllText(Path.Combine(output, "native-accessory-layers.log"),
            $"actor={AccessoryCaptureLabel} texture={texturePath} src={data.sourceRect} pos={data.position} " +
            $"size={data.texture.Width}x{data.texture.Height} clippedSource={clipped} " +
            $"sourceInBounds={AccessorySourceInsideTexture} sourceAlphaPixels={AccessorySourceAlphaPixels} " +
            $"origin={data.origin} rot={data.rotation} scale={data.scale} effect={data.effect}\n");
        if (AccessoryCaptureLabel.StartsWith("vanilla-cobalt-", StringComparison.Ordinal) &&
            texturePath.Contains("Acc_Shield", StringComparison.OrdinalIgnoreCase))
        {
            string atlasPath = Path.Combine(output, "vanilla-cobalt-shield-atlas.png");
            if (!File.Exists(atlasPath))
            {
                using var stream = File.Create(atlasPath);
                data.texture.SaveAsPng(stream, data.texture.Width, data.texture.Height);
            }
            int frame = Math.Max(0, (data.sourceRect?.Y ?? 0) / 56);
            SaveAccessorySourceFrame(data,
                Path.Combine(output, $"vanilla-cobalt-shield-frame{frame:D2}.png"));
        }
        if (AccessoryCaptureLabel == "vanilla-magic-quiver" && texturePath.Contains("Acc_Back", StringComparison.OrdinalIgnoreCase))
        {
            using var stream = File.Create(Path.Combine(output, "vanilla-magic-quiver-atlas.png"));
            data.texture.SaveAsPng(stream, data.texture.Width, data.texture.Height);
            SaveAccessorySourceFrame(data, Path.Combine(output, "vanilla-magic-quiver-frame0.png"));
        }
        if (AccessoryCaptureLabel.StartsWith("vanilla-waist-", StringComparison.Ordinal) &&
            texturePath.Contains("Acc_Waist", StringComparison.OrdinalIgnoreCase))
        {
            string safeName = AccessoryCaptureLabel.Replace('/', '-');
            using var stream = File.Create(Path.Combine(output, $"{safeName}-atlas.png"));
            data.texture.SaveAsPng(stream, data.texture.Width, data.texture.Height);
            SaveAccessorySourceFrame(data, Path.Combine(output, $"{safeName}-frame.png"));
        }
    }

    public static void LogRobeLegDraw(string texturePath, DrawData data)
    {
        if (!CaptureRobeLegDraws ||
            !(texturePath.Contains("ClothRobe_Legs", StringComparison.OrdinalIgnoreCase) ||
              texturePath.Contains("EnergyRobe_Legs", StringComparison.OrdinalIgnoreCase)))
            return;

        RobeLegCaptureDrawCount++;
        string output = Environment.GetEnvironmentVariable("MILKCRAFT_TERRARIA_SMOKE_OUT")
            ?? Environment.CurrentDirectory;
        Rectangle source = data.sourceRect ?? new Rectangle(0, 0, data.texture.Width, data.texture.Height);
        Rectangle clipped = Rectangle.Intersect(source,
            new Rectangle(0, 0, data.texture.Width, data.texture.Height));
        Color[] pixels = clipped.Width > 0 && clipped.Height > 0
            ? new Color[clipped.Width * clipped.Height] : Array.Empty<Color>();
        if (pixels.Length > 0)
            data.texture.GetData(0, clipped, pixels, 0, pixels.Length);
        RobeLegSourceInsideTexture = clipped == source;
        RobeLegSourceAlphaPixels = pixels.Count(pixel => pixel.A > 0);
        File.AppendAllText(Path.Combine(output, "native-robe-leg-frames.log"),
            $"case={RobeLegCaptureLabel} texture={texturePath} src={source} " +
            $"textureSize={data.texture.Width}x{data.texture.Height} inBounds={RobeLegSourceInsideTexture} " +
            $"alphaPixels={RobeLegSourceAlphaPixels} pos={data.position} origin={data.origin} " +
            $"rot={data.rotation} effect={data.effect}\n");
    }

    private static void SaveAccessorySourceFrame(DrawData data, string path)
    {
        Rectangle source = data.sourceRect ?? new Rectangle(0, 0, data.texture.Width, data.texture.Height);
        Rectangle bounds = Rectangle.Intersect(source, new Rectangle(0, 0, data.texture.Width, data.texture.Height));
        if (bounds.Width <= 0 || bounds.Height <= 0)
            return;
        var pixels = new Color[bounds.Width * bounds.Height];
        data.texture.GetData(0, bounds, pixels, 0, pixels.Length);
        using var frame = new Texture2D(data.texture.GraphicsDevice, bounds.Width, bounds.Height);
        frame.SetData(pixels);
        using var stream = File.Create(path);
        frame.SaveAsPng(stream, bounds.Width, bounds.Height);
    }

    private static void TestStationPlacementAndDrops()
    {
        Item[] oldItems = Main.item;
        bool oldGameMenu = Main.gameMenu;
        int oldNetMode = Main.netMode;
        bool oldWorldGen = WorldGen.gen;
        bool oldGeneratingWorld = WorldGen.generatingWorld;
        bool oldNoTileActions = WorldGen.noTileActions;
        bool oldDestroyObject = WorldGen.destroyObject;
        const int width = 200;
        const int height = 140;
        const int originX = 20;
        const int groundY = 80;
        try
        {
            Require(Main.maxTilesX > width + originX && Main.maxTilesY > height,
                $"test tilemap bounds too small: {Main.maxTilesX}x{Main.maxTilesY}");
            for (int x = originX; x < originX + width; x++)
            for (int y = groundY - 6; y <= groundY + 1; y++)
            {
                Tile tile = Main.tile[x, y];
                Require(!tile.HasTile && tile.WallType == WallID.None,
                    $"scratch arena is not empty at {x},{y}; refusing to alter it");
            }
            // NewItem uses a sentinel slot after maxItems in this tML build.
            Main.item = Enumerable.Range(0, Math.Max(400, Main.maxItems + 1)).Select(_ => new Item()).ToArray();
            Main.gameMenu = false;
            Main.netMode = NetmodeID.SinglePlayer;
            // Menu startup can leave world-generation suppression flags set.
            // This is a scratch arena in memory, so exercise normal tile actions.
            WorldGen.gen = false;
            WorldGen.generatingWorld = false;
            WorldGen.noTileActions = false;
            WorldGen.destroyObject = false;
            for (int x = originX; x < originX + width; x++)
            {
                Tile tile = Main.tile[x, groundY];
                tile.HasTile = true;
                tile.TileType = TileID.Stone;
            }

            var stations = new (int TileType, int ItemType)[]
            {
                (ModContent.TileType<MilkcraftFurnaceTile>(), ModContent.ItemType<MilkcraftFurnace>()),
                (ModContent.TileType<MilkcraftLoomTile>(), ModContent.ItemType<MilkcraftLoom>()),
                (ModContent.TileType<MilkcraftForgeTile>(), ModContent.ItemType<MilkcraftForge>()),
                (ModContent.TileType<MilkcraftDistilleryTile>(), ModContent.ItemType<MilkcraftDistillery>()),
                (ModContent.TileType<MilkcraftAlchemyTableTile>(), ModContent.ItemType<MilkcraftAlchemyTable>()),
                (ModContent.TileType<MilkcraftEnchantingTableTile>(), ModContent.ItemType<MilkcraftEnchantingTable>()),
                (ModContent.TileType<MilkcraftWorkbenchTile>(), ModContent.ItemType<MilkcraftWorkbench>())
            };
            var stationErrors = new System.Collections.Generic.List<string>();
            for (int index = 0; index < stations.Length; index++)
            {
                var (tileType, itemType) = stations[index];
                int placeX = originX + 5 + index * 24;
                int placeY = groundY - 1;
                bool destroyBeforePlace = WorldGen.destroyObject;
                bool placed = WorldGen.PlaceObject(placeX, placeY, tileType, true, 0);
                bool destroyAfterPlace = WorldGen.destroyObject;
                Require(placed, $"WorldGen.PlaceObject failed for tile type {tileType}");
                var objectData = TileObjectData.GetTileData(tileType, 0);
                int minX = placeX, minY = placeY, pieces = 0;
                for (int x = placeX - 5; x < placeX + 6; x++)
                for (int y = placeY - 5; y < placeY + 3; y++)
                    if (Main.tile[x, y].HasTile && Main.tile[x, y].TileType == tileType)
                    {
                        minX = Math.Min(minX, x);
                        minY = Math.Min(minY, y);
                        pieces++;
                    }
                Require(pieces == objectData.Width * objectData.Height,
                    $"tile {tileType}: expected {objectData.Width * objectData.Height} placed cells, found {pieces}");
                bool destroyBeforeKill = WorldGen.destroyObject;
                WorldGen.KillTile(minX + objectData.Width / 2, minY + objectData.Height / 2, false, false, false);
                bool destroyAfterKill = WorldGen.destroyObject;
                // In a menu-time scratch map KillTile can return while the
                // global recursion guard remains set. Resume the normal tile
                // framing pass with the original guard state.
                WorldGen.destroyObject = false;
                // The menu-time synthetic arena does not run the normal tile
                // update pass. Re-enter the same tML multitile dispatcher from
                // the preserved top-left component; this performs native
                // removal/drop handling rather than manually editing cells.
                WorldGen.destroyObject = false;
                int checkX = -1, checkY = -1;
                for (int x = minX - 1; x <= minX + objectData.Width && checkX < 0; x++)
                for (int y = minY - 1; y <= minY + objectData.Height; y++)
                    if (Main.tile[x, y].HasTile && Main.tile[x, y].TileType == tileType)
                    {
                        checkX = x;
                        checkY = y;
                        break;
                    }
                if (checkX >= 0)
                    TileLoader.CheckModTile(checkX, checkY, tileType);
                var leftoverCells = Enumerable.Range(minX - 1, objectData.Width + 2)
                    .SelectMany(x => Enumerable.Range(minY - 1, objectData.Height + 2)
                        .Where(y => Main.tile[x, y].HasTile && Main.tile[x, y].TileType == tileType)
                        .Select(y => $"{x},{y}/f{Main.tile[x, y].TileFrameX},{Main.tile[x, y].TileFrameY}"))
                    .ToArray();
                int drops = Main.item.Count(item => item.active && item.type == itemType);
                if (leftoverCells.Length != 0 || drops != 1)
                    stationErrors.Add($"tile={tileType}, footprint={objectData.Width}x{objectData.Height}, leftovers=[{string.Join(';', leftoverCells)}], drops={drops}, destroyPlace={destroyBeforePlace}->{destroyAfterPlace}, destroyKill={destroyBeforeKill}->{destroyAfterKill}, TileID.Count={TileID.Count}, gen={WorldGen.gen}, generatingWorld={WorldGen.generatingWorld}, noTileActions={WorldGen.noTileActions}, destroyObject={WorldGen.destroyObject}");
            }
            Require(stationErrors.Count == 0, "station place/break failures: " + string.Join(" | ", stationErrors));
        }
        finally
        {
            for (int x = originX; x < originX + width; x++)
            for (int y = groundY - 6; y <= groundY + 1; y++)
            {
                Tile tile = Main.tile[x, y];
                tile.ClearEverything();
            }
            Main.item = oldItems;
            Main.gameMenu = oldGameMenu;
            Main.netMode = oldNetMode;
            WorldGen.gen = oldWorldGen;
            WorldGen.generatingWorld = oldGeneratingWorld;
            WorldGen.noTileActions = oldNoTileActions;
            WorldGen.destroyObject = oldDestroyObject;
        }
    }

    private static string TestDedicatedStationRecipes()
    {
        Player[] oldPlayers = (Player[])Main.player.Clone();
        int oldMyPlayer = Main.myPlayer;
        const int playerIndex = 0;
        Player player = Main.player[playerIndex].SerializedClone();
        MethodInfo setupPlayer = typeof(PlayerLoader).GetMethod("SetupPlayer", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("PlayerLoader.SetupPlayer unavailable for native station fixture");
        setupPlayer.Invoke(null, new object[] { player });
        player.whoAmI = playerIndex;
        bool oldGameMenu = Main.gameMenu;
        bool oldActive = player.active;
        bool oldDestroyObject = WorldGen.destroyObject;
        bool oldGen = WorldGen.gen, oldGenerating = WorldGen.generatingWorld, oldNoTileActions = WorldGen.noTileActions;
        Item[] oldWorldItems = Main.item;
        Vector2 oldPosition = player.position;
        Item[] oldInventory = player.inventory.Select(item => item.Clone()).ToArray();
        bool[] oldAdjTiles = player.adjTile.ToArray();
        const int x0 = 72, groundY = 92;
        var cases = new (int Tile, int Output, (int Item, int Stack)[] Ingredients, string Name)[]
        {
            (ModContent.TileType<MilkcraftWorkbenchTile>(), ModContent.ItemType<MilkcraftFurnace>(),
                new[] { ((int)ItemID.StoneBlock, 20), ((int)ItemID.Wood, 4), ((int)ItemID.Torch, 3) }, "workbench->furnace"),
            (ModContent.TileType<MilkcraftFurnaceTile>(), ModContent.ItemType<BlackIronBar>(),
                new[] { (ModContent.ItemType<BlackIronOre>(), 3) }, "furnace->black-iron-bar"),
            (ModContent.TileType<MilkcraftLoomTile>(), ModContent.ItemType<CoarseWool>(),
                new[] { ((int)ItemID.Silk, 1) }, "loom->coarse-wool"),
            (ModContent.TileType<MilkcraftForgeTile>(), ModContent.ItemType<IronShield>(),
                new[] { ((int)ItemID.IronBar, 6), ((int)ItemID.GoldBar, 1), ((int)ItemID.Wood, 4) }, "forge->iron-shield"),
            (ModContent.TileType<MilkcraftDistilleryTile>(), ModContent.ItemType<Alcohol>(),
                new[] { ((int)ItemID.BottledWater, 1), ((int)ItemID.Mushroom, 1), ((int)ItemID.Gel, 1) }, "distillery->alcohol")
        };
        MethodInfo scanAdjacency = typeof(Player).GetMethod("AdjTiles", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null, Type.EmptyTypes, null) ?? typeof(Player).GetMethod("UpdateAdjTiles", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null, Type.EmptyTypes, null);
        MethodInfo findRecipes = typeof(Recipe).GetMethod("FindRecipes", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
            null, new[] { typeof(bool) }, null);
        if (scanAdjacency is null || findRecipes is null)
            throw new InvalidOperationException($"native crafting methods unavailable: adjacency={scanAdjacency?.Name ?? "missing"}, findRecipes={findRecipes?.Name ?? "missing"}");

        List<int> touchedX = new(), touchedY = new();
        try
        {
            Main.gameMenu = false;
            Main.myPlayer = playerIndex;
            Main.player[playerIndex] = player;
            player.active = true;
            Main.item = Enumerable.Range(0, Math.Max(400, Main.maxItems + 1)).Select(_ => new Item()).ToArray();
            WorldGen.gen = false;
            WorldGen.generatingWorld = false;
            WorldGen.noTileActions = false;
            WorldGen.destroyObject = false;
            for (int x = x0 - 4; x <= x0 + 4; x++)
            for (int y = groundY - 5; y <= groundY + 1; y++)
            {
                Require(!Main.tile[x, y].HasTile, $"station-recipe scratch cell occupied at {x},{y}");
                if (y == groundY)
                {
                    Tile tile = Main.tile[x, y];
                    tile.HasTile = true;
                    tile.TileType = TileID.Stone;
                    touchedX.Add(x); touchedY.Add(y);
                }
            }

            foreach (var test in cases)
            {
                ModTile stationTile = TileLoader.GetTile(test.Tile)
                    ?? throw new InvalidOperationException($"missing ModTile for {test.Name}");
                int[] expectedAdjacency = test.Name.StartsWith("workbench", StringComparison.Ordinal)
                    ? new[] { (int)TileID.WorkBenches }
                    : Array.Empty<int>();
                Require(stationTile.AdjTiles.OrderBy(value => value).SequenceEqual(expectedAdjacency),
                    $"{test.Name}: station aliases unexpected vanilla crafting tiles [{string.Join(',', stationTile.AdjTiles)}]");

                for (int slot = 0; slot < player.inventory.Length; slot++)
                    player.inventory[slot].TurnToAir();
                foreach ((int type, int stack) in test.Ingredients)
                {
                    Item ingredient = new();
                    ingredient.SetDefaults(type);
                    ingredient.stack = stack;
                    player.inventory[Array.FindIndex(player.inventory, item => item.IsAir)] = ingredient;
                }

                TileObjectData data = TileObjectData.GetTileData(test.Tile, 0);
                int anchorX = x0, anchorY = groundY - 1;
                Require(WorldGen.PlaceObject(anchorX, anchorY, test.Tile, true, 0), $"could not place {test.Name} station");
                int originX = anchorX, originY = anchorY, cells = 0;
                for (int x = anchorX - data.Width; x <= anchorX + data.Width; x++)
                for (int y = anchorY - data.Height; y <= anchorY + 1; y++)
                    if (Main.tile[x, y].HasTile && Main.tile[x, y].TileType == test.Tile)
                    {
                        originX = Math.Min(originX, x); originY = Math.Min(originY, y); cells++;
                        touchedX.Add(x); touchedY.Add(y);
                    }
                Require(cells == data.Width * data.Height, $"{test.Name}: incomplete placed station footprint {cells}/{data.Width * data.Height}");
                player.position = new Vector2((originX + data.Origin.X + 1) * 16f, (originY + data.Origin.Y + 1) * 16f);
                Require(!stationTile.RightClick(originX + data.Width / 2, originY + data.Height / 2),
                    $"{test.Name}: ordinary crafting station unexpectedly consumed right-click for a custom UI action");
                Array.Clear(player.adjTile);
                scanAdjacency.Invoke(player, null);
                Require(player.adjTile[test.Tile], $"{test.Name}: actual Player.AdjTiles did not detect nearby dedicated station");

                findRecipes.Invoke(null, new object[] { false });
                bool available = false;
                for (int i = 0; i < Main.numAvailableRecipes; i++)
                {
                    int recipeIndex = Main.availableRecipe[i];
                    if (recipeIndex >= 0 && recipeIndex < Main.recipe.Length
                        && Main.recipe[recipeIndex].createItem.type == test.Output)
                    { available = true; break; }
                }
                Require(available, $"{test.Name}: native Recipe.FindRecipes did not expose representative output");

                // A nearby tile from another dedicated station must not satisfy this recipe.
                Array.Clear(player.adjTile);
                player.adjTile[TileID.Anvils] = true;
                findRecipes.Invoke(null, new object[] { false });
                bool leaked = false;
                for (int i = 0; i < Main.numAvailableRecipes; i++)
                {
                    int recipeIndex = Main.availableRecipe[i];
                    if (recipeIndex >= 0 && recipeIndex < Main.recipe.Length
                        && Main.recipe[recipeIndex].createItem.type == test.Output)
                    { leaked = true; break; }
                }
                Require(!leaked, $"{test.Name}: dedicated recipe leaked to a vanilla workbench");
                WorldGen.KillTile(originX + data.Width / 2, originY + data.Height / 2, false, false, false);
                WorldGen.destroyObject = false;
            }
            return "native-StationAdjacency+Recipe.FindRecipes=PASS workbench/furnace/loom/forge/distillery; dedicated outputs available only at their station";
        }
        finally
        {
            foreach (int x in touchedX.Distinct())
            foreach (int y in touchedY.Distinct())
                if (x >= x0 - 4 && x <= x0 + 4 && y >= groundY - 5 && y <= groundY + 1)
                    Main.tile[x, y].ClearEverything();
            Main.myPlayer = oldMyPlayer;
            Main.player = oldPlayers;
            Main.gameMenu = oldGameMenu;
            player.active = oldActive;
            player.position = oldPosition;
            for (int slot = 0; slot < oldInventory.Length; slot++) player.inventory[slot] = oldInventory[slot];
            Array.Copy(oldAdjTiles, player.adjTile, Math.Min(oldAdjTiles.Length, player.adjTile.Length));
            Main.item = oldWorldItems;
            WorldGen.gen = oldGen;
            WorldGen.generatingWorld = oldGenerating;
            WorldGen.noTileActions = oldNoTileActions;
            WorldGen.destroyObject = oldDestroyObject;
        }
    }

    private void RenderPreviews()
    {
        GraphicsDevice device = Main.graphics.GraphicsDevice;
        const int width = 400;
        const int height = 240;
        const int scale = 3;
        int renderWidth = Math.Max(width, Main.screenWidth);
        int renderHeight = Math.Max(height, Main.screenHeight);
        using var fullTarget = new RenderTarget2D(device, renderWidth, renderHeight, false, SurfaceFormat.Color,
            DepthFormat.None, 0, RenderTargetUsage.PreserveContents);

        var suits = new[]
        {
            (ModContent.ItemType<IronHelmet>(), ModContent.ItemType<IronChestplate>(), ModContent.ItemType<IronGreaves>(), ModContent.ItemType<IronSword>(), "T1 Iron"),
            (ModContent.ItemType<LeatherHeadband>(), ModContent.ItemType<LeatherTunic>(), ModContent.ItemType<LeatherLeggings>(), ModContent.ItemType<IronBow>(), "T1 Leather"),
            (ModContent.ItemType<ClothHood>(), ModContent.ItemType<ClothRobe>(), ModContent.ItemType<ClothLeggings>(), ModContent.ItemType<IronStaff>(), "T1 Cloth"),
            (ModContent.ItemType<KnightHelmet>(), ModContent.ItemType<KnightChestplate>(), ModContent.ItemType<KnightGreaves>(), ModContent.ItemType<KnightSword>(), "T2 Knight"),
            (ModContent.ItemType<HuntingHood>(), ModContent.ItemType<HuntingChestplate>(), ModContent.ItemType<HuntingLeggings>(), ModContent.ItemType<HuntingBow>(), "T2 Hunt"),
            (ModContent.ItemType<EnergyHood>(), ModContent.ItemType<EnergyRobe>(), ModContent.ItemType<EnergyLeggings>(), ModContent.ItemType<EnergyStaff>(), "T2 Energy")
        };

        device.SetRenderTarget(fullTarget);
        device.Clear(new Color(36, 39, 48));
        Main.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp,
            DepthStencilState.None, RasterizerState.CullCounterClockwise);
        var temporaryTextures = new System.Collections.Generic.List<Texture2D>();
        temporaryTextures.Add(DrawStation(ModContent.GetInstance<MilkcraftWorkbenchTile>().Texture, 35, 28));
        temporaryTextures.Add(DrawStation(ModContent.GetInstance<MilkcraftFurnaceTile>().Texture, 90, 28));
        temporaryTextures.Add(DrawStation(ModContent.GetInstance<MilkcraftLoomTile>().Texture, 145, 28));
        temporaryTextures.Add(DrawStation(ModContent.GetInstance<MilkcraftForgeTile>().Texture, 200, 28));
        temporaryTextures.Add(DrawStation(ModContent.GetInstance<MilkcraftDistilleryTile>().Texture, 255, 28));
        temporaryTextures.Add(DrawStation(ModContent.GetInstance<MilkcraftAlchemyTableTile>().Texture, 310, 28));
        temporaryTextures.Add(DrawStation(ModContent.GetInstance<MilkcraftEnchantingTableTile>().Texture, 365, 28));

        for (int i = 0; i < suits.Length; i++)
        {
            var suit = suits[i];
            int x = 40 + i * 64;
            Player still = CreatePreviewPlayer(suit.Item1, suit.Item2, suit.Item3);
            DrawPreviewPlayer(still, x, 88);

            Player walking = CreatePreviewPlayer(suit.Item1, suit.Item2, suit.Item3, walk: true);
            DrawPreviewPlayer(walking, x, 140);
        }
        Main.spriteBatch.End();

        // PlayerRenderer alters shader/batch state and does not reliably draw
        // held items for menu mannequins. Show the six actual native item icons
        // in a fresh batch instead of claiming this is a held-weapon preview.
        Main.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp,
            DepthStencilState.None, RasterizerState.CullCounterClockwise);
        for (int i = 0; i < suits.Length; i++)
            DrawWeaponIcon(suits[i].Item4, 40 + i * 64, 210);
        Main.spriteBatch.End();

        foreach (Texture2D temporaryTexture in temporaryTextures)
            temporaryTexture.Dispose();

        using var target = new RenderTarget2D(device, width, height, false, SurfaceFormat.Color,
            DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
        device.SetRenderTarget(target);
        device.Clear(new Color(36, 39, 48));
        Main.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp,
            DepthStencilState.None, RasterizerState.CullCounterClockwise);
        Main.spriteBatch.Draw(fullTarget, new Rectangle(0, 0, width, height), new Rectangle(0, 0, width, height), Color.White);
        Main.spriteBatch.End();

        using var enlarged = new RenderTarget2D(device, width * scale, height * scale, false, SurfaceFormat.Color,
            DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
        device.SetRenderTarget(enlarged);
        device.Clear(Color.Black);
        Main.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp,
            DepthStencilState.None, RasterizerState.CullCounterClockwise);
        Main.spriteBatch.Draw(target, new Rectangle(0, 0, width * scale, height * scale), Color.White);
        Main.spriteBatch.End();
        device.SetRenderTarget(null);

        using var stream = File.Create(Path.Combine(OutputDirectory, "milkcraft-native-preview.png"));
        enlarged.SaveAsPng(stream, width * scale, height * scale);
        using var nativeStream = File.Create(Path.Combine(OutputDirectory, "milkcraft-native-preview-1x.png"));
        target.SaveAsPng(nativeStream, width, height);
    }

    private void RenderTier3T4ArmorFrames()
    {
        ValidateHeadgearFaceFlags();
        GraphicsDevice device = Main.graphics.GraphicsDevice;
        Viewport previousViewport = device.Viewport;
        const int width = 800;
        const int height = 450;
        const int scale = 2;
        int renderWidth = Main.screenWidth;
        int renderHeight = Main.screenHeight;
        using var fullTarget = new RenderTarget2D(device, renderWidth, renderHeight, false,
            SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
        // Keep one shared native-render matrix for all armor tiers. This lets
        // reviewers compare body silhouette and pose anchors without changing
        // player scale, armor slots, or weapon geometry between pages.
        var suits = new (string Name, int Head, int Body, int Legs)[]
        {
            ("T1-Iron", ModContent.ItemType<IronHelmet>(), ModContent.ItemType<IronChestplate>(), ModContent.ItemType<IronGreaves>()),
            ("T1-Ranger", ModContent.ItemType<LeatherHeadband>(), ModContent.ItemType<LeatherTunic>(), ModContent.ItemType<LeatherLeggings>()),
            ("T1-Mage", ModContent.ItemType<ClothHood>(), ModContent.ItemType<ClothRobe>(), ModContent.ItemType<ClothLeggings>()),
            ("T2-Knight", ModContent.ItemType<KnightHelmet>(), ModContent.ItemType<KnightChestplate>(), ModContent.ItemType<KnightGreaves>()),
            ("T2-Hunter", ModContent.ItemType<HuntingHood>(), ModContent.ItemType<HuntingChestplate>(), ModContent.ItemType<HuntingLeggings>()),
            ("T2-Mage", ModContent.ItemType<EnergyHood>(), ModContent.ItemType<EnergyRobe>(), ModContent.ItemType<EnergyLeggings>()),
            ("Templar", ModContent.ItemType<TemplarHelmet>(), ModContent.ItemType<TemplarChestplate>(), ModContent.ItemType<TemplarGreaves>()),
            ("Nightmare", ModContent.ItemType<NightmareHood>(), ModContent.ItemType<NightmareChestplate>(), ModContent.ItemType<NightmareLeggings>()),
            ("Fallen", ModContent.ItemType<FallenHood>(), ModContent.ItemType<FallenRobe>(), ModContent.ItemType<FallenLeggings>()),
            ("Soulforged", ModContent.ItemType<SoulforgedHelmet>(), ModContent.ItemType<SoulforgedChestplate>(), ModContent.ItemType<SoulforgedGreaves>()),
            ("Abyss", ModContent.ItemType<AbyssHood>(), ModContent.ItemType<AbyssChestplate>(), ModContent.ItemType<AbyssLeggings>()),
            ("Dark", ModContent.ItemType<DarkHood>(), ModContent.ItemType<DarkRobe>(), ModContent.ItemType<DarkLeggings>())
        };
        int localPlayerIndex = Math.Clamp(Main.myPlayer, 0, Main.player.Length - 1);
        Player previousPlayer = Main.player[localPlayerIndex];
        bool previousMenu = Main.gameMenu;
        bool previousLighting = ForcePreviewLighting;
        bool previousArmorCapture = CaptureTier3T4ArmorData;
        float previousUiScale = Main.UIScale;
        Vector2 previousZoom = Main.GameViewMatrix.Zoom;
        Vector2 previousScreenPosition = Main.screenPosition;
        bool poseBatchBegun = false;
        try
        {
            Main.gameMenu = false;
            ForcePreviewLighting = true;
            Main.UIScale = 1f;
            Main.GameViewMatrix.Zoom = Vector2.One;
            CaptureTier3T4ArmorData = true;
            File.WriteAllText(Path.Combine(OutputDirectory, "native-12-tier-armor-frames.log"),
                $"Twelve T1-T4 armor sets; native PlayerRenderer; target={renderWidth}x{renderHeight}, viewport={device.Viewport.Width}x{device.Viewport.Height}, " +
                $"screen={Main.screenWidth}x{Main.screenHeight}, zoom={Main.GameViewMatrix.Zoom}, transform={Main.GameViewMatrix.TransformationMatrix}, " +
                $"effectMatrix={Main.GameViewMatrix.EffectMatrix}, UIScale={Main.UIScale}.\n");
            string[] poseNames = { "stand", "walk", "jump" };
            foreach (int direction in new[] { 1, -1 })
            {
                Main.screenPosition = Vector2.Zero;
                device.SetRenderTarget(fullTarget);
                var targetViewport = new Viewport(0, 0, renderWidth, renderHeight);
                device.Viewport = targetViewport;
                Main.GameViewMatrix.SetViewportOverride(targetViewport);
                File.AppendAllText(Path.Combine(OutputDirectory, "native-12-tier-armor-frames.log"),
                    $"direction={(direction > 0 ? "right" : "left")} viewport={targetViewport} " +
                    $"effectMatrix={Main.GameViewMatrix.EffectMatrix}\n");
                device.Clear(new Color(28, 31, 39));
                Main.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp,
                    DepthStencilState.None, RasterizerState.CullCounterClockwise);
                poseBatchBegun = true;
                for (int row = 0; row < poseNames.Length; row++)
                for (int col = 0; col < suits.Length; col++)
                {
                    var suit = suits[col];
                    Player actor = CreatePreviewPlayer(suit.Head, suit.Body, suit.Legs,
                        walk: row == 1, jump: row == 2);
                    actor.whoAmI = localPlayerIndex;
                    actor.Male = true;
                    actor.GetModPlayer<Tier3T4ProgressPlayer>().ApplyNetworkState(true, true, false);
                    actor.direction = direction;
                    actor.isDisplayDollOrInanimate = false;
                    if (row == 1) actor.velocity = new Vector2(direction * 2.2f, 0f);
                    if (row == 2)
                    {
                        actor.velocity = new Vector2(direction, -6f);
                        actor.controlJump = true;
                        actor.jump = 1;
                    }
                    actor.oldVelocity = actor.velocity;
                    Main.player[localPlayerIndex] = actor;
                    // Let the game choose the native composite torso, arm and
                    // leg frames for this movement state instead of assigning
                    // a guessed atlas row in the preview fixture.
                    actor.PlayerFrame();
                    NativePoseLabel = $"{suit.Name}/{poseNames[row]}/{(direction > 0 ? "right" : "left")}/male";
                    Require(ArmorIDs.Body.Sets.HidesArms[actor.body] && ArmorIDs.Body.Sets.HidesHands[actor.body] &&
                            !ArmorIDs.Body.Sets.DisableHandOnAndOffAccDraw[actor.body],
                        $"{NativePoseLabel} must use armor-covered arms/hands while preserving hand/accessory draw");
                    ArmorCaptureDrawCount = 0;
                    DrawPreviewPlayer(actor, 28 + col * 64, 40 + row * 132, 1f);
                    Require(ArmorCaptureDrawCount >= 3,
                        $"{NativePoseLabel} must draw head/body/leg armor through native PlayerRenderer, saw {ArmorCaptureDrawCount} armor draw entries");
                    File.AppendAllText(Path.Combine(OutputDirectory, "native-12-tier-armor-frames.log"),
                        $"suit={suit.Name} pose={poseNames[row]} direction={direction} male={actor.Male} head={actor.head} body={actor.body} legs={actor.legs} " +
                        $"bodyFrame={actor.bodyFrame} legFrame={actor.legFrame} " +
                        $"HidesArms={ArmorIDs.Body.Sets.HidesArms[actor.body]} HidesHands={ArmorIDs.Body.Sets.HidesHands[actor.body]} " +
                        $"DisableHandOnAndOffAccDraw={ArmorIDs.Body.Sets.DisableHandOnAndOffAccDraw[actor.body]} armorDrawEntries={ArmorCaptureDrawCount}\n");
                }
                Main.spriteBatch.End();
                poseBatchBegun = false;

                string directionName = direction > 0 ? "right" : "left";
                SavePreviewCrop(fullTarget, width, height, scale,
                    $"native-12-tier-armor-frames-{directionName}-1x.png",
                    $"native-12-tier-armor-frames-{directionName}.png");
            }
        }
        finally
        {
            NativePoseLabel = "";
            CaptureTier3T4ArmorData = previousArmorCapture;
            Main.player[localPlayerIndex] = previousPlayer;
            ForcePreviewLighting = previousLighting;
            Main.gameMenu = previousMenu;
            Main.UIScale = previousUiScale;
            Main.screenPosition = previousScreenPosition;
            if (poseBatchBegun)
                Main.spriteBatch.End();
            device.SetRenderTarget(null);
            device.Viewport = previousViewport;
            Main.GameViewMatrix.ClearViewportOverride();
            Main.GameViewMatrix.Zoom = previousZoom;
        }

    }

    internal void RenderNativeArmReachDiagnostics()
    {
        const int width = 900;
        const int height = 360;
        const int scale = 2;
        int renderWidth = Math.Max(width, Main.screenWidth);
        int renderHeight = Math.Max(height, Main.screenHeight);
        GraphicsDevice device = Main.graphics.GraphicsDevice;
        Viewport previousViewport = device.Viewport;
        var suits = new (string Tier, string Name, int Head, int Body, int Legs, int Weapon)[]
        {
            ("T1", "Iron", ModContent.ItemType<IronHelmet>(), ModContent.ItemType<IronChestplate>(), ModContent.ItemType<IronGreaves>(), ModContent.ItemType<IronSword>()),
            ("T1", "Ranger", ModContent.ItemType<LeatherHeadband>(), ModContent.ItemType<LeatherTunic>(), ModContent.ItemType<LeatherLeggings>(), ModContent.ItemType<IronBow>()),
            ("T1", "Mage", ModContent.ItemType<ClothHood>(), ModContent.ItemType<ClothRobe>(), ModContent.ItemType<ClothLeggings>(), ModContent.ItemType<IronStaff>()),
            ("T2", "Knight", ModContent.ItemType<KnightHelmet>(), ModContent.ItemType<KnightChestplate>(), ModContent.ItemType<KnightGreaves>(), ModContent.ItemType<KnightSword>()),
            ("T2", "Hunter", ModContent.ItemType<HuntingHood>(), ModContent.ItemType<HuntingChestplate>(), ModContent.ItemType<HuntingLeggings>(), ModContent.ItemType<HuntingBow>()),
            ("T2", "Mage", ModContent.ItemType<EnergyHood>(), ModContent.ItemType<EnergyRobe>(), ModContent.ItemType<EnergyLeggings>(), ModContent.ItemType<EnergyStaff>()),
            ("T3", "Templar", ModContent.ItemType<TemplarHelmet>(), ModContent.ItemType<TemplarChestplate>(), ModContent.ItemType<TemplarGreaves>(), ModContent.ItemType<TemplarSword>()),
            ("T3", "Nightmare", ModContent.ItemType<NightmareHood>(), ModContent.ItemType<NightmareChestplate>(), ModContent.ItemType<NightmareLeggings>(), ModContent.ItemType<NightmareBow>()),
            ("T3", "Fallen", ModContent.ItemType<FallenHood>(), ModContent.ItemType<FallenRobe>(), ModContent.ItemType<FallenLeggings>(), ModContent.ItemType<FallenStaff>()),
            ("T4", "Soulforged", ModContent.ItemType<SoulforgedHelmet>(), ModContent.ItemType<SoulforgedChestplate>(), ModContent.ItemType<SoulforgedGreaves>(), ModContent.ItemType<SoulforgedSword>()),
            ("T4", "Abyss", ModContent.ItemType<AbyssHood>(), ModContent.ItemType<AbyssChestplate>(), ModContent.ItemType<AbyssLeggings>(), ModContent.ItemType<AbyssBow>()),
            ("T4", "Dark", ModContent.ItemType<DarkHood>(), ModContent.ItemType<DarkRobe>(), ModContent.ItemType<DarkLeggings>(), ModContent.ItemType<DarkStaff>())
        };
        var poses = new (string Name, int Direction, float Angle)[]
        {
            ("horizontal-right", 1, 0f),
            ("horizontal-left", -1, MathHelper.Pi),
            ("raised-right", 1, -MathHelper.PiOver4),
            ("raised-left", -1, -3f * MathHelper.PiOver4)
        };
        int localPlayerIndex = Math.Clamp(Main.myPlayer, 0, Main.player.Length - 1);
        Player previousPlayer = Main.player[localPlayerIndex];
        bool previousMenu = Main.gameMenu;
        bool previousLighting = ForcePreviewLighting;
        bool previousCapture = CaptureArmReachData;
        Vector2 previousScreenPosition = Main.screenPosition;
        float previousUiScale = Main.UIScale;
        Vector2 previousZoom = Main.GameViewMatrix.Zoom;
        int previousMouseX = Main.mouseX;
        int previousMouseY = Main.mouseY;
        Projectile[] previousProjectiles = Main.projectile;
        try
        {
            Main.gameMenu = false;
            ForcePreviewLighting = true;
            CaptureArmReachData = true;
            Main.UIScale = 1f;
            Main.GameViewMatrix.Zoom = Vector2.One;
            Main.projectile = (Projectile[])previousProjectiles.Clone();
            File.WriteAllText(Path.Combine(OutputDirectory, "native-12-tier-arm-reach-drawdata.log"),
                "Native ItemCheck + PlayerFrame + PlayerRenderer; 12 complete sets, 4 poses per set. " +
                "Each row logs source UV, alpha pixels, position/origin/rotation/effect, native composite-arm frames and hand/armor draw order.\n");

            foreach (string tier in new[] { "T1", "T2", "T3", "T4" })
            {
                using var target = new RenderTarget2D(Main.graphics.GraphicsDevice, renderWidth, renderHeight, false,
                    SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
                device.SetRenderTarget(target);
                var targetViewport = new Viewport(0, 0, renderWidth, renderHeight);
                device.Viewport = targetViewport;
                Main.GameViewMatrix.SetViewportOverride(targetViewport);
                File.AppendAllText(Path.Combine(OutputDirectory, "native-12-tier-arm-reach-drawdata.log"),
                    $"tier={tier} viewport={targetViewport} effectMatrix={Main.GameViewMatrix.EffectMatrix}\n");
                device.Clear(new Color(28, 31, 39));
                Main.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp,
                    DepthStencilState.None, RasterizerState.CullCounterClockwise);
                try
                {
                    var tierSuits = suits.Where(suit => suit.Tier == tier).ToArray();
                    for (int row = 0; row < tierSuits.Length; row++)
                    for (int col = 0; col < poses.Length; col++)
                    {
                        var suit = tierSuits[row];
                        var pose = poses[col];
                        Main.screenPosition = Vector2.Zero;
                        NativePoseLabel = $"arm-reach/{suit.Tier}-{suit.Name}/{pose.Name}";
                        int reachWeapon = suit.Name switch
                        {
                            "Iron" => ModContent.ItemType<IronStaff>(),
                            "Knight" => ModContent.ItemType<EnergyStaff>(),
                            "Templar" => ModContent.ItemType<FallenStaff>(),
                            "Soulforged" => ModContent.ItemType<DarkStaff>(),
                            _ => suit.Weapon
                        };
                        Player actor = CreateActionPlayer(suit.Head, suit.Body, suit.Legs, reachWeapon,
                            pose.Direction, pose.Angle, male: true, pose: 0);
                        actor.whoAmI = localPlayerIndex;
                        int bodySlot = actor.body;
                        bool previousHidesArms = ArmorIDs.Body.Sets.HidesArms[bodySlot];
                        bool previousHidesHands = ArmorIDs.Body.Sets.HidesHands[bodySlot];
                        bool previousDisableHandAcc = ArmorIDs.Body.Sets.DisableHandOnAndOffAccDraw[bodySlot];
                        Require(previousHidesArms && previousHidesHands && !previousDisableHandAcc,
                            $"{suit.Tier}-{suit.Name} must hide bare skin and preserve native hand/accessory draw");

                        // Draw a matching skin-visible reference off-canvas to
                        // capture the real hand UVs, then compare those pixels
                        // against later armor-body DrawData at the same UV and
                        // transform. The actual page below uses covered flags.
                        Player referenceActor = CreateActionPlayer(suit.Head, suit.Body, suit.Legs, reachWeapon,
                            pose.Direction, pose.Angle, male: true, pose: 0);
                        referenceActor.whoAmI = localPlayerIndex;
                        Main.player[localPlayerIndex] = referenceActor;
                        NativePoseLabel = $"arm-reach/{suit.Tier}-{suit.Name}/{pose.Name}/hand-uv-reference";
                        ArmReachCaptureDrawCount = 0;
                        ArmReachNativeHandDrawCount = 0;
                        ArmReachReferenceHandPixels = 0;
                        ArmReachCoveredHandPixels = 0;
                        ArmReachCoverageDrawPairs = 0;
                        CaptureArmReachReferenceOnly = true;
                        try
                        {
                            ArmorIDs.Body.Sets.HidesArms[bodySlot] = false;
                            ArmorIDs.Body.Sets.HidesHands[bodySlot] = false;
                            DrawNativeItemCheckPreview(referenceActor, -4096, -4096, 1f, pose.Angle);
                        }
                        finally
                        {
                            ArmorIDs.Body.Sets.HidesArms[bodySlot] = previousHidesArms;
                            ArmorIDs.Body.Sets.HidesHands[bodySlot] = previousHidesHands;
                            ArmorIDs.Body.Sets.DisableHandOnAndOffAccDraw[bodySlot] = previousDisableHandAcc;
                            CaptureArmReachReferenceOnly = false;
                        }

                        int nativeReferencePixels = ArmReachReferenceHandPixels;
                        int coveredReferencePixels = ArmReachCoveredHandPixels;
                        int coverageDrawPairs = ArmReachCoverageDrawPairs;
                        Require(ArmReachNativeHandDrawCount > 0 && nativeReferencePixels > 0,
                            $"{NativePoseLabel} must capture nontransparent native hand atlas pixels");
                        Require(coverageDrawPairs > 0 && coveredReferencePixels == nativeReferencePixels,
                            $"{NativePoseLabel} armor body UV must cover every native hand pixel: " +
                            $"covered={coveredReferencePixels}/{nativeReferencePixels}, matchingDraws={coverageDrawPairs}");

                        actor = CreateActionPlayer(suit.Head, suit.Body, suit.Legs, reachWeapon,
                            pose.Direction, pose.Angle, male: true, pose: 0);
                        actor.whoAmI = localPlayerIndex;
                        Main.player[localPlayerIndex] = actor;
                        NativePoseLabel = $"arm-reach/{suit.Tier}-{suit.Name}/{pose.Name}";
                        ArmReachCaptureDrawCount = 0;
                        ArmReachNativeHandDrawCount = 0;
                        CaptureArmReachReferenceOnly = false;
                        DrawNativeItemCheckPreview(actor, 55 + col * 205, 45 + row * 105, 1f, pose.Angle);
                        Require(ArmReachCaptureDrawCount > 0,
                            $"{NativePoseLabel} must capture native body and segmented armor DrawData");
                        Require(ArmReachNativeHandDrawCount == 0,
                            $"{NativePoseLabel} must not draw bare native hand pixels over the gauntlet");
                        File.AppendAllText(Path.Combine(OutputDirectory, "native-12-tier-arm-reach-drawdata.log"),
                            $"case={NativePoseLabel} held={actor.inventory[0].Name} source={reachWeapon} bodySlot={actor.body} " +
                            $"HidesArms={ArmorIDs.Body.Sets.HidesArms[actor.body]} HidesHands={ArmorIDs.Body.Sets.HidesHands[actor.body]} " +
                            $"DisableHandOnAndOffAccDraw={ArmorIDs.Body.Sets.DisableHandOnAndOffAccDraw[actor.body]} " +
                            $"nativeHandReferencePixels={nativeReferencePixels} armoredCoverage={coveredReferencePixels}/{nativeReferencePixels} " +
                            $"coverageDrawPairs={coverageDrawPairs} finalNativeHandDraws={ArmReachNativeHandDrawCount} " +
                            $"drawEntries={ArmReachCaptureDrawCount} PASS\n");
                    }
                }
                finally
                {
                    Main.spriteBatch.End();
                }

                Main.screenPosition = Vector2.Zero;
                SavePreviewCrop(target, width, height, scale, null, $"native-12-tier-arm-reach-{tier}.png");
            }
        }
        finally
        {
            NativePoseLabel = "";
            CaptureArmReachData = previousCapture;
            CaptureArmReachReferenceOnly = false;
            Main.player[localPlayerIndex] = previousPlayer;
            Main.projectile = previousProjectiles;
            ForcePreviewLighting = previousLighting;
            Main.gameMenu = previousMenu;
            Main.screenPosition = previousScreenPosition;
            Main.UIScale = previousUiScale;
            Main.mouseX = previousMouseX;
            Main.mouseY = previousMouseY;
            device.SetRenderTarget(null);
            device.Viewport = previousViewport;
            Main.GameViewMatrix.ClearViewportOverride();
            Main.GameViewMatrix.Zoom = previousZoom;
        }
    }

    private void RenderHeadwearPreview()
    {
        GraphicsDevice device = Main.graphics.GraphicsDevice;
        const int width = 420;
        const int height = 140;
        const int scale = 3;
        int renderWidth = Math.Max(width, Main.screenWidth);
        int renderHeight = Math.Max(height, Main.screenHeight);
        using var fullTarget = new RenderTarget2D(device, renderWidth, renderHeight, false, SurfaceFormat.Color,
            DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
        device.SetRenderTarget(fullTarget);
        device.Clear(new Color(36, 39, 48));
        Main.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp,
            DepthStencilState.None, RasterizerState.CullCounterClockwise);

        var previews = new[]
        {
            (0, 1, "bare"),
            (ModContent.ItemType<LeatherHeadband>(), 1, "leather-open"),
            (ModContent.ItemType<DarkHood>(), 1, "dark-open"),
            (ModContent.ItemType<TemplarHelmet>(), 1, "templar-faceplate"),
            (0, -1, "bare"),
            (ModContent.ItemType<LeatherHeadband>(), -1, "leather-open"),
            (ModContent.ItemType<DarkHood>(), -1, "dark-open"),
            (ModContent.ItemType<TemplarHelmet>(), -1, "templar-faceplate")
        };
        int localPlayerIndex = Math.Clamp(Main.myPlayer, 0, Main.player.Length - 1);
        Player previousLocalPlayer = Main.player[localPlayerIndex];
        bool previousMenu = Main.gameMenu;
        bool previousLighting = ForcePreviewLighting;
        float previousUiScale = Main.UIScale;
        Vector2 previousGameZoom = Main.GameViewMatrix.Zoom;
        string drawDataLog = Path.Combine(OutputDirectory, "native-headwear-drawdata.log");
        File.WriteAllText(drawDataLog, "Native player-sheet DrawData; positions/source rectangles are captured from the original PlayerRenderer at 1x.\n");
        HeadwearCaptureDrawCount = 0;
        try
        {
            Main.gameMenu = false;
            Main.UIScale = 1f;
            Main.GameViewMatrix.Zoom = Vector2.One;
            File.AppendAllText(drawDataLog,
                $"fixture UIScale={Main.UIScale} screen={Main.screenWidth}x{Main.screenHeight} zoom={Main.GameViewMatrix.Zoom} " +
                $"matrix={Main.GameViewMatrix.TransformationMatrix} camera={Main.screenPosition} " +
                $"backbuffer={device.PresentationParameters.BackBufferWidth}x{device.PresentationParameters.BackBufferHeight} " +
                $"viewport={device.Viewport.Width}x{device.Viewport.Height} nativeFixtureScale=1.5 columnStep=156\n");
            ForcePreviewLighting = true;
            CaptureHeadwearNativeData = true;
            for (int i = 0; i < previews.Length; i++)
            {
                var spec = previews[i];
                Player player = CreateHeadwearPlayer(spec.Item1, spec.Item2);
                player.whoAmI = localPlayerIndex;
                Main.player[localPlayerIndex] = player;
                HeadwearCaptureLabel = $"{spec.Item3}/{(spec.Item2 > 0 ? "right" : "left")}";
                File.AppendAllText(drawDataLog,
                    $"case={HeadwearCaptureLabel} headSlot={player.head} headItem={spec.Item1} DrawHead=" +
                    $"{(player.head >= 0 ? ArmorIDs.Head.Sets.DrawHead[player.head] : false)}\n");
                // Camera's menu-stage render scale is 2/3 despite identity
                // GameViewMatrix. Compensate in the fixture only so native
                // sprite pixels map 1:1 into this 1x evidence sheet.
                DrawPreviewPlayer(player, 100 + (i % 4) * 156, i / 4 == 0 ? 31 : 91, 1.5f);
            }
            Require(HeadwearCaptureDrawCount == 8,
                $"expected native player-sheet capture for all 8 headwear actors, found {HeadwearCaptureDrawCount}");
        }
        finally
        {
            CaptureHeadwearNativeData = false;
            HeadwearCaptureLabel = "";
            ForcePreviewLighting = previousLighting;
            Main.gameMenu = previousMenu;
            Main.UIScale = previousUiScale;
            Main.GameViewMatrix.Zoom = previousGameZoom;
            Main.player[localPlayerIndex] = previousLocalPlayer;
        }
        Main.spriteBatch.End();

        using var native = new RenderTarget2D(device, width, height, false, SurfaceFormat.Color,
            DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
        device.SetRenderTarget(native);
        device.Clear(new Color(36, 39, 48));
        Main.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp,
            DepthStencilState.None, RasterizerState.CullCounterClockwise);
        Main.spriteBatch.Draw(fullTarget, new Rectangle(0, 0, width, height), new Rectangle(0, 0, width, height), Color.White);
        Main.spriteBatch.End();

        using var enlarged = new RenderTarget2D(device, width * scale, height * scale, false, SurfaceFormat.Color,
            DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
        device.SetRenderTarget(enlarged);
        device.Clear(Color.Black);
        Main.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp,
            DepthStencilState.None, RasterizerState.CullCounterClockwise);
        Main.spriteBatch.Draw(native, new Rectangle(0, 0, width * scale, height * scale), Color.White);
        Main.spriteBatch.End();
        device.SetRenderTarget(null);

        using var stream = File.Create(Path.Combine(OutputDirectory, "headwear-preview.png"));
        enlarged.SaveAsPng(stream, width * scale, height * scale);
        using var nativeStream = File.Create(Path.Combine(OutputDirectory, "headwear-preview-1x.png"));
        native.SaveAsPng(nativeStream, width, height);
    }

    private void RenderWeaponOrientationPreviews()
    {
        RenderWeaponPreviewPage("native-bow-orientation.png", bows: true);
        RenderSwordGripPreview();
    }

    private void TestKnightSwordWave()
    {
        int localPlayerIndex = Math.Clamp(Main.myPlayer, 0, Main.player.Length - 1);
        Player previousLocalPlayer = Main.player[localPlayerIndex];
        Projectile[] previousProjectiles = Main.projectile;
        bool previousMenu = Main.gameMenu;
        bool previousLighting = ForcePreviewLighting;
        float previousUiScale = Main.UIScale;
        Vector2 previousGameZoom = Main.GameViewMatrix.Zoom;
        int previousMouseX = Main.mouseX;
        int previousMouseY = Main.mouseY;
        string log = Path.Combine(OutputDirectory, "knight-sword-wave.log");
        _knightWavePreview.Clear();
        File.WriteAllText(log,
            "Native ItemCheck test: two consecutive swing starts at each direction and increased melee attack speed; each swing must add exactly one wave.\n");
        try
        {
            Main.gameMenu = false;
            ForcePreviewLighting = true;
            Main.UIScale = 1f;
            Main.GameViewMatrix.Zoom = Vector2.One;
            var aims = new (string Name, int Direction, float Angle)[]
            {
                ("right", 1, 0f),
                ("left", -1, MathHelper.Pi),
                ("up", 1, -MathHelper.PiOver2),
                ("down", 1, MathHelper.PiOver2)
            };
            int waveType = ModContent.ProjectileType<KnightSwordWave>();
            foreach (var aim in aims)
            {
                // A fresh array prevents old visual-test projectiles from
                // contaminating the count and prevents smoke from touching
                // any projectile objects owned by the actual game state.
                Main.projectile = Enumerable.Range(0, previousProjectiles.Length)
                    .Select(_ => new Projectile()).ToArray();
                Player actor = CreateActionPlayer(ModContent.ItemType<KnightHelmet>(),
                    ModContent.ItemType<KnightChestplate>(), ModContent.ItemType<KnightGreaves>(),
                    ModContent.ItemType<KnightSword>(), aim.Direction, aim.Angle, male: true, pose: 0);
                actor.whoAmI = localPlayerIndex;
                actor.GetAttackSpeed(DamageClass.Melee) += 0.50f;
                Require(!actor.inventory[0].noUseGraphic,
                    "KnightSword must remain visibly held while the wave fires");
                Main.player[localPlayerIndex] = actor;
                NativePoseLabel = $"knight-wave/{aim.Name}";
                int speedCase = 0;
                float[] speedMultipliers = { 1f, 1.5f };
                foreach (float speedMultiplier in speedMultipliers)
                {
                    Main.projectile = Enumerable.Range(0, previousProjectiles.Length)
                        .Select(_ => new Projectile()).ToArray();
                    actor.GetAttackSpeed(DamageClass.Melee) = speedMultiplier;
                    actor.itemTime = 0;
                    actor.itemAnimation = 0;
                    actor.controlUseItem = true;
                    actor.releaseUseItem = false;
                    actor.position = new Vector2(250f, 120f) + Main.screenPosition;
                    actor.itemRotation = aim.Angle;
                    Vector2 screenCenter = actor.position - Main.screenPosition +
                        new Vector2(actor.width * 0.5f, actor.height * 0.5f);
                    Vector2 mouseTarget = screenCenter +
                        new Vector2((float)Math.Cos(aim.Angle), (float)Math.Sin(aim.Angle)) * 150f;
                    Main.mouseX = (int)mouseTarget.X;
                    Main.mouseY = (int)mouseTarget.Y;
                    int lastWaveCount = 0;
                    var starts = new System.Collections.Generic.List<(int Tick, int Animation)>();
                    int tick = 0;
                    while (tick < 240 && starts.Count < 2)
                    {
                        // ItemCheck advances the item's use clocks itself in
                        // this tModLoader build; do not decrement them twice.
                        actor.ItemCheck();
                        Projectile[] waves = Main.projectile.Where(projectile => projectile.active &&
                            projectile.type == waveType && projectile.owner == localPlayerIndex).ToArray();
                        if (waves.Length > lastWaveCount)
                        {
                            Require(waves.Length == lastWaveCount + 1,
                                $"KnightSword {aim.Name} speed={speedMultiplier} spawned {waves.Length - lastWaveCount} waves on one ItemCheck tick");
                            starts.Add((tick, actor.itemAnimation));
                            Vector2 expectedVelocity = new Vector2((float)Math.Cos(aim.Angle), (float)Math.Sin(aim.Angle));
                            Vector2 actualVelocity = waves[^1].velocity.SafeNormalize(Vector2.Zero);
                            Require(Vector2.Dot(expectedVelocity, actualVelocity) > 0.75f,
                                $"KnightSword {aim.Name} wave velocity {waves[^1].velocity} does not follow aim {aim.Angle}");
                            if (speedCase == 0)
                            {
                                waves[^1].AI();
                                _knightWavePreview.Add((aim.Name, waves[^1]));
                                if (_weaponDiagnosticsEnabled && !_weaponDiagnosticProjectiles.Any(projectile => projectile.type == waveType))
                                    _weaponDiagnosticProjectiles.Add(waves[^1]);
                            }
                            lastWaveCount = waves.Length;
                        }
                        tick++;
                    }
                    Require(starts.Count == 2,
                        $"KnightSword {aim.Name} at {speedMultiplier:0.0}x melee speed did not auto-reuse twice in {tick} ItemCheck ticks");
                    int minCycle = Math.Max(1, starts[0].Animation - 1);
                    Require(starts[1].Tick - starts[0].Tick >= minCycle,
                        $"KnightSword {aim.Name} emitted swings only {starts[1].Tick - starts[0].Tick} ticks apart; expected a full {starts[0].Animation}-tick animation");
                    var last = Main.projectile.Last(projectile => projectile.active &&
                        projectile.type == waveType && projectile.owner == localPlayerIndex);
                    Require(last.DamageType == DamageClass.Melee && last.damage > 0,
                        "KnightSword wave must retain positive melee damage");
                    File.AppendAllText(log,
                        $"case={aim.Name} speed={actor.GetAttackSpeed(DamageClass.Melee):0.0}x itemCheckTicks={tick} starts=" +
                        $"{starts[0].Tick}:{starts[0].Animation},{starts[1].Tick}:{starts[1].Animation} " +
                        $"activeWaves={lastWaveCount} damage={last.damage} damageClass={last.DamageType}\n");
                    speedCase++;
                }
            }
            RenderKnightWavePreviews();
        }
        finally
        {
            NativePoseLabel = "";
            Main.player[localPlayerIndex] = previousLocalPlayer;
            Main.projectile = previousProjectiles;
            ForcePreviewLighting = previousLighting;
            Main.gameMenu = previousMenu;
            Main.UIScale = previousUiScale;
            Main.GameViewMatrix.Zoom = previousGameZoom;
            Main.mouseX = previousMouseX;
            Main.mouseY = previousMouseY;
        }
    }

    private void TestTier3T4SwordWaves()
    {
        int localPlayerIndex = Math.Clamp(Main.myPlayer, 0, Main.player.Length - 1);
        Player previousLocalPlayer = Main.player[localPlayerIndex];
        Projectile[] previousProjectiles = Main.projectile;
        bool previousMenu = Main.gameMenu;
        float previousUiScale = Main.UIScale;
        Vector2 previousGameZoom = Main.GameViewMatrix.Zoom;
        int previousMouseX = Main.mouseX;
        int previousMouseY = Main.mouseY;
        string log = Path.Combine(OutputDirectory, "t3-t4-sword-wave-cycles.log");
        File.WriteAllText(log,
            "Native ItemCheck cycles for new T3/T4 swords. Each tested autoReuse swing must emit exactly one tier-specific melee wave.\n");
        var weapons = new[]
        {
            (Name: "TemplarSword", Item: ModContent.ItemType<TemplarSword>(), Wave: ModContent.ProjectileType<TemplarWave>(),
                Head: ModContent.ItemType<TemplarHelmet>(), Body: ModContent.ItemType<TemplarChestplate>(), Legs: ModContent.ItemType<TemplarGreaves>()),
            (Name: "SoulforgedSword", Item: ModContent.ItemType<SoulforgedSword>(), Wave: ModContent.ProjectileType<SoulforgedWave>(),
                Head: ModContent.ItemType<SoulforgedHelmet>(), Body: ModContent.ItemType<SoulforgedChestplate>(), Legs: ModContent.ItemType<SoulforgedGreaves>())
        };
        try
        {
            Main.gameMenu = false;
            Main.UIScale = 1f;
            Main.GameViewMatrix.Zoom = Vector2.One;
            foreach (var weapon in weapons)
            foreach (var aim in new[] { (Name: "right", Direction: 1, Angle: 0f), (Name: "left", Direction: -1, Angle: MathHelper.Pi) })
            foreach (float speed in new[] { 1f, 1.5f })
            {
                Main.projectile = Enumerable.Range(0, previousProjectiles.Length)
                    .Select(_ => new Projectile()).ToArray();
                Player actor = CreateActionPlayer(weapon.Head, weapon.Body, weapon.Legs,
                    weapon.Item, aim.Direction, aim.Angle, male: true, pose: 0);
                actor.whoAmI = localPlayerIndex;
                actor.GetModPlayer<Tier3T4ProgressPlayer>().ApplyNetworkState(true, true, false);
                actor.GetAttackSpeed(DamageClass.Melee) = speed;
                actor.position = new Vector2(250f, 120f) + Main.screenPosition;
                actor.controlUseItem = true;
                actor.releaseUseItem = false;
                Main.player[localPlayerIndex] = actor;
                Vector2 center = actor.position - Main.screenPosition + new Vector2(actor.width * 0.5f, actor.height * 0.5f);
                Vector2 mouse = center + new Vector2((float)Math.Cos(aim.Angle), (float)Math.Sin(aim.Angle)) * 150f;
                Main.mouseX = (int)mouse.X;
                Main.mouseY = (int)mouse.Y;

                var starts = new System.Collections.Generic.List<(int Tick, int Animation)>();
                int lastCount = 0;
                int tick = 0;
                while (tick < 240 && starts.Count < 2)
                {
                    actor.ItemCheck();
                    Projectile[] waves = Main.projectile.Where(projectile => projectile.active &&
                        projectile.type == weapon.Wave && projectile.owner == localPlayerIndex).ToArray();
                    if (waves.Length > lastCount)
                    {
                        Require(waves.Length == lastCount + 1,
                            $"{weapon.Name} {aim.Name} at {speed:0.0}x emitted {waves.Length - lastCount} waves for one swing");
                        starts.Add((tick, actor.itemAnimation));
                        Vector2 expected = new((float)Math.Cos(aim.Angle), (float)Math.Sin(aim.Angle));
                        Require(Vector2.Dot(expected, waves[^1].velocity.SafeNormalize(Vector2.Zero)) > 0.75f,
                            $"{weapon.Name} wave velocity does not follow {aim.Name} aim");
                        Require(waves[^1].DamageType == DamageClass.Melee && waves[^1].damage > 0,
                            $"{weapon.Name} wave must retain positive melee damage");
                        if (_weaponDiagnosticsEnabled)
                        {
                            waves[^1].AI(); // Exercise the real ModProjectile lighting/target path once per spawn.
                            if (!_weaponDiagnosticProjectiles.Any(projectile => projectile.type == weapon.Wave))
                                _weaponDiagnosticProjectiles.Add(waves[^1]);
                        }
                        lastCount = waves.Length;
                    }
                    tick++;
                }
                Require(starts.Count == 2,
                    $"{weapon.Name} {aim.Name} at {speed:0.0}x did not complete two autoReuse swings");
                Require(starts[1].Tick - starts[0].Tick >= Math.Max(1, starts[0].Animation - 1),
                    $"{weapon.Name} emitted another wave before the first full swing cycle ended");
                File.AppendAllText(log,
                    $"weapon={weapon.Name} aim={aim.Name} meleeSpeed={speed:0.0}x ticks={tick} " +
                    $"swingStarts={starts[0].Tick}:{starts[0].Animation},{starts[1].Tick}:{starts[1].Animation} " +
                    $"waves={lastCount} type={weapon.Wave}\n");
            }
        }
        finally
        {
            Main.player[localPlayerIndex] = previousLocalPlayer;
            Main.projectile = previousProjectiles;
            Main.gameMenu = previousMenu;
            Main.UIScale = previousUiScale;
            Main.GameViewMatrix.Zoom = previousGameZoom;
            Main.mouseX = previousMouseX;
            Main.mouseY = previousMouseY;
        }
    }

    private readonly System.Collections.Generic.List<(string Name, Projectile Projectile)> _knightWavePreview = new();

    private void RenderKnightWavePreviews()
    {
        GraphicsDevice device = Main.graphics.GraphicsDevice;
        const int width = 460;
        const int height = 150;
        const int scale = 3;
        var directions = new (string Name, Vector2 Velocity)[]
        {
            ("right", Vector2.UnitX), ("left", -Vector2.UnitX),
            ("up", -Vector2.UnitY), ("down", Vector2.UnitY)
        };
        using var target = new RenderTarget2D(device, width, height, false,
            SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
        device.SetRenderTarget(target);
        device.Clear(new Color(28, 31, 39));
        Main.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp,
            DepthStencilState.None, RasterizerState.CullCounterClockwise);
        int waveType = ModContent.ProjectileType<KnightSwordWave>();
        for (int i = 0; i < directions.Length; i++)
        {
            var direction = directions[i];
            Projectile projectile = _knightWavePreview.FirstOrDefault(entry => entry.Name == direction.Name).Projectile;
            Require(projectile is not null,
                $"no real KnightSwordWave projectile captured for {direction.Name} preview");
            projectile.velocity = direction.Velocity * 9f;
            projectile.rotation = projectile.velocity.ToRotation();
            projectile.Center = new Vector2(56 + i * 112, 70);
            Texture2D texture = TextureAssets.Projectile[waveType].Value;
            Vector2 drawPosition = projectile.Center;
            Main.spriteBatch.Draw(texture, drawPosition, null, Color.White, projectile.rotation,
                texture.Size() * 0.5f, projectile.scale, SpriteEffects.None, 0f);
            File.AppendAllText(Path.Combine(OutputDirectory, "knight-sword-wave.log"),
                $"render-direction={direction.Name} entity={projectile.type} texture={texture.Width}x{texture.Height} " +
                $"velocity={projectile.velocity} rotation={projectile.rotation} center={projectile.Center}\n");
        }
        Main.spriteBatch.End();
        using var enlarged = new RenderTarget2D(device, width * scale, height * scale, false,
            SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
        device.SetRenderTarget(enlarged);
        device.Clear(Color.Black);
        Main.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp,
            DepthStencilState.None, RasterizerState.CullCounterClockwise);
        Main.spriteBatch.Draw(target, new Rectangle(0, 0, width * scale, height * scale), Color.White);
        Main.spriteBatch.End();
        device.SetRenderTarget(null);
        using var stream = File.Create(Path.Combine(OutputDirectory, "native-knight-sword-wave.png"));
        enlarged.SaveAsPng(stream, width * scale, height * scale);
        _knightWavePreview.Clear();
    }

    private void RenderSwordGripPreview()
    {
        GraphicsDevice device = Main.graphics.GraphicsDevice;
        const int width = 620;
        const int height = 450;
        const int scale = 2;
        int renderWidth = Math.Max(width, Main.screenWidth);
        int renderHeight = Math.Max(height, Main.screenHeight);
        using var fullTarget = new RenderTarget2D(device, renderWidth, renderHeight, false,
            SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
        device.SetRenderTarget(fullTarget);
        device.Clear(new Color(28, 31, 39));
        Main.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp,
            DepthStencilState.None, RasterizerState.CullCounterClockwise);
        int localPlayerIndex = Math.Clamp(Main.myPlayer, 0, Main.player.Length - 1);
        Player previousLocalPlayer = Main.player[localPlayerIndex];
        bool previousMenu = Main.gameMenu;
        bool previousLighting = ForcePreviewLighting;
        float previousUiScale = Main.UIScale;
        Vector2 previousGameZoom = Main.GameViewMatrix.Zoom;
        int previousMouseX = Main.mouseX;
        int previousMouseY = Main.mouseY;
        Projectile[] previousProjectiles = Main.projectile;
        int previousHeldCount = NativeHeldDrawCount;
        int expected = 0;
        try
        {
            Main.gameMenu = false;
            ForcePreviewLighting = true;
            Main.UIScale = 1f;
            Main.GameViewMatrix.Zoom = Vector2.One;
            Main.projectile = (Projectile[])previousProjectiles.Clone();
            NativeHeldDrawCount = 0;
            File.WriteAllText(Path.Combine(OutputDirectory, "native-sword-axis-ab.log"),
                "Native KnightSword ItemCheck/PlayerFrame/PlayerRenderer; same actor and source texture rendered baseline vs longitudinal blade-axis reflection. Rows: first/middle/last swing frame, right then left.\n" +
                "Columns: baseline at x=150, axis-reflected at x=430.\n");
            var directions = new (string Name, int Direction, float Angle)[]
            {
                ("right", 1, 0f), ("left", -1, MathHelper.Pi)
            };
            var animationFrames = new (string Name, int Tick)[]
            {
                ("first", 0), ("middle", 11), ("last", 22)
            };
            foreach (var direction in directions)
            foreach (var frame in animationFrames)
            {
                NativePoseLabel = $"knight-axis/{direction.Name}/{frame.Name}/baseline";
                Player actor = CreateActionPlayer(ModContent.ItemType<KnightHelmet>(),
                    ModContent.ItemType<KnightChestplate>(), ModContent.ItemType<KnightGreaves>(),
                    ModContent.ItemType<KnightSword>(), direction.Direction, direction.Angle, male: true, pose: 0);
                actor.whoAmI = localPlayerIndex;
                actor.itemAnimation = 0;
                actor.itemTime = 0;
                actor.controlUseItem = true;
                actor.releaseUseItem = false;
                actor.position = new Vector2(80f, 80f) + Main.screenPosition;
                Vector2 screenCenter = actor.position - Main.screenPosition +
                    new Vector2(actor.width * 0.5f, actor.height * 0.5f);
                Vector2 aimPosition = screenCenter + new Vector2((float)Math.Cos(direction.Angle),
                    (float)Math.Sin(direction.Angle)) * 150f;
                Main.mouseX = (int)aimPosition.X;
                Main.mouseY = (int)aimPosition.Y;
                Main.player[localPlayerIndex] = actor;
                actor.ItemCheck();
                actor.PlayerFrame();
                for (int tick = 0; tick <= frame.Tick; tick++)
                {
                    if (tick > 0)
                        actor.ItemCheck();
                    actor.PlayerFrame();
                }
                int y = 38 + (direction.Direction < 0 ? 225 : 0) +
                    (frame.Name == "first" ? 0 : frame.Name == "middle" ? 66 : 132);
                NativePoseLabel = $"knight-axis/{direction.Name}/{frame.Name}/baseline";
                UseOriginalKnightSwordDrawForPreview = true;
                DrawPreviewPlayer(actor, 150, y, 1f);
                expected++;
                NativePoseLabel = $"knight-axis/{direction.Name}/{frame.Name}/axis-reflected";
                UseOriginalKnightSwordDrawForPreview = false;
                DrawPreviewPlayer(actor, 430, y, 1f);
                expected++;
                File.AppendAllText(Path.Combine(OutputDirectory, "native-sword-axis-ab.log"),
                    $"case={direction.Name}/{frame.Name} tick={frame.Tick} animation={actor.itemAnimation} " +
                    $"itemRotation={actor.itemRotation} direction={actor.direction} itemLocation={actor.itemLocation}\n");
            }
            Require(NativeHeldDrawCount == expected,
                $"expected twelve native KnightSword draws, got {NativeHeldDrawCount}");
            File.AppendAllText(Path.Combine(OutputDirectory, "native-sword-axis-ab.log"),
                $"native-held-draws={NativeHeldDrawCount}/{expected}\n");
        }
        finally
        {
            UseGripOriginForPreview = false;
            UseOriginalKnightSwordDrawForPreview = false;
            NativePoseLabel = "";
            NativeHeldDrawCount = previousHeldCount;
            Main.player[localPlayerIndex] = previousLocalPlayer;
            Main.projectile = previousProjectiles;
            ForcePreviewLighting = previousLighting;
            Main.gameMenu = previousMenu;
            Main.UIScale = previousUiScale;
            Main.GameViewMatrix.Zoom = previousGameZoom;
            Main.mouseX = previousMouseX;
            Main.mouseY = previousMouseY;
            Main.spriteBatch.End();
        }

        using var target = new RenderTarget2D(device, width, height, false, SurfaceFormat.Color,
            DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
        device.SetRenderTarget(target);
        device.Clear(new Color(28, 31, 39));
        Main.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp,
            DepthStencilState.None, RasterizerState.CullCounterClockwise);
        Main.spriteBatch.Draw(fullTarget, new Rectangle(0, 0, width, height), new Rectangle(0, 0, width, height), Color.White);
        Main.spriteBatch.End();
        using var enlarged = new RenderTarget2D(device, width * scale, height * scale, false,
            SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
        device.SetRenderTarget(enlarged);
        device.Clear(Color.Black);
        Main.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp,
            DepthStencilState.None, RasterizerState.CullCounterClockwise);
        Main.spriteBatch.Draw(target, new Rectangle(0, 0, width * scale, height * scale), Color.White);
        Main.spriteBatch.End();
        device.SetRenderTarget(null);
        using var stream = File.Create(Path.Combine(OutputDirectory, "native-knight-sword-axis-ab.png"));
        enlarged.SaveAsPng(stream, width * scale, height * scale);
    }

    /// <summary>
    /// Controlled native PlayerRenderer A/B for the two mage body slots.
    /// The same staff, pose, actor setup, and source sheets are rendered with
    /// only HidesHands toggled. The static armor flag is restored in finally.
    /// </summary>
    private void RenderMageHandsFlagAB()
    {
        GraphicsDevice device = Main.graphics.GraphicsDevice;
        const int width = 640;
        const int height = 360;
        const int scale = 3;
        int renderWidth = Math.Max(width, Main.screenWidth);
        int renderHeight = Math.Max(height, Main.screenHeight);
        using var fullTarget = new RenderTarget2D(device, renderWidth, renderHeight, false,
            SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
        device.SetRenderTarget(fullTarget);
        device.Clear(new Color(28, 31, 39));
        Main.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp,
            DepthStencilState.None, RasterizerState.CullCounterClockwise);

        int localPlayerIndex = Math.Clamp(Main.myPlayer, 0, Main.player.Length - 1);
        Player previousLocalPlayer = Main.player[localPlayerIndex];
        bool previousMenu = Main.gameMenu;
        bool previousLighting = ForcePreviewLighting;
        int previousMouseX = Main.mouseX;
        int previousMouseY = Main.mouseY;
        float previousUiScale = Main.UIScale;
        Vector2 previousGameZoom = Main.GameViewMatrix.Zoom;
        Projectile[] previousProjectiles = Main.projectile;
        int previousHeldCount = NativeHeldDrawCount;
        int expectedDraws = 0;
        try
        {
            Main.gameMenu = false;
            ForcePreviewLighting = true;
            Main.UIScale = 1f;
            Main.GameViewMatrix.Zoom = Vector2.One;
            Main.projectile = (Projectile[])previousProjectiles.Clone();
            NativeHeldDrawCount = 0;
            File.WriteAllText(Path.Combine(OutputDirectory, "native-mage-hands-ab.log"),
                "Native PlayerRenderer ItemCheck A/B. Identical actor/staff/pose; compare current grip hook with vanilla position plus explicit texture grip origin. " +
                "The origin candidate is smoke-only and restored after each draw.\n");

            var suits = new (string Name, int Head, int Body, int Legs, int Staff)[]
            {
                ("cloth", ModContent.ItemType<ClothHood>(), ModContent.ItemType<ClothRobe>(),
                    ModContent.ItemType<ClothLeggings>(), ModContent.ItemType<IronStaff>()),
                ("energy", ModContent.ItemType<EnergyHood>(), ModContent.ItemType<EnergyRobe>(),
                    ModContent.ItemType<EnergyLeggings>(), ModContent.ItemType<EnergyStaff>())
            };
            for (int row = 0; row < suits.Length; row++)
            {
                var suit = suits[row];
                float[] angles = { 0f, -MathHelper.PiOver4 };
                for (int poseIndex = 0; poseIndex < angles.Length; poseIndex++)
                for (int originIndex = 0; originIndex < 2; originIndex++)
                {
                    bool useGripOrigin = originIndex == 1;
                    UseGripOriginForPreview = useGripOrigin;
                    string label = $"{suit.Name}/{(poseIndex == 0 ? "horizontal" : "raised")}/gripOrigin-{useGripOrigin}";
                    NativePoseLabel = label;
                    File.AppendAllText(Path.Combine(OutputDirectory, "native-mage-hands-ab.log"),
                        $"case={label}\n");
                    Player actor = CreateActionPlayer(suit.Head, suit.Body, suit.Legs, suit.Staff,
                        direction: 1, aimAngle: angles[poseIndex], male: true, pose: 0);
                    actor.whoAmI = localPlayerIndex;
                    Main.player[localPlayerIndex] = actor;
                    int x = 100 + poseIndex * 220 + originIndex * 90;
                    int y = row == 0 ? 62 : 182;
                    DrawNativeItemCheckPreview(actor, x, y, 1f, angles[poseIndex]);
                    expectedDraws++;
                    UseGripOriginForPreview = false;
                }
            }
            // Engine baseline: run the identical synthetic use path with
            // vanilla jungle armor and Ruby Staff. If this also lacks an
            // extended arm, the remaining limitation is the isolated use
            // fixture rather than the Milkcraft body texture.
            foreach ((string Name, float Angle, int X) pose in new[]
            {
                ("horizontal", 0f, 200),
                ("raised", -MathHelper.PiOver4, 440)
            })
            {
                NativePoseLabel = $"vanilla-jungle/{pose.Name}";
                Player actor = CreateActionPlayer(ItemID.JungleHat, ItemID.JungleShirt,
                    ItemID.JunglePants, ItemID.RubyStaff, direction: 1,
                    aimAngle: pose.Angle, male: true, pose: 0);
                actor.whoAmI = localPlayerIndex;
                Main.player[localPlayerIndex] = actor;
                DrawNativeItemCheckPreview(actor, pose.X, 230, 1f, pose.Angle);
                expectedDraws++;
            }
            Require(NativeHeldDrawCount == expectedDraws,
                $"mage hands A/B expected {expectedDraws} native staff draws, got {NativeHeldDrawCount}");
            File.AppendAllText(Path.Combine(OutputDirectory, "native-mage-hands-ab.log"),
                $"native-held-draws={NativeHeldDrawCount}/{expectedDraws}; all requested A/B actors queued native held items.\n");
        }
        finally
        {
            NativePoseLabel = "";
            UseGripOriginForPreview = false;
            NativeHeldDrawCount = previousHeldCount;
            Main.projectile = previousProjectiles;
            Main.player[localPlayerIndex] = previousLocalPlayer;
            ForcePreviewLighting = previousLighting;
            Main.gameMenu = previousMenu;
            Main.UIScale = previousUiScale;
            Main.GameViewMatrix.Zoom = previousGameZoom;
            Main.mouseX = previousMouseX;
            Main.mouseY = previousMouseY;
            Main.spriteBatch.End();
        }

        using var target = new RenderTarget2D(device, width, height, false, SurfaceFormat.Color,
            DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
        device.SetRenderTarget(target);
        device.Clear(new Color(28, 31, 39));
        Main.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp,
            DepthStencilState.None, RasterizerState.CullCounterClockwise);
        Main.spriteBatch.Draw(fullTarget, new Rectangle(0, 0, width, height), new Rectangle(0, 0, width, height), Color.White);
        Main.spriteBatch.End();
        using var enlarged = new RenderTarget2D(device, width * scale, height * scale, false,
            SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
        device.SetRenderTarget(enlarged);
        device.Clear(Color.Black);
        Main.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp,
            DepthStencilState.None, RasterizerState.CullCounterClockwise);
        Main.spriteBatch.Draw(target, new Rectangle(0, 0, width * scale, height * scale), Color.White);
        Main.spriteBatch.End();
        device.SetRenderTarget(null);
        using var stream = File.Create(Path.Combine(OutputDirectory, "native-mage-hands-ab.png"));
        enlarged.SaveAsPng(stream, width * scale, height * scale);
    }

    private void RenderWeaponPreviewPage(string fileName, bool bows)
    {
        GraphicsDevice device = Main.graphics.GraphicsDevice;
        const int width = 900;
        const int height = 360;
        const int scale = 2;
        int renderWidth = Math.Max(width, Main.screenWidth);
        int renderHeight = Math.Max(height, Main.screenHeight);
        using var fullTarget = new RenderTarget2D(device, renderWidth, renderHeight, false,
            SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
        device.SetRenderTarget(fullTarget);
        device.Clear(new Color(28, 31, 39));
        Main.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp,
            DepthStencilState.None, RasterizerState.CullCounterClockwise);
        int localPlayerIndex = Math.Clamp(Main.myPlayer, 0, Main.player.Length - 1);
        Player previousLocalPlayer = Main.player[localPlayerIndex];
        bool previousMenu = Main.gameMenu;
        bool previousLighting = ForcePreviewLighting;
        int previousMouseX = Main.mouseX;
        int previousMouseY = Main.mouseY;
        float previousUiScale = Main.UIScale;
        Vector2 previousGameZoom = Main.GameViewMatrix.Zoom;
        Projectile[] previousProjectiles = Main.projectile;
        try
        {
            Main.gameMenu = false;
            ForcePreviewLighting = true;
            Main.UIScale = 1f;
            Main.GameViewMatrix.Zoom = Vector2.One;
            // Any projectile caused by a one-frame ItemCheck belongs to this
            // disposable renderer fixture. Restore the untouched game array.
            Main.projectile = (Projectile[])previousProjectiles.Clone();
            NativeHeldDrawCount = 0;
            NativeBowDrawCount = 0;
            File.AppendAllText(Path.Combine(OutputDirectory, "native-held-layer.log"),
                $"\n{(bows ? "bow" : "sword")}-orientation-preview: native PlayerRenderer; no texture overlay.\n");
            int[] weapons = bows
                ? new[] { ModContent.ItemType<IronBow>(), ModContent.ItemType<HuntingBow>() }
                : new[] { ModContent.ItemType<IronSword>(), ModContent.ItemType<KnightSword>() };
            for (int i = 0; i < weapons.Length; i++)
            {
                int head = bows ? ModContent.ItemType<LeatherHeadband>() : ModContent.ItemType<IronHelmet>();
                int body = bows ? ModContent.ItemType<LeatherTunic>() : ModContent.ItemType<IronChestplate>();
                int legs = bows ? ModContent.ItemType<LeatherLeggings>() : ModContent.ItemType<IronGreaves>();
                for (int directionIndex = 0; directionIndex < 2; directionIndex++)
                {
                    int direction = directionIndex == 0 ? 1 : -1;
                    Player actor = CreateActionPlayer(head, body, legs, weapons[i], direction,
                        0f, male: true, pose: 0);
                    actor.whoAmI = localPlayerIndex;
                    Main.player[localPlayerIndex] = actor;
                    DrawNativeItemCheckPreview(actor, directionIndex == 0 ? 235 : 665,
                        i == 0 ? 56 : 160, 1f);
                }
            }
            Require(NativeHeldDrawCount >= 4,
                $"expected four native held draws, found {NativeHeldDrawCount}");
            if (bows)
                Require(NativeBowDrawCount == 4,
                    $"expected four native bow ModifyItemDraw hooks, found {NativeBowDrawCount}");
        }
        finally
        {
            Main.player[localPlayerIndex] = previousLocalPlayer;
            Main.projectile = previousProjectiles;
            ForcePreviewLighting = previousLighting;
            Main.gameMenu = previousMenu;
            Main.UIScale = previousUiScale;
            Main.GameViewMatrix.Zoom = previousGameZoom;
            Main.mouseX = previousMouseX;
            Main.mouseY = previousMouseY;
            Main.spriteBatch.End();
        }

        using var target = new RenderTarget2D(device, width, height, false, SurfaceFormat.Color,
            DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
        device.SetRenderTarget(target);
        device.Clear(new Color(28, 31, 39));
        Main.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp,
            DepthStencilState.None, RasterizerState.CullCounterClockwise);
        Main.spriteBatch.Draw(fullTarget, new Rectangle(0, 0, width, height), new Rectangle(0, 0, width, height), Color.White);
        Main.spriteBatch.End();
        using var enlarged = new RenderTarget2D(device, width * scale, height * scale, false,
            SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
        device.SetRenderTarget(enlarged);
        device.Clear(Color.Black);
        Main.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp,
            DepthStencilState.None, RasterizerState.CullCounterClockwise);
        Main.spriteBatch.Draw(target, new Rectangle(0, 0, width * scale, height * scale), Color.White);
        Main.spriteBatch.End();
        device.SetRenderTarget(null);
        using var stream = File.Create(Path.Combine(OutputDirectory, fileName));
        enlarged.SaveAsPng(stream, width * scale, height * scale);
    }

    private void RenderTier3T4WeaponPreviews()
    {
        RenderTier3T4BowDirections();
        RenderTier3T4SwordSwingFrames();
    }

    private void RenderTier3T4BowDirections()
    {
        GraphicsDevice device = Main.graphics.GraphicsDevice;
        const int width = 800;
        const int height = 450;
        const int scale = 2;
        int renderWidth = Main.screenWidth;
        int renderHeight = Main.screenHeight;
        using var fullTarget = new RenderTarget2D(device, renderWidth, renderHeight, false,
            SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
        device.SetRenderTarget(fullTarget);
        device.Clear(new Color(28, 31, 39));
        Main.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp,
            DepthStencilState.None, RasterizerState.CullCounterClockwise);
        int localPlayerIndex = Math.Clamp(Main.myPlayer, 0, Main.player.Length - 1);
        Player previousPlayer = Main.player[localPlayerIndex];
        Projectile[] previousProjectiles = Main.projectile;
        bool previousMenu = Main.gameMenu;
        bool previousLighting = ForcePreviewLighting;
        float previousUiScale = Main.UIScale;
        Vector2 previousZoom = Main.GameViewMatrix.Zoom;
        int previousMouseX = Main.mouseX;
        int previousMouseY = Main.mouseY;
        try
        {
            Main.gameMenu = false;
            ForcePreviewLighting = true;
            Main.UIScale = 1f;
            Main.GameViewMatrix.Zoom = Vector2.One;
            Main.projectile = (Projectile[])previousProjectiles.Clone();
            NativeHeldDrawCount = 0;
            NativeBowDrawCount = 0;
            var cases = new[]
            {
                (Name: "Iron/right", Item: ModContent.ItemType<IronBow>(), Head: ModContent.ItemType<IronHelmet>(), Body: ModContent.ItemType<IronChestplate>(), Legs: ModContent.ItemType<IronGreaves>(), Dir: 1, X: 90, Row: 0),
                (Name: "Iron/left", Item: ModContent.ItemType<IronBow>(), Head: ModContent.ItemType<IronHelmet>(), Body: ModContent.ItemType<IronChestplate>(), Legs: ModContent.ItemType<IronGreaves>(), Dir: -1, X: 290, Row: 0),
                (Name: "Hunting/right", Item: ModContent.ItemType<HuntingBow>(), Head: ModContent.ItemType<HuntingHood>(), Body: ModContent.ItemType<HuntingChestplate>(), Legs: ModContent.ItemType<HuntingLeggings>(), Dir: 1, X: 490, Row: 0),
                (Name: "Hunting/left", Item: ModContent.ItemType<HuntingBow>(), Head: ModContent.ItemType<HuntingHood>(), Body: ModContent.ItemType<HuntingChestplate>(), Legs: ModContent.ItemType<HuntingLeggings>(), Dir: -1, X: 690, Row: 0),
                (Name: "Nightmare/right", Item: ModContent.ItemType<NightmareBow>(), Head: ModContent.ItemType<NightmareHood>(), Body: ModContent.ItemType<NightmareChestplate>(), Legs: ModContent.ItemType<NightmareLeggings>(), Dir: 1, X: 90, Row: 1),
                (Name: "Nightmare/left", Item: ModContent.ItemType<NightmareBow>(), Head: ModContent.ItemType<NightmareHood>(), Body: ModContent.ItemType<NightmareChestplate>(), Legs: ModContent.ItemType<NightmareLeggings>(), Dir: -1, X: 290, Row: 1),
                (Name: "Abyss/right", Item: ModContent.ItemType<AbyssBow>(), Head: ModContent.ItemType<AbyssHood>(), Body: ModContent.ItemType<AbyssChestplate>(), Legs: ModContent.ItemType<AbyssLeggings>(), Dir: 1, X: 490, Row: 1),
                (Name: "Abyss/left", Item: ModContent.ItemType<AbyssBow>(), Head: ModContent.ItemType<AbyssHood>(), Body: ModContent.ItemType<AbyssChestplate>(), Legs: ModContent.ItemType<AbyssLeggings>(), Dir: -1, X: 690, Row: 1)
            };
            File.WriteAllText(Path.Combine(OutputDirectory, "native-four-tier-bow-directions.log"),
                "Native ItemCheck/PlayerFrame/PlayerRenderer bow use plus actual arrow projectile, male reference model; all four tiers and directions.\n");
            foreach (var spec in cases)
            {
                Main.projectile = Enumerable.Range(0, previousProjectiles.Length).Select(_ => new Projectile()).ToArray();
                float angle = spec.Dir > 0 ? 0f : MathHelper.Pi;
                Player actor = CreateActionPlayer(spec.Head, spec.Body, spec.Legs, spec.Item,
                    spec.Dir, angle, male: true, pose: 0);
                actor.whoAmI = localPlayerIndex;
                Main.player[localPlayerIndex] = actor;
                NativePoseLabel = spec.Name;
                DrawNativeItemCheckPreview(actor, spec.X, spec.Row == 0 ? 72 : 292, 1f, angle);
                int arrowType = spec.Item == ModContent.ItemType<IronBow>()
                    ? ProjectileID.WoodenArrowFriendly
                    : ModContent.ProjectileType<HuntingArrow>();
                Projectile[] arrows = Main.projectile.Where(projectile => projectile.active &&
                    projectile.owner == actor.whoAmI && projectile.type == arrowType).ToArray();
                int expectedArrowCount = spec.Item == ModContent.ItemType<AbyssBow>() ? 2 : 1;
                Require(arrows.Length == expectedArrowCount,
                    $"{spec.Name} should spawn {expectedArrowCount} native ammo projectiles in the fixture, found {arrows.Length}");
                Require(arrows.All(projectile => Math.Sign(projectile.velocity.X) == spec.Dir),
                    $"{spec.Name} arrow velocities should follow facing: velocity={string.Join(',', arrows.Select(projectile => projectile.velocity))}");
                for (int arrowIndex = 0; arrowIndex < arrows.Length; arrowIndex++)
                {
                    Vector2 arrowAnchor = new(spec.X + spec.Dir * 52f,
                        (spec.Row == 0 ? 125f : 345f) + (arrows.Length == 2 ? (arrowIndex == 0 ? -7f : 7f) : 0f));
                    DrawPreviewArrow(arrows[arrowIndex], arrowAnchor, 1f);
                }
                File.AppendAllText(Path.Combine(OutputDirectory, "native-four-tier-bow-directions.log"),
                    $"case={spec.Name} direction={actor.direction} item={actor.inventory[0].Name} " +
                    $"projectileCount={arrows.Length} types={string.Join(',', arrows.Select(projectile => projectile.type))} " +
                    $"velocities={string.Join(',', arrows.Select(projectile => projectile.velocity))} " +
                    $"rotations={string.Join(',', arrows.Select(projectile => projectile.velocity.ToRotation()))} " +
                    $"projectilePositions={string.Join(',', arrows.Select(projectile => projectile.position))} drawn=true\n");
            }
            Require(NativeHeldDrawCount == 8 && NativeBowDrawCount == 8,
                $"expected 8 native T1-T4 bow draws and flip hooks; got held={NativeHeldDrawCount} flip={NativeBowDrawCount}");
        }
        finally
        {
            NativePoseLabel = "";
            Main.player[localPlayerIndex] = previousPlayer;
            Main.projectile = previousProjectiles;
            ForcePreviewLighting = previousLighting;
            Main.gameMenu = previousMenu;
            Main.UIScale = previousUiScale;
            Main.GameViewMatrix.Zoom = previousZoom;
            Main.mouseX = previousMouseX;
            Main.mouseY = previousMouseY;
            Main.spriteBatch.End();
        }
        SavePreviewCrop(fullTarget, width, height, scale, null,
            "native-four-tier-bow-directions.png");
    }

    private static void DrawPreviewArrow(Projectile arrow, Vector2 screenAnchor, float scale = 1.5f)
    {
        Texture2D texture = TextureAssets.Projectile[arrow.type].Value;
        Rectangle frame = new Rectangle(0, 0, texture.Width, texture.Height);
        // The WoodenArrow texture points upward at zero rotation; the native
        // projectile convention adds Pi/2 to align it to horizontal velocity.
        float nativeRotation = arrow.velocity.ToRotation() + MathHelper.PiOver2;
        Main.spriteBatch.Draw(texture, screenAnchor, frame, Color.White,
            nativeRotation, new Vector2(frame.Width * 0.5f, frame.Height * 0.5f),
            scale, SpriteEffects.None, 0f);
    }

    private void RenderTier3T4SwordSwingFrames()
    {
        GraphicsDevice device = Main.graphics.GraphicsDevice;
        // PlayerRenderer uses the game's camera/viewport projection. Render
        // to a target with the real screen dimensions, then copy an equal-size
        // source rectangle so players and held weapons keep their native ratio.
        int width = Main.screenWidth;
        int height = Main.screenHeight;
        const int scale = 2;
        int renderWidth = width;
        int renderHeight = height;
        using var fullTarget = new RenderTarget2D(device, renderWidth, renderHeight, false,
            SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
        device.SetRenderTarget(fullTarget);
        device.Clear(new Color(28, 31, 39));
        Main.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp,
            DepthStencilState.None, RasterizerState.CullCounterClockwise);
        int localPlayerIndex = Math.Clamp(Main.myPlayer, 0, Main.player.Length - 1);
        Player previousPlayer = Main.player[localPlayerIndex];
        Projectile[] previousProjectiles = Main.projectile;
        bool previousMenu = Main.gameMenu;
        bool previousLighting = ForcePreviewLighting;
        float previousUiScale = Main.UIScale;
        Vector2 previousZoom = Main.GameViewMatrix.Zoom;
        int previousMouseX = Main.mouseX;
        int previousMouseY = Main.mouseY;
        try
        {
            Main.gameMenu = false;
            ForcePreviewLighting = true;
            Main.UIScale = 1f;
            Main.GameViewMatrix.Zoom = Vector2.One;
            NativeHeldDrawCount = 0;
            var swords = new[]
            {
                (Name: "Iron", Item: ModContent.ItemType<IronSword>(), Head: ModContent.ItemType<IronHelmet>(), Body: ModContent.ItemType<IronChestplate>(), Legs: ModContent.ItemType<IronGreaves>()),
                (Name: "Knight", Item: ModContent.ItemType<KnightSword>(), Head: ModContent.ItemType<KnightHelmet>(), Body: ModContent.ItemType<KnightChestplate>(), Legs: ModContent.ItemType<KnightGreaves>()),
                (Name: "Templar", Item: ModContent.ItemType<TemplarSword>(), Head: ModContent.ItemType<TemplarHelmet>(), Body: ModContent.ItemType<TemplarChestplate>(), Legs: ModContent.ItemType<TemplarGreaves>()),
                (Name: "Soulforged", Item: ModContent.ItemType<SoulforgedSword>(), Head: ModContent.ItemType<SoulforgedHelmet>(), Body: ModContent.ItemType<SoulforgedChestplate>(), Legs: ModContent.ItemType<SoulforgedGreaves>())
            };
            var directions = new[] { (Name: "right", Value: 1), (Name: "left", Value: -1) };
            var frames = new[] { (Name: "first", Fraction: 0.08f), (Name: "middle", Fraction: 0.50f), (Name: "last", Fraction: 0.90f) };
            File.WriteAllText(Path.Combine(OutputDirectory, "native-four-tier-sword-swing-frames.log"),
                "Native ItemCheck/PlayerFrame/PlayerRenderer sword poses at first, middle and last swing ticks; male reference model; all four tiers, both directions.\n");
            for (int row = 0; row < swords.Length; row++)
            for (int directionIndex = 0; directionIndex < directions.Length; directionIndex++)
            for (int frameIndex = 0; frameIndex < frames.Length; frameIndex++)
            {
                var sword = swords[row];
                var direction = directions[directionIndex];
                var frame = frames[frameIndex];
                int previewX = 80 + (directionIndex * frames.Length + frameIndex) * 112;
                int previewY = 70 + row * 130;
                Main.projectile = Enumerable.Range(0, previousProjectiles.Length).Select(_ => new Projectile()).ToArray();
                float angle = direction.Value > 0 ? 0f : MathHelper.Pi;
                Player actor = CreateActionPlayer(sword.Head, sword.Body, sword.Legs, sword.Item,
                    direction.Value, angle, male: true, pose: 0);
                actor.whoAmI = localPlayerIndex;
                actor.GetModPlayer<Tier3T4ProgressPlayer>().ApplyNetworkState(true, true, false);
                actor.position = new Vector2(previewX, previewY) + Main.screenPosition;
                Main.player[localPlayerIndex] = actor;
                Vector2 center = actor.position - Main.screenPosition + new Vector2(actor.width * 0.5f, actor.height * 0.5f);
                Vector2 target = center + new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle)) * 150f;
                Main.mouseX = (int)target.X;
                Main.mouseY = (int)target.Y;
                actor.itemAnimation = 0;
                actor.itemTime = 0;
                actor.controlUseItem = true;
                actor.releaseUseItem = false;
                int targetTick = Math.Max(1, (int)(actor.inventory[0].useAnimation * frame.Fraction));
                // Enter the native swing once with a clean synthetic actor,
                // then advance only through ItemCheck/PlayerFrame. A leftover
                // dummy item-time can otherwise leave itemRotation infinite.
                actor.ItemCheck();
                actor.PlayerFrame();
                for (int tick = 1; tick <= targetTick; tick++)
                {
                    actor.ItemCheck();
                    actor.PlayerFrame();
                }
                NativePoseLabel = $"{sword.Name}/{direction.Name}/{frame.Name}";
                DrawPreviewPlayer(actor, previewX, previewY, 1f);
                Require(NativeHeldDrawCount > 0,
                    $"{NativePoseLabel} did not queue a native held item");
                Require(float.IsFinite(actor.itemRotation),
                    $"{NativePoseLabel} produced a non-finite native swing rotation: {actor.itemRotation}");
                File.AppendAllText(Path.Combine(OutputDirectory, "native-four-tier-sword-swing-frames.log"),
                    $"case={NativePoseLabel} useTime={actor.inventory[0].useTime} targetTick={targetTick} " +
                    $"itemAnimation={actor.itemAnimation} itemRotation={actor.itemRotation} direction={actor.direction}\n");
            }

            // Same native renderer, same suit and camera, but no held weapon.
            // This gives the reviewer an in-frame scale reference so the long
            // diagonal blades are not mistaken for a stretched player sprite.
            for (int row = 0; row < swords.Length; row++)
            {
                var sword = swords[row];
                Player baseline = CreateActionPlayer(sword.Head, sword.Body, sword.Legs,
                    ItemID.None, direction: 1, aimAngle: 0f, male: true, pose: 0);
                baseline.whoAmI = localPlayerIndex;
                baseline.GetModPlayer<Tier3T4ProgressPlayer>().ApplyNetworkState(true, true, false);
                Main.player[localPlayerIndex] = baseline;
                NativePoseLabel = $"{sword.Name}/unarmed-scale-reference";
                DrawPreviewPlayer(baseline, width - 28, 70 + row * 130, 1f);
                File.AppendAllText(Path.Combine(OutputDirectory, "native-four-tier-sword-swing-frames.log"),
                    $"case={NativePoseLabel} playerScale=1.0 position={baseline.position} bodyFrame={baseline.bodyFrame}\n");
            }
            Require(NativeHeldDrawCount == 24,
                $"expected 24 native T1-T4 sword pose draws, found {NativeHeldDrawCount}");
        }
        finally
        {
            NativePoseLabel = "";
            Main.player[localPlayerIndex] = previousPlayer;
            Main.projectile = previousProjectiles;
            ForcePreviewLighting = previousLighting;
            Main.gameMenu = previousMenu;
            Main.UIScale = previousUiScale;
            Main.GameViewMatrix.Zoom = previousZoom;
            Main.mouseX = previousMouseX;
            Main.mouseY = previousMouseY;
            Main.spriteBatch.End();
        }
        SavePreviewCrop(fullTarget, width, height, scale, null,
            "native-four-tier-sword-swing-frames.png");
    }

    private static void DrawNativeItemCheckPreview(Player player, int x, int y, float scale, float? aimOverride = null)
    {
        Vector2 position = new Vector2(x, y) + Main.screenPosition;
        player.position = position;
        player.itemTime = 0;
        player.itemAnimation = 0;
        player.controlUseItem = true;
        player.releaseUseItem = false;
        player.statMana = 200;
        if (player.inventory[0].useAmmo == AmmoID.Arrow)
        {
            player.inventory[1] = new Item();
            player.inventory[1].SetDefaults(ItemID.WoodenArrow);
            player.inventory[1].stack = 999;
        }

        float aim = aimOverride ?? (player.direction > 0 ? 0f : MathHelper.Pi);
        Vector2 screenCenter = position - Main.screenPosition + new Vector2(player.width * 0.5f, player.height * 0.5f);
        Vector2 target = screenCenter + new Vector2((float)Math.Cos(aim), (float)Math.Sin(aim)) * 150f;
        Main.mouseX = (int)target.X;
        Main.mouseY = (int)target.Y;
        player.ItemCheck();
        // The live Player.Update loop computes use frames after item timing.
        // Run that same native frame function in the isolated renderer actor.
        player.PlayerFrame();
        Texture2D heldTexture = TextureAssets.Item[player.inventory[0].type].Value;
        File.AppendAllText(Path.Combine(
                Environment.GetEnvironmentVariable("MILKCRAFT_TERRARIA_SMOKE_OUT") ?? Environment.CurrentDirectory,
                "native-held-layer.log"),
            $"itemcheck fixture={NativePoseLabel} actor={player.inventory[0].Name} direction={player.direction} anim={player.itemAnimation} " +
            $"itemTime={player.itemTime} itemSize={player.inventory[0].width}x{player.inventory[0].height} " +
            $"textureSize={heldTexture.Width}x{heldTexture.Height} itemLocation={player.itemLocation} itemRotation={player.itemRotation} " +
            $"front={player.compositeFrontArm.enabled}/{player.compositeFrontArm.stretch}/{player.compositeFrontArm.rotation} " +
            $"bodyFrame={player.bodyFrame}\n");
        Main.PlayerRenderer.DrawPlayer(Main.Camera, player, position, 0f,
            new Vector2(player.width / 2f, player.height / 2f), 0f, scale);
    }

    private void RenderAccessoryPreviews()
    {
        GraphicsDevice device = Main.graphics.GraphicsDevice;
        const int width = 960;
        const int height = 480;
        const int scale = 2;
        int renderWidth = Math.Max(width, Main.screenWidth);
        int renderHeight = Math.Max(height, Main.screenHeight);
        using var fullTarget = new RenderTarget2D(device, renderWidth, renderHeight, false,
            SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
        device.SetRenderTarget(fullTarget);
        device.Clear(new Color(28, 31, 39));
        Main.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp,
            DepthStencilState.None, RasterizerState.CullCounterClockwise);

        var entries = new (string Name, int Item, string Texture)[]
        {
            ("iron-shield", ModContent.ItemType<IronShield>(), "IronShield_Shield"),
            ("knight-bulwark", ModContent.ItemType<KnightBulwark>(), "KnightBulwark_Shield"),
            ("iron-quiver", ModContent.ItemType<IronQuiver>(), "IronQuiver_Back"),
            ("hunting-quiver", ModContent.ItemType<HuntingQuiver>(), "HuntingQuiver_Back"),
            ("beginner-tome", ModContent.ItemType<BeginnerTome>(), "BeginnerTome_Waist"),
            ("energy-tome", ModContent.ItemType<EnergyTome>(), "EnergyTome_Waist")
        };
        int localPlayerIndex = Math.Clamp(Main.myPlayer, 0, Main.player.Length - 1);
        Player previousLocalPlayer = Main.player[localPlayerIndex];
        bool previousMenu = Main.gameMenu;
        bool previousLighting = ForcePreviewLighting;
        float previousUiScale = Main.UIScale;
        Vector2 previousGameZoom = Main.GameViewMatrix.Zoom;
        string log = Path.Combine(OutputDirectory, "native-accessory-layers.log");
        File.WriteAllText(log, "Native PlayerRenderer accessory layers; first row visible, second row hideVisibleAccessory[0]=true.\n");
        try
        {
            Main.gameMenu = false;
            ForcePreviewLighting = true;
            Main.UIScale = 1f;
            Main.GameViewMatrix.Zoom = Vector2.One;
            CaptureAccessoryDraws = true;
            for (int i = 0; i < entries.Length; i++)
            {
                var entry = entries[i];
                bool isShield = entry.Texture.Contains("Shield", StringComparison.Ordinal);
                int rowCount = isShield ? 5 : 3;
                for (int row = 0; row < rowCount; row++)
                {
                    Player actor = CreateHeadwearPlayer(0, 1);
                    actor.whoAmI = localPlayerIndex;
                    actor.armor[3] = new Item();
                    actor.armor[3].SetDefaults(entry.Item);
                    bool hidden = row == rowCount - 1;
                    actor.hideVisibleAccessory[0] = hidden;
                    actor.UpdateVisibleAccessories(actor.armor[3], hidden, 0, modded: false);
                    if (row == 1 || (isShield && row == 2))
                    {
                        actor.direction = -1;
                        actor.velocity = new Vector2(-1f, 0f);
                        actor.bodyFrame = new Rectangle(0, 56 * 3, 40, 56);
                        actor.legFrame = new Rectangle(0, 56 * 3, 40, 56);
                    }
                    else if (isShield && row == 3)
                    {
                        // Cobalt's native body frame 5 is the raised/attack
                        // pose used to validate the moving shield anchor.
                        actor.bodyFrame = new Rectangle(0, 56 * 5, 40, 56);
                        actor.legFrame = new Rectangle(0, 56 * 5, 40, 56);
                    }
                    Main.player[localPlayerIndex] = actor;
                    string pose = hidden ? "hidden" : row switch
                    {
                        0 => "right-idle",
                        1 => "left-idle",
                        2 => "left-walk-frame3",
                        3 => "shield-frame5",
                        _ => "pose"
                    };
                    AccessoryCaptureLabel = $"{entry.Name}/{pose}";
                    AccessoryCaptureSearch = entry.Texture;
                    AccessoryCaptureDrawCount = 0;
                    AccessorySourceAlphaPixels = 0;
                    AccessorySourceInsideTexture = false;
                    DrawPreviewPlayer(actor, 60 + i * 120, 28 + row * 88, 1f);
                    int draws = AccessoryCaptureDrawCount;
                    Require(hidden ? draws == 0 : draws > 0,
                        $"{entry.Name} native layer visibility mismatch: hidden={hidden}, matching DrawData={draws}");
                    if (!hidden)
                        Require(AccessorySourceInsideTexture && AccessorySourceAlphaPixels > 0,
                            $"{entry.Name}/{AccessoryCaptureLabel} has invalid/transparent native frame source {AccessorySourceAlphaPixels}px");
                }
            }

            // Capture a vanilla Shield slot and texture for correct native
            // frame/source anchor comparison with the Milkcraft shields.
            Player vanilla = CreateHeadwearPlayer(0, 1);
            vanilla.whoAmI = localPlayerIndex;
            vanilla.armor[3] = new Item();
            vanilla.armor[3].SetDefaults(ItemID.CobaltShield);
            vanilla.hideVisibleAccessory[0] = false;
            vanilla.UpdateVisibleAccessories(vanilla.armor[3], invisible: false, slot: 0, modded: false);
            Main.player[localPlayerIndex] = vanilla;
            AccessoryCaptureLabel = "vanilla-cobalt-shield";
            AccessoryCaptureSearch = "Armor_Shield";
            AccessoryCaptureDrawCount = 0;
            // Cobalt Shield is a functional accessory without a wearable
            // shield texture. Probe vanilla shield accessories and select the
            // first one with a real registered shield slot for the reference.
            int[] vanillaShieldIds = { ItemID.CobaltShield, ItemID.PaladinsShield,
                ItemID.AnkhShield, ItemID.ObsidianShield };
            Item vanillaShield = null;
            foreach (int candidateId in vanillaShieldIds)
            {
                var candidate = new Item();
                candidate.SetDefaults(candidateId);
                File.AppendAllText(log, $"vanilla-shield-candidate type={candidate.type} name={candidate.Name} slot={candidate.shieldSlot}\n");
                if (candidate.shieldSlot >= 0)
                {
                    vanillaShield = candidate;
                    break;
                }
            }
            Require(vanillaShield != null, "no vanilla wearable shield slot found in reference candidates");
            vanilla.armor[3] = vanillaShield;
            vanilla.UpdateVisibleAccessories(vanilla.armor[3], invisible: false, slot: 0, modded: false);
            AccessoryCaptureSearch = "Acc_Shield_1";
            int referenceRow = 0;
            foreach (int frame in new[] { 0, 3, 5 })
            {
                vanilla.bodyFrame = new Rectangle(0, frame * 56, 40, 56);
                vanilla.legFrame = new Rectangle(0, frame * 56, 40, 56);
                vanilla.direction = frame == 3 ? -1 : 1;
                AccessoryCaptureLabel = $"vanilla-{(vanillaShield.type == ItemID.CobaltShield ? "cobalt" : "shield")}-frame{frame}";
                AccessoryCaptureDrawCount = 0;
                AccessorySourceAlphaPixels = 0;
                AccessorySourceInsideTexture = false;
                DrawPreviewPlayer(vanilla, 800, 28 + referenceRow++ * 88, 1f);
                Require(AccessoryCaptureDrawCount > 0 && AccessorySourceInsideTexture && AccessorySourceAlphaPixels > 0,
                    $"vanilla shield reference frame {frame} was not drawn from a valid native source rectangle");
            }

            Item magicQuiver = new();
            magicQuiver.SetDefaults(ItemID.MagicQuiver);
            File.AppendAllText(log, $"vanilla-magic-quiver type={magicQuiver.type} backSlot={magicQuiver.backSlot} waistSlot={magicQuiver.waistSlot}\n");
            if (magicQuiver.backSlot >= 0)
            {
                Player quiverActor = CreateHeadwearPlayer(0, 1);
                quiverActor.whoAmI = localPlayerIndex;
                quiverActor.armor[3] = magicQuiver;
                quiverActor.UpdateVisibleAccessories(magicQuiver, invisible: false, slot: 0, modded: false);
                Main.player[localPlayerIndex] = quiverActor;
                AccessoryCaptureLabel = "vanilla-magic-quiver";
                AccessoryCaptureSearch = "Acc_Back";
                AccessoryCaptureDrawCount = 0;
                DrawPreviewPlayer(quiverActor, 740, 136, 1f);
                File.AppendAllText(log, $"vanilla-magic-quiver DrawData={AccessoryCaptureDrawCount}\n");
            }

            foreach (int itemId in new[] { ItemID.BlackBelt, ItemID.Toolbelt })
            {
                Item waistItem = new();
                waistItem.SetDefaults(itemId);
                File.AppendAllText(log,
                    $"vanilla-waist-candidate type={waistItem.type} name={waistItem.Name} waistSlot={waistItem.waistSlot}\n");
                if (waistItem.waistSlot < 0)
                    continue;
                Player waistActor = CreateHeadwearPlayer(0, 1);
                waistActor.whoAmI = localPlayerIndex;
                waistActor.armor[3] = waistItem;
                waistActor.UpdateVisibleAccessories(waistItem, invisible: false, slot: 0, modded: false);
                Main.player[localPlayerIndex] = waistActor;
                AccessoryCaptureLabel = $"vanilla-waist-{waistItem.Name}";
                AccessoryCaptureSearch = "Acc_Waist";
                AccessoryCaptureDrawCount = 0;
                DrawPreviewPlayer(waistActor, 740, 236, 1f);
                File.AppendAllText(log, $"{AccessoryCaptureLabel} DrawData={AccessoryCaptureDrawCount}\n");
            }
        }
        finally
        {
            CaptureAccessoryDraws = false;
            AccessoryCaptureLabel = "";
            AccessoryCaptureSearch = "";
            Main.player[localPlayerIndex] = previousLocalPlayer;
            ForcePreviewLighting = previousLighting;
            Main.gameMenu = previousMenu;
            Main.UIScale = previousUiScale;
            Main.GameViewMatrix.Zoom = previousGameZoom;
            Main.spriteBatch.End();
        }

        using var target = new RenderTarget2D(device, width, height, false, SurfaceFormat.Color,
            DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
        device.SetRenderTarget(target);
        device.Clear(new Color(28, 31, 39));
        Main.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp,
            DepthStencilState.None, RasterizerState.CullCounterClockwise);
        Main.spriteBatch.Draw(fullTarget, new Rectangle(0, 0, width, height), new Rectangle(0, 0, width, height), Color.White);
        Main.spriteBatch.End();
        using var enlarged = new RenderTarget2D(device, width * scale, height * scale, false,
            SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
        device.SetRenderTarget(enlarged);
        device.Clear(Color.Black);
        Main.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp,
            DepthStencilState.None, RasterizerState.CullCounterClockwise);
        Main.spriteBatch.Draw(target, new Rectangle(0, 0, width * scale, height * scale), Color.White);
        Main.spriteBatch.End();
        device.SetRenderTarget(null);
        using var stream = File.Create(Path.Combine(OutputDirectory, "native-accessory-visibility.png"));
        enlarged.SaveAsPng(stream, width * scale, height * scale);
    }

    private string RunPassiveSuitStateFixture()
    {
        var suits = new (string Name, string Head, string Body, string Legs)[]
        {
            ("Iron", "IronHelmet", "IronChestplate", "IronGreaves"),
            ("Leather", "LeatherHeadband", "LeatherTunic", "LeatherLeggings"),
            ("Cloth", "ClothHood", "ClothRobe", "ClothLeggings"),
            ("Knight", "KnightHelmet", "KnightChestplate", "KnightGreaves"),
            ("Hunting", "HuntingHood", "HuntingChestplate", "HuntingLeggings"),
            ("Energy", "EnergyHood", "EnergyRobe", "EnergyLeggings"),
            ("Templar", "TemplarHelmet", "TemplarChestplate", "TemplarGreaves"),
            ("Nightmare", "NightmareHood", "NightmareChestplate", "NightmareLeggings"),
            ("Fallen", "FallenHood", "FallenRobe", "FallenLeggings"),
            ("Soulforged", "SoulforgedHelmet", "SoulforgedChestplate", "SoulforgedGreaves"),
            ("Abyss", "AbyssHood", "AbyssChestplate", "AbyssLeggings"),
            ("Dark", "DarkHood", "DarkRobe", "DarkLeggings")
        };
        var itemTypes = Mod.GetContent<ModItem>().ToDictionary(item => item.Name, item => (int)item.Type);
        int localIndex = Math.Clamp(Main.myPlayer, 0, Main.player.Length - 1);
        Player previousPlayer = Main.player[localIndex];
        bool previousMenu = Main.gameMenu;
        bool previousPaused = Main.gamePaused;
        try
        {
            Main.gameMenu = false;
            Main.gamePaused = false;
            int checkedSuits = 0;
            int statusBuffChecks = 0, tooltipChecks = 0;
            foreach (var suit in suits)
            {
                Player actor = CreatePreviewPlayer(itemTypes[suit.Head], itemTypes[suit.Body], itemTypes[suit.Legs]);
                actor.whoAmI = localIndex;
                actor.active = true;
                actor.statLife = actor.statLifeMax = 100;
                actor.GetModPlayer<Tier3T4ProgressPlayer>().ApplyNetworkState(true, true, false);
                Main.player[localIndex] = actor;
                MilkcraftPassivePlayer passive = actor.GetModPlayer<MilkcraftPassivePlayer>();
                passive.ResetEffects();
                passive.PostUpdateEquips();
                Require(passive.Suit == suit.Name && !passive.IsPaired, $"independent armor set failed: {suit.Name} got {passive.Suit}");

                string matchingAccessory = MilkcraftPassivePlayer.AccessoryForSuit(suit.Name);
                Item accessory = new();
                accessory.SetDefaults(itemTypes[matchingAccessory]);
                accessory.ModItem.UpdateAccessory(actor, false);
                passive.PostUpdateEquips();
                Require(passive.Suit == suit.Name && passive.IsPaired, $"matching accessory did not activate {suit.Name}: {matchingAccessory}");

                int statusBuffType = MilkcraftPassiveVfx.StatusBuffType(suit.Name);
                Require(statusBuffType > 0 && actor.buffType.Contains(statusBuffType), $"complete {suit.Name} set did not receive its one read-only status buff");
                Require(Main.buffNoTimeDisplay[statusBuffType], $"{suit.Name} status buff exposes a misleading engine timer");
                ModBuff statusBuff = GetPassiveStatusBuff(suit.Name);
                string buffName = "", buffTip = "";
                int buffRare = 0;
                statusBuff.ModifyBuffText(ref buffName, ref buffTip, ref buffRare);
                Require(!string.IsNullOrWhiteSpace(buffName) && buffTip == actor.setBonus,
                    $"{suit.Name} status buff did not read the active passive text: name={buffName}");
                statusBuffChecks++;

                var tooltipLines = new System.Collections.Generic.List<TooltipLine>
                {
                    new(Mod, "SetBonus", Terraria.Localization.Language.GetTextValue($"Mods.{Mod.Name}.SetBonuses.{suit.Name}"))
                };
                MilkcraftPassiveVfx.TintArmorTooltip(tooltipLines, Mod, suit.Name);
                Require(tooltipLines.Exists(line => line.Name == "SetBonus") &&
                    !tooltipLines.Exists(line => line.Name == "PassiveDescription") &&
                    tooltipLines.Exists(line => line.Name == "PassiveState") &&
                    tooltipLines.Exists(line => line.Name == "PassivePairState"),
                    $"{suit.Name} armor tooltip omitted static effect or active/pair state");
                Require(GetTooltipColor(tooltipLines, "SetBonus") == MilkcraftPassiveVfx.SuitColor(suit.Name) &&
                    GetTooltipColor(tooltipLines, "PassiveState") == MilkcraftPassiveVfx.SuitColor(suit.Name) &&
                    GetTooltipColor(tooltipLines, "PassivePairState") == new Color(113, 222, 142),
                    $"{suit.Name} active armor tooltip colors are not theme/green");
                var accessoryLines = new System.Collections.Generic.List<TooltipLine>();
                MilkcraftPassiveVfx.TintAccessoryTooltip(accessoryLines, Mod, matchingAccessory);
                Require(GetTooltipColor(accessoryLines, "PassiveFunctional") == new Color(232, 232, 220) &&
                    GetTooltipColor(accessoryLines, "PassivePair") == new Color(113, 222, 142),
                    $"{suit.Name} matching accessory tooltip colors are not active/green");
                tooltipChecks++;

                passive.ResetEffects();
                Item wrongAccessory = new();
                string wrongName = MilkcraftPassivePlayer.AccessoryForSuit(suits[(checkedSuits + 1) % suits.Length].Name);
                wrongAccessory.SetDefaults(itemTypes[wrongName]);
                wrongAccessory.ModItem.UpdateAccessory(actor, false);
                passive.PostUpdateEquips();
                Require(passive.Suit == suit.Name && !passive.IsPaired, $"wrong accessory activated {suit.Name}: {wrongName}");
                var wrongArmorLines = new System.Collections.Generic.List<TooltipLine>
                {
                    new(Mod, "SetBonus", Terraria.Localization.Language.GetTextValue($"Mods.{Mod.Name}.SetBonuses.{suit.Name}"))
                };
                MilkcraftPassiveVfx.TintArmorTooltip(wrongArmorLines, Mod, suit.Name);
                Require(GetTooltipColor(wrongArmorLines, "SetBonus") == MilkcraftPassiveVfx.SuitColor(suit.Name) &&
                    GetTooltipColor(wrongArmorLines, "PassivePairState") == new Color(145, 149, 158),
                    $"{suit.Name} wrong-pair armor tooltip did not retain theme with gray synergy");
                var wrongAccessoryLines = new System.Collections.Generic.List<TooltipLine>();
                MilkcraftPassiveVfx.TintAccessoryTooltip(wrongAccessoryLines, Mod, wrongName);
                Require(GetTooltipColor(wrongAccessoryLines, "PassiveFunctional") == new Color(232, 232, 220) &&
                    GetTooltipColor(wrongAccessoryLines, "PassivePair") == new Color(145, 149, 158),
                    $"{suit.Name} wrong-pair accessory tooltip did not retain functional/gray pair colors");
                checkedSuits++;
            }
            Player fullBarActor = Main.player[localIndex];
            for (int i = 0; i < fullBarActor.buffType.Length; i++)
            {
                fullBarActor.buffType[i] = BuffID.Ironskin;
                fullBarActor.buffTime[i] = 600;
            }
            MilkcraftPassiveVfx.UpdateStatusBuff(fullBarActor, "Iron");
            Require(fullBarActor.buffType.All(type => type == BuffID.Ironskin), "read-only passive status displaced a vanilla buff when the buff bar was full");
            return $"native-suit-hooks=PASS independent/matching/wrong-accessory={checkedSuits}/12 each via ResetEffects+UpdateAccessory+PostUpdateEquips statusBuffReadback={statusBuffChecks}/12 noTimer=12/12 tooltipStateColors={tooltipChecks}/12 activeTheme+pairGreen/wrongPairGray=PASS fullBuffBarVanillaPreserved={fullBarActor.buffType.Length}/{fullBarActor.buffType.Length}";
        }
        finally
        {
            Main.player[localIndex] = previousPlayer;
            Main.gameMenu = previousMenu;
            Main.gamePaused = previousPaused;
        }
    }

    private static ModBuff GetPassiveStatusBuff(string suit) => suit switch
    {
        "Iron" => ModContent.GetInstance<PassiveStatusIron>(), "Leather" => ModContent.GetInstance<PassiveStatusLeather>(),
        "Cloth" => ModContent.GetInstance<PassiveStatusCloth>(), "Knight" => ModContent.GetInstance<PassiveStatusKnight>(),
        "Hunting" => ModContent.GetInstance<PassiveStatusHunting>(), "Energy" => ModContent.GetInstance<PassiveStatusEnergy>(),
        "Templar" => ModContent.GetInstance<PassiveStatusTemplar>(), "Nightmare" => ModContent.GetInstance<PassiveStatusNightmare>(),
        "Fallen" => ModContent.GetInstance<PassiveStatusFallen>(), "Soulforged" => ModContent.GetInstance<PassiveStatusSoulforged>(),
        "Abyss" => ModContent.GetInstance<PassiveStatusAbyss>(), "Dark" => ModContent.GetInstance<PassiveStatusDark>(),
        _ => throw new InvalidOperationException($"No passive status buff fixture for {suit}")
    };

    private static Color? GetTooltipColor(System.Collections.Generic.List<TooltipLine> lines, string name)
        => lines.FirstOrDefault(line => line.Name == name)?.OverrideColor;

    private string RunPassiveShieldHurtFixture()
    {
        int localIndex = Math.Clamp(Main.myPlayer, 0, Main.player.Length - 1);
        Player previousPlayer = Main.player[localIndex];
        bool previousMenu = Main.gameMenu, previousPaused = Main.gamePaused;
        Player actor = null;
        try
        {
            Main.gameMenu = false;
            Main.gamePaused = false;
            actor = CreatePreviewPlayer(ModContent.ItemType<TemplarHelmet>(), ModContent.ItemType<TemplarChestplate>(),
                ModContent.ItemType<TemplarGreaves>());
            actor.whoAmI = localIndex;
            actor.active = true;
            actor.statLife = actor.statLifeMax = 100;
            actor.statDefense = default;
            actor.endurance = 0f;
            actor.immune = false;
            actor.immuneTime = 0;
            actor.GetModPlayer<Tier3T4ProgressPlayer>().ApplyNetworkState(true, true, false);
            Main.player[localIndex] = actor;
            MilkcraftPassivePlayer passive = actor.GetModPlayer<MilkcraftPassivePlayer>();
            passive.ResetEffects();
            passive.PostUpdateEquips(); // Initial 30-point independent Templar barrier.
            Require(passive.DiagnosticShield("Templar") == 30, "Templar barrier did not initialize to 30");

            int lifeBeforeFullAbsorb = actor.statLife;
            actor.Hurt(PlayerDeathReason.ByCustomReason(Terraria.Localization.NetworkText.FromLiteral("Milkcraft passive shield diagnostic")), 10, 1, false, true);
            Require(actor.statLife == lifeBeforeFullAbsorb,
                $"fully absorbed Player.Hurt leaked life: {lifeBeforeFullAbsorb}->{actor.statLife}");
            Require(passive.DiagnosticShield("Templar") == 20, "fully absorbed hit did not remove exactly 10 barrier points");
            int lifeBeforeBreak = actor.statLife;
            actor.immune = false;
            actor.immuneTime = 0;
            actor.Hurt(PlayerDeathReason.ByCustomReason(Terraria.Localization.NetworkText.FromLiteral("Milkcraft passive shield diagnostic")), 40, 1, false, true);
            int lifeAfterBreak = actor.statLife;
            Require(lifeAfterBreak < lifeBeforeBreak, "partial Player.Hurt did not pass damage after barrier depletion");
            Require(passive.DiagnosticShield("Templar") == 0 && passive.DiagnosticShieldCooldown("Templar") == 720,
                $"Templar barrier break state mismatch: shield={passive.DiagnosticShield("Templar")} cooldown={passive.DiagnosticShieldCooldown("Templar")}");
            actor.armor[0].SetDefaults(ModContent.ItemType<KnightHelmet>());
            actor.armor[1].SetDefaults(ModContent.ItemType<KnightChestplate>());
            actor.armor[2].SetDefaults(ModContent.ItemType<KnightGreaves>());
            passive.PostUpdateEquips();
            actor.armor[0].SetDefaults(ModContent.ItemType<TemplarHelmet>());
            actor.armor[1].SetDefaults(ModContent.ItemType<TemplarChestplate>());
            actor.armor[2].SetDefaults(ModContent.ItemType<TemplarGreaves>());
            passive.PostUpdateEquips();
            Require(passive.Suit == "Templar" && passive.DiagnosticShield("Templar") == 0 && passive.DiagnosticShieldCooldown("Templar") == 718,
                "switching away from and back to Templar refilled its depleted barrier");
            return $"native-player-hurt=PASS fullAbsorbDamage=10 life={lifeBeforeFullAbsorb}->{lifeBeforeBreak} barrier={30}->{passive.DiagnosticShield("Templar")} partialDamage=40 life={lifeBeforeBreak}->{lifeAfterBreak} cooldown={passive.DiagnosticShieldCooldown("Templar")} switchAwayBackDidNotRefill=PASS";
        }
        finally
        {
            Main.player[localIndex] = previousPlayer;
            Main.gameMenu = previousMenu;
            Main.gamePaused = previousPaused;
        }
    }

    private string RunPassiveManaCycleFixture()
    {
        int localIndex = Math.Clamp(Main.myPlayer, 0, Main.player.Length - 1);
        Player previousPlayer = Main.player[localIndex];
        bool previousMenu = Main.gameMenu, previousPaused = Main.gamePaused;
        NPC[] previousNpcs = Main.npc;
        try
        {
            Main.gameMenu = false;
            Main.gamePaused = false;
            Main.npc = Enumerable.Range(0, previousNpcs.Length).Select(_ => new NPC()).ToArray();
            NPC target = Main.npc[0];
            target.SetDefaults(NPCID.BlueSlime);
            target.whoAmI = 0;
            target.active = true;
            target.friendly = false;
            target.dontTakeDamage = false;
            target.life = target.lifeMax = 500;

            Player actor = CreatePreviewPlayer(ModContent.ItemType<FallenHood>(), ModContent.ItemType<FallenRobe>(),
                ModContent.ItemType<FallenLeggings>());
            actor.whoAmI = localIndex;
            actor.active = true;
            actor.position = new Vector2(400f, 400f);
            actor.statLifeMax = 100;
            actor.statLife = 50;
            actor.statManaMax2 = 200;
            actor.statMana = 100;
            actor.GetModPlayer<Tier3T4ProgressPlayer>().ApplyNetworkState(true, true, false);
            Item tome = new();
            tome.SetDefaults(ModContent.ItemType<FallenTome>());
            tome.ModItem.UpdateAccessory(actor, false);
            Main.player[localIndex] = actor;
            MilkcraftPassivePlayer passive = actor.GetModPlayer<MilkcraftPassivePlayer>();
            passive.ResetEffects();
            tome.ModItem.UpdateAccessory(actor, false);
            passive.PostUpdateEquips();

            Item paidStaff = new();
            paidStaff.SetDefaults(ModContent.ItemType<FallenStaff>());
            int manaBeforePaidCycle = actor.statMana;
            Require(actor.CheckMana(paidStaff, paidStaff.mana, true, true), "first paid FallenStaff mana use failed");
            int afterFirstSpend = actor.statMana;
            int actualManaPaid = manaBeforePaidCycle - afterFirstSpend;
            Require(afterFirstSpend < manaBeforePaidCycle, "CheckMana did not consume actual mana");
            passive.OnHitNPCWithItem(paidStaff, target, default, 20); // Valid hit starts the native ModPlayer six-second combat cycle.
            Require(passive.DiagnosticManaPaid("Fallen") == actualManaPaid,
                $"GlobalItem.OnConsumeMana ledger mismatch: actual={actualManaPaid}, recorded={passive.DiagnosticManaPaid("Fallen")}");
            int lifeBeforeCycleReward = actor.statLife;
            for (int tick = 0; tick < 360; tick++) passive.PostUpdateEquips();

            Item zeroCostStaff = paidStaff.Clone();
            zeroCostStaff.mana = 0;
            int beforeSecondCycleHitMana = actor.statMana;
            int beforeSecondCycleHitLife = actor.statLife;
            Require(actor.CheckMana(zeroCostStaff, 0, true, true), "zero-cost FallenStaff test use should pass");
            passive.OnHitNPCWithItem(zeroCostStaff, target, default, 20);
            int afterSecondCycleMana = actor.statMana;
            int afterSecondCycleLife = actor.statLife;
            Require(afterSecondCycleLife > beforeSecondCycleHitLife, "paid-cycle zero-cost Fallen spell failed its once-per-cycle heal");
            int expectedRefund = actualManaPaid * 20 / 100;
            Require(afterSecondCycleLife - beforeSecondCycleHitLife == 15,
                $"paired Fallen heal amount mismatch: expected 15, got {afterSecondCycleLife - beforeSecondCycleHitLife}");
            Require(afterSecondCycleMana - beforeSecondCycleHitMana == expectedRefund,
                $"Fallen refund did not match actual spent mana: expected={expectedRefund}, got={afterSecondCycleMana - beforeSecondCycleHitMana}");

            Player zeroSpendActor = CreatePreviewPlayer(ModContent.ItemType<FallenHood>(), ModContent.ItemType<FallenRobe>(),
                ModContent.ItemType<FallenLeggings>());
            zeroSpendActor.whoAmI = localIndex;
            zeroSpendActor.active = true;
            zeroSpendActor.statLifeMax = 100;
            zeroSpendActor.statLife = 50;
            zeroSpendActor.GetModPlayer<Tier3T4ProgressPlayer>().ApplyNetworkState(true, true, false);
            Main.player[localIndex] = zeroSpendActor;
            MilkcraftPassivePlayer zeroPassive = zeroSpendActor.GetModPlayer<MilkcraftPassivePlayer>();
            zeroPassive.ResetEffects();
            zeroPassive.PostUpdateEquips();
            int zeroLife = zeroSpendActor.statLife;
            for (int cycle = 0; cycle < 2; cycle++)
            {
                zeroPassive.OnHitNPCWithItem(zeroCostStaff, target, default, 20);
                for (int tick = 0; tick < 360; tick++) zeroPassive.PostUpdateEquips();
            }
            Require(zeroSpendActor.statLife == zeroLife,
                $"zero-spend magic attacks generated free Fallen healing: {zeroLife}->{zeroSpendActor.statLife}");
            return $"fallen-mana-cycle=PASS CheckManaSpent={actualManaPaid} OnConsumeManaLedger={passive.DiagnosticManaPaid("Fallen")} 360Frames=PASS zeroCostHitInPaidCycleHeal={afterSecondCycleLife - beforeSecondCycleHitLife} refundExpected/Actual={expectedRefund}/{afterSecondCycleMana - beforeSecondCycleHitMana} zeroSpendTwoCyclesNoFreeHeal=PASS";
        }
        finally
        {
            Main.player[localIndex] = previousPlayer;
            Main.npc = previousNpcs;
            Main.gameMenu = previousMenu;
            Main.gamePaused = previousPaused;
        }
    }

    private (string Report, System.Collections.Generic.List<Projectile> Projectiles) RunPassiveWeaponNativeFixture()
    {
        var cases = new (string Weapon, string Accessory, bool Homing)[]
        {
            ("IronBow", "IronQuiver", false), ("HuntingBow", "HuntingQuiver", false),
            ("NightmareBow", "NightmareQuiver", true), ("AbyssBow", "AbyssQuiver", true),
            ("IronStaff", "BeginnerTome", false), ("EnergyStaff", "EnergyTome", false),
            ("FallenStaff", "FallenTome", true), ("DarkStaff", "DarkTome", true)
        };
        var itemTypes = Mod.GetContent<ModItem>().ToDictionary(item => item.Name, item => (int)item.Type);
        int localIndex = Math.Clamp(Main.myPlayer, 0, Main.player.Length - 1);
        Player previousPlayer = Main.player[localIndex];
        Projectile[] previousProjectiles = Main.projectile;
        NPC[] previousNpcs = Main.npc;
        Dust[] previousDust = Main.dust;
        bool previousMenu = Main.gameMenu, previousPaused = Main.gamePaused, previousWorldGen = WorldGen.gen;
        Vector2 previousScreen = Main.screenPosition;
        int previousMouseX = Main.mouseX, previousMouseY = Main.mouseY;
        Terraria.Utilities.UnifiedRandom previousRandom = Main.rand;
        var rendered = new System.Collections.Generic.List<Projectile>();
        int fired = 0, updated = 0, homingAcquired = 0, homingRetargeted = 0;
        try
        {
            Main.gameMenu = false;
            Main.gamePaused = false;
            WorldGen.gen = false;
            Main.screenPosition = Vector2.Zero;
            Main.rand = new Terraria.Utilities.UnifiedRandom(20261007);
            Main.projectile = Enumerable.Range(0, previousProjectiles.Length).Select(_ => new Projectile()).ToArray();
            Main.npc = Enumerable.Range(0, previousNpcs.Length).Select(_ => new NPC()).ToArray();
            Main.dust = Enumerable.Range(0, previousDust.Length).Select(_ => new Dust()).ToArray();
            File.WriteAllText(Path.Combine(OutputDirectory, "passive-native-itemcheck.log"),
                "Native Player.ItemCheck/Shoot paired accessory cases, then native Projectile.Update homing and target-death retarget checks.\n");
            Require(Main.tile.Width > 100 && Main.tile.Height > 100, "paired weapon fixture requires a loaded native tile world");

            foreach (var test in cases)
            {
                Main.projectile = Enumerable.Range(0, previousProjectiles.Length).Select(_ => new Projectile()).ToArray();
                foreach (NPC npc in Main.npc) npc.active = false;
                Player actor = CreateActionPlayer(ModContent.ItemType<KnightHelmet>(), ModContent.ItemType<KnightChestplate>(),
                    ModContent.ItemType<KnightGreaves>(), itemTypes[test.Weapon], 1, 0f, male: true, pose: 0);
                actor.active = true;
                actor.whoAmI = localIndex;
                actor.position = new Vector2(400f, 400f);
                actor.statMana = 200;
                actor.armor[3] = new Item();
                actor.armor[3].SetDefaults(itemTypes[test.Accessory]);
                actor.GetModPlayer<Tier3T4ProgressPlayer>().ApplyNetworkState(true, true, false);
                actor.GetModPlayer<MilkcraftPassivePlayer>().ResetEffects();
                actor.armor[3].ModItem.UpdateAccessory(actor, false);
                Main.player[localIndex] = actor;

                if (actor.inventory[0].useAmmo == AmmoID.Arrow)
                {
                    actor.inventory[1] = new Item();
                    actor.inventory[1].SetDefaults(ItemID.WoodenArrow);
                    actor.inventory[1].stack = 999;
                }
                actor.itemTime = actor.itemAnimation = 0;
                actor.controlUseItem = true;
                actor.releaseUseItem = false;
                Vector2 aimOrigin = actor.position + new Vector2(actor.width * .5f, actor.height * .5f);
                Main.mouseX = (int)(aimOrigin.X + 220f);
                Main.mouseY = (int)aimOrigin.Y;
                actor.ItemCheck();

                Projectile[] spawned = Main.projectile.Where(projectile => projectile.active && projectile.owner == localIndex).ToArray();
                Require(spawned.Length > 0, $"native ItemCheck did not fire {test.Weapon}");
                foreach (Projectile projectile in spawned)
                {
                    MilkcraftPairedProjectile global = projectile.GetGlobalProjectile<MilkcraftPairedProjectile>();
                    Require(global.Paired && global.Homing == test.Homing,
                        $"native source/accessory pairing failed for {test.Weapon}: paired={global.Paired}, homing={global.Homing}");
                    Require(projectile.ModProjectile is not SuitPassiveProjectile,
                        $"paired appearance replaced native projectile behavior for {test.Weapon}");
                    int originalType = projectile.type;
                    Require(originalType > 0, $"native projectile type missing for {test.Weapon}");

                    if (test.Homing)
                    {
                        projectile.Center = actor.Center;
                        projectile.velocity = Vector2.UnitX * 9f;
                        projectile.friendly = false;
                        projectile.tileCollide = false;
                        projectile.damage = 0;
                        projectile.timeLeft = 60;
                        NPC first = Main.npc[0];
                        first.SetDefaults(NPCID.BlueSlime);
                        first.whoAmI = 0;
                        first.active = true;
                        first.friendly = false;
                        first.dontTakeDamage = false;
                        first.Center = actor.Center + new Vector2(28f, 30f);
                        int slot = projectile.whoAmI;
                        Require(slot >= 0 && slot < Main.projectile.Length, $"invalid projectile slot for {test.Weapon}");
                        Vector2 beforeTurn = projectile.velocity;
                        projectile.Update(slot);
                        updated++;
                        int acquired = global.DiagnosticTarget;
                        Require(acquired == 0, $"native Projectile.Update failed to acquire first target for {test.Weapon}; target={acquired}");
                        Require(projectile.velocity.Y > beforeTurn.Y + 0.05f,
                            $"native Projectile.Update acquired but did not steer toward first target for {test.Weapon}: before={beforeTurn}, after={projectile.velocity}");
                        homingAcquired++;

                        first.active = false;
                        NPC second = Main.npc[1];
                        second.SetDefaults(NPCID.BlueSlime);
                        second.whoAmI = 1;
                        second.active = true;
                        second.friendly = false;
                        second.dontTakeDamage = false;
                        second.Center = actor.Center + new Vector2(18f, 32f);
                        projectile.Update(slot);
                        updated++;
                        Require(global.DiagnosticTarget == 1,
                            $"native Projectile.Update failed to retarget dead target for {test.Weapon}; target={global.DiagnosticTarget}");
                        homingRetargeted++;
                        second.active = false;
                        projectile.Update(slot);
                        updated++;
                        Require(global.DiagnosticTarget == -1,
                            $"native Projectile.Update did not clear target after all NPCs died for {test.Weapon}; target={global.DiagnosticTarget}");
                    }
                    else
                    {
                        projectile.Center = actor.Center;
                        projectile.velocity = Vector2.UnitX * 9f;
                        projectile.friendly = false;
                        projectile.tileCollide = false;
                        projectile.damage = 0;
                        projectile.timeLeft = 60;
                        projectile.Update(projectile.whoAmI);
                        updated++;
                        Require(global.DiagnosticTarget == -1, $"T1/T2 projectile unexpectedly acquired a homing target: {test.Weapon}");
                    }
                    Require(projectile.type == originalType, $"paired draw changed native projectile type for {test.Weapon}");
                    int previewIndex = rendered.Count;
                    projectile.Center = new Vector2(92 + (previewIndex % 4) * 205, 110 + (previewIndex / 4) * 185);
                    projectile.velocity = Vector2.UnitX * 3f;
                    projectile.friendly = false;
                    projectile.tileCollide = false;
                    projectile.timeLeft = 60;
                    int originalExtraUpdates = projectile.extraUpdates;
                    if (previewIndex == 0) projectile.extraUpdates = 2;
                    global.ResetDiagnosticTrailHistory();
                    global.ResetDiagnosticPostAiTrace();
                    for (int trailTick = 0; trailTick < 8; trailTick++)
                    {
                        projectile.Update(projectile.whoAmI);
                        updated++;
                        Require(projectile.active, $"{test.Weapon} projectile became inactive during real trail history updates");
                    }
                    FieldInfo trailCountField = typeof(MilkcraftPairedProjectile).GetField("_vfxCount", BindingFlags.Instance | BindingFlags.NonPublic);
                    int trailSamples = trailCountField is null ? 0 : (int)trailCountField.GetValue(global);
                    Require(trailSamples >= 8 && global.DiagnosticAppendCount == 8,
                        $"{test.Weapon} real Projectile.Update produced history={trailSamples}/8 with appendCount={global.DiagnosticAppendCount}/8; native PostAI trace: {global.DiagnosticPostAiTrace}");
                    if (previewIndex == 0)
                        Require(global.DiagnosticPostAiCalls == 24 && global.DiagnosticFirstNumUpdates == 1 && global.DiagnosticLastNumUpdates == -1,
                            $"extraUpdates=2 must run 3 PostAI subupdates per frame (1,0,-1) and append once at terminal -1: {global.DiagnosticPostAiTrace}; append={global.DiagnosticAppendCount}");
                    projectile.extraUpdates = originalExtraUpdates;
                    rendered.Add(projectile);
                    File.AppendAllText(Path.Combine(OutputDirectory, "passive-native-itemcheck.log"),
                        $"weapon={test.Weapon} accessory={test.Accessory} projectiles={spawned.Length} type={projectile.type} paired={global.Paired} homing={global.Homing} target={global.DiagnosticTarget} historySamples={trailSamples} appendCount={global.DiagnosticAppendCount} nativePostAI={global.DiagnosticPostAiTrace} updates={updated}\n");
                    break; // One real shot per weapon is enough; preserve shotgun/projectile fanout for gameplay.
                }
                fired++;
            }
            Require(fired == 8 && rendered.Count == 8, $"expected eight native matched shots, got {fired}/8");
            Require(homingAcquired == 4 && homingRetargeted == 4, $"expected 4 T3/T4 homing acquire/reselect cases, got {homingAcquired}/{homingRetargeted}");

            Main.projectile = Enumerable.Range(0, previousProjectiles.Length).Select(_ => new Projectile()).ToArray();
            Player ichorActor = CreateActionPlayer(ModContent.ItemType<KnightHelmet>(), ModContent.ItemType<KnightChestplate>(),
                ModContent.ItemType<KnightGreaves>(), itemTypes["HuntingBow"], 1, 0f, male: true, pose: 0);
            ichorActor.active = true;
            ichorActor.whoAmI = localIndex;
            ichorActor.position = new Vector2(400f, 400f);
            ichorActor.armor[3] = new Item();
            ichorActor.armor[3].SetDefaults(itemTypes["HuntingQuiver"]);
            ichorActor.GetModPlayer<MilkcraftPassivePlayer>().ResetEffects();
            ichorActor.armor[3].ModItem.UpdateAccessory(ichorActor, false);
            ichorActor.inventory[1] = new Item();
            ichorActor.inventory[1].SetDefaults(ItemID.IchorArrow);
            ichorActor.inventory[1].stack = 999;
            ichorActor.itemTime = ichorActor.itemAnimation = 0;
            ichorActor.controlUseItem = true;
            ichorActor.releaseUseItem = false;
            Main.player[localIndex] = ichorActor;
            Main.mouseX = (int)(ichorActor.Center.X + 220f);
            Main.mouseY = (int)ichorActor.Center.Y;
            ichorActor.ItemCheck();
            Projectile ichorShot = Main.projectile.FirstOrDefault(projectile => projectile.active && projectile.owner == localIndex);
            Require(ichorShot is not null && ichorShot.type == ProjectileID.IchorArrow &&
                ichorShot.GetGlobalProjectile<MilkcraftPairedProjectile>().Paired,
                $"matched HuntingBow must preserve Ichor Arrow projectile type, actual={ichorShot?.type}");
            File.AppendAllText(Path.Combine(OutputDirectory, "passive-native-itemcheck.log"),
                $"ammo-preservation=PASS bow=HuntingBow ammo=IchorArrow projectile={ichorShot.type}\n");
            return ($"native-itemcheck=PASS matchedShots={fired}/8 Projectile.Update={updated} nativeTypePreserved=8/8 IchorArrowPreserved=PASS T1T2NoHoming=4/4 T3T4AcquireRetarget={homingAcquired}/{homingRetargeted}", rendered);
        }
        finally
        {
            Main.player[localIndex] = previousPlayer;
            Main.projectile = previousProjectiles;
            Main.npc = previousNpcs;
            Main.dust = previousDust;
            Main.gameMenu = previousMenu;
            Main.gamePaused = previousPaused;
            WorldGen.gen = previousWorldGen;
            Main.screenPosition = previousScreen;
            Main.mouseX = previousMouseX;
            Main.mouseY = previousMouseY;
            Main.rand = previousRandom;
        }
    }

    private string RunPassiveProcHookFixture()
    {
        var cases = new (string Suit, string Weapon, string Accessory, float Fraction, int Penetrate)[]
        {
            ("Nightmare", "NightmareBow", "NightmareQuiver", .60f, 1),
            ("Abyss", "AbyssBow", "AbyssQuiver", .65f, 3),
            ("Dark", "DarkStaff", "DarkTome", .75f, 1),
            ("Abyss", "AbyssBow", "", .65f, 2)
        };
        var itemTypes = Mod.GetContent<ModItem>().ToDictionary(item => item.Name, item => (int)item.Type);
        int localIndex = Math.Clamp(Main.myPlayer, 0, Main.player.Length - 1);
        Player previousPlayer = Main.player[localIndex];
        Projectile[] previousProjectiles = Main.projectile;
        NPC[] previousNpcs = Main.npc;
        Dust[] previousDust = Main.dust;
        bool previousMenu = Main.gameMenu, previousPaused = Main.gamePaused;
        Vector2 previousScreen = Main.screenPosition;
        int previousMouseX = Main.mouseX, previousMouseY = Main.mouseY;
        try
        {
            Main.gameMenu = false;
            Main.gamePaused = false;
            Main.screenPosition = Vector2.Zero;
            File.WriteAllText(Path.Combine(OutputDirectory, "passive-proc-hooks.log"),
                "Actual matched ItemCheck projectiles enter ModPlayer/GlobalProjectile hit hooks; generated passives are counted and fed back into the owner hit hook to verify no recursion.\n");
            int checkedCases = 0;
            foreach (var test in cases)
            {
                Main.projectile = Enumerable.Range(0, previousProjectiles.Length).Select(_ => new Projectile()).ToArray();
                Main.npc = Enumerable.Range(0, previousNpcs.Length).Select(_ => new NPC()).ToArray();
                Main.dust = Enumerable.Range(0, previousDust.Length).Select(_ => new Dust()).ToArray();
                Player actor = CreateActionPlayer(itemTypes[test.Suit + (test.Suit is "Nightmare" or "Abyss" or "Dark" ? "Hood" : "Helmet")],
                    itemTypes[test.Suit + (test.Suit == "Dark" ? "Robe" : "Chestplate")],
                    itemTypes[test.Suit + (test.Suit == "Nightmare" ? "Leggings" : test.Suit == "Abyss" ? "Leggings" : test.Suit == "Dark" ? "Leggings" : "Greaves")],
                    itemTypes[test.Weapon], 1, 0f, male: true, pose: 0);
                actor.active = true;
                actor.whoAmI = localIndex;
                actor.position = new Vector2(400f, 400f);
                actor.statMana = 200;
                // Do not inherit an accessory from CreateActionPlayer's cloned local player.
                actor.armor[3] = new Item();
                actor.GetModPlayer<Tier3T4ProgressPlayer>().ApplyNetworkState(true, true, false);
                actor.GetModPlayer<MilkcraftPassivePlayer>().ResetEffects();
                if (!string.IsNullOrEmpty(test.Accessory))
                {
                    actor.armor[3] = new Item();
                    actor.armor[3].SetDefaults(itemTypes[test.Accessory]);
                    actor.armor[3].ModItem.UpdateAccessory(actor, false);
                }
                Main.player[localIndex] = actor;
                MilkcraftPassivePlayer passive = actor.GetModPlayer<MilkcraftPassivePlayer>();
                passive.PostUpdateEquips();
                Require(passive.Suit == test.Suit && passive.IsPaired == !string.IsNullOrEmpty(test.Accessory),
                    $"proc fixture suit/accessory state mismatch: {test.Suit}/{test.Accessory} => {passive.Suit}/{passive.IsPaired}");
                if (actor.inventory[0].useAmmo == AmmoID.Arrow)
                {
                    actor.inventory[1] = new Item();
                    actor.inventory[1].SetDefaults(ItemID.WoodenArrow);
                    actor.inventory[1].stack = 999;
                }
                actor.itemTime = actor.itemAnimation = 0;
                actor.controlUseItem = true;
                actor.releaseUseItem = false;
                Main.mouseX = (int)(actor.Center.X + 220f);
                Main.mouseY = (int)actor.Center.Y;
                actor.ItemCheck();
                Projectile mainShot = Main.projectile.FirstOrDefault(projectile => projectile.active && projectile.owner == localIndex);
                Require(mainShot is not null, $"proc fixture native ItemCheck did not fire {test.Weapon}");
                MilkcraftPairedProjectile mainGlobal = mainShot.GetGlobalProjectile<MilkcraftPairedProjectile>();
                Require(mainGlobal.Paired == !string.IsNullOrEmpty(test.Accessory),
                    $"main ItemCheck projectile pairing mismatch for {test.Weapon}");
                NPC target = Main.npc[0];
                target.SetDefaults(NPCID.BlueSlime);
                target.whoAmI = 0;
                target.active = true;
                target.friendly = false;
                target.dontTakeDamage = false;
                target.life = target.lifeMax = 500;
                mainShot.damage = 100;
                mainShot.owner = localIndex;
                passive.OnHitNPCWithProj(mainShot, target, default, 100);
                passive.OnHitNPCWithProj(mainShot, target, default, 100); // Same-tick/multi-hit duplicate must be gated.
                Projectile[] procs = Main.projectile.Where(projectile => projectile.active && projectile.owner == localIndex &&
                    projectile.type == ModContent.ProjectileType<SuitPassiveProjectile>()).ToArray();
                Require(procs.Length == 1, $"{test.Suit} same-tick hits emitted {procs.Length} generated passives instead of one");
                Projectile proc = procs[0];
                int expectedDamage = (int)(100 * test.Fraction);
                Require(proc.damage == expectedDamage,
                    $"{test.Suit} passive damage mismatch: expected {expectedDamage}, actual {proc.damage}");
                Require(proc.penetrate == test.Penetrate,
                    $"{test.Suit} passive penetration mismatch: expected {test.Penetrate}, actual {proc.penetrate}");
                Require(proc.GetGlobalProjectile<MilkcraftPairedProjectile>().PassiveProjectile,
                    $"{test.Suit} generated projectile lacks persistent no-recursion marker");
                // The recursion assertion must not pass merely because the shared proc cooldown is active.
                for (int tick = 0; tick < 120; tick++) passive.PostUpdateEquips();
                int countBeforeFeedback = procs.Length;
                passive.OnHitNPCWithProj(proc, target, default, proc.damage);
                int countAfterFeedback = Main.projectile.Count(projectile => projectile.active && projectile.owner == localIndex &&
                    projectile.type == ModContent.ProjectileType<SuitPassiveProjectile>());
                Require(countAfterFeedback == countBeforeFeedback,
                    $"{test.Suit} generated projectile recursively triggered another passive proc");
                File.AppendAllText(Path.Combine(OutputDirectory, "passive-proc-hooks.log"),
                    $"suit={test.Suit} weapon={test.Weapon} paired={mainGlobal.Paired} baseDamage=100 fraction={test.Fraction:0.00} procDamage={proc.damage} penetrate={proc.penetrate} duplicateSameTick=1->1 recursion=0\n");
                checkedCases++;
            }
            Require(checkedCases == cases.Length, $"incomplete passive proc hook matrix: {checkedCases}/{cases.Length}");
            return $"native-passive-proc-hooks=PASS matchedMainProjectileTriggers=3/3 sameTickDuplicateBlocked=4/4 exactDamageFractions=4/4 abyssPenetrateUnpaired/paired=2/3 generatedProjectileRecursion=0/4";
        }
        finally
        {
            Main.player[localIndex] = previousPlayer;
            Main.projectile = previousProjectiles;
            Main.npc = previousNpcs;
            Main.dust = previousDust;
            Main.gameMenu = previousMenu;
            Main.gamePaused = previousPaused;
            Main.screenPosition = previousScreen;
            Main.mouseX = previousMouseX;
            Main.mouseY = previousMouseY;
        }
    }

    private void RenderTier3T4AccessoryPreviews()
    {
        GraphicsDevice device = Main.graphics.GraphicsDevice;
        const int width = 800;
        const int height = 480;
        const int scale = 2;
        int renderWidth = Math.Max(width, Main.screenWidth);
        int renderHeight = Math.Max(height, Main.screenHeight);
        using var fullTarget = new RenderTarget2D(device, renderWidth, renderHeight, false,
            SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
        device.SetRenderTarget(fullTarget);
        device.Clear(new Color(28, 31, 39));
        Main.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp,
            DepthStencilState.None, RasterizerState.CullCounterClockwise);

        var entries = new (string Name, int Item, string Texture)[]
        {
            ("TemplarShield", ModContent.ItemType<TemplarShield>(), "TemplarShield_Shield"),
            ("NightmareQuiver", ModContent.ItemType<NightmareQuiver>(), "NightmareQuiver_Back"),
            ("FallenTome", ModContent.ItemType<FallenTome>(), "FallenTome_Waist"),
            ("SoulforgedShield", ModContent.ItemType<SoulforgedShield>(), "SoulforgedShield_Shield"),
            ("AbyssQuiver", ModContent.ItemType<AbyssQuiver>(), "AbyssQuiver_Back"),
            ("DarkTome", ModContent.ItemType<DarkTome>(), "DarkTome_Waist")
        };
        int localPlayerIndex = Math.Clamp(Main.myPlayer, 0, Main.player.Length - 1);
        Player previousPlayer = Main.player[localPlayerIndex];
        bool previousMenu = Main.gameMenu;
        bool previousLighting = ForcePreviewLighting;
        float previousUiScale = Main.UIScale;
        Vector2 previousZoom = Main.GameViewMatrix.Zoom;
        string log = Path.Combine(OutputDirectory, "native-t3-t4-accessory-layers.log");
        File.WriteAllText(log,
            "Native PlayerRenderer for T3/T4 shield, back and waist slots on the male model; both facing directions, walk/guard frames, and hideVisual.\n");
        try
        {
            Main.gameMenu = false;
            ForcePreviewLighting = true;
            Main.UIScale = 1f;
            Main.GameViewMatrix.Zoom = Vector2.One;
            CaptureAccessoryDraws = true;
            for (int index = 0; index < entries.Length; index++)
            {
                var entry = entries[index];
                bool shield = entry.Texture.Contains("Shield", StringComparison.Ordinal);
                int visiblePoses = shield ? 4 : 3;
                for (int pose = 0; pose <= visiblePoses; pose++)
                {
                    bool hidden = pose == visiblePoses;
                    Player actor = CreateHeadwearPlayer(0, 1);
                    actor.whoAmI = localPlayerIndex;
                    actor.Male = true;
                    actor.GetModPlayer<Tier3T4ProgressPlayer>().ApplyNetworkState(true, true, false);
                    actor.armor[3] = new Item();
                    actor.armor[3].SetDefaults(entry.Item);
                    actor.hideVisibleAccessory[0] = hidden;
                    actor.UpdateVisibleAccessories(actor.armor[3], hidden, 0, modded: false);
                    int frame = pose == 3 ? 5 : pose == 2 ? 3 : 0;
                    actor.bodyFrame = new Rectangle(0, frame * 56, 40, 56);
                    actor.legFrame = new Rectangle(0, frame * 56, 40, 56);
                    actor.direction = pose == 1 || pose == 2 ? -1 : 1;
                    actor.velocity = actor.direction < 0 ? new Vector2(-1f, 0f) : Vector2.Zero;
                    Main.player[localPlayerIndex] = actor;
                    string poseName = hidden ? "hidden" : pose switch
                    {
                        0 => "right-idle",
                        1 => "left-idle",
                        2 => "left-walk-frame3",
                        3 => "guard-frame5",
                        _ => "pose"
                    };
                    AccessoryCaptureLabel = $"{entry.Name}/{poseName}";
                    AccessoryCaptureSearch = entry.Texture;
                    AccessoryCaptureDrawCount = 0;
                    AccessorySourceAlphaPixels = 0;
                    AccessorySourceInsideTexture = false;
                    DrawPreviewPlayer(actor, 64 + index * 122, 36 + pose * 82, 1f);
                    Require(hidden ? AccessoryCaptureDrawCount == 0 : AccessoryCaptureDrawCount > 0,
                        $"{entry.Name}/{poseName} native layer visibility mismatch: draws={AccessoryCaptureDrawCount}");
                    if (!hidden)
                        Require(AccessorySourceInsideTexture && AccessorySourceAlphaPixels > 0,
                            $"{entry.Name}/{poseName} uses invalid or transparent native source frame");
                    File.AppendAllText(log,
                        $"item={entry.Name} pose={poseName} direction={actor.direction} body={actor.bodyFrame} " +
                        $"draws={AccessoryCaptureDrawCount} alpha={AccessorySourceAlphaPixels} inside={AccessorySourceInsideTexture}\n");
                }
            }
        }
        finally
        {
            CaptureAccessoryDraws = false;
            AccessoryCaptureLabel = "";
            AccessoryCaptureSearch = "";
            Main.player[localPlayerIndex] = previousPlayer;
            ForcePreviewLighting = previousLighting;
            Main.gameMenu = previousMenu;
            Main.UIScale = previousUiScale;
            Main.GameViewMatrix.Zoom = previousZoom;
            Main.spriteBatch.End();
        }

        using var native = new RenderTarget2D(device, width, height, false, SurfaceFormat.Color,
            DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
        device.SetRenderTarget(native);
        device.Clear(new Color(28, 31, 39));
        Main.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp,
            DepthStencilState.None, RasterizerState.CullCounterClockwise);
        Main.spriteBatch.Draw(fullTarget, new Rectangle(0, 0, width, height), new Rectangle(0, 0, width, height), Color.White);
        Main.spriteBatch.End();
        using var enlarged = new RenderTarget2D(device, width * scale, height * scale, false,
            SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
        device.SetRenderTarget(enlarged);
        device.Clear(Color.Black);
        Main.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp,
            DepthStencilState.None, RasterizerState.CullCounterClockwise);
        Main.spriteBatch.Draw(native, new Rectangle(0, 0, width * scale, height * scale), Color.White);
        Main.spriteBatch.End();
        device.SetRenderTarget(null);
        using var stream = File.Create(Path.Combine(OutputDirectory, "native-t3-t4-accessory-layers.png"));
        enlarged.SaveAsPng(stream, width * scale, height * scale);
        using var nativeStream = File.Create(Path.Combine(OutputDirectory, "native-t3-t4-accessory-layers-1x.png"));
        native.SaveAsPng(nativeStream, width, height);
    }

    private void RenderShieldCobaltComparison()
    {
        GraphicsDevice device = Main.graphics.GraphicsDevice;
        const int width = 700;
        const int height = 510;
        const int scale = 3;
        int renderWidth = Math.Max(width, Main.screenWidth);
        int renderHeight = Math.Max(height, Main.screenHeight);
        using var fullTarget = new RenderTarget2D(device, renderWidth, renderHeight, false,
            SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
        int localPlayerIndex = Math.Clamp(Main.myPlayer, 0, Main.player.Length - 1);
        Player previousLocalPlayer = Main.player[localPlayerIndex];
        bool previousMenu = Main.gameMenu;
        bool previousLighting = ForcePreviewLighting;
        float previousUiScale = Main.UIScale;
        Vector2 previousGameZoom = Main.GameViewMatrix.Zoom;
        try
        {
            Main.gameMenu = false;
            ForcePreviewLighting = true;
            Main.UIScale = 1f;
            Main.GameViewMatrix.Zoom = Vector2.One;
            device.SetRenderTarget(fullTarget);
            device.Clear(new Color(28, 31, 39));
            Main.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp,
                DepthStencilState.None, RasterizerState.CullCounterClockwise);
            Item vanillaShield = null;
            foreach (int id in new[] { ItemID.CobaltShield, ItemID.PaladinsShield, ItemID.AnkhShield, ItemID.ObsidianShield })
            {
                Item candidate = new();
                candidate.SetDefaults(id);
                if (candidate.shieldSlot >= 0)
                {
                    vanillaShield = candidate;
                    break;
                }
            }
            Require(vanillaShield is not null, "no vanilla shield-slot item available for native visual comparison");
            var columns = new (string Name, Item Item)[]
            {
                ("iron", MakeAccessory(ModContent.ItemType<IronShield>())),
                ("knight", MakeAccessory(ModContent.ItemType<KnightBulwark>())),
                ("templar", MakeAccessory(ModContent.ItemType<TemplarShield>())),
                ("soulforged", MakeAccessory(ModContent.ItemType<SoulforgedShield>())),
                ("vanilla", vanillaShield)
            };
            var frames = new (string Name, int Frame, int Direction)[]
            {
                ("idle", 0, 1), ("walk-right", 3, 1), ("walk-left", 3, -1),
                ("raised-right", 5, 1), ("raised-left", 5, -1)
            };
            string log = Path.Combine(OutputDirectory, "native-shield-cobalt-comparison.log");
            File.WriteAllText(log, $"Native wearable shield comparison; vanilla item={vanillaShield.Name} type={vanillaShield.type} slot={vanillaShield.shieldSlot}. Columns T1 Iron/T2 Knight/T3 Templar/T4 Soulforged/vanilla; rows idle, walk-right, walk-left, raised-right, raised-left.\n");
            CaptureAccessoryDraws = true;
            for (int row = 0; row < frames.Length; row++)
            for (int col = 0; col < columns.Length; col++)
            {
                var pose = frames[row];
                var entry = columns[col];
                Player actor = CreateRobePreviewPlayer(
                    ModContent.ItemType<ClothHood>(), ModContent.ItemType<ClothRobe>(),
                    ModContent.ItemType<ClothLeggings>(), pose.Direction, pose.Frame);
                actor.whoAmI = localPlayerIndex;
                actor.Male = true;
                actor.bodyFrame = new Rectangle(0, pose.Frame * 56, 40, 56);
                actor.legFrame = new Rectangle(0, pose.Frame * 56, 40, 56);
                actor.armor[3] = entry.Item.Clone();
                actor.hideVisibleAccessory[0] = false;
                actor.UpdateVisibleAccessories(actor.armor[3], invisible: false, slot: 0, modded: false);
                Main.player[localPlayerIndex] = actor;
                AccessoryCaptureLabel = $"shield-compare/{entry.Name}/{pose.Name}";
                AccessoryCaptureSearch = entry.Name == "vanilla" ? "Acc_Shield" :
                    entry.Name == "iron" ? "IronShield_Shield" : entry.Name == "knight" ? "KnightBulwark_Shield" :
                    entry.Name == "templar" ? "TemplarShield_Shield" : "SoulforgedShield_Shield";
                AccessoryCaptureDrawCount = 0;
                AccessorySourceInsideTexture = false;
                AccessorySourceAlphaPixels = 0;
                DrawPreviewPlayer(actor, 60 + col * 140, 35 + row * 90, 1f);
                Require(AccessoryCaptureDrawCount > 0 && AccessorySourceInsideTexture && AccessorySourceAlphaPixels > 0,
                    $"native shield comparison missing valid draw for {AccessoryCaptureLabel}");
                Require(AccessorySourceFrameY == pose.Frame * 56,
                    $"native shield comparison {AccessoryCaptureLabel} sampled frame Y={AccessorySourceFrameY}, expected {pose.Frame * 56}");
                File.AppendAllText(log,
                    $"case={AccessoryCaptureLabel} bodyFrame={actor.bodyFrame} direction={actor.direction} " +
                    $"sourceFrameY={AccessorySourceFrameY} sourceAlpha={AccessorySourceAlphaPixels} sourceInBounds={AccessorySourceInsideTexture}\n");
            }
            Main.spriteBatch.End();
            CaptureAccessoryDraws = false;
            AccessoryCaptureLabel = "";
            AccessoryCaptureSearch = "";
        }
        finally
        {
            CaptureAccessoryDraws = false;
            AccessoryCaptureLabel = "";
            AccessoryCaptureSearch = "";
            Main.player[localPlayerIndex] = previousLocalPlayer;
            ForcePreviewLighting = previousLighting;
            Main.gameMenu = previousMenu;
            Main.UIScale = previousUiScale;
            Main.GameViewMatrix.Zoom = previousGameZoom;
        }
        device.SetRenderTarget(null);
        using var enlarged = new RenderTarget2D(device, width * scale, height * scale, false,
            SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
        device.SetRenderTarget(enlarged);
        device.Clear(Color.Black);
        Main.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp,
            DepthStencilState.None, RasterizerState.CullCounterClockwise);
        Main.spriteBatch.Draw(fullTarget, new Rectangle(0, 0, width * scale, height * scale),
            new Rectangle(0, 0, width, height), Color.White);
        Main.spriteBatch.End();
        device.SetRenderTarget(null);
        using var stream = File.Create(Path.Combine(OutputDirectory, "native-shield-cobalt-comparison.png"));
        enlarged.SaveAsPng(stream, width * scale, height * scale);
    }

    private static Item MakeAccessory(int itemType)
    {
        Item item = new();
        item.SetDefaults(itemType);
        return item;
    }

    private void RenderRobeVisibilityABPreviews()
    {
        GraphicsDevice device = Main.graphics.GraphicsDevice;
        const int width = 440;
        const int height = 590;
        const int scale = 3;
        int renderWidth = Math.Max(width, Main.screenWidth);
        int renderHeight = Math.Max(height, Main.screenHeight);
        using var fullTarget = new RenderTarget2D(device, renderWidth, renderHeight, false,
            SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
        int localPlayerIndex = Math.Clamp(Main.myPlayer, 0, Main.player.Length - 1);
        Player previousLocalPlayer = Main.player[localPlayerIndex];
        bool previousMenu = Main.gameMenu;
        bool previousLighting = ForcePreviewLighting;
        float previousUiScale = Main.UIScale;
        Vector2 previousGameZoom = Main.GameViewMatrix.Zoom;
        string log = Path.Combine(OutputDirectory, "native-robe-hides-bottom-skin-ab.log");
        File.WriteAllText(log,
            "Same cloned player equipment, skin, and native pose rendered with HidesBottomSkin false/true; rows are idle, walk-right, walk-left, jump-right, jump-left.\n");
        try
        {
            Main.gameMenu = false;
            ForcePreviewLighting = true;
            Main.UIScale = 1f;
            Main.GameViewMatrix.Zoom = Vector2.One;
            CaptureRobeLegDraws = true;
            device.SetRenderTarget(fullTarget);
            device.Clear(new Color(28, 31, 39));
            Main.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp,
                DepthStencilState.None, RasterizerState.CullCounterClockwise);
            var suits = new (string Name, int Head, int Body, int Legs)[]
            {
                ("cloth", ModContent.ItemType<ClothHood>(), ModContent.ItemType<ClothRobe>(), ModContent.ItemType<ClothLeggings>()),
                ("energy", ModContent.ItemType<EnergyHood>(), ModContent.ItemType<EnergyRobe>(), ModContent.ItemType<EnergyLeggings>())
            };
            var poses = new (string Name, int Direction, Vector2 Velocity, bool Jumping)[]
            {
                ("idle", 1, Vector2.Zero, false),
                ("walk-right", 1, new Vector2(2.2f, 0f), false),
                ("walk-left", -1, new Vector2(-2.2f, 0f), false),
                ("jump-right", 1, new Vector2(1.5f, -6f), true),
                ("jump-left", -1, new Vector2(-1.5f, -6f), true)
            };
            for (int suitIndex = 0; suitIndex < suits.Length; suitIndex++)
            {
                var suit = suits[suitIndex];
                Item robe = new();
                robe.SetDefaults(suit.Body);
                bool previousFlag = ArmorIDs.Body.Sets.HidesBottomSkin[robe.bodySlot];
                for (int poseIndex = 0; poseIndex < poses.Length; poseIndex++)
                {
                    var pose = poses[poseIndex];
                    for (int variant = 0; variant < 2; variant++)
                    {
                        bool hidesSkin = variant == 1;
                        ArmorIDs.Body.Sets.HidesBottomSkin[robe.bodySlot] = hidesSkin;
                        Player actor = CreateRobePreviewPlayer(suit.Head, suit.Body, suit.Legs,
                            pose.Direction, poseIndex == 0 ? 0 : -1, pose.Velocity, pose.Jumping);
                        actor.whoAmI = localPlayerIndex;
                        Main.player[localPlayerIndex] = actor;
                        RobeLegCaptureLabel = $"{suit.Name}/{pose.Name}/hidesBottomSkin-{hidesSkin}";
                        RobeLegCaptureDrawCount = 0;
                        RobeLegSourceAlphaPixels = 0;
                        RobeLegSourceInsideTexture = false;
                        int x = variant == 0 ? 125 : 285;
                        int y = 35 + suitIndex * 275 + poseIndex * 52;
                        DrawPreviewPlayer(actor, x, y, 1f);
                        Require(RobeLegCaptureDrawCount > 0 && RobeLegSourceInsideTexture && RobeLegSourceAlphaPixels > 0,
                            $"{RobeLegCaptureLabel} did not draw a valid skirt frame");
                        File.AppendAllText(log,
                            $"case={RobeLegCaptureLabel} bodyFrame={actor.bodyFrame} legFrame={actor.legFrame} " +
                            $"direction={actor.direction} velocity={actor.velocity} jumping={pose.Jumping} " +
                            $"nativeRobeDraws={RobeLegCaptureDrawCount} sourceAlpha={RobeLegSourceAlphaPixels}\n");
                    }
                }
                ArmorIDs.Body.Sets.HidesBottomSkin[robe.bodySlot] = previousFlag;
            }
            Main.spriteBatch.End();
            CaptureRobeLegDraws = false;
            RobeLegCaptureLabel = "";
        }
        finally
        {
            CaptureRobeLegDraws = false;
            RobeLegCaptureLabel = "";
            // Production robe visibility remains enabled after smoke.
            foreach (int robeType in new[] { ModContent.ItemType<ClothRobe>(), ModContent.ItemType<EnergyRobe>() })
            {
                Item robe = new();
                robe.SetDefaults(robeType);
                ArmorIDs.Body.Sets.HidesBottomSkin[robe.bodySlot] = true;
            }
            Main.player[localPlayerIndex] = previousLocalPlayer;
            ForcePreviewLighting = previousLighting;
            Main.gameMenu = previousMenu;
            Main.UIScale = previousUiScale;
            Main.GameViewMatrix.Zoom = previousGameZoom;
        }
        device.SetRenderTarget(null);
        using var enlarged = new RenderTarget2D(device, width * scale, height * scale, false,
            SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
        device.SetRenderTarget(enlarged);
        device.Clear(Color.Black);
        Main.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp,
            DepthStencilState.None, RasterizerState.CullCounterClockwise);
        Main.spriteBatch.Draw(fullTarget, new Rectangle(0, 0, width * scale, height * scale),
            new Rectangle(0, 0, width, height), Color.White);
        Main.spriteBatch.End();
        device.SetRenderTarget(null);
        using var stream = File.Create(Path.Combine(OutputDirectory, "native-robe-hides-bottom-skin-ab.png"));
        enlarged.SaveAsPng(stream, width * scale, height * scale);
    }

    private static Player CreateRobePreviewPlayer(int head, int body, int legs, int direction,
        int explicitLegFrame = -1, Vector2 velocity = default, bool jumping = false)
    {
        Player player = Main.LocalPlayer.SerializedClone();
        player.active = true;
        player.whoAmI = Main.myPlayer;
        player.position = Vector2.Zero;
        player.velocity = velocity;
        player.oldVelocity = velocity;
        player.fullRotation = 0f;
        player.direction = direction;
        player.Male = true;
        player.head = 0;
        player.body = 0;
        player.legs = 0;
        for (int i = 0; i < player.armor.Length; i++)
            player.armor[i] = new Item();
        player.armor[0].SetDefaults(head);
        player.armor[1].SetDefaults(body);
        player.armor[2].SetDefaults(legs);
        // Air uses the engine's bare-head slot 0; Item.headSlot itself is -1.
        player.head = head == ItemID.None ? 0 : player.armor[0].headSlot;
        player.body = player.armor[1].bodySlot;
        string robeName = body == ModContent.ItemType<ClothRobe>() ? nameof(ClothRobe) : nameof(EnergyRobe);
        player.legs = EquipLoader.GetEquipSlot(ModContent.GetInstance<ClothRobe>().Mod, robeName, EquipType.Legs);
        for (int i = 0; i < player.inventory.Length; i++)
            player.inventory[i] = new Item();
        player.selectedItem = 0;
        player.lastVisualizedSelectedItem = new Item();
        player.itemAnimation = player.itemTime = 0;
        player.itemRotation = 0f;
        player.hair = 0;
        player.hairColor = new Color(88, 58, 35);
        player.skinColor = new Color(238, 190, 150);
        player.eyeColor = new Color(55, 95, 145);
        player.jump = jumping ? 1 : 0;
        player.controlJump = jumping;
        player.bodyFrame = new Rectangle(0, 0, 40, 56);
        player.legFrame = new Rectangle(0, Math.Max(0, explicitLegFrame) * 56, 40, 56);
        player.isDisplayDollOrInanimate = false;
        if (explicitLegFrame < 0)
            player.PlayerFrame();
        else
            player.bodyFrame = new Rectangle(0, 0, 40, 56);
        Require((head == ItemID.None || player.head >= 0) && player.body >= 0 && player.legs >= 0,
            $"robe preview missing equip slots: head={player.head} body={player.body} legs={player.legs}");
        return player;
    }

    /// <summary>
    /// Draws every native 56px robe-leg frame and native PlayerFrame movement
    /// poses. This remains smoke-only and uses PlayerRenderer, never a pasted
    /// robe texture overlay.
    /// </summary>
    private void RenderRobeNativePosePreviews()
    {
        GraphicsDevice device = Main.graphics.GraphicsDevice;
        const int width = 800;
        const int height = 260;
        const int scale = 2;
        int renderWidth = Math.Max(width, Main.screenWidth);
        int renderHeight = Math.Max(height, Main.screenHeight);
        int localPlayerIndex = Math.Clamp(Main.myPlayer, 0, Main.player.Length - 1);
        Player previousLocalPlayer = Main.player[localPlayerIndex];
        bool previousMenu = Main.gameMenu;
        bool previousLighting = ForcePreviewLighting;
        float previousUiScale = Main.UIScale;
        Vector2 previousGameZoom = Main.GameViewMatrix.Zoom;
        string log = Path.Combine(OutputDirectory, "native-robe-leg-frames.log");
        File.WriteAllText(log,
            "Robe legs use the native EquipType.Legs sheet through body SetMatch(robes=true). " +
            "Frame contact sheet covers all 20 40x56 source cells; movement page calls PlayerFrame for left/right walk and jump poses.\n");
        bool spriteBatchBegun = false;
        using var fullTarget = new RenderTarget2D(device, renderWidth, renderHeight, false,
            SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
        try
        {
            Main.gameMenu = false;
            ForcePreviewLighting = true;
            Main.UIScale = 1f;
            Main.GameViewMatrix.Zoom = Vector2.One;
            CaptureRobeLegDraws = true;
            device.SetRenderTarget(fullTarget);
            device.Clear(new Color(28, 31, 39));
            Main.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp,
                DepthStencilState.None, RasterizerState.CullCounterClockwise);
            spriteBatchBegun = true;

            var suits = new (string Name, int Head, int Body, int Legs)[]
            {
                ("cloth", ModContent.ItemType<ClothHood>(), ModContent.ItemType<ClothRobe>(),
                    ModContent.ItemType<ClothLeggings>()),
                ("energy", ModContent.ItemType<EnergyHood>(), ModContent.ItemType<EnergyRobe>(),
                    ModContent.ItemType<EnergyLeggings>())
            };
            Item clothRobe = new();
            clothRobe.SetDefaults(suits[0].Body);
            Item energyRobe = new();
            energyRobe.SetDefaults(suits[1].Body);
            Require(ArmorIDs.Body.Sets.HidesBottomSkin[clothRobe.bodySlot] &&
                    ArmorIDs.Body.Sets.HidesBottomSkin[energyRobe.bodySlot],
                "robe body equipment must hide bare leg skin behind the skirt sheets");

            for (int suitIndex = 0; suitIndex < suits.Length; suitIndex++)
            for (int frame = 0; frame < 20; frame++)
            {
                var suit = suits[suitIndex];
                Player actor = CreatePreviewPlayer(suit.Head, suit.Body, suit.Legs);
                actor.whoAmI = localPlayerIndex;
                actor.isDisplayDollOrInanimate = false;
                actor.direction = frame % 2 == 0 ? 1 : -1;
                actor.bodyFrame = new Rectangle(0, 0, 40, 56);
                actor.legFrame = new Rectangle(0, frame * 56, 40, 56);
                Main.player[localPlayerIndex] = actor;
                RobeLegCaptureLabel = $"{suit.Name}/frame-{frame:D2}/direction-{actor.direction}";
                RobeLegCaptureDrawCount = 0;
                RobeLegSourceAlphaPixels = 0;
                RobeLegSourceInsideTexture = false;
                DrawPreviewPlayer(actor, 25 + (frame % 10) * 76,
                    18 + suitIndex * 115 + (frame / 10) * 54, 1f);
                Require(RobeLegCaptureDrawCount > 0 && RobeLegSourceInsideTexture && RobeLegSourceAlphaPixels > 0,
                    $"{RobeLegCaptureLabel} did not draw a valid native robe leg frame");
            }

            Main.spriteBatch.End();
            spriteBatchBegun = false;
            using var native = new RenderTarget2D(device, width, height, false,
                SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
            device.SetRenderTarget(native);
            device.Clear(new Color(28, 31, 39));
            Main.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp,
                DepthStencilState.None, RasterizerState.CullCounterClockwise);
            spriteBatchBegun = true;
            Main.spriteBatch.Draw(fullTarget, new Rectangle(0, 0, width, height),
                new Rectangle(0, 0, width, height), Color.White);
            Main.spriteBatch.End();
            spriteBatchBegun = false;
            using var enlarged = new RenderTarget2D(device, width * scale, height * scale, false,
                SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
            device.SetRenderTarget(enlarged);
            device.Clear(Color.Black);
            Main.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp,
                DepthStencilState.None, RasterizerState.CullCounterClockwise);
            spriteBatchBegun = true;
            Main.spriteBatch.Draw(native, new Rectangle(0, 0, width * scale, height * scale), Color.White);
            Main.spriteBatch.End();
            spriteBatchBegun = false;
            device.SetRenderTarget(null);
            using (var stream = File.Create(Path.Combine(OutputDirectory, "native-robe-leg-frames.png")))
                enlarged.SaveAsPng(stream, width * scale, height * scale);

            // The second sheet captures real movement inputs after PlayerFrame
            // computes the current torso and leg frames.
            using var movementTarget = new RenderTarget2D(device, renderWidth, renderHeight, false,
                SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
            device.SetRenderTarget(movementTarget);
            device.Clear(new Color(28, 31, 39));
            Main.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp,
                DepthStencilState.None, RasterizerState.CullCounterClockwise);
            spriteBatchBegun = true;
            string[] movementNames = { "walk-right", "walk-left", "jump-right", "jump-left" };
            for (int suitIndex = 0; suitIndex < suits.Length; suitIndex++)
            for (int movement = 0; movement < movementNames.Length; movement++)
            {
                var suit = suits[suitIndex];
                int direction = movement % 2 == 0 ? 1 : -1;
                bool jumping = movement >= 2;
                Player actor = CreatePreviewPlayer(suit.Head, suit.Body, suit.Legs);
                actor.whoAmI = localPlayerIndex;
                actor.isDisplayDollOrInanimate = false;
                actor.direction = direction;
                actor.velocity = new Vector2(direction * 2.2f, jumping ? -6f : 0f);
                actor.oldVelocity = actor.velocity;
                actor.controlJump = jumping;
                actor.jump = jumping ? 1 : 0;
                actor.bodyFrame = new Rectangle(0, 0, 40, 56);
                actor.legFrame = new Rectangle(0, 0, 40, 56);
                actor.PlayerFrame();
                Main.player[localPlayerIndex] = actor;
                RobeLegCaptureLabel = $"{suit.Name}/{movementNames[movement]}/leg-frame-{actor.legFrame.Y / 56}";
                RobeLegCaptureDrawCount = 0;
                RobeLegSourceInsideTexture = false;
                RobeLegSourceAlphaPixels = 0;
                DrawPreviewPlayer(actor, 90 + movement * 175, 28 + suitIndex * 110, 1f);
                Require(RobeLegCaptureDrawCount > 0 && RobeLegSourceInsideTexture && RobeLegSourceAlphaPixels > 0,
                    $"{RobeLegCaptureLabel} did not draw a valid native movement frame");
                File.AppendAllText(log,
                    $"movement-pose={RobeLegCaptureLabel} bodyFrame={actor.bodyFrame} legFrame={actor.legFrame} " +
                    $"velocity={actor.velocity} direction={actor.direction} jump={actor.jump}\n");
            }
            Main.spriteBatch.End();
            spriteBatchBegun = false;
            spriteBatchBegun = false;
            using var movementNative = new RenderTarget2D(device, width, height, false,
                SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
            device.SetRenderTarget(movementNative);
            device.Clear(new Color(28, 31, 39));
            Main.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp,
                DepthStencilState.None, RasterizerState.CullCounterClockwise);
            spriteBatchBegun = true;
            Main.spriteBatch.Draw(movementTarget, new Rectangle(0, 0, width, height),
                new Rectangle(0, 0, width, height), Color.White);
            Main.spriteBatch.End();
            spriteBatchBegun = false;
            using var movementEnlarged = new RenderTarget2D(device, width * scale, height * scale, false,
                SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
            device.SetRenderTarget(movementEnlarged);
            device.Clear(Color.Black);
            Main.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp,
                DepthStencilState.None, RasterizerState.CullCounterClockwise);
            Main.spriteBatch.Draw(movementNative, new Rectangle(0, 0, width * scale, height * scale), Color.White);
            Main.spriteBatch.End();
            device.SetRenderTarget(null);
            using (var stream = File.Create(Path.Combine(OutputDirectory, "native-robe-movement.png")))
                movementEnlarged.SaveAsPng(stream, width * scale, height * scale);
        }
        finally
        {
            CaptureRobeLegDraws = false;
            RobeLegCaptureLabel = "";
            Main.player[localPlayerIndex] = previousLocalPlayer;
            ForcePreviewLighting = previousLighting;
            Main.gameMenu = previousMenu;
            Main.UIScale = previousUiScale;
            Main.GameViewMatrix.Zoom = previousGameZoom;
            if (spriteBatchBegun)
                Main.spriteBatch.End();
            device.SetRenderTarget(null);
        }
    }

    private static Player CreateHeadwearPlayer(int headItem, int direction)
    {
        Player player = Main.LocalPlayer.SerializedClone();
        player.active = true;
        player.whoAmI = Main.myPlayer;
        player.position = Vector2.Zero;
        player.velocity = Vector2.Zero;
        player.fullRotation = 0f;
        player.direction = direction;
        player.Male = true;
        for (int i = 0; i < player.armor.Length; i++)
            player.armor[i] = new Item();
        player.armor[0].SetDefaults(headItem);
        player.armor[1].SetDefaults(ModContent.ItemType<ClothRobe>());
        player.armor[2].SetDefaults(ModContent.ItemType<ClothLeggings>());
        player.head = headItem == 0 ? 0 : player.armor[0].headSlot;
        player.body = player.armor[1].bodySlot;
        player.legs = EquipLoader.GetEquipSlot(ModContent.GetInstance<ClothRobe>().Mod,
            nameof(ClothRobe), EquipType.Legs);
        for (int i = 0; i < player.inventory.Length; i++)
            player.inventory[i] = new Item();
        player.selectedItem = 0;
        player.lastVisualizedSelectedItem = new Item();
        player.itemAnimation = player.itemTime = 0;
        player.itemRotation = 0f;
        player.hair = 0;
        player.hairColor = new Color(88, 58, 35);
        player.skinColor = new Color(238, 190, 150);
        player.eyeColor = new Color(55, 95, 145);
        player.bodyFrame = new Rectangle(0, 0, 40, 56);
        player.legFrame = new Rectangle(0, 0, 40, 56);
        player.isDisplayDollOrInanimate = false;
        return player;
    }

    private void RenderActionPreviews(bool tier3T4Only = false)
    {
        GraphicsDevice device = Main.graphics.GraphicsDevice;
        const int width = 600;
        int height = tier3T4Only ? 380 : 620;
        const int scale = 3;
        int renderWidth = Math.Max(width, Main.screenWidth);
        int renderHeight = Math.Max(height, Main.screenHeight);
        using var fullTarget = new RenderTarget2D(device, renderWidth, renderHeight, false,
            SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
        device.SetRenderTarget(fullTarget);
        device.Clear(new Color(28, 31, 39));
        Main.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp,
            DepthStencilState.None, RasterizerState.CullCounterClockwise);
        int localPlayerIndex = Math.Clamp(Main.myPlayer, 0, Main.player.Length - 1);
        Player previousLocalPlayer = Main.player[localPlayerIndex];
        bool previousMenu = Main.gameMenu;
        bool previousForceLighting = ForcePreviewLighting;
        float previousUiScale = Main.UIScale;
        Vector2 previousGameZoom = Main.GameViewMatrix.Zoom;
        int previousMouseX = Main.mouseX;
        int previousMouseY = Main.mouseY;
        Projectile[] previousProjectiles = Main.projectile;
        string logName = tier3T4Only ? "native-t3-t4-staff-directions.log" : "native-mage-held-directions.log";
        string imageName = tier3T4Only ? "native-t3-t4-staff-directions.png" : "milkcraft-mage-held-directions.png";
        try
        {
            // Run ordinary ItemCheck/PlayerFrame and PlayerRenderer on cloned
            // actors. The only draw adjustment is white diagnostic lighting.
            Main.gameMenu = false;
            ForcePreviewLighting = true;
            Main.UIScale = 1f;
            Main.GameViewMatrix.Zoom = Vector2.One;
            Main.projectile = (Projectile[])previousProjectiles.Clone();
            NativeHeldDrawCount = 0;
            int[] directions = { 1, -1, 1, -1 };
            float[] angles = { 0f, MathHelper.Pi, -MathHelper.PiOver4, -3f * MathHelper.PiOver4 };
            File.WriteAllText(Path.Combine(OutputDirectory, logName), tier3T4Only
                ? "Native ItemCheck/PlayerFrame/PlayerRenderer; T3/T4 male-reference staff, horizontal and raised aim, both directions.\n"
                : "Native ItemCheck/PlayerFrame/PlayerRenderer; male reference model; T1/T2/T3/T4 mages, horizontal and raised aim, both directions.\n");
            var mageSets = new (string Name, int Head, int Body, int Legs, int Staff)[]
            {
                ("cloth", ModContent.ItemType<ClothHood>(), ModContent.ItemType<ClothRobe>(), ModContent.ItemType<ClothLeggings>(), ModContent.ItemType<IronStaff>()),
                ("energy", ModContent.ItemType<EnergyHood>(), ModContent.ItemType<EnergyRobe>(), ModContent.ItemType<EnergyLeggings>(), ModContent.ItemType<EnergyStaff>()),
                ("fallen", ModContent.ItemType<FallenHood>(), ModContent.ItemType<FallenRobe>(), ModContent.ItemType<FallenLeggings>(), ModContent.ItemType<FallenStaff>()),
                ("dark", ModContent.ItemType<DarkHood>(), ModContent.ItemType<DarkRobe>(), ModContent.ItemType<DarkLeggings>(), ModContent.ItemType<DarkStaff>())
            };
            if (tier3T4Only)
                mageSets = mageSets.Where(suit => suit.Name is "fallen" or "dark").ToArray();
            for (int row = 0; row < mageSets.Length; row++)
            for (int col = 0; col < 4; col++)
            {
                var suit = mageSets[row];
                NativePoseLabel = $"{suit.Name}/{col switch { 0 => "right", 1 => "left", 2 => "right-up", _ => "left-up" }}/male";
                Player actor = CreateActionPlayer(suit.Head, suit.Body, suit.Legs, suit.Staff, directions[col],
                    angles[col], male: true, pose: 0);
                actor.whoAmI = localPlayerIndex;
                Main.player[localPlayerIndex] = actor;
                File.AppendAllText(Path.Combine(OutputDirectory, logName),
                    $"case={NativePoseLabel}\n");
                DrawNativeItemCheckPreview(actor, 55 + col * 125, 55 + row * 145, 1f, angles[col]);
            }
            int expectedDraws = mageSets.Length * 4;
            Require(NativeHeldDrawCount == expectedDraws,
                $"expected native held-item draws for {expectedDraws} mage actors, found {NativeHeldDrawCount}");
            File.AppendAllText(Path.Combine(OutputDirectory, logName),
                $"native-held-draws={NativeHeldDrawCount}/{expectedDraws}\n");
        }
        finally
        {
            NativePoseLabel = "";
            Main.player[localPlayerIndex] = previousLocalPlayer;
            ForcePreviewLighting = previousForceLighting;
            Main.gameMenu = previousMenu;
            Main.UIScale = previousUiScale;
            Main.GameViewMatrix.Zoom = previousGameZoom;
            Main.projectile = previousProjectiles;
            Main.mouseX = previousMouseX;
            Main.mouseY = previousMouseY;
            Main.spriteBatch.End();
        }

        SavePreviewCrop(fullTarget, width, height, scale, null, imageName);
    }

    private void RenderTier3T4HeadwearReference()
    {
        GraphicsDevice device = Main.graphics.GraphicsDevice;
        const int width = 600;
        const int height = 150;
        const int scale = 3;
        using var fullTarget = new RenderTarget2D(device, Main.screenWidth, Main.screenHeight, false,
            SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
        device.SetRenderTarget(fullTarget);
        device.Clear(new Color(28, 31, 39));
        Main.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp,
            DepthStencilState.None, RasterizerState.CullCounterClockwise);

        int localPlayerIndex = Math.Clamp(Main.myPlayer, 0, Main.player.Length - 1);
        Player previousPlayer = Main.player[localPlayerIndex];
        bool previousMenu = Main.gameMenu;
        bool previousLighting = ForcePreviewLighting;
        bool previousCapture = CaptureHeadwearNativeData;
        float previousUiScale = Main.UIScale;
        Vector2 previousZoom = Main.GameViewMatrix.Zoom;
        int previousMouseX = Main.mouseX;
        int previousMouseY = Main.mouseY;
        Projectile[] previousProjectiles = Main.projectile;
        try
        {
            Main.gameMenu = false;
            ForcePreviewLighting = true;
            CaptureHeadwearNativeData = true;
            Main.UIScale = 1f;
            Main.GameViewMatrix.Zoom = Vector2.One;
            Main.projectile = (Projectile[])previousProjectiles.Clone();
            HeadwearCaptureDrawCount = 0;
            string log = Path.Combine(OutputDirectory, "native-t3-t4-headwear-reference.log");
            File.WriteAllText(log,
                "Male idle head comparison: same body, legs, skin, hair and pose; naked head, Nightmare hood, Fallen hood. " +
                "DrawData position/origin/source and Player texture atlases are captured from native PlayerRenderer.\n");
            var cases = new (string Label, int Head)[]
            {
                ("bare/right", 0),
                ("nightmare/male", ModContent.ItemType<NightmareHood>()),
                ("fallen/male", ModContent.ItemType<FallenHood>())
            };
            int[] columns = { 55, 235, 415 };
            for (int i = 0; i < cases.Length; i++)
            {
                var spec = cases[i];
                Player actor = CreatePreviewPlayer(spec.Head, ModContent.ItemType<FallenRobe>(),
                    ModContent.ItemType<FallenLeggings>());
                actor.whoAmI = localPlayerIndex;
                actor.Male = true;
                actor.direction = 1;
                actor.bodyFrame = new Rectangle(0, 0, 40, 56);
                actor.legFrame = new Rectangle(0, 0, 40, 56);
                Main.player[localPlayerIndex] = actor;
                HeadwearCaptureLabel = spec.Label;
                NativePoseLabel = $"head-reference/{spec.Label}";
                File.AppendAllText(log, $"case={spec.Label} headSlot={actor.head} headItem={spec.Head}\n");
                DrawPreviewPlayer(actor, columns[i], 40, 1.5f);
            }
            Require(HeadwearCaptureDrawCount == cases.Length,
                $"expected {cases.Length} head-reference PlayerRenderer actors, found {HeadwearCaptureDrawCount}");
            File.AppendAllText(log, $"player-renderer-actors={HeadwearCaptureDrawCount}/{cases.Length}\n");
        }
        finally
        {
            NativePoseLabel = "";
            HeadwearCaptureLabel = "";
            CaptureHeadwearNativeData = previousCapture;
            Main.player[localPlayerIndex] = previousPlayer;
            Main.projectile = previousProjectiles;
            ForcePreviewLighting = previousLighting;
            Main.gameMenu = previousMenu;
            Main.UIScale = previousUiScale;
            Main.GameViewMatrix.Zoom = previousZoom;
            Main.mouseX = previousMouseX;
            Main.mouseY = previousMouseY;
            Main.spriteBatch.End();
        }

        using var native = new RenderTarget2D(device, width, height, false, SurfaceFormat.Color,
            DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
        device.SetRenderTarget(native);
        device.Clear(new Color(28, 31, 39));
        Main.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp,
            DepthStencilState.None, RasterizerState.CullCounterClockwise);
        Main.spriteBatch.Draw(fullTarget, new Rectangle(0, 0, width, height),
            new Rectangle(0, 0, width, height), Color.White);
        Main.spriteBatch.End();
        using var enlarged = new RenderTarget2D(device, width * scale, height * scale, false,
            SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
        device.SetRenderTarget(enlarged);
        device.Clear(Color.Black);
        Main.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp,
            DepthStencilState.None, RasterizerState.CullCounterClockwise);
        Main.spriteBatch.Draw(native, new Rectangle(0, 0, width * scale, height * scale), Color.White);
        Main.spriteBatch.End();
        device.SetRenderTarget(null);
        using var stream = File.Create(Path.Combine(OutputDirectory, "native-t3-t4-headwear-reference.png"));
        enlarged.SaveAsPng(stream, width * scale, height * scale);
    }

    /// <summary>
    /// Crops and nearest-neighbor enlarges a full-screen native render target
    /// through CPU buffers. Avoiding a smaller intermediate RenderTarget keeps
    /// tModLoader's cached camera/viewport projection stable for later pages.
    /// </summary>
    private void SavePreviewCrop(RenderTarget2D source, int width, int height, int scale,
        string nativeFileName, string enlargedFileName)
    {
        GraphicsDevice device = source.GraphicsDevice;
        device.SetRenderTarget(null);
        var crop = new Color[width * height];
        source.GetData(0, new Rectangle(0, 0, width, height), crop, 0, crop.Length);
        using var cropped = new Texture2D(device, width, height, false, SurfaceFormat.Color);
        cropped.SetData(crop);
        if (nativeFileName is not null)
        {
            using FileStream nativeStream = File.Create(Path.Combine(OutputDirectory, nativeFileName));
            cropped.SaveAsPng(nativeStream, width, height);
        }
        var enlargedPixels = new Color[width * scale * height * scale];
        int enlargedWidth = width * scale;
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            Color color = crop[y * width + x];
            int targetX = x * scale;
            int targetY = y * scale;
            for (int dy = 0; dy < scale; dy++)
            for (int dx = 0; dx < scale; dx++)
                enlargedPixels[(targetY + dy) * enlargedWidth + targetX + dx] = color;
        }
        using var enlarged = new Texture2D(device, enlargedWidth, height * scale, false, SurfaceFormat.Color);
        enlarged.SetData(enlargedPixels);
        using FileStream output = File.Create(Path.Combine(OutputDirectory, enlargedFileName));
        enlarged.SaveAsPng(output, enlargedWidth, height * scale);
    }

    internal static void DrawPreviewPlayer(Player player, int x, int y, float scale = 1f)
    {
        Vector2 position = new Vector2(x, y) + Main.screenPosition;
        player.position = position;
        if (player.itemAnimation > 0 && player.selectedItem >= 0 && player.selectedItem < player.inventory.Length)
        {
            // The action fixtures are positioned after their arm pose is set.
            // A synthetic Player has no loaded-world mounted-center caches,
            // so GetFrontHandPosition can return a stale/negative coordinate.
            // Anchor this diagnostic directly to its native torso center.
            player.itemLocation = player.position + new Vector2(
                player.width * 0.5f + player.direction * 6f,
                player.height * 0.5f - 8f);
            float aim = player.itemRotation + (player.direction < 0 ? MathHelper.Pi : 0f);
            Vector2 screenCenter = player.position - Main.screenPosition + new Vector2(player.width * 0.5f, player.height * 0.5f);
            Vector2 target = screenCenter + new Vector2((float)Math.Cos(aim), (float)Math.Sin(aim)) * 120f;
            Main.mouseX = (int)target.X;
            Main.mouseY = (int)target.Y;
            // PlayerFrame derives composite-arm/body frames from the held
            // item's use style and current mouse aim. Directly setting the
            // body/leg frame bypasses those native pose calculations.
            player.PlayerFrame();
            // The synthetic fixture supplies the native composite-arm use pose
            // for shoot-style items. Swing-style swords keep the pose produced
            // by PlayerFrame instead of being forced into a staff pose.
            if (player.inventory[player.selectedItem].useStyle == ItemUseStyleID.Shoot)
            {
                player.SetCompositeArmFront(true, Player.CompositeArmStretchAmount.Full,
                    aim - MathHelper.PiOver2);
                player.SetCompositeArmBack(true, Player.CompositeArmStretchAmount.Quarter,
                    aim - MathHelper.PiOver2);
            }
        }
        Main.PlayerRenderer.DrawPlayer(Main.Camera, player, position, 0f,
            new Vector2(player.width / 2f, player.height / 2f), 0f, scale);
    }

    private static void DrawWeaponIcon(int itemType, int x, int y)
    {
        Texture2D texture = TextureAssets.Item[itemType].Value;
        float scale = Math.Min(28f / texture.Width, 28f / texture.Height);
        Main.spriteBatch.Draw(texture, new Vector2(x, y), null, Color.White, 0f,
            new Vector2(texture.Width / 2f, texture.Height / 2f), scale, SpriteEffects.None, 0f);
    }

    private static int GetRobeLegSlot(int bodyType) => bodyType switch
    {
        var type when type == ModContent.ItemType<ClothRobe>() => EquipLoader.GetEquipSlot(ModContent.GetInstance<ClothRobe>().Mod, nameof(ClothRobe), EquipType.Legs),
        var type when type == ModContent.ItemType<EnergyRobe>() => EquipLoader.GetEquipSlot(ModContent.GetInstance<EnergyRobe>().Mod, nameof(EnergyRobe), EquipType.Legs),
        var type when type == ModContent.ItemType<FallenRobe>() => EquipLoader.GetEquipSlot(ModContent.GetInstance<FallenRobe>().Mod, nameof(FallenRobe), EquipType.Legs),
        var type when type == ModContent.ItemType<DarkRobe>() => EquipLoader.GetEquipSlot(ModContent.GetInstance<DarkRobe>().Mod, nameof(DarkRobe), EquipType.Legs),
        _ => -1
    };

    internal static Player CreatePreviewPlayer(int head, int body, int legs, bool walk = false, bool jump = false)
    {
        var player = new Player
        {
            active = true,
            direction = 1,
            width = 20,
            height = 42,
            position = Vector2.Zero,
            whoAmI = Main.myPlayer,
            statLife = 100,
            statLifeMax = 100
        };
        player.armor[0].SetDefaults(head);
        player.armor[1].SetDefaults(body);
        player.armor[2].SetDefaults(legs);
        player.head = head == ItemID.None ? 0 : player.armor[0].headSlot;
        player.body = player.armor[1].bodySlot;
        int robeLegSlot = GetRobeLegSlot(body);
        player.legs = robeLegSlot >= 0 ? robeLegSlot : player.armor[2].legSlot;
        Require((head == ItemID.None || player.head >= 0) && player.body >= 0 && player.legs >= 0,
            $"missing equip slot: head={player.head}, body={player.body}, legs={player.legs}");
        int frameRow = jump ? 5 : walk ? 3 : 0;
        player.bodyFrame = new Rectangle(0, frameRow * 56, 40, 56);
        player.legFrame = new Rectangle(0, frameRow * 56, 40, 56);
        if (walk)
        {
            player.velocity = new Vector2(1f, 0f);
            player.direction = 1;
        }
        else if (jump)
            player.velocity = new Vector2(1f, -5f);
        player.skinColor = new Color(238, 190, 150);
        player.hairColor = new Color(88, 58, 35);
        player.eyeColor = new Color(55, 95, 145);
        player.hair = 0;
        player.isDisplayDollOrInanimate = true;
        return player;
    }

    internal static Player CreateActionPlayer(int head, int body, int legs, int weapon,
        int direction, float aimAngle, bool male, int pose)
    {
        Player player = Main.LocalPlayer.SerializedClone();
        player.active = true;
        player.whoAmI = Main.myPlayer;
        player.position = Vector2.Zero;
        player.velocity = Vector2.Zero;
        player.fullRotation = 0f;
        player.armor[0] = new Item(); player.armor[0].SetDefaults(head);
        player.armor[1] = new Item(); player.armor[1].SetDefaults(body);
        player.armor[2] = new Item(); player.armor[2].SetDefaults(legs);
        player.head = player.armor[0].headSlot;
        player.body = player.armor[1].bodySlot;
        int robeLegSlot = GetRobeLegSlot(body);
        player.legs = robeLegSlot >= 0 ? robeLegSlot : player.armor[2].legSlot;
        player.inventory[0] = new Item();
        player.Male = male;
        player.skinColor = new Color(238, 190, 150);
        player.hairColor = new Color(88, 58, 35);
        player.eyeColor = new Color(55, 95, 145);
        player.hair = 0;
        player.direction = direction;
        player.inventory[0].SetDefaults(weapon);
        player.selectedItem = 0;
        // PlayerDrawSet copies lastVisualizedSelectedItem rather than reading
        // inventory[selectedItem]; synthetic actors must mirror a live select.
        player.lastVisualizedSelectedItem = player.inventory[0];
        player.SetDummyItemTime(18);
        player.controlUseItem = true;
        player.releaseUseItem = false;
        player.itemRotation = aimAngle;
        player.velocity = pose == 1 ? new Vector2(direction, 0f) : Vector2.Zero;
        player.isDisplayDollOrInanimate = false;
        player.heldProj = -1;
        player.GetModPlayer<Tier3T4ProgressPlayer>().ApplyNetworkState(true, true, false);
        return player;
    }

    private static Texture2D DrawStation(string texturePath, int x, int y)
    {
        Texture2D texture = ModContent.Request<Texture2D>(texturePath, ReLogic.Content.AssetRequestMode.ImmediateLoad).Value;
        const int stride = 18; // 16px tile plus the native 2px frame padding.
        int columns = texture.Width / stride;
        int rows = texture.Height / stride;
        int width = columns * 16;
        int height = rows * 16;
        var source = new Color[texture.Width * texture.Height];
        texture.GetData(source);
        var packed = new Color[width * height];
        for (int row = 0; row < rows; row++)
        for (int column = 0; column < columns; column++)
        for (int py = 0; py < 16; py++)
        for (int px = 0; px < 16; px++)
            packed[(row * 16 + py) * width + column * 16 + px] = source[(row * stride + py) * texture.Width + column * stride + px];
        var continuous = new Texture2D(texture.GraphicsDevice, width, height);
        continuous.SetData(packed);
        Main.spriteBatch.Draw(continuous, new Vector2(x, y), null, Color.White, 0f,
            new Vector2(width / 2f, height / 2f), 1f, SpriteEffects.None, 0f);
        return continuous;
    }
}

