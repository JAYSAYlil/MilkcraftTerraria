using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;
using Terraria.UI;
using Terraria.Localization;
using MilkcraftTerraria.Content.Items;

namespace MilkcraftTerraria.Content;

internal static class ProgressionBossDiagnostics
{
    public static string Run()
    {
        bool oldMenu = Main.gameMenu;
        int oldMode = Main.netMode;
        int oldGameMode = Main.GameMode;
        bool oldHardMode = Main.hardMode;
        FieldInfo[] downedFlags = typeof(NPC).GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .Where(field => field.FieldType == typeof(bool) && !field.IsInitOnly).ToArray();
        bool[] oldDownedFlags = downedFlags.Select(field => (bool)field.GetValue(null)).ToArray();
        int oldMyPlayer = Main.myPlayer;
        bool oldGen = WorldGen.gen;
        bool oldGenerating = WorldGen.generatingWorld;
        bool oldNoActions = WorldGen.noTileActions;
        bool oldDestroy = WorldGen.destroyObject;
        bool oldRightRelease = Main.mouseRightRelease;
        bool oldMouseRight = Main.mouseRight;
        Player player = Main.LocalPlayer;
        bool oldPlayerActive = player.active;
        bool oldPlayerDead = player.dead;
        int oldSelected = player.selectedItem;
        int oldItemAnimation = player.itemAnimation;
        Item[] oldInventory = player.inventory.Select(item => item.Clone()).ToArray();
        NPC[] oldNpcs = (NPC[])Main.npc.Clone();
        Item[] oldItems = Main.item;
        TagCompound oldGolemCycle = new();
        ModContent.GetInstance<ProgressionGolemCycleSystem>().SaveWorldData(oldGolemCycle);
        bool oldEaterClaim = ProgressionBossCycleSystem.EaterRewardClaimed;
        bool oldTwinClaim = ProgressionBossCycleSystem.TwinsRewardClaimed;
        try
        {
            Main.gameMenu = false;
            Main.netMode = NetmodeID.SinglePlayer;
            Main.GameMode = 0;
            Main.hardMode = true;
            if (Main.expertMode)
                throw new InvalidOperationException("could not switch boss fixture to normal difficulty");
            Main.myPlayer = player.whoAmI;
            player.active = true;
            player.dead = false;
            player.itemAnimation = 0;
            Main.item = Enumerable.Range(0, Math.Max(400, Main.maxItems + 1)).Select(_ => new Item()).ToArray();
            WorldGen.gen = false;
            WorldGen.generatingWorld = false;
            WorldGen.noTileActions = false;
            WorldGen.destroyObject = false;

            string eater = TestFinalSegmentRewards();
            string normalDrops = TestNormalBossDrops();
            string expertBags = TestBossBags(Main.GameMode = 1, player);
            string masterBags = TestBossBags(Main.GameMode = 2, player);
            string golem = TestGolemCycleAndActualBagOpen(player);
            string golemNormal = TestGolemNormalCycle();
            string golemMaster = TestGolemMasterCycle(player);
            return eater + "\n" + normalDrops + "\n" + expertBags + "\n" + masterBags + "\n" + golem + "\n" + golemNormal + "\n" + golemMaster;
        }
        finally
        {
            Main.gameMenu = oldMenu;
            Main.netMode = oldMode;
            Main.myPlayer = oldMyPlayer;
            WorldGen.gen = oldGen;
            WorldGen.generatingWorld = oldGenerating;
            WorldGen.noTileActions = oldNoActions;
            WorldGen.destroyObject = oldDestroy;
            player.active = oldPlayerActive;
            player.dead = oldPlayerDead;
            player.selectedItem = oldSelected;
            for (int i = 0; i < player.inventory.Length; i++)
                player.inventory[i] = oldInventory[i];
            Main.npc = oldNpcs;
            Main.item = oldItems;
            Main.mouseRightRelease = oldRightRelease;
            Main.mouseRight = oldMouseRight;
            player.itemAnimation = oldItemAnimation;
            Main.GameMode = oldGameMode;
            Main.hardMode = oldHardMode;
            for (int i = 0; i < downedFlags.Length; i++) downedFlags[i].SetValue(null, oldDownedFlags[i]);
            ModContent.GetInstance<ProgressionGolemCycleSystem>().LoadWorldData(oldGolemCycle);
            ProgressionBossCycleSystem.EaterRewardClaimed = oldEaterClaim;
            ProgressionBossCycleSystem.TwinsRewardClaimed = oldTwinClaim;
        }
    }

    private static string TestFinalSegmentRewards()
    {
        int[] slots = { 40, 41, 42, 43 };
        foreach (int slot in slots)
            Main.npc[slot] = new NPC();
        NPC head = MakeNpc(slots[0], NPCID.EaterofWorldsHead);
        NPC body = MakeNpc(slots[1], NPCID.EaterofWorldsBody);
        NPC tail = MakeNpc(slots[2], NPCID.EaterofWorldsTail);
        Main.npc[slots[0]] = head;
        Main.npc[slots[1]] = body;
        Main.npc[slots[2]] = tail;
        ProgressionBossCycleSystem.EaterRewardClaimed = false;
        KillThroughNativeCheckDead(tail);
        Require(CountWorldItem(ModContent.ItemType<BlackIronOre>()) == 0,
            "Eater paid while head/body segments were still alive");
        KillThroughNativeCheckDead(body);
        Require(CountWorldItem(ModContent.ItemType<BlackIronOre>()) == 0,
            "Eater paid before the last segment's native CheckDead");
        head.life = 0;
        KillThroughNativeCheckDead(head);
        Require(CountWorldItem(ModContent.ItemType<BlackIronOre>()) == 120
            && CountWorldItem(ModContent.ItemType<PinkDiamond>()) == 24,
            "final Eater segment did not award exactly 120 ore and 24 diamonds once");
        Require(ProgressionBossCycleSystem.EaterRewardClaimed,
            "Eater encounter did not set its one-reward guard");
        head.GetGlobalNPC<ProgressionBossLoot>().OnKill(head);
        Require(CountWorldItem(ModContent.ItemType<BlackIronOre>()) == 120
            && CountWorldItem(ModContent.ItemType<PinkDiamond>()) == 24,
            "duplicate Eater final OnKill callback paid a second reward");

        NPC nextEater = MakeNpc(slots[3], NPCID.EaterofWorldsHead);
        Main.npc[slots[3]] = nextEater;
        ProgressionBossCycleSystem.ObserveEaterSpawn(nextEater);
        Require(!ProgressionBossCycleSystem.EaterRewardClaimed,
            "new Eater encounter failed to reset reward guard when SetDefaults actor was already active");
        nextEater.life = 0;
        KillThroughNativeCheckDead(nextEater);
        Require(CountWorldItem(ModContent.ItemType<BlackIronOre>()) == 240
            && CountWorldItem(ModContent.ItemType<PinkDiamond>()) == 48,
            "second Eater encounter did not award a fresh single reward");

        // Twins use the same final-living-member rule and per-encounter guard.
        int twinA = 44, twinB = 45;
        NPC retinazer = MakeNpc(twinA, NPCID.Retinazer);
        NPC spazmatism = MakeNpc(twinB, NPCID.Spazmatism);
        Main.npc[twinA] = retinazer;
        Main.npc[twinB] = spazmatism;
        ProgressionBossCycleSystem.TwinsRewardClaimed = false;
        retinazer.life = 0;
        KillThroughNativeCheckDead(retinazer);
        Require(CountWorldItem(ModContent.ItemType<TruthStaff>()) == 0,
            "Twin reward paid while the other Twin was alive");
        spazmatism.life = 0;
        KillThroughNativeCheckDead(spazmatism);
        Require(CountWorldItem(ModContent.ItemType<TruthStaff>()) == 1
            && CountWorldItem(ModContent.ItemType<ArcaneCore>()) == 1,
            "Twins did not award exactly one pair on the last living Twin");
        return "eater-final-segment=PASS native-NPC.checkDead=PASS head/body/tail alive-guard=PASS duplicate-final-callback=PASS consecutive-encounter-reset=PASS twins-last-member=PASS";
    }

    private static string TestNormalBossDrops()
    {
        Main.GameMode = 0;
        TestNormalDrop(NPCID.BrainofCthulhu, (ModContent.ItemType<BlackIronOre>(), 120), (ModContent.ItemType<PinkDiamond>(), 24));
        TestNormalDrop(NPCID.SkeletronPrime, (ModContent.ItemType<TruthSword>(), 1), (ModContent.ItemType<CrystalSkull>(), 1));
        TestNormalDrop(NPCID.TheDestroyer, (ModContent.ItemType<TruthBow>(), 1), (ModContent.ItemType<AlienKeratin>(), 1));
        TestNormalDrop(NPCID.WallofFlesh, (ModContent.ItemType<EnchantmentHeart>(), 1), (0, 0));
        return "normal-boss-guaranteed-drops=PASS brain-prime-destroyer-wall-of-flesh=native-NPC.checkDead";
    }

    private static void TestNormalDrop(int npcType, (int Type, int Stack) first, (int Type, int Stack) second)
    {
        Main.item = Enumerable.Range(0, Math.Max(400, Main.maxItems + 1)).Select(_ => new Item()).ToArray();
        NPC npc = MakeNpc(60 + npcType % 20, npcType);
        Main.npc[npc.whoAmI] = npc;
        KillThroughNativeCheckDead(npc);
        Require(CountWorldItem(first.Type) == first.Stack, $"normal boss type {npcType} did not guarantee {first.Type} x{first.Stack}");
        if (second.Type > 0)
            Require(CountWorldItem(second.Type) == second.Stack, $"normal boss type {npcType} did not guarantee {second.Type} x{second.Stack}");
    }

    private static string TestBossBags(int gameMode, Player player)
    {
        Main.GameMode = gameMode;
        if ((gameMode >= 1) != Main.expertMode || (gameMode == 2) != Main.masterMode)
            throw new InvalidOperationException($"could not enter isolated boss bag difficulty {gameMode}: GameMode={Main.GameMode} expert={Main.expertMode} master={Main.masterMode}");
        var cases = new (short[] Npcs, int Bag, (int, int) A, (int, int) B)[]
        {
            (new[] { NPCID.BrainofCthulhu }, ItemID.BrainOfCthulhuBossBag, (ModContent.ItemType<BlackIronOre>(), 120), (ModContent.ItemType<PinkDiamond>(), 24)),
            (new[] { NPCID.EaterofWorldsHead }, ItemID.EaterOfWorldsBossBag, (ModContent.ItemType<BlackIronOre>(), 120), (ModContent.ItemType<PinkDiamond>(), 24)),
            (new[] { NPCID.Retinazer, NPCID.Spazmatism }, ItemID.TwinsBossBag, (ModContent.ItemType<TruthStaff>(), 1), (ModContent.ItemType<ArcaneCore>(), 1)),
            (new[] { NPCID.SkeletronPrime }, ItemID.SkeletronPrimeBossBag, (ModContent.ItemType<TruthSword>(), 1), (ModContent.ItemType<CrystalSkull>(), 1)),
            (new[] { NPCID.TheDestroyer }, ItemID.DestroyerBossBag, (ModContent.ItemType<TruthBow>(), 1), (ModContent.ItemType<AlienKeratin>(), 1)),
            (new[] { NPCID.WallofFlesh }, ItemID.WallOfFleshBossBag, (ModContent.ItemType<EnchantmentHeart>(), 1), (0, 0))
        };
        foreach (var test in cases)
        {
            Main.npc = Enumerable.Range(0, Main.maxNPCs).Select(_ => new NPC()).ToArray();
            Main.item = Enumerable.Range(0, Math.Max(400, Main.maxItems + 1)).Select(_ => new Item()).ToArray();
            foreach (int slot in Enumerable.Range(0, player.inventory.Length)) player.inventory[slot].TurnToAir();
            NPC[] actors = new NPC[test.Npcs.Length];
            for (int i = 0; i < test.Npcs.Length; i++)
            {
                actors[i] = MakeNpc(30 + i, test.Npcs[i]);
                Main.npc[actors[i].whoAmI] = actors[i];
            }
            for (int i = 0; i < actors.Length; i++)
            {
                KillThroughNativeCheckDead(actors[i]);
                if (test.Npcs.Length > 1 && i == 0)
                    Require(!Main.item.Any(item => item.active && item.type == test.Bag),
                        $"multi-member boss {test.Bag} produced a bag before the last member died");
            }
            Item bag = Main.item.FirstOrDefault(item => item.active && item.type == test.Bag)
                ?? throw new InvalidOperationException($"native {gameMode} difficulty CheckDead did not create boss bag {test.Bag}");
            int bagIndex = Array.IndexOf(Main.item, bag);
            player.inventory[0] = bag.Clone();
            Main.item[bagIndex].TurnToAir();
            player.selectedItem = 0;
            player.itemAnimation = 0;
            Main.mouseRight = true;
            Main.mouseRightRelease = true;
            ItemSlot.RightClick(player.inventory, ItemSlot.Context.InventoryItem, 0);
            Main.mouseRight = false;
            Require(CountPlayerOrWorld(player, test.A.Item1) == test.A.Item2,
                $"native {gameMode} boss bag {test.Bag} omitted guaranteed reward {test.A.Item1}");
            if (test.B.Item1 > 0)
                Require(CountPlayerOrWorld(player, test.B.Item1) == test.B.Item2,
                    $"native {gameMode} boss bag {test.Bag} omitted guaranteed reward {test.B.Item1}");
        }
        return $"boss-bags-{(gameMode == 1 ? "expert" : "master")}=PASS eater-brain-twins-prime-destroyer-wall-of-flesh=actual-CheckDead+ItemSlot.RightClick";
    }

    private static string TestGolemCycleAndActualBagOpen(Player player)
    {
        Item tooltipBag = new();
        tooltipBag.SetDefaults(ItemID.GolemBossBag);
        ProgressionGolemBagRole tooltipRole = tooltipBag.GetGlobalItem<ProgressionGolemBagRole>();
        tooltipRole.Role = GolemRewardRole.Warrior;
        var roleLines = new List<TooltipLine>();
        tooltipRole.ModifyTooltips(tooltipBag, roleLines);
        string expectedWarrior = Language.GetTextValue("Mods.MilkcraftTerraria.GolemBagBonus.Warrior");
        Require(roleLines.Count == 1 && roleLines[0].Text == expectedWarrior && !expectedWarrior.StartsWith("Mods."),
            "role-tagged Golem bag did not identify its Warrior bonus in localized tooltip");
        tooltipRole.Role = GolemRewardRole.None;
        roleLines.Clear();
        tooltipRole.ModifyTooltips(tooltipBag, roleLines);
        Require(roleLines.Count == 0, "legacy roleless Golem bag showed a false bonus tooltip");

        var cycle = ModContent.GetInstance<ProgressionGolemCycleSystem>();
        cycle.LoadWorldData(new TagCompound { ["MilkcraftGolemNextRole"] = (byte)0 });
        int[] expectedBroken = { ModContent.ItemType<BrokenSwordHilt>(), ModContent.ItemType<BrokenBowstring>(), ModContent.ItemType<BrokenStaffHandle>() };
        int[] expectedCore = { ModContent.ItemType<WitherTear>(), ModContent.ItemType<DemonWing>(), ModContent.ItemType<EnchantedShinbone>() };
        int[] npcSlots = { 50, 51, 52 };
        Item[] delayedBags = new Item[npcSlots.Length];
        Main.GameMode = 1;
        if (!Main.expertMode)
            throw new InvalidOperationException("could not switch isolated boss fixture to Expert mode");
        for (int roleIndex = 0; roleIndex < npcSlots.Length; roleIndex++)
        {
            NPC golem = MakeNpc(npcSlots[roleIndex], NPCID.Golem);
            Main.npc[npcSlots[roleIndex]] = golem;
            golem.life = 0;
            KillThroughNativeCheckDead(golem);
            GolemRewardRole prepared = (GolemRewardRole)(roleIndex + 1);
            Item bag = Main.item.FirstOrDefault(item => item.active && item.type == ItemID.GolemBossBag
                && item.GetGlobalItem<ProgressionGolemBagRole>().Role == prepared)
                ?? throw new InvalidOperationException($"native Golem checkDead did not create the {prepared} expert bag with its role");
            int bagIndex = Array.IndexOf(Main.item, bag);
            var bagRole = bag.GetGlobalItem<ProgressionGolemBagRole>();
            if (bagRole.Role != prepared || cycle.CurrentPreparedRole != GolemRewardRole.None)
                throw new InvalidOperationException($"bag OnSpawn lost prepared role: prepared={prepared} bag={bagRole.Role}");

            var roleTag = new TagCompound();
            bagRole.SaveData(bag, roleTag);
            Item persistedClone = bag.Clone();
            persistedClone.GetGlobalItem<ProgressionGolemBagRole>().LoadData(persistedClone, roleTag);
            if (persistedClone.GetGlobalItem<ProgressionGolemBagRole>().Role != prepared)
                throw new InvalidOperationException("Golem bag role did not survive clone/save/load");
            Item mismatched = bag.Clone();
            mismatched.GetGlobalItem<ProgressionGolemBagRole>().Role = prepared == GolemRewardRole.Mage ? GolemRewardRole.Warrior : GolemRewardRole.Mage;
            if (bagRole.CanStack(bag, mismatched) || bagRole.CanStackInWorld(bag, mismatched))
                throw new InvalidOperationException("different-role Golem bags can stack in inventory or the world");

            delayedBags[roleIndex] = bag.Clone();
            Main.item[bagIndex].TurnToAir();
        }

        var worldState = new TagCompound();
        cycle.SaveWorldData(worldState);
        cycle.LoadWorldData(worldState);
        if (worldState.GetByte("MilkcraftGolemNextRole") != 0)
            throw new InvalidOperationException("Golem role cycle did not wrap and persist after warrior/ranger/mage");
        // Open the captured bags only after all three native kills advanced the shared cycle.
        for (int roleIndex = 0; roleIndex < delayedBags.Length; roleIndex++)
        {
            for (int slot = 0; slot < player.inventory.Length; slot++)
                player.inventory[slot].TurnToAir();
            player.inventory[0] = delayedBags[roleIndex];
            player.selectedItem = 0;
            player.itemAnimation = 0;
            Main.mouseRight = true;
            Main.mouseRightRelease = true;
            int roleQueryBefore = ProgressionGolemBagOpeningPlayer.RightClickQueryCount;
            ItemSlot.RightClick(player.inventory, ItemSlot.Context.InventoryItem, 0);
            if (ProgressionGolemBagOpeningPlayer.RightClickQueryCount <= roleQueryBefore)
                throw new InvalidOperationException("native ItemSlot.RightClick did not query the Golem bag's role hook");
            int brokenType = expectedBroken[roleIndex];
            int coreType = expectedCore[roleIndex];
            if (CountPlayerOrWorld(player, brokenType) != 1 || CountPlayerOrWorld(player, coreType) != 1)
                throw new InvalidOperationException($"delayed native ItemSlot.RightClick did not open {delayedBags[roleIndex].GetGlobalItem<ProgressionGolemBagRole>().Role} bag after later Golem kills: broken={CountPlayerOrWorld(player, brokenType)} core={CountPlayerOrWorld(player, coreType)}");
            Main.mouseRight = false;
        }
        return "golem-cycle=PASS role-tooltip-and-roleless-legacy=PASS native-NPC.checkDead=PASS expert-bags-created-before-OnKill-clears-role=PASS delayed-actual-ItemSlot.RightClick=PASS per-role bag persistence=PASS cross-role stacking blocked=PASS shared-world-cycle=PASS";
    }

    private static string TestGolemNormalCycle()
    {
        Main.GameMode = 0;
        if (Main.expertMode || Main.masterMode)
            throw new InvalidOperationException("could not enter Normal Golem reward fixture");
        var cycle = ModContent.GetInstance<ProgressionGolemCycleSystem>();
        cycle.LoadWorldData(new TagCompound { ["MilkcraftGolemNextRole"] = (byte)0 });
        int[] brokenTypes = { ModContent.ItemType<BrokenSwordHilt>(), ModContent.ItemType<BrokenBowstring>(), ModContent.ItemType<BrokenStaffHandle>() };
        int[] coreTypes = { ModContent.ItemType<WitherTear>(), ModContent.ItemType<DemonWing>(), ModContent.ItemType<EnchantedShinbone>() };
        Main.npc = Enumerable.Range(0, Main.maxNPCs).Select(_ => new NPC()).ToArray();
        Main.item = Enumerable.Range(0, Math.Max(400, Main.maxItems + 1)).Select(_ => new Item()).ToArray();
        for (int i = 0; i < 3; i++)
        {
            NPC golem = MakeNpc(50 + i, NPCID.Golem);
            Main.npc[golem.whoAmI] = golem;
            KillThroughNativeCheckDead(golem);
            Require(CountWorldItem(brokenTypes[i]) == 1 && CountWorldItem(coreTypes[i]) == 1,
                $"Normal Golem kill {i} did not grant its paired broken material/core exactly once");
        }
        var saved = new TagCompound();
        cycle.SaveWorldData(saved);
        Require(saved.GetByte("MilkcraftGolemNextRole") == 0, "Normal Golem role sequence failed to wrap after three actual kills");
        return "golem-normal-cycle=PASS native-NPC.checkDead=PASS three-kill-paired-rewards=PASS";
    }

    private static string TestGolemMasterCycle(Player player)
    {
        Main.GameMode = 2;
        if (!Main.masterMode || !Main.expertMode)
            throw new InvalidOperationException($"could not enter Master Golem fixture: GameMode={Main.GameMode} expert={Main.expertMode} master={Main.masterMode}");
        var cycle = ModContent.GetInstance<ProgressionGolemCycleSystem>();
        cycle.LoadWorldData(new TagCompound { ["MilkcraftGolemNextRole"] = (byte)0 });
        int[] brokenTypes = { ModContent.ItemType<BrokenSwordHilt>(), ModContent.ItemType<BrokenBowstring>(), ModContent.ItemType<BrokenStaffHandle>() };
        int[] coreTypes = { ModContent.ItemType<WitherTear>(), ModContent.ItemType<DemonWing>(), ModContent.ItemType<EnchantedShinbone>() };
        Main.npc = Enumerable.Range(0, Main.maxNPCs).Select(_ => new NPC()).ToArray();
        for (int i = 0; i < 3; i++)
        {
            Main.item = Enumerable.Range(0, Math.Max(400, Main.maxItems + 1)).Select(_ => new Item()).ToArray();
            foreach (Item item in player.inventory) item.TurnToAir();
            NPC golem = MakeNpc(50 + i, NPCID.Golem);
            Main.npc[golem.whoAmI] = golem;
            KillThroughNativeCheckDead(golem);
            GolemRewardRole expectedRole = (GolemRewardRole)(i + 1);
            Item bag = Main.item.FirstOrDefault(item => item.active && item.type == ItemID.GolemBossBag
                && item.GetGlobalItem<ProgressionGolemBagRole>().Role == expectedRole)
                ?? throw new InvalidOperationException($"Master Golem kill failed to create role {expectedRole} bag");
            int bagIndex = Array.IndexOf(Main.item, bag);
            player.inventory[0] = bag.Clone();
            Main.item[bagIndex].TurnToAir();
            player.selectedItem = 0;
            player.itemAnimation = 0;
            Main.mouseRight = true;
            Main.mouseRightRelease = true;
            int queryBefore = ProgressionGolemBagOpeningPlayer.RightClickQueryCount;
            ItemSlot.RightClick(player.inventory, ItemSlot.Context.InventoryItem, 0);
            Main.mouseRight = false;
            Require(ProgressionGolemBagOpeningPlayer.RightClickQueryCount > queryBefore,
                "Master Golem role bag bypassed the native ItemSlot right-click hook");
            Require(CountPlayerOrWorld(player, brokenTypes[i]) == 1 && CountPlayerOrWorld(player, coreTypes[i]) == 1,
                $"Master Golem role {expectedRole} bag did not yield its exact paired rewards");
        }
        var saved = new TagCompound();
        cycle.SaveWorldData(saved);
        Require(saved.GetByte("MilkcraftGolemNextRole") == 0, "Master Golem role cycle failed to persist wrap");
        Main.item = Enumerable.Range(0, Math.Max(400, Main.maxItems + 1)).Select(_ => new Item()).ToArray();
        foreach (Item item in player.inventory) item.TurnToAir();
        Item legacyBag = new();
        legacyBag.SetDefaults(ItemID.GolemBossBag);
        player.inventory[0] = legacyBag;
        int legacyQueries = ProgressionGolemBagOpeningPlayer.RightClickQueryCount;
        Main.mouseRight = true;
        Main.mouseRightRelease = true;
        ItemSlot.RightClick(player.inventory, ItemSlot.Context.InventoryItem, 0);
        Main.mouseRight = false;
        Require(ProgressionGolemBagOpeningPlayer.RightClickQueryCount > legacyQueries
            && brokenTypes.Concat(coreTypes).All(type => CountPlayerOrWorld(player, type) == 0),
            "roleless legacy Golem bag inherited a same-frame role from a previously opened bag");
        return "golem-master-cycle=PASS native-NPC.checkDead=PASS actual-role-bag-open=PASS three-role-wrap=PASS roleless-same-frame-clear=PASS";
    }

    private static NPC MakeNpc(int whoAmI, int type)
    {
        NPC npc = new();
        npc.whoAmI = whoAmI;
        npc.SetDefaults(type);
        npc.whoAmI = whoAmI;
        npc.active = true;
        npc.life = Math.Max(npc.lifeMax, 1);
        npc.position = new Microsoft.Xna.Framework.Vector2(700f + whoAmI * 20f, 700f);
        return npc;
    }

    private static void KillThroughNativeCheckDead(NPC npc)
    {
        npc.life = 0;
        npc.checkDead();
        if (npc.active)
            throw new InvalidOperationException($"native NPC.checkDead did not deactivate expected death for npcType={npc.type} slot={npc.whoAmI}");
    }

    private static int CountWorldItem(int itemType)
        => Main.item.Where(item => item.active && item.type == itemType).Sum(item => item.stack);

    private static int CountPlayerOrWorld(Player player, int itemType)
        => player.CountItem(itemType) + CountWorldItem(itemType);

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
