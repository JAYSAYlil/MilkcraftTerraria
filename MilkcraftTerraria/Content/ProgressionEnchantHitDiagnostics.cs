using System;
using System.Linq;
using System.Reflection;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.Utilities;
using MilkcraftTerraria.Content.Items;

namespace MilkcraftTerraria.Content;

internal static class ProgressionEnchantHitDiagnostics
{
    private readonly record struct WeaponCase(string Name, int ItemType, bool Bow);

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

            // Reproduce the reported geometry with the vanilla King Slime AI
            // and its real minion types, then settle those NPCs on an actual
            // solid floor before testing the current center-ray rule.
            string groundedRepro = RunGroundedKingMinionRepro(actor, outputDirectory);
            File.WriteAllText(Path.Combine(outputDirectory, "grounded-king-minion-repro.log"), groundedRepro + "\n");

            foreach (WeaponCase test in weaponCases)
            {
                bool firstHasNearby = (scenarios % 2) == 0;
                bool firstPaired = (scenarios % 2) == 0;
                RunScenario(test, lethal: false, hasNearby: firstHasNearby, pairedCase: firstPaired, actor,
                    outputDirectory, trace, ref traceIndex, ref bossSurvivalHits, ref splashHits, ref emptyAreaHits,
                    ref previewPath);
                scenarios++;
                bool secondHasNearby = (scenarios % 2) == 0;
                bool secondPaired = (scenarios % 2) == 0;
                RunScenario(test, lethal: true, hasNearby: secondHasNearby, pairedCase: secondPaired, actor,
                    outputDirectory, trace, ref traceIndex, ref bossLethalHits, ref splashHits, ref emptyAreaHits,
                    ref previewPath);
                scenarios++;
            }

            if (traceIndex != 16 || bossSurvivalHits != 8 || bossLethalHits != 8)
                throw new InvalidOperationException($"expected 16 real explosive hits (8 surviving/8 lethal), got rows={traceIndex} survive={bossSurvivalHits} lethal={bossLethalHits}");
            if (splashHits == 0 || emptyAreaHits == 0)
                throw new InvalidOperationException($"Slime King tests must cover nearby and solo cases: splash={splashHits} solo={emptyAreaHits}");
            if (!File.Exists(previewPath))
                throw new InvalidOperationException("successful Slime King explosion did not produce a visible flash preview");

            File.WriteAllLines(Path.Combine(outputDirectory, "enchant-hit-itemcheck.log"), trace.Take(traceIndex));
            return $"real-Player.ItemCheck->native-Shoot->Projectile.Update/Damage=PASS explosive=8bow+staff x surviving/lethal SlimeKing=16/16; boss-survival={bossSurvivalHits}/8 lethal={bossLethalHits}/8; paired/unpaired=8/8; splash-with-nearby={splashHits} solo-boss={emptyAreaHits}; cooldown=30f; nonrecursive-CanDamage=false; grounded-native-KingSlime-minion-repro={groundedRepro}; harmless-12f-gold-flash/Lighting=PASS VFX=slime-king-explosive-burst-frame1/6/10.png";
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

    private static string RunGroundedKingMinionRepro(Player actor, string outputDirectory)
    {
        const int x0 = 80, floorY = 150;
        var touchedTiles = new System.Collections.Generic.List<Point>();
        for (int x = x0 - 10; x <= x0 + 12; x++)
        {
            if (!WorldGen.InWorld(x, floorY + 1, 10)) throw new InvalidOperationException("grounded King fixture outside world");
            for (int y = floorY - 8; y <= floorY; y++)
            {
                if (Main.tile[x, y].HasTile || Main.tile[x, y].WallType != WallID.None)
                    throw new InvalidOperationException($"grounded King fixture arena occupied at {x},{y}");
                touchedTiles.Add(new Point(x, y));
            }
        }
        try
        {
            for (int x = x0 - 10; x <= x0 + 12; x++)
            {
                Tile tile = Main.tile[x, floorY];
                tile.HasTile = true;
                tile.TileType = TileID.Stone;
            }

            Main.npc = Enumerable.Range(0, Main.maxNPCs).Select(_ => new NPC()).ToArray();
            NPC king = new();
            king.SetDefaults(NPCID.KingSlime);
            king.whoAmI = 0;
            king.active = true;
            king.target = actor.whoAmI;
            king.noTileCollide = false;
            king.noGravity = false;
            king.Center = new Vector2((x0 - 3) * 16f, floorY * 16f - king.height * 0.5f);
            king.ai[3] = king.lifeMax;
            king.life = king.lifeMax - Math.Max(1, (int)(king.lifeMax * 0.08f));
            Main.npc[0] = king;

            MethodInfo kingAi = typeof(NPC).GetMethod("AI", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null, types: Type.EmptyTypes, modifiers: null);
            if (kingAi is null)
            {
                string methods = string.Join(";", typeof(NPC).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .Where(method => method.Name.Contains("AI", StringComparison.OrdinalIgnoreCase))
                    .Select(method => method.Name + "(" + string.Join(",", method.GetParameters().Select(p => p.ParameterType.Name)) + ")")
                    .Distinct().Take(24));
                throw new InvalidOperationException("native zero-arg NPC.AI not found; available=" + methods);
            }
            kingAi.Invoke(king, null);

            NPC[] spawned = Main.npc.Where(npc => npc.active && npc.whoAmI != king.whoAmI
                && npc.type is NPCID.BlueSlime or NPCID.Slimer or NPCID.Slimeling).ToArray();
            if (spawned.Length == 0)
                throw new InvalidOperationException($"native KingSlime AI spawned no small slime after 8% life loss; kingLife={king.life}/{king.lifeMax}, ai3={king.ai[3]}, localAi3={king.localAI[3]}, activeNpc={Main.npc.Count(n => n.active)}");

            // Preserve each minion's vanilla defaults/hitbox; place the actual
            // spawned NPC instances at ordinary grounded locations beside the
            // King so the tile LOS path is tested without inventing NPC stats.
            for (int i = 0; i < spawned.Length; i++)
            {
                NPC minion = spawned[i];
                minion.noGravity = false;
                minion.noTileCollide = false;
                minion.velocity = Vector2.Zero;
                minion.Center = new Vector2(king.Center.X + (i % 2 == 0 ? 1 : -1) * (25f + i * 2f),
                    floorY * 16f - minion.height * 0.5f);
            }

            var rows = new System.Collections.Generic.List<string>();
            int centerBlocked = 0, sampledVisible = 0, wallBlocked = 0;
            NPC nearest = spawned.OrderBy(npc => Vector2.DistanceSquared(npc.Center, king.Center)).First();
            foreach (NPC minion in spawned)
            {
                float distance = Vector2.Distance(king.Center, minion.Center);
                bool centerLine = Collision.CanHitLine(king.Center, 0, 0, minion.Center, 0, 0);
                bool edgeLine = HasVisibleHitboxSample(king, minion);
                if (!centerLine) centerBlocked++;
                if (edgeLine) sampledVisible++;

                rows.Add($"minion={minion.type}/{minion.FullName} who={minion.whoAmI} life={minion.life}/{minion.lifeMax} def={minion.defense} size={minion.width}x{minion.height} center={minion.Center} king={king.Center} dist={distance:0.0} centerLOS={centerLine} hitboxSampleLOS={edgeLine}");
            }

            // A real solid wall remains an occluder for every sampled line.
            int wallX = x0 + 3;
            for (int y = floorY - 5; y < floorY; y++)
            {
                Tile tile = Main.tile[wallX, y];
                tile.HasTile = true;
                tile.TileType = TileID.Stone;
            }
            NPC wallTarget = spawned.OrderBy(npc => npc.Center.X).Last();
            bool wallLine = HasVisibleHitboxSample(king, wallTarget);
            if (!wallLine) wallBlocked++;
            File.WriteAllLines(Path.Combine(outputDirectory, "grounded-king-minion-rays.log"), rows);
            return $"nativeAI=NPC.AI KingSlime-spawned={spawned.Length} centerRayBlocked={centerBlocked}/{spawned.Length} hitboxVisible={sampledVisible}/{spawned.Length} solidWallBlocked={wallBlocked}/1; nearest={nearest.type} life={nearest.life}/{nearest.lifeMax} defense={nearest.defense} size={nearest.width}x{nearest.height} centers={king.Center}->{nearest.Center} distance={Vector2.Distance(king.Center, nearest.Center):0.0}";
        }
        finally
        {
            foreach (Point point in touchedTiles) Main.tile[point.X, point.Y].ClearEverything();
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

    private static void RunScenario(WeaponCase test, bool lethal, bool hasNearby, bool pairedCase, Player actor,
        string outputDirectory, string[] trace, ref int traceIndex, ref int successfulBossHits,
        ref int splashHits, ref int soloHits, ref string previewPath)
    {
        Main.projectile = Enumerable.Range(0, Main.maxProjectiles).Select(_ => new Projectile()).ToArray();
        Main.npc = Enumerable.Range(0, Main.maxNPCs).Select(_ => new NPC()).ToArray();
        Main.item = Enumerable.Range(0, Math.Max(400, Main.maxItems + 1)).Select(_ => new Item()).ToArray();
        Main.dust = Enumerable.Range(0, Main.maxDust).Select(_ => new Dust()).ToArray();
        Main.rand = new UnifiedRandom(20261008 + traceIndex);

        actor.active = true;
        actor.dead = false;
        actor.position = new Vector2(820f, 380f);
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
        boss.noGravity = true;
        boss.noTileCollide = true;
        boss.velocity = Vector2.Zero;
        boss.Center = shot.Center + direction * 18f;
        int bossLifeMax = boss.lifeMax;
        boss.life = lethal ? 1 : bossLifeMax;
        Main.npc[0] = boss;

        NPC nearby = null;
        int nearbyLife = 0;
        if (hasNearby)
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
            if (afterNearbyLife >= nearbyLife)
                throw new InvalidOperationException($"{test.Name} boss explosion did not splash nearby enemy: {nearbyLife}->{afterNearbyLife}");
            splashHits++;
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
