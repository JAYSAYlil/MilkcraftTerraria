using Terraria;
using Terraria.ModLoader;

namespace MilkcraftTerraria.Content;

// Intentionally empty compatibility hooks. Vanilla Terraria bosses no longer
// award Milkcraft progression materials; those rewards now come from the seven
// Milkcraft bosses. Explicit vanilla exceptions (Plantera's fox and Moon Lord's
// broken sword) remain in their dedicated GlobalNPC implementations.
public sealed class ProgressionBossLoot : GlobalNPC
{
    internal static bool IsEaterSegment(int type) => false;
    internal static bool NoLivingEaterSegmentOtherThan(NPC npc) => true;
    public override void OnKill(NPC npc) { }
}
public sealed class ProgressionBossBagLoot : GlobalItem { }

// Kept only so archived smoke helpers still compile; no hook observes or pays
// Eater/Twins cycles in the current progression.
public sealed class ProgressionBossCycleSystem : ModSystem
{
    internal static bool TwinsRewardClaimed;
    internal static bool EaterRewardClaimed;
    internal static void ObserveEaterSpawn(NPC spawning) { }
    internal static void ObserveTwinSpawn(NPC spawning) { }
}
