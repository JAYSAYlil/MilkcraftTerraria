using Terraria;
using Terraria.GameContent.ItemDropRules;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;
using MilkcraftTerraria.Content.Tiles;
using System.Collections.Generic;

namespace MilkcraftTerraria.Content.Items;

public abstract class BossSummonItem : ModItem
{
    protected abstract int BossType { get; }
    protected abstract bool Progress(Player p);
    protected abstract bool Location(Player p);
    public override void SetDefaults()
    {
        Item.width = 26; Item.height = 32; Item.maxStack = Item.CommonMaxStack; Item.rare = ItemRarityID.LightPurple;
        Item.value = Item.sellPrice(gold: 2); Item.useStyle = ItemUseStyleID.HoldUp;
        Item.useTime = Item.useAnimation = 30; Item.UseSound = SoundID.Roar; Item.consumable = true;
        Item.noMelee = true; Item.noUseGraphic = false;
    }
    public override bool CanUseItem(Player player)
    {
        if (!player.active || player.dead) return false;
        if (!Progress(player) || !Location(player)) return false;
        for (int i = 0; i < Main.maxNPCs; i++)
            if (Main.npc[i].active && Main.npc[i].type == BossType) return false;
        return true;
    }

    // In multiplayer the server owns the summon transaction. The client sends the
    // request but must not also decrement its local stack; the server validates,
    // creates the boss, decrements exactly the held stack once, then syncs it.
    public override bool ConsumeItem(Player player)
        => Main.netMode == NetmodeID.SinglePlayer;
    public override bool? UseItem(Player player)
    {
        if (Main.netMode == NetmodeID.MultiplayerClient)
        {
            var packet = ModContent.GetInstance<MilkcraftTerraria.MilkcraftTerrariaMod>().GetPacket();
            packet.Write(MilkcraftTerraria.MilkcraftTerrariaMod.BossSummonRequestMessage);
            packet.Write((byte)player.whoAmI);
            packet.Write((short)BossType);
            packet.Send();
            return true;
        }
        // In multiplayer the request handler is the only server-authoritative
        // spawn-and-charge path; a replicated ItemCheck must not spawn twice.
        if (Main.netMode == NetmodeID.Server)
            return false;
        // NPC.NewNPC can fail when the pool is full. A failed spawn returns false
        // so the native item-use path does not consume the summon.
        return Spawn(player);
    }
    internal bool Spawn(Player player)
    {
        if (Main.netMode == NetmodeID.MultiplayerClient || !CanUseItem(player)) return false;
        float empowered = BossType == ModContent.NPCType<CastleCoreBoss>() && Main.dayTime ? 1f : 0f;
        int index = NPC.NewNPC(player.GetSource_ItemUse(Item), (int)player.Center.X, (int)player.Center.Y - 150,
            BossType, ai3: empowered);
        if (index < 0 || index >= Main.maxNPCs) return false;
        Main.npc[index].netUpdate = true;
        return true;
    }

    internal static bool TrySpawnFromServer(Player player, int bossType)
    {
        foreach (BossSummonItem summon in ModContent.GetContent<BossSummonItem>())
        {
            if (summon.BossType != bossType || !player.active || player.dead
                || player.HeldItem.type != summon.Type || player.HeldItem.stack <= 0
                || !summon.CanUseItem(player)) continue;
            // Recheck immediately before the authoritative spawn, then charge only
            // after NPC.NewNPC succeeds. A duplicate packet sees the active boss and
            // exits without another charge.
            if (!summon.Spawn(player)) return false;
            Item held = player.HeldItem;
            if (held.type != summon.Type || held.stack <= 0) return false;
            held.stack--;
            if (held.stack <= 0) held.TurnToAir();
            if (Main.netMode == NetmodeID.Server)
                NetMessage.SendData(MessageID.SyncEquipment, -1, -1, null, player.whoAmI, player.selectedItem);
            return true;
        }
        return false;
    }
}

public abstract class DuskSummonItem : BossSummonItem
{
    protected override bool Progress(Player p) => NPC.downedBoss2;
    protected override bool Location(Player p) => p.ZoneOverworldHeight;
}
public abstract class HardmodeSummonItem : BossSummonItem
{
    protected override bool Progress(Player p) => Main.hardMode;
    protected override bool Location(Player p) => Main.hardMode && p.ZoneOverworldHeight;
}
public sealed class WraithWarriorSummon : DuskSummonItem
{
    protected override int BossType => ModContent.NPCType<WraithWarriorBoss>();
    protected override bool Location(Player p) => p.ZoneOverworldHeight && !Main.dayTime;
}
public sealed class JungleLordSummon : DuskSummonItem
{
    protected override int BossType => ModContent.NPCType<JungleLordBoss>();
    protected override bool Location(Player p) => p.ZoneOverworldHeight && p.ZoneJungle;
}
public sealed class BansheeSummon : DuskSummonItem
{
    protected override int BossType => ModContent.NPCType<BansheeBoss>();
    protected override bool Location(Player p) => p.ZoneOverworldHeight && !Main.dayTime;
}
public sealed class ObsidianGuardianSummon : HardmodeSummonItem
{
    protected override int BossType => ModContent.NPCType<ObsidianGuardianBoss>();
    protected override bool Location(Player p) => Main.hardMode && p.ZoneOverworldHeight && p.ZoneSnow;
}
public sealed class SphinxSummon : HardmodeSummonItem
{
    protected override int BossType => ModContent.NPCType<SphinxBoss>();
    protected override bool Location(Player p) => Main.hardMode && p.ZoneOverworldHeight && p.ZoneDesert;
}
public sealed class FrostWitchSummon : HardmodeSummonItem
{
    protected override int BossType => ModContent.NPCType<FrostWitchBoss>();
    protected override bool Location(Player p) => Main.hardMode && p.ZoneOverworldHeight && p.ZoneSnow;
}
public sealed class CastleCoreSummon : BossSummonItem
{
    protected override int BossType => ModContent.NPCType<CastleCoreBoss>();
    protected override bool Progress(Player p) => NPC.downedGolemBoss;
    protected override bool Location(Player p) => NPC.downedGolemBoss && p.ZoneOverworldHeight;
    public override void ModifyTooltips(List<TooltipLine> tooltips)
    {
        tooltips.Add(new TooltipLine(Mod, "EmpoweredUnicornGuarantee",
            Language.GetTextValue("Mods.MilkcraftTerraria.CastleCoreSummon.EmpoweredUnicornGuarantee")));
    }
}

public abstract class MilkcraftBossBag : ModItem
{
    protected abstract MilkcraftBossKind Kind { get; }
    public override bool CanRightClick()
    {
        // ItemLoader.CanRightClick returns immediately for a ModItem that allows right-click,
        // before GlobalItem.CanRightClick runs. Capture the Castle Core bag state here, at the
        // actual native ItemSlot query that precedes the bag's loot roll.
        if (Kind == MilkcraftBossKind.CastleCore)
        {
            CastleCoreBagOpenContext.Clear();
            CastleCoreBagOpenContext.Set(Item,
                Item.GetGlobalItem<CastleCoreBagItemState>().EmpoweredAtKill);
        }
        return true;
    }
    public override void SetStaticDefaults() => ItemID.Sets.BossBag[Type] = true;
    public override void SetDefaults()
    {
        Item.width = Item.height = 32; Item.maxStack = Item.CommonMaxStack;
        Item.rare = ItemRarityID.Expert; Item.consumable = true; Item.expert = true;
    }
    public override void ModifyTooltips(List<TooltipLine> tooltips)
    {
        if (Kind != MilkcraftBossKind.CastleCore) return;
        string key = Item.GetGlobalItem<CastleCoreBagItemState>().EmpoweredAtKill
            ? "Mods.MilkcraftTerraria.CastleCoreBag.EmpoweredTooltip"
            : "Mods.MilkcraftTerraria.CastleCoreBag.NormalTooltip";
        tooltips.Add(new TooltipLine(Mod, "UnicornReward", Language.GetTextValue(key)));
    }
    public override void ModifyItemLoot(ItemLoot loot)
    {
        loot.Add(ItemDropRule.CoinsBasedOnNPCValue(BossItemTypes.NpcType(Kind)));
        foreach (var reward in BossItemTypes.FixedRewards(Kind))
            loot.Add(ItemDropRule.Common(reward.Type, 1, reward.Count, reward.Count));
        // Expert and Master both use the same single 1-in-4 bag roll, independent
        // of the world difficulty where the bag is later opened.
        loot.Add(ItemDropRule.Common(BossItemTypes.Pet(Kind), 4));
        loot.Add(ItemDropRule.Common(BossItemTypes.Mask(Kind), 7));
        if (Kind is MilkcraftBossKind.WraithWarrior or MilkcraftBossKind.JungleLord or MilkcraftBossKind.Banshee)
            loot.Add(ItemDropRule.Common(ModContent.ItemType<WhiteTigerSummon>(), 10));
        if (Kind == MilkcraftBossKind.FrostWitch)
            loot.Add(ItemDropRule.Common(ModContent.ItemType<WhiteDodoSummon>(), 4));
        if (Kind == MilkcraftBossKind.CastleCore)
        {
            loot.Add(ItemDropRule.ByCondition(new CastleCoreBagStateCondition(empowered: true), ModContent.ItemType<WhiteUnicornSummon>(), 1));
            loot.Add(ItemDropRule.ByCondition(new CastleCoreBagStateCondition(empowered: false), ModContent.ItemType<WhiteUnicornSummon>(), 4));
        }
    }
}

internal sealed class CastleCoreBagStateCondition : IItemDropRuleCondition
{
    private readonly bool _empowered;
    internal CastleCoreBagStateCondition(bool empowered) => _empowered = empowered;
    public bool CanDrop(DropAttemptInfo info)
    {
        return CastleCoreBagOpenContext.Matches(info.item, info.player, _empowered);
    }
    public bool CanShowItemDropInUI() => true;
    public string GetConditionDescription() => Language.GetTextValue(_empowered
        ? "Mods.MilkcraftTerraria.Conditions.EmpoweredCastleCore"
        : "Mods.MilkcraftTerraria.Conditions.NormalCastleCore");
}
public sealed class WraithWarriorBag : MilkcraftBossBag { protected override MilkcraftBossKind Kind => MilkcraftBossKind.WraithWarrior; }
public sealed class JungleLordBag : MilkcraftBossBag { protected override MilkcraftBossKind Kind => MilkcraftBossKind.JungleLord; }
public sealed class BansheeBag : MilkcraftBossBag { protected override MilkcraftBossKind Kind => MilkcraftBossKind.Banshee; }
public sealed class ObsidianGuardianBag : MilkcraftBossBag { protected override MilkcraftBossKind Kind => MilkcraftBossKind.ObsidianGuardian; }
public sealed class SphinxBag : MilkcraftBossBag { protected override MilkcraftBossKind Kind => MilkcraftBossKind.Sphinx; }
public sealed class FrostWitchBag : MilkcraftBossBag { protected override MilkcraftBossKind Kind => MilkcraftBossKind.FrostWitch; }
public sealed class CastleCoreBag : MilkcraftBossBag { protected override MilkcraftBossKind Kind => MilkcraftBossKind.CastleCore; }

public sealed class CastleCoreBagItemState : GlobalItem
{
    public override bool InstancePerEntity => true;
    internal bool EmpoweredAtKill;
    public override GlobalItem Clone(Item from, Item to)
    { var clone = (CastleCoreBagItemState)base.Clone(from, to); clone.EmpoweredAtKill = EmpoweredAtKill; return clone; }
    public override void OnSpawn(Item item, Terraria.DataStructures.IEntitySource source)
    {
        if (item.type == ModContent.ItemType<CastleCoreBag>() && source is Terraria.DataStructures.EntitySource_Loot loot
            && loot.Entity is NPC npc && npc.type == ModContent.NPCType<CastleCoreBoss>())
            EmpoweredAtKill = npc.ai[3] > 0.5f;
    }
    public override void SaveData(Item item, TagCompound tag)
    { if (item.type == ModContent.ItemType<CastleCoreBag>() && EmpoweredAtKill) tag["MilkcraftCastleEmpoweredBag"] = true; }
    public override void LoadData(Item item, TagCompound tag)
    { EmpoweredAtKill = item.type == ModContent.ItemType<CastleCoreBag>() && tag.GetBool("MilkcraftCastleEmpoweredBag"); }
    public override void NetSend(Item item, System.IO.BinaryWriter writer) => writer.Write(EmpoweredAtKill);
    public override void NetReceive(Item item, System.IO.BinaryReader reader) => EmpoweredAtKill = reader.ReadBoolean();
    public override bool CanStack(Item destination, Item source) => destination.type != ModContent.ItemType<CastleCoreBag>() ||
        destination.GetGlobalItem<CastleCoreBagItemState>().EmpoweredAtKill == source.GetGlobalItem<CastleCoreBagItemState>().EmpoweredAtKill;
    public override bool CanStackInWorld(Item destination, Item source) => CanStack(destination, source);
    public override bool CanRightClick(Item item)
    {
        if (item.type != ModContent.ItemType<CastleCoreBag>()) return false;
        // Every actual Castle Core bag query starts a fresh scope, including legacy/unmarked bags.
        // Queries for unrelated right-clickable items must not erase this bag's in-flight roll.
        CastleCoreBagOpenContext.Clear();
        CastleCoreBagOpenContext.Set(item, EmpoweredAtKill);
        return true;
    }
}

internal static class CastleCoreBagOpenContext
{
    private static Item _item;
    private static bool _empowered;
    private static ulong _frame;
    private static Player _player;
    private static int _inventorySlot = -1;
    internal static string LastMatchReport { get; private set; } = "not evaluated";
    internal static void Clear() { _item = null; _player = null; _inventorySlot = -1; _empowered = false; _frame = 0; }
    internal static void Set(Item item, bool empowered)
    {
        Clear();
        _item = item;
        _empowered = empowered;
        _frame = Main.GameUpdateCount;
        _player = Main.LocalPlayer;
        if (_player != null)
            for (int i = 0; i < _player.inventory.Length; i++)
                if (ReferenceEquals(_player.inventory[i], item)) { _inventorySlot = i; break; }
    }
    internal static bool Matches(int itemType, Player player, bool empowered)
    {
        bool match = _frame == Main.GameUpdateCount && _item != null && itemType == ModContent.ItemType<CastleCoreBag>()
            && _empowered == empowered && ReferenceEquals(player, _player) && _inventorySlot >= 0
            && _inventorySlot < player.inventory.Length && ReferenceEquals(player.inventory[_inventorySlot], _item);
        LastMatchReport = $"match={match};type={itemType};empowered={empowered}/{_empowered};frame={Main.GameUpdateCount}/{_frame};" +
            $"player={player?.whoAmI}/{_player?.whoAmI};samePlayer={ReferenceEquals(player, _player)};slot={_inventorySlot};" +
            $"sameItem={_player != null && _inventorySlot >= 0 && _inventorySlot < _player.inventory.Length && ReferenceEquals(_player.inventory[_inventorySlot], _item)}";
        return match;
    }
}

public abstract class BossProofItem : ModItem
{
    public override void SetDefaults() { Item.width = 24; Item.height = 28; Item.maxStack = Item.CommonMaxStack; Item.rare = ItemRarityID.Orange; Item.value = Item.sellPrice(silver: 10); }
}
public sealed class WraithWarriorProof : BossProofItem { }
public sealed class JungleLordProof : BossProofItem { }
public sealed class BansheeProof : BossProofItem { }
public sealed class CastleCoreClearSeal : BossProofItem { public override void SetDefaults() { base.SetDefaults(); Item.rare = ItemRarityID.LightPurple; } }

[AutoloadEquip(EquipType.Head)] public sealed class WraithWarriorMask : ModItem { public override void SetStaticDefaults() => ArmorIDs.Head.Sets.DrawHead[Item.headSlot] = true; public override void SetDefaults() => BossMaskDefaults.Setup(Item, 36, 34); }
[AutoloadEquip(EquipType.Head)] public sealed class JungleLordMask : ModItem { public override void SetStaticDefaults() => ArmorIDs.Head.Sets.DrawHead[Item.headSlot] = true; public override void SetDefaults() => BossMaskDefaults.Setup(Item, 34, 30); }
[AutoloadEquip(EquipType.Head)] public sealed class BansheeMask : ModItem { public override void SetStaticDefaults() => ArmorIDs.Head.Sets.DrawHead[Item.headSlot] = true; public override void SetDefaults() => BossMaskDefaults.Setup(Item, 28, 34); }
[AutoloadEquip(EquipType.Head)] public sealed class ObsidianGuardianMask : ModItem { public override void SetStaticDefaults() => ArmorIDs.Head.Sets.DrawHead[Item.headSlot] = true; public override void SetDefaults() => BossMaskDefaults.Setup(Item, 34, 36); }
[AutoloadEquip(EquipType.Head)] public sealed class SphinxMask : ModItem { public override void SetStaticDefaults() => ArmorIDs.Head.Sets.DrawHead[Item.headSlot] = true; public override void SetDefaults() => BossMaskDefaults.Setup(Item, 36, 32); }
[AutoloadEquip(EquipType.Head)] public sealed class FrostWitchMask : ModItem { public override void SetStaticDefaults() => ArmorIDs.Head.Sets.DrawHead[Item.headSlot] = true; public override void SetDefaults() => BossMaskDefaults.Setup(Item, 30, 38); }
[AutoloadEquip(EquipType.Head)] public sealed class CastleCoreMask : ModItem { public override void SetStaticDefaults() => ArmorIDs.Head.Sets.DrawHead[Item.headSlot] = true; public override void SetDefaults() => BossMaskDefaults.Setup(Item, 36, 36); }
internal static class BossMaskDefaults { internal static void Setup(Item item, int width, int height) { item.width = width; item.height = height; item.maxStack = 1; item.rare = ItemRarityID.Blue; item.vanity = true; } }

public abstract class BossDisplayItem : ModItem
{
    protected abstract int TileType { get; }
    protected virtual int Rarity => ItemRarityID.Blue;
    protected virtual bool Relic => false;
    public override void SetDefaults()
    {
        Item.DefaultToPlaceableTile(TileType);
        Item.width = Item.height = 32;
        Item.maxStack = Relic ? 99 : Item.CommonMaxStack;
        Item.rare = Rarity;
        Item.consumable = true;
    }
}
public sealed class WraithWarriorTrophy : BossDisplayItem { protected override int TileType => ModContent.TileType<WraithWarriorTrophyTile>(); }
public sealed class JungleLordTrophy : BossDisplayItem { protected override int TileType => ModContent.TileType<JungleLordTrophyTile>(); }
public sealed class BansheeTrophy : BossDisplayItem { protected override int TileType => ModContent.TileType<BansheeTrophyTile>(); }
public sealed class ObsidianGuardianTrophy : BossDisplayItem { protected override int TileType => ModContent.TileType<ObsidianGuardianTrophyTile>(); }
public sealed class SphinxTrophy : BossDisplayItem { protected override int TileType => ModContent.TileType<SphinxTrophyTile>(); }
public sealed class FrostWitchTrophy : BossDisplayItem { protected override int TileType => ModContent.TileType<FrostWitchTrophyTile>(); }
public sealed class CastleCoreTrophy : BossDisplayItem { protected override int TileType => ModContent.TileType<CastleCoreTrophyTile>(); }
public sealed class WraithWarriorRelic : BossDisplayItem { protected override int TileType => ModContent.TileType<WraithWarriorRelicTile>(); protected override int Rarity => ItemRarityID.Master; protected override bool Relic => true; }
public sealed class JungleLordRelic : BossDisplayItem { protected override int TileType => ModContent.TileType<JungleLordRelicTile>(); protected override int Rarity => ItemRarityID.Master; protected override bool Relic => true; }
public sealed class BansheeRelic : BossDisplayItem { protected override int TileType => ModContent.TileType<BansheeRelicTile>(); protected override int Rarity => ItemRarityID.Master; protected override bool Relic => true; }
public sealed class ObsidianGuardianRelic : BossDisplayItem { protected override int TileType => ModContent.TileType<ObsidianGuardianRelicTile>(); protected override int Rarity => ItemRarityID.Master; protected override bool Relic => true; }
public sealed class SphinxRelic : BossDisplayItem { protected override int TileType => ModContent.TileType<SphinxRelicTile>(); protected override int Rarity => ItemRarityID.Master; protected override bool Relic => true; }
public sealed class FrostWitchRelic : BossDisplayItem { protected override int TileType => ModContent.TileType<FrostWitchRelicTile>(); protected override int Rarity => ItemRarityID.Master; protected override bool Relic => true; }
public sealed class CastleCoreRelic : BossDisplayItem { protected override int TileType => ModContent.TileType<CastleCoreRelicTile>(); protected override int Rarity => ItemRarityID.Master; protected override bool Relic => true; }
