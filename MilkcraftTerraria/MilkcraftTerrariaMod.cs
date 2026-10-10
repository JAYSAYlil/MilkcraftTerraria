using System.IO;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.ModLoader;
using MilkcraftTerraria.Content;
using MilkcraftTerraria.Content.Items;

namespace MilkcraftTerraria;

public sealed class MilkcraftTerrariaMod : Mod
{
    private const byte TierProgressMessage = 1;
    internal const byte AlchemyActivationRequestMessage = 2;
    internal const byte AlchemyActivationSyncMessage = 3;
    internal const byte EnchantApplyRequestMessage = 4;
    internal const byte EnchantApplyResultMessage = 5;
    internal const byte EnchantBankSyncMessage = 6;
    internal const byte BossSummonRequestMessage = 7;

    internal static void SendProgressPacket(Tier3T4ProgressPlayer progress, int toWho, int fromWho, bool initialSync)
    {
        ModPacket packet = ModContent.GetInstance<MilkcraftTerrariaMod>().GetPacket();
        packet.Write(TierProgressMessage);
        packet.Write((byte)progress.Player.whoAmI);
        packet.Write(initialSync);
        packet.Write(progress.HasT3License);
        packet.Write(progress.HasT4License);
        packet.Write(progress.HasT3ChampionTitle);
        packet.Send(toWho, fromWho);
    }

    public override void HandlePacket(BinaryReader reader, int whoAmI)
    {
        byte message = reader.ReadByte();
        if (message == BossSummonRequestMessage)
        {
            byte summonPlayerIndex = reader.ReadByte();
            int bossType = reader.ReadInt16();
            if (Main.netMode == NetmodeID.Server && summonPlayerIndex == whoAmI && summonPlayerIndex < Main.maxPlayers)
                BossSummonItem.TrySpawnFromServer(Main.player[summonPlayerIndex], bossType);
            return;
        }
        if (message == AlchemyActivationRequestMessage)
        {
            Point16 origin = new(reader.ReadInt16(), reader.ReadInt16());
            bool heldAlcohol = reader.ReadBoolean();
            int inventorySlot = reader.ReadByte();
            if (Main.netMode == NetmodeID.Server && whoAmI >= 0 && whoAmI < Main.maxPlayers)
                MilkcraftAlchemyWorldSystem.HandleActivationRequest(Main.player[whoAmI], origin, heldAlcohol, inventorySlot);
            return;
        }
        if (message == AlchemyActivationSyncMessage)
        {
            Point16 origin = new(reader.ReadInt16(), reader.ReadInt16());
            bool active = reader.ReadBoolean();
            if (Main.netMode == NetmodeID.MultiplayerClient)
                MilkcraftAlchemyWorldSystem.ApplyActivationSync(origin, active);
            return;
        }
        if (message == EnchantApplyRequestMessage)
        {
            byte slot = reader.ReadByte();
            int itemType = reader.ReadInt32();
            byte prefix = reader.ReadByte();
            byte oldKind = reader.ReadByte();
            byte newKind = reader.ReadByte();
            if (Main.netMode == NetmodeID.Server && whoAmI >= 0 && whoAmI < Main.maxPlayers)
            {
                bool applied = MilkcraftEnchantingSystem.HandleApplyRequest(Main.player[whoAmI], slot, itemType, prefix, oldKind, newKind);
                ModPacket result = GetPacket();
                result.Write(EnchantApplyResultMessage);
                result.Write(applied);
                result.Send(whoAmI);
            }
            return;
        }
        if (message == EnchantApplyResultMessage)
        {
            bool applied = reader.ReadBoolean();
            if (Main.netMode == NetmodeID.MultiplayerClient)
                Main.NewText(Terraria.Localization.Language.GetTextValue(applied
                    ? "Mods.MilkcraftTerraria.Enchanting.Applied"
                    : "Mods.MilkcraftTerraria.Enchanting.ApplyFailed"), applied ? Microsoft.Xna.Framework.Color.LightGreen : Microsoft.Xna.Framework.Color.OrangeRed);
            return;
        }
        if (message == EnchantBankSyncMessage)
        {
            if (Main.netMode == NetmodeID.MultiplayerClient)
                MilkcraftEnchantingSystem.HandleBankSync(reader);
            return;
        }
        if (message != TierProgressMessage)
            return;

        int playerIndex = reader.ReadByte();
        bool initialSync = reader.ReadBoolean();
        bool t3 = reader.ReadBoolean();
        bool t4 = reader.ReadBoolean();
        bool champion = reader.ReadBoolean();
        if (playerIndex < 0 || playerIndex >= Main.maxPlayers)
            return;

        Tier3T4ProgressPlayer progress = Main.player[playerIndex].GetModPlayer<Tier3T4ProgressPlayer>();
        if (Main.netMode == NetmodeID.Server)
        {
            // The client owns its character save, so an initial join sync may
            // restore previously earned licenses in an older world. Later
            // state changes are accepted only when the present world allows
            // that milestone (a license itself is never revoked).
            if (whoAmI != playerIndex)
                return;

            if (initialSync)
                progress.ApplyNetworkState(t3, t4, champion);
            else
                progress.ApplyClientProgress(t3, t4, champion);

            SendProgressPacket(progress, -1, playerIndex, initialSync: false);
        }
        else if (Main.netMode == NetmodeID.MultiplayerClient)
        {
            progress.ApplyNetworkState(t3, t4, champion);
        }
    }
}
