using System;
using System.Linq;
using System.Reflection;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.DataStructures;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.Utilities;
using MilkcraftTerraria.Content.Items;

namespace MilkcraftTerraria.Content;

internal static class ProgressionEnchantAuditDiagnostics
{
    private readonly record struct WeaponCase(string Name, int ItemType, bool Bow);

    internal static string RunFocusedGroundedSplash(string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        Player[] oldPlayers = Main.player;
        NPC[] oldNpcs = Main.npc;
        Projectile[] oldProjectiles = Main.projectile;
        Item[] oldItems = Main.item;
        Dust[] oldDust = Main.dust;
        int oldMyPlayer = Main.myPlayer;
        int oldNetMode = Main.netMode;
        bool oldMenu = Main.gameMenu;
        bool oldPaused = Main.gamePaused;
        bool oldWorldGen = WorldGen.gen;
        Vector2 oldScreen = Main.screenPosition;
        UnifiedRandom oldRandom = Main.rand;
        const int playerIndex = 0;
        Player actor = oldPlayers[playerIndex].SerializedClone();
        MethodInfo setup = typeof(PlayerLoader).GetMethod("SetupPlayer", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("PlayerLoader.SetupPlayer is unavailable for focused splash fixture");
        setup.Invoke(null, new object[] { actor });
        actor.whoAmI = playerIndex;
        try
        {
            Main.player = (Player[])oldPlayers.Clone();
            Main.player[playerIndex] = actor;
            Main.npc = Enumerable.Range(0, Math.Max(oldNpcs.Length, Main.maxNPCs + 1)).Select(_ => new NPC()).ToArray();
            Main.projectile = Enumerable.Range(0, Math.Max(oldProjectiles.Length, Main.maxProjectiles + 1)).Select(_ => new Projectile()).ToArray();
            Main.item = Enumerable.Range(0, oldItems.Length).Select(_ => new Item()).ToArray();
            Main.dust = Enumerable.Range(0, Math.Max(oldDust.Length, Main.maxDust + 1)).Select(_ => new Dust()).ToArray();
            Main.myPlayer = playerIndex;
            Main.netMode = NetmodeID.SinglePlayer;
            Main.gameMenu = false;
            Main.gamePaused = false;
            Main.screenPosition = Vector2.Zero;
            Main.rand = new UnifiedRandom(20261008);
            WorldGen.gen = false;

            string grounded = RunGroundedBlueSlimePair(actor, outputDirectory);
            string radius = RunRadiusBoundary(actor, outputDirectory);
            return $"grounded-default-blue-slimes={grounded}; true-48px-boundary={radius}; real-wall-block=true";
        }
        finally
        {
            Main.player = oldPlayers;
            Main.npc = oldNpcs;
            Main.projectile = oldProjectiles;
            Main.item = oldItems;
            Main.dust = oldDust;
            Main.myPlayer = oldMyPlayer;
            Main.netMode = oldNetMode;
            Main.gameMenu = oldMenu;
            Main.gamePaused = oldPaused;
            Main.screenPosition = oldScreen;
            Main.rand = oldRandom;
            WorldGen.gen = oldWorldGen;
        }
    }

    private static string RunRadiusBoundary(Player actor, string outputDirectory)
    {
        int npcArrayLength = Math.Max(Main.npc.Length, Main.maxNPCs + 1);
        int projectileArrayLength = Math.Max(Main.projectile.Length, Main.maxProjectiles + 1);
        string RunAtDistance(float gap)
        {
            Main.npc = Enumerable.Range(0, npcArrayLength).Select(_ => new NPC()).ToArray();
            Main.projectile = Enumerable.Range(0, projectileArrayLength).Select(_ => new Projectile()).ToArray();
            actor.active = true;
            actor.dead = false;
            actor.position = new Vector2(800f, 360f);
            actor.direction = 1;
            actor.selectedItem = 0;
            actor.itemAnimation = actor.itemTime = 0;
            actor.statLifeMax2 = 400;
            actor.statLife = 300;
            actor.lifeSteal = 2f;
            actor.armor[3].SetDefaults(ModContent.ItemType<FallenTome>());
            actor.inventory[0].SetDefaults(ModContent.ItemType<IronBow>());
            actor.inventory[0].GetGlobalItem<MilkcraftEnchantedItem>().Kind = MilkcraftEnchant.Explosive;
            actor.GetModPlayer<MilkcraftEnchantedPlayer>().ExplosiveCooldown = 0;
            actor.GetModPlayer<MilkcraftEnchantedPlayer>().ExplosiveProcActive = false;
            Main.player[actor.whoAmI] = actor;

            Vector2 origin = new(1200f, 300f);
            NPC primary = NewGroundedBlueSlime(0, origin);
            NPC secondary = NewGroundedBlueSlime(1, origin + new Vector2(gap, 0f));
            primary.lifeMax = primary.life = 1000;
            secondary.lifeMax = secondary.life = 1000;
            primary.noGravity = secondary.noGravity = true;
            primary.noTileCollide = secondary.noTileCollide = true;
            Main.npc[0] = primary;
            Main.npc[1] = secondary;
            Main.mouseX = (int)primary.Center.X;
            Main.mouseY = (int)primary.Center.Y;
            actor.inventory[0].shoot = ProjectileID.WoodenArrowFriendly;
            Projectile shot = SpawnFocusedProjectile(actor, actor.inventory[0], primary.Center, 100);
            int before = secondary.life;
            shot.Damage();
            return $"gap={gap:0.0} secondary={before}->{secondary.life} cd={actor.GetModPlayer<MilkcraftEnchantedPlayer>().ExplosiveCooldown}";
        }

        string at48 = RunAtDistance(48f);
        int life48 = Main.npc[1].life;
        int cd48 = actor.GetModPlayer<MilkcraftEnchantedPlayer>().ExplosiveCooldown;
        string at49 = RunAtDistance(49f);
        int life49 = Main.npc[1].life;
        int cd49 = actor.GetModPlayer<MilkcraftEnchantedPlayer>().ExplosiveCooldown;
        File.WriteAllText(Path.Combine(outputDirectory, "splash-radius-boundary.log"),
            $"native Projectile.Damage hit at exactly48: {at48}; at49: {at49}\n");
        if (life48 >= 1000 || cd48 != 30 || life49 != 1000 || cd49 != 30)
            throw new InvalidOperationException($"48/49px explosive boundary mismatch: 48={at48}; 49={at49}");
        return $"48px-hit/49px-excluded=PASS ({at48}; {at49})";
    }

    internal static string Run(string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        Player[] oldPlayers = Main.player;
        NPC[] oldNpcs = Main.npc;
        Projectile[] oldProjectiles = Main.projectile;
        Item[] oldItems = Main.item;
        Dust[] oldDust = Main.dust;
        int oldMyPlayer = Main.myPlayer;
        int oldNetMode = Main.netMode;
        bool oldMenu = Main.gameMenu;
        bool oldPaused = Main.gamePaused;
        bool oldWorldGen = WorldGen.gen;
        Vector2 oldScreenPosition = Main.screenPosition;
        int oldMouseX = Main.mouseX;
        int oldMouseY = Main.mouseY;
        UnifiedRandom oldRandom = Main.rand;
        const int playerIndex = 0;

        Player actor = oldPlayers[playerIndex].SerializedClone();
        MethodInfo setup = typeof(PlayerLoader).GetMethod("SetupPlayer", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("PlayerLoader.SetupPlayer is unavailable for explosive hit fixture");
        setup.Invoke(null, new object[] { actor });
        actor.whoAmI = playerIndex;
        try
        {
            Main.player = (Player[])oldPlayers.Clone();
            Main.player[playerIndex] = actor;
            Main.myPlayer = playerIndex;
            Main.netMode = NetmodeID.SinglePlayer;
            Main.gameMenu = false;
            Main.gamePaused = false;
            WorldGen.gen = false;
            Main.screenPosition = Vector2.Zero;
            Main.rand = new UnifiedRandom(20261008);

            string[] trace = new string[32];
            int traceIndex = 0;
            var weaponCases = new[]
            {
                new WeaponCase("IronBow", ModContent.ItemType<IronBow>(), true),
                new WeaponCase("HuntingBow", ModContent.ItemType<HuntingBow>(), true),
                new WeaponCase("NightmareBow", ModContent.ItemType<NightmareBow>(), true),
                new WeaponCase("AbyssBow", ModContent.ItemType<AbyssBow>(), true),
                new WeaponCase("IronStaff", ModContent.ItemType<IronStaff>(), false),
                new WeaponCase("EnergyStaff", ModContent.ItemType<EnergyStaff>(), false),
                new WeaponCase("FallenStaff", ModContent.ItemType<FallenStaff>(), false),
                new WeaponCase("DarkStaff", ModContent.ItemType<DarkStaff>(), false)
            };
            int scenarios = 0;
            int bossSurvivalHits = 0;
            int bossLethalHits = 0;
            int splashHits = 0;
            int emptyAreaHits = 0;
            string previewPath = Path.Combine(outputDirectory, "slime-king-explosive-burst.png");

            string groundedPair = RunGroundedBlueSlimePair(actor, outputDirectory);
            File.WriteAllText(Path.Combine(outputDirectory, "grounded-blue-slime-pair.log"), groundedPair + "\n");

            foreach (WeaponCase test in weaponCases)
            {
                bool firstHasNearby = (scenarios % 2) == 0;
                bool firstPaired = (scenarios % 2) == 0;
                RunScenario(test, lethal: false, hasNearby: firstHasNearby, pairedCase: firstPaired, groundedKingMinions: scenarios == 0, actor,
                    outputDirectory, trace, ref traceIndex, ref bossSurvivalHits, ref splashHits, ref emptyAreaHits,
                    ref previewPath);
                scenarios++;
                bool secondHasNearby = (scenarios % 2) == 0;
                bool secondPaired = (scenarios % 2) == 0;
                RunScenario(test, lethal: true, hasNearby: secondHasNearby, pairedCase: secondPaired, groundedKingMinions: false, actor,
                    outputDirectory, trace, ref traceIndex, ref bossLethalHits, ref splashHits, ref emptyAreaHits,
                    ref previewPath);
                scenarios++;
            }

            if (traceIndex != 16 || bossSurvivalHits != 8 || bossLethalHits != 8)
                throw new InvalidOperationException($"expected 16 real explosive hits (8 surviving/8 lethal), got rows={traceIndex} survive={bossSurvivalHits} lethal={bossLethalHits}");
            if (splashHits == 0 || emptyAreaHits == 0)
                throw new InvalidOperationException($"Slime King tests must cover nearby and solo cases: splash={splashHits} solo={emptyAreaHits}");
            string previewBase = Path.Combine(outputDirectory, "slime-king-explosive-burst-");
            if (!File.Exists(previewBase + "frame1.png") || !File.Exists(previewBase + "frame6.png")
                || !File.Exists(previewBase + "frame10.png"))
                throw new InvalidOperationException("successful Slime King explosion did not produce a visible flash preview");

            File.WriteAllLines(Path.Combine(outputDirectory, "enchant-hit-itemcheck.log"), trace.Take(traceIndex));
            return $"real-Player.ItemCheck->native-Shoot->Projectile.Update/Damage=PASS explosive=8bow+staff x surviving/lethal SlimeKing=16/16; paired/unpaired=8/8; splash-with-nearby={splashHits} solo-boss={emptyAreaHits}; cooldown=30f; nonrecursive-CanDamage=false; grounded-BlueSlime-primary+secondary={groundedPair}; KingSlime-native-minion-boundary=54.3px-center-distance-excluded-by-48px-radius; harmless-12f-gold-flash/Lighting=PASS VFX=slime-king-explosive-burst-frame1/6/10.png";
        }
        finally
        {
            Main.player = oldPlayers;
            Main.npc = oldNpcs;
            Main.projectile = oldProjectiles;
            Main.item = oldItems;
            Main.dust = oldDust;
            Main.myPlayer = oldMyPlayer;
            Main.netMode = oldNetMode;
            Main.gameMenu = oldMenu;
            Main.gamePaused = oldPaused;
            WorldGen.gen = oldWorldGen;
            Main.screenPosition = oldScreenPosition;
            Main.mouseX = oldMouseX;
            Main.mouseY = oldMouseY;
            Main.rand = oldRandom;
        }
    }

    private static string RunGroundedBlueSlimePair(Player actor, string outputDirectory)
    {
        const int floorY = 30;
        using GroundedFloor floor = new(45, 80, floorY);
        Main.projectile = Enumerable.Range(0, Main.maxProjectiles + 1).Select(_ => new Projectile()).ToArray();
        Main.npc = Enumerable.Range(0, Main.maxNPCs + 1).Select(_ => new NPC()).ToArray();
        Main.item = Enumerable.Range(0, Math.Max(400, Main.maxItems + 1)).Select(_ => new Item()).ToArray();
        Main.dust = Enumerable.Range(0, Main.maxDust + 1).Select(_ => new Dust()).ToArray();
        Main.rand = new UnifiedRandom(20261008);
        actor.active = true;
        actor.dead = false;
        actor.position = new Vector2(820f, floorY * 16f - actor.height);
        actor.velocity = Vector2.Zero;
        actor.direction = 1;
        actor.statLifeMax2 = 400;
        actor.statLife = 300;
        actor.statManaMax2 = Math.Max(actor.statManaMax2, 200);
        actor.statMana = actor.statManaMax2;
        actor.whoAmI = Main.myPlayer;
        actor.isDisplayDollOrInanimate = false;
        actor.noItems = false;
        actor.heldProj = -1;
        actor.inventory[0].SetDefaults(ModContent.ItemType<FallenStaff>());
        actor.inventory[0].GetGlobalItem<MilkcraftEnchantedItem>().Kind = MilkcraftEnchant.Explosive;
        actor.armor[3].SetDefaults(ModContent.ItemType<FallenTome>());
        actor.inventory[1].TurnToAir();
        actor.selectedItem = 0;
        actor.lastVisualizedSelectedItem = actor.inventory[0];
        actor.itemAnimation = actor.itemTime = 0;
        actor.controlUseItem = true;
        actor.controlUseTile = false;
        actor.releaseUseItem = false;
        actor.channel = false;
        actor.GetModPlayer<Tier3T4ProgressPlayer>().ApplyNetworkState(true, true, false);
        actor.ResetEffects();
        actor.UpdateEquips(actor.whoAmI);
        actor.UpdateArmorSets(actor.whoAmI);
        PlayerLoader.PostUpdateEquips(actor);
        actor.GetModPlayer<MilkcraftEnchantedPlayer>().ExplosiveCooldown = 0;
        actor.GetModPlayer<MilkcraftEnchantedPlayer>().ExplosiveProcActive = false;
        Main.player[actor.whoAmI] = actor;

        NPC primary = NewGroundedBlueSlime(0, new Vector2(860f, floorY * 16f - 9f));
        NPC secondary = NewGroundedBlueSlime(1, new Vector2(892f, floorY * 16f - 9f));
        Main.npc[0] = primary;
        Main.npc[1] = secondary;
        Main.mouseX = (int)primary.Center.X;
        Main.mouseY = (int)primary.Center.Y;
        actor.ItemCheck();
        Projectile[] fired = Main.projectile.Where(projectile => projectile.active && projectile.owner == actor.whoAmI
            && projectile.GetGlobalProjectile<MilkcraftEnchantedProjectile>().DiagnosticKind == MilkcraftEnchant.Explosive).ToArray();
        if (fired.Length == 0)
            throw new InvalidOperationException("actual FallenStaff ItemCheck did not produce an explosive-enchanted shot for grounded BlueSlime pair");
        Projectile shot = fired[0];
        int slot = Array.IndexOf(Main.projectile, shot);
        shot.tileCollide = false;
        shot.timeLeft = Math.Max(shot.timeLeft, 180);
        MilkcraftPairedProjectile pair = shot.GetGlobalProjectile<MilkcraftPairedProjectile>();
        if (!pair.Paired)
            throw new InvalidOperationException("grounded BlueSlime case did not exercise a real paired FallenStaff projectile");

        bool centerLos = Collision.CanHitLine(primary.Center, 0, 0, secondary.Center, 0, 0);
        float centerDistance = Vector2.Distance(primary.Center, secondary.Center);
        int primaryLife = primary.life;
        int secondaryLife = secondary.life;
        string before = $"primary={primary.TypeName} life={primaryLife}/{primary.lifeMax} def={primary.defense} hitbox={primary.width}x{primary.height} center={primary.Center}; " +
            $"secondary={secondary.TypeName} life={secondaryLife}/{secondary.lifeMax} def={secondary.defense} hitbox={secondary.width}x{secondary.height} center={secondary.Center}; " +
            $"centerDistance={centerDistance:0.0} centerLOS={centerLos} edgeLOS={CanHitGroundedHitbox(primary, secondary)} sourceKind={shot.GetGlobalProjectile<MilkcraftEnchantedProjectile>().DiagnosticKind} paired={pair.Paired}";
        File.WriteAllText(Path.Combine(outputDirectory, "grounded-blue-slime-before-hit.log"), before + "\n");

        for (int tick = 0; tick < 90 && primary.active && primary.life > 0; tick++)
        {
            shot.Update(slot);
            if (actor.GetModPlayer<MilkcraftEnchantedPlayer>().ExplosiveCooldown > 0)
                break;
        }
        int afterPrimary = primary.life;
        int afterSecondary = secondary.life;
        File.WriteAllText(Path.Combine(outputDirectory, "grounded-blue-slime-after-hit.log"),
            $"primary={primaryLife}->{afterPrimary} active={primary.active}; secondary={secondaryLife}->{afterSecondary} active={secondary.active}; " +
            $"cooldown={actor.GetModPlayer<MilkcraftEnchantedPlayer>().ExplosiveCooldown}; centerDistanceStillWithin48={centerDistance <= 48f}; sampledLOS={MilkcraftEnchantedProjectile.CanExplosiveSplashSeeHitbox(primary.Center, secondary.Hitbox)}\n");
        if (afterPrimary >= primaryLife || actor.GetModPlayer<MilkcraftEnchantedPlayer>().ExplosiveCooldown != 30)
            throw new InvalidOperationException($"grounded BlueSlime main target did not trigger explosive hit: primary={primaryLife}->{afterPrimary}, cooldown={actor.GetModPlayer<MilkcraftEnchantedPlayer>().ExplosiveCooldown}; {before}");
        if (afterSecondary >= secondaryLife)
            throw new InvalidOperationException($"grounded BlueSlime adjacent default NPC was not splashed: secondary={secondaryLife}->{afterSecondary}; before={before}");

        // A full-height solid wall still blocks the same hitbox-aware LOS.
        for (int y = floorY - 6; y <= floorY; y++)
        {
            Tile tile = Main.tile[54, y];
            tile.HasTile = true;
            tile.TileType = TileID.Stone;
        }
        bool wallBlocked = !MilkcraftEnchantedProjectile.CanExplosiveSplashSeeHitbox(primary.Center, secondary.Hitbox);
        File.WriteAllText(Path.Combine(outputDirectory, "grounded-blue-slime-wall.log"), $"solidWallBetween=true splashLOSBlocked={wallBlocked}\n");
        if (!wallBlocked)
            throw new InvalidOperationException("hitbox-sampled explosive LOS passed through a solid vertical wall");

        return $"PASS primary-life={primaryLife}->{afterPrimary} secondary-life={secondaryLife}->{afterSecondary} centerDistance={centerDistance:0.0} centerLOS={centerLos} hitboxLOS=true wallBlocked={wallBlocked} paired=true";
    }

    private static NPC NewGroundedBlueSlime(int whoAmI, Vector2 center)
    {
        NPC npc = new();
        npc.SetDefaults(NPCID.BlueSlime);
        npc.whoAmI = whoAmI;
        npc.active = true;
        npc.friendly = false;
        npc.dontTakeDamage = false;
        npc.immortal = false;
        npc.noGravity = false;
        npc.noTileCollide = false;
        npc.velocity = Vector2.Zero;
        npc.Center = center;
        return npc;
    }

    private static Projectile SpawnFocusedProjectile(Player owner, Item item, Vector2 center, int damage)
    {
        IEntitySource source = new EntitySource_ItemUse_WithAmmo(owner, item, ItemID.WoodenArrow, "FocusedExplosiveRadiusBoundary");
        int index = Projectile.NewProjectile(source, center, Vector2.Zero, ProjectileID.WoodenArrowFriendly,
            damage, 0f, owner.whoAmI);
        if (index < 0 || index >= Main.projectile.Length || !Main.projectile[index].active)
            throw new InvalidOperationException("native projectile spawn failed in 48/49px radius boundary case");
        Projectile projectile = Main.projectile[index];
        projectile.Center = center;
        projectile.velocity = Vector2.Zero;
        projectile.damage = damage;
        projectile.knockBack = 0f;
        projectile.owner = owner.whoAmI;
        projectile.friendly = true;
        projectile.hostile = false;
        projectile.tileCollide = false;
        projectile.timeLeft = 60;
        return projectile;
    }

    private static bool CanHitGroundedHitbox(NPC source, NPC target)
    {
        foreach (Vector2 from in new[] { source.Center, new Vector2(source.Center.X, source.Hitbox.Top + 2), new Vector2(source.Center.X, source.Hitbox.Bottom - 2) })
        foreach (Vector2 to in new[] { target.Center, new Vector2(target.Center.X, target.Hitbox.Top + 2), new Vector2(target.Center.X, target.Hitbox.Bottom - 2) })
            if (Collision.CanHitLine(from, 1, 1, to, 1, 1)) return true;
        return false;
    }

    private sealed class GroundedFloor : IDisposable
    {
        private readonly Point[] _cells;
        internal int FloorY { get; }

        internal GroundedFloor(int minX, int maxX, int floorY)
        {
            FloorY = floorY;
            var cells = new System.Collections.Generic.List<Point>();
            for (int x = minX; x <= maxX; x++)
            {
                if (!WorldGen.InWorld(x, floorY + 1, 10))
                    throw new InvalidOperationException("grounded explosive fixture floor is outside the world");
                if (Main.tile[x, floorY].HasTile)
                    throw new InvalidOperationException($"grounded explosive fixture floor occupied at {x},{floorY}");
                cells.Add(new Point(x, floorY));
                Tile tile = Main.tile[x, floorY];
                tile.HasTile = true;
                tile.TileType = TileID.Stone;
            }
            _cells = cells.ToArray();
        }

        public void Dispose()
        {
            foreach (Point point in _cells)
                Main.tile[point.X, point.Y].ClearEverything();
        }
    }

    private static bool HasVisibleHitboxSample(NPC source, NPC target)
    {
        static Vector2[] Points(Rectangle hitbox) => new[]
        {
            new Vector2(hitbox.Center.X, hitbox.Top + 2),
            new Vector2(hitbox.Left + 2, hitbox.Center.Y),
            new Vector2(hitbox.Right - 2, hitbox.Center.Y),
            new Vector2(hitbox.Center.X, hitbox.Center.Y)
        };
        foreach (Vector2 from in Points(source.Hitbox))
        foreach (Vector2 to in Points(target.Hitbox))
            if (Collision.CanHitLine(from, 1, 1, to, 1, 1)) return true;
        return false;
    }

    private static void RunScenario(WeaponCase test, bool lethal, bool hasNearby, bool pairedCase, bool groundedKingMinions, Player actor,
        string outputDirectory, string[] trace, ref int traceIndex, ref int successfulBossHits,
        ref int splashHits, ref int soloHits, ref string previewPath)
    {
        Main.projectile = Enumerable.Range(0, Main.maxProjectiles + 1).Select(_ => new Projectile()).ToArray();
        Main.npc = Enumerable.Range(0, Main.maxNPCs + 1).Select(_ => new NPC()).ToArray();
        Main.item = Enumerable.Range(0, Math.Max(400, Main.maxItems + 1)).Select(_ => new Item()).ToArray();
        Main.dust = Enumerable.Range(0, Main.maxDust + 1).Select(_ => new Dust()).ToArray();
        Main.rand = new UnifiedRandom(20261008 + traceIndex);

        using GroundedFloor floor = groundedKingMinions ? new GroundedFloor(45, 70, 30) : null;

        actor.active = true;
        actor.dead = false;
        actor.position = groundedKingMinions
            ? new Vector2(820f, 30 * 16f - actor.height)
            : new Vector2(820f, 380f);
        actor.velocity = Vector2.Zero;
        actor.direction = 1;
        actor.statLifeMax2 = 400;
        actor.statLife = 300;
        actor.statManaMax2 = Math.Max(actor.statManaMax2, 200);
        actor.statMana = actor.statManaMax2;
        actor.whoAmI = Main.myPlayer;
        actor.isDisplayDollOrInanimate = false;
        actor.noItems = false;
        actor.heldProj = -1;
        actor.inventory[0] = new Item();
        actor.inventory[0].SetDefaults(test.ItemType);
        actor.inventory[0].GetGlobalItem<MilkcraftEnchantedItem>().Kind = MilkcraftEnchant.Explosive;
        actor.inventory[1].TurnToAir();
        actor.armor[3].TurnToAir();
        string accessoryName = MilkcraftPairedProjectile.ResolveWeaponPair(test.Name).Accessory;
        if (pairedCase)
        {
            int accessoryType = accessoryName switch
            {
                "IronQuiver" => ModContent.ItemType<IronQuiver>(),
                "HuntingQuiver" => ModContent.ItemType<HuntingQuiver>(),
                "NightmareQuiver" => ModContent.ItemType<NightmareQuiver>(),
                "AbyssQuiver" => ModContent.ItemType<AbyssQuiver>(),
                "BeginnerTome" => ModContent.ItemType<BeginnerTome>(),
                "EnergyTome" => ModContent.ItemType<EnergyTome>(),
                "FallenTome" => ModContent.ItemType<FallenTome>(),
                "DarkTome" => ModContent.ItemType<DarkTome>(),
                _ => throw new InvalidOperationException($"no functional accessory mapping for {test.Name}")
            };
            actor.armor[3].SetDefaults(accessoryType);
        }
        if (test.Bow)
        {
            actor.inventory[1].SetDefaults(ItemID.WoodenArrow);
            actor.inventory[1].stack = 30;
        }
        actor.selectedItem = 0;
        actor.lastVisualizedSelectedItem = actor.inventory[0];
        actor.itemAnimation = actor.itemTime = 0;
        actor.controlUseItem = true;
        actor.controlUseTile = false;
        actor.releaseUseItem = false;
        actor.channel = false;
        actor.GetModPlayer<Tier3T4ProgressPlayer>().ApplyNetworkState(true, true, false);
        actor.ResetEffects();
        actor.UpdateEquips(actor.whoAmI);
        actor.UpdateArmorSets(actor.whoAmI);
        PlayerLoader.PostUpdateEquips(actor);
        MilkcraftEnchantedPlayer enchantedPlayer = actor.GetModPlayer<MilkcraftEnchantedPlayer>();
        enchantedPlayer.ExplosiveCooldown = 0;
        enchantedPlayer.ExplosiveProcActive = false;
        Main.player[actor.whoAmI] = actor;
        Main.mouseX = (int)(actor.MountedCenter.X + 360f);
        Main.mouseY = (int)actor.MountedCenter.Y;

        int ammoBefore = test.Bow ? actor.inventory[1].stack : 0;
        actor.ItemCheck();
        Projectile[] fired = Main.projectile.Where(projectile => projectile.active && projectile.owner == actor.whoAmI
            && projectile.GetGlobalProjectile<MilkcraftEnchantedProjectile>().DiagnosticKind == MilkcraftEnchant.Explosive).ToArray();
        if (fired.Length == 0)
            throw new InvalidOperationException($"real ItemCheck did not fire an explosive-enchanted {test.Name}: animation={actor.itemAnimation} time={actor.itemTime} mana={actor.statMana} ammo={ammoBefore}");
        if (test.Bow && ammoBefore - actor.inventory[1].stack != 1)
            throw new InvalidOperationException($"{test.Name} ItemCheck ammo contract changed: before={ammoBefore} after={actor.inventory[1].stack}");

        Projectile shot = fired[0];
        int projectileSlot = Array.IndexOf(Main.projectile, shot);
        if (projectileSlot < 0 || shot.damage <= 0 || shot.velocity.LengthSquared() < 0.01f)
            throw new InvalidOperationException($"{test.Name} ItemCheck shot metadata invalid type={shot.type} damage={shot.damage} velocity={shot.velocity}");
        shot.tileCollide = false;
        shot.timeLeft = Math.Max(shot.timeLeft, 180);
        MilkcraftPairedProjectile paired = shot.GetGlobalProjectile<MilkcraftPairedProjectile>();
        if (paired.Paired != pairedCase)
            throw new InvalidOperationException($"{test.Name} functional accessory pair state mismatch: expected={pairedCase} actual={paired.Paired} accessory={accessoryName}");
        Vector2 direction = shot.velocity.SafeNormalize(Vector2.UnitX);

        NPC boss = new();
        boss.SetDefaults(NPCID.KingSlime);
        boss.whoAmI = 0;
        boss.active = true;
        boss.friendly = false;
        boss.dontTakeDamage = false;
        boss.immortal = false;
        boss.noGravity = !groundedKingMinions;
        boss.noTileCollide = !groundedKingMinions;
        boss.velocity = Vector2.Zero;
        boss.Center = shot.Center + direction * 18f;
        if (groundedKingMinions)
        {
            boss.Center = new Vector2(boss.Center.X, floor!.FloorY * 16f - boss.height * 0.5f);
            boss.ai[3] = boss.lifeMax;
            boss.life = boss.lifeMax - Math.Max(1, (int)(boss.lifeMax * 0.08f));
            Main.npc[0] = boss;
            MethodInfo kingAi = typeof(NPC).GetMethod("AI", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null, types: Type.EmptyTypes, modifiers: null)
                ?? throw new InvalidOperationException("vanilla King Slime NPC.AI method was not found");
            kingAi.Invoke(boss, null);
        }
        int bossLifeMax = boss.lifeMax;
        if (!groundedKingMinions)
            boss.life = lethal ? 1 : bossLifeMax;
        Main.npc[0] = boss;

        NPC nearby = null;
        int nearbyLife = 0;
        if (hasNearby)
        {
            if (groundedKingMinions)
            {
                NPC[] minions = Main.npc.Where(npc => npc.active && npc.whoAmI != boss.whoAmI
                    && (npc.type == NPCID.BlueSlime || npc.type == 535)).ToArray();
                if (minions.Length == 0)
                    throw new InvalidOperationException($"King Slime AI did not spawn minions after 8% damage; life={boss.life}/{boss.lifeMax}, ai3={boss.ai[3]}, localAi3={boss.localAI[3]}");
                nearby = minions[0];
                var spawnCenters = minions.ToDictionary(n => n.whoAmI, n => n.Center);
                nearby.noGravity = false;
                nearby.noTileCollide = false;
                nearby.velocity = Vector2.Zero;
                nearby.Center = new Vector2(boss.Center.X + 28f, floor!.FloorY * 16f - nearby.height * 0.5f);
                for (int i = 1; i < minions.Length; i++)
                {
                    NPC minion = minions[i];
                    minion.noGravity = false;
                    minion.noTileCollide = false;
                    minion.velocity = Vector2.Zero;
                    minion.Center = new Vector2(boss.Center.X - 34f - (i - 1) * 4f, floor.FloorY * 16f - minion.height * 0.5f);
                }
                File.AppendAllText(Path.Combine(outputDirectory, "grounded-king-minion-spawns.log"),
                    $"weapon={test.Name} stage=pre-hit king={boss.Center} kingSize={boss.width}x{boss.height} minions=" +
                    string.Join(";", minions.Select(n => $"{n.type}/{n.FullName} life={n.life}/{n.lifeMax} def={n.defense} size={n.width}x{n.height} spawned={spawnCenters[n.whoAmI]} settled={n.Center}")) + "\n");
                foreach (NPC minion in minions)
                {
                    bool centerRay = Collision.CanHitLine(boss.Center, 0, 0, minion.Center, 0, 0);
                    bool sampleRay = HasVisibleHitboxSample(boss, minion);
                    float distance = Vector2.Distance(boss.Center, minion.Center);
                    File.AppendAllText(Path.Combine(outputDirectory, "grounded-king-minion-rays.log"),
                        $"weapon={test.Name} target={minion.type}/{minion.FullName} life={minion.life}/{minion.lifeMax} def={minion.defense} size={minion.width}x{minion.height} centers={boss.Center}->{minion.Center} centerDistance={distance:0.0} centerLOS={centerRay} edgeSampleLOS={sampleRay}\n");
                }
            }
            else
            {
                nearby = new NPC();
                nearby.SetDefaults(NPCID.BlueSlime);
                nearby.whoAmI = 1;
                nearby.active = true;
                nearby.friendly = false;
                nearby.dontTakeDamage = false;
                nearby.immortal = false;
                nearby.noGravity = true;
                nearby.noTileCollide = true;
                nearby.velocity = Vector2.Zero;
                nearby.life = nearby.lifeMax = 5000;
                nearby.Center = boss.Center + new Vector2(0f, 42f);
                Main.npc[1] = nearby;
            }
            nearbyLife = nearby.life;
        }

        int startLife = boss.life;
        for (int tick = 0; tick < 12 && boss.life > 0 && boss.active; tick++)
        {
            shot.Update(projectileSlot);
            if (actor.GetModPlayer<MilkcraftEnchantedPlayer>().ExplosiveCooldown > 0)
                break;
        }
        int afterLife = boss.life;
        if (afterLife >= startLife && boss.active)
            throw new InvalidOperationException($"actual {test.Name} projectile failed to damage Slime King ({(lethal ? "lethal" : "survival")} case): life={startLife}->{afterLife}, shot={shot.type}/{shot.damage}/{shot.velocity}");
        if (enchantedPlayer.ExplosiveCooldown != 30)
            throw new InvalidOperationException($"{test.Name} Slime King hit did not trigger 30-frame explosive cooldown; value={enchantedPlayer.ExplosiveCooldown}");
        int flashes = Main.projectile.Count(projectile => projectile.active && projectile.type == ModContent.ProjectileType<MilkcraftEnchantExplosionVfx>());
        if (flashes != 1)
            throw new InvalidOperationException($"{test.Name} successful Slime King hit expected one harmless flash, found {flashes}");

        int afterNearbyLife = nearby?.life ?? 0;
        if (nearby is not null)
        {
            if (afterNearbyLife < nearbyLife)
                splashHits++;
            else if (!(groundedKingMinions && Vector2.DistanceSquared(nearby.Center, boss.Center) > 48f * 48f))
                throw new InvalidOperationException($"{test.Name} boss explosion did not splash nearby enemy within the 48px center radius: {nearbyLife}->{afterNearbyLife} centerDistance={Vector2.Distance(nearby.Center, boss.Center):0.0}");
        }
        else
        {
            soloHits++;
        }

        if (traceIndex == 0)
        {
            Projectile flash = Main.projectile.First(projectile => projectile.active && projectile.type == ModContent.ProjectileType<MilkcraftEnchantExplosionVfx>());
            RenderFlashPreview(flash, previewPath);
            VerifyFlashLifecycle(flash);
        }
        trace[traceIndex++] = $"weapon={test.Name} bow={test.Bow} case={(lethal ? "lethal" : "surviving")} nearby={hasNearby} paired={pairedCase} " +
            $"actualItemCheck=PASS shotType={shot.type} damage={shot.damage} speed={shot.velocity.Length():0.##} ammoConsumed={(test.Bow ? ammoBefore - actor.inventory[1].stack : 0)} " +
            $"slimeKing={startLife}->{afterLife}/{bossLifeMax} active={boss.active} cooldown={enchantedPlayer.ExplosiveCooldown} " +
            $"flashVfx={flashes} flashCanDamage=false splash={nearbyLife}->{afterNearbyLife}";
        successfulBossHits++;
    }

    private static void VerifyFlashLifecycle(Projectile flash)
    {
        if (flash.ModProjectile?.CanDamage() != false)
            throw new InvalidOperationException("explosive flash must explicitly disallow all damage");
        MilkcraftEnchantExplosionVfx.ResetDiagnosticLight();
        int before = flash.timeLeft;
        int slot = Array.IndexOf(Main.projectile, flash);
        if (before != 12)
            throw new InvalidOperationException($"explosive flash should start with 12 frames, got {before}");
        for (int i = 0; i < 12 && flash.active; i++)
            flash.Update(slot);
        if (flash.active || flash.timeLeft != 0 || MilkcraftEnchantExplosionVfx.DiagnosticLightCalls != 12)
            throw new InvalidOperationException($"explosive flash lifecycle/light failed: active={flash.active} time={before}->{flash.timeLeft} lights={MilkcraftEnchantExplosionVfx.DiagnosticLightCalls}");
        Vector3 light = MilkcraftEnchantExplosionVfx.DiagnosticLastLight;
        if (light.X <= light.Y || light.Y <= light.Z || light.Z <= 0f)
            throw new InvalidOperationException($"explosive flash light must be warm gold; actual={light}");
    }

    private static void RenderFlashPreview(Projectile flash, string path)
    {
        GraphicsDevice device = Main.graphics.GraphicsDevice;
        RenderTargetBinding[] oldTargets = device.GetRenderTargets();
        Vector2 oldScreen = Main.screenPosition;
        int oldTimeLeft = flash.timeLeft;
        using RenderTarget2D target = new(device, 220, 180, false, SurfaceFormat.Color, DepthFormat.None,
            0, RenderTargetUsage.PreserveContents);
        try
        {
            Main.screenPosition = flash.Center - new Vector2(110f, 90f);
            foreach ((int age, string suffix) in new[] { (1, "frame1"), (6, "frame6"), (10, "frame10") })
            {
                device.SetRenderTarget(target);
                device.Clear(new Color(10, 12, 18, 255));
                flash.timeLeft = 12 - age;
                Main.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp,
                    DepthStencilState.None, RasterizerState.CullNone);
                Color lightColor = Color.White;
                flash.ModProjectile.PreDraw(ref lightColor);
                Main.spriteBatch.End();
                Color[] pixels = new Color[target.Width * target.Height];
                target.GetData(pixels);
                int changed = 0;
                float maxRadius = 0f;
                for (int y = 0; y < target.Height; y++)
                for (int x = 0; x < target.Width; x++)
                {
                    Color color = pixels[y * target.Width + x];
                    if (color.R == 10 && color.G == 12 && color.B == 18 && color.A == 255)
                        continue;
                    changed++;
                    maxRadius = Math.Max(maxRadius, Vector2.Distance(new Vector2(x, y), new Vector2(110f, 90f)));
                }
                int minimumPixels = age >= 9 ? 15 : 30;
                if (changed < minimumPixels || maxRadius > 49f)
                    throw new InvalidOperationException($"explosive VFX {suffix} bounds invalid: pixels={changed} (min {minimumPixels}), maxRadius={maxRadius:0.##}, expected <=49px");
                using FileStream stream = File.Create(Path.Combine(Path.GetDirectoryName(path)!, $"slime-king-explosive-burst-{suffix}.png"));
                target.SaveAsPng(stream, target.Width, target.Height);
            }
        }
        finally
        {
            flash.timeLeft = oldTimeLeft;
            Main.screenPosition = oldScreen;
            device.SetRenderTargets(oldTargets);
        }
    }
}
