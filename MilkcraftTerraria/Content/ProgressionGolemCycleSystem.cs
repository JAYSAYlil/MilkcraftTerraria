using System.Collections.Generic;
using System.IO;
using System;
using Terraria.Localization;
using Terraria;
using Terraria.DataStructures;
using Terraria.GameContent.ItemDropRules;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;
using MilkcraftTerraria.Content.Items;

namespace MilkcraftTerraria.Content;

internal enum GolemRewardRole : byte
{
    None,
    Warrior,
    Ranger,
    Mage
}

/// <summary>One server-owned role per complete Golem kill; opening a bag never advances it.</summary>
public sealed class ProgressionGolemCycleSystem : ModSystem
{
    private const string NextRoleKey = "MilkcraftGolemNextRole";
    private byte _nextRole;
    private GolemRewardRole _preparedRole;

    public override void ClearWorld()
    {
        _nextRole = 0;
        _preparedRole = GolemRewardRole.None;
    }

    public override void SaveWorldData(TagCompound tag) => tag[NextRoleKey] = _nextRole;
    public override void LoadWorldData(TagCompound tag)
    {
        _nextRole = (byte)(tag.ContainsKey(NextRoleKey) ? tag.GetByte(NextRoleKey) % 3 : 0);
        _preparedRole = GolemRewardRole.None;
    }

    public override void NetSend(BinaryWriter writer) => writer.Write(_nextRole);
    public override void NetReceive(BinaryReader reader)
    {
        _nextRole = (byte)(reader.ReadByte() % 3);
        _preparedRole = GolemRewardRole.None;
    }

    internal GolemRewardRole PrepareKill()
    {
        if (_preparedRole == GolemRewardRole.None)
            _preparedRole = (GolemRewardRole)(_nextRole + 1);
        return _preparedRole;
    }

    internal GolemRewardRole CurrentPreparedRole => _preparedRole;

    internal GolemRewardRole CompleteKill()
    {
        GolemRewardRole role = _preparedRole == GolemRewardRole.None ? PrepareKill() : _preparedRole;
        _nextRole = (byte)(((byte)role) % 3);
        _preparedRole = GolemRewardRole.None;
        return role;
    }

    internal static (int Broken, int Core) RewardFor(GolemRewardRole role) => role switch
    {
        GolemRewardRole.Warrior => (ModContent.ItemType<BrokenSwordHilt>(), ModContent.ItemType<WitherTear>()),
        GolemRewardRole.Ranger => (ModContent.ItemType<BrokenBowstring>(), ModContent.ItemType<DemonWing>()),
        GolemRewardRole.Mage => (ModContent.ItemType<BrokenStaffHandle>(), ModContent.ItemType<EnchantedShinbone>()),
        _ => (0, 0)
    };
}

/// <summary>
/// tML returns early for vanilla OpenableBag items before querying GlobalItem.CanRightClick.
/// Temporarily route the Golem bag through its normal GlobalItem hook so each bag can bind
/// its persisted role before the unchanged ItemSlot/ItemDropDatabase open path runs.
/// </summary>
public sealed class ProgressionGolemBagRoutingSystem : ModSystem
{
    private bool _oldOpenableBag;
    public override void PostSetupContent()
    {
        _oldOpenableBag = ItemID.Sets.OpenableBag[ItemID.GolemBossBag];
        ItemID.Sets.OpenableBag[ItemID.GolemBossBag] = false;
    }

    public override void Unload()
        => ItemID.Sets.OpenableBag[ItemID.GolemBossBag] = _oldOpenableBag;
}

/// <summary>Stores the assigned role on each vanilla bag, including save and network state.</summary>
public sealed class ProgressionGolemBagRole : GlobalItem
{
    public override bool InstancePerEntity => true;
    internal GolemRewardRole Role;

    public override GlobalItem Clone(Item from, Item to)
    {
        var clone = (ProgressionGolemBagRole)base.Clone(from, to);
        clone.Role = Role;
        return clone;
    }

    public override void OnSpawn(Item item, IEntitySource source)
    {
        if (item.type != ItemID.GolemBossBag || source is not EntitySource_Loot loot || loot.Entity is not NPC npc || npc.type != NPCID.Golem)
            return;
        Role = ModContent.GetInstance<ProgressionGolemCycleSystem>().CurrentPreparedRole;
    }

    public override void SaveData(Item item, TagCompound tag)
    {
        if (Role != GolemRewardRole.None)
            tag["MilkcraftGolemBagRole"] = (byte)Role;
    }

    public override void LoadData(Item item, TagCompound tag)
        => Role = tag.ContainsKey("MilkcraftGolemBagRole") ? (GolemRewardRole)tag.GetByte("MilkcraftGolemBagRole") : GolemRewardRole.None;

    public override void NetSend(Item item, BinaryWriter writer) => writer.Write((byte)Role);
    public override void NetReceive(Item item, BinaryReader reader) => Role = (GolemRewardRole)reader.ReadByte();

    public override void ModifyTooltips(Item item, List<TooltipLine> tooltips)
    {
        if (item.type != ItemID.GolemBossBag || Role == GolemRewardRole.None)
            return;
        string roleKey = Role switch
        {
            GolemRewardRole.Warrior => "Warrior",
            GolemRewardRole.Ranger => "Ranger",
            GolemRewardRole.Mage => "Mage",
            _ => null
        };
        if (roleKey is null)
            return;
        tooltips.Add(new TooltipLine(Mod, "MilkcraftGolemBonus",
            Language.GetTextValue($"Mods.MilkcraftTerraria.GolemBagBonus.{roleKey}")));
    }

    public override bool CanStack(Item destination, Item source)
        => destination.GetGlobalItem<ProgressionGolemBagRole>().Role == source.GetGlobalItem<ProgressionGolemBagRole>().Role;

    public override bool CanStackInWorld(Item destination, Item source)
        => destination.GetGlobalItem<ProgressionGolemBagRole>().Role == source.GetGlobalItem<ProgressionGolemBagRole>().Role;

    public override bool CanRightClick(Item item)
    {
        if (item.type != ItemID.GolemBossBag)
            return false;
        ProgressionGolemBagOpeningPlayer.SetRole(Role);
        // ItemSlot.RightClick queries this hook as part of its actual vanilla
        // OpenableBag path; returning true preserves bag opening after tagging
        // the role for the same-frame loot-condition evaluation.
        return true;
    }
}

public sealed class ProgressionGolemBagOpeningPlayer : ModPlayer
{
    private static GolemRewardRole _role;
    private static ulong _setFrame;
    private static int _rightClickQueryCount;
    internal static void SetRole(GolemRewardRole role)
    {
        _role = role;
        _setFrame = Main.GameUpdateCount;
        _rightClickQueryCount++;
    }

    internal static bool HasRole(GolemRewardRole role)
        => _role == role && _setFrame == Main.GameUpdateCount;

    internal static int RightClickQueryCount => _rightClickQueryCount;

    public override void PostUpdate()
    {
        if (_setFrame != Main.GameUpdateCount)
            _role = GolemRewardRole.None;
    }
}

internal sealed class GolemRoleLootCondition : IItemDropRuleCondition
{
    private readonly GolemRewardRole _role;
    internal GolemRoleLootCondition(GolemRewardRole role) => _role = role;
    public bool CanDrop(DropAttemptInfo info)
        => info.player is not null && ProgressionGolemBagOpeningPlayer.HasRole(_role);
    public bool CanShowItemDropInUI() => false;
    public string GetConditionDescription() => "";
}
