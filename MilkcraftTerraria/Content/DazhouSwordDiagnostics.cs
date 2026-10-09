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
using Terraria.GameContent.ItemDropRules;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.Utilities;
using MilkcraftTerraria.Content.Items;

namespace MilkcraftTerraria.Content;

/// <summary>Opt-in runtime comparison of the new sword against local Zenith.</summary>
internal static class DazhouSwordDiagnostics
{
    private readonly record struct Shot(float Tick, Vector2 Position, Vector2 Velocity, int Damage,
        float Knockback, int Owner, float Ai0, float Ai1, float Ai2);
    private readonly record struct ProjectileEnd(Vector2 Center, Vector2 Velocity, float Rotation, int TimeLeft, bool Active);
    private sealed record CycleRun(int[] Starts, int[] ShotsPerCycle, Shot[] Shots, ProjectileEnd[] Ends,
        Projectile[] Projectiles, string ItemLabel, bool TargetSelected, Vector2 ExpectedTargetVelocity);

    public static string Run(string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        string path = Path.Combine(outputDirectory, "dazhou-sword-diagnostics.log");
        var lines = new List<string> { "0.2.11.1 Dazhou profile visuals; native Zenith behavior and five-sword art cycle." };
        TestItemDefaults(lines);
        TestSwordEdgeMask(lines);
        TestRecipeAndLoot(lines);

        CycleRun leftVisualRun = null;
        CycleRun rightVisualRun = null;
        foreach (float speed in new[] { 1f, 1.5f })
        foreach (int direction in new[] { -1, 1 })
        {
            string caseName = $"{speed:0.0}x-{(direction > 0 ? "right" : "left")}";
            CycleRun native = CaptureCycles(ItemID.Zenith, speed, direction, 20261008,
                tracePath: Path.Combine(outputDirectory, $"zenith-cycle-{caseName}.log"));
            CycleRun dazhou = CaptureCycles(ModContent.ItemType<DazhouIronSword>(), speed, direction, 20261008,
                tracePath: Path.Combine(outputDirectory, $"dazhou-cycle-{caseName}.log"));
            ValidateVisualCycle(dazhou, lines, caseName);
            if (Math.Abs(speed - 1f) < 0.001f)
            {
                if (direction < 0)
                    leftVisualRun = dazhou;
                else
                    rightVisualRun = dazhou;
            }
            Require(native.Starts.Length >= 2 && dazhou.Starts.Length >= 2,
                $"two native ItemCheck use cycles required ({speed:0.0}x dir={direction})");
            int nativeInterval = native.Starts[1] - native.Starts[0];
            int dazhouInterval = dazhou.Starts[1] - dazhou.Starts[0];
            Require(nativeInterval == dazhouInterval,
                $"Zenith/Dazhou use-cycle mismatch at {speed:0.0}x dir={direction}: {nativeInterval} vs {dazhouInterval}");
            Require(native.ShotsPerCycle.SequenceEqual(dazhou.ShotsPerCycle),
                $"Zenith/Dazhou projectile cadence mismatch at {speed:0.0}x dir={direction}: " +
                $"[{string.Join(',', native.ShotsPerCycle)}] vs [{string.Join(',', dazhou.ShotsPerCycle)}]");
            Require(native.Shots.Length == dazhou.Shots.Length,
                $"Zenith/Dazhou fired different projectile counts at {speed:0.0}x dir={direction}");

            int equalInitial = Math.Min(native.Shots.Length, dazhou.Shots.Length);
            for (int i = 0; i < equalInitial; i++)
            {
                Shot a = native.Shots[i], b = dazhou.Shots[i];
                Require(Vector2.Distance(a.Position, b.Position) < 0.1f &&
                    Vector2.Distance(a.Velocity, b.Velocity) < 0.1f &&
                    a.Damage == b.Damage && Math.Abs(a.Knockback - b.Knockback) < 0.001f && a.Owner == b.Owner &&
                    Math.Abs(a.Ai0 - b.Ai0) < 0.1f,
                    $"Zenith/Dazhou spawn trajectory metadata differs shot={i} speed={speed:0.0}x dir={direction}: " +
                    $"native={a} dazhou={b}");
            }


            Require(native.Ends.Length == dazhou.Ends.Length,
                $"Zenith/Dazhou projectile survival sample count mismatch speed={speed:0.0}x dir={direction}");
            for (int i = 0; i < native.Ends.Length; i++)
            {
                ProjectileEnd a = native.Ends[i], b = dazhou.Ends[i];
                Require(a.Active == b.Active && a.TimeLeft == b.TimeLeft &&
                    Vector2.Distance(a.Center, b.Center) < 0.5f &&
                    Vector2.Distance(a.Velocity, b.Velocity) < 0.1f &&
                    Math.Abs(MathHelper.WrapAngle(a.Rotation - b.Rotation)) < 0.01f,
                    $"Zenith/Dazhou native Projectile.Update diverged shot={i} speed={speed:0.0}x dir={direction}: native={a} dazhou={b}");
            }

            lines.Add($"native-itemcheck=PASS speed={speed:0.0}x direction={direction} " +
                $"Zenith={nativeInterval}ticks/{native.ShotsPerCycle[0]}shots/cycle " +
                $"Dazhou={dazhouInterval}ticks/{dazhou.ShotsPerCycle[0]}shots/cycle " +
                $"spawn-metadata={equalInitial}/{equalInitial} update-survival={native.Ends.Length}/{dazhou.Ends.Length}");
        }

        CycleRun targetedNative = CaptureCycles(ItemID.Zenith, 1f, 1, 20261009, targetNpc: true,
            tracePath: Path.Combine(outputDirectory, "zenith-cycle-npc-target.log"));
        CycleRun targetedDazhou = CaptureCycles(ModContent.ItemType<DazhouIronSword>(), 1f, 1, 20261009, targetNpc: true,
            tracePath: Path.Combine(outputDirectory, "dazhou-cycle-npc-target.log"));
        Require(targetedNative.TargetSelected && targetedDazhou.TargetSelected,
            "Zenith native GetZenithTarget failed to acquire the diagnostic NPC");
        Require(targetedNative.Shots.Length == targetedDazhou.Shots.Length &&
            targetedNative.Shots.Zip(targetedDazhou.Shots).All(pair =>
                Vector2.Distance(pair.First.Velocity, pair.Second.Velocity) < 0.1f &&
                pair.First.Damage == pair.Second.Damage &&
                Math.Abs(pair.First.Knockback - pair.Second.Knockback) < 0.001f &&
                pair.First.Owner == pair.Second.Owner &&
                Math.Abs(pair.First.Ai0 - pair.Second.Ai0) < 0.1f),
            "Zenith/Dazhou trajectories diverged with a valid chaseable NPC target");
        Require(targetedDazhou.Shots.Any(shot =>
                Vector2.Distance(shot.Velocity, targetedDazhou.ExpectedTargetVelocity) < 0.1f),
            $"targeted second/third Final Fractal swing did not aim at native NPC: expected={targetedDazhou.ExpectedTargetVelocity}");
        lines.Add($"native-zenith-target=PASS selectedNpc=true expectedVelocity={targetedDazhou.ExpectedTargetVelocity} " +
            $"matchedShots={targetedNative.Shots.Length}/{targetedDazhou.Shots.Length}");

        var rightAim = CaptureFirstShot(ModContent.ItemType<DazhouIronSword>(), 1, new Point(620, 300), 703);
        var upperAim = CaptureFirstShot(ModContent.ItemType<DazhouIronSword>(), 1, new Point(525, 205), 703);
        Require(Vector2.Distance(rightAim.Velocity, upperAim.Velocity) > 10f,
            $"cursor movement did not change the initial sword trajectory: {rightAim.Velocity} vs {upperAim.Velocity}");
        lines.Add($"cursor-targeting=PASS right={rightAim.Velocity} raised={upperAim.Velocity}");

        TestCollisionParity(lines);
        TestTargetedNativePreview(lines);
        ValidateVisualCycle(targetedDazhou, lines, "targeted");
        Require(leftVisualRun is not null && rightVisualRun is not null,
            "left/right native ItemCheck samples were not captured for profile rendering");
        RenderDarkAndLitSword(outputDirectory, lines, leftVisualRun.Projectiles, rightVisualRun.Projectiles);
        RenderSwordBodiesOnly(outputDirectory, lines, leftVisualRun.Projectiles, rightVisualRun.Projectiles);
        RenderInventoryAndWorldSamples(outputDirectory, lines);
        File.WriteAllLines(path, lines);
        return string.Join("\n", lines);
    }

    private static void TestItemDefaults(List<string> lines)
    {
        Item vanilla = new(); vanilla.SetDefaults(ItemID.Zenith);
        Item custom = new(); custom.SetDefaults(ModContent.ItemType<DazhouIronSword>());
        string[] differences =
        {
            Diff("useTime", vanilla.useTime, custom.useTime),
            Diff("useAnimation", vanilla.useAnimation, custom.useAnimation),
            Diff("damage", vanilla.damage, custom.damage),
            Diff("knockBack", vanilla.knockBack, custom.knockBack),
            Diff("crit", vanilla.crit, custom.crit),
            Diff("shootSpeed", vanilla.shootSpeed, custom.shootSpeed),
            Diff("DamageType", vanilla.DamageType, custom.DamageType),
            Diff("autoReuse", vanilla.autoReuse, custom.autoReuse),
            Diff("noMelee", vanilla.noMelee, custom.noMelee),
            Diff("useStyle", vanilla.useStyle, custom.useStyle),
            Diff("reuseDelay", vanilla.reuseDelay, custom.reuseDelay),
            Diff("useTurn", vanilla.useTurn, custom.useTurn),
            Diff("noUseGraphic", vanilla.noUseGraphic, custom.noUseGraphic),
            Diff("attackSpeedOnlyAffectsWeaponAnimation", vanilla.attackSpeedOnlyAffectsWeaponAnimation,
                custom.attackSpeedOnlyAffectsWeaponAnimation)
        };
        string mismatch = string.Join("; ", differences.Where(value => value is not null));
        Require(string.IsNullOrEmpty(mismatch), "Dazhou item differs from local Zenith defaults: " + mismatch);
        Require(custom.shoot == ModContent.ProjectileType<DazhouZenithBlade>(),
            "Dazhou must use its Iron Sword visual projectile");

        Projectile vanillaProjectile = new(); vanillaProjectile.SetDefaults(vanilla.shoot);
        Projectile customProjectile = new(); customProjectile.SetDefaults(custom.shoot);
        string[] projectileDifferences =
        {
            Diff("size", $"{vanillaProjectile.width}x{vanillaProjectile.height}", $"{customProjectile.width}x{customProjectile.height}"),
            Diff("aiStyle", vanillaProjectile.aiStyle, customProjectile.aiStyle),
            Diff("timeLeft", vanillaProjectile.timeLeft, customProjectile.timeLeft),
            Diff("extraUpdates", vanillaProjectile.extraUpdates, customProjectile.extraUpdates),
            Diff("scale", vanillaProjectile.scale, customProjectile.scale),
            Diff("penetrate", vanillaProjectile.penetrate, customProjectile.penetrate),
            Diff("tileCollide", vanillaProjectile.tileCollide, customProjectile.tileCollide),
            Diff("friendly", vanillaProjectile.friendly, customProjectile.friendly),
            Diff("DamageType", vanillaProjectile.DamageType, customProjectile.DamageType)
        };
        string projectileMismatch = string.Join("; ", projectileDifferences.Where(value => value is not null));
        Require(string.IsNullOrEmpty(projectileMismatch), "Dazhou projectile defaults differ from Final Fractal: " + projectileMismatch);
        lines.Add($"runtime-defaults=PASS ZenithItem(type={ItemID.Zenith},useTime={vanilla.useTime},useAnimation={vanilla.useAnimation},damage={vanilla.damage},knockback={vanilla.knockBack:0.###},crit={vanilla.crit},shootSpeed={vanilla.shootSpeed:0.###},DamageType={vanilla.DamageType},autoReuse={vanilla.autoReuse},noMelee={vanilla.noMelee}) " +
            $"FinalFractal(type={vanilla.shoot},size={vanillaProjectile.width}x{vanillaProjectile.height},aiStyle={vanillaProjectile.aiStyle},timeLeft={vanillaProjectile.timeLeft},extraUpdates={vanillaProjectile.extraUpdates},scale={vanillaProjectile.scale:0.###},penetrate={vanillaProjectile.penetrate},tileCollide={vanillaProjectile.tileCollide})");
    }

    private static string Diff<T>(string key, T left, T right) => EqualityComparer<T>.Default.Equals(left, right)
        ? null
        : $"{key}: Zenith={left} Dazhou={right}";

    private static void TestSwordEdgeMask(List<string> lines)
    {
        Texture2D mask = DazhouSwordVisualSystem.EdgeMask;
        Require(mask is not null, "Dazhou sword edge mask was not created");
        int sourceItemType = ModContent.ItemType<IronSword>();
        Require(DazhouSwordVisualSystem.ItemTypeForVisual(0) == sourceItemType &&
            DazhouSwordVisualSystem.ItemTypeForVisual(1) == sourceItemType &&
            DazhouSwordVisualSystem.ItemTypeForVisual(2) == ModContent.ItemType<KnightSword>() &&
            DazhouSwordVisualSystem.ItemTypeForVisual(3) == ModContent.ItemType<TemplarSword>() &&
            DazhouSwordVisualSystem.ItemTypeForVisual(4) == ModContent.ItemType<SoulforgedSword>(),
            "Dazhou visual ItemTypes do not match their registered mod item types");
        Texture2D source = ModContent.Request<Texture2D>(
            $"{ModContent.GetInstance<MilkcraftTerrariaMod>().Name}/Content/Items/IronSword",
            ReLogic.Content.AssetRequestMode.ImmediateLoad).Value;
        Require(mask.Width == source.Width && mask.Height == source.Height,
            $"Dazhou sword edge mask dimensions differ from source: {mask.Width}x{mask.Height} vs {source.Width}x{source.Height}");

        var sourcePixels = new Color[source.Width * source.Height];
        var maskPixels = new Color[mask.Width * mask.Height];
        source.GetData(sourcePixels);
        mask.GetData(maskPixels);
        int transparent = 0;
        for (int i = 0; i < maskPixels.Length; i++)
        {
            Color pixel = maskPixels[i];
            Require(pixel.A == sourcePixels[i].A,
                $"Dazhou sword edge alpha diverges from Iron Sword source pixel {i}");
            if (pixel.A == 0)
            {
                transparent++;
                Require(pixel.R == 0 && pixel.G == 0 && pixel.B == 0,
                    $"Dazhou sword edge mask has nonzero RGB in transparent pixel {i}: {pixel}");
            }
        }
        Require(transparent > 0, "Dazhou sword edge mask source has no transparent pixels to validate");
        lines.Add($"sword-edge-mask=PASS source={source.Width}x{source.Height} transparent={transparent} premultiplied=true " +
            $"registeredTypes=[{string.Join(',', Enumerable.Range(0, 5).Select(DazhouSwordVisualSystem.ItemTypeForVisual))}]");
    }

    private static void TestRecipeAndLoot(List<string> lines)
    {
        int dazhouType = ModContent.ItemType<DazhouIronSword>();
        int brokenType = ModContent.ItemType<HiddenIronSword>();
        Recipe[] matching = Main.recipe.Take(Recipe.numRecipes)
            .Where(recipe => recipe.createItem.type == dazhouType).ToArray();
        Require(matching.Length == 1, $"expected exactly one Dazhou recipe, found {matching.Length}");
        Recipe recipe = matching[0];
        int[] expectedIngredients =
        {
            ModContent.ItemType<IronSword>(), ModContent.ItemType<KnightSword>(),
            ModContent.ItemType<TemplarSword>(), ModContent.ItemType<SoulforgedSword>(), brokenType
        };
        Require(recipe.createItem.stack == 1 && recipe.requiredItem.Where(item => item.type > ItemID.None)
            .Select(item => item.type).OrderBy(type => type).SequenceEqual(expectedIngredients.OrderBy(type => type)),
            "Dazhou recipe ingredients must be exactly one Iron/Knight/Templar/Soulforged/Broken Iron Sword");
        Require(recipe.requiredTile.Contains(TileID.MythrilAnvil),
            "Dazhou recipe must require the Mythril/Orichalcum Anvil");
        Require(!Main.recipe.Take(Recipe.numRecipes).Any(candidate => candidate.createItem.type == brokenType),
            "Broken Iron Sword must not have a recipe");

        List<IItemDropRule> core = Main.ItemDropsDB.GetRulesForNPCID(NPCID.MoonLordCore);
        List<IItemDropRule> hand = Main.ItemDropsDB.GetRulesForNPCID(NPCID.MoonLordHand);
        List<IItemDropRule> head = Main.ItemDropsDB.GetRulesForNPCID(NPCID.MoonLordHead);
        CommonDrop brokenRule = core.OfType<CommonDrop>().SingleOrDefault(rule => rule.itemId == brokenType);
        Require(brokenRule is not null && brokenRule.chanceDenominator == 1 && brokenRule.chanceNumerator == 1 &&
            brokenRule.amountDroppedMinimum == 1 && brokenRule.amountDroppedMaximum == 1,
            "Moon Lord core must have exactly one guaranteed Broken Iron Sword rule");
        Require(!hand.OfType<CommonDrop>().Any(rule => rule.itemId == brokenType) &&
            !head.OfType<CommonDrop>().Any(rule => rule.itemId == brokenType),
            "Moon Lord hands/head must not drop the Broken Iron Sword");
        lines.Add($"recipe-loot=PASS recipe=5x1 ingredients at MythrilAnvil; brokenSword=noRecipe; MoonLordCore=CommonDrop(1/1,1) hands=none head=none expertBag=none");
    }

    private static CycleRun CaptureCycles(int itemType, float meleeSpeed, int direction, int seed,
        bool targetNpc = false, string tracePath = null)
    {
        int playerIndex = Math.Clamp(Main.myPlayer, 0, Main.player.Length - 1);
        Player previousPlayer = Main.player[playerIndex];
        Projectile[] previousProjectiles = Main.projectile;
        NPC[] previousNpcs = Main.npc;
        bool previousMenu = Main.gameMenu;
        bool previousPaused = Main.gamePaused;
        bool previousWorldGen = WorldGen.gen;
        int previousMouseX = Main.mouseX, previousMouseY = Main.mouseY;
        Vector2 previousScreen = Main.screenPosition;
        UnifiedRandom previousRandom = Main.rand;
        var trace = new List<string>();
        try
        {
            Main.gameMenu = false;
            Main.gamePaused = false;
            WorldGen.gen = false;
            Main.screenPosition = Vector2.Zero;
            Main.rand = new UnifiedRandom(seed);
            Main.npc = Enumerable.Range(0, previousNpcs.Length).Select(_ => new NPC()).ToArray();
            Main.projectile = Enumerable.Range(0, previousProjectiles.Length).Select(_ => new Projectile()).ToArray();

            Player actor = CreateActionActor(itemType, playerIndex, direction);
            actor.position = new Vector2(420f, 360f);
            actor.velocity = Vector2.Zero;
            actor.fullRotation = 0f;
            actor.armor[0] = new Item(); actor.armor[0].SetDefaults(ItemID.IronHelmet);
            actor.armor[1] = new Item(); actor.armor[1].SetDefaults(ItemID.IronChainmail);
            actor.armor[2] = new Item(); actor.armor[2].SetDefaults(ItemID.IronGreaves);
            actor.GetAttackSpeed(DamageClass.Melee) = meleeSpeed;
            actor.itemAnimation = 0;
            actor.itemTime = 0;
            Main.player[playerIndex] = actor;
            trace.Add($"initial item={ContentSamples.ItemsByType[itemType].Name} useTime={actor.HeldItem.useTime} " +
                $"useAnimation={actor.HeldItem.useAnimation} itemAnimation={actor.itemAnimation}/{actor.itemAnimationMax} " +
                $"itemTime={actor.itemTime} dummy=18 attackSpeed={meleeSpeed:0.###} active={actor.active} " +
                $"displayDoll={actor.isDisplayDollOrInanimate} heldProj={actor.heldProj} owner={actor.whoAmI}");
            Vector2 mousePoint = actor.MountedCenter + new Vector2(direction * 180f, -40f);
            bool targetSelected = false;
            Vector2 expectedTargetVelocity = Vector2.Zero;
            if (targetNpc)
            {
                NPC target = Main.npc[0];
                target.SetDefaults(NPCID.BlueSlime);
                target.active = true;
                target.friendly = false;
                target.dontTakeDamage = false;
                target.Center = actor.MountedCenter + new Vector2(110f, -18f);
                mousePoint = target.Center;
                targetSelected = DazhouZenithBlade.TryGetZenithTarget(actor, mousePoint, out int selectedNpc) && selectedNpc == 0;
                expectedTargetVelocity = (target.Center - actor.MountedCenter) / 2f;
            }
            Main.mouseX = (int)mousePoint.X;
            Main.mouseY = (int)mousePoint.Y;

            var shots = new List<Shot>();
            var projectiles = new List<Projectile>();
            var seen = new HashSet<int>();
            var starts = new List<int>();
            int[] cycleCounts = Array.Empty<int>();
            for (int tick = 0; tick < 360 && starts.Count < 3; tick++)
            {
                int animationBefore = actor.itemAnimation;
                int itemTimeBefore = actor.itemTime;
                int projectileCountBefore = Main.projectile.Count(projectile => projectile.active &&
                    projectile.owner == playerIndex && projectile.type == actor.HeldItem.shoot);
                actor.ItemCheck();
                // Auto-reuse can restart a countdown directly from a positive
                // frame without exposing an intermediate zero to this fixture.
                if (actor.itemAnimation > animationBefore)
                {
                    starts.Add(tick);
                    Array.Resize(ref cycleCounts, starts.Count);
                }

                int newlySeenThisTick = 0;
                for (int slot = 0; slot < Main.projectile.Length; slot++)
                {
                    Projectile projectile = Main.projectile[slot];
                    if (!projectile.active || projectile.owner != playerIndex || projectile.type != actor.HeldItem.shoot ||
                        !seen.Add(slot))
                        continue;
                    newlySeenThisTick++;
                    int cycle = Math.Max(0, starts.Count - 1);
                    if (cycle < cycleCounts.Length)
                        cycleCounts[cycle]++;
                    shots.Add(new Shot(tick, projectile.position, projectile.velocity,
                        projectile.damage, projectile.knockBack, projectile.owner,
                        projectile.ai[0], projectile.ai[1], projectile.ai[2]));
                    projectiles.Add(projectile);
                }
                int projectileCountAfter = Main.projectile.Count(projectile => projectile.active &&
                    projectile.owner == playerIndex && projectile.type == actor.HeldItem.shoot);
                trace.Add($"tick={tick} anim={animationBefore}->{actor.itemAnimation}/{actor.itemAnimationMax} " +
                    $"itemTime={itemTimeBefore}->{actor.itemTime} control={actor.controlUseItem} release={actor.releaseUseItem} " +
                    $"displayDoll={actor.isDisplayDollOrInanimate} active={actor.active} " +
                    $"projectiles={projectileCountBefore}->{projectileCountAfter} newSlots={newlySeenThisTick} starts={starts.Count}");
            }

            trace.Add($"loop-end starts={starts.Count} shots={shots.Count} cycles=[{string.Join(',', cycleCounts)}]");
            Require(starts.Count >= 2, $"ItemCheck did not start two cycles for {ContentSamples.ItemsByType[itemType].Name}");
            int[] usedCycles = cycleCounts.Take(Math.Min(2, cycleCounts.Length)).ToArray();
            Projectile[] advancing = projectiles.ToArray();
            for (int frame = 0; frame < 8; frame++)
            foreach (Projectile projectile in advancing)
                if (projectile.active)
                    projectile.Update(projectile.whoAmI);

            ProjectileEnd[] ends = advancing.Select(projectile => new ProjectileEnd(projectile.Center,
                projectile.velocity, projectile.rotation, projectile.timeLeft, projectile.active)).ToArray();
            return new CycleRun(starts.ToArray(), usedCycles,
                shots.Take(shots.Count).ToArray(), ends, advancing, ContentSamples.ItemsByType[itemType].Name,
                targetSelected, expectedTargetVelocity);
        }
        finally
        {
            if (tracePath is not null)
                File.WriteAllLines(tracePath, trace);
            Main.player[playerIndex] = previousPlayer;
            Main.projectile = previousProjectiles;
            Main.npc = previousNpcs;
            Main.gameMenu = previousMenu;
            Main.gamePaused = previousPaused;
            WorldGen.gen = previousWorldGen;
            Main.mouseX = previousMouseX;
            Main.mouseY = previousMouseY;
            Main.screenPosition = previousScreen;
            Main.rand = previousRandom;
        }
    }

    private static Shot CaptureFirstShot(int itemType, int direction, Point cursor, int seed)
    {
        int playerIndex = Math.Clamp(Main.myPlayer, 0, Main.player.Length - 1);
        Player previousPlayer = Main.player[playerIndex];
        Projectile[] previousProjectiles = Main.projectile;
        NPC[] previousNpcs = Main.npc;
        bool previousMenu = Main.gameMenu;
        int oldMouseX = Main.mouseX, oldMouseY = Main.mouseY;
        Vector2 oldScreen = Main.screenPosition;
        UnifiedRandom oldRandom = Main.rand;
        try
        {
            Main.gameMenu = false;
            Main.screenPosition = Vector2.Zero;
            Main.rand = new UnifiedRandom(seed);
            Main.npc = Enumerable.Range(0, previousNpcs.Length).Select(_ => new NPC()).ToArray();
            Main.projectile = Enumerable.Range(0, previousProjectiles.Length).Select(_ => new Projectile()).ToArray();
            Player actor = CreateActionActor(itemType, playerIndex, direction);
            actor.position = new Vector2(420f, 360f);
            actor.itemTime = 0;
            actor.itemAnimation = 0;
            Main.player[playerIndex] = actor;
            Main.mouseX = cursor.X; Main.mouseY = cursor.Y;
            for (int i = 0; i < 100; i++)
            {
                actor.ItemCheck();
                Projectile result = Main.projectile.FirstOrDefault(projectile => projectile.active &&
                    projectile.owner == playerIndex && projectile.type == actor.HeldItem.shoot);
                if (result is not null)
                    return new Shot(i, result.position, result.velocity, result.damage, result.knockBack,
                        result.owner, result.ai[0], result.ai[1], result.ai[2]);
            }
            throw new InvalidOperationException("Dazhou ItemCheck did not emit a projectile for cursor probe.");
        }
        finally
        {
            Main.player[playerIndex] = previousPlayer;
            Main.projectile = previousProjectiles;
            Main.npc = previousNpcs;
            Main.gameMenu = previousMenu;
            Main.mouseX = oldMouseX; Main.mouseY = oldMouseY;
            Main.screenPosition = oldScreen;
            Main.rand = oldRandom;
        }
    }

    private static Player CreateActionActor(int itemType, int playerIndex, int direction)
    {
        Player actor = Main.LocalPlayer.SerializedClone();
        actor.active = true;
        actor.whoAmI = playerIndex;
        actor.Male = true;
        actor.position = Vector2.Zero;
        actor.velocity = Vector2.Zero;
        actor.fullRotation = 0f;
        actor.direction = direction;
        actor.inventory[0] = new Item();
        actor.inventory[0].SetDefaults(itemType);
        actor.selectedItem = 0;
        actor.lastVisualizedSelectedItem = actor.inventory[0];
        actor.SetDummyItemTime(18);
        actor.controlUseItem = true;
        actor.releaseUseItem = false;
        actor.isDisplayDollOrInanimate = false;
        actor.heldProj = -1;
        actor.GetAttackSpeed(DamageClass.Melee) = 1f;
        return actor;
    }

    private static void TestCollisionParity(List<string> lines)
    {
        Projectile vanilla = new(); vanilla.SetDefaults(ProjectileID.FinalFractal);
        Projectile custom = new(); custom.SetDefaults(ModContent.ProjectileType<DazhouZenithBlade>());
        vanilla.active = custom.active = true;
        vanilla.position = custom.position = new Vector2(500f, 500f);
        vanilla.velocity = custom.velocity = new Vector2(8f, -2f);
        vanilla.rotation = custom.rotation = 0.31f;
        vanilla.localAI[0] = custom.localAI[0] = 30f;
        for (int i = 0; i < Math.Min(vanilla.oldPos.Length, custom.oldPos.Length); i++)
        {
            vanilla.oldPos[i] = custom.oldPos[i] = new Vector2(500f - i * 8f, 500f + i * 2f);
            vanilla.oldRot[i] = custom.oldRot[i] = 0.31f;
        }

        int compared = 0;
        for (int x = -200; x <= 200; x += 20)
        for (int y = -200; y <= 200; y += 20)
        {
            Rectangle target = new((int)vanilla.Center.X + x, (int)vanilla.Center.Y + y, 16, 16);
            bool nativeHit = vanilla.Colliding(vanilla.Hitbox, target);
            bool customHit = custom.Colliding(custom.Hitbox, target);
            Require(nativeHit == customHit,
                $"Final Fractal collision mismatch at offset=({x},{y}): vanilla={nativeHit} custom={customHit}");
            compared++;
        }
        lines.Add($"native-collision=PASS FinalFractal-privateTypeBranch-reproduced AABB samples={compared} geometry=300px broadphase/40px blade/20px width/history oldRot+current");
    }

    private static void RenderDarkAndLitSword(string outputDirectory, List<string> lines,
        Projectile[] leftProjectiles, Projectile[] rightProjectiles)
    {
        if (Main.dedServ || Main.graphics?.GraphicsDevice is null)
            throw new InvalidOperationException("Dazhou visual diagnostic requires the native client renderer.");
        GraphicsDevice device = Main.graphics.GraphicsDevice;
        const int width = 1280, height = 900;
        var visuals = new List<(int id, Projectile left, Projectile right)>();
        for (int id = 0; id < 5; id++)
        {
            Projectile left = leftProjectiles.FirstOrDefault(projectile => projectile.active &&
                projectile.type == ModContent.ProjectileType<DazhouZenithBlade>() &&
                DazhouSwordVisualSystem.NormalizeVisualId(projectile.ai[2]) == id);
            Projectile right = rightProjectiles.FirstOrDefault(projectile => projectile.active &&
                projectile.type == ModContent.ProjectileType<DazhouZenithBlade>() &&
                DazhouSwordVisualSystem.NormalizeVisualId(projectile.ai[2]) == id);
            Require(left is not null && left.oldPos?.Any(position => position != Vector2.Zero) == true &&
                right is not null && right.oldPos?.Any(position => position != Vector2.Zero) == true,
                $"Dazhou preview needs real left/right ItemCheck projectiles with trail history for visual={id}");
            visuals.Add((id, left, right));
        }
        Vector2 oldScreen = Main.screenPosition;
        RenderTargetBinding[] oldTargets = device.GetRenderTargets();
        try
        {
            Main.screenPosition = Vector2.Zero;
            using RenderTarget2D target = new(device, width, height, false, SurfaceFormat.Color,
                DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
            device.SetRenderTarget(target);
            device.Clear(Color.Black);
            Main.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp,
                DepthStencilState.None, RasterizerState.CullNone);
            for (int row = 0; row < visuals.Count; row++)
            {
                (int id, Projectile left, Projectile right) = visuals[row];
                float y = 112f + row * 150f;
                DrawLabel($"{VisualName(id)} - L dark", new Vector2(22f, y - 34f));
                DrawLabel($"{VisualName(id)} - L lit", new Vector2(324f, y - 34f));
                DrawLabel($"{VisualName(id)} - R dark", new Vector2(662f, y - 34f));
                DrawLabel($"{VisualName(id)} - R lit", new Vector2(964f, y - 34f));
                DrawProjectileSample(left, new Vector2(160f, y), Color.Black, id, "left-dark");
                DrawProjectileSample(left, new Vector2(460f, y), new Color(150, 150, 160), id, "left-lit");
                DrawProjectileSample(right, new Vector2(800f, y), Color.Black, id, "right-dark");
                DrawProjectileSample(right, new Vector2(1100f, y), new Color(150, 150, 160), id, "right-lit");
            }
            Main.spriteBatch.End();
            device.SetRenderTarget(null);
            string png = Path.Combine(outputDirectory, "dazhou-sword-profile-left-right-dark-lit.png");
            using FileStream stream = File.Create(png);
            target.SaveAsPng(stream, width, height);
            lines.Add($"profile-previews=PASS actualNativeItemCheckProjectile+FinalFractalHelper fiveVisuals=Dazhou/Iron/Knight/Templar/Soulforged dark-lit={png}");
        }
        finally
        {
            Main.screenPosition = oldScreen;
            device.SetRenderTargets(oldTargets);
        }

        void DrawLabel(string text, Vector2 position) => Terraria.UI.Chat.ChatManager.DrawColorCodedStringWithShadow(
            Main.spriteBatch, FontAssets.MouseText.Value, text, position, Color.White, 0f,
            Vector2.Zero, Vector2.One, 330f, 2f);

        void DrawProjectileSample(Projectile visual, Vector2 anchor, Color light, int id, string sample)
        {
            Main.screenPosition = visual.Center - anchor;
            Color drawLight = light;
            Require(visual.ModProjectile.PreDraw(ref drawLight) == false,
                $"Dazhou projectile PreDraw failed visual={id} sample={sample}");
        }
    }

    private static void ValidateVisualCycle(CycleRun run, List<string> lines, string caseName)
    {
        int[] visuals = run.Shots.Select(shot => DazhouSwordVisualSystem.NormalizeVisualId(shot.Ai2)).ToArray();
        Require(visuals.Contains(0) && Enumerable.Range(1, 4).All(visuals.Contains),
            $"continuous Dazhou ItemCheck did not cover all five sword arts in {caseName}: [{string.Join(',', visuals)}]");
        for (int i = 0; i < run.Shots.Length; i++)
        {
            Shot shot = run.Shots[i];
            int visual = DazhouSwordVisualSystem.NormalizeVisualId(shot.Ai2);
            int expectedProfile = DazhouSwordVisualSystem.NativeProfileForVisual(visual);
            Require(Math.Abs(shot.Ai1 - expectedProfile) < 0.01f,
                $"Dazhou visual={visual} did not use its stable native Final Fractal profile: ai1={shot.Ai1} expected={expectedProfile}");
        }
        lines.Add($"visual-itemcheck=PASS {caseName} all-five=[{string.Join(',', visuals.Distinct().OrderBy(value => value))}] " +
            $"profile-pairs={string.Join(',', run.Shots.Select(shot => $"{DazhouSwordVisualSystem.NormalizeVisualId(shot.Ai2)}:{shot.Ai1:0}").Distinct())}");
    }

    private static string VisualName(int id) => id switch
    {
        0 => "Dazhou gray",
        1 => "Iron",
        2 => "Knight",
        3 => "Templar",
        4 => "Soulforged",
        _ => "Iron"
    };

    private static void RenderSwordBodiesOnly(string outputDirectory, List<string> lines,
        Projectile[] leftProjectiles, Projectile[] rightProjectiles)
    {
        GraphicsDevice device = Main.graphics.GraphicsDevice;
        const int width = 1280, height = 900;
        Vector2 oldScreen = Main.screenPosition;
        RenderTargetBinding[] oldTargets = device.GetRenderTargets();
        bool batchBegun = false;
        try
        {
            Main.screenPosition = Vector2.Zero;
            using RenderTarget2D target = new(device, width, height, false, SurfaceFormat.Color,
                DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
            device.SetRenderTarget(target);
            device.Clear(Color.Black);
            Main.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp,
                DepthStencilState.None, RasterizerState.CullNone);
            batchBegun = true;
            for (int visual = 0; visual < 5; visual++)
            {
                Projectile left = leftProjectiles.First(projectile => projectile.active &&
                    DazhouSwordVisualSystem.NormalizeVisualId(projectile.ai[2]) == visual);
                Projectile right = rightProjectiles.First(projectile => projectile.active &&
                    DazhouSwordVisualSystem.NormalizeVisualId(projectile.ai[2]) == visual);
                float y = 112f + visual * 150f;
                DrawLabel($"{VisualName(visual)} L dark", new Vector2(20f, y - 34f));
                DrawLabel($"{VisualName(visual)} L lit", new Vector2(324f, y - 34f));
                DrawLabel($"{VisualName(visual)} R dark", new Vector2(660f, y - 34f));
                DrawLabel($"{VisualName(visual)} R lit", new Vector2(964f, y - 34f));
                DrawBody(left, new Vector2(160f, y), Color.Black);
                DrawBody(left, new Vector2(460f, y), new Color(150, 150, 160));
                DrawBody(right, new Vector2(800f, y), Color.Black);
                DrawBody(right, new Vector2(1100f, y), new Color(150, 150, 160));
            }
            Main.spriteBatch.End();
            batchBegun = false;
            device.SetRenderTarget(null);
            string png = Path.Combine(outputDirectory, "dazhou-sword-bodies-only-dark-lit.png");
            using (FileStream stream = File.Create(png))
                target.SaveAsPng(stream, width, height);
            Color[] pixels = new Color[width * height];
            target.GetData(pixels);
            for (int visual = 0; visual < 5; visual++)
            {
                int y = 112 + visual * 150;
                foreach (int x in new[] { 110, 410, 750, 1050 })
                    Require(CountVisibleColorVariety(pixels, width, new Rectangle(x, y - 34, 110, 88)) >= 3,
                        $"Sword body details are not visible for visual={visual}, sampleX={x}");
            }
            lines.Add($"body-only-previews=PASS cachedImmediateLoadSwordTexture+nativeItemCheckScale; all-five left-right dark-lit={png}");
        }
        finally
        {
            if (batchBegun)
                Main.spriteBatch.End();
            Main.screenPosition = oldScreen;
            device.SetRenderTargets(oldTargets);
        }

        void DrawLabel(string text, Vector2 position) => Terraria.UI.Chat.ChatManager.DrawColorCodedStringWithShadow(
            Main.spriteBatch, FontAssets.MouseText.Value, text, position, Color.White, 0f,
            Vector2.Zero, Vector2.One, 280f, 2f);

        void DrawBody(Projectile projectile, Vector2 anchor, Color light)
        {
            Main.screenPosition = projectile.Center - anchor;
            DazhouSwordEdge.DrawProjectile(Main.spriteBatch, projectile, light);
        }
    }

    private static int CountVisibleColorVariety(Color[] pixels, int stride, Rectangle area)
    {
        var colors = new HashSet<int>();
        for (int y = area.Top; y < area.Bottom; y++)
        for (int x = area.Left; x < area.Right; x++)
        {
            Color color = pixels[y * stride + x];
            if (color.A > 0 && (color.R > 25 || color.G > 25 || color.B > 25))
                colors.Add((color.R << 16) | (color.G << 8) | color.B);
        }
        return colors.Count;
    }

    private static void TestTargetedNativePreview(List<string> lines)
    {
        int playerIndex = Math.Clamp(Main.myPlayer, 0, Main.player.Length - 1);
        Player oldPlayer = Main.player[playerIndex];
        NPC[] oldNpcs = Main.npc;
        bool oldMenu = Main.gameMenu;
        Vector2 oldScreen = Main.screenPosition;
        int oldMouseX = Main.mouseX, oldMouseY = Main.mouseY;
        try
        {
            Main.gameMenu = false;
            Main.screenPosition = Vector2.Zero;
            Main.npc = Enumerable.Range(0, oldNpcs.Length).Select(_ => new NPC()).ToArray();
            Player actor = Main.LocalPlayer.SerializedClone();
            actor.active = true;
            actor.whoAmI = playerIndex;
            actor.position = new Vector2(420f, 360f);
            Main.player[playerIndex] = actor;
            NPC target = Main.npc[0];
            target.SetDefaults(NPCID.BlueSlime);
            target.active = true;
            target.friendly = false;
            target.dontTakeDamage = false;
            target.Center = actor.MountedCenter + new Vector2(110f, -18f);
            bool found = DazhouZenithBlade.TryGetZenithTarget(actor, target.Center, out int selected);
            Require(found && selected == 0,
                $"native GetZenithTarget did not choose nearby hostile NPC: found={found}, selected={selected}");
            lines.Add($"native-target-acquisition=PASS NPC=BlueSlime index={selected} distance={Vector2.Distance(actor.MountedCenter, target.Center):0.0}px");
        }
        finally
        {
            Main.player[playerIndex] = oldPlayer;
            Main.npc = oldNpcs;
            Main.gameMenu = oldMenu;
            Main.screenPosition = oldScreen;
            Main.mouseX = oldMouseX;
            Main.mouseY = oldMouseY;
        }
    }

    private static void RenderInventoryAndWorldSamples(string outputDirectory, List<string> lines)
    {
        if (Main.dedServ || Main.graphics?.GraphicsDevice is null)
            throw new InvalidOperationException("Dazhou inventory/world preview requires the native client renderer.");

        GraphicsDevice device = Main.graphics.GraphicsDevice;
        const int width = 720, height = 400;
        Vector2 oldScreen = Main.screenPosition;
        RenderTargetBinding[] oldTargets = device.GetRenderTargets();
        bool batchBegun = false;
        var font = FontAssets.MouseText.Value;
        try
        {
            Main.screenPosition = Vector2.Zero;
            using RenderTarget2D target = new(device, width, height, false, SurfaceFormat.Color,
                DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
            device.SetRenderTarget(target);
            device.Clear(new Color(13, 15, 20));
            Main.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp,
                DepthStencilState.None, RasterizerState.CullNone);
            batchBegun = true;
            int customType = ModContent.ItemType<DazhouIronSword>();
            int brokenType = ModContent.ItemType<HiddenIronSword>();
            Main.instance.LoadItem(customType);
            Main.instance.LoadItem(brokenType);
            DrawLabel("Dazhou Iron Sword - inventory", new Vector2(60f, 25f));
            DrawLabel("Broken Iron Sword - inventory", new Vector2(390f, 25f));
            DrawInventoryHook(customType, new Vector2(170f, 100f), 1f);
            DrawInventoryHook(brokenType, new Vector2(520f, 100f), 32f /
                Math.Max(TextureAssets.Item[brokenType].Value.Width, TextureAssets.Item[brokenType].Value.Height));

            DrawLabel("Dazhou Iron Sword - world drop", new Vector2(60f, 225f));
            DrawLabel("Broken Iron Sword - world drop", new Vector2(390f, 225f));
            DrawWorldHook(customType, new Vector2(180f, 300f), 0.85f);
            DrawWorldHook(brokenType, new Vector2(520f, 300f), 0.85f);
            Main.spriteBatch.End();
            batchBegun = false;
            device.SetRenderTarget(null);
            string png = Path.Combine(outputDirectory, "dazhou-sword-inventory-world.png");
            using (FileStream stream = File.Create(png))
                target.SaveAsPng(stream, width, height);

            Color[] pixels = new Color[width * height];
            target.GetData(pixels);
            Require(CountNonBackground(pixels, width, new Rectangle(80, 25, 180, 160)) > 20,
                "Dazhou inventory hook produced no visible pixels");
            Require(CountNonBackground(pixels, width, new Rectangle(405, 25, 220, 160)) > 20,
                "Broken Iron Sword inventory crop produced no visible pixels");
            Require(CountNonBackground(pixels, width, new Rectangle(80, 220, 200, 150)) > 20,
                "Dazhou world-drop hook produced no visible pixels");
            Require(CountNonBackground(pixels, width, new Rectangle(405, 220, 220, 150)) > 20,
                "Broken Iron Sword world-drop crop produced no visible pixels");
            lines.Add($"inventory-world=PASS Dazhou IronSword native hooks; BrokenIronSword crop={HiddenIronSword.VisibleSourceWidth}x{HiddenIronSword.VisibleSourceHeight} normalized<=32px; PNG={png}");
        }
        finally
        {
            if (batchBegun)
                Main.spriteBatch.End();
            Main.screenPosition = oldScreen;
            device.SetRenderTargets(oldTargets);
        }

        void DrawInventoryHook(int type, Vector2 position, float scale)
        {
            Item item = new();
            item.SetDefaults(type);
            Texture2D texture = TextureAssets.Item[type].Value;
            Rectangle frame = new(0, 0, texture.Width, texture.Height);
            Vector2 origin = frame.Size() * 0.5f;
            bool drawVanillaBody = item.ModItem.PreDrawInInventory(Main.spriteBatch, position, frame,
                Color.White, Color.White, origin, scale);
            if (drawVanillaBody)
                Main.spriteBatch.Draw(texture, position, frame, Color.White, 0f, origin, scale,
                    SpriteEffects.None, 0f);
        }

        void DrawWorldHook(int type, Vector2 center, float scale)
        {
            Item item = new();
            item.SetDefaults(type);
            item.position = center - item.Size * 0.5f;
            float rotation = 0.15f;
            float drawScale = scale;
            bool drawVanillaBody = item.ModItem.PreDrawInWorld(Main.spriteBatch, Color.White, Color.White,
                ref rotation, ref drawScale, 0);
            if (drawVanillaBody)
            {
                Texture2D texture = TextureAssets.Item[type].Value;
                Main.spriteBatch.Draw(texture, center, null, Color.White, rotation,
                    texture.Size() * 0.5f, drawScale, SpriteEffects.None, 0f);
            }
        }

        void DrawLabel(string text, Vector2 position)
            => Terraria.UI.Chat.ChatManager.DrawColorCodedStringWithShadow(Main.spriteBatch, font, text,
                position, Color.White, 0f, Vector2.Zero, Vector2.One, 330f, 2f);

        static int CountNonBackground(Color[] pixels, int stride, Rectangle area)
        {
            int count = 0;
            for (int y = area.Top; y < area.Bottom; y++)
            for (int x = area.Left; x < area.Right; x++)
            {
                Color color = pixels[y * stride + x];
                if (color.R > 25 || color.G > 25 || color.B > 25)
                    count++;
            }
            return count;
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
