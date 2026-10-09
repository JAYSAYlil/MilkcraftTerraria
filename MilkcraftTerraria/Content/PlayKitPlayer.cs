using System;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;
using MilkcraftTerraria.Content.Items;

namespace MilkcraftTerraria.Content;

/// <summary>Optional first-world starter kit, enabled only by the isolated play launcher.</summary>
public sealed class PlayKitPlayer : ModPlayer
{
    private bool _kitGranted;
    private bool _tier3Tier4KitGranted;
    private bool _passiveKitGranted;
    private bool _balanceKitGranted;
    private bool _mountKitGranted;
    private bool _mountKit0213Granted;
    private static bool Enabled => Environment.GetEnvironmentVariable("MILKCRAFT_TERRARIA_PLAY_KIT") == "1" || Environment.GetEnvironmentVariable("PLAY_KIT") == "1";
    private static bool BalanceKitEnabled => Environment.GetEnvironmentVariable("MILKCRAFT_TERRARIA_BALANCE_PLAY_KIT") == "1";

    public override void OnEnterWorld()
    {
        if ((!Enabled && !BalanceKitEnabled) || Main.netMode == NetmodeID.MultiplayerClient)
            return;

        // Balance-only migration supplies are independent of the older starter
        // kit's early-return flags and are never enabled outside the isolated
        // balance play profile.
        if (BalanceKitEnabled && !_balanceKitGranted)
        {
            _balanceKitGranted = true;
            Player.QuickSpawnItem(Player.GetSource_GiftOrReward(), ItemID.Chest, 20);
            Player.QuickSpawnItem(Player.GetSource_GiftOrReward(), ItemID.SlimeCrown, 20);
            Main.NewText("0.2.10 平衡试玩补给已发放：木箱与史莱姆王召唤物各 20 个。", 220, 190, 120);
        }
        if (!Enabled)
            return;

        Tier3T4ProgressPlayer progress = Player.GetModPlayer<Tier3T4ProgressPlayer>();
        progress.ApplyNetworkState(true, true, progress.HasT3ChampionTitle);
        if (!_mountKitGranted)
        {
            _mountKitGranted = true;
            Player.QuickSpawnItem(Player.GetSource_GiftOrReward(), ModContent.ItemType<UndeadWarhorseSummon>());
            Player.QuickSpawnItem(Player.GetSource_GiftOrReward(), ModContent.ItemType<CelestialTenguSummon>());
            Main.NewText("0.2.12 坐骑试玩补给已发放：亡灵军马与圣天狗召唤物各 1 件；已放入背包，空间不足时会掉在脚边。", 220, 190, 120);
        }
        if (!_mountKit0213Granted)
        {
            _mountKit0213Granted = true;
            Player.QuickSpawnItem(Player.GetSource_GiftOrReward(), ModContent.ItemType<WhiteTigerSummon>());
            Player.QuickSpawnItem(Player.GetSource_GiftOrReward(), ModContent.ItemType<WhiteNineTailedFoxSummon>());
            Player.QuickSpawnItem(Player.GetSource_GiftOrReward(), ModContent.ItemType<RhinoSummon>());
            Player.QuickSpawnItem(Player.GetSource_GiftOrReward(), ModContent.ItemType<FlameWarhorseSummon>());
            Player.QuickSpawnItem(Player.GetSource_GiftOrReward(), ModContent.ItemType<WhiteUnicornSummon>());
            Main.NewText("0.2.13 坐骑试玩补给已发放：白虎、白色九尾狐、犀牛、烈焰战马、白色独角兽各 1 件；空间不足时掉在脚边。", 220, 190, 120);
        }
        if (_kitGranted && _tier3Tier4KitGranted && _passiveKitGranted) return;

        bool grantOriginalKit = !_kitGranted;
        bool grantLateTierKit = !_tier3Tier4KitGranted;
        _kitGranted = true;
        _tier3Tier4KitGranted = true;
        foreach (ModItem content in ModContent.GetInstance<MilkcraftTerrariaMod>().GetContent<ModItem>())
        {
            if (content is IronQuiver or UndeadWarhorseSummon or CelestialTenguSummon
                or WhiteTigerSummon or WhiteNineTailedFoxSummon or RhinoSummon or FlameWarhorseSummon or WhiteUnicornSummon) continue;
            bool isLateTier = content is Tier3T4ArmorItem or Tier3T4WeaponItem or Tier3T4AccessoryItem or
                Tier3T4MaterialItem or TierEquipmentLicenseItem;
            if (isLateTier ? !grantLateTierKit : !grantOriginalKit)
                continue;
            int stack = content is MilkcraftMaterial ? 20 : 1;
            Player.QuickSpawnItem(Player.GetSource_GiftOrReward(), content.Type, stack);
        }
        if (grantOriginalKit)
        {
            Player.QuickSpawnItem(Player.GetSource_GiftOrReward(), ItemID.CopperPickaxe);
            Player.QuickSpawnItem(Player.GetSource_GiftOrReward(), ItemID.CopperHammer);
            Player.QuickSpawnItem(Player.GetSource_GiftOrReward(), ItemID.WoodenArrow, 999);
            Player.QuickSpawnItem(Player.GetSource_GiftOrReward(), ItemID.ManaCrystal, 5);
        }

        if (!_passiveKitGranted)
        {
            _passiveKitGranted = true;
            Player.QuickSpawnItem(Player.GetSource_GiftOrReward(), ModContent.ItemType<IronQuiver>());
            Player.QuickSpawnItem(Player.GetSource_GiftOrReward(), ItemID.ManaPotion, 12);
        }

        // The isolated play profile is a visual review harness, so grant both
        // persistent tier permissions without changing any world boss flags.
        // This environment variable is set only by tools/play-mod.ps1.
        Main.NewText(grantOriginalKit
            ? "奶块试玩物资已发放：本模组装备、材料与工作站各一份；T3/T4许可已解锁。"
            : grantLateTierKit
                ? "奶块 T3/T4 新增装备与材料已发放；试玩角色许可已解锁。"
                : "0.2.5 试玩补给已发放：皮质箭袋与魔力药水。", 220, 190, 120);
    }

    public override void SaveData(TagCompound tag)
    {
        tag["milkcraftPlayKitGranted"] = _kitGranted;
        tag["milkcraftT3T4PlayKitGranted"] = _tier3Tier4KitGranted;
        tag["milkcraft025PassiveKitGranted"] = _passiveKitGranted;
        tag["milkcraft0210BalanceKitGranted"] = _balanceKitGranted;
        tag["milkcraft0212MountKitGranted"] = _mountKitGranted;
        tag["milkcraft0213MountKitGranted"] = _mountKit0213Granted;
    }

    public override void LoadData(TagCompound tag)
    {
        _kitGranted = tag.GetBool("milkcraftPlayKitGranted");
        _tier3Tier4KitGranted = tag.GetBool("milkcraftT3T4PlayKitGranted");
        _passiveKitGranted = tag.GetBool("milkcraft025PassiveKitGranted");
        _balanceKitGranted = tag.GetBool("milkcraft0210BalanceKitGranted");
        _mountKitGranted = tag.GetBool("milkcraft0212MountKitGranted");
        _mountKit0213Granted = tag.GetBool("milkcraft0213MountKitGranted");
    }
}
