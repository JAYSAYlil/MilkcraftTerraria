using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using MonoMod.RuntimeDetour;
using Terraria;
using Terraria.DataStructures;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MilkcraftTerraria.Content.Items;

namespace MilkcraftTerraria.Content;

/// <summary>Opt-in isolated native mount behavior checks. Does not run in normal play.</summary>
internal static class MountDiagnostics
{
    private static bool _forceWhiteMountColor;
    private static bool _captureMountGeometry;
    private static string _mountGeometryLabel = "";
    private static readonly List<string> _mountGeometryRows = new();
    private delegate void OrigMountDraw(Mount self, List<DrawData> drawData, int drawType, Player drawPlayer,
        Vector2 position, Color drawColor, SpriteEffects spriteEffects, float shadow);
    private delegate void MountDrawDetour(OrigMountDraw orig, Mount self, List<DrawData> drawData, int drawType,
        Player drawPlayer, Vector2 position, Color drawColor, SpriteEffects spriteEffects, float shadow);

    internal static string Run()
    {
        var report = new StringBuilder();
        Player warhorse = SmokeSystem.CreatePreviewPlayer(ItemID.IronHelmet, ItemID.IronChainmail, ItemID.IronGreaves);
        Player tengu = SmokeSystem.CreatePreviewPlayer(ItemID.IronHelmet, ItemID.IronChainmail, ItemID.IronGreaves);
        try
        {
            int horseType = ModContent.MountType<UndeadWarhorseMount>();
            int tenguType = ModContent.MountType<CelestialTenguMount>();
            TestNativeSummonsAndMountedWeapons(report, horseType, tenguType);
            var horseData = Mount.mounts[horseType];
            var tenguData = Mount.mounts[tenguType];
            string horseBack = GetBackTextureSize(horseData);
            string tenguBack = GetBackTextureSize(tenguData);
            report.AppendLine($"horse-texture-size={horseData.textureWidth}x{horseData.textureHeight} back={horseBack} tengu-texture-size={tenguData.textureWidth}x{tenguData.textureHeight} back={tenguBack}");
            if ($"{horseData.textureWidth}x{horseData.textureHeight}" != horseBack || $"{tenguData.textureWidth}x{tenguData.textureHeight}" != tenguBack)
                throw new InvalidOperationException("Mounted texture dimensions do not match the registered back atlases.");
            if (horseData.flightTimeMax != 120)
                throw new InvalidOperationException($"Warhorse contract mismatch: flight={horseData.flightTimeMax}, dash={horseData.dashSpeed}, Unicorn={Mount.mounts[MountID.Unicorn].dashSpeed}.");
            if (!tenguData.usesHover || tenguData.flightTimeMax <= 0)
                throw new InvalidOperationException("Tengu must use native hover with a replenished native flight reserve.");
            TestGroundRunningAnimation(warhorse, horseType, horseData, "warhorse", report);
            TestGroundRunningAnimation(tengu, tenguType, tenguData, "tengu", report);

            warhorse.mount.SetMount(horseType, warhorse, true);
            if (!warhorse.mount.Active || warhorse.mount.Type != horseType)
                throw new InvalidOperationException("Native SetMount did not activate the warhorse.");
            warhorse.mount.UpdateFrame(warhorse, 3, Vector2.Zero);
            for (int i = 0; i < 120; i++)
                warhorse.mount.Flight();
            int horseExhausted = warhorse.mount.FlyTime;
            if (horseExhausted != 0)
                throw new InvalidOperationException($"Warhorse native flight reserve did not exhaust at 120 ticks: {horseExhausted}.");
            warhorse.mount.UpdateFrame(warhorse, 0, Vector2.Zero);
            warhorse.mount.ResetFlightTime(0f);
            if (warhorse.mount.FlyTime != 120)
                throw new InvalidOperationException("Native ResetFlightTime API did not restore the warhorse reserve.");
            report.AppendLine($"warhorse-native-setmount=PASS flightTicks=120 exhausted={horseExhausted} refreshed={warhorse.mount.FlyTime} dash={horseData.dashSpeed:0.00} unicornDash={Mount.mounts[MountID.Unicorn].dashSpeed:0.00}");

            tengu.mount.SetMount(tenguType, tengu, true);
            if (!tengu.mount.Active || tengu.mount.Type != tenguType)
                throw new InvalidOperationException("Native SetMount did not activate the tengu.");
            tengu.controlUp = false;
            tengu.controlDown = false;
            tengu.controlJump = false;
            tengu.velocity = Vector2.Zero;
            int minReserve = int.MaxValue;
            float maxAbsHoverY = 0f;
            Vector2 hoverVelocityBefore = tengu.velocity;
            for (int i = 0; i < 7200; i++)
            {
                tengu.mount.UpdateEffects(tengu); // Executes the real ModMount.UpdateEffects hook.
                tengu.mount.UpdateFrame(tengu, 2, tengu.velocity);
                tengu.mount.Hover(tengu);
                maxAbsHoverY = Math.Max(maxAbsHoverY, Math.Abs(tengu.velocity.Y));
                tengu.mount.Flight();
                minReserve = Math.Min(minReserve, tengu.mount.FlyTime);
                if (!tengu.mount.Active || tengu.mount.Type != tenguType)
                    throw new InvalidOperationException($"Tengu dismounted during sustained native flight at tick {i}.");
            }
            if (minReserve <= 0 || tengu.mount.FlyTime <= 0)
                throw new InvalidOperationException($"Tengu flight reserve depleted despite UpdateEffects refresh: min={minReserve}, final={tengu.mount.FlyTime}.");

            if (maxAbsHoverY > 1.01f)
                throw new InvalidOperationException($"Tengu no-input native hover Y velocity exceeded neutral threshold: {maxAbsHoverY:0.00}.");
            report.AppendLine($"tengu-native-hover=PASS noDirection=1 v0={hoverVelocityBefore} maxAbsVy={maxAbsHoverY:0.00} ticks=7200 minReserve={minReserve} finalReserve={tengu.mount.FlyTime} witchBroomFatigueExemption=IL-verified");
            TestTenguHoverControls(tengu, report);
            RenderNativeRiderSheets();
            report.AppendLine("mount-layer-draw=present; rider sheets use native PlayerRenderer and fixture-scoped Mount.Draw color neutralization; frame bands logged in mount-frame-audit.log");
            return report.ToString();
        }
        finally
        {
            warhorse.mount.Dismount(warhorse);
            tengu.mount.Dismount(tengu);
        }
    }

    private static void TestTenguHoverControls(Player actor, StringBuilder report)
    {
        var cases = new (string Name, bool Up, bool Down, int Direction)[]
        {
            ("up", true, false, 1), ("down", false, true, 1),
            ("left-facing", false, false, -1), ("right-facing", false, false, 1)
        };
        var rows = new List<string>();
        foreach (var test in cases)
        {
            actor.controlUp = test.Up;
            actor.controlDown = test.Down;
            actor.controlLeft = test.Direction < 0;
            actor.controlRight = test.Direction > 0;
            actor.controlJump = false;
            actor.direction = test.Direction;
            actor.velocity = Vector2.Zero;
            actor.mount.ResetFlightTime(0f);
            Vector2 maxVelocity = Vector2.Zero;
            for (int tick = 0; tick < 60; tick++)
            {
                actor.mount.UpdateEffects(actor);
                actor.mount.UpdateFrame(actor, 2, actor.velocity);
                actor.mount.Hover(actor);
                actor.mount.Flight();
                if (!float.IsFinite(actor.velocity.X) || !float.IsFinite(actor.velocity.Y))
                    throw new InvalidOperationException($"Tengu native Hover produced a non-finite velocity during {test.Name} input at tick {tick}: {actor.velocity}.");
                maxVelocity.X = Math.Max(maxVelocity.X, Math.Abs(actor.velocity.X));
                maxVelocity.Y = Math.Max(maxVelocity.Y, Math.Abs(actor.velocity.Y));
            }
            if (actor.direction != test.Direction || actor.mount.FlyTime <= 0)
                throw new InvalidOperationException($"Tengu lost orientation or flight reserve during {test.Name}: direction={actor.direction}, flight={actor.mount.FlyTime}.");
            if (test.Up && actor.velocity.Y >= 0f)
                throw new InvalidOperationException($"Tengu native hover did not ascend under Up input: {actor.velocity}.");
            if (test.Down && actor.velocity.Y <= 0f)
                throw new InvalidOperationException($"Tengu native hover did not descend under Down input: {actor.velocity}.");
            rows.Add($"{test.Name}:v={actor.velocity},maxAbs=({maxVelocity.X:0.00},{maxVelocity.Y:0.00}),direction={actor.direction},reserve={actor.mount.FlyTime}");
        }
        actor.controlUp = actor.controlDown = actor.controlLeft = actor.controlRight = false;
        report.AppendLine("tengu-hover-controls-finite=PASS " + string.Join("; ", rows));
    }

    private static void TestGroundRunningAnimation(Player actor, int mountType, Mount.MountData data, string label, StringBuilder report)
    {
        actor.mount.SetMount(mountType, actor, true);
        var rows = new List<string>();
        foreach (var speedCase in new (string Name, float Speed)[] { ("run", data.runSpeed), ("dash", data.dashSpeed) })
        {
            actor.mount.UpdateFrame(actor, 0, Vector2.Zero);
            for (int i = 0; i < data.standingFrameDelay * 2; i++)
                actor.mount.UpdateFrame(actor, 0, Vector2.Zero);
            int standFrame = ReadMountFrame(actor.mount);
            if (standFrame < data.standingFrameStart || standFrame >= data.standingFrameStart + Math.Max(1, data.standingFrameCount))
                throw new InvalidOperationException($"{label} failed to settle at its standing frame before {speedCase.Name}: frame={standFrame}.");

            actor.mount.UpdateFrame(actor, 1, new Vector2(speedCase.Speed, 0f));
            int previous = ReadMountFrame(actor.mount);
            int transitions = 0;
            int cycles = 0;
            const int ticks = 600;
            for (int tick = 0; tick < ticks; tick++)
            {
                actor.mount.UpdateFrame(actor, 1, new Vector2(speedCase.Speed, 0f));
                int frame = ReadMountFrame(actor.mount);
                if (frame != previous)
                {
                    int delta = (frame - data.runningFrameStart + data.runningFrameCount) % data.runningFrameCount;
                    int previousOffset = (previous - data.runningFrameStart + data.runningFrameCount) % data.runningFrameCount;
                    int advance = (delta - previousOffset + data.runningFrameCount) % data.runningFrameCount;
                    if (advance != 1)
                        throw new InvalidOperationException($"{label} {speedCase.Name} skipped running frames: {previous}->{frame} at tick {tick}.");
                    transitions++;
                    if (previous == data.runningFrameStart + data.runningFrameCount - 1 && frame == data.runningFrameStart)
                        cycles++;
                }
                previous = frame;
            }
            float framesPerSecond = transitions * 60f / ticks;
            float cyclesPerSecond = cycles * 60f / ticks;
            rows.Add($"{speedCase.Name}Speed={speedCase.Speed:0.00}:frameFPS={framesPerSecond:0.00},cyclesPerSecond={cyclesPerSecond:0.00},transitions={transitions}/{ticks}");
            if (speedCase.Name == "dash")
            {
                bool valid = label == "warhorse"
                    ? framesPerSecond is >= 10f and <= 12f && cyclesPerSecond is >= 1.2f and <= 1.6f
                    : framesPerSecond is >= 8f and <= 10f && cyclesPerSecond is >= 2f and <= 2.5f;
                if (!valid)
                    throw new InvalidOperationException($"{label} dash animation rate is outside its tuned range: {framesPerSecond:0.00} frame FPS, {cyclesPerSecond:0.00} cycles/s.");
            }
        }

        actor.mount.UpdateFrame(actor, 0, Vector2.Zero);
        for (int i = 0; i < data.standingFrameDelay * 2; i++)
            actor.mount.UpdateFrame(actor, 0, Vector2.Zero);
        int settledFrame = ReadMountFrame(actor.mount);
        if (settledFrame != data.standingFrameStart)
            throw new InvalidOperationException($"{label} did not return to its standing frame after stopping: frame={settledFrame}.");
        report.AppendLine($"{label}-ground-animation=PASS delay={data.runningFrameDelay},runFrames={data.runningFrameCount},settledStand={settledFrame}; {string.Join("; ", rows)}");
    }

    private static void TestNativeSummonsAndMountedWeapons(StringBuilder report, int horseType, int tenguType)
    {
        int playerIndex = Math.Clamp(Main.myPlayer, 0, Main.player.Length - 1);
        Player[] oldPlayers = Main.player;
        Projectile[] oldProjectiles = Main.projectile;
        Item[] oldItems = Main.item;
        Dust[] oldDust = Main.dust;
        NPC[] oldNpcs = Main.npc;
        int oldMouseX = Main.mouseX;
        int oldMouseY = Main.mouseY;
        int oldMyPlayer = Main.myPlayer;
        int oldNetMode = Main.netMode;
        bool oldMenu = Main.gameMenu;
        bool oldWorldGen = WorldGen.gen;
        try
        {
            Main.gameMenu = false;
            var setupPlayer = typeof(PlayerLoader).GetMethod("SetupPlayer", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("PlayerLoader.SetupPlayer is unavailable in native mount use fixture.");
            Player actor = oldPlayers[playerIndex].SerializedClone();
            setupPlayer.Invoke(null, new object[] { actor });
            actor.active = true;
            actor.dead = false;
            actor.whoAmI = playerIndex;
            actor.position = new Vector2(400f, 400f);
            actor.velocity = Vector2.Zero;
            actor.statLife = actor.statLifeMax = 200;
            actor.statMana = actor.statManaMax = 200;
            actor.isDisplayDollOrInanimate = false;
            actor.direction = 1;
            Main.player = (Player[])oldPlayers.Clone();
            Main.projectile = Enumerable.Range(0, Math.Max(oldProjectiles.Length, Main.maxProjectiles + 1)).Select(_ => new Projectile()).ToArray();
            Main.item = Enumerable.Range(0, Math.Max(oldItems.Length, Main.maxItems + 1)).Select(_ => new Item()).ToArray();
            Main.dust = Enumerable.Range(0, Math.Max(oldDust.Length, Main.maxDust + 1)).Select(_ => new Dust()).ToArray();
            Main.npc = Enumerable.Range(0, Math.Max(oldNpcs.Length, Main.maxNPCs + 1)).Select(_ => new NPC()).ToArray();
            Main.myPlayer = playerIndex;
            Main.netMode = NetmodeID.SinglePlayer;
            WorldGen.gen = false;
            Main.player[playerIndex] = actor;

            int horseItem = ModContent.ItemType<UndeadWarhorseSummon>();
            actor.inventory[0] = new Item();
            actor.inventory[0].SetDefaults(horseItem);
            actor.selectedItem = 0;
            actor.lastVisualizedSelectedItem = actor.inventory[0];
            actor.itemAnimation = actor.itemTime = 0;
            actor.controlUseItem = true;
            actor.releaseUseItem = true;
            Main.mouseX = (int)(actor.position.X + actor.width + 160);
            Main.mouseY = (int)(actor.position.Y + actor.height / 2f);
            for (int tick = 0; tick < 30 && !actor.mount.Active; tick++)
            {
                actor.controlUseItem = true;
                actor.ItemCheck();
                actor.PlayerFrame();
            }
            if (!actor.mount.Active || actor.mount.Type != horseType)
                throw new InvalidOperationException($"Native ItemCheck did not summon the undead warhorse: active={actor.mount.Active}, type={actor.mount.Type}, buff={actor.HasBuff(ModContent.BuffType<UndeadWarhorseMountBuff>())}, itemAnimation={actor.itemAnimation}, itemTime={actor.itemTime}, stack={actor.inventory[0].stack}, buffType={actor.inventory[0].buffType}, mountType={actor.inventory[0].mountType}.");
            report.AppendLine($"summon-itemcheck=PASS item={horseItem} mount={actor.mount.Type} buff={actor.HasBuff(ModContent.BuffType<UndeadWarhorseMountBuff>())}");

            actor.mount.Dismount(actor);
            if (actor.mount.Active)
                throw new InvalidOperationException("Native Dismount did not remove the summoned mount.");
            int tenguItem = ModContent.ItemType<CelestialTenguSummon>();
            actor.inventory[0] = new Item();
            actor.inventory[0].SetDefaults(tenguItem);
            actor.itemAnimation = actor.itemTime = 0;
            actor.controlUseItem = true;
            actor.releaseUseItem = true;
            for (int tick = 0; tick < 30 && !actor.mount.Active; tick++)
            {
                actor.controlUseItem = true;
                actor.ItemCheck();
                actor.PlayerFrame();
            }
            if (!actor.mount.Active || actor.mount.Type != tenguType)
                throw new InvalidOperationException($"Native ItemCheck did not summon the celestial tengu: active={actor.mount.Active}, type={actor.mount.Type}, buff={actor.HasBuff(ModContent.BuffType<CelestialTenguMountBuff>())}, itemAnimation={actor.itemAnimation}, itemTime={actor.itemTime}.");
            report.AppendLine($"tengu-summon-itemcheck=PASS item={tenguItem} mount={actor.mount.Type} buff={actor.HasBuff(ModContent.BuffType<CelestialTenguMountBuff>())}");
            actor.mount.Dismount(actor);
            if (actor.mount.Active)
                throw new InvalidOperationException("Native Dismount did not remove the celestial tengu.");
            report.AppendLine("native-dismount=PASS both summoned mounts");

            MethodInfo quickMount = typeof(Player).GetMethod("QuickMount", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("Native Player.QuickMount method was unavailable.");
            foreach (var quickCase in new (string Name, int Item, int Type)[]
            {
                ("warhorse", horseItem, horseType), ("tengu", tenguItem, tenguType)
            })
            {
                actor.mount.Dismount(actor);
                actor.miscEquips[3] = new Item();
                actor.miscEquips[3].SetDefaults(quickCase.Item);
                quickMount.Invoke(actor, null);
                if (!actor.mount.Active || actor.mount.Type != quickCase.Type)
                    throw new InvalidOperationException($"Native QuickMount did not equip {quickCase.Name} from mount slot: active={actor.mount.Active}, type={actor.mount.Type}.");
                quickMount.Invoke(actor, null);
                if (actor.mount.Active)
                    throw new InvalidOperationException($"Native QuickMount did not dismount {quickCase.Name} on the second invocation.");
                report.AppendLine($"native-quickmount={quickCase.Name} PASS mount-slot=3 summon-and-toggle-off");
            }

            actor.inventory[0] = new Item();
            actor.inventory[0].SetDefaults(ModContent.ItemType<IronSword>());
            actor.selectedItem = 0;
            actor.lastVisualizedSelectedItem = actor.inventory[0];
            actor.itemAnimation = actor.itemTime = 0;
            actor.noItems = false;
            actor.controlUseItem = true;
            actor.releaseUseItem = true;
            actor.ResetEffects();
            actor.UpdateEquips(actor.whoAmI);
            actor.UpdateArmorSets(actor.whoAmI);
            PlayerLoader.PostUpdateEquips(actor);
            actor.ItemCheck();
            int unmountedSwordAnimation = actor.itemAnimation;
            if (unmountedSwordAnimation <= 0)
                throw new InvalidOperationException($"Native unmounted sword ItemCheck fixture failed before the mounted comparison: animation={actor.itemAnimation}, itemTime={actor.itemTime}, useStyle={actor.inventory[0].useStyle}, release={actor.releaseUseItem}, noItems={actor.noItems}.");
            report.AppendLine($"unmounted-sword-itemcheck=PASS animation={unmountedSwordAnimation}");

            var weaponCases = new (string Name, int Item, bool Ammo, bool Mana)[]
            {
                ("IronSword", ModContent.ItemType<IronSword>(), false, false),
                ("IronBow", ModContent.ItemType<IronBow>(), true, false),
                ("IronStaff", ModContent.ItemType<IronStaff>(), false, true)
            };
            int mountedCases = 0;
            foreach (var mountCase in new (string Name, int Type)[] { ("warhorse", horseType), ("tengu", tenguType) })
            foreach (int direction in new[] { -1, 1 })
            foreach (var test in weaponCases)
            {
                actor.mount.Dismount(actor);
                actor.mount.SetMount(mountCase.Type, actor, true);
                Main.projectile = Enumerable.Range(0, Math.Max(oldProjectiles.Length, Main.maxProjectiles + 1)).Select(_ => new Projectile()).ToArray();
                actor.inventory[0] = new Item();
                actor.inventory[0].SetDefaults(test.Item);
                actor.selectedItem = 0;
                actor.lastVisualizedSelectedItem = actor.inventory[0];
                actor.itemAnimation = actor.itemTime = 0;
                actor.noItems = false;
                actor.heldProj = -1;
                actor.controlUseItem = true;
                actor.releaseUseItem = true;
                actor.direction = direction;
                actor.statMana = actor.statManaMax = 200;
                actor.ResetEffects();
                actor.UpdateEquips(actor.whoAmI);
                actor.UpdateArmorSets(actor.whoAmI);
                PlayerLoader.PostUpdateEquips(actor);
                if (test.Ammo)
                {
                    actor.inventory[1] = new Item();
                    actor.inventory[1].SetDefaults(ItemID.WoodenArrow);
                    actor.inventory[1].stack = 99;
                }
                int ammoBefore = actor.inventory[1].stack;
                int manaBefore = actor.statMana;
                Vector2 center = actor.Center - Main.screenPosition;
                Main.mouseX = (int)(center.X + 200f * direction);
                Main.mouseY = (int)center.Y;
                actor.ItemCheck();
                actor.PlayerFrame();
                int ownedShots = 0;
                for (int i = 0; i < Main.projectile.Length; i++)
                    if (Main.projectile[i].active && Main.projectile[i].owner == playerIndex) ownedShots++;
                if (!actor.mount.Active || actor.mount.Type != mountCase.Type)
                    throw new InvalidOperationException($"Mounted {mountCase.Name}/{test.Name}/{direction} ItemCheck changed the active mount.");
                if (test.Ammo && ownedShots == 0)
                    throw new InvalidOperationException($"Mounted {mountCase.Name}/{test.Name}/{direction} ItemCheck did not fire a projectile.");
                if (test.Name == "IronSword" && actor.itemAnimation <= 0)
                    throw new InvalidOperationException($"Mounted sword ItemCheck did not begin its melee animation: itemTime={actor.itemTime} useTime={actor.inventory[0].useTime} useAnimation={actor.inventory[0].useAnimation} release={actor.releaseUseItem} noItems={actor.noItems} active={actor.active} dead={actor.dead}.");
                if (test.Mana && actor.statMana >= manaBefore)
                    throw new InvalidOperationException($"Mounted {mountCase.Name}/{test.Name}/{direction} did not consume mana.");
                if (test.Ammo && actor.inventory[1].stack > ammoBefore)
                    throw new InvalidOperationException($"Mounted {mountCase.Name}/{test.Name}/{direction} increased ammo unexpectedly.");
                report.AppendLine($"mounted-itemcheck={mountCase.Name}/{test.Name}/{(direction < 0 ? "left" : "right")} PASS projectiles={ownedShots} mount={actor.mount.Type} animation={actor.itemAnimation} ammo={ammoBefore}->{actor.inventory[1].stack} mana={manaBefore}->{actor.statMana}");
                mountedCases++;
            }
            if (mountedCases != 12)
                throw new InvalidOperationException($"Expected 12 mounted mod-weapon ItemCheck cases, got {mountedCases}.");
            report.AppendLine("mounted-mod-weapon-itemcheck-matrix=PASS cases=12 mounts=2 directions=2 weapons=IronSword,IronBow,IronStaff");
            report.AppendLine(AuditMountRecipeConditions());
            report.AppendLine(AuditMountSummonShimmer(horseItem, tenguItem));
        }
        finally
        {
            Main.player = oldPlayers;
            Main.projectile = oldProjectiles;
            Main.item = oldItems;
            Main.dust = oldDust;
            Main.npc = oldNpcs;
            Main.myPlayer = oldMyPlayer;
            Main.netMode = oldNetMode;
            Main.mouseX = oldMouseX;
            Main.mouseY = oldMouseY;
            Main.gameMenu = oldMenu;
            WorldGen.gen = oldWorldGen;
        }
    }

    private static string AuditMountSummonShimmer(int horseItem, int tenguItem)
    {
        int[] items = { horseItem, tenguItem };
        var results = new List<string>();
        MethodInfo getRecipe = typeof(ShimmerTransforms).GetMethod("GetDecraftingRecipeIndex", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Native shimmer decraft recipe lookup was unavailable.");
        MethodInfo getShimmered = typeof(Item).GetMethod("GetShimmered", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Native Item.GetShimmered was unavailable.");
        foreach (int type in items)
        {
            int recipeIndex = (int)getRecipe.Invoke(null, new object[] { type })!;
            if (recipeIndex < 0 || recipeIndex >= Main.recipe.Length)
                throw new InvalidOperationException($"Summon item {Lang.GetItemNameValue(type)} has no native decrafting recipe.");
            Recipe recipe = Main.recipe[recipeIndex];
            if (!RecipeLoader.DecraftAvailable(recipe))
                throw new InvalidOperationException($"Native decrafting recipe unavailable for {Lang.GetItemNameValue(type)}.");
            Item source = new Item();
            source.SetDefaults(type);
            source.stack = Math.Max(1, recipe.createItem.stack);
            source.active = true;
            source.Center = new Vector2(800f, 800f);
            if (!source.CanShimmer())
                throw new InvalidOperationException($"Summon item {Lang.GetItemNameValue(type)} cannot shimmer through native CanShimmer.");

            Main.item = Enumerable.Range(0, Math.Max(Main.item.Length, Main.maxItems + 1)).Select(_ => new Item()).ToArray();
            getShimmered.Invoke(source, null);
            var actual = new Dictionary<int, int>();
            foreach (Item drop in Main.item)
                if (drop is { active: true } && drop.type > ItemID.None && drop.stack > 0)
                    actual[drop.type] = actual.GetValueOrDefault(drop.type) + drop.stack;
            var expected = new Dictionary<int, int>();
            foreach (Item ingredient in recipe.requiredItem)
                if (ingredient is not null && ingredient.type > ItemID.None && ingredient.stack > 0)
                    expected[ingredient.type] = expected.GetValueOrDefault(ingredient.type) + ingredient.stack;
            if (source.type != ItemID.None || source.stack != 0 || expected.Count != actual.Count || expected.Any(pair => actual.GetValueOrDefault(pair.Key) != pair.Value))
                throw new InvalidOperationException($"Native shimmer mismatch for {Lang.GetItemNameValue(type)}: expected={string.Join(',', expected.Select(p => $"{Lang.GetItemNameValue(p.Key)}x{p.Value}"))}; actual={string.Join(',', actual.Select(p => $"{Lang.GetItemNameValue(p.Key)}x{p.Value}"))}; source={source.type}/{source.stack}.");
            results.Add($"{Lang.GetItemNameValue(type)}=>{string.Join('+', actual.Select(p => $"{Lang.GetItemNameValue(p.Key)}x{p.Value}"))}");
        }
        return "native-mount-recipes-shimmer=PASS " + string.Join("; ", results);
    }

    private static string AuditMountRecipeConditions()
    {
        bool oldMech = NPC.downedMechBossAny;
        bool oldGolem = NPC.downedGolemBoss;
        try
        {
            Recipe horse = Main.recipe.FirstOrDefault(r => r.createItem.type == ModContent.ItemType<UndeadWarhorseSummon>())
                ?? throw new InvalidOperationException("Warhorse recipe was not registered.");
            Recipe tengu = Main.recipe.FirstOrDefault(r => r.createItem.type == ModContent.ItemType<CelestialTenguSummon>())
                ?? throw new InvalidOperationException("Tengu recipe was not registered.");
            if (horse.Conditions.Count == 0 || tengu.Conditions.Count == 0)
                throw new InvalidOperationException("Mount recipes are missing their progression conditions.");
            NPC.downedMechBossAny = false;
            NPC.downedGolemBoss = false;
            bool horseBefore = horse.Conditions.All(c => c.IsMet());
            bool tenguBefore = tengu.Conditions.All(c => c.IsMet());
            NPC.downedMechBossAny = true;
            bool horseAtT3 = horse.Conditions.All(c => c.IsMet());
            bool tenguAtT3 = tengu.Conditions.All(c => c.IsMet());
            NPC.downedGolemBoss = true;
            bool horseAtT4 = horse.Conditions.All(c => c.IsMet());
            bool tenguAtT4 = tengu.Conditions.All(c => c.IsMet());
            if (horseBefore || tenguBefore || !horseAtT3 || tenguAtT3 || !horseAtT4 || !tenguAtT4)
                throw new InvalidOperationException($"Mount progression condition mismatch: horse false/T3/T4={horseBefore}/{horseAtT3}/{horseAtT4}, tengu={tenguBefore}/{tenguAtT3}/{tenguAtT4}.");
            return "mount-recipe-progression=PASS horse false→mech→golem; tengu false→golem";
        }
        finally
        {
            NPC.downedMechBossAny = oldMech;
            NPC.downedGolemBoss = oldGolem;
        }
    }

    private static string GetBackTextureSize(object mountData)
    {
        var field = mountData.GetType().GetField("backTexture", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
        object asset = field?.GetValue(mountData);
        if (asset == null)
            return "missing";
        object texture = asset.GetType().GetProperty("Value")?.GetValue(asset);
        if (texture is null)
            return "unloaded";
        int width = (int)texture.GetType().GetProperty("Width")!.GetValue(texture)!;
        int height = (int)texture.GetType().GetProperty("Height")!.GetValue(texture)!;
        return $"{width}x{height}";
    }

    private static Texture2D GetBackTexture(object mountData)
    {
        FieldInfo field = mountData.GetType().GetField("backTexture", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("MountData.backTexture was not found.");
        object asset = field.GetValue(mountData) ?? throw new InvalidOperationException("Mount back texture asset is missing.");
        return (Texture2D)(asset.GetType().GetProperty("Value")?.GetValue(asset)
            ?? throw new InvalidOperationException("Mount back texture is not loaded."));
    }

    private static int ReadMountFrame(Mount mount)
    {
        FieldInfo field = typeof(Mount).GetField("_frame", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Native Mount._frame field was not found.");
        return (int)field.GetValue(mount)!;
    }

    private static void RenderNativeRiderSheets()
    {
        GraphicsDevice device = Main.graphics.GraphicsDevice;
        const int width = 1280;
        const int height = 900;
        int playerIndex = Math.Clamp(Main.myPlayer, 0, Main.player.Length - 1);
        Player previousPlayer = Main.player[playerIndex];
        bool previousMenu = Main.gameMenu;
        float previousUiScale = Main.UIScale;
        Vector2 previousScreen = Main.screenPosition;
        Vector2 previousZoom = Main.GameViewMatrix.Zoom;
        var previousLighting = SmokeSystem.ForcePreviewLighting;
        var suits = new (string Name, int Head, int Body, int Legs)[]
        {
            ("iron", ModContent.ItemType<IronHelmet>(), ModContent.ItemType<IronChestplate>(), ModContent.ItemType<IronGreaves>()),
            ("leather", ModContent.ItemType<LeatherHeadband>(), ModContent.ItemType<LeatherTunic>(), ModContent.ItemType<LeatherLeggings>()),
            ("knight", ModContent.ItemType<KnightHelmet>(), ModContent.ItemType<KnightChestplate>(), ModContent.ItemType<KnightGreaves>()),
            ("hunting", ModContent.ItemType<HuntingHood>(), ModContent.ItemType<HuntingChestplate>(), ModContent.ItemType<HuntingLeggings>()),
            ("cloth", ModContent.ItemType<ClothHood>(), ModContent.ItemType<ClothRobe>(), ModContent.ItemType<ClothLeggings>()),
            ("energy", ModContent.ItemType<EnergyHood>(), ModContent.ItemType<EnergyRobe>(), ModContent.ItemType<EnergyLeggings>()),
            ("templar", ModContent.ItemType<TemplarHelmet>(), ModContent.ItemType<TemplarChestplate>(), ModContent.ItemType<TemplarGreaves>()),
            ("nightmare", ModContent.ItemType<NightmareHood>(), ModContent.ItemType<NightmareChestplate>(), ModContent.ItemType<NightmareLeggings>()),
            ("fallen", ModContent.ItemType<FallenHood>(), ModContent.ItemType<FallenRobe>(), ModContent.ItemType<FallenLeggings>()),
            ("soulforged", ModContent.ItemType<SoulforgedHelmet>(), ModContent.ItemType<SoulforgedChestplate>(), ModContent.ItemType<SoulforgedGreaves>()),
            ("abyss", ModContent.ItemType<AbyssHood>(), ModContent.ItemType<AbyssChestplate>(), ModContent.ItemType<AbyssLeggings>()),
            ("dark", ModContent.ItemType<DarkHood>(), ModContent.ItemType<DarkRobe>(), ModContent.ItemType<DarkLeggings>())
        };
        var mountTypes = new (string Name, int Type)[]
        {
            ("warhorse", ModContent.MountType<UndeadWarhorseMount>()),
            ("tengu", ModContent.MountType<CelestialTenguMount>())
        };
        var poses = new (string Name, int MountState, float XVelocity, float YVelocity)[]
        {
            ("stand", 0, 0f, 0f), ("run", 1, 3f, 0f), ("jump", 2, 2f, -5f), ("fly", 3, 2f, -2f)
        };
        bool batchBegun = false;
        using var target = new RenderTarget2D(device, width, height, false, SurfaceFormat.Color,
            DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
        MethodInfo mountDrawMethod = typeof(Mount).GetMethod("Draw",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            types: new[] { typeof(List<DrawData>), typeof(int), typeof(Player), typeof(Vector2), typeof(Color), typeof(SpriteEffects), typeof(float) },
            modifiers: null) ?? throw new InvalidOperationException("Could not locate native Mount.Draw signature.");
        using var mountDrawHook = new Hook(mountDrawMethod, (MountDrawDetour)NeutralizeNativeMountDrawColor);
        try
        {
            _forceWhiteMountColor = true;
            Main.gameMenu = false;
            Main.UIScale = 1f;
            Main.screenPosition = Vector2.Zero;
            Main.GameViewMatrix.Zoom = Vector2.One;
            SmokeSystem.ForcePreviewLighting = true;
            foreach (var mountSpec in mountTypes)
            for (int page = 0; page < 2; page++)
            {
                device.SetRenderTarget(target);
                device.Clear(new Color(28, 31, 39));
                Main.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp,
                    DepthStencilState.None, RasterizerState.CullCounterClockwise);
                batchBegun = true;
                // Warm the native player/mount projection before capturing this page.
                Player warm = SmokeSystem.CreatePreviewPlayer(suits[page * 6].Head, suits[page * 6].Body, suits[page * 6].Legs);
                warm.whoAmI = playerIndex;
                warm.isDisplayDollOrInanimate = false;
                warm.mount.SetMount(mountSpec.Type, warm, true);
                SmokeSystem.DrawPreviewPlayer(warm, -500, -500, 1f);
                Main.spriteBatch.End();
                batchBegun = false;
                device.Clear(new Color(28, 31, 39));
                Main.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp,
                    DepthStencilState.None, RasterizerState.CullCounterClockwise);
                batchBegun = true;

                for (int row = 0; row < 6; row++)
                {
                    var suit = suits[page * 6 + row];
                    for (int col = 0; col < 8; col++)
                    {
                        var pose = poses[col / 2];
                        int direction = col % 2 == 0 ? 1 : -1;
                        Player actor = SmokeSystem.CreatePreviewPlayer(suit.Head, suit.Body, suit.Legs,
                            walk: pose.Name == "run", jump: pose.Name == "jump");
                        actor.whoAmI = playerIndex;
                        actor.isDisplayDollOrInanimate = false;
                        actor.direction = direction;
                        actor.velocity = new Vector2(direction * pose.XVelocity, pose.YVelocity);
                        actor.oldVelocity = actor.velocity;
                        if (pose.Name == "fly")
                        {
                            actor.controlJump = true;
                            actor.controlUp = true;
                        }
                        actor.mount.SetMount(mountSpec.Type, actor, true);
                        var mountData = Mount.mounts[mountSpec.Type];
                        int nativeState = pose.Name == "fly" && mountData.usesHover ? 2 : pose.MountState;
                        if (pose.Name == "fly")
                        {
                            actor.mount.ResetFlightTime(0f);
                            actor.controlJump = true;
                            actor.jump = 0;
                        }
                        int frameTicks = pose.Name switch { "run" => 48, "jump" => 18, "fly" => 60, _ => 1 };
                        for (int frameTick = 0; frameTick < frameTicks; frameTick++)
                            actor.PlayerFrame();
                        int actualFrame = ReadMountFrame(actor.mount);
                        bool inExpectedBand = pose.Name switch
                        {
                            "stand" => actualFrame >= mountData.standingFrameStart && actualFrame < mountData.standingFrameStart + mountData.standingFrameCount,
                            "run" => actualFrame >= mountData.runningFrameStart && actualFrame < mountData.runningFrameStart + mountData.runningFrameCount,
                            "jump" => actualFrame >= mountData.inAirFrameStart && actualFrame < mountData.inAirFrameStart + mountData.inAirFrameCount,
                            "fly" when mountData.usesHover => actualFrame >= mountData.inAirFrameStart && actualFrame < mountData.inAirFrameStart + mountData.inAirFrameCount,
                            "fly" => actualFrame >= mountData.flyingFrameStart && actualFrame < mountData.flyingFrameStart + mountData.flyingFrameCount,
                            _ => false
                        };
                        if (!inExpectedBand)
                            throw new InvalidOperationException($"{mountSpec.Name}/{pose.Name} selected frame {actualFrame}, outside its configured frame band; nativeState={nativeState}, jump={actor.jump}, controlJump={actor.controlJump}, flyTime={actor.mount.FlyTime}, velocity={actor.velocity}.");
                        Main.player[playerIndex] = actor;
                        bool captureGround = suit.Name == "iron" && pose.Name == "stand" && direction == 1;
                        if (captureGround)
                        {
                            _mountGeometryRows.Clear();
                            _mountGeometryLabel = mountSpec.Name;
                            _captureMountGeometry = true;
                        }
                        try
                        {
                            SmokeSystem.DrawPreviewPlayer(actor, 85 + col * 145, 62 + row * 142, 1f);
                        }
                        finally
                        {
                            _captureMountGeometry = false;
                        }
                        int drawnFrame = ReadMountFrame(actor.mount);
                        if (drawnFrame != actualFrame)
                            throw new InvalidOperationException($"{mountSpec.Name}/{pose.Name} changed mount frame during native PlayerRenderer: {actualFrame}->{drawnFrame}.");
                        File.AppendAllText(Path.Combine(Environment.GetEnvironmentVariable("MILKCRAFT_TERRARIA_SMOKE_OUT") ?? Environment.CurrentDirectory, "mount-frame-audit.log"),
                            $"{mountSpec.Name},{suit.Name},{pose.Name},{direction},state={nativeState},drawFrame={drawnFrame}\n");
                        if (captureGround)
                        {
                            File.AppendAllText(Path.Combine(Environment.GetEnvironmentVariable("MILKCRAFT_TERRARIA_SMOKE_OUT") ?? Environment.CurrentDirectory, "mount-ground-clearance.log"),
                                $"{mountSpec.Name},frame={drawnFrame},heightBoost={mountData.heightBoost},mountedActorHeight={actor.height},collisionBottom={actor.position.Y + actor.height:0.0}; " +
                                string.Join(" | ", _mountGeometryRows) + "\n");
                        }
                    }
                }
                Main.spriteBatch.End();
                batchBegun = false;
                device.SetRenderTarget(null);
                string filename = $"mount-riders-{mountSpec.Name}-page-{page + 1}.png";
                using FileStream output = File.Create(Path.Combine(Environment.GetEnvironmentVariable("MILKCRAFT_TERRARIA_SMOKE_OUT") ?? Environment.CurrentDirectory, filename));
                target.SaveAsPng(output, width, height);
            }
            RenderSeatOffsetCandidates(device, playerIndex, mountTypes);
        }
        finally
        {
            if (batchBegun)
                Main.spriteBatch.End();
            device.SetRenderTarget(null);
            Main.player[playerIndex] = previousPlayer;
            Main.gameMenu = previousMenu;
            Main.UIScale = previousUiScale;
            Main.screenPosition = previousScreen;
            Main.GameViewMatrix.Zoom = previousZoom;
            SmokeSystem.ForcePreviewLighting = previousLighting;
            _forceWhiteMountColor = false;
        }
    }

    private static void RenderSeatOffsetCandidates(GraphicsDevice device, int playerIndex, (string Name, int Type)[] mountTypes)
    {
        const int width = 1260;
        const int height = 700;
        int oldScreenWidth = Main.screenWidth;
        int oldScreenHeight = Main.screenHeight;
        Vector2 oldScreen = Main.screenPosition;
        Vector2 oldZoom = Main.GameViewMatrix.Zoom;
        bool begun = false;
        using var target = new RenderTarget2D(device, width, height, false, SurfaceFormat.Color,
            DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
        try
        {
            Main.screenWidth = width;
            Main.screenHeight = height;
            Main.screenPosition = Vector2.Zero;
            Main.GameViewMatrix.Zoom = Vector2.One;
            device.SetRenderTarget(target);
            device.Clear(new Color(28, 31, 39));
            Main.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp,
                DepthStencilState.None, RasterizerState.CullCounterClockwise);
            begun = true;
            var font = FontAssets.MouseText.Value;
            Terraria.UI.Chat.ChatManager.DrawColorCodedStringWithShadow(Main.spriteBatch, font,
                "Native Mount.Draw seat offset candidates; both directions / mod Iron set", new Vector2(20, 12), Color.White, 0f, Vector2.Zero, Vector2.One, 1200f, 1f);

            for (int mountRow = 0; mountRow < mountTypes.Length; mountRow++)
            {
                var spec = mountTypes[mountRow];
                Mount.MountData data = Mount.mounts[spec.Type];
                int[] oldOffsets = (int[])data.playerYOffsets.Clone();
                int[] candidates = spec.Name == "warhorse" ? new[] { 0, 8, 16, 24, 32, 40 } : new[] { 0, 6, 12, 18, 24, 30 };
                for (int index = 0; index < candidates.Length; index++)
                {
                    int col = index % 3;
                    int row = index / 3;
                    int left = 20 + col * 415;
                    int top = 52 + row * 315;
                    Array.Fill(data.playerYOffsets, candidates[index]);
                    Terraria.UI.Chat.ChatManager.DrawColorCodedStringWithShadow(Main.spriteBatch, font,
                        $"{spec.Name}: playerYOffsets={candidates[index]}", new Vector2(left + 8, top), Color.LightGoldenrodYellow, 0f, Vector2.Zero, Vector2.One, 400f, 1f);
                    foreach (int direction in new[] { 1, -1 })
                    {
                        Player actor = SmokeSystem.CreatePreviewPlayer(ModContent.ItemType<IronHelmet>(),
                            ModContent.ItemType<IronChestplate>(), ModContent.ItemType<IronGreaves>());
                        actor.whoAmI = playerIndex;
                        actor.isDisplayDollOrInanimate = false;
                        actor.direction = direction;
                        actor.mount.SetMount(spec.Type, actor, true);
                        actor.mount.UpdateFrame(actor, 0, Vector2.Zero);
                        actor.PlayerFrame();
                        Main.player[playerIndex] = actor;
                        SmokeSystem.DrawPreviewPlayer(actor, left + (direction > 0 ? 132 : 290), top + 214, 1f);
                    }
                }
                Array.Copy(oldOffsets, data.playerYOffsets, oldOffsets.Length);
                // Save each mount's six candidates on its own row/sheet for review.
                Main.spriteBatch.End();
                begun = false;
                device.SetRenderTarget(null);
                string file = $"mount-seat-candidates-{spec.Name}.png";
                using FileStream output = File.Create(Path.Combine(Environment.GetEnvironmentVariable("MILKCRAFT_TERRARIA_SMOKE_OUT") ?? Environment.CurrentDirectory, file));
                target.SaveAsPng(output, width, height);
                if (mountRow + 1 < mountTypes.Length)
                {
                    device.SetRenderTarget(target);
                    device.Clear(new Color(28, 31, 39));
                    Main.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp,
                        DepthStencilState.None, RasterizerState.CullCounterClockwise);
                    begun = true;
                }
            }
        }
        finally
        {
            if (begun)
                Main.spriteBatch.End();
            device.SetRenderTarget(null);
            Main.screenWidth = oldScreenWidth;
            Main.screenHeight = oldScreenHeight;
            Main.screenPosition = oldScreen;
            Main.GameViewMatrix.Zoom = oldZoom;
        }
    }

    private static void NeutralizeNativeMountDrawColor(OrigMountDraw orig, Mount self,
        List<DrawData> drawData, int drawType, Player drawPlayer, Vector2 position,
        Color drawColor, SpriteEffects spriteEffects, float shadow)
    {
        int start = drawData.Count;
        orig(self, drawData, drawType, drawPlayer, position,
            _forceWhiteMountColor ? Color.White : drawColor, spriteEffects, shadow);
        if (!_captureMountGeometry || self.Type <= 0)
            return;

        Texture2D expected = GetBackTexture(Mount.mounts[self.Type]);
        for (int i = start; i < drawData.Count; i++)
        {
            DrawData data = drawData[i];
            if (!ReferenceEquals(data.texture, expected) || data.sourceRect is not Rectangle source)
                continue;

            var pixels = new Color[source.Width * source.Height];
            expected.GetData(0, source, pixels, 0, pixels.Length);
            int opaqueBottom = -1;
            for (int y = source.Height - 1; y >= 0 && opaqueBottom < 0; y--)
                for (int x = 0; x < source.Width; x++)
                    if (pixels[y * source.Width + x].A > 0) { opaqueBottom = y; break; }
            float bottomWorld = data.position.Y + Main.screenPosition.Y +
                (opaqueBottom - data.origin.Y) * data.scale.Y;
            float actorBottom = drawPlayer.position.Y + drawPlayer.height;
            _mountGeometryRows.Add(
                $"nativeDrawData=pos({data.position.X:0.0},{data.position.Y:0.0}) src({source.X},{source.Y},{source.Width},{source.Height}) opaqueBottomInSource={opaqueBottom} " +
                $"origin({data.origin.X:0.0},{data.origin.Y:0.0}) scale({data.scale.X:0.00},{data.scale.Y:0.00}) " +
                $"mountVisualBottomWorld={bottomWorld:0.0} playerCollisionBottomWorld={actorBottom:0.0} delta={bottomWorld - actorBottom:0.0}px");
        }
    }
}
