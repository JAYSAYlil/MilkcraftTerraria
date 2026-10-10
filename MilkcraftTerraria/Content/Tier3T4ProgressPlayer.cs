using Terraria;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;
using MilkcraftTerraria.Content.Items;

namespace MilkcraftTerraria.Content;

/// <summary>
/// Character-owned progression for the T3/T4 equipment. The world only gates
/// crafting and first activation; an already licensed character keeps access
/// when visiting an earlier world.
/// </summary>
public sealed class Tier3T4ProgressPlayer : ModPlayer
{
    private const string T3Key = "tier3EquipmentLicense";
    private const string T4Key = "tier4EquipmentLicense";
    private const string ChampionKey = "tier3ChampionTitle";

    public bool HasT3License { get; private set; }
    public bool HasT4License { get; private set; }
    public bool HasT3ChampionTitle { get; private set; }

    public bool HasLicense(int tier) => tier switch
    {
        3 => HasT3License,
        4 => HasT4License,
        _ => false
    };

    public static bool WorldAllowsTier(int tier) => tier switch
    {
        3 => NPC.downedMechBossAny,
        4 => NPC.downedGolemBoss,
        _ => false
    };

    public static bool WorldGrantsT3ChampionTitle =>
        MilkcraftBossWorldSystem.ObsidianGuardianDowned && MilkcraftBossWorldSystem.SphinxDowned
        && MilkcraftBossWorldSystem.FrostWitchDowned;

    internal bool ActivateLicense(int tier)
    {
        if (!WorldAllowsTier(tier) || HasLicense(tier))
            return false;

        if (tier == 3)
            HasT3License = true;
        else if (tier == 4)
            HasT4License = true;
        else
            return false;

        return true;
    }

    public override void PreUpdate()
    {
        if (!HasT3ChampionTitle && WorldGrantsT3ChampionTitle)
        {
            HasT3ChampionTitle = true;
            if (Player.whoAmI == Main.myPlayer && Main.netMode != NetmodeID.Server)
                Main.NewText(Language.GetTextValue("Mods.MilkcraftTerraria.Progression.T3ChampionUnlocked"), 255, 220, 96);
        }
    }

    public override void PostUpdateEquips()
    {
        // Armor has no general CanEquip hook. Keep it visually wearable, but
        // remove its defense as well as its UpdateEquip effects until the
        // character has activated that tier's permanent license.
        for (int slot = 0; slot < 3; slot++)
        {
            Item equipped = Player.armor[slot];
            if (equipped.ModItem is Tier3T4ArmorItem armor && !HasLicense(armor.Tier))
                Player.statDefense -= equipped.defense;
        }
    }

    public override void SaveData(TagCompound tag)
    {
        if (HasT3License) tag[T3Key] = true;
        if (HasT4License) tag[T4Key] = true;
        if (HasT3ChampionTitle) tag[ChampionKey] = true;
    }

    public override void LoadData(TagCompound tag)
    {
        HasT3License = tag.GetBool(T3Key);
        HasT4License = tag.GetBool(T4Key);
        HasT3ChampionTitle = tag.GetBool(ChampionKey);
    }

    public override void CopyClientState(ModPlayer targetCopy)
    {
        var clone = (Tier3T4ProgressPlayer)targetCopy;
        clone.HasT3License = HasT3License;
        clone.HasT4License = HasT4License;
        clone.HasT3ChampionTitle = HasT3ChampionTitle;
    }

    public override void SendClientChanges(ModPlayer clientPlayer)
    {
        var old = (Tier3T4ProgressPlayer)clientPlayer;
        if (old.HasT3License != HasT3License || old.HasT4License != HasT4License || old.HasT3ChampionTitle != HasT3ChampionTitle)
            MilkcraftTerrariaMod.SendProgressPacket(this, -1, Player.whoAmI, initialSync: false);
    }

    public override void SyncPlayer(int toWho, int fromWho, bool newPlayer)
    {
        if (Main.netMode == NetmodeID.MultiplayerClient)
            MilkcraftTerrariaMod.SendProgressPacket(this, toWho, fromWho, initialSync: true);
        else if (Main.netMode == NetmodeID.Server)
            MilkcraftTerrariaMod.SendProgressPacket(this, toWho, fromWho, initialSync: false);
    }

    internal void ApplyNetworkState(bool t3, bool t4, bool champion)
    {
        HasT3License = t3;
        HasT4License = t4;
        HasT3ChampionTitle = champion;
    }

    internal void ApplyClientProgress(bool t3, bool t4, bool champion)
    {
        // Runtime client updates may only add a license after the current
        // world has reached its stage. Previously saved licenses remain valid.
        if (t3 && !HasT3License && WorldAllowsTier(3)) HasT3License = true;
        if (t4 && !HasT4License && WorldAllowsTier(4)) HasT4License = true;
        if (champion && WorldGrantsT3ChampionTitle) HasT3ChampionTitle = true;
    }
}
