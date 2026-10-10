using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Terraria;
using Terraria.DataStructures;
using Terraria.GameContent.ItemDropRules;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;
using Terraria.UI;
using Terraria.Utilities;
using Microsoft.Xna.Framework;
using MilkcraftTerraria.Content.Items;

namespace MilkcraftTerraria.Content;

/// <summary>Native checkDead, boss-bag right-click, and difficulty attack-damage checks for the seven new bosses.</summary>
internal static class ProgressionBossNativeDiagnostics
{
    private static int _diagnosticDiscardedServerPackets;

    private static void DiscardDiagnosticServerPacket(On_NetMessage.orig_SendData orig, int msgType, int remoteClient,
        int ignoreClient, NetworkText text, int number, float number2, float number3, float number4,
        int number5, int number6, int number7)
    {
        _diagnosticDiscardedServerPackets++;
    }

    internal static string VerifyLegacyVanillaLootRemoved()
    {
        int[] legacyBosses =
        {
            NPCID.BrainofCthulhu, NPCID.EaterofWorldsHead, NPCID.EaterofWorldsBody, NPCID.EaterofWorldsTail,
            NPCID.Retinazer, NPCID.Spazmatism, NPCID.TheDestroyer, NPCID.SkeletronPrime, NPCID.WallofFlesh, NPCID.Golem
        };
        int[] legacyBags =
        {
            ItemID.BrainOfCthulhuBossBag, ItemID.EaterOfWorldsBossBag, ItemID.TwinsBossBag,
            ItemID.DestroyerBossBag, ItemID.SkeletronPrimeBossBag, ItemID.WallOfFleshBossBag, ItemID.GolemBossBag
        };
        HashSet<int> milkcraftItems = ModContent.GetContent<ModItem>().Select(item => item.Type).ToHashSet();
        foreach (int npcType in legacyBosses)
        {
            int[] added = EnumerateRuleGraph(Main.ItemDropsDB.GetRulesForNPCID(npcType))
                .OfType<IItemDropRule>().Select(ReadRuleItemType).Where(type => milkcraftItems.Contains(type)).Distinct().ToArray();
            if (added.Length != 0)
                throw new InvalidOperationException($"Vanilla NPC {npcType} still has Milkcraft item rules: {string.Join(',', added)}");
        }
        foreach (int bagType in legacyBags)
        {
            int[] added = EnumerateRuleGraph(Main.ItemDropsDB.GetRulesForItemID(bagType))
                .OfType<IItemDropRule>().Select(ReadRuleItemType).Where(type => milkcraftItems.Contains(type)).Distinct().ToArray();
            if (added.Length != 0)
                throw new InvalidOperationException($"Vanilla boss bag {bagType} still has Milkcraft item rules: {string.Join(',', added)}");
        }

        int tiger = ModContent.ItemType<WhiteTigerSummon>();
        foreach (BossCase test in Cases.Take(3))
        {
            object[] npcGraph = EnumerateRuleGraph(Main.ItemDropsDB.GetRulesForNPCID(test.NpcType)).ToArray();
            CommonDrop[] body = npcGraph.OfType<CommonDrop>().Where(rule => rule.itemId == tiger).ToArray();
            IItemDropRuleCondition[] conditions = body.Length == 1 ? GetDirectConditions(body[0]).ToArray() : Array.Empty<IItemDropRuleCondition>();
            bool normalOnly = conditions.Length == 1 && conditions[0].GetType().Name == "NotExpert"
                && EvaluateDifficultyCondition(conditions[0], false, false)
                && !EvaluateDifficultyCondition(conditions[0], true, false)
                && !EvaluateDifficultyCondition(conditions[0], true, true);
            object[] bagGraph = EnumerateRuleGraph(Main.ItemDropsDB.GetRulesForItemID(test.BagType)).ToArray();
            CommonDrop[] bag = bagGraph.OfType<CommonDrop>().Where(rule => rule.itemId == tiger).ToArray();
            if (body.Length != 1 || body[0].chanceNumerator != 1 || body[0].chanceDenominator != 10 || !normalOnly
                || bag.Length != 1 || bag[0].chanceNumerator != 1 || bag[0].chanceDenominator != 10)
                throw new InvalidOperationException($"White Tiger drop mismatch NPC={test.NpcType}: body={string.Join(',', body.Select(rule => $"{rule.chanceNumerator}/{rule.chanceDenominator}"))}/normal={normalOnly}; bag={string.Join(',', bag.Select(rule => $"{rule.chanceNumerator}/{rule.chanceDenominator}"))}");
        }
        return "vanilla-milkcraft-material-rules=PASS vanilla-body-and-bags-clean; preserved exceptions are isolated in Plantera/MoonLord hooks\nwhite-tiger-t2-drops=PASS normal-body 1/10 NotExpert; expert/master bag 1/10; no duplicate body roll";
    }

    private sealed record BossCase(string Name, MilkcraftBossKind Kind, int NpcType, int BagType, BossProjectileStyle ProjectileStyle);

    private static readonly BossCase[] Cases =
    {
        new("WraithWarrior", MilkcraftBossKind.WraithWarrior, ModContent.NPCType<WraithWarriorBoss>(), ModContent.ItemType<WraithWarriorBag>(), BossProjectileStyle.WraithSpear),
        new("JungleLord", MilkcraftBossKind.JungleLord, ModContent.NPCType<JungleLordBoss>(), ModContent.ItemType<JungleLordBag>(), BossProjectileStyle.VineRoot),
        new("Banshee", MilkcraftBossKind.Banshee, ModContent.NPCType<BansheeBoss>(), ModContent.ItemType<BansheeBag>(), BossProjectileStyle.BansheeWave),
        new("ObsidianGuardian", MilkcraftBossKind.ObsidianGuardian, ModContent.NPCType<ObsidianGuardianBoss>(), ModContent.ItemType<ObsidianGuardianBag>(), BossProjectileStyle.IceNaga),
        new("Sphinx", MilkcraftBossKind.Sphinx, ModContent.NPCType<SphinxBoss>(), ModContent.ItemType<SphinxBag>(), BossProjectileStyle.SandShot),
        new("FrostWitch", MilkcraftBossKind.FrostWitch, ModContent.NPCType<FrostWitchBoss>(), ModContent.ItemType<FrostWitchBag>(), BossProjectileStyle.FrostSpear),
        new("CastleCore", MilkcraftBossKind.CastleCore, ModContent.NPCType<CastleCoreBoss>(), ModContent.ItemType<CastleCoreBag>(), BossProjectileStyle.CastleRed)
    };

    internal static string Run()
    {
        bool oldMenu = Main.gameMenu;
        int oldMode = Main.GameMode, oldNetMode = Main.netMode, oldMyPlayer = Main.myPlayer;
        Terraria.IO.WorldFileData oldActiveWorld = Main.ActiveWorldFileData;
        FieldInfo currentModeField = typeof(Main).GetField("_currentGameModeInfo", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Main._currentGameModeInfo unavailable for isolated Journey mode fixture");
        FieldInfo expertOverrideField = typeof(Main).GetField("_overrideForExpertMode", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Main expert override unavailable for Journey fixture");
        FieldInfo masterOverrideField = typeof(Main).GetField("_overrideForMasterMode", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Main master override unavailable for Journey fixture");
        object oldCurrentModeInfo = currentModeField.GetValue(null)!;
        object oldExpertOverride = expertOverrideField.GetValue(null);
        object oldMasterOverride = masterOverrideField.GetValue(null);
        Player[] oldPlayers = (Player[])Main.player.Clone();
        int owner = Math.Clamp(Main.myPlayer, 0, Main.player.Length - 1);
        Player player = Main.player[owner].SerializedClone();
        MethodInfo setupPlayer = typeof(PlayerLoader).GetMethod("SetupPlayer", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("PlayerLoader.SetupPlayer unavailable for isolated native boss player");
        setupPlayer.Invoke(null, new object[] { player });
        player.whoAmI = owner;
        Main.player[owner] = player;
        Item[] oldInventory = player.inventory.Select(item => item.Clone()).ToArray();
        NPC[] oldNpcs = Main.npc;
        Projectile[] oldProjectiles = Main.projectile;
        Item[] oldItems = Main.item;
        bool oldDay = Main.dayTime;
        byte oldPlayerDifficulty = player.difficulty;
        UnifiedRandom oldRandom = Main.rand;
        var oldWorld = new Terraria.ModLoader.IO.TagCompound();
        ModContent.GetInstance<MilkcraftBossWorldSystem>().SaveWorldData(oldWorld);
        string[] rows = new string[Cases.Length * 6];
        float oldJourneySliderValue = GetJourneySliderValue();
        try
        {
            Main.gameMenu = false;
            Main.netMode = NetmodeID.SinglePlayer;
            Main.myPlayer = owner;
            player.active = true;
            player.dead = false;
            player.difficulty = 3; // PlayerDifficultyID.Creative, isolated diagnostic actor only
            Main.ActiveWorldFileData = new Terraria.IO.WorldFileData();
            Main.rand = new UnifiedRandom(0x303011);
            WorldGen.gen = false;
            WorldGen.generatingWorld = false;
            WorldGen.noTileActions = false;
            WorldGen.destroyObject = false;
            MethodInfo fire = typeof(MilkcraftBossNPC).GetMethod("Fire", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("MilkcraftBossNPC.Fire unavailable for native projectile-damage capture");
            MethodInfo quickLookups = typeof(Recipe).GetMethod("CreateRequiredItemQuickLookups",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("Recipe.CreateRequiredItemQuickLookups unavailable for native boss bag right-click");
            string petDrops = VerifyPetDropRegistrations();
            string journeyDifficulty = VerifyJourneySliderConditions();
            quickLookups.Invoke(null, null);

            int row = 0;
            for (int mode = 0; mode <= 5; mode++)
            {
                bool journey = mode >= 3;
                int difficulty = mode % 3;
                SetGameModeAndRefreshOverrides(journey ? 3 : difficulty);
                if (journey) SetJourneySliderValue(difficulty switch { 0 => 0.33f, 1 => 0.66f, _ => 1f });
                MethodInfo updateJourneyOverride = typeof(Main).GetMethod("UpdateCreativeGameModeOverride", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                    ?? throw new InvalidOperationException("Main.UpdateCreativeGameModeOverride unavailable for native Journey fixture");
                updateJourneyOverride.Invoke(null, null);
                // Journey's slider scales NPC strength; native loot rules still
                // read Main.expertMode/Main.masterMode. Respect those live flags.
                int lootDifficulty = journey
                    ? (Main.masterMode ? 2 : Main.expertMode ? 1 : 0)
                    : difficulty;
                string caseModeName = journey
                    ? $"Journey-{ModeName(difficulty)}(loot-{ModeName(lootDifficulty)})"
                    : ModeName(difficulty);
                for (int caseIndex = 0; caseIndex < Cases.Length; caseIndex++)
                {
                    BossCase test = Cases[caseIndex];
                    Main.npc = Enumerable.Range(0, oldNpcs.Length).Select(_ => new NPC()).ToArray();
                    Main.projectile = Enumerable.Range(0, oldProjectiles.Length).Select(_ => new Projectile()).ToArray();
                    Main.item = Enumerable.Range(0, Math.Max(400, Main.maxItems + 1)).Select(_ => new Item()).ToArray();
                    foreach (Item item in player.inventory) item.TurnToAir();

                    NPC boss = new();
                    boss.whoAmI = 60;
                    boss.SetDefaults(test.NpcType);
                    boss.whoAmI = 60;
                    boss.active = true;
                    boss.target = owner;
                    boss.Center = player.Center + new Microsoft.Xna.Framework.Vector2(150f, 0f);
                    Main.npc[boss.whoAmI] = boss;
                    int actualLifeMax = boss.lifeMax;
                    int actualContactDamage = boss.damage;

                    fire.Invoke(boss.ModNPC, new object[] { player, boss.Center, new Microsoft.Xna.Framework.Vector2(-8f, 0f), test.ProjectileStyle, 1f });
                    Projectile shot = Main.projectile.FirstOrDefault(projectile => projectile.active && projectile.type == ModContent.ProjectileType<MilkcraftBossProjectile>())
                        ?? throw new InvalidOperationException($"{test.Name}/{caseModeName}: native boss attack helper spawned no projectile");
                    int projectileDamage = shot.damage;
                    int expectedProjectileDamage = ((MilkcraftBossNPC)boss.ModNPC).AttackDamage(1f);
                    if (projectileDamage != expectedProjectileDamage)
                        throw new InvalidOperationException($"{test.Name}/{caseModeName} projectile damage mismatch: expectedAttackDamage={expectedProjectileDamage}, NPC.damage={actualContactDamage}, shot={projectileDamage}");
                    int empoweredCastleProjectileDamage = 0;
                    if (test.Kind == MilkcraftBossKind.CastleCore)
                    {
                        boss.ai[3] = 1f;
                        Main.projectile = Enumerable.Range(0, oldProjectiles.Length).Select(_ => new Projectile()).ToArray();
                        fire.Invoke(boss.ModNPC, new object[] { player, boss.Center, new Microsoft.Xna.Framework.Vector2(-8f, 0f), test.ProjectileStyle, 1f });
                        Projectile empoweredShot = Main.projectile.FirstOrDefault(projectile => projectile.active && projectile.type == ModContent.ProjectileType<MilkcraftBossProjectile>())
                        ?? throw new InvalidOperationException($"CastleCore/{caseModeName} empowered attack spawned no projectile");
                        empoweredCastleProjectileDamage = empoweredShot.damage;
                        int expectedEmpowered = ((MilkcraftBossNPC)boss.ModNPC).AttackDamage(1f);
                        if (empoweredCastleProjectileDamage != expectedEmpowered)
                            throw new InvalidOperationException($"CastleCore/{caseModeName} empowered projectile expected +20% ({expectedEmpowered}), got {empoweredCastleProjectileDamage}");
                        boss.ai[3] = 0f;
                    }

                    if (test.Kind == MilkcraftBossKind.CastleCore && lootDifficulty > 0)
                        boss.ai[3] = 1f; // challenge was selected before the real death/drop callback
                    boss.life = 0;
                    boss.checkDead();
                    if (boss.active)
                        throw new InvalidOperationException($"{test.Name}/{caseModeName}: native NPC.checkDead did not finish boss death");

                    (int Type, int Count)[] rewards = BossItemTypes.FixedRewards(test.Kind);
                    if (lootDifficulty == 0)
                    {
                        foreach ((int type, int count) in rewards)
                        {
                            int actual = CountWorld(type);
                            if (actual != count)
                                throw new InvalidOperationException($"{test.Name}/{caseModeName}: fixed reward {type} expected {count}, got {actual}");
                        }
                    }
                    else
                    {
                        Item bag = Main.item.FirstOrDefault(item => item.active && item.type == test.BagType)
                            ?? throw new InvalidOperationException($"{test.Name}/{caseModeName}: native checkDead produced no bag");
                        int bagSlot = Array.IndexOf(Main.item, bag);
                        bool empoweredCastle = test.Kind == MilkcraftBossKind.CastleCore && lootDifficulty > 0;
                        if (empoweredCastle)
                        {
                            Item serialized = Terraria.ModLoader.IO.ItemIO.Load(Terraria.ModLoader.IO.ItemIO.Save(bag));
                            if (!serialized.GetGlobalItem<CastleCoreBagItemState>().EmpoweredAtKill)
                                throw new InvalidOperationException($"CastleCore/{ModeName(difficulty)} native checkDead bag lost empowered-at-kill state through ItemIO");
                            bag = serialized;
                            Main.dayTime = false; // kill was empowered in daylight; open only after sunset
                        }
                        player.inventory[0] = bag.Clone();
                        Main.item[bagSlot].TurnToAir();
                        player.selectedItem = 0;
                        player.itemAnimation = 0;
                        Main.mouseRight = true;
                        Main.mouseRightRelease = true;
                        ItemSlot.RightClick(player.inventory, ItemSlot.Context.InventoryItem, 0);
                        Main.mouseRight = false;
                        Main.mouseRightRelease = false;
                        foreach ((int type, int count) in rewards)
                        {
                            int actual = CountPlayerOrWorld(player, type);
                            if (actual != count)
                                throw new InvalidOperationException($"{test.Name}/{caseModeName} bag: fixed reward {type} expected {count}, got {actual}");
                        }
                        if (empoweredCastle)
                        {
                            int unicorns = CountPlayerOrWorld(player, ModContent.ItemType<WhiteUnicornSummon>());
                            if (unicorns != 1)
                                throw new InvalidOperationException($"CastleCore/{ModeName(difficulty)} daylight empowered native checkDead→bag ItemIO→night ItemSlot.RightClick yielded White Unicorn x{unicorns}, expected 1");
                        }
                    }

                    rows[row++] = $"boss={test.Name} mode={(journey ? "Journey-" : "")}{ModeName(difficulty)} actualNpc={test.NpcType} lifeMax={actualLifeMax} contactDamage={actualContactDamage} " +
                        $"projectileStyle={test.ProjectileStyle} actualSpawnDamage={projectileDamage}" +
                        (test.Kind == MilkcraftBossKind.CastleCore ? $" empoweredProjectileDamage={empoweredCastleProjectileDamage}(+20%)" : "") +
                        (test.Kind == MilkcraftBossKind.CastleCore && lootDifficulty > 0 ? " empowered-checkDead-ItemIO-night-open=PASS" : "") +
                        $" rewards={(difficulty == 0 ? "direct-native-checkDead" : "native-bag-ItemSlot.RightClick")} fixedRewardCounts=PASS" +
                        (journey ? $" difficultySource=Journey-slider-{ModeName(difficulty)} lootFlags={Main.expertMode}/{Main.masterMode}" : "");
                }
            }
            string armorHurt = RunStageArmorHurtProbe(owner, player, oldNpcs.Length, oldProjectiles.Length);
            string networkRoundtrip = RunExtraAiAndClientAuthorityProbe(owner, player, oldNpcs.Length, oldProjectiles.Length);
            string aiCycles = RunActualAiCycles(owner, player, oldNpcs.Length, oldProjectiles.Length);
            return "seven-boss-native-drops=PASS normal/expert/master/Journey-slider-normal/expert/master=42/42 checkDead+bag-right-click; attack-projectile-damage=\n" + string.Join("\n", rows) + "\n" + petDrops + "\n" + journeyDifficulty + "\n" + armorHurt + "\n" + networkRoundtrip + "\n" + aiCycles;
        }
        finally
        {
            Main.gameMenu = oldMenu;
            Main.GameMode = oldMode;
            Main.netMode = oldNetMode;
            Main.myPlayer = oldMyPlayer;
            Main.player = oldPlayers;
            Main.npc = oldNpcs;
            Main.projectile = oldProjectiles;
            Main.item = oldItems;
            Main.dayTime = oldDay;
            Main.rand = oldRandom;
            SetJourneySliderValue(oldJourneySliderValue);
            player.difficulty = oldPlayerDifficulty;
            Main.ActiveWorldFileData = oldActiveWorld;
            currentModeField.SetValue(null, oldCurrentModeInfo);
            expertOverrideField.SetValue(null, oldExpertOverride);
            masterOverrideField.SetValue(null, oldMasterOverride);
            Main.GameMode = oldMode;
            for (int i = 0; i < player.inventory.Length; i++) player.inventory[i] = oldInventory[i];
            ModContent.GetInstance<MilkcraftBossWorldSystem>().LoadWorldData(oldWorld);
            Main.mouseRight = false;
            Main.mouseRightRelease = false;
        }

        int CountWorld(int type) => Main.item.Where(item => item != null && item.active && item.type == type).Sum(item => item.stack);
        int CountPlayerOrWorld(Player actor, int type) => actor.CountItem(type) + CountWorld(type);
        static string ModeName(int mode) => mode switch { 0 => "normal", 1 => "expert", _ => "master" };
    }

    private static string VerifyJourneySliderConditions()
    {
        Type managerType = typeof(Terraria.GameContent.Creative.CreativePowerManager);
        Type sliderType = typeof(Terraria.GameContent.Creative.CreativePowers.DifficultySliderPower);
        object manager = managerType.GetProperty("Instance", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(null)
            ?? managerType.GetField("Instance", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(null)
            ?? throw new InvalidOperationException("CreativePowerManager.Instance missing in Journey drop test");
        MethodInfo getPower = managerType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .FirstOrDefault(method => method.Name == "GetPower" && method.IsGenericMethodDefinition && method.GetParameters().Length == 0)
            ?? throw new InvalidOperationException("CreativePowerManager.GetPower<T>() missing in Journey drop test");
        object slider = getPower.MakeGenericMethod(sliderType).Invoke(manager, null)
            ?? throw new InvalidOperationException("native Journey difficulty slider power is unavailable");
        FieldInfo valueField = sliderType.BaseType?.GetField("_sliderCurrentValueCache", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Journey slider value cache field missing");
        float oldSlider = (float)valueField.GetValue(slider)!;
        int oldMode = Main.GameMode;
        try
        {
            SetGameModeAndRefreshOverrides(3);
            var cases = new (float Value, string Name, float Strength)[]
            {
                (0.33f, "normal", 1f),
                (0.66f, "expert", 2f),
                (1f, "master", 3f)
            };
            var rows = new List<string>();
            foreach (var test in cases)
            {
                SetGameModeAndRefreshOverrides(3);
                SetJourneySliderValue(test.Value);
                MethodInfo updateOverride = typeof(Main).GetMethod("UpdateCreativeGameModeOverride", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                    ?? throw new InvalidOperationException("Main.UpdateCreativeGameModeOverride unavailable for native Journey slider test");
                updateOverride.Invoke(null, null);
                bool expert = Main.expertMode, master = Main.masterMode;
                PropertyInfo multiplierProperty = sliderType.GetProperty("StrengthMultiplierToGiveNPCs", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    ?? sliderType.BaseType?.GetProperty("StrengthMultiplierToGiveNPCs", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                float multiplier = Convert.ToSingle(multiplierProperty?.GetValue(slider)
                    ?? throw new InvalidOperationException("Journey NPC strength multiplier property unavailable"));
                if (Math.Abs(multiplier - test.Strength) > 0.051f)
                    throw new InvalidOperationException($"native Journey difficulty slider {test.Name}={test.Value:0.##} yielded strengthMultiplier={multiplier:0.###}, expected {test.Strength:0.###}");
                bool expectedExpert = test.Strength >= 2f;
                bool expectedMaster = test.Strength >= 3f;
                if (expert != expectedExpert || master != expectedMaster)
                    throw new InvalidOperationException($"native Journey override at slider={test.Value:0.##},strength={multiplier:0.###} yielded expert={expert},master={master}; expected {expectedExpert}/{expectedMaster}");
                rows.Add($"{test.Name}@{test.Value:0.##}:expert={expert},master={master},strengthMultiplier={multiplier}");
            }
            return "journey-difficulty-native-slider=PASS GameMode=Journey; SetValueKeyboardForced(value); slider scales NPC strength while native loot flags remain as observed: " + string.Join(";", rows);
        }
        finally
        {
            SetGameModeAndRefreshOverrides(3);
            SetJourneySliderValue(oldSlider);
            Main.GameMode = oldMode;
            typeof(Main).GetMethod("UpdateCreativeGameModeOverride", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.Invoke(null, null);
        }
    }

    private static object GetJourneyDifficultySlider()
    {
        Type managerType = typeof(Terraria.GameContent.Creative.CreativePowerManager);
        Type sliderType = typeof(Terraria.GameContent.Creative.CreativePowers.DifficultySliderPower);
        object manager = managerType.GetProperty("Instance", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(null)
            ?? managerType.GetField("Instance", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(null)
            ?? throw new InvalidOperationException("CreativePowerManager.Instance unavailable");
        MethodInfo getPower = managerType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .FirstOrDefault(method => method.Name == "GetPower" && method.IsGenericMethodDefinition && method.GetParameters().Length == 0)
            ?? throw new InvalidOperationException("CreativePowerManager.GetPower<T>() unavailable");
        return getPower.MakeGenericMethod(sliderType).Invoke(manager, null)
            ?? throw new InvalidOperationException("native Journey difficulty slider unavailable");
    }

    private static float GetJourneySliderValue()
    {
        object slider = GetJourneyDifficultySlider();
        FieldInfo field = slider.GetType().BaseType?.GetField("_sliderCurrentValueCache", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Journey slider value cache field missing");
        return (float)field.GetValue(slider)!;
    }

    private static void SetJourneySliderValue(float value)
    {
        object slider = GetJourneyDifficultySlider();
        Type sliderType = slider.GetType();
        MethodInfo setter = sliderType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Concat(slider.GetType().BaseType?.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) ?? Array.Empty<MethodInfo>())
            .FirstOrDefault(method => method.Name == "SetValueKeyboardForced" && method.GetParameters().Length == 1 && method.GetParameters()[0].ParameterType == typeof(float))
            ?? throw new InvalidOperationException("DifficultySliderPower.SetValueKeyboardForced(float) unavailable");
        setter.Invoke(slider, new object[] { value });
        MethodInfo commit = sliderType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Concat(sliderType.BaseType?.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) ?? Array.Empty<MethodInfo>())
            .FirstOrDefault(method => method.Name == "AttemptPushingChange" && method.GetParameters().Length == 0)
            ?? throw new InvalidOperationException("ASharedSliderPower.AttemptPushingChange() unavailable for native Journey slider commit");
        commit.Invoke(slider, null);
    }

    private static void SetGameModeAndRefreshOverrides(int mode)
    {
        Main.GameMode = mode;
        typeof(Main).GetMethod("UpdateCreativeGameModeOverride", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            ?.Invoke(null, null);
    }
    private static string VerifyPetDropRegistrations()
    {
        var rows = new List<string>();
        foreach (BossCase test in Cases)
        {
            int petType = BossItemTypes.Pet(test.Kind);
            var npcRules = Main.ItemDropsDB.GetRulesForNPCID(test.NpcType, false);
            var npcGraph = EnumerateRuleGraph(npcRules).ToArray();
            CommonDrop[] bodyDrops = npcGraph.OfType<CommonDrop>().Where(rule => rule.itemId == petType).ToArray();
            IItemDropRuleCondition[] bodyConditions = bodyDrops.Length == 1 ? GetDirectConditions(bodyDrops[0]).ToArray() : Array.Empty<IItemDropRuleCondition>();
            bool normalOnly = bodyConditions.Length == 1
                && bodyConditions[0].GetType().Name == "NotExpert"
                && EvaluateDifficultyCondition(bodyConditions[0], expert: false, master: false)
                && !EvaluateDifficultyCondition(bodyConditions[0], expert: true, master: false)
                && !EvaluateDifficultyCondition(bodyConditions[0], expert: true, master: true);
            if (bodyDrops.Length != 1 || bodyDrops[0].chanceNumerator != 1 || bodyDrops[0].chanceDenominator != 4 || !normalOnly)
                throw new InvalidOperationException($"{test.Name} normal boss pet registration mismatch: count={bodyDrops.Length} rates={string.Join(',', bodyDrops.Select(rule => $"{rule.chanceNumerator}/{rule.chanceDenominator}"))} petConditions={string.Join(',', bodyConditions.Select(condition => condition.GetType().Name))} normalOnlyCondition={normalOnly} graph={string.Join('|', npcGraph.Select(value => value.GetType().FullName).Distinct())}");

            var bagRules = Main.ItemDropsDB.GetRulesForItemID(test.BagType);
            var bagGraph = EnumerateRuleGraph(bagRules).ToArray();
            CommonDrop[] bagDrops = bagGraph.OfType<CommonDrop>().Where(rule => rule.itemId == petType).ToArray();
            int petRuleNodes = bagGraph.OfType<IItemDropRule>().Count(rule => ReadRuleItemType(rule) == petType);
            if (bagDrops.Length != 1 || bagDrops[0].chanceNumerator != 1 || bagDrops[0].chanceDenominator != 4 || petRuleNodes != 1)
                throw new InvalidOperationException($"{test.Name} expert/master bag pet registration mismatch: common={bagDrops.Length} rate={string.Join(',', bagDrops.Select(rule => $"{rule.chanceNumerator}/{rule.chanceDenominator}"))} allPetRuleNodes={petRuleNodes}");

            int bodyRuleNodes = npcGraph.OfType<IItemDropRule>().Count(rule => ReadRuleItemType(rule) == petType);
            if (bodyRuleNodes != 1)
                throw new InvalidOperationException($"{test.Name} has an extra normal/master body pet roll: item-rule nodes={bodyRuleNodes}");
            rows.Add($"pet-drop={test.Name} normal-body=1/4(NotExpert) expert-bag=1/4 master-bag=1/4 single-bag-rule=PASS no-extra-master-body-roll=PASS");
        }
        return "seven-boss-pet-drop-registration=PASS native registered rule graph; each normal body 1/4 NotExpert condition evaluates true/false/false for Normal/Expert/Master; each Expert/Master bag has one 1/4 rule; no extra master body rule\n" + string.Join("\n", rows);
    }

    private static IEnumerable<IItemDropRuleCondition> GetDirectConditions(IItemDropRule rule)
    {
        for (Type type = rule.GetType(); type is not null; type = type.BaseType)
        foreach (FieldInfo field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            if (typeof(IItemDropRuleCondition).IsAssignableFrom(field.FieldType) && field.GetValue(rule) is IItemDropRuleCondition condition)
                yield return condition;
    }

    private static bool EvaluateDifficultyCondition(IItemDropRuleCondition condition, bool expert, bool master)
    {
        MethodInfo canDrop = typeof(IItemDropRuleCondition).GetMethod("CanDrop")
            ?? throw new InvalidOperationException("IItemDropRuleCondition.CanDrop unavailable");
        Type infoType = canDrop.GetParameters()[0].ParameterType;
        object info = System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(infoType);
        int oldMode = Main.GameMode;
        try
        {
            SetGameModeAndRefreshOverrides(master ? 2 : expert ? 1 : 0);
            return (bool)(canDrop.Invoke(condition, new[] { info }) ?? false);
        }
        finally { SetGameModeAndRefreshOverrides(oldMode); }
    }

    internal static int ReadRuleItemType(IItemDropRule rule)
    {
        for (Type type = rule.GetType(); type is not null; type = type.BaseType)
        {
            FieldInfo field = type.GetField("itemId", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (field?.GetValue(rule) is int itemType) return itemType;
        }
        return -1;
    }

    internal static IEnumerable<object> EnumerateRuleGraph(IEnumerable<IItemDropRule> roots)
    {
        var pending = new Stack<object>(roots.Cast<object>());
        var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
        while (pending.Count > 0)
        {
            object value = pending.Pop();
            if (!seen.Add(value)) continue;
            yield return value;
            if (value is string || value is Type || value.GetType().IsPrimitive || value.GetType().IsEnum || value.GetType().IsValueType)
                continue;
            if (value is IEnumerable enumerable)
            {
                foreach (object child in enumerable)
                    if (child is not null && (child is IItemDropRule || child.GetType().Namespace?.StartsWith("Terraria.GameContent.ItemDropRules", StringComparison.Ordinal) == true || child is IEnumerable))
                        pending.Push(child);
                continue;
            }
            if (value.GetType().Namespace?.StartsWith("Terraria.GameContent.ItemDropRules", StringComparison.Ordinal) != true)
                continue;
            for (Type type = value.GetType(); type is not null && type.Namespace?.StartsWith("Terraria.GameContent.ItemDropRules", StringComparison.Ordinal) == true; type = type.BaseType)
            foreach (FieldInfo field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                object child;
                try { child = field.GetValue(value); }
                catch { continue; }
                if (child is null) continue;
                Type childType = child.GetType();
                if (child is IItemDropRule || child is IEnumerable || childType.Namespace?.StartsWith("Terraria.GameContent.ItemDropRules", StringComparison.Ordinal) == true)
                    pending.Push(child);
            }
        }
    }

    private static string RunStageArmorHurtProbe(int owner, Player player, int npcLength, int projectileLength)
    {
        NPC[] savedNpcs = Main.npc;
        Projectile[] savedProjectiles = Main.projectile;
        Player savedPlayer = Main.player[owner];
        UnifiedRandom oldRandom = Main.rand;
        int oldMode = Main.GameMode;
        MethodInfo setupPlayer = typeof(PlayerLoader).GetMethod("SetupPlayer", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("PlayerLoader.SetupPlayer unavailable for isolated armor Hurt measurements");
        Tier3T4ProgressPlayer tierLicense = player.GetModPlayer<Tier3T4ProgressPlayer>();
        TagCompound oldTierLicense = new();
        tierLicense.SaveData(oldTierLicense);
        try
        {
            BossHurtProbePlayer.Enabled = true;
            Main.netMode = NetmodeID.SinglePlayer;
            Main.npc = Enumerable.Range(0, npcLength).Select(_ => new NPC()).ToArray();
            Main.projectile = Enumerable.Range(0, projectileLength).Select(_ => new Projectile()).ToArray();
            tierLicense.LoadData(new TagCompound { ["tier3EquipmentLicense"] = true, ["tier4EquipmentLicense"] = true });
            var modSets = new[]
            {
                new[] { ModContent.ItemType<KnightHelmet>(), ModContent.ItemType<KnightChestplate>(), ModContent.ItemType<KnightGreaves>() },
                new[] { ModContent.ItemType<TemplarHelmet>(), ModContent.ItemType<TemplarChestplate>(), ModContent.ItemType<TemplarGreaves>() },
                new[] { ModContent.ItemType<SoulforgedHelmet>(), ModContent.ItemType<SoulforgedChestplate>(), ModContent.ItemType<SoulforgedGreaves>() }
            };
            short[][] vanillaSets =
            {
                new short[] { ItemID.MoltenHelmet, ItemID.MoltenBreastplate, ItemID.MoltenGreaves },
                new short[] { ItemID.HallowedMask, ItemID.HallowedPlateMail, ItemID.HallowedGreaves },
                new short[] { ItemID.BeetleHelmet, ItemID.BeetleShell, ItemID.BeetleLeggings }
            };
            var output = new List<string>();
            MethodInfo fire = typeof(MilkcraftBossNPC).GetMethod("Fire", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("MilkcraftBossNPC.Fire unavailable for actual boss armor-hit cases");
            for (int mode = 0; mode <= 1; mode++)
            {
                SetGameModeAndRefreshOverrides(mode);
                foreach (BossCase test in Cases)
                {
                    int tier = test.Kind is MilkcraftBossKind.WraithWarrior or MilkcraftBossKind.JungleLord or MilkcraftBossKind.Banshee ? 0
                        : test.Kind is MilkcraftBossKind.ObsidianGuardian or MilkcraftBossKind.Sphinx or MilkcraftBossKind.FrostWitch ? 1 : 2;
                    NPC boss = new() { whoAmI = 60 };
                    boss.SetDefaults(test.NpcType);
                    boss.whoAmI = 60;
                    boss.active = true;
                    boss.target = owner;
                    Main.npc[60] = boss;
                    foreach (var armorCase in new[] { (Name: "Milkcraft", Types: modSets[tier]), (Name: "Vanilla", Types: vanillaSets[tier].Select(type => (int)type).ToArray()) })
                    foreach (float scale in new[] { 0.38f, 0.94f })
                    {
                        // Recreate a fully initialized native Player for every hit so armor-set
                        // effects, buffs, endurance, and Hurt cooldowns cannot leak between rows.
                        player = new Player();
                        setupPlayer.Invoke(null, new object[] { player });
                        player.whoAmI = owner;
                        Main.player[owner] = player;
                        player.active = true;
                        player.dead = false;
                        player.GetModPlayer<Tier3T4ProgressPlayer>().LoadData(new TagCompound
                        {
                            ["tier3EquipmentLicense"] = true,
                            ["tier4EquipmentLicense"] = true
                        });
                        int lifeBudget = tier == 2 ? 500 : 400;
                        player.statLifeMax = lifeBudget;
                        player.statLifeMax2 = lifeBudget;
                        player.statLife = lifeBudget;
                        player.endurance = 0f;
                        player.immune = false;
                        player.immuneTime = 0;
                        Array.Clear(player.hurtCooldowns);
                        Array.Clear(player.buffType);
                        Array.Clear(player.buffTime);
                        foreach (Item item in player.armor) item.TurnToAir();
                        foreach (Item item in player.inventory) item.TurnToAir();
                        for (int i = 0; i < 3; i++) player.armor[i].SetDefaults(armorCase.Types[i]);
                        player.ResetEffects();
                        player.UpdateEquips(owner);
                        player.UpdateArmorSets(owner);
                        PlayerLoader.PostUpdateEquips(player);
                        int defense = player.statDefense;
                        player.statLifeMax2 = lifeBudget;
                        player.statLife = lifeBudget;
                        player.immune = false;
                        player.immuneTime = 0;
                        Array.Clear(player.hurtCooldowns);
                        Main.projectile = Enumerable.Range(0, projectileLength).Select(_ => new Projectile()).ToArray();
                        float expectedDamage = ((MilkcraftBossNPC)boss.ModNPC).AttackDamage(scale);
                        fire.Invoke(boss.ModNPC, new object[] { player, player.Center, new Vector2(8f, 0f), test.ProjectileStyle, scale });
                        int slot = Array.FindIndex(Main.projectile, projectile => projectile != null && projectile.active
                            && projectile.type == ModContent.ProjectileType<MilkcraftBossProjectile>()
                            && projectile.ai[0] == (float)test.ProjectileStyle && (int)projectile.ai[1] == boss.whoAmI);
                        if (slot < 0) throw new InvalidOperationException($"{test.Name}/{(mode == 0 ? "normal" : "expert")}/{armorCase.Name}/scale{scale}: actual boss Fire created no projectile");
                        Projectile hit = Main.projectile[slot];
                        if (hit.damage != (int)expectedDamage)
                            throw new InvalidOperationException($"{test.Name}: actual boss Fire damage={hit.damage}, AttackDamage={expectedDamage}");
                        hit.whoAmI = slot;
                        hit.owner = Main.maxPlayers;
                        hit.hostile = true;
                        hit.friendly = false;
                        hit.tileCollide = false;
                        hit.width = 80;
                        hit.height = 80;
                        hit.localAI[0] = 1f;
                        hit.Center = player.Center;
                        BossHurtProbePlayer hurtProbe = player.GetModPlayer<BossHurtProbePlayer>();
                        hurtProbe.HurtCalls = 0;
                        Main.rand = new UnifiedRandom(0x3031 + mode);
                        int before = player.statLife;
                        hit.Damage();
                        int loss = before - player.statLife;
                        if (hurtProbe.HurtCalls != 1 || loss <= 0 || loss >= lifeBudget)
                            throw new InvalidOperationException($"{test.Name}/{(mode == 0 ? "normal" : "expert")}/{armorCase.Name}/scale{scale:0.00}: expected one nonlethal clean Player.Hurt, calls={hurtProbe.HurtCalls}, loss={loss}/{lifeBudget}, raw={hit.damage}, defense={defense}");
                        output.Add($"stage-armor-Hurt={test.Name}/{(mode == 0 ? "normal" : "expert")}/{armorCase.Name}/scale{scale:0.00} mode={Main.GameMode} raw={hit.damage} defense={defense} endurance={player.endurance:0.###} lifeBudget={lifeBudget} hurtCalls={hurtProbe.HurtCalls} finalHurtDamage={hurtProbe.LastHurtDamage} modifiers=({hurtProbe.ModifierSummary}) actualLifeLoss={loss} nonlethal=PASS");
                    }
                }
            }
            return $"same-stage-all-boss-armor-actual-projectile-hurt=PASS rows={output.Count} normal/expert x seven NPCs x normal/heavy projectile x stage armor/vanilla armor\n" + string.Join("\n", output);
        }
        finally
        {
            Main.npc = savedNpcs;
            Main.projectile = savedProjectiles;
            Main.player[owner] = savedPlayer;
            Main.rand = oldRandom;
            SetGameModeAndRefreshOverrides(oldMode);
            tierLicense.LoadData(oldTierLicense);
            BossHurtProbePlayer.Enabled = false;
        }
    }

    private static string RunExtraAiAndClientAuthorityProbe(int owner, Player player, int npcLength, int projectileLength)
    {
        NPC[] savedNpcs = Main.npc;
        Projectile[] savedProjectiles = Main.projectile;
        WorldSections savedSectionManager = Main.sectionManager;
        int strideTileX = Math.Clamp(Main.maxTilesX / 2, 80, Main.maxTilesX - 80);
        int strideTileY = Math.Clamp(Main.maxTilesY / 2, 80, Main.maxTilesY - 12);
        const int strideFloorHalfWidth = 32;
        Tile[] strideFloorBackup = Enumerable.Range(0, strideFloorHalfWidth * 2 + 1)
            .Select(dx => { Tile copy = new(); copy.CopyFrom(Main.tile[strideTileX + dx - strideFloorHalfWidth, strideTileY]); return copy; }).ToArray();
        int oldNetMode = Main.netMode;
        Vector2 oldCenter = player.Center;
        bool oldActive = player.active, oldDead = player.dead;
        MethodInfo updateNpc = typeof(NPC).GetMethod("UpdateNPC", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null, new[] { typeof(int) }, null) ?? throw new InvalidOperationException("NPC.UpdateNPC missing for native client gait probe");
        try
        {
            NPC source = new() { whoAmI = 60 };
            source.SetDefaults(ModContent.NPCType<CastleCoreBoss>());
            source.whoAmI = 60;
            NPC receiver = new() { whoAmI = 61 };
            receiver.SetDefaults(ModContent.NPCType<CastleCoreBoss>());
            receiver.whoAmI = 61;
            MilkcraftBossNPC from = (MilkcraftBossNPC)source.ModNPC;
            MilkcraftBossNPC to = (MilkcraftBossNPC)receiver.ModNPC;
            string[] names = { "_lockedPoint", "_castleAnchor", "_warningTicks", "_nextMove", "_runFrame", "_groundedFlag", "_phase", "_transitionTicks", "_soulCrystalSurvived", "_runProgress", "_castleAnchorSet", "_lowPhaseTransitionStarted", "_lowPhaseTransitionTicks", "_despawnTicks" };
            object[] values = { new Vector2(710.25f, 128.75f), new Vector2(544.5f, 288.5f), 27, 3, 2, 1, 2, 59, 2, 91.25f, true, true, 37, 19 };
            for (int i = 0; i < names.Length; i++)
                typeof(MilkcraftBossNPC).GetField(names[i], BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(from, values[i]);
            byte[] encoded;
            using (MemoryStream stream = new())
            {
                using (BinaryWriter writer = new(stream, System.Text.Encoding.UTF8, leaveOpen: true)) from.SendExtraAI(writer);
                encoded = stream.ToArray();
            }
            using (MemoryStream stream = new(encoded))
            using (BinaryReader reader = new(stream)) to.ReceiveExtraAI(reader);
            byte[] roundTripped;
            using (MemoryStream stream = new())
            {
                using (BinaryWriter writer = new(stream, System.Text.Encoding.UTF8, leaveOpen: true)) to.SendExtraAI(writer);
                roundTripped = stream.ToArray();
            }
            if (!encoded.SequenceEqual(roundTripped))
                throw new InvalidOperationException($"Boss ExtraAI serialize/deserialize mismatch: sent={encoded.Length} received={roundTripped.Length}");

            Main.netMode = NetmodeID.MultiplayerClient;
            player.active = true;
            player.dead = false;
            player.Center = new Vector2(800f, 400f);
            var clientRows = new List<string>();
            int bossClientCases = 0;
            foreach (BossCase test in Cases)
            {
                Main.npc = Enumerable.Range(0, npcLength).Select(_ => new NPC()).ToArray();
                Main.projectile = Enumerable.Range(0, projectileLength).Select(_ => new Projectile()).ToArray();
                NPC parent = new() { whoAmI = 60 };
                parent.SetDefaults(test.NpcType);
                parent.whoAmI = 60;
                parent.active = true;
                parent.life = parent.lifeMax;
                parent.target = owner;
                parent.Center = player.Center + new Vector2(-80f, 0f);
                parent.ai[0] = 2f;
                parent.ai[1] = 0f;
                parent.ai[2] = 0f;
                Main.npc[60] = parent;
                parent.ModNPC.AI();
                int projectileCount = Main.projectile.Count(projectile => projectile != null && projectile.active
                    && projectile.type == ModContent.ProjectileType<MilkcraftBossProjectile>());
                if (projectileCount != 0)
                    throw new InvalidOperationException($"MultiplayerClient {test.Name} attack point created {projectileCount} damage projectiles");
                clientRows.Add($"client-authority-boss={test.Name} attackPresentation=advanced damageProjectiles={projectileCount}");
                bossClientCases++;
            }
            int[] clientStride = { 1, 3, 2, 3 };
            var strideRows = new List<string>();
            // NPC.UpdateNPC returns before AI on multiplayer clients unless all
            // tiles covered by the NPC and its hitbox are in a loaded world section.
            // The smoke fixture has no world sections, so use an isolated manager
            // with the same section dimensions and mark that test world loaded.
            FieldInfo sectionWidthField = typeof(WorldSections).GetField("width", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("WorldSections.width field unavailable for isolated native client gait fixture");
            FieldInfo sectionHeightField = typeof(WorldSections).GetField("height", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("WorldSections.height field unavailable for isolated native client gait fixture");
            int sectionWidth = (int)sectionWidthField.GetValue(savedSectionManager)!;
            int sectionHeight = (int)sectionHeightField.GetValue(savedSectionManager)!;
            Main.sectionManager = new WorldSections(sectionWidth, sectionHeight);
            Main.sectionManager.SetAllSectionsLoaded();
            float strideFloorY = strideTileY * 16f;
            for (int dx = -strideFloorHalfWidth; dx <= strideFloorHalfWidth; dx++)
            {
                Tile floor = Main.tile[strideTileX + dx, strideTileY];
                floor.HasTile = true;
                floor.TileType = TileID.Stone;
                floor.IsActuated = false;
                floor.IsHalfBlock = false;
                floor.Slope = SlopeType.Solid;
            }
            foreach (BossCase test in Cases.Where(test => test.Name is "WraithWarrior" or "JungleLord" or "ObsidianGuardian"))
            {
                Main.npc = Enumerable.Range(0, npcLength).Select(_ => new NPC()).ToArray();
                Main.projectile = Enumerable.Range(0, projectileLength).Select(_ => new Projectile()).ToArray();
                NPC parent = new() { whoAmI = 60 };
                parent.SetDefaults(test.NpcType);
                parent.whoAmI = 60;
                parent.active = true;
                parent.life = parent.lifeMax;
                parent.target = owner;
                parent.Bottom = new Vector2(strideTileX * 16f, strideFloorY);
                player.Center = parent.Center + new Vector2(80f, 0f);
                parent.ai[0] = 0f;
                parent.ai[1] = 17f;
                parent.velocity = new Vector2(4f, 0f);
                Main.npc[60] = parent;
                var seen = new List<int>();
                for (int tick = 1; tick <= 32; tick++)
                {
                    // The networked velocity is the client presentation input. Reapply
                    // that synchronized motion before each native update so vanilla
                    // friction/collision from this isolated no-world fixture cannot
                    // turn a walking actor into a stationary one.
                    parent.velocity = new Vector2(4f, 0f);
                    updateNpc.Invoke(parent, new object[] { parent.whoAmI });
                    // The first transition is reached on tick 8 because the
                    // 32-pixel stride threshold is crossed during that update.
                    // Sample one tick before each transition to assert the poses
                    // actually drawn during each complete phase.
                    if (tick % 8 == 7) seen.Add((int)parent.localAI[0]);
                }
                if (!seen.SequenceEqual(clientStride) || parent.ai[0] != 0f || parent.ai[1] != 17f || Math.Abs(parent.Bottom.Y - strideFloorY) > 1f)
                    throw new InvalidOperationException($"MultiplayerClient {test.Name} NPC.UpdateNPC stride presentation mismatch: frames=[{string.Join(',', seen)}], stage/tick={parent.ai[0]}/{parent.ai[1]}, active={parent.active}, target={parent.target}, vel={parent.velocity}, bottom={parent.Bottom.Y:0.0}/{strideFloorY:0.0}, playerActive={Main.player[owner].active}, playerDead={Main.player[owner].dead}, position={parent.position}");
                strideRows.Add($"native-client-ground-gait={test.Name} NPC.UpdateNPC sampledFrames=[{string.Join(',', seen)}] expected=[1,3,2,3] x=4px/tick floorY={strideFloorY:0.0} stage/tick={parent.ai[0]}/{parent.ai[1]} unchanged");
            }
            var minions = new[]
            {
                (Name: "WraithArcher", Type: ModContent.NPCType<WraithArcherMinion>(), Ranged: true, Melee: false, Explosive: false),
                (Name: "SkeletonWarrior", Type: ModContent.NPCType<SkeletonWarriorMinion>(), Ranged: false, Melee: true, Explosive: false),
                (Name: "SkeletonArcher", Type: ModContent.NPCType<SkeletonArcherMinion>(), Ranged: true, Melee: false, Explosive: false),
                (Name: "ExplosiveSlime", Type: ModContent.NPCType<ExplosiveSlimeMinion>(), Ranged: false, Melee: false, Explosive: true),
                (Name: "FrostNaga", Type: ModContent.NPCType<FrostNagaMinion>(), Ranged: false, Melee: true, Explosive: false),
                (Name: "FrostArcher", Type: ModContent.NPCType<FrostArcherMinion>(), Ranged: true, Melee: false, Explosive: false)
            };
            int minionProjectileCount = 0;
            foreach (var test in minions)
            {
                Main.npc = Enumerable.Range(0, npcLength).Select(_ => new NPC()).ToArray();
                Main.projectile = Enumerable.Range(0, projectileLength).Select(_ => new Projectile()).ToArray();
                NPC parent = new() { whoAmI = 60 };
                parent.SetDefaults(ModContent.NPCType<WraithWarriorBoss>());
                parent.whoAmI = 60;
                parent.active = true;
                parent.life = parent.lifeMax;
                parent.target = owner;
                parent.Center = player.Center + new Vector2(300f, 0f);
                Main.npc[60] = parent;
                NPC minion = new() { whoAmI = 61 };
                minion.SetDefaults(test.Type);
                minion.whoAmI = 61;
                minion.active = true;
                minion.target = owner;
                minion.Center = player.Center;
                minion.GetGlobalNPC<MilkcraftBossEscortGlobalNPC>().ParentBoss = 60;
                if (test.Ranged) minion.ai[0] = 1f;
                if (test.Melee) { minion.ai[1] = 1f; minion.ai[3] = 1f; }
                if (test.Explosive) { minion.ai[0] = 179f; minion.ai[1] = 1f; minion.ai[2] = 1f; }
                Main.npc[61] = minion;
                minion.ModNPC.AI();
                int releasePose = -1, releaseTimer = -1;
                if (test.Ranged)
                {
                    minion.ai[0] = 1f;
                    minion.ai[2] = 0f;
                    minion.localAI[3] = 0f;
                    minion.ModNPC.AI();
                    releasePose = (int)minion.localAI[3];
                    int timerAtRelease = (int)minion.ai[2];
                    minion.ModNPC.AI();
                    releaseTimer = (int)minion.ai[2];
                    if (releasePose != 4 || timerAtRelease != 6 || releaseTimer != 5)
                        throw new InvalidOperationException($"MultiplayerClient {test.Name} release pose mismatch: pose={releasePose}, timer={timerAtRelease}->{releaseTimer}");
                }
                int spawned = Main.projectile.Count(projectile => projectile != null && projectile.active
                    && projectile.type == ModContent.ProjectileType<MilkcraftBossProjectile>());
                minionProjectileCount += spawned;
                if (spawned != 0)
                    throw new InvalidOperationException($"MultiplayerClient {test.Name} release point created {spawned} damage projectiles");
                if (!minion.active)
                    throw new InvalidOperationException($"MultiplayerClient {test.Name} presentation update incorrectly removed a valid parent-linked minion");
                if (test.Ranged) clientRows.Add($"client-release-pose={test.Name} frame={releasePose} visualTimerAfterNextTick={releaseTimer} projectiles={spawned}");
            }
            return $"boss-ExtraAI-byte-roundtrip=PASS bytes={encoded.Length} fields={names.Length}; multiplayer-client-no-authority-spawns=PASS bosses={bossClientCases}/7 minions={minions.Length}/6 damageProjectiles={minionProjectileCount}\n" + string.Join("\n", clientRows.Concat(strideRows));
        }
        finally
        {
            Main.npc = savedNpcs;
            Main.projectile = savedProjectiles;
            Main.sectionManager = savedSectionManager;
            Main.netMode = oldNetMode;
            player.Center = oldCenter;
            player.active = oldActive;
            player.dead = oldDead;
            for (int dx = -strideFloorHalfWidth; dx <= strideFloorHalfWidth; dx++)
                Main.tile[strideTileX + dx, strideTileY].CopyFrom(strideFloorBackup[dx + strideFloorHalfWidth]);
        }
    }

    // Drive each encounter through NPC.UpdateNPC and each spawned projectile through
    // Projectile.Update on an isolated stone floor, so state transitions and tile motion
    // use the same native update path as a real encounter.
    private static string RunCastlePhaseMoveMatrix(int owner, Player player, int npcLength, int projectileLength)
    {
        NPC[] savedNpcs = Main.npc;
        Projectile[] savedProjectiles = Main.projectile;
        int oldLife = player.statLife;
        bool oldImmune = player.immune;
        int oldImmuneTime = player.immuneTime;
        try
        {
            MethodInfo updateNpc = typeof(NPC).GetMethod("UpdateNPC", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null, new[] { typeof(int) }, null) ?? throw new InvalidOperationException("NPC.UpdateNPC(int) unavailable for Castle phase matrix.");
            MethodInfo updateProjectile = typeof(Projectile).GetMethod("Update", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null, new[] { typeof(int) }, null) ?? throw new InvalidOperationException("Projectile.Update(int) unavailable for Castle phase matrix.");
            FieldInfo phaseField = typeof(MilkcraftBossNPC).GetField("_phase", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("Castle _phase field not found.");
            FieldInfo moveField = typeof(MilkcraftBossNPC).GetField("_nextMove", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("Castle _nextMove field not found.");
            FieldInfo anchorField = typeof(MilkcraftBossNPC).GetField("_castleAnchorSet", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("Castle anchor field not found.");
            var rows = new List<string>(48);
            player.statLife = player.statLifeMax2;
            player.immune = true;
            player.immuneTime = 600;
            for (int mode = 0; mode <= 1; mode++)
            {
            SetGameModeAndRefreshOverrides(mode);
            for (int empowered = 0; empowered <= 1; empowered++)
            for (int phase = 0; phase < 3; phase++)
            for (int move = 0; move < 4; move++)
            {
                Main.npc = Enumerable.Range(0, npcLength).Select(_ => new NPC()).ToArray();
                Main.projectile = Enumerable.Range(0, projectileLength).Select(_ => new Projectile()).ToArray();
                NPC boss = new() { whoAmI = 60 };
                boss.SetDefaults(ModContent.NPCType<CastleCoreBoss>());
                boss.whoAmI = 60;
                boss.active = true;
                boss.timeLeft = 600;
                boss.target = owner;
                boss.life = phase switch { 0 => boss.lifeMax * 90 / 100, 1 => boss.lifeMax * 65 / 100, _ => boss.lifeMax * 40 / 100 };
                boss.Center = player.Center + new Vector2(230f, -40f);
                player.Center = new Vector2(Math.Clamp(Main.maxTilesX / 2, 80, Main.maxTilesX - 80) * 16f,
                    Math.Clamp(Main.maxTilesY / 2, 80, Main.maxTilesY - 12) * 16f - 120f);
                boss.Center = player.Center + new Vector2(230f, -40f);
                boss.ai[0] = 0f;
                boss.ai[1] = (phase == 0 ? 180 : phase == 1 ? 166 : 150) - 1;
                boss.ai[2] = 0f;
                boss.ai[3] = empowered;
                phaseField.SetValue(boss.ModNPC, phase);
                moveField.SetValue(boss.ModNPC, move);
                anchorField.SetValue(boss.ModNPC, false);
                Main.npc[60] = boss;
                if (empowered == 1)
                {
                    boss.ai[3] = 0f;
                    int baseDamage = ((MilkcraftBossNPC)boss.ModNPC).AttackDamage(1f);
                    boss.ai[3] = 1f;
                    int empoweredDamage = ((MilkcraftBossNPC)boss.ModNPC).AttackDamage(1f);
                    if (empoweredDamage != (int)(baseDamage * 1.2f))
                        throw new InvalidOperationException($"Castle empowered multiplier mismatch {(mode == 0 ? "normal" : "expert")}/phase{phase}/move{move}: base={baseDamage}, empowered={empoweredDamage}");
                }
                int warningTick = -1, attackTick = -1;
                var observedStyles = new HashSet<int>();
                var observedShots = new HashSet<string>();
                int maxDamage = 0;
                int warningFrames = phase == 0 ? 42 : phase == 1 ? 46 : 40;
                int attackFrames = phase == 0 ? move switch { 0 => 48, 1 => 38, 2 => 54, _ => 48 }
                    : phase == 1 ? move switch { 0 => 42, 1 => 50, 2 => 128, _ => 54 }
                    : move switch { 0 => 42, 1 => 50, 2 => 54, _ => 38 };
                int ticks = warningFrames + attackFrames + 70;
                for (int tick = 0; tick < ticks; tick++)
                {
                    updateNpc.Invoke(boss, new object[] { boss.whoAmI });
                    if ((int)boss.ai[2] == move && (int)boss.ai[0] == 1 && warningTick < 0) warningTick = tick;
                    if ((int)boss.ai[2] == move && (int)boss.ai[0] == 2 && attackTick < 0) attackTick = tick;
                    foreach (Projectile p in Main.projectile)
                        if (p != null && p.active && p.type == ModContent.ProjectileType<MilkcraftBossProjectile>())
                        {
                            observedStyles.Add((int)p.ai[0]);
                            maxDamage = Math.Max(maxDamage, p.damage);
                            observedShots.Add($"style={(int)p.ai[0]} ai1={p.ai[1]} ai2={p.ai[2]} dmg={p.damage}");
                        }
                    for (int n = 0; n < Main.npc.Length; n++)
                        if (n != boss.whoAmI && Main.npc[n] != null && Main.npc[n].active)
                            updateNpc.Invoke(Main.npc[n], new object[] { n });
                    for (int i = 0; i < Main.projectile.Length; i++)
                        if (Main.projectile[i] != null && Main.projectile[i].active)
                            updateProjectile.Invoke(Main.projectile[i], new object[] { i });
                }
                int expectedStyle = phase switch
                {
                    0 => move == 2 ? (int)BossProjectileStyle.CastleGreenRing : (int)BossProjectileStyle.CastleGreen,
                    1 => move == 3 ? (int)BossProjectileStyle.CastleBlueRing : (int)BossProjectileStyle.CastleBlue,
                    _ => move == 2 ? (int)BossProjectileStyle.CastleRedRing : move == 3 ? (int)BossProjectileStyle.CastleRedTracking : (int)BossProjectileStyle.CastleRed
                };
                if (warningTick < 0 || attackTick <= warningTick || !observedStyles.Contains(expectedStyle))
                    throw new InvalidOperationException($"Castle phase{phase}/move{move}: expectedStyle={expectedStyle} missing; warn={warningTick} attack={attackTick} styles={string.Join(',', observedStyles)} shots={string.Join(';', observedShots)} finalStage={boss.ai[0]} tick={boss.ai[1]}");
                rows.Add($"castle-mode={(mode == 0 ? "normal" : "expert")} empowered={empowered == 1} phase={phase} move={move} warnTick={warningTick} attackTick={attackTick} maxRawDamage={maxDamage} nativeProjectileStyles={string.Join(',', observedStyles.OrderBy(v => v))} expectedStyle={expectedStyle} nativeNPC+Projectile.Update=PASS");
            }
            }
            return "castle-normal-expert-empowered-three-phase-twelve-move-native-matrix=PASS rows=" + rows.Count + " (warning before attack; all attacks from native update; empowered +20%)\n" + string.Join("\n", rows);
        }
        finally
        {
            Main.npc = savedNpcs;
            Main.projectile = savedProjectiles;
            player.statLife = oldLife;
            player.immune = oldImmune;
            player.immuneTime = oldImmuneTime;
        }
    }

    private static string RunActualAiCycles(int owner, Player player, int npcLength, int projectileLength)
    {
        NPC[] savedNpcs = Main.npc;
        Projectile[] savedProjectiles = Main.projectile;
        Dust[] savedDust = Main.dust;
        int savedMode = Main.GameMode;
        int savedNetMode = Main.netMode;
        int savedMyPlayer = Main.myPlayer;
        UnifiedRandom savedRandom = Main.rand;
        bool savedActive = player.active, savedDead = player.dead;
        Vector2 savedCenter = player.Center;
        bool savedDay = Main.dayTime;
        bool savedWorldGen = WorldGen.gen, savedGeneratingWorld = WorldGen.generatingWorld;
        bool savedNoTileActions = WorldGen.noTileActions, savedDestroyObject = WorldGen.destroyObject;
        int tileX = Math.Clamp(Main.maxTilesX / 2, 80, Main.maxTilesX - 80);
        int tileY = Math.Clamp(Main.maxTilesY / 2, 80, Main.maxTilesY - 12);
        const int halfFloor = 48;
        Tile[] floorBackup = Enumerable.Range(0, halfFloor * 2 + 1)
            .Select(dx => { Tile copy = new(); copy.CopyFrom(Main.tile[tileX + dx - halfFloor, tileY]); return copy; }).ToArray();
        var traces = new string[Cases.Length];
        try
        {
            SetGameModeAndRefreshOverrides(0);
            Main.netMode = NetmodeID.SinglePlayer;
            Main.myPlayer = owner;
            Main.dayTime = false;
            Main.rand = new UnifiedRandom(0x303012);
            WorldGen.gen = false;
            WorldGen.generatingWorld = false;
            WorldGen.noTileActions = false;
            WorldGen.destroyObject = false;
            for (int dx = -halfFloor; dx <= halfFloor; dx++)
            {
                Tile floor = Main.tile[tileX + dx, tileY];
                floor.HasTile = true;
                floor.TileType = TileID.Stone;
                floor.IsActuated = false;
                floor.IsHalfBlock = false;
                floor.Slope = SlopeType.Solid;
            }
            MethodInfo updateNpc = typeof(NPC).GetMethod("UpdateNPC", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null, new[] { typeof(int) }, null) ?? throw new InvalidOperationException("NPC.UpdateNPC(int) unavailable for boss physical-cycle test.");
            MethodInfo updateProjectile = typeof(Projectile).GetMethod("Update", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null, new[] { typeof(int) }, null) ?? throw new InvalidOperationException("Projectile.Update(int) unavailable for boss physical-cycle test.");
            player.active = true;
            player.dead = false;
            Main.npc = Enumerable.Range(0, npcLength).Select(_ => new NPC()).ToArray();
            Main.projectile = Enumerable.Range(0, projectileLength).Select(_ => new Projectile()).ToArray();
            // Native arrays include a final sentinel slot (maxDust is one smaller).
            Main.dust = Enumerable.Range(0, Math.Max(Main.maxDust + 1, savedDust.Length)).Select(_ => new Dust()).ToArray();

            for (int i = 0; i < Cases.Length; i++)
            {
                BossCase test = Cases[i];
                Main.projectile = Enumerable.Range(0, projectileLength).Select(_ => new Projectile()).ToArray();
                Main.dust = Enumerable.Range(0, Math.Max(Main.maxDust + 1, savedDust.Length)).Select(_ => new Dust()).ToArray();
                NPC npc = new() { whoAmI = 60 };
                npc.SetDefaults(test.NpcType);
                npc.whoAmI = 60;
                npc.active = true;
                npc.timeLeft = 600;
                npc.target = owner;
                npc.Center = new Vector2(tileX * 16f, tileY * 16f - npc.height * 0.5f - 80f);
                player.Center = npc.Center + new Vector2(230f, 0f);
                player.velocity = Vector2.Zero;
                Main.npc[npc.whoAmI] = npc;
                int period = test.Kind switch
                {
                    MilkcraftBossKind.WraithWarrior => 180,
                    MilkcraftBossKind.JungleLord => 210,
                    MilkcraftBossKind.Banshee => 156,
                    MilkcraftBossKind.ObsidianGuardian => 190,
                    MilkcraftBossKind.Sphinx => 170,
                    MilkcraftBossKind.FrostWitch => 144,
                    // CastleCore starts its first ordinary cycle at 180 ticks, then
                    // gives its own 45–60 tick warning before the first hit event.
                    _ => 280
                };
                int firstFireTick = -1;
                int firstTelegraphWindowTick = -1;
                int spawned = 0;
                int everSpawned = 0;
                for (int tick = 0; tick <= period; tick++)
                {
                    updateNpc.Invoke(npc, new object[] { npc.whoAmI });
                    int nowSpawned = Main.projectile.Count(p => p != null && p.active && p.type == ModContent.ProjectileType<MilkcraftBossProjectile>());
                    if (nowSpawned > spawned)
                    {
                        if (firstFireTick < 0) firstFireTick = tick;
                        everSpawned += nowSpawned - spawned;
                    }
                    spawned = nowSpawned;
                    for (int projectileIndex = 0; projectileIndex < Main.projectile.Length; projectileIndex++)
                        if (Main.projectile[projectileIndex] != null && Main.projectile[projectileIndex].active)
                            updateProjectile.Invoke(Main.projectile[projectileIndex], new object[] { projectileIndex });
                    if (firstTelegraphWindowTick < 0 && (int)npc.ai[0] == 1)
                        firstTelegraphWindowTick = tick;
                }
                if (firstFireTick < 0 || everSpawned == 0)
                    throw new InvalidOperationException($"{test.Name}: NPC.UpdateNPC produced no projectile within period={period}; telegraphWindowTick={firstTelegraphWindowTick}, " +
                        $"stage={npc.ai[0]} tick={npc.ai[1]} move={npc.ai[2]} active={npc.active} type={npc.type}/{test.NpcType} " +
                        $"pos={npc.Center} velocity={npc.velocity} net={Main.netMode} updateNPC={npc.ModNPC?.GetType().Name}");
                if (firstTelegraphWindowTick < 0 || firstTelegraphWindowTick >= firstFireTick)
                    throw new InvalidOperationException($"{test.Name}: telegraph interval was not advanced before first attack (interval={firstTelegraphWindowTick}, fire={firstFireTick})");
                traces[i] = $"ai-cycle={test.Name} nativeNPCUpdateTicks={period + 1} telegraphWindowStart={firstTelegraphWindowTick} firstProjectileTick={firstFireTick} everSpawned={everSpawned} liveAtEnd={spawned} tileGroundY={tileY * 16f} native-update-and-projectile-update=PASS";
            }
            string castleMoves = RunCastlePhaseMoveMatrix(owner, player, npcLength, projectileLength);
            string sixBossMoves = RunSixBossMovePhaseMatrix(owner, player, npcLength, projectileLength);
            string castlePull = RunCastleWeakPullHit(owner, player, npcLength, projectileLength);
            string soulLife = RunCastleSoulCrystalLifetime(owner, player, npcLength);
            string naturalCycles = RunNaturalCyclesAndTransitions(owner, player, npcLength, projectileLength);
            string contactGate = RunBossContactGates(owner, player, npcLength);
            string minionLoops = RunMinionCombatLoops(owner, player, npcLength, projectileLength);
            string capAndGeometry = RunMinionCapsAndAttackGeometry(owner, player, npcLength, projectileLength);
            string explosiveSlime = RunExplosiveSlimeLifetimeAndDistance(owner, player, npcLength, projectileLength);
            string noTargetCleanup = RunNoTargetHazardCleanup(owner, player, npcLength, projectileLength);
            string groundWaves = RunGroundWavePhysicalProbe(owner, player, npcLength, projectileLength, updateProjectile, tileX, tileY);
            return "seven-boss-native-NPC-and-projectile-cycles=PASS (real NPC.UpdateNPC + Projectile.Update on an isolated stone floor)\n" + string.Join("\n", traces) + "\n" + castleMoves + "\n" + sixBossMoves + "\n" + castlePull + "\n" + soulLife + "\n" + naturalCycles + "\n" + contactGate + "\n" + minionLoops + "\n" + capAndGeometry + "\n" + explosiveSlime + "\n" + noTargetCleanup + "\n" + groundWaves;
        }
        finally
        {
            Main.npc = savedNpcs;
            Main.projectile = savedProjectiles;
            Main.dust = savedDust;
            SetGameModeAndRefreshOverrides(savedMode);
            Main.netMode = savedNetMode;
            Main.myPlayer = savedMyPlayer;
            Main.rand = savedRandom;
            Main.dayTime = savedDay;
            WorldGen.gen = savedWorldGen;
            WorldGen.generatingWorld = savedGeneratingWorld;
            WorldGen.noTileActions = savedNoTileActions;
            WorldGen.destroyObject = savedDestroyObject;
            for (int dx = -halfFloor; dx <= halfFloor; dx++)
                Main.tile[tileX + dx, tileY].CopyFrom(floorBackup[dx + halfFloor]);
            player.active = savedActive;
            player.dead = savedDead;
            player.Center = savedCenter;
        }
    }

    private static string RunGroundWavePhysicalProbe(int owner, Player player, int npcLength, int projectileLength,
        MethodInfo updateProjectile, int tileX, int tileY)
    {
        Main.npc = Enumerable.Range(0, npcLength).Select(_ => new NPC()).ToArray();
        Main.projectile = Enumerable.Range(0, projectileLength).Select(_ => new Projectile()).ToArray();
        float floorY = tileY * 16f;
        var cases = new[]
        {
            (Name: "JungleRoot", NpcType: ModContent.NPCType<JungleLordBoss>(), Style: BossProjectileStyle.VineRoot, Width: 20, Height: 64, Speed: 8f),
            (Name: "JungleLandingWave", NpcType: ModContent.NPCType<JungleLordBoss>(), Style: BossProjectileStyle.VineRoot, Width: 20, Height: 64, Speed: 6.5f),
            (Name: "GuardianLandingWave", NpcType: ModContent.NPCType<ObsidianGuardianBoss>(), Style: BossProjectileStyle.ObsidianFlame, Width: 30, Height: 40, Speed: 7f)
        };
        var results = new List<string>();
        MethodInfo fireWave = typeof(MilkcraftBossNPC).GetMethod("FireGroundWave", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("MilkcraftBossNPC.FireGroundWave unavailable for physical floor probe");
        foreach (var test in cases)
        {
            Main.projectile = Enumerable.Range(0, projectileLength).Select(_ => new Projectile()).ToArray();
            NPC boss = new() { whoAmI = 60 };
            boss.SetDefaults(test.NpcType);
            boss.whoAmI = 60;
            boss.active = true;
            boss.target = owner;
            boss.Bottom = new Vector2(tileX * 16f, floorY);
            Main.npc[60] = boss;
            Vector2 expectedCenter = new(tileX * 16f + 30f, floorY - test.Height / 2f - 1f);
            Vector2 velocity = new(test.Speed, 0f);
            fireWave.Invoke(boss.ModNPC, new object[] { player, expectedCenter, velocity, test.Style, 0.5f });
            int slot = Array.FindIndex(Main.projectile, projectile => projectile != null && projectile.active
                && projectile.type == ModContent.ProjectileType<MilkcraftBossProjectile>()
                && projectile.ai[0] == (float)test.Style && (int)projectile.ai[1] == boss.whoAmI);
            if (slot < 0) throw new InvalidOperationException($"{test.Name}: native ground wave did not spawn");
            Projectile wave = Main.projectile[slot];
            if (wave.width != test.Width || wave.height != test.Height || Math.Abs(wave.Hitbox.Bottom - floorY + 1f) > 1.1f)
                throw new InvalidOperationException($"{test.Name}: initial ground geometry={wave.Hitbox} floorY={floorY}, expected bottom={floorY - 1}");
            float x0 = wave.Center.X;
            for (int tick = 0; tick < 4 && wave.active; tick++)
                updateProjectile.Invoke(wave, new object[] { slot });
            float distance = wave.Center.X - x0;
            if (!wave.active || distance < test.Speed * 2.5f || wave.Hitbox.Bottom > floorY + 0.1f)
                throw new InvalidOperationException($"{test.Name}: wave was consumed/embedded by floor: active={wave.active}, xDelta={distance:0.0}, bottom={wave.Hitbox.Bottom:0.0}, floor={floorY:0.0}");
            results.Add($"ground-wave={test.Name} style={test.Style} size={wave.width}x{wave.height} floorY={floorY:0.0} spawnedBottom={floorY - 1:0.0} native4TickDelta={distance:0.0} survived-and-travelled=PASS");
        }
        return "native-ground-wave-floor-clearance=PASS real Projectile.Update four ticks\n" + string.Join("\n", results);
    }

    private static string RunNaturalCyclesAndTransitions(int owner, Player player, int npcLength, int projectileLength)
    {
        NPC[] oldNpcs = Main.npc;
        Projectile[] oldProjectiles = Main.projectile;
        Dust[] oldDust = Main.dust;
        Vector2 oldCenter = player.Center;
        bool oldImmune = player.immune, oldActive = player.active, oldDead = player.dead;
        int oldImmuneTime = player.immuneTime, oldMode = Main.GameMode;
        int tx = Math.Clamp(Main.maxTilesX / 2, 100, Main.maxTilesX - 100);
        int ty = Math.Clamp(Main.maxTilesY / 2, 100, Main.maxTilesY - 30);
        const int radius = 90;
        Tile[] backup = Enumerable.Range(-radius, radius * 2 + 1).Select(dx =>
        {
            Tile copy = new(); copy.CopyFrom(Main.tile[tx + dx, ty]); return copy;
        }).ToArray();
        MethodInfo updateNpc = typeof(NPC).GetMethod("UpdateNPC", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null, new[] { typeof(int) }, null) ?? throw new InvalidOperationException("NPC.UpdateNPC unavailable for natural cycle test");
        MethodInfo updateProjectile = typeof(Projectile).GetMethod("Update", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null, new[] { typeof(int) }, null) ?? throw new InvalidOperationException("Projectile.Update unavailable for natural cycle test");
        FieldInfo lowTicks = typeof(MilkcraftBossNPC).GetField("_lowPhaseTransitionTicks", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Low phase timer field missing");
        FieldInfo castlePhase = typeof(MilkcraftBossNPC).GetField("_phase", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Castle phase field missing");
        FieldInfo castleTicks = typeof(MilkcraftBossNPC).GetField("_transitionTicks", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Castle phase timer field missing");
        var rows = new List<string>(Cases.Length);
        try
        {
            Main.netMode = NetmodeID.SinglePlayer;
            SetGameModeAndRefreshOverrides(0);
            Main.myPlayer = owner;
            Main.dayTime = false;
            player.active = true; player.dead = false; player.immune = true; player.immuneTime = 600;
            Main.dust = Enumerable.Range(0, Math.Max(Main.maxDust + 1, oldDust.Length)).Select(_ => new Dust()).ToArray();
            for (int x = -radius; x <= radius; x++)
            {
                Tile floor = Main.tile[tx + x, ty];
                floor.HasTile = true; floor.TileType = TileID.Stone; floor.IsActuated = false;
                floor.IsHalfBlock = false; floor.Slope = SlopeType.Solid;
            }

            foreach (BossCase test in Cases)
            {
                Main.npc = Enumerable.Range(0, npcLength).Select(_ => new NPC()).ToArray();
                Main.projectile = Enumerable.Range(0, projectileLength).Select(_ => new Projectile()).ToArray();
                SetGameModeAndRefreshOverrides(0);
                NPC boss = new() { whoAmI = 60 };
                boss.SetDefaults(test.NpcType); boss.whoAmI = 60; boss.active = true;
                boss.timeLeft = 4000; boss.target = owner; boss.life = boss.lifeMax * 90 / 100;
                boss.Center = new Vector2((tx - 5) * 16f, ty * 16f - boss.height * 0.5f);
                player.Center = new Vector2((tx + 7) * 16f, ty * 16f - player.height * 0.5f);
                player.velocity = Vector2.Zero;
                Main.npc[60] = boss;
                int moveCount = test.Kind is MilkcraftBossKind.ObsidianGuardian or MilkcraftBossKind.FrostWitch ? 5 : 4;
                int[][] moveVisits = Enumerable.Range(0, 3).Select(_ => new int[moveCount]).ToArray();
                int attackStarts = 0, postTransitionAttacks = 0, warningStarts = 0, elapsedTicks = 0;
                int transitionAt = -1, transitionCompleteAt = -1, transitionFrames = 0, castlePhaseOneAt = -1, castlePhaseTwoAt = -1;
                int previousStage = -1, previousMove = -1;
                bool thresholdApplied = false, castleFirstThresholdApplied = false, castleSecondThresholdApplied = false;
                int minionPeak = 0, projectilePeak = 0;
                int totalTicks = 8000;
                for (int tick = 0; tick < totalTicks; tick++)
                {
                    if (test.Kind == MilkcraftBossKind.CastleCore)
                    {
                        int phase = (int)castlePhase.GetValue(boss.ModNPC);
                        int transition = (int)castleTicks.GetValue(boss.ModNPC);
                        if (!castleFirstThresholdApplied && phase == 0 && moveVisits[0].All(count => count >= 1))
                        {
                            boss.life = boss.lifeMax * 65 / 100;
                            castleFirstThresholdApplied = true;
                        }
                        if (phase == 1 && transition <= 0 && !castleSecondThresholdApplied && moveVisits[1].All(count => count >= 1))
                        {
                            castlePhaseOneAt = tick;
                            boss.life = boss.lifeMax * 40 / 100;
                            castleSecondThresholdApplied = true;
                        }
                        if (phase == 2 && transition <= 0 && moveVisits[2].All(count => count >= 1) && castlePhaseTwoAt < 0)
                            castlePhaseTwoAt = tick;
                    }
                    else if (!thresholdApplied && moveVisits[0].All(count => count >= 2))
                    {
                        boss.life = boss.lifeMax * 42 / 100;
                        thresholdApplied = true;
                    }

                    updateNpc.Invoke(boss, new object[] { boss.whoAmI });
                    elapsedTicks = tick + 1;
                    int stageNow = (int)boss.ai[0];
                    int moveNow = (int)boss.ai[2];
                    if (stageNow == 1 && (previousStage != 1 || previousMove != moveNow)) warningStarts++;
                    if (stageNow == 2 && previousStage != 2)
                    {
                        attackStarts++;
                        int phaseIndex = test.Kind == MilkcraftBossKind.CastleCore
                            ? Math.Clamp((int)castlePhase.GetValue(boss.ModNPC), 0, 2)
                            : thresholdApplied && transitionCompleteAt >= 0 ? 1 : 0;
                        if ((uint)moveNow < (uint)moveCount)
                            moveVisits[phaseIndex][moveNow]++;
                        if (phaseIndex > 0)
                            postTransitionAttacks++;
                    }
                    previousStage = stageNow; previousMove = moveNow;

                    if (test.Kind != MilkcraftBossKind.CastleCore && transitionAt < 0 && (int)lowTicks.GetValue(boss.ModNPC) > 0)
                        transitionAt = tick;
                    if (test.Kind != MilkcraftBossKind.CastleCore && transitionAt >= 0 && transitionCompleteAt < 0
                        && (int)lowTicks.GetValue(boss.ModNPC) > 0)
                        transitionFrames++;
                    if (test.Kind != MilkcraftBossKind.CastleCore && transitionAt >= 0 && transitionCompleteAt < 0
                        && (int)lowTicks.GetValue(boss.ModNPC) == 0 && tick >= transitionAt + 60)
                        transitionCompleteAt = tick;
                    else if (test.Kind != MilkcraftBossKind.CastleCore && transitionAt >= 0 && transitionCompleteAt < 0
                        && (int)lowTicks.GetValue(boss.ModNPC) == 0 && transitionFrames >= 59)
                        transitionCompleteAt = tick;

                    int activeProjectiles = 0, activeMinions = 0;
                    for (int n = 0; n < Main.npc.Length; n++)
                    {
                        NPC child = Main.npc[n];
                        if (n != boss.whoAmI && child != null && child.active && child.ModNPC is MilkcraftBossMinion)
                        {
                            activeMinions++;
                            updateNpc.Invoke(child, new object[] { n });
                        }
                    }
                    minionPeak = Math.Max(minionPeak, activeMinions);
                    for (int i = 0; i < Main.projectile.Length; i++)
                    {
                        Projectile projectile = Main.projectile[i];
                        if (projectile == null || !projectile.active) continue;
                        if (projectile.type == ModContent.ProjectileType<MilkcraftBossProjectile>()
                            && (int)projectile.ai[1] == boss.whoAmI) activeProjectiles++;
                        updateProjectile.Invoke(projectile, new object[] { i });
                    }
                    projectilePeak = Math.Max(projectilePeak, activeProjectiles);

                    if (test.Kind == MilkcraftBossKind.CastleCore && castleSecondThresholdApplied
                        && castlePhaseTwoAt >= 0)
                        break;
                    if (test.Kind != MilkcraftBossKind.CastleCore && thresholdApplied
                        && transitionCompleteAt >= 0 && moveVisits[1].All(count => count >= 2))
                        break;
                }

                bool pass = test.Kind == MilkcraftBossKind.CastleCore
                    ? castleFirstThresholdApplied && castleSecondThresholdApplied && moveVisits[0].All(count => count >= 1)
                        && moveVisits[1].All(count => count >= 1) && moveVisits[2].All(count => count >= 1)
                        && warningStarts >= moveCount * 3 && castlePhaseOneAt >= 0 && castlePhaseTwoAt >= castlePhaseOneAt
                    : thresholdApplied && moveVisits[0].All(count => count >= 2) && moveVisits[1].All(count => count >= 2)
                        && warningStarts >= moveCount * 4 && transitionAt >= 0 && transitionFrames >= 59
                        && transitionCompleteAt >= transitionAt + 59 && postTransitionAttacks >= moveCount * 2;
                if (!pass)
                    throw new InvalidOperationException($"{test.Name} natural full-cycle/low-phase sequence failed: elapsed={elapsedTicks}/{totalTicks} moves={string.Join('/', moveVisits.Select(visits => string.Join(',', visits)))} attacks={attackStarts} warnings={warningStarts} transition={transitionAt}->{transitionCompleteAt}/{transitionFrames} castlePhases={castlePhaseOneAt}/{castlePhaseTwoAt} postTransition={postTransitionAttacks} stage={boss.ai[0]} move={boss.ai[2]} projectiles={projectilePeak} minions={minionPeak}");

                // Native parent invalidation must remove its support NPCs and hazards on the following updates.
                boss.active = false;
                for (int n = 0; n < Main.npc.Length; n++)
                    if (n != boss.whoAmI && Main.npc[n] != null && Main.npc[n].active && Main.npc[n].ModNPC is MilkcraftBossMinion)
                        updateNpc.Invoke(Main.npc[n], new object[] { n });
                for (int i = 0; i < Main.projectile.Length; i++)
                    if (Main.projectile[i] != null && Main.projectile[i].active && Main.projectile[i].type == ModContent.ProjectileType<MilkcraftBossProjectile>())
                        updateProjectile.Invoke(Main.projectile[i], new object[] { i });
                int childrenAfterParentLoss = Main.npc.Count(n => n != null && n.active && n.ModNPC is MilkcraftBossMinion);
                int shotsAfterParentLoss = Main.projectile.Count(p => p != null && p.active && p.type == ModContent.ProjectileType<MilkcraftBossProjectile>());
                if (childrenAfterParentLoss != 0 || shotsAfterParentLoss != 0)
                    throw new InvalidOperationException($"{test.Name} parent loss cleanup failed: minions={childrenAfterParentLoss} shots={shotsAfterParentLoss}");
                rows.Add($"natural-cycle={test.Name} elapsedTicks={elapsedTicks}/{totalTicks} moveVisits={string.Join('/', moveVisits.Select(visits => string.Join(',', visits)))} attacks={attackStarts} warnings={warningStarts} lowTransition={transitionAt}->{transitionCompleteAt}/{transitionFrames} castlePhases={castlePhaseOneAt}/{castlePhaseTwoAt} postTransitionAttacks={postTransitionAttacks} peakMinions={minionPeak} peakOwnedProjectiles={projectilePeak} parent-loss-cleanup=PASS");
            }
            return "seven-boss-natural-cycles-and-transitions=PASS real consecutive attacks, native phase changes, escort/projectile cleanup\n" + string.Join("\n", rows);
        }
        finally
        {
            Main.npc = oldNpcs; Main.projectile = oldProjectiles; Main.dust = oldDust; SetGameModeAndRefreshOverrides(oldMode);
            player.Center = oldCenter; player.immune = oldImmune; player.immuneTime = oldImmuneTime; player.active = oldActive; player.dead = oldDead;
            for (int x = -radius; x <= radius; x++) Main.tile[tx + x, ty].CopyFrom(backup[x + radius]);
        }
    }

    private static string RunBossContactGates(int owner, Player player, int npcLength)
    {
        NPC[] oldNpcs = Main.npc;
        Vector2 oldCenter = player.Center;
        var rows = new List<string>();
        try
        {
            Main.npc = Enumerable.Range(0, npcLength).Select(_ => new NPC()).ToArray();
            player.active = true; player.dead = false; player.Center = new Vector2(1600f, 1300f);
            foreach (BossCase test in Cases)
            {
                NPC boss = new() { whoAmI = 60 };
                boss.SetDefaults(test.NpcType); boss.whoAmI = 60; boss.active = true; boss.target = owner;
                Main.npc[60] = boss;
                int cooldown = ImmunityCooldownID.Bosses;
                bool stage0 = boss.ModNPC is MilkcraftBossNPC m0 && m0.CanHitPlayer(player, ref cooldown);
                boss.ai[0] = 1f; cooldown = ImmunityCooldownID.Bosses;
                bool warning = boss.ModNPC is MilkcraftBossNPC m1 && m1.CanHitPlayer(player, ref cooldown);
                boss.ai[0] = 3f; cooldown = ImmunityCooldownID.Bosses;
                bool recovery = boss.ModNPC is MilkcraftBossNPC m2 && m2.CanHitPlayer(player, ref cooldown);
                boss.ai[0] = 2f; boss.ai[1] = 8f; boss.ai[2] = 0f; cooldown = ImmunityCooldownID.Bosses;
                bool attack = boss.ModNPC is MilkcraftBossNPC m3 && m3.CanHitPlayer(player, ref cooldown);
                bool floating = test.Kind is MilkcraftBossKind.Banshee or MilkcraftBossKind.Sphinx or MilkcraftBossKind.FrostWitch;
                bool expectedNormal = !floating && test.Kind != MilkcraftBossKind.CastleCore;
                bool expectedAttack = expectedNormal || test.Kind == MilkcraftBossKind.Sphinx;
                if (stage0 != expectedNormal || warning || recovery || attack != expectedAttack)
                    throw new InvalidOperationException($"{test.Name} contact gate mismatch: stage0={stage0}/{expectedNormal} warning={warning} recovery={recovery} attack={attack}/{expectedAttack}");
                if (floating && test.Kind != MilkcraftBossKind.Sphinx)
                {
                    boss.ai[2] = test.Kind == MilkcraftBossKind.Banshee ? 3f : 4f;
                    boss.ai[1] = test.Kind == MilkcraftBossKind.Banshee ? 12f : 10f;
                    cooldown = ImmunityCooldownID.Bosses;
                    bool dashContact = boss.ModNPC is MilkcraftBossNPC dash && dash.CanHitPlayer(player, ref cooldown);
                    if (!dashContact)
                        throw new InvalidOperationException($"{test.Name} intended dash contact window was rejected");
                    rows.Add($"contact-gate={test.Name} approach={stage0} telegraph={warning} recovery={recovery} attack-window={attack} dash-window={dashContact} PASS");
                }
                else rows.Add($"contact-gate={test.Name} approach={stage0} telegraph={warning} recovery={recovery} attack-window={attack} PASS");
            }
            return "boss-contact-damage-gates=PASS phase windows reject touch; floating pursuits only attack-window contact; Castle never touches\n" + string.Join("\n", rows);
        }
        finally { Main.npc = oldNpcs; player.Center = oldCenter; }
    }

    private static string RunMinionCombatLoops(int owner, Player player, int npcLength, int projectileLength)
    {
        NPC[] oldNpcs = Main.npc;
        Projectile[] oldProjectiles = Main.projectile;
        Dust[] oldDust = Main.dust;
        Vector2 oldCenter = player.Center;
        bool oldActive = player.active, oldDead = player.dead;
        int tx = Math.Clamp(Main.maxTilesX / 2, 100, Main.maxTilesX - 100);
        int ty = Math.Clamp(Main.maxTilesY / 2, 100, Main.maxTilesY - 30);
        const int floorRadius = 30;
        Tile[] floorBackup = Enumerable.Range(-floorRadius, floorRadius * 2 + 1).Select(dx =>
        {
            Tile copy = new(); copy.CopyFrom(Main.tile[tx + dx, ty]); return copy;
        }).ToArray();
        MethodInfo updateNpc = typeof(NPC).GetMethod("UpdateNPC", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null, new[] { typeof(int) }, null) ?? throw new InvalidOperationException("NPC.UpdateNPC unavailable for minion combat loop");
        MethodInfo updateProjectile = typeof(Projectile).GetMethod("Update", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null, new[] { typeof(int) }, null) ?? throw new InvalidOperationException("Projectile.Update unavailable for minion combat loop");
        (string name, int parentType, int minionType, int expectedStyle, bool expectedDeath)[] minions =
        {
            ("WraithArcher", ModContent.NPCType<WraithWarriorBoss>(), ModContent.NPCType<WraithArcherMinion>(), (int)BossProjectileStyle.WraithArrow, false),
            ("SkeletonWarrior", ModContent.NPCType<BansheeBoss>(), ModContent.NPCType<SkeletonWarriorMinion>(), (int)BossProjectileStyle.MinionSweep, false),
            ("SkeletonArcher", ModContent.NPCType<BansheeBoss>(), ModContent.NPCType<SkeletonArcherMinion>(), (int)BossProjectileStyle.BansheeSkull, false),
            ("ExplosiveSlime", ModContent.NPCType<ObsidianGuardianBoss>(), ModContent.NPCType<ExplosiveSlimeMinion>(), (int)BossProjectileStyle.ObsidianFlame, true),
            ("FrostNaga", ModContent.NPCType<FrostWitchBoss>(), ModContent.NPCType<FrostNagaMinion>(), (int)BossProjectileStyle.MinionSweep, false),
            ("FrostArcher", ModContent.NPCType<FrostWitchBoss>(), ModContent.NPCType<FrostArcherMinion>(), (int)BossProjectileStyle.FrostSpear, false)
        };
        var rows = new List<string>(minions.Length);
        try
        {
            Main.netMode = NetmodeID.SinglePlayer;
            Main.myPlayer = owner;
            SetGameModeAndRefreshOverrides(0);
            Main.dust = Enumerable.Range(0, Math.Max(Main.maxDust + 1, oldDust.Length)).Select(_ => new Dust()).ToArray();
            player.active = true; player.dead = false;
            for (int x = -floorRadius; x <= floorRadius; x++)
            {
                Tile tile = Main.tile[tx + x, ty];
                tile.HasTile = true; tile.TileType = TileID.Stone; tile.IsActuated = false;
                tile.IsHalfBlock = false; tile.Slope = SlopeType.Solid;
            }

            foreach (var test in minions)
            {
                Main.npc = Enumerable.Range(0, npcLength).Select(_ => new NPC()).ToArray();
                Main.projectile = Enumerable.Range(0, projectileLength).Select(_ => new Projectile()).ToArray();
                NPC parent = new() { whoAmI = 60 };
                parent.SetDefaults(test.parentType); parent.whoAmI = 60; parent.active = true; parent.life = parent.lifeMax;
                parent.timeLeft = 4000; parent.target = owner;
                parent.Center = new Vector2(tx * 16f, ty * 16f - parent.height * 0.5f);
                Main.npc[60] = parent;
                float offset = test.name is "WraithArcher" or "SkeletonArcher" or "FrostArcher" ? -190f : -62f;
                int minionIndex = NPC.NewNPC(new EntitySource_Misc("boss minion native loop"),
                    (int)(parent.Center.X + offset), (int)(ty * 16f - 50f), test.minionType);
                if (minionIndex < 0 || minionIndex >= Main.maxNPCs)
                    throw new InvalidOperationException($"{test.name} native NPC.NewNPC failed");
                NPC minion = Main.npc[minionIndex];
                minion.target = owner; minion.timeLeft = 4000;
                if (minion.noGravity)
                    minion.Center = parent.Center + new Vector2(offset, -75f);
                else
                    minion.Bottom = new Vector2(parent.Center.X + offset, ty * 16f);
                minion.GetGlobalNPC<MilkcraftBossEscortGlobalNPC>().ParentBoss = parent.whoAmI;
                minion.netUpdate = true;
                player.Center = new Vector2(parent.Center.X + 240f, ty * 16f - player.height * 0.5f);
                player.velocity = Vector2.Zero;
                var seenStyles = new HashSet<int>();
                int parentBudgetPeak = 0;
                for (int tick = 0; tick < 260 && minion.active; tick++)
                {
                    updateNpc.Invoke(minion, new object[] { minionIndex });
                    int countNow = 0;
                    for (int i = 0; i < Main.projectile.Length; i++)
                    {
                        Projectile p = Main.projectile[i];
                        if (p == null || !p.active || p.type != ModContent.ProjectileType<MilkcraftBossProjectile>()) continue;
                        if ((int)p.ai[1] == parent.whoAmI)
                        {
                            if ((int)p.ai[2] == minionIndex)
                            {
                                countNow++;
                                seenStyles.Add((int)p.ai[0]);
                            }
                        }
                        updateProjectile.Invoke(p, new object[] { i });
                    }
                    parentBudgetPeak = Math.Max(parentBudgetPeak, MilkcraftBossProjectileBudget.CountForBoss(parent.whoAmI));
                }
                if (!seenStyles.Contains(test.expectedStyle))
                    throw new InvalidOperationException($"{test.name} native attack cycle did not spawn expected style {test.expectedStyle}; observed={string.Join(',', seenStyles)} ai={string.Join(',', minion.ai)} active={minion.active} pos={minion.Center}");
                if (parentBudgetPeak > 40)
                    throw new InvalidOperationException($"{test.name} exceeded shared boss projectile budget: peak={parentBudgetPeak}");
                if (test.expectedDeath && minion.active)
                    throw new InvalidOperationException("ExplosiveSlime did not detonate/expire through native NPC updates");
                parent.active = false;
                if (minion.active) updateNpc.Invoke(minion, new object[] { minionIndex });
                for (int i = 0; i < Main.projectile.Length; i++)
                    if (Main.projectile[i] != null && Main.projectile[i].active && Main.projectile[i].type == ModContent.ProjectileType<MilkcraftBossProjectile>())
                        updateProjectile.Invoke(Main.projectile[i], new object[] { i });
                int survivors = Main.npc.Count(n => n != null && n.active && n.ModNPC is MilkcraftBossMinion);
                int ownedShots = Main.projectile.Count(p => p != null && p.active && p.type == ModContent.ProjectileType<MilkcraftBossProjectile>());
                if (survivors != 0 || ownedShots != 0)
                    throw new InvalidOperationException($"{test.name} parent invalidation left survivors minion={survivors} projectiles={ownedShots}");
                rows.Add($"minion-native-loop={test.name} expectedStyle={test.expectedStyle} observed={string.Join(',', seenStyles)} ownerSlot={minionIndex} parentSlot=60 peakBossBudget={parentBudgetPeak} expectedDetonation={test.expectedDeath} parent-loss-cleanup=PASS");
            }
            return "all-six-boss-minions-native-attack-lifetime-parent-cleanup=PASS NPC.NewNPC+NPC.UpdateNPC+Projectile.Update; boss budget<=40\n" + string.Join("\n", rows);
        }
        finally
        {
            Main.npc = oldNpcs; Main.projectile = oldProjectiles; Main.dust = oldDust;
            player.Center = oldCenter; player.active = oldActive; player.dead = oldDead;
            for (int x = -floorRadius; x <= floorRadius; x++) Main.tile[tx + x, ty].CopyFrom(floorBackup[x + floorRadius]);
        }
    }

    private static string RunMinionCapsAndAttackGeometry(int owner, Player player, int npcLength, int projectileLength)
    {
        NPC[] oldNpcs = Main.npc;
        Projectile[] oldProjectiles = Main.projectile;
        Vector2 oldCenter = player.Center;
        MethodInfo spawnEscort = typeof(MilkcraftBossNPC).GetMethod("SpawnEscort", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("SpawnEscort unavailable for native cap check");
        MethodInfo fire = typeof(MilkcraftBossNPC).GetMethod("Fire", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Fire unavailable for native projectile cap check");
        try
        {
            Main.netMode = NetmodeID.SinglePlayer;
            Main.myPlayer = owner;
            player.active = true; player.dead = false; player.Center = new Vector2(1800f, 1400f);
            Main.npc = Enumerable.Range(0, npcLength).Select(_ => new NPC()).ToArray();
            Main.projectile = Enumerable.Range(0, projectileLength).Select(_ => new Projectile()).ToArray();

            NPC MakeBoss(int slot, int type)
            {
                NPC boss = new() { whoAmI = slot };
                boss.SetDefaults(type); boss.whoAmI = slot; boss.active = true; boss.life = boss.lifeMax;
                boss.target = owner; boss.timeLeft = 4000; boss.Center = player.Center + new Vector2(400f + slot * 16f, -60f);
                Main.npc[slot] = boss;
                return boss;
            }

            int TrySpawn(NPC boss, MilkcraftBossMinionKind kind, int cap) => (int)spawnEscort.Invoke(boss.ModNPC,
                new object[] { kind, boss.Center + new Vector2(260f, -24f), cap })!;
            int OwnedMinions(int bossSlot) => Main.npc.Count(n => n != null && n.active
                && n.ModNPC is MilkcraftBossMinion
                && n.GetGlobalNPC<MilkcraftBossEscortGlobalNPC>().ParentBoss == bossSlot);

            var capRows = new List<string>();
            (string name, int type, (MilkcraftBossMinionKind kind, int count, int localCap)[] plan, int expected)[] caps =
            {
                ("WraithWarrior", ModContent.NPCType<WraithWarriorBoss>(), new[] { (MilkcraftBossMinionKind.WraithArcher, 2, 2) }, 2),
                ("Banshee", ModContent.NPCType<BansheeBoss>(), new[] { (MilkcraftBossMinionKind.SkeletonWarrior, 2, 2), (MilkcraftBossMinionKind.SkeletonArcher, 2, 2) }, 3),
                ("ObsidianGuardian", ModContent.NPCType<ObsidianGuardianBoss>(), new[] { (MilkcraftBossMinionKind.ExplosiveSlime, 3, 3), (MilkcraftBossMinionKind.FrostNaga, 2, 2) }, 4),
                ("Sphinx", ModContent.NPCType<SphinxBoss>(), new[] { (MilkcraftBossMinionKind.SkeletonWarrior, 1, 2), (MilkcraftBossMinionKind.SkeletonArcher, 1, 2) }, 2),
                ("FrostWitch", ModContent.NPCType<FrostWitchBoss>(), new[] { (MilkcraftBossMinionKind.FrostArcher, 4, 4) }, 4)
            };
            int slot = 60;
            foreach (var capCase in caps)
            {
                Main.npc = Enumerable.Range(0, npcLength).Select(_ => new NPC()).ToArray();
                NPC boss = MakeBoss(slot, capCase.type);
                int created = 0;
                foreach (var group in capCase.plan)
                    for (int i = 0; i < group.count; i++)
                    {
                        int id = TrySpawn(boss, group.kind, group.localCap);
                        if (id >= 0) created++;
                    }
                int denied = TrySpawn(boss, capCase.plan[0].kind, capCase.plan[0].localCap);
                int actual = OwnedMinions(slot);
                if (created != capCase.expected || denied >= 0 || actual != capCase.expected)
                    throw new InvalidOperationException($"{capCase.name} escort cap mismatch: created={created}, expected={capCase.expected}, extra={denied}, active={actual}");
                NPC first = Main.npc.First(n => n != null && n.active && n.ModNPC is MilkcraftBossMinion
                    && n.GetGlobalNPC<MilkcraftBossEscortGlobalNPC>().ParentBoss == slot);
                first.active = false;
                int refill = TrySpawn(boss, capCase.plan[0].kind, capCase.plan[0].localCap);
                if (refill < 0 || OwnedMinions(slot) != capCase.expected)
                    throw new InvalidOperationException($"{capCase.name} escort slot did not refill after one minion was defeated");
                capRows.Add($"escort-cap={capCase.name} actual={OwnedMinions(slot)}/{capCase.expected} extraRejected={denied < 0} refillAfterKill=PASS");
                slot++;
            }

            // Fill one real parent budget, prove the 41st shot is denied while another
            // encounter can still fire, then free one slot and prove it can be reused.
            Main.npc = Enumerable.Range(0, npcLength).Select(_ => new NPC()).ToArray();
            Main.projectile = Enumerable.Range(0, projectileLength).Select(_ => new Projectile()).ToArray();
            NPC parentA = MakeBoss(60, ModContent.NPCType<WraithWarriorBoss>());
            NPC parentB = MakeBoss(61, ModContent.NPCType<SphinxBoss>());
            for (int i = 0; i < 40; i++)
            {
                int p = Projectile.NewProjectile(parentA.GetSource_FromAI(), parentA.Center + new Vector2(0, -300f), new Vector2(4f, 0f),
                    ModContent.ProjectileType<MilkcraftBossProjectile>(), 12, 0f, owner, (float)BossProjectileStyle.WraithArrow, parentA.whoAmI);
                if (p < 0 || p >= Main.maxProjectiles) throw new InvalidOperationException("Could not fill a boss projectile budget with native projectiles");
                Main.projectile[p].ai[0] = (float)BossProjectileStyle.WraithArrow;
                Main.projectile[p].ai[1] = parentA.whoAmI;
            }
            fire.Invoke(parentA.ModNPC, new object[] { player, parentA.Center, new Vector2(5f, 0f), BossProjectileStyle.WraithArrow, 0.4f });
            int atCap = MilkcraftBossProjectileBudget.CountForBoss(parentA.whoAmI);
            fire.Invoke(parentB.ModNPC, new object[] { player, parentB.Center, new Vector2(5f, 0f), BossProjectileStyle.SandShot, 0.4f });
            int secondParent = MilkcraftBossProjectileBudget.CountForBoss(parentB.whoAmI);
            Projectile freed = Main.projectile.First(p => p != null && p.active && (int)p.ai[1] == parentA.whoAmI);
            freed.Kill();
            fire.Invoke(parentA.ModNPC, new object[] { player, parentA.Center, new Vector2(5f, 0f), BossProjectileStyle.WraithArrow, 0.4f });
            int afterRefill = MilkcraftBossProjectileBudget.CountForBoss(parentA.whoAmI);
            if (atCap != 40 || secondParent != 1 || afterRefill != 40)
                throw new InvalidOperationException($"Boss projectile budget isolation failed: atCap={atCap}, otherParent={secondParent}, afterRefill={afterRefill}");
            parentA.active = false;
            MethodInfo updateProjectile = typeof(Projectile).GetMethod("Update", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null, new[] { typeof(int) }, null) ?? throw new InvalidOperationException("Projectile.Update unavailable for budget owner cleanup");
            for (int i = 0; i < Main.projectile.Length; i++)
                if (Main.projectile[i] != null && Main.projectile[i].active && (int)Main.projectile[i].ai[1] == parentA.whoAmI)
                    updateProjectile.Invoke(Main.projectile[i], new object[] { i });
            int otherParentAfterLoss = MilkcraftBossProjectileBudget.CountForBoss(parentB.whoAmI);
            if (otherParentAfterLoss != 1)
                throw new InvalidOperationException($"Removing parent A incorrectly removed parent B attack: surviving={otherParentAfterLoss}");

            // Exercise the production Colliding hooks for the expanding ring, rotating
            // spear and attached minion sweep: center hole/gap and safe backside reject.
            NPC geometryBoss = MakeBoss(62, ModContent.NPCType<WraithWarriorBoss>());
            geometryBoss.Center = new Vector2(1000f, 1000f); geometryBoss.life = geometryBoss.lifeMax;
            int ringSlot = Projectile.NewProjectile(geometryBoss.GetSource_FromAI(), geometryBoss.Center, Vector2.Zero,
                ModContent.ProjectileType<MilkcraftBossProjectile>(), 1, 0f, owner, (float)BossProjectileStyle.BansheeWave, geometryBoss.whoAmI, 0f);
            Projectile ring = Main.projectile[ringSlot]; ring.ai[0] = (float)BossProjectileStyle.BansheeWave; ring.ai[1] = geometryBoss.whoAmI; ring.ai[2] = 0f; ring.timeLeft = 20;
            bool ringCenter = ring.ModProjectile.Colliding(ring.Hitbox, new Rectangle(995, 995, 10, 10)) == true;
            bool ringGap = ring.ModProjectile.Colliding(ring.Hitbox, new Rectangle(1096, 995, 10, 10)) == true;
            bool ringBand = ring.ModProjectile.Colliding(ring.Hitbox, new Rectangle(995, 1096, 10, 10)) == true;
            bool ringOutside = ring.ModProjectile.Colliding(ring.Hitbox, new Rectangle(1300, 995, 10, 10)) == true;
            if (ringCenter || ringGap || !ringBand || ringOutside)
                throw new InvalidOperationException($"Ring collision geometry failed: center={ringCenter}, gap={ringGap}, band={ringBand}, outside={ringOutside}");

            geometryBoss.life = geometryBoss.lifeMax;
            int spinSlot = Projectile.NewProjectile(geometryBoss.GetSource_FromAI(), geometryBoss.Center, Vector2.Zero,
                ModContent.ProjectileType<MilkcraftBossProjectile>(), 1, 0f, owner, (float)BossProjectileStyle.WraithSpin, geometryBoss.whoAmI, 0f);
            Projectile spin = Main.projectile[spinSlot]; spin.ai[0] = (float)BossProjectileStyle.WraithSpin; spin.ai[1] = geometryBoss.whoAmI; spin.rotation = 0f;
            bool spearFront = spin.ModProjectile.Colliding(spin.Hitbox, new Rectangle(1060, 995, 12, 10)) == true;
            bool spearBack = spin.ModProjectile.Colliding(spin.Hitbox, new Rectangle(930, 995, 12, 10)) == true;
            bool spearLongRange = spin.ModProjectile.Colliding(spin.Hitbox, new Rectangle(1165, 995, 12, 10)) == true;
            if (!spearFront || spearBack || spearLongRange)
                throw new InvalidOperationException($"Wraith spear collision line failed: front={spearFront}, back={spearBack}, beyond={spearLongRange}");

            NPC minion = new(); minion.SetDefaults(ModContent.NPCType<SkeletonWarriorMinion>()); minion.whoAmI = 70; minion.active = true; minion.Center = geometryBoss.Center;
            minion.GetGlobalNPC<MilkcraftBossEscortGlobalNPC>().ParentBoss = geometryBoss.whoAmI; Main.npc[minion.whoAmI] = minion;
            int sweepSlot = Projectile.NewProjectile(minion.GetSource_FromAI(), minion.Center, Vector2.Zero,
                ModContent.ProjectileType<MilkcraftBossProjectile>(), 1, 0f, owner, (float)BossProjectileStyle.MinionSweep, geometryBoss.whoAmI, minion.whoAmI);
            Projectile sweep = Main.projectile[sweepSlot]; sweep.ai[0] = (float)BossProjectileStyle.MinionSweep; sweep.ai[1] = geometryBoss.whoAmI; sweep.ai[2] = minion.whoAmI; sweep.rotation = 0f;
            bool sweepFront = sweep.ModProjectile.Colliding(sweep.Hitbox, new Rectangle(1045, 995, 12, 10)) == true;
            bool sweepBack = sweep.ModProjectile.Colliding(sweep.Hitbox, new Rectangle(945, 995, 12, 10)) == true;
            bool sweepSide = sweep.ModProjectile.Colliding(sweep.Hitbox, new Rectangle(995, 1060, 10, 12)) == true;
            if (!sweepFront || sweepBack || sweepSide)
                throw new InvalidOperationException($"Minion sword sweep collision line failed: front={sweepFront}, back={sweepBack}, side={sweepSide}");

            return "escort-caps=PASS boss-specific totals Wraith2/Banshee3/Guardian4/Sphinx2/Frost4; defeated slot refills\n" + string.Join("\n", capRows)
                + $"\nboss-projectile-budget-isolation=PASS parentA-at-cap={atCap}, parentB={secondParent}, parentA-refill={afterRefill}, parentB-after-parentA-loss={otherParentAfterLoss}"
                + $"\nattack-collision-geometry=PASS ring(center={ringCenter},gap={ringGap},band={ringBand},outside={ringOutside}) wraithSpin(front={spearFront},back={spearBack},beyond={spearLongRange}) minionSweep(front={sweepFront},back={sweepBack},side={sweepSide})";
        }
        finally { Main.npc = oldNpcs; Main.projectile = oldProjectiles; player.Center = oldCenter; }
    }

    private static string RunExplosiveSlimeLifetimeAndDistance(int owner, Player player, int npcLength, int projectileLength)
    {
        NPC[] oldNpcs = Main.npc;
        Projectile[] oldProjectiles = Main.projectile;
        Vector2 oldCenter = player.Center;
        int tx = Math.Clamp(Main.maxTilesX / 2, 100, Main.maxTilesX - 100);
        int ty = Math.Clamp(Main.maxTilesY / 2, 100, Main.maxTilesY - 30);
        const int halfFloor = 8;
        Tile[] oldFloor = Enumerable.Range(-halfFloor, halfFloor * 2 + 1).Select(dx => { Tile t = new(); t.CopyFrom(Main.tile[tx + dx, ty]); return t; }).ToArray();
        MethodInfo updateNpc = typeof(NPC).GetMethod("UpdateNPC", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null, new[] { typeof(int) }, null) ?? throw new InvalidOperationException("NPC.UpdateNPC unavailable for explosive slime lifetime");
        try
        {
            Main.npc = Enumerable.Range(0, npcLength).Select(_ => new NPC()).ToArray();
            Main.projectile = Enumerable.Range(0, projectileLength).Select(_ => new Projectile()).ToArray();
            for (int dx = -halfFloor; dx <= halfFloor; dx++)
            {
                Tile floor = Main.tile[tx + dx, ty]; floor.HasTile = true; floor.TileType = TileID.Stone;
                floor.IsActuated = false; floor.IsHalfBlock = false; floor.Slope = SlopeType.Solid;
            }
            NPC parent = new() { whoAmI = 60 };
            parent.SetDefaults(ModContent.NPCType<ObsidianGuardianBoss>()); parent.whoAmI = 60; parent.active = true; parent.life = parent.lifeMax;
            parent.target = owner; parent.timeLeft = 4000; parent.Center = new Vector2(tx * 16f - 260f, ty * 16f - 80f); Main.npc[60] = parent;

            NPC MakeSlime(int slot, float xOffset)
            {
                int index = NPC.NewNPC(new EntitySource_Misc("explosive slime 2D lifetime diagnostic"),
                    (int)(parent.Center.X + xOffset), ty * 16 - 80, ModContent.NPCType<ExplosiveSlimeMinion>());
                if (index < 0 || index >= Main.maxNPCs) throw new InvalidOperationException("NPC.NewNPC failed for explosive slime lifetime case");
                NPC slime = Main.npc[index]; slime.whoAmI = index; slime.target = owner; slime.timeLeft = 3000;
                slime.Bottom = new Vector2(parent.Center.X + xOffset, ty * 16f);
                slime.GetGlobalNPC<MilkcraftBossEscortGlobalNPC>().ParentBoss = parent.whoAmI;
                return slime;
            }
            NPC highCase = MakeSlime(61, 0f);
            player.active = true; player.dead = false; player.Center = highCase.Center + new Vector2(0f, -100f);
            int highChecks = 12;
            for (int tick = 0; tick < highChecks; tick++) updateNpc.Invoke(highCase, new object[] { highCase.whoAmI });
            if (!highCase.active || highCase.ai[1] != 0f || highCase.ai[2] != 0f)
                throw new InvalidOperationException($"Explosive slime 2D proximity incorrectly triggered from above: playerDistance={Vector2.Distance(player.Center, highCase.Center):0.0}, age={highCase.ai[0]}, timer={highCase.ai[1]}");
            int highDistance = (int)Vector2.Distance(player.Center, highCase.Center);
            player.Center = highCase.Center + new Vector2(38f, 0f);
            int triggeredAt = -1, explodedAt = -1;
            for (int tick = 0; tick < 60 && highCase.active; tick++)
            {
                updateNpc.Invoke(highCase, new object[] { highCase.whoAmI });
                if (highCase.ai[2] > 0f && triggeredAt < 0) triggeredAt = (int)highCase.ai[0];
                if (!highCase.active) explodedAt = (int)highCase.ai[0];
            }
            if (highCase.active || triggeredAt < 0 || explodedAt <= triggeredAt || explodedAt >= 180)
                throw new InvalidOperationException($"Explosive slime proximity trigger did not detonate on its warning timer: active={highCase.active} triggerAge={triggeredAt} explodeAge={explodedAt}");

            Main.npc = Enumerable.Range(0, npcLength).Select(_ => new NPC()).ToArray();
            Main.projectile = Enumerable.Range(0, projectileLength).Select(_ => new Projectile()).ToArray();
            parent = new NPC() { whoAmI = 60 }; parent.SetDefaults(ModContent.NPCType<ObsidianGuardianBoss>());
            parent.whoAmI = 60; parent.active = true; parent.life = parent.lifeMax; parent.target = owner; parent.timeLeft = 4000;
            parent.Center = new Vector2(tx * 16f - 260f, ty * 16f - 80f); Main.npc[60] = parent;
            NPC timeoutCase = MakeSlime(61, 0f);
            player.Center = timeoutCase.Center + new Vector2(1100f, -350f);
            for (int tick = 0; tick < 179 && timeoutCase.active; tick++) updateNpc.Invoke(timeoutCase, new object[] { timeoutCase.whoAmI });
            int warningRemainingAt179 = (int)timeoutCase.ai[1];
            if (!timeoutCase.active || timeoutCase.ai[0] < 170f || timeoutCase.ai[1] <= 0f)
                throw new InvalidOperationException($"Explosive slime lacked a hard 180-tick lifetime/final warning: active={timeoutCase.active} age={timeoutCase.ai[0]} timer={timeoutCase.ai[1]}");
            updateNpc.Invoke(timeoutCase, new object[] { timeoutCase.whoAmI });
            if (timeoutCase.active || timeoutCase.ai[0] != 180f)
                throw new InvalidOperationException($"Explosive slime did not expire and detonate at its 180-tick cap: active={timeoutCase.active} age={timeoutCase.ai[0]}");

            Main.npc = Enumerable.Range(0, npcLength).Select(_ => new NPC()).ToArray();
            Main.projectile = Enumerable.Range(0, projectileLength).Select(_ => new Projectile()).ToArray();
            parent = new NPC() { whoAmI = 60 }; parent.SetDefaults(ModContent.NPCType<ObsidianGuardianBoss>());
            parent.whoAmI = 60; parent.active = true; parent.life = parent.lifeMax; parent.target = owner; parent.timeLeft = 4000;
            parent.Center = new Vector2(tx * 16f - 260f, ty * 16f - 80f); Main.npc[60] = parent;
            NPC killedEarly = MakeSlime(61, 0f);
            killedEarly.ai[1] = 18f; killedEarly.ai[2] = 1f;
            killedEarly.life = 0;
            killedEarly.checkDead();
            int earlyKillProjectiles = Main.projectile.Count(projectile => projectile != null && projectile.active
                && projectile.type == ModContent.ProjectileType<MilkcraftBossProjectile>()
                && projectile.ai[0] == (float)BossProjectileStyle.ObsidianFlame);
            if (killedEarly.active || earlyKillProjectiles != 0)
                throw new InvalidOperationException($"Early-killed explosive slime left an attack: active={killedEarly.active} hostileShots={earlyKillProjectiles}");

            Main.npc = Enumerable.Range(0, npcLength).Select(_ => new NPC()).ToArray();
            Main.projectile = Enumerable.Range(0, projectileLength).Select(_ => new Projectile()).ToArray();
            parent = new NPC() { whoAmI = 60 }; parent.SetDefaults(ModContent.NPCType<ObsidianGuardianBoss>());
            parent.whoAmI = 60; parent.active = true; parent.life = parent.lifeMax; parent.target = owner; parent.timeLeft = 4000;
            parent.Center = new Vector2(tx * 16f - 260f, ty * 16f - 80f); Main.npc[60] = parent;
            NPC parentCancelled = MakeSlime(61, 0f);
            parentCancelled.ai[1] = 2f; parentCancelled.ai[2] = 1f;
            parent.active = false;
            updateNpc.Invoke(parentCancelled, new object[] { parentCancelled.whoAmI });
            int parentLossProjectiles = Main.projectile.Count(projectile => projectile != null && projectile.active
                && projectile.type == ModContent.ProjectileType<MilkcraftBossProjectile>()
                && projectile.ai[0] == (float)BossProjectileStyle.ObsidianFlame);
            if (parentCancelled.active || parentLossProjectiles != 0)
                throw new InvalidOperationException($"Parent-death-cancelled explosive slime left an attack: active={parentCancelled.active} hostileShots={parentLossProjectiles}");
            return $"explosive-slime-native-2D-fuse=PASS high-offset-distance={highDistance}px/{highChecks}ticks no-trigger; close-trigger-age={triggeredAt}, proximity-detonation-age={explodedAt}; distant-hard-lifetime=180ticks warningRemainingAt179={warningRemainingAt179}; early-checkDead-cancel={earlyKillProjectiles}, parent-loss-NPC.Update-cancel={parentLossProjectiles}";
        }
        finally
        {
            Main.npc = oldNpcs; Main.projectile = oldProjectiles; player.Center = oldCenter;
            for (int dx = -halfFloor; dx <= halfFloor; dx++) Main.tile[tx + dx, ty].CopyFrom(oldFloor[dx + halfFloor]);
        }
    }

    private static string RunSixBossMovePhaseMatrix(int owner, Player player, int npcLength, int projectileLength)
    {
        NPC[] oldNpcs = Main.npc;
        Projectile[] oldProjectiles = Main.projectile;
        Vector2 oldCenter = player.Center;
        bool oldImmune = player.immune;
        int oldImmuneTime = player.immuneTime;
        int oldMode = Main.GameMode;
        UnifiedRandom oldRandom = Main.rand;
        int tx = Math.Clamp(Main.maxTilesX / 2, 80, Main.maxTilesX - 80);
        int ty = Math.Clamp(Main.maxTilesY / 2, 80, Main.maxTilesY - 24);
        // Wide enough for a real pounce/dash to land before the fixture floor ends.
        const int xRadius = 60;
        const int yDepth = 20;
        Tile[,] backup = new Tile[xRadius * 2 + 1, yDepth + 1];
        for (int x = -xRadius; x <= xRadius; x++)
        for (int y = -yDepth; y <= 0; y++)
        {
            Tile copy = new(); copy.CopyFrom(Main.tile[tx + x, ty + y]); backup[x + xRadius, y + yDepth] = copy;
        }

        MethodInfo updateNpc = typeof(NPC).GetMethod("UpdateNPC", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null, new[] { typeof(int) }, null) ?? throw new InvalidOperationException("NPC.UpdateNPC(int) unavailable for six-boss matrix");
        MethodInfo updateProjectile = typeof(Projectile).GetMethod("Update", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null, new[] { typeof(int) }, null) ?? throw new InvalidOperationException("Projectile.Update(int) unavailable for six-boss matrix");
        FieldInfo moveField = typeof(MilkcraftBossNPC).GetField("_nextMove", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Boss _nextMove unavailable for six-boss matrix");
        FieldInfo transitionStarted = typeof(MilkcraftBossNPC).GetField("_lowPhaseTransitionStarted", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Boss low-phase transition state unavailable for six-boss matrix");
        FieldInfo transitionTicks = typeof(MilkcraftBossNPC).GetField("_lowPhaseTransitionTicks", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Boss low-phase transition timer unavailable for six-boss matrix");
        FieldInfo lockField = typeof(MilkcraftBossNPC).GetField("_lockedPoint", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Boss locked aim point unavailable for six-boss matrix");

        var rows = new List<string>(240);
        try
        {
            Main.netMode = NetmodeID.SinglePlayer;
            Main.myPlayer = owner;
            player.active = true; player.dead = false; player.immune = true; player.immuneTime = 600;
            int completed = 0;
            foreach (BossCase test in Cases.Where(c => c.Kind != MilkcraftBossKind.CastleCore))
            {
                int moveCount = test.Kind is MilkcraftBossKind.ObsidianGuardian or MilkcraftBossKind.FrostWitch ? 5 : 4;
                for (int mode = 0; mode <= 1; mode++)
                for (int lowPhase = 0; lowPhase <= 1; lowPhase++)
                for (int terrain = 0; terrain < 3; terrain++)
                for (int move = 0; move < moveCount; move++)
                {
                    SetGameModeAndRefreshOverrides(mode);
                    Main.rand = new UnifiedRandom(0x310000 + (int)test.Kind * 1000 + mode * 100 + lowPhase * 50 + terrain * 10 + move);
                    ResetMatrixTerrain(terrain, tx, ty, xRadius, yDepth, backup);
                    Main.npc = Enumerable.Range(0, npcLength).Select(_ => new NPC()).ToArray();
                    Main.projectile = Enumerable.Range(0, projectileLength).Select(_ => new Projectile()).ToArray();
                    float floorY = ty * 16f;
                    player.Center = new Vector2((tx + 15) * 16f, floorY - player.height * 0.5f);
                    player.velocity = Vector2.Zero;
                    NPC boss = new() { whoAmI = 60 };
                    boss.SetDefaults(test.NpcType);
                    boss.whoAmI = 60; boss.active = true; boss.timeLeft = 600; boss.target = owner;
                    boss.life = lowPhase == 0 ? boss.lifeMax * 90 / 100 : boss.lifeMax * 42 / 100;
                    boss.Center = new Vector2((tx - 8) * 16f, floorY - boss.height * 0.5f);
                    Main.npc[60] = boss;
                    transitionStarted.SetValue(boss.ModNPC, true);
                    transitionTicks.SetValue(boss.ModNPC, 0);
                    moveField.SetValue(boss.ModNPC, move);
                    boss.ai[0] = 0f; boss.ai[1] = 1000f; boss.ai[2] = 0f;
                    var styles = new HashSet<int>();
                    var minions = new HashSet<int>();
                    int warnTick = -1, attackTick = -1, recoverTick = -1, endTick = -1;
                    int jumpStartTick = -1, jumpLandingTick = -1;
                    float jumpLandingOffset = float.NaN;
                    Vector2 lockedAtWarning = Vector2.Zero;
                    bool lockChecked = false;
                    int totalShots = 0, priorCount = 0;
                    bool warningLockHeld = true;
                    bool sphinxSafeLaneHeld = true;
                    int maxTicks = test.Kind == MilkcraftBossKind.FrostWitch && move == 1 ? 380 : 330;
                    for (int tick = 0; tick < maxTicks; tick++)
                    {
                        updateNpc.Invoke(boss, new object[] { boss.whoAmI });
                        if (((test.Kind == MilkcraftBossKind.JungleLord && move == 3)
                                || (test.Kind == MilkcraftBossKind.ObsidianGuardian && move == 4))
                            && (int)boss.ai[0] == 2 && (int)boss.ai[2] == move)
                        {
                            if (jumpStartTick < 0 && boss.velocity.Y < -1f)
                                jumpStartTick = tick;
                            else if (jumpStartTick >= 0 && jumpLandingTick < 0 && tick > jumpStartTick + 4
                                && boss.velocity.Y == 0f && boss.collideY)
                            {
                                jumpLandingTick = tick;
                                jumpLandingOffset = boss.Center.X - ((Vector2)lockField.GetValue(boss.ModNPC)).X;
                            }
                        }
                        if ((int)boss.ai[2] == move && (int)boss.ai[0] == 1)
                        {
                            if (warnTick < 0)
                            {
                                warnTick = tick;
                                lockedAtWarning = (Vector2)lockField.GetValue(boss.ModNPC);
                            }
                            else if (!lockChecked)
                            {
                                player.Center += new Vector2(28f, -14f);
                                lockChecked = true;
                            }
                            if (Vector2.DistanceSquared((Vector2)lockField.GetValue(boss.ModNPC), lockedAtWarning) > 0.1f)
                                warningLockHeld = false;
                        }
                        if ((int)boss.ai[2] == move && (int)boss.ai[0] == 2 && attackTick < 0) attackTick = tick;
                        if ((int)boss.ai[2] == move && (int)boss.ai[0] == 3 && recoverTick < 0) recoverTick = tick;
                        if ((int)boss.ai[2] == move && (int)boss.ai[0] == 0 && recoverTick >= 0) { endTick = tick; break; }

                        int activeShots = 0;
                        for (int i = 0; i < Main.projectile.Length; i++)
                        {
                            Projectile p = Main.projectile[i];
                            if (p == null || !p.active || p.type != ModContent.ProjectileType<MilkcraftBossProjectile>()) continue;
                            activeShots++;
                            styles.Add((int)p.ai[0]);
                            if (test.Kind == MilkcraftBossKind.Sphinx && move == 3
                                && ((int)p.ai[0] == (int)BossProjectileStyle.Sandnado || (int)p.ai[0] == (int)BossProjectileStyle.SandShot)
                                && Math.Abs(p.Center.X - lockedAtWarning.X) < 96f)
                                sphinxSafeLaneHeld = false;
                        }
                        if (activeShots > priorCount) totalShots += activeShots - priorCount;
                        priorCount = activeShots;
                        for (int n = 0; n < Main.npc.Length; n++)
                        {
                            NPC escort = Main.npc[n];
                            if (n != boss.whoAmI && escort != null && escort.active && escort.ModNPC is MilkcraftBossMinion)
                                minions.Add(escort.type);
                        }
                        for (int n = 0; n < Main.npc.Length; n++)
                            if (n != boss.whoAmI && Main.npc[n] != null && Main.npc[n].active)
                                updateNpc.Invoke(Main.npc[n], new object[] { n });
                        for (int i = 0; i < Main.projectile.Length; i++)
                            if (Main.projectile[i] != null && Main.projectile[i].active)
                                updateProjectile.Invoke(Main.projectile[i], new object[] { i });
                    }
                    int[] expected = ExpectedStyles(test.Kind, move, lowPhase == 1);
                    bool stylesPass = expected.Length == 0 ? minions.Count > 0 : expected.All(styles.Contains);
                    bool landingRequired = (test.Kind == MilkcraftBossKind.JungleLord && move == 3)
                        || (test.Kind == MilkcraftBossKind.ObsidianGuardian && move == 4);
                    bool landingPass = !landingRequired || (jumpStartTick >= 0 && jumpLandingTick > jumpStartTick && Math.Abs(jumpLandingOffset) <= 140f);
                    if (warnTick < 0 || attackTick <= warnTick || recoverTick <= attackTick || endTick <= recoverTick || !warningLockHeld || !stylesPass || !sphinxSafeLaneHeld || !landingPass)
                        throw new InvalidOperationException($"move-matrix fail boss={test.Name} mode={(mode == 0 ? "normal" : "expert")} phase={(lowPhase == 0 ? "base" : "low")} terrain={terrain} move={move} warn={warnTick} attack={attackTick} recovery={recoverTick} end={endTick} lockStable={warningLockHeld} safeLane={sphinxSafeLaneHeld} landing={landingPass}/{jumpStartTick}->{jumpLandingTick}@{jumpLandingOffset:0.0} styles={string.Join(',', styles)} expected={string.Join(',', expected)} minions={string.Join(',', minions)} shots={totalShots} pos={boss.Center} vel={boss.velocity} noTileCollide={boss.noTileCollide}");
                    rows.Add($"move-matrix={test.Name}/{(mode == 0 ? "normal" : "expert")}/{(lowPhase == 0 ? "base" : "low")}/terrain{terrain}/move{move} warn={warnTick} attack={attackTick} recovery={recoverTick} complete={endTick} jump={jumpStartTick}->{jumpLandingTick}@{jumpLandingOffset:0.0} styles={string.Join(',', styles.OrderBy(v => v))} minions={minions.Count} shots={totalShots} lockedAim=PASS sandnadoSafeLane={(test.Kind == MilkcraftBossKind.Sphinx && move == 3 ? sphinxSafeLaneHeld : true)} nativeNPC+Projectile.Update=PASS");
                    completed++;
                }
            }
            const int expectedRows = (4 + 4 + 4 + 5 + 4 + 5) * 2 * 2 * 3;
            if (completed != expectedRows)
                throw new InvalidOperationException($"Six-boss move matrix incomplete: expected={expectedRows} actual={completed}");
            return $"six-boss-all-move-low-phase-native-matrix=PASS rows={completed} normal/expert x base/low x flat/platform/uneven; actual NPC.UpdateNPC+Projectile.Update\n" + string.Join("\n", rows);
        }
        finally
        {
            Main.npc = oldNpcs; Main.projectile = oldProjectiles;
            player.Center = oldCenter; player.immune = oldImmune; player.immuneTime = oldImmuneTime;
            SetGameModeAndRefreshOverrides(oldMode); Main.rand = oldRandom;
            for (int x = -xRadius; x <= xRadius; x++)
            for (int y = -yDepth; y <= 0; y++)
                Main.tile[tx + x, ty + y].CopyFrom(backup[x + xRadius, y + yDepth]);
        }
    }

    private static int[] ExpectedStyles(MilkcraftBossKind kind, int move, bool lowPhase)
        => kind switch
        {
            MilkcraftBossKind.WraithWarrior => move switch
            {
                0 => new[] { (int)BossProjectileStyle.WraithSpear },
                1 => new[] { (int)BossProjectileStyle.WraithSpin },
                2 => new[] { (int)BossProjectileStyle.WraithArrow },
                _ => new[] { (int)BossProjectileStyle.WraithSpear }
            },
            MilkcraftBossKind.JungleLord => move switch
            {
                0 or 1 or 3 => new[] { (int)BossProjectileStyle.VineRoot },
                _ => new[] { (int)BossProjectileStyle.JungleSeed }
            },
            MilkcraftBossKind.Banshee => new[] { (int)(move == 1 ? BossProjectileStyle.BansheeWave : BossProjectileStyle.BansheeSkull) },
            MilkcraftBossKind.ObsidianGuardian => move switch
            {
                1 => new[] { (int)BossProjectileStyle.ObsidianRing },
                2 => new[] { (int)BossProjectileStyle.ObsidianMeteor },
                3 => new[] { (int)BossProjectileStyle.IceNaga },
                _ => new[] { (int)BossProjectileStyle.ObsidianFlame }
            },
            MilkcraftBossKind.Sphinx => move switch
            {
                1 => new[] { (int)BossProjectileStyle.SphinxRing },
                3 => new[] { (int)BossProjectileStyle.Sandnado, (int)BossProjectileStyle.SandShot },
                _ => new[] { (int)BossProjectileStyle.SandShot }
            },
            MilkcraftBossKind.FrostWitch => move switch
            {
                1 => new[] { (int)BossProjectileStyle.FrostPillar },
                2 => new[] { (int)BossProjectileStyle.FrostRing },
                _ => new[] { (int)BossProjectileStyle.FrostSpear }
            },
            _ => Array.Empty<int>()
        };

    private static void ResetMatrixTerrain(int terrain, int tx, int ty, int xRadius, int yDepth, Tile[,] backup)
    {
        for (int x = -xRadius; x <= xRadius; x++)
        for (int y = -yDepth; y <= 0; y++)
            Main.tile[tx + x, ty + y].CopyFrom(backup[x + xRadius, y + yDepth]);
        void Solid(int x, int y, ushort type = TileID.Stone)
        {
            Tile t = Main.tile[tx + x, ty + y]; t.HasTile = true; t.TileType = type;
            t.IsActuated = false; t.IsHalfBlock = false; t.Slope = SlopeType.Solid;
        }
        for (int x = -xRadius; x <= xRadius; x++) Solid(x, 0);
        if (terrain == 1)
        {
            for (int x = -12; x <= -6; x++) Solid(x, -7, TileID.Platforms);
            for (int x = 5; x <= 12; x++) Solid(x, -10, TileID.Platforms);
            for (int x = -3; x <= 2; x++) Solid(x, -1);
        }
        else if (terrain == 2)
        {
            for (int step = 1; step <= 4; step++)
                for (int x = 3 + step; x <= 4 + step; x++)
                    for (int y = -step; y <= -1; y++) Solid(x, y);
            for (int x = -12; x <= -9; x++) Solid(x, -1);
        }
    }

    private static string RunCastleWeakPullHit(int owner, Player player, int npcLength, int projectileLength)
    {
        NPC[] oldNpcs = Main.npc;
        Projectile[] oldProjectiles = Main.projectile;
        Vector2 oldPosition = player.position, oldVelocity = player.velocity;
        int oldLife = player.statLife, oldImmuneTime = player.immuneTime;
        bool oldImmune = player.immune;
        int[] oldCooldowns = (int[])player.hurtCooldowns.Clone();
        Item[] oldItems = Main.item;
        try
        {
            Main.npc = Enumerable.Range(0, npcLength).Select(_ => new NPC()).ToArray();
            Main.projectile = Enumerable.Range(0, projectileLength).Select(_ => new Projectile()).ToArray();
            Main.item = Enumerable.Range(0, Math.Max(Main.maxItems + 1, oldItems.Length)).Select(_ => new Item()).ToArray();
            player.active = true; player.dead = false; player.statLife = player.statLifeMax2;
            player.immune = false; player.immuneTime = 0; Array.Clear(player.hurtCooldowns);
            player.Center = new Vector2(1600f, 1300f);
            player.velocity = new Vector2(12f, 0f);
            NPC boss = new() { whoAmI = 60 };
            boss.SetDefaults(ModContent.NPCType<CastleCoreBoss>());
            boss.whoAmI = 60; boss.active = true; boss.life = boss.lifeMax; boss.target = owner;
            boss.Center = player.Center + new Vector2(-120f, 0f);
            Main.npc[60] = boss;
            MethodInfo fire = typeof(MilkcraftBossNPC).GetMethod("Fire", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("Castle Fire helper unavailable for weak-pull actual hit test");
            fire.Invoke(boss.ModNPC, new object[] { player, player.Center + new Vector2(-48f, 0f), new Vector2(9f, 0f), BossProjectileStyle.CastleBlue, 0.25f });
            Projectile shot = Main.projectile.FirstOrDefault(p => p != null && p.active && p.type == ModContent.ProjectileType<MilkcraftBossProjectile>())
                ?? throw new InvalidOperationException("Castle weak-pull fixture failed to spawn the native blue projectile");
            shot.tileCollide = false;
            MethodInfo updateProjectile = typeof(Projectile).GetMethod("Update", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null, new[] { typeof(int) }, null) ?? throw new InvalidOperationException("Projectile.Update(int) unavailable for weak-pull test");
            for (int tick = 0; tick < 12 && player.FindBuffIndex(BuffID.BrokenArmor) < 0; tick++)
                updateProjectile.Invoke(shot, new object[] { Array.IndexOf(Main.projectile, shot) });
            CastleWeakPullPlayer pull = player.GetModPlayer<CastleWeakPullPlayer>();
            if (player.FindBuffIndex(BuffID.BrokenArmor) < 0 || pull.ActiveTicks <= 0)
                throw new InvalidOperationException($"Castle blue projectile did not apply native hit effects: life={player.statLife} brokenArmor={player.FindBuffIndex(BuffID.BrokenArmor)} pullTicks={pull.ActiveTicks}");
            float actualHitVelocityX = player.velocity.X;
            // Preserve the ModPlayer status created by the real hit while giving
            // its next movement hook a fast mount/dash-like velocity to ensure it
            // adds only its small pull and never clamps pre-existing motion.
            player.velocity = new Vector2(12f, 0f);
            float beforeX = player.velocity.X;
            pull.PreUpdateMovement();
            if (player.velocity.X >= beforeX || player.velocity.X <= 8f || Math.Abs((beforeX - player.velocity.X) - 0.10f) > 0.02f)
                throw new InvalidOperationException($"Castle weak pull changed/clamped existing movement incorrectly: before={beforeX} after={player.velocity.X}");
            int remaining = pull.ActiveTicks;
            pull.UpdateDead();
            if (pull.ActiveTicks != 0)
                throw new InvalidOperationException("Castle weak pull timer was not cleared on death");
            return $"castle-blue-Projectile.Update-hit=PASS BrokenArmor=120 pullTicks={remaining} actualHurtVelocity={actualHitVelocityX:0.00} highSpeedProbe={beforeX:0.00}->{player.velocity.X:0.00}(0.1 pull, no clamp) UpdateDead-clear=PASS";
        }
        finally
        {
            Main.npc = oldNpcs; Main.projectile = oldProjectiles; Main.item = oldItems;
            player.position = oldPosition; player.velocity = oldVelocity; player.statLife = oldLife;
            player.immune = oldImmune; player.immuneTime = oldImmuneTime;
            Array.Copy(oldCooldowns, player.hurtCooldowns, Math.Min(oldCooldowns.Length, player.hurtCooldowns.Length));
        }
    }

    private static string RunCastleSoulCrystalLifetime(int owner, Player player, int npcLength)
    {
        NPC[] oldNpcs = Main.npc;
        Vector2 oldPlayerCenter = player.Center;
        try
        {
            Main.npc = Enumerable.Range(0, npcLength).Select(_ => new NPC()).ToArray();
            player.active = true; player.dead = false; player.Center = new Vector2(1600f, 1300f);
            NPC parent = new() { whoAmI = 60 };
            parent.SetDefaults(ModContent.NPCType<CastleCoreBoss>());
            parent.whoAmI = 60; parent.active = true; parent.target = owner; parent.Center = player.Center + new Vector2(-300f, -80f);
            Main.npc[60] = parent;
            NPC crystal = new() { whoAmI = 61 };
            crystal.SetDefaults(ModContent.NPCType<CastleSoulCrystal>());
            crystal.whoAmI = 61; crystal.active = true; crystal.target = owner; crystal.ai[0] = 60f; crystal.Center = parent.Center + new Vector2(120f, 0f);
            Main.npc[61] = crystal;
            MethodInfo updateNpc = typeof(NPC).GetMethod("UpdateNPC", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null, new[] { typeof(int) }, null) ?? throw new InvalidOperationException("NPC.UpdateNPC(int) unavailable for soul lifetime test");
            for (int tick = 0; tick < 160 && crystal.active; tick++)
                updateNpc.Invoke(crystal, new object[] { crystal.whoAmI });
            if (crystal.active || crystal.ai[2] < 150f)
                throw new InvalidOperationException($"Castle soul crystal exceeded 150 native updates: active={crystal.active} age={crystal.ai[2]}");
            return $"castle-soul-crystal-native-UpdateNPC-age={crystal.ai[2]:0} active={crystal.active}=PASS (hard AI age limit; not timeLeft refresh)";
        }
        finally
        {
            Main.npc = oldNpcs; player.Center = oldPlayerCenter;
        }
    }

    private static string RunNoTargetHazardCleanup(int owner, Player player, int npcLength, int projectileLength)
    {
        NPC[] oldNpcs = Main.npc;
        Projectile[] oldProjectiles = Main.projectile;
        bool[] oldActive = Main.player.Select(p => p.active).ToArray();
        bool[] oldDead = Main.player.Select(p => p.dead).ToArray();
        bool[] oldGhost = Main.player.Select(p => p.ghost).ToArray();
        Vector2[] oldPlayerCenters = Main.player.Select(p => p.Center).ToArray();
        Vector2 oldCenter = player.Center;
        Item[] oldItems = Main.item;
        int oldNetMode = Main.netMode;
        var oldWorldFlags = new Terraria.ModLoader.IO.TagCompound();
        ModContent.GetInstance<MilkcraftBossWorldSystem>().SaveWorldData(oldWorldFlags);
        try
        {
            // Keep the native dedicated-server NPC path. This menu fixture has
            // no live RemoteClient sockets, so discard outgoing packets only;
            // NPC.UpdateNPC/AI/targeting/authority still execute normally.
            _diagnosticDiscardedServerPackets = 0;
            On_NetMessage.SendData += DiscardDiagnosticServerPacket;
            Main.netMode = NetmodeID.Server;
            int survivorIndex = (owner + 1) % Main.maxPlayers;
            Main.npc = Enumerable.Range(0, npcLength).Select(_ => new NPC()).ToArray();
            Main.projectile = Enumerable.Range(0, projectileLength).Select(_ => new Projectile()).ToArray();
            MethodInfo updateNpc = typeof(NPC).GetMethod("UpdateNPC", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null, new[] { typeof(int) }, null) ?? throw new InvalidOperationException("NPC.UpdateNPC(int) unavailable for native retarget test");
            var targetRows = new List<string>();
            foreach (BossCase targetCase in Cases)
            foreach (string unavailableReason in new[] { "dead", "inactive", "ghost" })
            {
                Main.npc = Enumerable.Range(0, npcLength).Select(_ => new NPC()).ToArray();
                foreach (Player candidate in Main.player) { candidate.active = false; candidate.dead = true; candidate.ghost = false; }
                player.Center = new Vector2(2000f, 1500f);
                player.active = unavailableReason != "inactive";
                player.dead = unavailableReason == "dead";
                player.ghost = unavailableReason == "ghost";
                Player survivor = Main.player[survivorIndex]; survivor.whoAmI = survivorIndex; survivor.active = true; survivor.dead = false; survivor.ghost = false;
                survivor.Center = player.Center + new Vector2(240f, 0f);
                NPC retargetBoss = new() { whoAmI = 60 };
                retargetBoss.SetDefaults(targetCase.NpcType);
                retargetBoss.whoAmI = 60; retargetBoss.active = true; retargetBoss.life = retargetBoss.lifeMax;
                retargetBoss.timeLeft = 600; retargetBoss.target = owner; retargetBoss.Center = player.Center + new Vector2(40f, 0f);
                Main.npc[60] = retargetBoss;
                updateNpc.Invoke(retargetBoss, new object[] { retargetBoss.whoAmI });
                if (!retargetBoss.active || retargetBoss.target != survivorIndex || retargetBoss.dontTakeDamage)
                    throw new InvalidOperationException($"Native NPC.UpdateNPC failed to retarget {targetCase.Name} from {unavailableReason} player to active teammate: active={retargetBoss.active} target={retargetBoss.target} expected={survivorIndex} invulnerable={retargetBoss.dontTakeDamage} despawnTime={retargetBoss.timeLeft}.");
                targetRows.Add($"{targetCase.Name}:{unavailableReason}->survivor{survivorIndex}");
            }

            // Retain vanilla TargetClosest aggro scoring: a farther live player
            // with strong aggro can outrank the geometrically-nearest teammate.
            Main.npc = Enumerable.Range(0, npcLength).Select(_ => new NPC()).ToArray();
            foreach (Player candidate in Main.player) { candidate.active = false; candidate.dead = true; candidate.ghost = false; candidate.aggro = 0; }
            player.Center = new Vector2(2000f, 1500f); player.active = true; player.dead = true;
            int nearestIndex = (survivorIndex + 1) % Main.maxPlayers;
            Player nearest = Main.player[nearestIndex]; nearest.whoAmI = nearestIndex; nearest.active = true; nearest.dead = false; nearest.Center = player.Center + new Vector2(100f, 0f);
            Player aggroTarget = Main.player[survivorIndex]; aggroTarget.whoAmI = survivorIndex; aggroTarget.active = true; aggroTarget.dead = false; aggroTarget.aggro = 1000; aggroTarget.Center = player.Center + new Vector2(280f, 0f);
            NPC aggroBoss = new() { whoAmI = 60 }; aggroBoss.SetDefaults(ModContent.NPCType<WraithWarriorBoss>());
            aggroBoss.whoAmI = 60; aggroBoss.active = true; aggroBoss.life = aggroBoss.lifeMax; aggroBoss.target = owner; aggroBoss.Center = player.Center;
            Main.npc[60] = aggroBoss; updateNpc.Invoke(aggroBoss, new object[] { aggroBoss.whoAmI });
            if (aggroBoss.target != survivorIndex)
                throw new InvalidOperationException($"Native aggro-weighted TargetClosest result changed: nearest={nearestIndex} highAggro={survivorIndex} selected={aggroBoss.target}.");
            string retargetResult = $"native-retarget=PASS 7Bosses×dead/inactive/ghost current target -> live teammate via server NPC.UpdateNPC; aggro-weighted native selection kept farther target={survivorIndex} over nearest={nearestIndex}; cases={string.Join(',', targetRows)}";

            FieldInfo despawnTicksField = typeof(MilkcraftBossNPC).GetField("_despawnTicks", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("Boss no-target despawn counter unavailable for revive probe");
            Main.npc = Enumerable.Range(0, npcLength).Select(_ => new NPC()).ToArray();
            foreach (Player candidate in Main.player) { candidate.active = true; candidate.dead = true; candidate.ghost = false; }
            player.Center = new Vector2(2000f, 1500f);
            NPC reviveBoss = new() { whoAmI = 60 }; reviveBoss.SetDefaults(ModContent.NPCType<WraithWarriorBoss>());
            reviveBoss.whoAmI = 60; reviveBoss.active = true; reviveBoss.life = reviveBoss.lifeMax; reviveBoss.target = owner;
            reviveBoss.timeLeft = 600; reviveBoss.Center = player.Center + new Vector2(180f, 0f); Main.npc[60] = reviveBoss;
            for (int tick = 0; tick < 5; tick++) updateNpc.Invoke(reviveBoss, new object[] { reviveBoss.whoAmI });
            int abandonedTicks = (int)despawnTicksField.GetValue(reviveBoss.ModNPC);
            if (abandonedTicks < 1) throw new InvalidOperationException($"No-target counter did not start before revive: ticks={abandonedTicks}.");
            player.active = true; player.dead = false; player.ghost = false; player.Center = reviveBoss.Center + new Vector2(120f, 0f);
            updateNpc.Invoke(reviveBoss, new object[] { reviveBoss.whoAmI });
            int revivedTicks = (int)despawnTicksField.GetValue(reviveBoss.ModNPC);
            if (!reviveBoss.active || reviveBoss.dontTakeDamage || reviveBoss.target != owner || revivedTicks != 0)
                throw new InvalidOperationException($"A returning living player did not resume the encounter: active={reviveBoss.active},invulnerable={reviveBoss.dontTakeDamage},target={reviveBoss.target},despawnTicks={revivedTicks}.");
            string reviveResult = $"native-revive=PASS noTargetTicks={abandonedTicks} revivedTarget={owner} counter={revivedTicks} invulnerable={reviveBoss.dontTakeDamage}";

            Main.npc = Enumerable.Range(0, npcLength).Select(_ => new NPC()).ToArray();
            Main.projectile = Enumerable.Range(0, projectileLength).Select(_ => new Projectile()).ToArray();
            Main.item = Enumerable.Range(0, Math.Max(Main.maxItems + 1, oldItems.Length)).Select(_ => new Item()).ToArray();
            foreach (Player candidate in Main.player) { candidate.active = true; candidate.dead = false; candidate.ghost = true; }
            player.Center = new Vector2(2000f, 1500f);
            NPC ghostsBoss = new() { whoAmI = 60 }; ghostsBoss.SetDefaults(ModContent.NPCType<WraithWarriorBoss>());
            ghostsBoss.whoAmI = 60; ghostsBoss.active = true; ghostsBoss.life = ghostsBoss.lifeMax; ghostsBoss.target = owner;
            ghostsBoss.timeLeft = 600; ghostsBoss.Center = player.Center + new Vector2(180f, 0f); Main.npc[60] = ghostsBoss;
            updateNpc.Invoke(ghostsBoss, new object[] { ghostsBoss.whoAmI });
            if (!ghostsBoss.dontTakeDamage || (int)despawnTicksField.GetValue(ghostsBoss.ModNPC) < 1)
                throw new InvalidOperationException($"All-ghost party was treated as a valid target: target={ghostsBoss.target},invulnerable={ghostsBoss.dontTakeDamage},ticks={despawnTicksField.GetValue(ghostsBoss.ModNPC)}.");
            for (int tick = 1; tick < 20 && ghostsBoss.active; tick++) updateNpc.Invoke(ghostsBoss, new object[] { ghostsBoss.whoAmI });
            if (ghostsBoss.active || ghostsBoss.life <= 0)
                throw new InvalidOperationException($"All-ghost party did not trigger no-target despawn: active={ghostsBoss.active},life={ghostsBoss.life}.");
            string ghostResult = "native-all-ghosts=PASS no valid target; server despawns without death/reward";

            Main.npc = Enumerable.Range(0, npcLength).Select(_ => new NPC()).ToArray();
            Main.projectile = Enumerable.Range(0, projectileLength).Select(_ => new Projectile()).ToArray();
            Main.item = Enumerable.Range(0, Math.Max(Main.maxItems + 1, oldItems.Length)).Select(_ => new Item()).ToArray();
            foreach (Player candidate in Main.player) { candidate.active = true; candidate.dead = true; }
            player.active = true; player.dead = true;
            player.Center = new Vector2(2000f, 1500f);
            NPC boss = new() { whoAmI = 60 };
            boss.SetDefaults(ModContent.NPCType<WraithWarriorBoss>());
            boss.whoAmI = 60; boss.active = true; boss.life = boss.lifeMax; boss.timeLeft = 600; boss.target = owner;
            boss.Center = player.Center + new Vector2(180f, 0f);
            Main.npc[60] = boss;
            NPC unrelatedBoss = new() { whoAmI = 61 };
            unrelatedBoss.SetDefaults(ModContent.NPCType<SphinxBoss>());
            unrelatedBoss.whoAmI = 61; unrelatedBoss.active = true; unrelatedBoss.life = unrelatedBoss.lifeMax;
            unrelatedBoss.timeLeft = 600; unrelatedBoss.target = owner; unrelatedBoss.Center = boss.Center + new Vector2(800f, 0f);
            Main.npc[61] = unrelatedBoss;

            Projectile SpawnOwned(int parent, Vector2 center)
            {
                int slot = Projectile.NewProjectile(new EntitySource_Misc("BossNoTargetCleanup"), center,
                    new Vector2(3f, 0f), ModContent.ProjectileType<MilkcraftBossProjectile>(), 30, 0f,
                    Main.maxPlayers, (float)BossProjectileStyle.WraithSpear, parent);
                if (slot < 0 || slot >= Main.projectile.Length) throw new InvalidOperationException("Could not create no-target cleanup probe projectile");
                Projectile p = Main.projectile[slot];
                p.ai[0] = (float)BossProjectileStyle.WraithSpear; p.ai[1] = parent;
                return p;
            }

            Projectile owned = SpawnOwned(60, boss.Center);
            Projectile other = SpawnOwned(61, unrelatedBoss.Center);
            int childIndex = NPC.NewNPC(new EntitySource_Misc("BossNoTargetCleanupEscort"), (int)boss.Center.X,
                (int)boss.Center.Y, ModContent.NPCType<WraithArcherMinion>());
            if (childIndex < 0 || childIndex >= Main.maxNPCs) throw new InvalidOperationException("Could not create linked escort cleanup probe");
            NPC child = Main.npc[childIndex];
            child.GetGlobalNPC<MilkcraftBossEscortGlobalNPC>().ParentBoss = 60;
            child.active = true;

            updateNpc.Invoke(boss, new object[] { boss.whoAmI });
            if (owned.active || child.active || !other.active || !boss.dontTakeDamage)
                throw new InvalidOperationException($"no-target cleanup mismatch: ownProjectile={owned.active} linkedEscort={child.active} unrelatedProjectile={other.active} invulnerable={boss.dontTakeDamage} timeLeft={boss.timeLeft}");
            for (int tick = 1; tick < 20 && boss.active; tick++) updateNpc.Invoke(boss, new object[] { boss.whoAmI });
            if (boss.active || boss.life <= 0 || Main.item.Any(item => item != null && item.active && item.type == ModContent.ItemType<WraithWarriorBag>()))
                throw new InvalidOperationException($"native no-target timeout did not despawn without death/reward: active={boss.active} life={boss.life} timeLeft={boss.timeLeft}");

            // Repeat the same native path for an active but far-away target.
            player.active = true; player.dead = false;
            NPC farBoss = new() { whoAmI = 62 };
            farBoss.SetDefaults(ModContent.NPCType<WraithWarriorBoss>());
            farBoss.whoAmI = 62; farBoss.active = true; farBoss.life = farBoss.lifeMax; farBoss.timeLeft = 600;
            farBoss.target = owner; farBoss.Center = new Vector2(2000f, 1500f);
            Main.npc[62] = farBoss;
            player.Center = farBoss.Center + new Vector2(4200f, 0f);
            Projectile farOwned = SpawnOwned(62, farBoss.Center);
            updateNpc.Invoke(farBoss, new object[] { farBoss.whoAmI });
            if (farOwned.active || !farBoss.dontTakeDamage)
                throw new InvalidOperationException($"far-target cleanup failed: projectile={farOwned.active} timeLeft={farBoss.timeLeft}");
            for (int tick = 1; tick < 20 && farBoss.active; tick++) updateNpc.Invoke(farBoss, new object[] { farBoss.whoAmI });
            if (farBoss.active || farBoss.life <= 0 || Main.item.Any(item => item != null && item.active && item.type == ModContent.ItemType<WraithWarriorBag>()))
                throw new InvalidOperationException($"far-target timeout did not despawn without death/reward: active={farBoss.active} life={farBoss.life} timeLeft={farBoss.timeLeft}");
            return retargetResult + "\n" + reviveResult + "\n" + ghostResult + $"\nboss-no-target-native-cleanup=PASS NPC.UpdateNPC all-active-dead/no-target and active-target-distance=4200px immediately clear own shots+escorts; unrelated boss-owned shot survives; server despawn after20 ticks without CheckDead/reward despite native timeLeft refresh; outgoing packets isolated={_diagnosticDiscardedServerPackets} (no live client transport)";
        }
        finally
        {
            On_NetMessage.SendData -= DiscardDiagnosticServerPacket;
            Main.netMode = oldNetMode;
            Main.npc = oldNpcs; Main.projectile = oldProjectiles; Main.item = oldItems; player.Center = oldCenter;
            for (int i = 0; i < Math.Min(Main.player.Length, oldActive.Length); i++)
            { Main.player[i].active = oldActive[i]; Main.player[i].dead = oldDead[i]; Main.player[i].ghost = oldGhost[i]; Main.player[i].Center = oldPlayerCenters[i]; }
            ModContent.GetInstance<MilkcraftBossWorldSystem>().LoadWorldData(oldWorldFlags);
        }
    }

}

/// <summary>Counts native Player.Hurt evaluations in isolated boss-damage measurements.</summary>
public sealed class BossHurtProbePlayer : ModPlayer
{
    public static bool Enabled;
    public int HurtCalls;
    public int LastHurtDamage;
    public string ModifierSummary = "";

    public override void ModifyHurt(ref Player.HurtModifiers modifiers)
    {
        if (!Enabled) return;
        HurtCalls++;
        var source = modifiers.SourceDamage;
        var incoming = modifiers.IncomingDamageMultiplier;
        var final = modifiers.FinalDamage;
        ModifierSummary = $"src={source.Base:0.##}/{source.Additive:0.###}/{source.Multiplicative:0.###}/{source.Flat:0.##},in={incoming},final={final.Base:0.##}/{final.Additive:0.###}/{final.Multiplicative:0.###}/{final.Flat:0.##},ap={modifiers.ArmorPenetration:0.##},sap={modifiers.ScalingArmorPenetration:0.###}";
    }

    public override void OnHurt(Player.HurtInfo info)
    {
        if (Enabled) LastHurtDamage = info.Damage;
    }
}
