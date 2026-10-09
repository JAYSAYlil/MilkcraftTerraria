using Terraria;
using Terraria.GameContent.ItemDropRules;
using Terraria.ID;
using Terraria.ModLoader;
using MilkcraftTerraria.Content.Items;

namespace MilkcraftTerraria.Content;

/// <summary>Exactly one hidden blade from the Moon Lord core in every mode.</summary>
public sealed class DazhouIronSwordLoot : GlobalNPC
{
    public override void ModifyNPCLoot(NPC npc, NPCLoot npcLoot)
    {
        if (npc.type == NPCID.MoonLordCore)
            npcLoot.Add(ItemDropRule.Common(ModContent.ItemType<HiddenIronSword>(), 1, 1, 1));
    }
}
