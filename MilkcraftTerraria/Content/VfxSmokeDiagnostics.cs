using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Graphics;
using Terraria;
using Terraria.DataStructures;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.Utilities;
using MilkcraftTerraria.Content.Items;

namespace MilkcraftTerraria.Content;

/// <summary>Fresh client fixture for event-driven suit visuals after real equipment/player hooks.</summary>
internal static class VfxSmokeDiagnostics
{
    private sealed record SuitCase(string Name, int Head, int Body, int Legs, int Accessory, int Weapon);

    private static SuitCase[] Cases => new[]
    {
        new SuitCase("Iron", ModContent.ItemType<IronHelmet>(), ModContent.ItemType<IronChestplate>(), ModContent.ItemType<IronGreaves>(), ModContent.ItemType<IronShield>(), ModContent.ItemType<IronBow>()),
        new SuitCase("Leather", ModContent.ItemType<LeatherHeadband>(), ModContent.ItemType<LeatherTunic>(), ModContent.ItemType<LeatherLeggings>(), ModContent.ItemType<IronQuiver>(), ModContent.ItemType<IronBow>()),
        new SuitCase("Cloth", ModContent.ItemType<ClothHood>(), ModContent.ItemType<ClothRobe>(), ModContent.ItemType<ClothLeggings>(), ModContent.ItemType<BeginnerTome>(), ModContent.ItemType<IronStaff>()),
        new SuitCase("Knight", ModContent.ItemType<KnightHelmet>(), ModContent.ItemType<KnightChestplate>(), ModContent.ItemType<KnightGreaves>(), ModContent.ItemType<KnightBulwark>(), ModContent.ItemType<KnightSword>()),
        new SuitCase("Hunting", ModContent.ItemType<HuntingHood>(), ModContent.ItemType<HuntingChestplate>(), ModContent.ItemType<HuntingLeggings>(), ModContent.ItemType<HuntingQuiver>(), ModContent.ItemType<HuntingBow>()),
        new SuitCase("Energy", ModContent.ItemType<EnergyHood>(), ModContent.ItemType<EnergyRobe>(), ModContent.ItemType<EnergyLeggings>(), ModContent.ItemType<EnergyTome>(), ModContent.ItemType<EnergyStaff>()),
        new SuitCase("Templar", ModContent.ItemType<TemplarHelmet>(), ModContent.ItemType<TemplarChestplate>(), ModContent.ItemType<TemplarGreaves>(), ModContent.ItemType<TemplarShield>(), ModContent.ItemType<TemplarSword>()),
        new SuitCase("Nightmare", ModContent.ItemType<NightmareHood>(), ModContent.ItemType<NightmareChestplate>(), ModContent.ItemType<NightmareLeggings>(), ModContent.ItemType<NightmareQuiver>(), ModContent.ItemType<NightmareBow>()),
        new SuitCase("Fallen", ModContent.ItemType<FallenHood>(), ModContent.ItemType<FallenRobe>(), ModContent.ItemType<FallenLeggings>(), ModContent.ItemType<FallenTome>(), ModContent.ItemType<FallenStaff>()),
        new SuitCase("Soulforged", ModContent.ItemType<SoulforgedHelmet>(), ModContent.ItemType<SoulforgedChestplate>(), ModContent.ItemType<SoulforgedGreaves>(), ModContent.ItemType<SoulforgedShield>(), ModContent.ItemType<SoulforgedSword>()),
        new SuitCase("Abyss", ModContent.ItemType<AbyssHood>(), ModContent.ItemType<AbyssChestplate>(), ModContent.ItemType<AbyssLeggings>(), ModContent.ItemType<AbyssQuiver>(), ModContent.ItemType<AbyssBow>()),
        new SuitCase("Dark", ModContent.ItemType<DarkHood>(), ModContent.ItemType<DarkRobe>(), ModContent.ItemType<DarkLeggings>(), ModContent.ItemType<DarkTome>(), ModContent.ItemType<DarkStaff>())
    };

    public static string Render(string outputDirectory, IReadOnlyList<Projectile> pairedProjectiles)
    {
        const int width = 1280, height = 900, columns = 4, cellWidth = 320, cellHeight = 300;
        int localIndex = Math.Clamp(Main.myPlayer, 0, Main.player.Length - 1);
        Player previousPlayer = Main.player[localIndex];
        Projectile[] previousProjectiles = Main.projectile;
        NPC[] previousNpcs = Main.npc;
        Dust[] previousDust = Main.dust;
        bool oldMenu = Main.gameMenu, oldPaused = Main.gamePaused, oldWorldGen = WorldGen.gen;
        bool oldPreviewLighting = SmokeSystem.ForcePreviewLighting;
        Vector2 oldScreen = Main.screenPosition, oldZoom = Main.GameViewMatrix.Zoom;
        UnifiedRandom oldRandom = Main.rand;
        GraphicsDevice device = Main.graphics.GraphicsDevice;
        RenderTargetBinding[] oldTargets = device.GetRenderTargets();
        using RenderTarget2D target = new(device, width, height, false, SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
        Color background = new(13, 17, 27);
        List<string> log = new() { "Native PlayerRenderer suit VFX fixture; each row pair is right idle / left walking with an actual suit event." };
        int rendered = 0;
        try
        {
            Main.gameMenu = false;
            Main.gamePaused = false;
            WorldGen.gen = false;
            Main.screenPosition = Vector2.Zero;
            Main.GameViewMatrix.Zoom = Vector2.One;
            Main.rand = new UnifiedRandom(20261007);
            SmokeSystem.ForcePreviewLighting = true;
            Main.projectile = Enumerable.Range(0, previousProjectiles.Length).Select(_ => new Projectile()).ToArray();
            Main.npc = Enumerable.Range(0, previousNpcs.Length).Select(_ => new NPC()).ToArray();
            Main.dust = Enumerable.Range(0, previousDust.Length).Select(_ => new Dust()).ToArray();
            device.SetRenderTarget(target);
            device.Clear(background);

            Player rendererWarmup = SmokeSystem.CreatePreviewPlayer(Cases[0].Head, Cases[0].Body, Cases[0].Legs);
            rendererWarmup.active = true;
            Main.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp,
                DepthStencilState.None, RasterizerState.CullCounterClockwise);
            SmokeSystem.DrawPreviewPlayer(rendererWarmup, width + 300, height + 300, 1f);
            Main.spriteBatch.End();
            device.Clear(background);

            for (int i = 0; i < Cases.Length; i++)
            {
                SuitCase suit = Cases[i];
                Player actor = SmokeSystem.CreatePreviewPlayer(suit.Head, suit.Body, suit.Legs);
                actor.whoAmI = localIndex;
                actor.active = true;
                actor.isDisplayDollOrInanimate = false;
                actor.statLife = actor.statLifeMax = suit.Name == "Soulforged" ? 500 : 100;
                actor.statMana = 100;
                actor.statManaMax2 = 200;
                actor.statDefense = default;
                actor.endurance = 0f;
                actor.GetModPlayer<Tier3T4ProgressPlayer>().ApplyNetworkState(true, true, false);
                actor.armor[3] = new Item();
                actor.armor[3].SetDefaults(suit.Accessory);
                Main.player[localIndex] = actor;
                MilkcraftPassivePlayer passive = actor.GetModPlayer<MilkcraftPassivePlayer>();
                passive.ResetEffects();
                actor.armor[3].ModItem.UpdateAccessory(actor, false);
                passive.PostUpdateEquips();

                foreach (Projectile p in Main.projectile) p.active = false;
                NPC targetNpc = Main.npc[0];
                targetNpc.SetDefaults(NPCID.BlueSlime);
                targetNpc.whoAmI = 0;
                targetNpc.active = true;
                targetNpc.friendly = false;
                targetNpc.dontTakeDamage = false;
                targetNpc.immortal = false;
                targetNpc.lifeMax = targetNpc.life = 1000;
                targetNpc.Center = actor.Center + new Vector2(64f, 12f);
                TriggerEvent(suit, actor, passive, targetNpc, localIndex);
                passive.PostUpdateEquips();

                bool eventState = passive.VfxPulse > 0 || passive.VfxShield > 0 || passive.VfxBuffTime > 0 ||
                    passive.VfxHuntProcFlash > 0 || passive.VfxManaPaid > 0 || passive.VfxEnergyPaid > 0;
                Require(eventState, $"native {suit.Name} event did not leave a drawable visual state");

                Main.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp,
                    DepthStencilState.None, RasterizerState.CullCounterClockwise);
                int x = (i % columns) * cellWidth;
                int y = (i / columns) * cellHeight;
                actor.direction = 1;
                actor.velocity = Vector2.Zero;
                actor.itemAnimation = actor.itemTime = 0;
                MilkcraftPassiveDrawLayer.ResetDiagnosticBounds();
                SmokeSystem.DrawPreviewPlayer(actor, x + 72, y + 58, 1f);
                int fxLines = MilkcraftPassiveDrawLayer.DiagnosticLineCount;
                float fxMinX = MilkcraftPassiveDrawLayer.DiagnosticMinX;
                float fxMinY = MilkcraftPassiveDrawLayer.DiagnosticMinY;
                float fxMaxX = MilkcraftPassiveDrawLayer.DiagnosticMaxX;
                float fxMaxY = MilkcraftPassiveDrawLayer.DiagnosticMaxY;
                Require(fxLines > 0, $"native {suit.Name} PlayerRenderer emitted no passive MagicPixel line DrawData");
                float expectedFxX = x + 82f, expectedFxY = y + 84f;
                Require(float.IsFinite(fxMinX) && float.IsFinite(fxMinY) &&
                    Math.Abs((fxMinX + fxMaxX) * .5f - expectedFxX) < 75f &&
                    Math.Abs((fxMinY + fxMaxY) * .5f - expectedFxY) < 75f,
                    $"native {suit.Name} passive DrawData is detached from PlayerRenderer actor: bounds=({fxMinX:0.#},{fxMinY:0.#},{fxMaxX:0.#},{fxMaxY:0.#}) actorCenter=({expectedFxX:0.#},{expectedFxY:0.#})");
                actor.inventory[0].SetDefaults(suit.Weapon);
                actor.selectedItem = 0;
                actor.direction = -1;
                actor.velocity = new Vector2(-1.1f, 0f);
                actor.bodyFrame = new Rectangle(0, 56 * 3, 40, 56);
                actor.legFrame = new Rectangle(0, 56 * 3, 40, 56);
                actor.itemAnimation = actor.itemTime = 8;
                actor.controlUseItem = true;
                actor.releaseUseItem = false;
                MilkcraftPassiveDrawLayer.ResetDiagnosticBounds();
                SmokeSystem.DrawPreviewPlayer(actor, x + 224, y + 58, 1f);
                int actionFxLines = MilkcraftPassiveDrawLayer.DiagnosticLineCount;
                float actionMinX = MilkcraftPassiveDrawLayer.DiagnosticMinX;
                float actionMinY = MilkcraftPassiveDrawLayer.DiagnosticMinY;
                float actionMaxX = MilkcraftPassiveDrawLayer.DiagnosticMaxX;
                float actionMaxY = MilkcraftPassiveDrawLayer.DiagnosticMaxY;
                Require(actionFxLines > 0, $"native {suit.Name} action PlayerRenderer emitted no passive DrawData");
                Require(Math.Abs((actionMinX + actionMaxX) * .5f - (x + 234f)) < 75f &&
                    Math.Abs((actionMinY + actionMaxY) * .5f - (y + 84f)) < 75f,
                    $"native {suit.Name} action DrawData detached from actor: bounds=({actionMinX:0.#},{actionMinY:0.#},{actionMaxX:0.#},{actionMaxY:0.#})");
                Main.spriteBatch.End();

                int buffs = actor.buffType.Count(type => MilkcraftPassiveVfx.StatusBuffType(suit.Name) == type);
                Require(buffs == 1, $"native {suit.Name} event preview should retain exactly one status icon; found {buffs}");
                bool wasPaired = passive.IsPaired;
                actor.dead = true;
                passive.PostUpdateEquips();
                bool deathCleared = !actor.buffType.Contains(MilkcraftPassiveVfx.StatusBuffType(suit.Name));
                Require(deathCleared, $"native {suit.Name} status buff remained after death");
                actor.dead = false;
                actor.armor[0].TurnToAir(); actor.armor[1].TurnToAir(); actor.armor[2].TurnToAir();
                passive.ResetEffects();
                passive.PostUpdateEquips();
                bool unequipCleared = !Cases.Any(c => actor.buffType.Contains(MilkcraftPassiveVfx.StatusBuffType(c.Name)));
                Require(unequipCleared, $"native {suit.Name} status buff remained after unequip");
                log.Add($"suit={suit.Name} set={suit.Name} paired={wasPaired} eventState=PASS buffIcon={buffs} nativePlayerRenderer=PASS rightIdle+leftWalk=PASS passiveDrawDataRight={fxLines}@({fxMinX:0.#},{fxMinY:0.#},{fxMaxX:0.#},{fxMaxY:0.#}) passiveDrawDataLeft={actionFxLines}@({actionMinX:0.#},{actionMinY:0.#},{actionMaxX:0.#},{actionMaxY:0.#}) actorNear=PASS deathClearsStatus={deathCleared} unequipClearsStatus={unequipCleared}");
                rendered++;
            }

            device.SetRenderTargets(oldTargets);
            Color[] pixels = new Color[width * height];
            target.GetData(pixels);
            for (int i = 0; i < Cases.Length; i++)
            {
                int x0 = (i % columns) * cellWidth, y0 = (i / columns) * cellHeight, changed = 0;
                for (int y = y0; y < y0 + cellHeight; y++)
                for (int x = x0; x < x0 + cellWidth; x++)
                {
                    Color p = pixels[y * width + x];
                    if (p.R != background.R || p.G != background.G || p.B != background.B) changed++;
                }
                Require(changed > 500, $"native VFX suit cell is blank: {Cases[i].Name}, visiblePixels={changed}");
                log.Add($"pixels suit={Cases[i].Name} changed={changed}");
            }
            using FileStream stream = File.Create(Path.Combine(outputDirectory, "native-passive-vfx-12-sets.png"));
            target.SaveAsPng(stream, width, height);
            File.WriteAllLines(Path.Combine(outputDirectory, "native-passive-vfx-12-sets.log"), log);
            string tooltipResult = RenderTooltipColorSample(outputDirectory);
            string projectileResult = RenderProjectileVfx15(outputDirectory, pairedProjectiles, localIndex);
            string stagesResult = RenderSuitVfxPhases(outputDirectory, localIndex);
            string armorMaterialResult = ArmorEmissiveDiagnostics.Render(outputDirectory, localIndex);
            return $"native-passive-vfx=PASS actualEventStates={rendered}/12 PlayerRenderer=12x(right-idle,left-walk) pixels=12/12 active-passive-DrawData=12/12 read-only-statusBuff=12/12; {tooltipResult}; {projectileResult}; {stagesResult}; {armorMaterialResult}";
        }
        finally
        {
            try { Main.spriteBatch.End(); } catch { }
            device.SetRenderTargets(oldTargets);
            Main.player[localIndex] = previousPlayer;
            Main.projectile = previousProjectiles;
            Main.npc = previousNpcs;
            Main.dust = previousDust;
            Main.gameMenu = oldMenu;
            Main.gamePaused = oldPaused;
            WorldGen.gen = oldWorldGen;
            SmokeSystem.ForcePreviewLighting = oldPreviewLighting;
            Main.screenPosition = oldScreen;
            Main.GameViewMatrix.Zoom = oldZoom;
            Main.rand = oldRandom;
            MilkcraftPassiveDrawLayer.DiagnosticCapture = false;
        }
    }

    private static string RenderSuitVfxPhases(string outputDirectory, int owner)
    {
        const int cellWidth = 420, cellHeight = 130, headerHeight = 58, width = 1280, height = 900;
        GraphicsDevice device = Main.graphics.GraphicsDevice;
        RenderTargetBinding[] oldTargets = device.GetRenderTargets();
        Color background = new(14, 18, 28);
        Vector2 oldScreen = Main.screenPosition, oldZoom = Main.GameViewMatrix.Zoom;
        bool oldMenu = Main.gameMenu, oldPaused = Main.gamePaused;
        List<string> log = new() { "Each phase uses a fresh actor/event and actual PlayerRenderer DrawData; two pages of six suits, early=0f, mid=12f, late=44f after real PostUpdateEquips timer ticks." };
        string[] phaseNames = { "触发初期 · 0帧", "展开中段 · 12帧", "收束后期 · 44帧" };
        try
        {
            Main.gameMenu = false;
            Main.gamePaused = false;
            WorldGen.gen = false;
            Main.screenPosition = Vector2.Zero;
            Main.GameViewMatrix.Zoom = Vector2.One;
            for (int page = 0; page < 2; page++)
            {
                using RenderTarget2D target = new(device, width, height, false, SurfaceFormat.Color,
                    DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
                int[,] lineCounts = new int[6, 3];
                device.SetRenderTarget(target);
                device.Clear(background);
                Player rendererWarmup = SmokeSystem.CreatePreviewPlayer(Cases[page * 6].Head, Cases[page * 6].Body, Cases[page * 6].Legs);
                rendererWarmup.active = true;
                Main.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp,
                    DepthStencilState.None, RasterizerState.CullNone);
                SmokeSystem.DrawPreviewPlayer(rendererWarmup, width + 300, height + 300, 1f);
                Main.spriteBatch.End();
                device.Clear(background);
                Main.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp,
                    DepthStencilState.None, RasterizerState.CullNone);
                for (int phase = 0; phase < 3; phase++)
                    Terraria.UI.Chat.ChatManager.DrawColorCodedStringWithShadow(Main.spriteBatch, FontAssets.MouseText.Value,
                        phaseNames[phase], new Vector2(phase * cellWidth + 14, 8), Color.White, 0f,
                        Vector2.Zero, Vector2.One, cellWidth - 28, 1f);

                for (int row = 0; row < 6; row++)
                {
                    int i = page * 6 + row;
                    SuitCase suit = Cases[i];
                    Player actor = SmokeSystem.CreatePreviewPlayer(suit.Head, suit.Body, suit.Legs);
                    actor.whoAmI = owner;
                    actor.active = true;
                    actor.isDisplayDollOrInanimate = false;
                    actor.dead = false;
                    actor.statLife = actor.statLifeMax = suit.Name == "Soulforged" ? 500 : 100;
                    actor.statMana = 100;
                    actor.statManaMax2 = 200;
                    actor.statDefense = default;
                    actor.endurance = 0f;
                    actor.GetModPlayer<Tier3T4ProgressPlayer>().ApplyNetworkState(true, true, false);
                    actor.armor[3] = new Item();
                    actor.armor[3].SetDefaults(suit.Accessory);
                    Main.player[owner] = actor;
                    MilkcraftPassivePlayer passive = actor.GetModPlayer<MilkcraftPassivePlayer>();
                    passive.ResetEffects();
                    actor.armor[3].ModItem.UpdateAccessory(actor, false);
                    actor.setBonus = string.Empty;
                    passive.PostUpdateEquips();
                    foreach (Projectile projectile in Main.projectile) projectile.active = false;
                    NPC npc = Main.npc[0];
                    npc.SetDefaults(NPCID.BlueSlime);
                    npc.whoAmI = 0;
                    npc.active = true;
                    npc.friendly = false;
                    npc.dontTakeDamage = false;
                    npc.immortal = false;
                    npc.lifeMax = npc.life = 1000;
                    npc.Center = actor.Center + new Vector2(64f, 12f);
                    TriggerEvent(suit, actor, passive, npc, owner);
                    actor.setBonus = string.Empty;
                    passive.PostUpdateEquips();
                    Require(passive.VfxPulse > 0 || passive.VfxShield > 0 || passive.VfxBuffTime > 0 ||
                        passive.VfxHuntProcFlash > 0 || passive.VfxManaPaid > 0 || passive.VfxEnergyPaid > 0,
                        $"phase preview {suit.Name} had no triggered state");

                    for (int phase = 0; phase < 3; phase++)
                    {
                        if (phase > 0)
                        {
                            int frames = phase == 1 ? 12 : 32;
                            for (int frame = 0; frame < frames; frame++)
                            {
                                actor.setBonus = string.Empty;
                                passive.PostUpdateEquips();
                            }
                        }
                        actor.direction = 1;
                        actor.velocity = Vector2.Zero;
                        actor.itemAnimation = actor.itemTime = 0;
                        MilkcraftPassiveDrawLayer.ResetDiagnosticBounds();
                        int x = phase * cellWidth;
                        int y = headerHeight + row * cellHeight;
                        SmokeSystem.DrawPreviewPlayer(actor, x + cellWidth / 2 - 20, y + 7, 1f);
                        lineCounts[row, phase] = MilkcraftPassiveDrawLayer.DiagnosticLineCount;
                        bool effectExpected = phase < 2 || PersistentVfxState(suit.Name, passive);
                        if (effectExpected)
                        {
                            Require(lineCounts[row, phase] > 0,
                                $"native {suit.Name} phase {phaseNames[phase]} should render but has no MagicPixel DrawData");
                            float centerX = (MilkcraftPassiveDrawLayer.DiagnosticMinX + MilkcraftPassiveDrawLayer.DiagnosticMaxX) * .5f;
                            float centerY = (MilkcraftPassiveDrawLayer.DiagnosticMinY + MilkcraftPassiveDrawLayer.DiagnosticMaxY) * .5f;
                            Require(float.IsFinite(centerX) && float.IsFinite(centerY) &&
                                Math.Abs(centerX - (x + cellWidth / 2f)) < 105f && centerY >= y - 3 && centerY <= y + cellHeight + 8,
                                $"native {suit.Name} phase {phaseNames[phase]} DrawData is outside its isolated row: bounds=({MilkcraftPassiveDrawLayer.DiagnosticMinX:0.#},{MilkcraftPassiveDrawLayer.DiagnosticMinY:0.#},{MilkcraftPassiveDrawLayer.DiagnosticMaxX:0.#},{MilkcraftPassiveDrawLayer.DiagnosticMaxY:0.#}) row=({x},{y},{cellWidth},{cellHeight})");
                        }
                        log.Add($"page={page + 1} suit={suit.Name} phase={phaseNames[phase]} pulse={passive.VfxPulse} shield={passive.VfxShield} buff={passive.VfxBuffTime} target={passive.VfxHuntTarget} lines={lineCounts[row, phase]} bounds=({MilkcraftPassiveDrawLayer.DiagnosticMinX:0.#},{MilkcraftPassiveDrawLayer.DiagnosticMinY:0.#},{MilkcraftPassiveDrawLayer.DiagnosticMaxX:0.#},{MilkcraftPassiveDrawLayer.DiagnosticMaxY:0.#}) expectedVisible={effectExpected}");
                    }
                }
                Main.spriteBatch.End();
                device.SetRenderTargets(oldTargets);
                Color[] pixels = new Color[width * height];
                target.GetData(pixels);
                for (int row = 0; row < 6; row++)
                for (int phase = 0; phase < 3; phase++)
                {
                    int x0 = phase * cellWidth, y0 = headerHeight + row * cellHeight, changed = 0;
                    for (int y = y0; y < y0 + cellHeight; y++)
                    for (int x = x0; x < x0 + cellWidth; x++)
                    {
                        Color pixel = pixels[y * width + x];
                        if (pixel.R != background.R || pixel.G != background.G || pixel.B != background.B) changed++;
                    }
                    Require(changed > 40, $"native early/mid VFX image cell is blank: {Cases[page * 6 + row].Name}, phase={phase}, pixels={changed}");
                }
                using FileStream stream = File.Create(Path.Combine(outputDirectory, $"native-passive-vfx-early-mid-late-page-{page + 1}.png"));
                target.SaveAsPng(stream, width, height);
            }
            File.WriteAllLines(Path.Combine(outputDirectory, "native-passive-vfx-12-early-mid-late.log"), log);
            return "suiteVfxStages=PASS actualPlayerRenderer=12x3 early=12/12 mid=12/12 late=timer-expired-or-persistent-state matrix=two-pages/six-suits/page/rowHeight=130";
        }
        finally
        {
            try { Main.spriteBatch.End(); } catch { }
            device.SetRenderTargets(oldTargets);
            Main.screenPosition = oldScreen;
            Main.GameViewMatrix.Zoom = oldZoom;
            Main.gameMenu = oldMenu;
            Main.gamePaused = oldPaused;
            MilkcraftPassiveDrawLayer.DiagnosticCapture = false;
        }

        static bool PersistentVfxState(string suit, MilkcraftPassivePlayer state)
            => state.VfxShield > 0 || state.VfxBuffTime > 0 || suit == "Hunting" && state.VfxHuntTarget >= 0;
    }

    private static string RenderProjectileVfx15(string outputDirectory, IReadOnlyList<Projectile> pairedProjectiles, int owner)
    {
        const int width = 1280, height = 900;
        Require(pairedProjectiles.Count == 8, $"15-projectile VFX preview needs eight real paired ItemCheck shots, got {pairedProjectiles.Count}");
        Projectile[] oldProjectiles = Main.projectile;
        NPC[] oldNpcs = Main.npc;
        Dust[] oldDust = Main.dust;
        Player oldPlayer = Main.player[owner];
        bool oldMenu = Main.gameMenu, oldPaused = Main.gamePaused, oldWorldgen = WorldGen.gen;
        Vector2 oldScreen = Main.screenPosition, oldZoom = Main.GameViewMatrix.Zoom;
        UnifiedRandom oldRandom = Main.rand;
        GraphicsDevice device = Main.graphics.GraphicsDevice;
        RenderTargetBinding[] oldTargets = device.GetRenderTargets();
        using RenderTarget2D target = new(device, width, height, false, SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
        Color background = new(8, 10, 20);
        string[] names =
        {
            "IronPairedArrow", "HuntingPairedArrow", "NightmarePairedArrow", "AbyssPairedArrow", "IronPairedBolt", "EnergyPairedBolt", "FallenPairedBolt", "DarkPairedBolt",
            "EmberBolt", "EnergyBolt", "FallenBolt", "DarkBolt", "SuitPassive-Nightmare", "SuitPassive-Abyss", "SuitPassive-Dark",
            "KnightSwordWave", "TemplarWave", "SoulforgedWave"
        };
        Vector2[] anchors = new Vector2[18];
        for (int i = 0; i < 8; i++) anchors[i] = new Vector2(92 + (i % 4) * 205, 110 + (i / 4) * 185);
        anchors[8] = new Vector2(950, 110); anchors[9] = new Vector2(1120, 110);
        anchors[10] = new Vector2(950, 295); anchors[11] = new Vector2(1120, 295);
        anchors[12] = new Vector2(940, 520); anchors[13] = new Vector2(1060, 520); anchors[14] = new Vector2(1180, 520);
        anchors[15] = new Vector2(930, 745); anchors[16] = new Vector2(1060, 745); anchors[17] = new Vector2(600, 745);
        string modName = ModContent.GetInstance<MilkcraftTerrariaMod>().Name;
        List<string> log = new() { "18 themed projectiles; first eight are real ItemCheck results, remaining ten use production projectile classes; each gets 8 native Projectile.Update ticks before dark PreDraw." };
        try
        {
            Main.gameMenu = false;
            Main.gamePaused = false;
            WorldGen.gen = false;
            Main.screenPosition = Vector2.Zero;
            Main.GameViewMatrix.Zoom = Vector2.One;
            Main.rand = new UnifiedRandom(20261007);
            Player actor = SmokeSystem.CreatePreviewPlayer(ModContent.ItemType<IronHelmet>(), ModContent.ItemType<IronChestplate>(), ModContent.ItemType<IronGreaves>());
            actor.active = true;
            actor.whoAmI = owner;
            actor.position = new Vector2(150, 80);
            actor.statLife = actor.statLifeMax = 500;
            Main.player[owner] = actor;
            Main.projectile = Enumerable.Range(0, oldProjectiles.Length).Select(_ => new Projectile()).ToArray();
            Main.npc = Enumerable.Range(0, oldNpcs.Length).Select(_ => new NPC()).ToArray();
            Main.dust = Enumerable.Range(0, oldDust.Length).Select(_ => new Dust()).ToArray();
            NPC chase = Main.npc[0];
            chase.SetDefaults(NPCID.BlueSlime);
            chase.whoAmI = 0;
            chase.active = true;
            chase.friendly = false;
            chase.dontTakeDamage = false;
            chase.Center = new Vector2(600, 400);

            Texture2D[] textures = new Texture2D[names.Length];
            for (int i = 0; i < names.Length; i++)
            {
                string texturePath = names[i] switch
                {
                    "FallenBolt" or "DarkBolt" => modName + "/Content/Items/" + names[i],
                    "SuitPassive-Nightmare" => modName + "/Content/Projectiles/NightmarePairedArrow",
                    "SuitPassive-Abyss" => modName + "/Content/Projectiles/AbyssPairedArrow",
                    "SuitPassive-Dark" => modName + "/Content/Projectiles/DarkPairedBolt",
                    "KnightSwordWave" => modName + "/Content/Projectiles/KnightSwordWave",
                    "TemplarWave" or "SoulforgedWave" => modName + "/Content/Items/" + names[i],
                    _ => modName + "/Content/Projectiles/" + names[i]
                };
                textures[i] = ModContent.Request<Texture2D>(texturePath, ReLogic.Content.AssetRequestMode.ImmediateLoad).Value;
                Require(textures[i].Width > 1 && textures[i].Height > 1, $"15-projectile texture did not load: {texturePath} {textures[i].Width}x{textures[i].Height}");
            }

            Main.projectile = Enumerable.Range(0, oldProjectiles.Length).Select(_ => new Projectile()).ToArray();
            Projectile[] cases = new Projectile[18];
            for (int i = 0; i < 8; i++) cases[i] = pairedProjectiles[i];
            int[] defaultTypes =
            {
                ModContent.ProjectileType<EmberBolt>(), ModContent.ProjectileType<EnergyBolt>(),
                ModContent.ProjectileType<FallenBolt>(), ModContent.ProjectileType<DarkBolt>()
            };
            for (int i = 0; i < 4; i++)
            {
                Projectile p = new();
                p.SetDefaults(defaultTypes[i]);
                cases[8 + i] = p;
            }
            for (int i = 0; i < 3; i++)
            {
                Projectile p = new();
                p.SetDefaults(ModContent.ProjectileType<SuitPassiveProjectile>());
                p.ai[0] = 0;
                p.ai[1] = i + 1;
                p.DamageType = i == 2 ? DamageClass.Magic : DamageClass.Ranged;
                cases[12 + i] = p;
            }
            int[] swordWaveTypes =
            {
                ModContent.ProjectileType<KnightSwordWave>(),
                ModContent.ProjectileType<TemplarWave>(),
                ModContent.ProjectileType<SoulforgedWave>()
            };
            for (int i = 0; i < swordWaveTypes.Length; i++)
            {
                Projectile p = new();
                p.SetDefaults(swordWaveTypes[i]);
                cases[15 + i] = p;
            }

            for (int i = 0; i < cases.Length; i++)
            {
                Projectile p = cases[i];
                int slot = i;
                Require(slot < Main.projectile.Length, $"projectile slot unavailable: {slot}");
                p.whoAmI = slot;
                p.owner = owner;
                p.active = true;
                p.friendly = true;
                p.hostile = false;
                p.tileCollide = false;
                p.timeLeft = 180;
                p.damage = Math.Max(1, p.damage);
                Require(p.light == 0f, $"themed projectile retained neutral light: {names[i]} light={p.light}");
                if (i >= 8)
                {
                    p.Center = anchors[i];
                    p.velocity = new Vector2(4.1f, -.28f);
                }
                p.scale = 1f;
                if (p.ModProjectile is SuitPassiveProjectile) p.ai[0] = 0;
                Main.projectile[slot] = p;
                if (p.ModProjectile is SuitPassiveProjectile)
                    Require((int)p.ai[1] is >= 1 and <= 3,
                        $"invalid suit passive variant for {names[i]}");
            }
            MilkcraftPairedProjectile.ResetDiagnosticLights();
            SmokeSystem.ResetVfxLightCapture();
            for (int tick = 0; tick < 8; tick++)
            for (int i = 0; i < cases.Length; i++)
            {
                Projectile p = cases[i];
                Require(p.active, $"projectile became inactive before render: {names[i]} tick={tick}");
                p.Update(i);
            }
            int dustCount = Main.dust.Count(d => d.active);
            Require(dustCount > 0, $"native projectile updates emitted no real Dust across 15 cases; activeDust={dustCount}");
            Require(MilkcraftPairedProjectile.DiagnosticLightCalls >= 8 * 15,
                $"native projectile PostAI did not execute real RGB Lighting.AddLight for every styled projectile: {MilkcraftPairedProjectile.DiagnosticLightCalls}/120");
            (int Index, Vector3 Main, Vector3 Accent)[] weaponLightAudit =
            {
                (10, new Vector3(.92f, .73f, .22f), new Vector3(.20f, .80f, .91f) * .16f),
                (11, new Vector3(.86f, .16f, .27f), new Vector3(.38f, .25f, .70f) * .16f),
                (15, new Vector3(.91f, .22f, .22f), new Vector3(.96f, .72f, .18f) * .16f),
                (16, new Vector3(.99f, .79f, .23f), new Vector3(.20f, .80f, .91f) * .16f),
                (17, new Vector3(.95f, .22f, .20f), new Vector3(.89f, .69f, .21f) * .16f)
            };
            foreach ((int index, Vector3 main, Vector3 accent) in weaponLightAudit)
            {
                IReadOnlyList<Vector3> lightSamples = SmokeSystem.GetVfxLightSamples(cases[index].type);
                Require(lightSamples.Count >= 16, $"native AI did not issue 8 main+accent AddLight pairs for {names[index]}: calls={lightSamples.Count}");
                bool hasMain = lightSamples.Any(c => Vector3.DistanceSquared(c, main) < .00001f);
                bool hasAccent = lightSamples.Any(c => Vector3.DistanceSquared(c, accent) < .00001f);
                Require(hasMain && hasAccent, $"native RGB AddLight palette mismatch for {names[index]}: expected main={main} accent={accent}; calls={string.Join(";", lightSamples)}");
                log.Add($"rgbLight name={names[index]} projectileLight={cases[index].light:0.##} calls={lightSamples.Count} main={main} accent={accent} actual=PASS");
            }

            device.SetRenderTarget(target);
            device.Clear(background);
            for (int i = 0; i < cases.Length; i++)
            {
                Projectile p = cases[i];
                Require(p.active, $"projectile inactive before PreDraw: {names[i]}");
                Vector2 oldScreenPosition = Main.screenPosition;
                Vector2 cameraFollow = p.Center - anchors[i];
                Main.screenPosition = cameraFollow;
                p.scale = 1.65f;
                Main.spriteBatch.Begin(SpriteSortMode.Immediate, BlendState.AlphaBlend, SamplerState.PointClamp,
                    DepthStencilState.None, RasterizerState.CullNone);
                Color light = Color.Black;
                if (i < 8 || i is >= 8 and <= 11)
                {
                    MilkcraftPairedProjectile global = p.GetGlobalProjectile<MilkcraftPairedProjectile>();
                    Require(global.PreDraw(p, ref light) == false, $"production PreDraw did not take over for {names[i]}");
                }
                else
                    Require(p.ModProjectile.PreDraw(ref light) == false, $"production passive PreDraw did not draw {names[i]}");
                Main.spriteBatch.End();
                Main.screenPosition = oldScreenPosition;
                FieldInfo countField = typeof(MilkcraftPairedProjectile).GetField("_vfxCount", BindingFlags.Instance | BindingFlags.NonPublic);
                int history = countField is null ? 0 : (int)countField.GetValue(p.GetGlobalProjectile<MilkcraftPairedProjectile>());
                if (i < 15)
                    Require(history >= 8, $"{names[i]} missing real VFX history after native updates: {history}/8");
                else
                    Require(p.light == 0f, $"sword wave regained neutral white light during update: {names[i]} light={p.light}");
                log.Add($"case={i + 1}/18 name={names[i]} type={p.type} owner={p.owner} sourceCenter=({p.Center.X:0.#},{p.Center.Y:0.#}) cameraFollow=({cameraFollow.X:0.#},{cameraFollow.Y:0.#}) updates=8 history={history} projectileLight={p.light:0.##} rgbAddLight={(i < 15 ? "PostAI main+accent" : "wave AI main+accent")} dustActive={dustCount} darkPreDraw=PASS");
            }
            device.SetRenderTargets(oldTargets);
            Color[] pixels = new Color[width * height];
            target.GetData(pixels);
            for (int i = 0; i < cases.Length; i++)
            {
                int roiWidth = i < 8 ? 185 : i < 12 ? 150 : i < 15 ? 110 : 210;
                int roiHeight = i < 8 ? 165 : i < 12 ? 150 : i < 15 ? 110 : 210;
                int x0 = Math.Max(0, (int)anchors[i].X - roiWidth / 2), y0 = Math.Max(0, (int)anchors[i].Y - roiHeight / 2), nonBackground = 0;
                int x1 = Math.Min(width, x0 + roiWidth), y1 = Math.Min(height, y0 + roiHeight);
                for (int y = y0; y < y1; y++)
                for (int x = x0; x < x1; x++)
                {
                    Color c = pixels[y * width + x];
                    if (c.R != background.R || c.G != background.G || c.B != background.B) nonBackground++;
                }
                Require(nonBackground > 0, $"dark projectile VFX cell is blank: {names[i]}");
                log.Add($"pixelAudit name={names[i]} anchor=({anchors[i].X:0},{anchors[i].Y:0}) roi=({x0},{y0},{x1 - x0},{y1 - y0}) nonBackground={nonBackground}");
            }
            using FileStream stream = File.Create(Path.Combine(outputDirectory, "projectile-vfx-18-dark.png"));
            target.SaveAsPng(stream, width, height);
            log.Add($"native-update=PASS actualItemCheckPaired=8/8 defaultMagic=4/4 suitPassive=3/3 oldSwordWaves=3/3 neutralProjectileLight=0/18 realDustActive={dustCount} actualPostAILightCalls={MilkcraftPairedProjectile.DiagnosticLightCalls} lastLight=({MilkcraftPairedProjectile.DiagnosticLastLightPosition.X:0.#},{MilkcraftPairedProjectile.DiagnosticLastLightPosition.Y:0.#}) darkPreDraw=18/18 nonBlankRegions=18/18");
            File.WriteAllLines(Path.Combine(outputDirectory, "projectile-vfx-18-dark.log"), log);
            return $"projectile-vfx=PASS actualItemCheck=8 defaultMagic=4 suitPassive=3 oldSwordWaves=3 nativeUpdates=8x18 neutralLight=0/18 darkPreDraw=18/18 nonBlank=18/18 dust={dustCount} AddLight={MilkcraftPairedProjectile.DiagnosticLightCalls}";
        }
        finally
        {
            try { Main.spriteBatch.End(); } catch { }
            device.SetRenderTargets(oldTargets);
            Main.player[owner] = oldPlayer;
            Main.projectile = oldProjectiles;
            Main.npc = oldNpcs;
            Main.dust = oldDust;
            Main.gameMenu = oldMenu;
            Main.gamePaused = oldPaused;
            WorldGen.gen = oldWorldgen;
            Main.screenPosition = oldScreen;
            Main.GameViewMatrix.Zoom = oldZoom;
            Main.rand = oldRandom;
            MilkcraftPairedProjectile.DiagnosticLightCapture = false;
        }
    }

    private static string RenderTooltipColorSample(string outputDirectory)
    {
        const int width = 1240, height = 420;
        Mod mod = ModContent.GetInstance<MilkcraftTerrariaMod>();
        ReLogic.Graphics.DynamicSpriteFont font = FontAssets.MouseText.Value;
        int owner = Math.Clamp(Main.myPlayer, 0, Main.player.Length - 1);
        Player oldPlayer = Main.player[owner];
        bool oldMenu = Main.gameMenu, oldPaused = Main.gamePaused, oldWorldgen = WorldGen.gen;
        Vector2 oldScreen = Main.screenPosition, oldZoom = Main.GameViewMatrix.Zoom;
        GraphicsDevice device = Main.graphics.GraphicsDevice;
        RenderTargetBinding[] oldTargets = device.GetRenderTargets();
        using RenderTarget2D target = new(device, width, height, false, SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
        Color background = new(15, 18, 28);
        try
        {
            Main.gameMenu = false;
            Main.gamePaused = false;
            WorldGen.gen = false;
            Main.screenPosition = Vector2.Zero;
            Main.GameViewMatrix.Zoom = Vector2.One;

            Player active = CreateTooltipPreviewPlayer(owner, complete: true);
            List<TooltipLine> activeLines = new() { new(mod, "SetBonus", active.setBonus) };
            MilkcraftPassiveVfx.TintArmorTooltip(activeLines, mod, "Templar");
            Main.player[owner] = active;
            List<TooltipLine> accessoryLines = new();
            MilkcraftPassiveVfx.TintAccessoryTooltip(accessoryLines, mod, "TemplarShield");

            Player incomplete = CreateTooltipPreviewPlayer(owner, complete: false);
            List<TooltipLine> incompleteLines = new() { new(mod, "SetBonus", incomplete.setBonus) };
            MilkcraftPassiveVfx.TintArmorTooltip(incompleteLines, mod, "Templar");

            Player cooldown = CreateTooltipPreviewPlayer(owner, complete: true);
            MilkcraftPassivePlayer cooldownState = cooldown.GetModPlayer<MilkcraftPassivePlayer>();
            cooldownState.ResetEffects();
            cooldown.armor[0].ModItem.UpdateArmorSet(cooldown);
            cooldown.armor[3].ModItem.UpdateAccessory(cooldown, false);
            cooldownState.PostUpdateEquips();
            cooldown.isDisplayDollOrInanimate = false;
            cooldown.statDefense = default;
            cooldown.endurance = 0f;
            cooldown.immune = false;
            cooldown.immuneTime = 0;
            int shieldBefore = cooldownState.DiagnosticShield("Templar");
            int lifeBefore = cooldown.statLife;
            cooldown.Hurt(PlayerDeathReason.ByCustomReason(Terraria.Localization.NetworkText.FromLiteral("VFX tooltip fixture")), 120, 1, false, true);
            cooldown.immune = false;
            cooldown.immuneTime = 0;
            cooldown.armor[0].ModItem.UpdateArmorSet(cooldown);
            cooldownState.PostUpdateEquips();
            Require(cooldownState.DiagnosticShield("Templar") == 0 && cooldownState.DiagnosticShieldCooldown("Templar") > 0,
                $"tooltip screenshot needs an actual broken Templar barrier and live cooldown; shield={shieldBefore}->{cooldownState.DiagnosticShield("Templar")}, cooldown={cooldownState.DiagnosticShieldCooldown("Templar")}, life={lifeBefore}->{cooldown.statLife}");
            Require(GetTooltipColor(activeLines, "SetBonus") == MilkcraftPassiveVfx.SuitColor("Templar"), "complete-set TooltipLine did not receive its suit theme color");
            Require(GetTooltipColor(incompleteLines, "PassiveState") == new Color(145, 149, 158), "incomplete-set state TooltipLine is not gray");
            Require(GetTooltipColor(accessoryLines, "PassivePair") == new Color(113, 222, 142), "matched accessory TooltipLine is not green");
            string activeBonus = active.setBonus;
            string cooldownBonus = cooldown.setBonus;
            Require(cooldownBonus.Contains("[c/"), "cooldown SetBonus did not preserve native color-tagged state text");

            device.SetRenderTarget(target);
            device.Clear(background);
            Main.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp,
                DepthStencilState.None, RasterizerState.CullNone);
            Terraria.UI.Chat.ChatManager.DrawColorCodedStringWithShadow(Main.spriteBatch, font,
                "奶块套装被动 · 原生 SetBonus / Tooltip 状态预览", new Vector2(24, 14), Color.White, 0f, Vector2.Zero, Vector2.One, 1180f, 2f);
            DrawChatLine("完整套装 · 主题色与配套绿", activeBonus, 24, 62, MilkcraftPassiveVfx.SuitColor("Templar"));
            DrawTooltipRow("未穿齐 · 灰色状态", FindTooltipText(incompleteLines, "PassiveState"), GetTooltipColor(incompleteLines, "PassiveState") ?? new Color(145, 149, 158), 24, 150, font);
            DrawTooltipRow("同阶配套 · 绿色强化", FindTooltipText(accessoryLines, "PassivePair"), GetTooltipColor(accessoryLines, "PassivePair") ?? new Color(145, 149, 158), 24, 218, font);
            DrawChatLine("实际受击破盾 · 金色冷却", cooldownBonus, 24, 286, Color.White);
            Main.spriteBatch.End();
            device.SetRenderTargets(oldTargets);
            Color[] pixels = new Color[width * height];
            target.GetData(pixels);
            int visible = pixels.Count(pixel => pixel.R != background.R || pixel.G != background.G || pixel.B != background.B);
            Require(visible > 2000, $"native ChatManager tooltip preview has too few visible pixels: {visible}");
            using FileStream stream = File.Create(Path.Combine(outputDirectory, "native-passive-tooltip-colors.png"));
            target.SaveAsPng(stream, width, height);
            File.WriteAllLines(Path.Combine(outputDirectory, "native-passive-tooltip-colors.log"), new[]
            {
                $"ChatManager=PASS font=FontAssets.MouseText nativeLanguage={Terraria.Localization.Language.ActiveCulture.Name}",
                $"activeSetBonus={activeBonus}",
                $"incompleteState={FindTooltipText(incompleteLines, "PassiveState")} color={GetTooltipColor(incompleteLines, "PassiveState")}",
                $"matchedAccessory={FindTooltipText(accessoryLines, "PassivePair")} color={GetTooltipColor(accessoryLines, "PassivePair")}",
                $"actualBrokenBarrier={cooldownBonus}",
                $"visiblePixels={visible}"
            });
            return $"native-tooltip-colors=PASS ChatManager=PASS active/incomplete/matched/actualCooldown=4 states visiblePixels={visible}";
        }
        finally
        {
            try { Main.spriteBatch.End(); } catch { }
            device.SetRenderTargets(oldTargets);
            Main.player[owner] = oldPlayer;
            Main.gameMenu = oldMenu;
            Main.gamePaused = oldPaused;
            WorldGen.gen = oldWorldgen;
            Main.screenPosition = oldScreen;
            Main.GameViewMatrix.Zoom = oldZoom;
        }

        Player CreateTooltipPreviewPlayer(int playerIndex, bool complete)
        {
            Player actor = SmokeSystem.CreatePreviewPlayer(ModContent.ItemType<TemplarHelmet>(), ModContent.ItemType<TemplarChestplate>(),
                ModContent.ItemType<TemplarGreaves>());
            if (!complete) actor.armor[2].TurnToAir();
            actor.active = true;
            actor.isDisplayDollOrInanimate = false;
            actor.whoAmI = playerIndex;
            actor.statLife = actor.statLifeMax = 500;
            actor.GetModPlayer<Tier3T4ProgressPlayer>().ApplyNetworkState(true, true, false);
            actor.armor[3].SetDefaults(ModContent.ItemType<TemplarShield>());
            Main.player[playerIndex] = actor;
            MilkcraftPassivePlayer state = actor.GetModPlayer<MilkcraftPassivePlayer>();
            state.ResetEffects();
            if (complete) actor.armor[0].ModItem.UpdateArmorSet(actor);
            actor.armor[3].ModItem.UpdateAccessory(actor, false);
            state.PostUpdateEquips();
            return actor;
        }

        void DrawChatLine(string label, string body, int x, int y, Color fallback)
        {
            Terraria.UI.Chat.ChatManager.DrawColorCodedStringWithShadow(Main.spriteBatch, font, label, new Vector2(x, y), Color.White, 0f, Vector2.Zero, Vector2.One, 1180f, 2f);
            Terraria.UI.Chat.ChatManager.DrawColorCodedStringWithShadow(Main.spriteBatch, font, body, new Vector2(x + 250, y), fallback, 0f, Vector2.Zero, Vector2.One, 930f, 2f);
        }
    }

    private static void DrawTooltipRow(string label, string text, Color color, int x, int y, DynamicSpriteFont font)
    {
        Terraria.UI.Chat.ChatManager.DrawColorCodedStringWithShadow(Main.spriteBatch, font, label, new Vector2(x, y), Color.White, 0f, Vector2.Zero, Vector2.One, 1180f, 2f);
        string tag = $"[c/{color.R:X2}{color.G:X2}{color.B:X2}:{text}]";
        Terraria.UI.Chat.ChatManager.DrawColorCodedStringWithShadow(Main.spriteBatch, font, tag, new Vector2(x + 250, y), Color.White, 0f, Vector2.Zero, Vector2.One, 930f, 2f);
    }

    private static string FindTooltipText(List<TooltipLine> lines, string name)
        => lines.FirstOrDefault(line => line.Name == name)?.Text ?? "";

    private static Color? GetTooltipColor(List<TooltipLine> lines, string name)
        => lines.FirstOrDefault(line => line.Name == name)?.OverrideColor;

    private static void TriggerEvent(SuitCase suit, Player actor, MilkcraftPassivePlayer passive, NPC npc, int owner)
    {
        if (suit.Name is "Iron" or "Knight" or "Templar" or "Soulforged")
        {
            int damage = suit.Name == "Soulforged" ? 120 : suit.Name == "Knight" ? 45 : 5;
            actor.Hurt(PlayerDeathReason.ByCustomReason(Terraria.Localization.NetworkText.FromLiteral("passive VFX fixture")), damage, 1, false, true);
            return;
        }

        Item weapon = new();
        weapon.SetDefaults(suit.Weapon);
        if (suit.Name == "Leather")
        {
            passive.OnHitNPCWithItem(weapon, npc, default, 1);
            return;
        }
        if (suit.Name == "Hunting")
        {
            Projectile shot = Main.projectile[0];
            shot.SetDefaults(ProjectileID.WoodenArrowFriendly);
            shot.active = true;
            shot.owner = owner;
            shot.DamageType = DamageClass.Ranged;
            shot.damage = 24;
            for (int hit = 0; hit < 3; hit++)
            {
                if (hit > 0) for (int frame = 0; frame < 15; frame++) passive.PostUpdateEquips();
                passive.OnHitNPCWithProj(shot, npc, default, 24);
            }
            return;
        }
        if (suit.Name is "Cloth" or "Fallen" or "Energy")
        {
            MilkcraftPassiveGlobalItem globalItem = ModContent.GetInstance<MilkcraftPassiveGlobalItem>();
            globalItem.OnConsumeMana(weapon, actor, 100);
            passive.OnHitNPCWithItem(weapon, npc, default, 1);
            if (suit.Name == "Energy") return;
            for (int frame = 0; frame < 360; frame++) passive.PostUpdateEquips();
            actor.statMana = 0;
            if (suit.Name == "Fallen") actor.statLife = 70;
            globalItem.OnConsumeMana(weapon, actor, 100);
            passive.OnHitNPCWithItem(weapon, npc, default, 1);
            return;
        }

        passive.OnHitNPCWithItem(weapon, npc, default, 1);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
