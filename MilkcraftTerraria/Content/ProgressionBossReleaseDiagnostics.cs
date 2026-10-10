using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;
using Terraria.GameContent;
using Terraria.Utilities;
using Terraria.UI;
using MilkcraftTerraria.Content.Items;

namespace MilkcraftTerraria.Content;

/// <summary>Focused native checks for new boss world flags and Castle Core bag state.</summary>
internal static class ProgressionBossReleaseDiagnostics
{
    internal static string Run()
    {
        string attackGeometry = TestBossAttackCollisionGeometry();
        TestWorldFlagsAndLegacyLicense();
        return attackGeometry + "; " + TestCastleBagActualRightClick();
    }

    private static string TestBossAttackCollisionGeometry()
    {
        static Projectile Make(BossProjectileStyle style, Vector2 center, Vector2 velocity)
        {
            Projectile projectile = new();
            projectile.SetDefaults(ModContent.ProjectileType<MilkcraftBossProjectile>());
            projectile.Center = center;
            projectile.velocity = velocity;
            projectile.ai[0] = (float)style;
            return projectile;
        }

        Projectile wave = Make(BossProjectileStyle.BansheeWave, new Vector2(100f, 100f), Vector2.UnitX);
        bool? ringAcrossCenter = wave.ModProjectile.Colliding(wave.Hitbox, new Rectangle(90, 90, 20, 20));
        bool? coreOnly = wave.ModProjectile.Colliding(wave.Hitbox, new Rectangle(96, 96, 8, 8));
        bool? outside = wave.ModProjectile.Colliding(wave.Hitbox, new Rectangle(126, 96, 8, 8));
        if (ringAcrossCenter != true || coreOnly != false || outside != false)
            throw new InvalidOperationException($"Banshee ring/AABB geometry failed center-overlap:{ringAcrossCenter},core:{coreOnly},outside:{outside}.");

        Projectile spear = Make(BossProjectileStyle.WraithSpear, new Vector2(100f, 100f), Vector2.UnitX);
        bool? horizontalTip = spear.ModProjectile.Colliding(spear.Hitbox, new Rectangle(122, 97, 8, 6));
        bool? oldAxisAlignedFalseHit = spear.ModProjectile.Colliding(spear.Hitbox, new Rectangle(97, 122, 6, 8));
        if (horizontalTip != true || oldAxisAlignedFalseHit != false)
            throw new InvalidOperationException($"Rotated spear line collision failed along={horizontalTip},off-axis={oldAxisAlignedFalseHit}.");
        return "boss-projectile-collision=PASS ring-AABB-crosses-center/inner-hole/outside; spear follows velocity axis instead of upright hitbox";
    }

    private static void TestWorldFlagsAndLegacyLicense()
    {
        MilkcraftBossWorldSystem world = ModContent.GetInstance<MilkcraftBossWorldSystem>();
        TagCompound oldWorld = new();
        world.SaveWorldData(oldWorld);
        int playerIndex = Main.myPlayer;
        Player originalPlayer = Main.player[playerIndex];
        Player player = originalPlayer.SerializedClone();
        MethodInfo setupPlayer = typeof(PlayerLoader).GetMethod("SetupPlayer", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("PlayerLoader.SetupPlayer unavailable for boss release fixture");
        setupPlayer.Invoke(null, new object[] { player });
        player.whoAmI = playerIndex;
        Main.player[playerIndex] = player;
        Tier3T4ProgressPlayer licenses = player.GetModPlayer<Tier3T4ProgressPlayer>();
        TagCompound oldPlayer = new();
        licenses.SaveData(oldPlayer);
        try
        {
            world.ClearWorld();
            Require(!MilkcraftBossWorldSystem.WraithWarriorDowned && !MilkcraftBossWorldSystem.JungleLordDowned
                && !MilkcraftBossWorldSystem.BansheeDowned && !MilkcraftBossWorldSystem.ObsidianGuardianDowned
                && !MilkcraftBossWorldSystem.SphinxDowned && !MilkcraftBossWorldSystem.FrostWitchDowned
                && !MilkcraftBossWorldSystem.CastleCoreDowned, "ClearWorld left a new boss flag set");

            MilkcraftBossWorldSystem.MarkDowned(MilkcraftBossKind.WraithWarrior);
            MilkcraftBossWorldSystem.MarkDowned(MilkcraftBossKind.Banshee);
            MilkcraftBossWorldSystem.MarkDowned(MilkcraftBossKind.Sphinx);
            MilkcraftBossWorldSystem.MarkDowned(MilkcraftBossKind.CastleCore);
            TagCompound saved = new();
            world.SaveWorldData(saved);
            world.ClearWorld();
            world.LoadWorldData(saved);
            Require(MilkcraftBossWorldSystem.WraithWarriorDowned && !MilkcraftBossWorldSystem.JungleLordDowned
                && MilkcraftBossWorldSystem.BansheeDowned && !MilkcraftBossWorldSystem.ObsidianGuardianDowned
                && MilkcraftBossWorldSystem.SphinxDowned && !MilkcraftBossWorldSystem.FrostWitchDowned
                && MilkcraftBossWorldSystem.CastleCoreDowned, "boss flags failed mixed SaveWorldData/LoadWorldData roundtrip");

            using var stream = new MemoryStream();
            using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
                world.NetSend(writer);
            world.ClearWorld();
            stream.Position = 0;
            using (var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true))
                world.NetReceive(reader);
            Require(MilkcraftBossWorldSystem.WraithWarriorDowned && !MilkcraftBossWorldSystem.JungleLordDowned
                && MilkcraftBossWorldSystem.BansheeDowned && !MilkcraftBossWorldSystem.ObsidianGuardianDowned
                && MilkcraftBossWorldSystem.SphinxDowned && !MilkcraftBossWorldSystem.FrostWitchDowned
                && MilkcraftBossWorldSystem.CastleCoreDowned, "boss flags failed mixed NetSend/NetReceive roundtrip");

            licenses.LoadData(new TagCompound { ["tier3EquipmentLicense"] = true, ["tier4EquipmentLicense"] = true });
            world.ClearWorld();
            TagCompound legacySaved = new();
            licenses.SaveData(legacySaved);
            Require(legacySaved.GetBool("tier3EquipmentLicense") && legacySaved.GetBool("tier4EquipmentLicense"),
                "world boss flags reset legacy per-character T3/T4 licenses");
        }
        finally
        {
            Main.player[playerIndex] = originalPlayer;
            world.LoadWorldData(oldWorld);
            licenses.LoadData(oldPlayer);
        }
    }

    private static string TestCastleBagActualRightClick()
    {
        Player player = Main.LocalPlayer;
        bool oldMenu = Main.gameMenu;
        int oldMode = Main.netMode;
        int oldMyPlayer = Main.myPlayer;
        UnifiedRandom oldRandom = Main.rand;
        Item[] oldWorldItems = Main.item;
        Item oldMouseItem = Main.mouseItem.Clone();
        bool oldDay = Main.dayTime;
        Item[] oldInventory = player.inventory.Select(item => item.Clone()).ToArray();
        try
        {
            Main.gameMenu = false;
            Main.netMode = NetmodeID.SinglePlayer;
            Main.myPlayer = player.whoAmI;
            Main.rand = new UnifiedRandom(0x3030);
            player.active = true;
            player.dead = false;
            player.inventory[0].TurnToAir();
            player.inventory[1].TurnToAir();
            Main.mouseItem.TurnToAir();
            Main.item = NewItemArray();
            Main.dayTime = true;

            // Use the actual loot source so CastleCoreBagItemState.OnSpawn snapshots ai[3].
            NPC castle = new();
            castle.SetDefaults(ModContent.NPCType<CastleCoreBoss>());
            castle.ai[3] = 1f;
            int spawnedIndex = Item.NewItem(castle.GetSource_Loot(), castle.Hitbox, ModContent.ItemType<CastleCoreBag>());
            Require(spawnedIndex >= 0 && spawnedIndex < Main.item.Length, "could not create empowered Castle Core bag through native Item.NewItem");
            Item empowered = ItemIO.Load(ItemIO.Save(Main.item[spawnedIndex]));
            Main.item[spawnedIndex].TurnToAir();
            Require(empowered.GetGlobalItem<CastleCoreBagItemState>().EmpoweredAtKill,
                "Castle Core bag lost empowered-at-kill state through ItemIO.Save/Load");

            // The kill happened in daylight; opening at night must still guarantee the mount.
            Main.dayTime = false;
            player.inventory[0] = empowered;
            OpenNativeBag(player, 0);
            int empoweredMounts = CountOwned(player, ModContent.ItemType<WhiteUnicornSummon>());
            Require(empoweredMounts == 1,
                $"empowered Castle bag opened after sunset yielded White Unicorn x{empoweredMounts}, expected exactly 1; " +
                $"bagSlot0={player.inventory[0].type}x{player.inventory[0].stack}, mouse={Main.mouseItem.type}x{Main.mouseItem.stack}, " +
                $"bagSet={ItemID.Sets.BossBag[ModContent.ItemType<CastleCoreBag>()]}, player={player.whoAmI}/{Main.myPlayer}/{player.active}, " +
                $"worldItems={Main.item.Where(item => item != null && item.type > ItemID.None).Take(12).Select(item => $"{item.type}x{item.stack}").ToArray()}; " +
                $"unicornWorld={Main.item.Count(item => item != null && item.type == ModContent.ItemType<WhiteUnicornSummon>())}; " +
                $"condition={CastleCoreBagOpenContext.LastMatchReport}");

            int ordinaryRolls = RunNormalBagSample(player, 64, interleaveWithEmpowered: false);
            int interleavedNormalRolls = RunNormalBagSample(player, 64, interleaveWithEmpowered: true);
            Require(ordinaryRolls is >= 7 and <= 25,
                $"64 native ordinary bag opens yielded {ordinaryRolls} mounts, outside a reasonable 1/4 sample");
            Require(interleavedNormalRolls is >= 7 and <= 25,
                $"64 same-frame mixed bag opens yielded {interleavedNormalRolls} ordinary mounts; context may have leaked");
            return $"castle-core-bag=native-ItemSlot.RightClick+OpenBossBag PASS empowered-kill-day/open-night=guaranteed:{empoweredMounts}; normal-kill-night/open-day=64-rolls:{ordinaryRolls}/64; same-frame-opposite-state=normal:{interleavedNormalRolls}/64; ItemIO=PASS; world-flags=7/7 mixed Save/Load+Net roundtrip; legacy T3/T4 licenses preserved";
        }
        finally
        {
            Main.gameMenu = oldMenu;
            Main.netMode = oldMode;
            Main.myPlayer = oldMyPlayer;
            Main.rand = oldRandom;
            Main.item = oldWorldItems;
            Main.mouseItem = oldMouseItem;
            Main.dayTime = oldDay;
            for (int i = 0; i < player.inventory.Length; i++) player.inventory[i] = oldInventory[i];
            CastleCoreBagOpenContext.Clear();
        }
    }

    private static Item MakeCastleBagFromLoot(bool empowered)
    {
        NPC castle = new();
        castle.SetDefaults(ModContent.NPCType<CastleCoreBoss>());
        castle.ai[3] = empowered ? 1f : 0f;
        int slot = Item.NewItem(castle.GetSource_Loot(), castle.Hitbox, ModContent.ItemType<CastleCoreBag>());
        if (slot < 0 || slot >= Main.item.Length)
            throw new InvalidOperationException("native Item.NewItem failed for Castle Core bag fixture");
        Item result = ItemIO.Load(ItemIO.Save(Main.item[slot]));
        Main.item[slot].TurnToAir();
        return result;
    }

    private static int RunNormalBagSample(Player player, int attempts, bool interleaveWithEmpowered)
    {
        int unicorns = 0;
        Main.rand = new UnifiedRandom(interleaveWithEmpowered ? 0x711A : 0x7110);
        for (int i = 0; i < attempts; i++)
        {
            Main.item = NewItemArray();
            for (int slot = 0; slot < player.inventory.Length; slot++) player.inventory[slot].TurnToAir();
            if (interleaveWithEmpowered)
            {
                player.inventory[0] = MakeCastleBagFromLoot(true);
                player.inventory[1] = MakeCastleBagFromLoot(false);
                Main.dayTime = true;
                OpenNativeBag(player, 0);
                int empoweredCount = CountOwned(player, ModContent.ItemType<WhiteUnicornSummon>());
                Require(empoweredCount == 1, $"same-frame bag pair {i} lost the empowered guarantee");
                OpenNativeBag(player, 1);
                unicorns += CountOwned(player, ModContent.ItemType<WhiteUnicornSummon>()) - empoweredCount;
            }
            else
            {
                player.inventory[0] = MakeCastleBagFromLoot(false);
                Main.dayTime = true;
                OpenNativeBag(player, 0);
                unicorns += CountOwned(player, ModContent.ItemType<WhiteUnicornSummon>());
            }
        }
        return unicorns;
    }

    private static void OpenNativeBag(Player player, int slot)
    {
        // In normal play this cache is prepared after entering a world. The smoke fixture runs
        // from the menu, so initialize the same native recipe lookup before exercising ItemSlot.
        MethodInfo quickLookups = typeof(Recipe).GetMethod("CreateRequiredItemQuickLookups",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Recipe.CreateRequiredItemQuickLookups unavailable");
        quickLookups.Invoke(null, null);
        player.selectedItem = slot;
        player.itemAnimation = 0;
        Main.mouseRight = true;
        Main.mouseRightRelease = true;
        ItemSlot.RightClick(player.inventory, ItemSlot.Context.InventoryItem, slot);
        Main.mouseRight = false;
        Main.mouseRightRelease = false;
    }

    private static int CountOwned(Player player, int type)
        => player.inventory.Concat(Main.item).Where(item => item != null && item.type == type)
            .Sum(item => item.stack);

    private static Item[] NewItemArray()
        => Enumerable.Range(0, Math.Max(400, Main.maxItems + 1)).Select(_ => new Item()).ToArray();

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
