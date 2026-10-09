using Terraria;
using Terraria.DataStructures;
using Terraria.GameContent.ItemDropRules;
using Terraria.ID;
using Terraria.ModLoader;
using MilkcraftTerraria.Content.Items;

namespace MilkcraftTerraria.Content;

/// <summary>Fixed additions to vanilla boss rewards. Existing vanilla rules are untouched.</summary>
public sealed class ProgressionBossLoot : GlobalNPC
{
    public override bool InstancePerEntity => true;
    private bool _ownsGolemRole;
    private bool _golemRewardHandled;

    public override void SetDefaults(NPC entity)
    {
        if (IsEaterSegment(entity.type))
            ProgressionBossCycleSystem.ObserveEaterSpawn(entity);
        if (entity.type == NPCID.Retinazer || entity.type == NPCID.Spazmatism)
            ProgressionBossCycleSystem.ObserveTwinSpawn(entity);
    }

    public override bool PreKill(NPC npc)
    {
        if (npc.type == NPCID.Golem && Main.netMode != NetmodeID.MultiplayerClient)
        {
            ModContent.GetInstance<ProgressionGolemCycleSystem>().PrepareKill();
            _ownsGolemRole = true;
        }
        return true;
    }

    public override void ModifyNPCLoot(NPC npc, NPCLoot npcLoot)
    {
        if (npc.type == NPCID.BrainofCthulhu)
            AddForNormalDifficulty(npcLoot, blackIronOre: 120, pinkDiamond: 24);
        else if (npc.type == NPCID.SkeletronPrime)
            AddForNormalDifficulty(npcLoot, truthSword: 1, crystalSkull: 1);
        else if (npc.type == NPCID.TheDestroyer)
            AddForNormalDifficulty(npcLoot, truthBow: 1, alienKeratin: 1);
        else if (npc.type == NPCID.WallofFlesh)
            AddForNormalDifficulty(npcLoot, enchantmentHeart: 1);
    }

    public override void OnKill(NPC npc)
    {
        if (Main.netMode == NetmodeID.MultiplayerClient)
            return;

        // Every Eater segment can split into more heads. Award only from the
        // final head/body/tail death, ignoring the dying NPC itself so this
        // still works while CheckDead has not yet cleared npc.active.
        if (!Main.expertMode && IsEaterSegment(npc.type)
            && !ProgressionBossCycleSystem.EaterRewardClaimed && NoLivingEaterSegmentOtherThan(npc))
        {
            ProgressionBossCycleSystem.EaterRewardClaimed = true;
            Drop(npc, ModContent.ItemType<BlackIronOre>(), 120, ModContent.ItemType<PinkDiamond>(), 24);
        }

        if (!Main.expertMode && (npc.type == NPCID.Retinazer || npc.type == NPCID.Spazmatism)
            && !ProgressionBossCycleSystem.TwinsRewardClaimed && NoLivingTwinOtherThan(npc))
        {
            ProgressionBossCycleSystem.TwinsRewardClaimed = true;
            Drop(npc, ModContent.ItemType<TruthStaff>(), 1, ModContent.ItemType<ArcaneCore>(), 1);
        }

        if (npc.type == NPCID.Golem && _ownsGolemRole && !_golemRewardHandled)
        {
            _golemRewardHandled = true;
            GolemRewardRole role = ModContent.GetInstance<ProgressionGolemCycleSystem>().CompleteKill();
            (int broken, int core) = ProgressionGolemCycleSystem.RewardFor(role);
            if (!Main.expertMode && broken > 0 && core > 0)
                Drop(npc, broken, 1, core, 1);
        }
    }

    private static bool NoLivingTwinOtherThan(NPC npc)
    {
        foreach (NPC other in Main.npc)
            if (other.whoAmI != npc.whoAmI && other.active
                && (other.type == NPCID.Retinazer || other.type == NPCID.Spazmatism) && other.life > 0)
                return false;
        return true;
    }

    internal static bool IsEaterSegment(int type)
        => type == NPCID.EaterofWorldsHead || type == NPCID.EaterofWorldsBody || type == NPCID.EaterofWorldsTail;

    internal static bool NoLivingEaterSegmentOtherThan(NPC npc)
    {
        foreach (NPC other in Main.npc)
            if (other.whoAmI != npc.whoAmI && other.active && other.life > 0 && IsEaterSegment(other.type))
                return false;
        return true;
    }

    private static void AddForNormalDifficulty(NPCLoot loot, int blackIronOre = 0, int pinkDiamond = 0,
        int truthSword = 0, int crystalSkull = 0, int truthBow = 0, int alienKeratin = 0,
        int enchantmentHeart = 0)
    {
        var normal = new Conditions.NotExpert();
        if (blackIronOre > 0)
            loot.Add(ItemDropRule.ByCondition(normal, ModContent.ItemType<BlackIronOre>(), 1, blackIronOre, blackIronOre));
        if (pinkDiamond > 0)
            loot.Add(ItemDropRule.ByCondition(normal, ModContent.ItemType<PinkDiamond>(), 1, pinkDiamond, pinkDiamond));
        if (truthSword > 0)
            loot.Add(ItemDropRule.ByCondition(normal, ModContent.ItemType<TruthSword>(), 1, truthSword, truthSword));
        if (crystalSkull > 0)
            loot.Add(ItemDropRule.ByCondition(normal, ModContent.ItemType<CrystalSkull>(), 1, crystalSkull, crystalSkull));
        if (truthBow > 0)
            loot.Add(ItemDropRule.ByCondition(normal, ModContent.ItemType<TruthBow>(), 1, truthBow, truthBow));
        if (alienKeratin > 0)
            loot.Add(ItemDropRule.ByCondition(normal, ModContent.ItemType<AlienKeratin>(), 1, alienKeratin, alienKeratin));
        if (enchantmentHeart > 0)
            loot.Add(ItemDropRule.ByCondition(normal, ModContent.ItemType<EnchantmentHeart>(), 1, enchantmentHeart, enchantmentHeart));
    }

    private static void Drop(NPC source, int firstType, int firstStack, int secondType, int secondStack)
    {
        IEntitySource lootSource = source.GetSource_Loot();
        Item.NewItem(lootSource, source.Hitbox, firstType, firstStack);
        Item.NewItem(lootSource, source.Hitbox, secondType, secondStack);
    }
}

public sealed class ProgressionBossCycleSystem : ModSystem
{
    internal static bool TwinsRewardClaimed;
    internal static bool EaterRewardClaimed;
    public override void ClearWorld()
    {
        TwinsRewardClaimed = false;
        EaterRewardClaimed = false;
    }

    internal static void ObserveEaterSpawn(NPC spawning)
    {
        bool anyOtherLivingEaterSegment = false;
        foreach (NPC npc in Main.npc)
            if (npc.whoAmI != spawning.whoAmI && npc.active && npc.life > 0 && ProgressionBossLoot.IsEaterSegment(npc.type))
                anyOtherLivingEaterSegment = true;
        if (!anyOtherLivingEaterSegment)
            EaterRewardClaimed = false;
    }

    internal static void ObserveTwinSpawn(NPC spawning)
    {
        bool anyOtherLivingTwin = false;
        foreach (NPC npc in Main.npc)
            if (npc.whoAmI != spawning.whoAmI && npc.active && npc.life > 0
                && (npc.type == NPCID.Retinazer || npc.type == NPCID.Spazmatism))
                anyOtherLivingTwin = true;
        if (!anyOtherLivingTwin)
            TwinsRewardClaimed = false;
    }
}

public sealed class ProgressionBossBagLoot : GlobalItem
{
    public override void ModifyItemLoot(Item item, ItemLoot itemLoot)
    {
        if (item.type == ItemID.BrainOfCthulhuBossBag || item.type == ItemID.EaterOfWorldsBossBag)
        {
            itemLoot.Add(ItemDropRule.Common(ModContent.ItemType<BlackIronOre>(), 1, 120, 120));
            itemLoot.Add(ItemDropRule.Common(ModContent.ItemType<PinkDiamond>(), 1, 24, 24));
        }
        else if (item.type == ItemID.SkeletronPrimeBossBag)
        {
            itemLoot.Add(ItemDropRule.Common(ModContent.ItemType<TruthSword>(), 1));
            itemLoot.Add(ItemDropRule.Common(ModContent.ItemType<CrystalSkull>(), 1));
        }
        else if (item.type == ItemID.DestroyerBossBag)
        {
            itemLoot.Add(ItemDropRule.Common(ModContent.ItemType<TruthBow>(), 1));
            itemLoot.Add(ItemDropRule.Common(ModContent.ItemType<AlienKeratin>(), 1));
        }
        else if (item.type == ItemID.TwinsBossBag)
        {
            itemLoot.Add(ItemDropRule.Common(ModContent.ItemType<TruthStaff>(), 1));
            itemLoot.Add(ItemDropRule.Common(ModContent.ItemType<ArcaneCore>(), 1));
        }
        else if (item.type == ItemID.WallOfFleshBossBag)
        {
            itemLoot.Add(ItemDropRule.Common(ModContent.ItemType<EnchantmentHeart>(), 1));
        }
        else if (item.type == ItemID.GolemBossBag)
        {
            AddGolemRoleDrop(itemLoot, GolemRewardRole.Warrior, ModContent.ItemType<BrokenSwordHilt>(), ModContent.ItemType<WitherTear>());
            AddGolemRoleDrop(itemLoot, GolemRewardRole.Ranger, ModContent.ItemType<BrokenBowstring>(), ModContent.ItemType<DemonWing>());
            AddGolemRoleDrop(itemLoot, GolemRewardRole.Mage, ModContent.ItemType<BrokenStaffHandle>(), ModContent.ItemType<EnchantedShinbone>());
        }
    }

    private static void AddGolemRoleDrop(ItemLoot loot, GolemRewardRole role, int broken, int core)
    {
        var condition = new GolemRoleLootCondition(role);
        loot.Add(ItemDropRule.ByCondition(condition, broken));
        loot.Add(ItemDropRule.ByCondition(condition, core));
    }
}
