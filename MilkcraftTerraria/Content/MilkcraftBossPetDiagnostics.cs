using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Globalization;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using MonoMod.RuntimeDetour;

namespace MilkcraftTerraria.Content;

internal static class MilkcraftBossPetDiagnostics
{
    private delegate void OrigAddLight(int x, int y, float r, float g, float b);
    private delegate void AddLightDetour(OrigAddLight orig, int x, int y, float r, float g, float b);
    private static int _capturedLightCalls;
    private static Vector3 _lastCapturedLight;

    private static void CaptureLighting(OrigAddLight orig, int x, int y, float r, float g, float b)
    {
        _capturedLightCalls++;
        _lastCapturedLight = new Vector3(r, g, b);
        orig(x, y, r, g, b);
    }

    private static readonly MilkcraftBossPetKind[] Kinds =
    {
        MilkcraftBossPetKind.WraithWarrior,
        MilkcraftBossPetKind.JungleLord,
        MilkcraftBossPetKind.Banshee,
        MilkcraftBossPetKind.ObsidianGuardian,
        MilkcraftBossPetKind.Sphinx,
        MilkcraftBossPetKind.FrostWitch,
        MilkcraftBossPetKind.CastleCore
    };

    internal static string Run()
    {
        Player[] oldPlayers = Main.player;
        Projectile[] oldProjectiles = Main.projectile;
        NPC[] oldNpcs = Main.npc;
        Item[] oldItems = Main.item;
        Dust[] oldDust = Main.dust;
        int oldMyPlayer = Main.myPlayer;
        int oldNetMode = Main.netMode;
        bool oldMenu = Main.gameMenu;
        bool oldPaused = Main.gamePaused;
        bool oldWorldGen = WorldGen.gen;
        int playerIndex = oldMyPlayer >= 0 && oldMyPlayer < Main.maxPlayers ? oldMyPlayer : 0;
        int tileX = Math.Clamp(Main.maxTilesX / 2, 30, Main.maxTilesX - 30);
        int tileY = Math.Clamp(Main.maxTilesY / 2, 60, Main.maxTilesY - 20);
        Tile[,] savedTerrain = new Tile[49, 12];
        for (int x = 0; x < 49; x++)
        for (int y = 0; y < 12; y++)
        {
            savedTerrain[x, y] = new Tile();
            savedTerrain[x, y].CopyFrom(Main.tile[tileX + x - 24, tileY + y - 11]);
        }

        var report = new StringBuilder("native boss pets: actual ItemCheck -> buff refresh -> owned projectile -> Projectile.Update\n");
        try
        {
            Main.gameMenu = false;
            Main.gamePaused = false;
            WorldGen.gen = false;
            Main.myPlayer = playerIndex;
            Main.netMode = NetmodeID.SinglePlayer;
            Main.player = (Player[])oldPlayers.Clone();
            Main.projectile = Enumerable.Range(0, Math.Max(oldProjectiles.Length, Main.maxProjectiles + 1)).Select(_ => new Projectile()).ToArray();
            Main.npc = Enumerable.Range(0, Math.Max(oldNpcs.Length, Main.maxNPCs + 1)).Select(_ => new NPC()).ToArray();
            Main.item = Enumerable.Range(0, Math.Max(oldItems.Length, Main.maxItems + 1)).Select(_ => new Item()).ToArray();
            Main.dust = Enumerable.Range(0, Math.Max(oldDust.Length, Main.maxDust + 1)).Select(_ => new Dust()).ToArray();

            // Clear a real 49x11-tile air space above the test floor. The isolated
            // world may already contain underground blocks before this fixture.
            for (int dx = -24; dx <= 24; dx++)
            for (int y = tileY - 11; y < tileY; y++)
                Main.tile[tileX + dx, y].ClearEverything();
            for (int dx = -24; dx <= 24; dx++)
            {
                Tile floor = Main.tile[tileX + dx, tileY];
                floor.HasTile = true;
                floor.TileType = TileID.Stone;
                floor.IsActuated = false;
                floor.IsHalfBlock = false;
                floor.Slope = SlopeType.Solid;
            }

            MethodInfo addLight = typeof(Lighting).GetMethod(nameof(Lighting.AddLight),
                new[] { typeof(int), typeof(int), typeof(float), typeof(float), typeof(float) })
                ?? throw new InvalidOperationException("Lighting.AddLight(int, int, float, float, float) unavailable for native boss pet light capture.");
            using Hook lightingHook = new(addLight, (AddLightDetour)CaptureLighting);
            _capturedLightCalls = 0;
            Lighting.AddLight(Vector2.Zero, Vector3.One);
            if (_capturedLightCalls != 1)
                throw new InvalidOperationException($"Native light observer failed its direct-call probe: calls={_capturedLightCalls}; hooked={addLight.DeclaringType?.FullName}.{addLight.Name}.");
            _capturedLightCalls = 0;

            MethodInfo setup = typeof(PlayerLoader).GetMethod("SetupPlayer", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("PlayerLoader.SetupPlayer was unavailable for boss-pet ItemCheck test.");
            MethodInfo[] buffUpdates = typeof(Player).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(m => m.Name == "UpdateBuffs").ToArray();
            MethodInfo updateProjectileCaches = typeof(Player).GetMethod("UpdateProjectileCaches",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(int) }, null)
                ?? throw new InvalidOperationException("Native Player.UpdateProjectileCaches(int) entry point was not found.");
            MethodInfo updatePet = typeof(Player).GetMethod("UpdatePet", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null, new[] { typeof(int) }, null) ?? throw new InvalidOperationException("Native Player.UpdatePet(int) entry point was not found.");
            MethodInfo updatePetLight = typeof(Player).GetMethod("UpdatePetLight", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null, new[] { typeof(int) }, null) ?? throw new InvalidOperationException("Native Player.UpdatePetLight(int) entry point was not found.");
            MethodInfo updateAllBuffs = buffUpdates.FirstOrDefault(m => m.GetParameters().Length == 0);
            MethodInfo updateOneBuff = buffUpdates.FirstOrDefault(m => m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(int));
            if (updateAllBuffs is null && updateOneBuff is null)
                throw new InvalidOperationException("Native Player.UpdateBuffs entry point was not found.");

            Player NewPlayer()
            {
                Player actor = oldPlayers[playerIndex].SerializedClone();
                setup.Invoke(null, new object[] { actor });
                if (actor.ownedProjectileCounts.Length < Terraria.ModLoader.ProjectileLoader.ProjectileCount)
                    actor.ownedProjectileCounts = new int[Terraria.ModLoader.ProjectileLoader.ProjectileCount];
                actor.active = true;
                actor.dead = false;
                actor.whoAmI = playerIndex;
                actor.noItems = false;
                actor.heldProj = -1;
                actor.position = new Vector2(tileX * 16f, tileY * 16f - actor.height - 28f);
                actor.velocity = Vector2.Zero;
                return actor;
            }

            void TickBuffs(Player actor)
            {
                // Player.Update refreshes ownedProjectileCounts immediately before UpdateBuffs.
                updateProjectileCaches.Invoke(actor, new object[] { actor.whoAmI });
                // UpdateBuffs(int) takes the player index and internally loops
                // every buff slot; invoking it once mirrors Player.Update.
                updateOneBuff!.Invoke(actor, new object[] { actor.whoAmI });
            }

            string CheckMiscSlot(MilkcraftBossPetKind kind, int slotIndex)
            {
                Main.projectile = Enumerable.Range(0, Math.Max(oldProjectiles.Length, Main.maxProjectiles + 1)).Select(_ => new Projectile()).ToArray();
                Player actor = NewPlayer();
                Main.player[playerIndex] = actor;
                Item slotItem = new();
                slotItem.SetDefaults(MilkcraftBossPetAssets.Item(kind));
                if (slotItem.buffTime != 0)
                    throw new InvalidOperationException($"{kind}: summon item exposes a finite buff time ({slotItem.buffTime}); expected permanent slot pet with no duration line.");
                actor.miscEquips[slotIndex] = slotItem;
                actor.hideMisc[slotIndex] = false;

                void UpdateNativeEquipmentAndPet()
                {
                    actor.GetModPlayer<MilkcraftBossPetPlayerState>().PreUpdate();
                    actor.ResetEffects();
                    Array.Clear(actor.ownedProjectileCounts);
                    if (slotIndex == 1) updatePetLight.Invoke(actor, new object[] { actor.whoAmI });
                    else updatePet.Invoke(actor, new object[] { actor.whoAmI });
                    TickBuffs(actor);
                    for (int p = 0; p < Main.projectile.Length; p++)
                        if (Main.projectile[p].active && Main.projectile[p].owner == playerIndex) Main.projectile[p].Update(p);
                }

                UpdateNativeEquipmentAndPet();
                int buff = MilkcraftBossPetAssets.Buff(kind);
                int pet = MilkcraftBossPetAssets.Projectile(kind);
                if (!actor.HasBuff(buff) || !Main.projectile.Any(p => p.active && p.owner == playerIndex && p.type == pet))
                    throw new InvalidOperationException($"{kind}: native miscEquips[{slotIndex}] did not create its permanent slot pet.");
                // The native update hook skips an invisible slot; buff update
                // then removes that slot-owned effect and its projectile.
                actor.hideMisc[slotIndex] = true;
                UpdateNativeEquipmentAndPet();
                if (actor.HasBuff(buff) || Main.projectile.Any(p => p.active && p.owner == playerIndex && p.type == pet))
                    throw new InvalidOperationException($"{kind}: native hide toggle failed to remove the slot-owned pet.");
                actor.hideMisc[slotIndex] = false;
                UpdateNativeEquipmentAndPet();
                if (!actor.HasBuff(buff) || !Main.projectile.Any(p => p.active && p.owner == playerIndex && p.type == pet))
                    throw new InvalidOperationException($"{kind}: native pet slot did not restore after unhiding; hasBuff={actor.HasBuff(buff)}, buffSlot={Array.IndexOf(actor.buffType, buff)}, petCount={Main.projectile.Count(p => p.active && p.owner == playerIndex && p.type == pet)}, ownedCount={(pet < actor.ownedProjectileCounts.Length ? actor.ownedProjectileCounts[pet] : -1)}/{actor.ownedProjectileCounts.Length}, slotBuff={actor.miscEquips[slotIndex].buffType}, expectedBuff={buff}, hide={actor.hideMisc[slotIndex]}.");
                actor.miscEquips[slotIndex].TurnToAir();
                UpdateNativeEquipmentAndPet();
                if (actor.HasBuff(buff) || Main.projectile.Any(p => p.active && p.owner == playerIndex && p.type == pet))
                    throw new InvalidOperationException($"{kind}: removing miscEquips[{slotIndex}] did not clear the permanent pet.");
                return $"{kind}: native-miscEquips[{slotIndex}]=PASS itemBuffTime=0; native slot add/hidden remove/unhide restore/unequip remove";
            }

            foreach (MilkcraftBossPetKind kind in Kinds)
            {
                _capturedLightCalls = 0;
                _lastCapturedLight = Vector3.Zero;
                Player actor = NewPlayer();
                Main.player[playerIndex] = actor;
                int summonType = MilkcraftBossPetAssets.Item(kind);
                int buffType = MilkcraftBossPetAssets.Buff(kind);
                int projectileType = MilkcraftBossPetAssets.Projectile(kind);
                bool light = MilkcraftBossPetAssets.IsLight(kind);
                int slotIndex = light ? 1 : 0;
                actor.miscEquips[slotIndex].SetDefaults(summonType);
                if (actor.miscEquips[slotIndex].buffTime != 0)
                    throw new InvalidOperationException($"{kind}: item tooltip metadata declares finite pet duration {actor.miscEquips[slotIndex].buffTime}.");

                for (int tick = 0; tick < 4; tick++)
                {
                    actor.ResetEffects();
                    Array.Clear(actor.ownedProjectileCounts);
                    if (light) updatePetLight.Invoke(actor, new object[] { playerIndex });
                    else updatePet.Invoke(actor, new object[] { playerIndex });
                    TickBuffs(actor);
                    for (int p = 0; p < Main.projectile.Length; p++)
                    {
                        Projectile projectile = Main.projectile[p];
                        if (projectile.active && projectile.owner == playerIndex)
                            projectile.Update(p);
                    }
                }

                int buffSlot = Array.IndexOf(actor.buffType, buffType);
                int matchingProjectiles = Main.projectile.Count(p => p.active && p.type == projectileType && p.owner == playerIndex);
                if (buffSlot < 0 || actor.buffTime[buffSlot] < 17995)
                    throw new InvalidOperationException($"{kind}: native ItemCheck/buff refresh failed, buffSlot={buffSlot}, buffTime={(buffSlot >= 0 ? actor.buffTime[buffSlot] : -1)}.");
                if (matchingProjectiles != 1)
                    throw new InvalidOperationException($"{kind}: expected one owned pet after buff update, got {matchingProjectiles}.");
                if (Main.vanityPet[buffType] == light || Main.lightPet[buffType] != light)
                    throw new InvalidOperationException($"{kind}: vanity/light pet registration mismatch; light={light}.");
                if (light && (_capturedLightCalls == 0 || _lastCapturedLight.LengthSquared() <= 0f))
                    throw new InvalidOperationException($"{kind}: real Projectile.Update produced no nonzero Lighting.AddLight call.");
                report.AppendLine($"{kind}: nativePetSlot={slotIndex} itemBuffTime=0 refreshedBuffTime={actor.buffTime[buffSlot]} ownedPet={matchingProjectiles} lightPet={Main.lightPet[buffType]} vanityPet={Main.vanityPet[buffType]}" +
                    (light ? $" nativeLightingCalls={_capturedLightCalls} rgb={_lastCapturedLight}" : ""));
            }

            report.AppendLine(CheckMiscSlot(MilkcraftBossPetKind.WraithWarrior, 0));
            report.AppendLine(CheckMiscSlot(MilkcraftBossPetKind.Banshee, 1));
            report.AppendLine(TestHeldSummon(MilkcraftBossPetKind.WraithWarrior, playerIndex));

            string castleLight = VerifyCastlePetFaceLighting(NewPlayer(), playerIndex, updateProjectileCaches, updateAllBuffs, updateOneBuff);
            report.AppendLine(castleLight);
            report.AppendLine(VerifyLightPropagation(playerIndex, tileX, tileY, setup, updatePetLight, updateProjectileCaches, updateOneBuff));

            // Each ground pet must settle on a real tile floor and remain blocked by a
            // five-tile wall; this exercises Projectile.Update/TileCollision, not just AI.
            var groundRows = new System.Collections.Generic.List<string>();
            foreach (MilkcraftBossPetKind kind in Kinds.Where(k => !MilkcraftBossPetAssets.IsLight(k)))
            {
                Main.projectile = Enumerable.Range(0, Math.Max(oldProjectiles.Length, Main.maxProjectiles + 1)).Select(_ => new Projectile()).ToArray();
                Player groundOwner = NewPlayer();
                Main.player[playerIndex] = groundOwner;
                groundOwner.Center = new Vector2(tileX * 16f + 16f * 18f, tileY * 16f - groundOwner.height * 0.5f - 30f);
                groundOwner.AddBuff(MilkcraftBossPetAssets.Buff(kind), 18000);
                int groundSlot = Projectile.NewProjectile(groundOwner.GetSource_Misc("BossPetTerrainDiagnostic"),
                    groundOwner.Center + new Vector2(-250f, -70f), new Vector2(0f, 4f),
                    MilkcraftBossPetAssets.Projectile(kind), 0, 0f, playerIndex);
                if (groundSlot < 0 || groundSlot >= Main.projectile.Length) throw new InvalidOperationException($"{kind}: ground pet projectile failed to spawn.");
                Projectile groundPet = Main.projectile[groundSlot];
                int landingTick = -1;
                for (int tick = 0; tick < 180; tick++)
                {
                    groundPet.Update(groundSlot);
                    if (groundPet.velocity.Y == 0f && Math.Abs(groundPet.Bottom.Y - tileY * 16f) <= 1f)
                    { landingTick = tick + 1; break; }
                }
                if (!groundPet.active || landingTick < 0)
                    throw new InvalidOperationException($"{kind}: failed native floor landing within 180 Projectile.Update ticks: active={groundPet.active}, vy={groundPet.velocity.Y:0.00}, bottom={groundPet.Bottom.Y:0.0}, floor={tileY * 16f}.");

                int wallX = tileX + 6;
                for (int y = tileY - 5; y < tileY; y++)
                {
                    Tile wall = Main.tile[wallX, y];
                    wall.HasTile = true; wall.TileType = TileID.Stone; wall.IsActuated = false;
                    wall.IsHalfBlock = false; wall.Slope = SlopeType.Solid;
                }
                Main.projectile = Enumerable.Range(0, Math.Max(oldProjectiles.Length, Main.maxProjectiles + 1)).Select(_ => new Projectile()).ToArray();
                int wallSlot = Projectile.NewProjectile(groundOwner.GetSource_Misc("BossPetWallDiagnostic"),
                    new Vector2((wallX - 3) * 16f, tileY * 16f - 44f), Vector2.Zero,
                    MilkcraftBossPetAssets.Projectile(kind), 0, 0f, playerIndex);
                if (wallSlot < 0 || wallSlot >= Main.projectile.Length) throw new InvalidOperationException($"{kind}: wall pet projectile failed to spawn.");
                Projectile wallPet = Main.projectile[wallSlot];
                float wallStartX = wallPet.Center.X;
                for (int tick = 0; tick < 40; tick++) wallPet.Update(wallSlot);
                float wallEdge = wallX * 16f;
                if (!wallPet.active || wallPet.Center.X >= wallEdge - wallPet.width * 0.5f + 1f || wallPet.Center.X <= wallStartX)
                    throw new InvalidOperationException($"{kind}: native wall collision failed: active={wallPet.active}, startX={wallStartX:0.0}, endX={wallPet.Center.X:0.0}, wallLeft={wallEdge:0.0}, tileCollide={wallPet.tileCollide}.");
                groundRows.Add($"{kind}: landing=PASS nativeUpdates={landingTick} bottom={groundPet.Bottom.Y:0.0}/{tileY * 16f:0.0}; solid-wall=PASS 40updates x={wallStartX:0.0}->{wallPet.Center.X:0.0}");
            }
            report.AppendLine("four-ground-pet-native-terrain=PASS\n" + string.Join("\n", groundRows));

            foreach (MilkcraftBossPetKind kind in Kinds)
            {
                Main.projectile = Enumerable.Range(0, Math.Max(oldProjectiles.Length, Main.maxProjectiles + 1))
                    .Select(_ => new Projectile()).ToArray();
                Player owner = NewPlayer();
                Main.player[playerIndex] = owner;
                owner.AddBuff(MilkcraftBossPetAssets.Buff(kind), 18000);
                owner.ownedProjectileCounts = new int[Terraria.ModLoader.ProjectileLoader.ProjectileCount];
                Vector2 start = owner.Center + new Vector2(-620f, -210f);
                int slot = Projectile.NewProjectile(owner.GetSource_Misc("BossPetCatchupDiagnostic"), start,
                    Vector2.Zero, MilkcraftBossPetAssets.Projectile(kind), 0, 0f, playerIndex);
                if (slot < 0 || slot >= Main.projectile.Length) throw new InvalidOperationException($"{kind}: native catch-up projectile did not spawn");
                Projectile follower = Main.projectile[slot];
                float initialDistance = Vector2.Distance(follower.Center, owner.Center);
                int followTicks = 0;
                for (; followTicks < 180 && Vector2.Distance(follower.Center, owner.Center) > 150f; followTicks++)
                    follower.Update(slot);
                // Give grounded pets the first in-range update that restores native tile collision.
                if (followTicks < 180 && !MilkcraftBossPetAssets.IsLight(kind))
                { followTicks++; follower.Update(slot); }
                float finalDistance = Vector2.Distance(follower.Center, owner.Center);
                if (!follower.active || finalDistance >= initialDistance || finalDistance <= 0f)
                    throw new InvalidOperationException($"{kind}: pet failed native catch-up; active={follower.active} initial={initialDistance:0.0} final={finalDistance:0.0}");
                bool expectedTileCollision = !MilkcraftBossPetAssets.IsLight(kind);
                if (follower.tileCollide != expectedTileCollision)
                    throw new InvalidOperationException($"{kind}: catch-up mode did not end at its native collision setting; tileCollide={follower.tileCollide} expected={expectedTileCollision} distance={finalDistance:0.0}");
                owner.dead = true;
                follower.Update(slot);
                if (follower.active) throw new InvalidOperationException($"{kind}: pet remained after owner death");

                owner.dead = false;
                owner.AddBuff(MilkcraftBossPetAssets.Buff(kind), 18000);
                Main.projectile = Enumerable.Range(0, Math.Max(oldProjectiles.Length, Main.maxProjectiles + 1))
                    .Select(_ => new Projectile()).ToArray();
                owner.Center = new Vector2(tileX * 16f, tileY * 16f - owner.height * 0.5f - 26f);
                int teleportSlot = Projectile.NewProjectile(owner.GetSource_Misc("BossPetTeleportDiagnostic"),
                    owner.Center + new Vector2(-1320f, -160f), Vector2.Zero,
                    MilkcraftBossPetAssets.Projectile(kind), 0, 0f, playerIndex);
                if (teleportSlot < 0 || teleportSlot >= Main.projectile.Length) throw new InvalidOperationException($"{kind}: long-distance pet spawn failed");
                Projectile teleporter = Main.projectile[teleportSlot];
                teleporter.Update(teleportSlot);
                float teleportedDistance = Vector2.Distance(teleporter.Center, owner.Center);
                if (!teleporter.active || teleportedDistance > 120f)
                    throw new InvalidOperationException($"{kind}: >1200px owner catch-up did not teleport safely: active={teleporter.active} remaining={teleportedDistance:0.0}");
                owner.buffTime[Array.IndexOf(owner.buffType, MilkcraftBossPetAssets.Buff(kind))] = 0;
                teleporter.Update(teleportSlot);
                if (teleporter.active) throw new InvalidOperationException($"{kind}: pet remained after its buff was removed");
                report.AppendLine($"{kind}: catchup=PASS updates={followTicks} distance={initialDistance:0.0}->{finalDistance:0.0}px collision-restored={follower.tileCollide}; teleport=PASS 1320px->{teleportedDistance:0.0}px; death+buff-removal clear=PASS");
            }

            Main.projectile = Enumerable.Range(0, Math.Max(oldProjectiles.Length, Main.maxProjectiles + 1))
                .Select(_ => new Projectile()).ToArray();
            Player pairedOwner = NewPlayer();
            Main.player[playerIndex] = pairedOwner;
            int vanityBuff = MilkcraftBossPetAssets.Buff(MilkcraftBossPetKind.WraithWarrior);
            int lightBuff = MilkcraftBossPetAssets.Buff(MilkcraftBossPetKind.Banshee);
            pairedOwner.miscEquips[0].SetDefaults(MilkcraftBossPetAssets.Item(MilkcraftBossPetKind.WraithWarrior));
            pairedOwner.miscEquips[1].SetDefaults(MilkcraftBossPetAssets.Item(MilkcraftBossPetKind.Banshee));
            pairedOwner.GetModPlayer<MilkcraftBossPetPlayerState>().PreUpdate();
            pairedOwner.ResetEffects();
            Array.Clear(pairedOwner.ownedProjectileCounts);
            updatePet.Invoke(pairedOwner, new object[] { playerIndex });
            updatePetLight.Invoke(pairedOwner, new object[] { playerIndex });
            TickBuffs(pairedOwner);
            for (int p = 0; p < Main.projectile.Length; p++)
                if (Main.projectile[p].active && Main.projectile[p].owner == playerIndex) Main.projectile[p].Update(p);
            int vanityCount = Main.projectile.Count(p => p.active && p.type == MilkcraftBossPetAssets.Projectile(MilkcraftBossPetKind.WraithWarrior) && p.owner == playerIndex);
            int lightCount = Main.projectile.Count(p => p.active && p.type == MilkcraftBossPetAssets.Projectile(MilkcraftBossPetKind.Banshee) && p.owner == playerIndex);
            if (vanityCount != 1 || lightCount != 1)
                throw new InvalidOperationException($"vanity/light pet coexistence failed: vanity={vanityCount} light={lightCount}");
            pairedOwner.miscEquips[0].TurnToAir();
            pairedOwner.GetModPlayer<MilkcraftBossPetPlayerState>().PreUpdate();
            pairedOwner.ResetEffects();
            Array.Clear(pairedOwner.ownedProjectileCounts);
            updatePet.Invoke(pairedOwner, new object[] { playerIndex });
            updatePetLight.Invoke(pairedOwner, new object[] { playerIndex });
            TickBuffs(pairedOwner);
            for (int p = 0; p < Main.projectile.Length; p++)
                if (Main.projectile[p].active && Main.projectile[p].owner == playerIndex) Main.projectile[p].Update(p);
            vanityCount = Main.projectile.Count(p => p.active && p.type == MilkcraftBossPetAssets.Projectile(MilkcraftBossPetKind.WraithWarrior) && p.owner == playerIndex);
            lightCount = Main.projectile.Count(p => p.active && p.type == MilkcraftBossPetAssets.Projectile(MilkcraftBossPetKind.Banshee) && p.owner == playerIndex);
            if (vanityCount != 0 || lightCount != 1)
                throw new InvalidOperationException($"vanity pet slot removal did not preserve only light pet: vanity={vanityCount} light={lightCount}; buff={pairedOwner.HasBuff(vanityBuff)} buffSlot={Array.IndexOf(pairedOwner.buffType, vanityBuff)} miscBuff={pairedOwner.miscEquips[0].buffType} hide={pairedOwner.hideMisc[0]} lightBuff={pairedOwner.HasBuff(lightBuff)}");
            report.AppendLine("pet-coexistence=PASS native misc slots 0+1 coexist; removing vanity slot cleared only its pet");

            report.AppendLine(TestPlayKitMigration(playerIndex));
            return report.ToString();
        }
        finally
        {
            for (int x = 0; x < 49; x++)
            for (int y = 0; y < 12; y++)
                Main.tile[tileX + x - 24, tileY + y - 11].CopyFrom(savedTerrain[x, y]);
            Main.player = oldPlayers;
            Main.projectile = oldProjectiles;
            Main.npc = oldNpcs;
            Main.item = oldItems;
            Main.dust = oldDust;
            Main.myPlayer = oldMyPlayer;
            Main.netMode = oldNetMode;
            Main.gameMenu = oldMenu;
            Main.gamePaused = oldPaused;
            WorldGen.gen = oldWorldGen;
        }
    }

    private static string TestHeldSummon(MilkcraftBossPetKind kind, int playerIndex)
    {
        Main.projectile = Enumerable.Range(0, Math.Max(Main.projectile.Length, Main.maxProjectiles + 1)).Select(_ => new Projectile()).ToArray();
        MethodInfo createActionPlayer = typeof(SmokeSystem).GetMethod("CreateActionPlayer", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Existing native ItemCheck actor factory is unavailable.");
        Player actor = (Player)createActionPlayer.Invoke(null, new object[]
        {
            ItemID.IronHelmet, ItemID.IronChainmail, ItemID.IronGreaves, MilkcraftBossPetAssets.Item(kind), 1, 0f, true, 0
        })!;
        actor.active = true;
        actor.dead = false;
        actor.whoAmI = playerIndex;
        actor.releaseUseItem = true;
        actor.position = new Vector2(900f, 300f);
        actor.velocity = Vector2.Zero;
        Main.player[playerIndex] = actor;
        Item summon = actor.inventory[0];
        summon.SetDefaults(MilkcraftBossPetAssets.Item(kind));
        actor.selectedItem = 0;
        actor.controlUseItem = true;
        // ItemCheck's release gate skips CanUseItem/UseItem until this is true;
        // mirrors the first real press after the button was released.
        actor.releaseUseItem = true;
        actor.itemTime = actor.itemAnimation = 0;
        actor.noItems = false;
        actor.Male = true;
        actor.isDisplayDollOrInanimate = false;
        actor.lastVisualizedSelectedItem = summon;
        bool oldMouseLeft = Main.mouseLeft, oldMouseRight = Main.mouseRight;
        int oldMouseX = Main.mouseX, oldMouseY = Main.mouseY;
        Main.mouseLeft = true;
        Main.mouseRight = false;
        Main.mouseX = (int)(actor.MountedCenter.X + 200f);
        Main.mouseY = (int)actor.MountedCenter.Y;
        actor.ItemCheck();
        Main.mouseLeft = oldMouseLeft;
        Main.mouseRight = oldMouseRight;
        Main.mouseX = oldMouseX;
        Main.mouseY = oldMouseY;
        int buffType = MilkcraftBossPetAssets.Buff(kind);
        if (!actor.HasBuff(buffType))
            throw new InvalidOperationException($"{kind}: native ItemCheck held summon failed; useStyle={summon.useStyle} useTime={summon.useTime}/{summon.useAnimation} selected={actor.selectedItem} control={actor.controlUseItem} release={actor.releaseUseItem} noItems={actor.noItems} dead={actor.dead} itemAnimation={actor.itemAnimation} itemTime={actor.itemTime} who={actor.whoAmI} mainMy={Main.myPlayer} gamePaused={Main.gamePaused}.");
        Main.player[playerIndex] = actor;
        actor.ResetEffects();
        Array.Clear(actor.ownedProjectileCounts);
        if (MilkcraftBossPetAssets.IsLight(kind))
        {
            typeof(Player).GetMethod("UpdatePetLight", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null, new[] { typeof(int) }, null)!.Invoke(actor, new object[] { playerIndex });
        }
        else
        {
            typeof(Player).GetMethod("UpdatePet", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null, new[] { typeof(int) }, null)!.Invoke(actor, new object[] { playerIndex });
        }
        MethodInfo updateBuffs = typeof(Player).GetMethod("UpdateBuffs", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null, new[] { typeof(int) }, null)
            ?? throw new InvalidOperationException("Player.UpdateBuffs(int playerIndex) unavailable for hand-pet test.");
        MethodInfo updateProjectileCaches = typeof(Player).GetMethod("UpdateProjectileCaches", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null, new[] { typeof(int) }, null)
            ?? throw new InvalidOperationException("Player.UpdateProjectileCaches(int) unavailable for hand-pet test.");
        updateProjectileCaches.Invoke(actor, new object[] { playerIndex });
        updateBuffs.Invoke(actor, new object[] { playerIndex });
        for (int p = 0; p < Main.projectile.Length; p++)
            if (Main.projectile[p].active && Main.projectile[p].owner == playerIndex) Main.projectile[p].Update(p);
        int pets = Main.projectile.Count(p => p.active && p.owner == playerIndex && p.type == MilkcraftBossPetAssets.Projectile(kind));
        if (pets != 1)
            throw new InvalidOperationException($"{kind}: hand-summoned buff failed to create one pet; count={pets}.");

        // A manually summoned pet must hand off cleanly when its item is put in
        // the native slot: slot ownership replaces the manual source, then hide
        // and unequip clear only the slot-owned buff.
        int miscSlot = MilkcraftBossPetAssets.IsLight(kind) ? 1 : 0;
        actor.miscEquips[miscSlot].SetDefaults(MilkcraftBossPetAssets.Item(kind));
        MilkcraftBossPetPlayerState petState = actor.GetModPlayer<MilkcraftBossPetPlayerState>();
        petState.PreUpdate();
        MethodInfo updateNativeSlot = typeof(Player).GetMethod(MilkcraftBossPetAssets.IsLight(kind) ? "UpdatePetLight" : "UpdatePet",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(int) }, null)
            ?? throw new InvalidOperationException($"Native pet slot updater unavailable for {kind}.");
        updateNativeSlot.Invoke(actor, new object[] { playerIndex });
        updateProjectileCaches.Invoke(actor, new object[] { playerIndex });
        updateBuffs.Invoke(actor, new object[] { playerIndex });
        if (!actor.HasBuff(buffType))
            throw new InvalidOperationException($"{kind}: native slot failed to take over the manual pet buff.");
        actor.hideMisc[miscSlot] = true;
        petState.PreUpdate();
        if (actor.HasBuff(buffType))
            throw new InvalidOperationException($"{kind}: hiding the equipped slot left its pet buff active.");
        actor.hideMisc[miscSlot] = false;
        petState.PreUpdate();
        updateNativeSlot.Invoke(actor, new object[] { playerIndex });
        updateProjectileCaches.Invoke(actor, new object[] { playerIndex });
        updateBuffs.Invoke(actor, new object[] { playerIndex });
        actor.miscEquips[miscSlot].TurnToAir();
        petState.PreUpdate();
        if (actor.HasBuff(buffType))
            throw new InvalidOperationException($"{kind}: removing the equipped slot left its pet buff active.");
        foreach (Projectile projectile in Main.projectile)
            if (projectile.active && projectile.owner == playerIndex && projectile.type == MilkcraftBossPetAssets.Projectile(kind)) projectile.Kill();
        return $"{kind}: native-held ItemCheck=PASS nonconsumable; misc-slot handoff/hide/unhide/unequip=PASS; ownedPet={pets}";
    }

    private static string VerifyCastlePetFaceLighting(Player actor, int owner, MethodInfo updateProjectileCaches,
        MethodInfo updateAllBuffs, MethodInfo updateOneBuff)
    {
        ulong oldCount = Main.GameUpdateCount;
        try
        {
            Main.player[owner] = actor;
            actor.active = true; actor.dead = false; actor.whoAmI = owner;
            actor.miscEquips[1].SetDefaults(MilkcraftBossPetAssets.Item(MilkcraftBossPetKind.CastleCore));
            MethodInfo updatePetLight = typeof(Player).GetMethod("UpdatePetLight", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null, new[] { typeof(int) }, null) ?? throw new InvalidOperationException("Native Player.UpdatePetLight(int) unavailable for Castle pet light test.");
            updatePetLight.Invoke(actor, new object[] { owner });
            updateProjectileCaches.Invoke(actor, new object[] { owner });
            if (updateAllBuffs is not null) updateAllBuffs.Invoke(actor, null);
            else updateOneBuff.Invoke(actor, new object[] { owner });
            int slot = Array.FindIndex(Main.projectile, p => p.active && p.owner == owner
                && p.type == MilkcraftBossPetAssets.Projectile(MilkcraftBossPetKind.CastleCore));
            if (slot < 0) throw new InvalidOperationException("CastleCore pet failed to spawn for face/light test.");
            Projectile pet = Main.projectile[slot];

            void Check(ulong updateCount, int expectedFrame, Vector3 expectedLight, string faceName)
            {
                SetDiagnosticGameUpdateCount(updateCount);
                _capturedLightCalls = 0; _lastCapturedLight = Vector3.Zero;
                pet.Update(slot);
                if (!pet.active || pet.frame != expectedFrame || _capturedLightCalls <= 0
                    || Vector3.Distance(_lastCapturedLight, expectedLight * 2.40f) > 0.002f)
                    throw new InvalidOperationException($"CastleCore pet {faceName} face/light mismatch: frame={pet.frame} expected={expectedFrame}, calls={_capturedLightCalls}, light={_lastCapturedLight} expected={expectedLight * 2.40f}.");
            }

            Check(120, 2, new Vector3(0.65f, 0.54f, 0.24f), "yellow");
            Check(320, 5, new Vector3(0.30f, 0.50f, 0.72f), "blue-sorrow");
            Check(420, 6, new Vector3(0.70f, 0.20f, 0.16f), "red-anger");
            return $"CastleCore-pet-native-Update-frameLighting=PASS yellowFrame=2 rgb={new Vector3(0.65f, 0.54f, 0.24f) * 2.40f}; blueFrame=5 rgb={new Vector3(0.30f, 0.50f, 0.72f) * 2.40f}; redFrame=6 rgb={new Vector3(0.70f, 0.20f, 0.16f) * 2.40f}";
        }
        finally
        {
            SetDiagnosticGameUpdateCount(oldCount);
        }
    }

    private static string VerifyLightPropagation(int owner, int tileX, int tileY, MethodInfo setup,
        MethodInfo updatePetLight, MethodInfo updateProjectileCaches, MethodInfo updateBuffs)
    {
        if (Main.gameMenu || Main.dedServ)
            throw new InvalidOperationException("actual light propagation requires the isolated client world/render light engine");
        FieldInfo engineField = typeof(Lighting).GetField("_activeEngine", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Lighting._activeEngine unavailable for propagation benchmark");
        object engine = engineField.GetValue(null) ?? throw new InvalidOperationException("active native lighting engine is null");
        MethodInfo lightTiles = typeof(Lighting).GetMethod(nameof(Lighting.LightTiles), BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
            null, new[] { typeof(int), typeof(int), typeof(int), typeof(int) }, null)
            ?? throw new InvalidOperationException("Lighting.LightTiles(xStart,xEnd,yStart,yEnd) unavailable for propagation benchmark");
        MethodInfo clear = typeof(Lighting).GetMethod(nameof(Lighting.Clear), BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Lighting.Clear unavailable");
        int lightX = tileX, lightY = tileY - 5;
        Rectangle area = new(Math.Max(1, lightX - 24), Math.Max(1, lightY - 20), 48, 40);
        int originalNetMode = Main.netMode;
        bool originalDay = Main.dayTime;
        ulong originalUpdateCount = GetDiagnosticGameUpdateCount();
        Player[] players = Main.player;
        Projectile[] projectiles = Main.projectile;
        int originalMyPlayer = Main.myPlayer;
        Main.netMode = NetmodeID.SinglePlayer;
        Main.dayTime = false;

        void ProcessEngine(Action beforePass = null)
        {
            // Call the same native entry point used by the renderer. LightTiles
            // publishes the region and advances LightingEngine's scan/blur stages.
            for (int pass = 0; pass < 8; pass++)
            {
                beforePass?.Invoke();
                lightTiles.Invoke(null, new object[] { area.X, area.Right, area.Y, area.Bottom });
            }
        }

        Vector3 Sample(int x, int y) => Lighting.GetColor(x, y).ToVector3();
        static string Rgb(Vector3 value) => string.Join(",", value.X.ToString("0.000", CultureInfo.InvariantCulture), value.Y.ToString("0.000", CultureInfo.InvariantCulture), value.Z.ToString("0.000", CultureInfo.InvariantCulture));
        var csv = new StringBuilder("source,dxTiles,dyTiles,r,g,b\n");
        var nativeMaps = new List<(string Label, Microsoft.Xna.Framework.Color[] Pixels)>();

        (Vector3 Center, float RadiusTiles, int PetCount) RunSource(string label, int itemType, ulong? forcedUpdateCount)
        {
            if (forcedUpdateCount.HasValue) SetDiagnosticGameUpdateCount(forcedUpdateCount.Value);
            clear.Invoke(null, null);
            Main.projectile = Enumerable.Range(0, Math.Max(projectiles.Length, Main.maxProjectiles + 1)).Select(_ => new Projectile()).ToArray();
            Player actor = players[owner].SerializedClone();
            setup.Invoke(null, new object[] { actor });
            actor.whoAmI = owner; actor.active = true; actor.dead = false; actor.noItems = false;
            actor.position = new Vector2(tileX * 16f, tileY * 16f - actor.height - 24f);
            actor.miscEquips[1].SetDefaults(itemType);
            Main.player[owner] = actor;
            Vector3 source = Vector3.Zero;
            for (int tick = 0; tick < 8; tick++)
            {
                void EmitLightFromRealPetUpdate()
                {
                    actor.ResetEffects();
                    Array.Clear(actor.ownedProjectileCounts);
                    updatePetLight.Invoke(actor, new object[] { owner });
                    updateProjectileCaches.Invoke(actor, new object[] { owner });
                    updateBuffs.Invoke(actor, new object[] { owner });
                    for (int i = 0; i < Main.projectile.Length; i++)
                    {
                        Projectile projectile = Main.projectile[i];
                        if (projectile.active && projectile.owner == owner) projectile.Update(i);
                    }
                    Projectile currentPet = Main.projectile.FirstOrDefault(projectile => projectile.active && projectile.owner == owner);
                    if (currentPet is not null)
                        source = new Vector3(currentPet.Center.X / 16f, currentPet.Center.Y / 16f, 0f);
                }
                ProcessEngine(EmitLightFromRealPetUpdate);
            }
            int sourceX = source.X > 0f ? (int)source.X : lightX;
            int sourceY = source.Y > 0f ? (int)source.Y : lightY;
            Vector3 center = Sample(sourceX, sourceY);
            float radius = 0f;
            for (int distance = 1; distance <= 24; distance++)
            {
                Vector3 sample = Vector3.Zero;
                foreach (int direction in Enumerable.Range(0, 16))
                {
                    double angle = Math.PI * 2d * direction / 16d;
                    int dx = (int)Math.Round(Math.Cos(angle) * distance);
                    int dy = (int)Math.Round(Math.Sin(angle) * distance);
                    Vector3 color = Sample(sourceX + dx, sourceY + dy);
                    sample = Vector3.Max(sample, color);
                    if (distance is 8 or 16 or 24)
                        csv.AppendLine($"{label},{dx},{dy},{Rgb(color)}");
                }
                if (sample.Length() > 0.06f) radius = distance;
            }
            csv.AppendLine($"{label},0,0,{Rgb(center)}");
            var map = new Microsoft.Xna.Framework.Color[49 * 49];
            for (int y = -24; y <= 24; y++)
                for (int x = -24; x <= 24; x++)
                    map[(y + 24) * 49 + x + 24] = Lighting.GetColor(sourceX + x, sourceY + y);
            nativeMaps.Add((label, map));
            int count = Main.projectile.Count(projectile => projectile.active && projectile.owner == owner);
            return (center, radius, count);
        }

        try
        {
            Main.myPlayer = owner;
            Main.gameMenu = false;
            clear.Invoke(null, null);
            ProcessEngine();
            Vector3 ambient = Sample(lightX, lightY);
            var tests = new (string Label, int Item)[]
            {
                ("Banshee", ModContent.ItemType<BansheePetSummon>()),
                ("FrostWitch", ModContent.ItemType<FrostWitchPetSummon>()),
                ("CastleCore-yellow", ModContent.ItemType<CastleCorePetSummon>()),
                ("CastleCore-blue", ModContent.ItemType<CastleCorePetSummon>()),
                ("CastleCore-red", ModContent.ItemType<CastleCorePetSummon>()),
                ("vanilla-ShadowOrb", ReadItemId("ShadowOrb")),
                ("vanilla-FairyBell", ReadItemId("FairyBell")),
                ("vanilla-Wisp", ReadItemId("WispinaBottle"))
            };
            var rows = new System.Collections.Generic.List<string>();
            foreach (var test in tests)
            {
                ulong? forcedCount = test.Label switch
                {
                    "CastleCore-yellow" => 120UL,
                    "CastleCore-blue" => 320UL,
                    "CastleCore-red" => 420UL,
                    _ => null
                };
                var measured = RunSource(test.Label, test.Item, forcedCount);
                if (measured.Center.Length() <= ambient.Length() + 0.015f || measured.RadiusTiles < 2f || measured.PetCount != 1)
                    throw new InvalidOperationException($"real LightEngine propagation failed for {test.Label}: ambient={ambient}, center={measured.Center}, radius={measured.RadiusTiles}, petProjectiles={measured.PetCount}, engine={engine.GetType().Name}");
                rows.Add($"{test.Label} native-GetColor center={measured.Center} sampledThrough=24tiles lastRingAboveThreshold={measured.RadiusTiles:0} projectiles={measured.PetCount}");
            }
            string outputDirectory = Environment.GetEnvironmentVariable("MILKCRAFT_TERRARIA_SMOKE_OUT") ?? AppContext.BaseDirectory;
            Directory.CreateDirectory(outputDirectory);
            File.WriteAllText(Path.Combine(outputDirectory, "native-light-propagation-radial.csv"), csv.ToString());
            int scale = 4, panelWidth = 49 * scale, panelHeight = 49 * scale, gutter = 8;
            int imageWidth = nativeMaps.Count * panelWidth + (nativeMaps.Count - 1) * gutter;
            var matrix = new Microsoft.Xna.Framework.Color[imageWidth * panelHeight];
            for (int panel = 0; panel < nativeMaps.Count; panel++)
                for (int y = 0; y < panelHeight; y++)
                    for (int x = 0; x < panelWidth; x++)
                    {
                        var color = nativeMaps[panel].Pixels[(y / scale) * 49 + x / scale];
                        matrix[y * imageWidth + panel * (panelWidth + gutter) + x] = color;
                    }
            using (var texture = new Microsoft.Xna.Framework.Graphics.Texture2D(Main.graphics.GraphicsDevice, imageWidth, panelHeight))
            {
                texture.SetData(matrix);
                using var output = File.Create(Path.Combine(outputDirectory, "native-light-propagation-matrix.png"));
                texture.SaveAsPng(output, imageWidth, panelHeight);
            }
            File.WriteAllText(Path.Combine(outputDirectory, "native-light-propagation-panels.txt"),
                string.Join(Environment.NewLine, nativeMaps.Select((map, index) => $"panel {index}: {map.Label}")) + Environment.NewLine +
                "Each pixel is a nearest-neighbor display of native Lighting.GetColor at the corresponding tile; panel covers -24..+24 tiles.");
            return "native-light-propagation=PASS dark-world native LightTiles+GetColor; ambient=" + ambient + "; radial-csv=native-light-propagation-radial.csv matrix=native-light-propagation-matrix.png panels=8 (sampled LightEngine colors); " + string.Join("; ", rows);
        }
        finally
        {
            clear.Invoke(null, null);
            Main.player = players;
            Main.projectile = projectiles;
            Main.netMode = originalNetMode;
            Main.dayTime = originalDay;
            SetDiagnosticGameUpdateCount(originalUpdateCount);
            Main.myPlayer = originalMyPlayer;
        }
    }

    private static int ReadItemId(string name)
    {
        FieldInfo field = typeof(ItemID).GetField(name, BindingFlags.Static | BindingFlags.Public)
            ?? throw new InvalidOperationException($"Vanilla light-pet item ItemID.{name} was not found");
        return Convert.ToInt32(field.IsLiteral ? field.GetRawConstantValue() : field.GetValue(null));
    }

    private static void SetDiagnosticGameUpdateCount(ulong value)
    {
        PropertyInfo property = typeof(Main).GetProperty("GameUpdateCount", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new MissingMemberException(typeof(Main).FullName, "GameUpdateCount");
        object boxed = Convert.ChangeType(value, property.PropertyType);
        MethodInfo setter = property.GetSetMethod(true);
        if (setter is not null) setter.Invoke(null, new[] { boxed });
        else
        {
            FieldInfo backing = typeof(Main).GetField("<GameUpdateCount>k__BackingField", BindingFlags.Static | BindingFlags.NonPublic)
                ?? typeof(Main).GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                    .FirstOrDefault(field => field.Name.Contains("GameUpdateCount", StringComparison.OrdinalIgnoreCase))
                ?? throw new MissingMemberException(typeof(Main).FullName, "GameUpdateCount backing storage");
            backing.SetValue(null, Convert.ChangeType(value, backing.FieldType));
        }
    }

    private static ulong GetDiagnosticGameUpdateCount()
    {
        PropertyInfo property = typeof(Main).GetProperty("GameUpdateCount", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new MissingMemberException(typeof(Main).FullName, "GameUpdateCount");
        return Convert.ToUInt64(property.GetValue(null));
    }

    private static string TestPlayKitMigration(int playerIndex)
    {
        Player[] oldPlayers = Main.player;
        Item[] oldItems = Main.item;
        string oldPlayKit = Environment.GetEnvironmentVariable("PLAY_KIT");
        string oldMilkcraftPlayKit = Environment.GetEnvironmentVariable("MILKCRAFT_TERRARIA_PLAY_KIT");
        string oldBalanceKit = Environment.GetEnvironmentVariable("MILKCRAFT_TERRARIA_BALANCE_PLAY_KIT");
        MethodInfo setup = typeof(PlayerLoader).GetMethod("SetupPlayer", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("PlayerLoader.SetupPlayer unavailable for playkit migration");
        var oldSave = new Terraria.ModLoader.IO.TagCompound
        {
            ["milkcraftPlayKitGranted"] = true,
            ["milkcraftT3T4PlayKitGranted"] = true,
            ["milkcraft025PassiveKitGranted"] = true,
            ["milkcraft0210BalanceKitGranted"] = true,
            ["milkcraft0212MountKitGranted"] = true,
            ["milkcraft0213MountKitGranted"] = true,
            ["milkcraft030BossMountKitGranted"] = true,
            ["milkcraft0301BossCollectionKitGranted"] = true
        };
        int[] petItems = Kinds.Select(MilkcraftBossPetAssets.Item).ToArray();
        int[] requestedItems = petItems.Concat(new[] { (int)ItemID.NebulaPickaxe, (int)ItemID.LunarHamaxeNebula }).ToArray();
        int CountWorld(int type) => Main.item.Where(item => item != null && item.active && item.type == type)
            .Where(item => Vector2.Distance(item.Center, Main.player[playerIndex].Center) <= 160f)
            .Sum(item => item.stack);
        int[] WorldCounts() => requestedItems.Select(CountWorld).ToArray();
        Player NewPlayer(Player source)
        {
            Player actor = source.SerializedClone();
            setup.Invoke(null, new object[] { actor });
            actor.whoAmI = playerIndex; actor.active = true; actor.dead = false;
            return actor;
        }
        try
        {
            Main.player = (Player[])oldPlayers.Clone();
            Main.item = Enumerable.Range(0, Math.Max(oldItems.Length, Main.maxItems + 1)).Select(_ => new Item()).ToArray();
            Environment.SetEnvironmentVariable("PLAY_KIT", "1");
            Environment.SetEnvironmentVariable("MILKCRAFT_TERRARIA_PLAY_KIT", "0");
            Environment.SetEnvironmentVariable("MILKCRAFT_TERRARIA_BALANCE_PLAY_KIT", "0");

            Player migration = NewPlayer(oldPlayers[playerIndex]);
            foreach (Item item in migration.inventory) item.TurnToAir();
            Main.player[playerIndex] = migration;
            migration.GetModPlayer<PlayKitPlayer>().LoadData(oldSave); // Existing 0.3 player flags; new flags absent.
            bool enabled = (bool)(typeof(PlayKitPlayer).GetProperty("Enabled", BindingFlags.Static | BindingFlags.NonPublic)?.GetValue(null) ?? false);
            migration.GetModPlayer<PlayKitPlayer>().OnEnterWorld();
            int[] firstCounts = WorldCounts();
            if (firstCounts.Any(count => count != 1))
            {
                var afterFailure = new Terraria.ModLoader.IO.TagCompound();
                migration.GetModPlayer<PlayKitPlayer>().SaveData(afterFailure);
                int dropped = Main.item.Where(item => item != null && item.active && requestedItems.Contains(item.type)).Sum(item => item.stack);
                throw new InvalidOperationException($"0.3 migration expected seven pets + Nebula pick/hamaxe as nearby native world drops exactly once; got {string.Join(',', firstCounts)}; env={(Environment.GetEnvironmentVariable("PLAY_KIT") ?? "<null>")}/{(Environment.GetEnvironmentVariable("MILKCRAFT_TERRARIA_PLAY_KIT") ?? "<null>")} enabled={enabled} net={Main.netMode} myPlayer={Main.myPlayer} owner={migration.whoAmI} active={migration.active} dead={migration.dead} inventoryLength={migration.inventory.Length} worldItems={dropped} savedMarkers=nebula:{afterFailure.GetBool("milkcraft031NebulaToolsKitGranted")},pet:{afterFailure.GetBool("milkcraft031BossPetKitGranted")}");
            }

            migration.GetModPlayer<PlayKitPlayer>().OnEnterWorld();
            if (!WorldCounts().SequenceEqual(firstCounts))
                throw new InvalidOperationException("PlayKit OnEnterWorld issued duplicate boss pet/tool grants before save");

            var saved = new Terraria.ModLoader.IO.TagCompound();
            migration.GetModPlayer<PlayKitPlayer>().SaveData(saved);
            Player reloaded = NewPlayer(migration);
            Main.player[playerIndex] = reloaded;
            reloaded.GetModPlayer<PlayKitPlayer>().LoadData(saved);
            reloaded.GetModPlayer<PlayKitPlayer>().OnEnterWorld();
            int[] afterReload = WorldCounts();
            if (!afterReload.SequenceEqual(firstCounts))
                throw new InvalidOperationException($"PlayKit migration repeated after SaveData/LoadData: before={string.Join(',', firstCounts)} after={string.Join(',', afterReload)}");

            foreach (Item item in Main.item)
                if (item != null && item.active && requestedItems.Contains(item.type)) item.active = false;
            Environment.SetEnvironmentVariable("PLAY_KIT", "0");
            Environment.SetEnvironmentVariable("MILKCRAFT_TERRARIA_PLAY_KIT", "0");
            Player disabled = NewPlayer(oldPlayers[playerIndex]);
            foreach (Item item in disabled.inventory) item.TurnToAir();
            Main.player[playerIndex] = disabled;
            disabled.GetModPlayer<PlayKitPlayer>().LoadData(oldSave);
            disabled.GetModPlayer<PlayKitPlayer>().OnEnterWorld();
            int disabledCount = WorldCounts().Sum();
            if (disabledCount != 0)
                throw new InvalidOperationException($"PLAY_KIT disabled still granted migration items: count={disabledCount}");

            return $"playkit-migration=PASS old-0.3-flags=true new-pet/tool-flags=missing; QuickSpawnItem nearby-world-drops=7pets+NebulaPickaxe+NebulaHamaxe counts={string.Join(',', firstCounts)}; repeated-entry=0; SaveData/LoadData=0 duplicate; PLAY_KIT=0 grants=0";
        }
        finally
        {
            Main.player = oldPlayers; Main.item = oldItems;
            Environment.SetEnvironmentVariable("PLAY_KIT", oldPlayKit);
            Environment.SetEnvironmentVariable("MILKCRAFT_TERRARIA_PLAY_KIT", oldMilkcraftPlayKit);
            Environment.SetEnvironmentVariable("MILKCRAFT_TERRARIA_BALANCE_PLAY_KIT", oldBalanceKit);
        }
    }
}


