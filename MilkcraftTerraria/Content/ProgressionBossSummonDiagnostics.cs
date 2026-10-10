using System;
using System.Linq;
using System.Reflection;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.Utilities;
using MilkcraftTerraria.Content.Items;

namespace MilkcraftTerraria.Content;

internal static class ProgressionBossSummonDiagnostics
{
    internal static string Run()
    {
        Player[] oldPlayers = Main.player;
        NPC[] oldNpcs = Main.npc;
        Item[] oldItems = Main.item;
        int oldMyPlayer = Main.myPlayer, oldMode = Main.netMode;
        int oldMouseX = Main.mouseX, oldMouseY = Main.mouseY;
        bool oldDay = Main.dayTime, oldMenu = Main.gameMenu, oldPaused = Main.gamePaused;
        bool oldDownedBoss2 = NPC.downedBoss2;
        double oldSurface = Main.worldSurface;
        UnifiedRandom oldRandom = Main.rand;
        int owner = 0;
        MethodInfo setup = typeof(PlayerLoader).GetMethod("SetupPlayer", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("PlayerLoader.SetupPlayer unavailable for summon ItemCheck fixture");
        MethodInfo updateBiomes = typeof(Player).GetMethod("UpdateBiomes", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null, Type.EmptyTypes, null);
        Player actor = oldPlayers[owner].SerializedClone();
        setup.Invoke(null, new object[] { actor });
        actor.whoAmI = owner;
        actor.active = true;
        actor.dead = false;
        actor.noItems = false;
        actor.selectedItem = 0;
        actor.releaseUseItem = true;
        actor.velocity = Vector2.Zero;
        try
        {
            Main.player = (Player[])oldPlayers.Clone();
            Main.player[owner] = actor;
            Main.myPlayer = owner;
            Main.netMode = NetmodeID.SinglePlayer;
            Main.gameMenu = false;
            Main.gamePaused = false;
            Main.dayTime = false;
            NPC.downedBoss2 = true;
            // The smoke client starts at the menu (worldSurface == 0). Seed a
            // fixture surface value so native UpdateBiomes/CanUseItem see the
            // same surface-height predicate as a real loaded world.
            Main.worldSurface = Math.Max(250d, Main.maxTilesY * 0.2d);
            actor.position = new Vector2(Math.Clamp(Main.maxTilesX / 2, 100, Main.maxTilesX - 100) * 16f,
                ((int)Main.worldSurface - 12) * 16f);
            Main.npc = Enumerable.Range(0, Math.Max(oldNpcs.Length, Main.maxNPCs + 1)).Select(_ => new NPC()).ToArray();
            Main.item = Enumerable.Range(0, Math.Max(oldItems.Length, Main.maxItems + 1)).Select(_ => new Item()).ToArray();

            void RefreshZone()
            {
                updateBiomes?.Invoke(actor, null);
            }
            RefreshZone();
            if (!actor.ZoneOverworldHeight)
                throw new InvalidOperationException($"Summon ItemCheck fixture could not reach surface: pos={actor.position} surface={Main.worldSurface} zoneSurface={actor.ZoneOverworldHeight} updateBiomes={updateBiomes is not null}");

            Item held = actor.inventory[0];
            held.SetDefaults(ModContent.ItemType<WraithWarriorSummon>());
            held.stack = 2;
            actor.lastVisualizedSelectedItem = held;
            actor.itemAnimation = actor.itemTime = 0;
            actor.controlUseItem = true;
            actor.releaseUseItem = true;
            actor.noItems = false;
            actor.heldProj = -1;
            Main.mouseX = (int)(actor.Center.X + 220f);
            Main.mouseY = (int)actor.Center.Y;
            actor.ItemCheck();
            int spawned = Main.npc.Count(n => n.active && n.type == ModContent.NPCType<WraithWarriorBoss>());
            if (spawned != 1 || held.stack != 1)
                throw new InvalidOperationException($"real singleplayer ItemCheck summon failed: bossCount={spawned},stack=2->{held.stack},canUse={ModContent.GetInstance<WraithWarriorSummon>().CanUseItem(actor)},progress={NPC.downedBoss2},zoneSurface={actor.ZoneOverworldHeight},night={!Main.dayTime},release={actor.releaseUseItem},control={actor.controlUseItem},anim={actor.itemAnimation},time={actor.itemTime},noItems={actor.noItems},useStyle={held.useStyle},useTime={held.useTime}/{held.useAnimation}");

            // Daytime rejection occurs in native CanUseItem before UseItem/ConsumeItem.
            Main.npc = Enumerable.Range(0, Math.Max(oldNpcs.Length, Main.maxNPCs + 1)).Select(_ => new NPC()).ToArray();
            actor.itemAnimation = actor.itemTime = 0;
            actor.releaseUseItem = true;
            actor.controlUseItem = true;
            Main.dayTime = true;
            int beforeDay = held.stack;
            actor.ItemCheck();
            if (held.stack != beforeDay || Main.npc.Any(n => n.active && n.type == ModContent.NPCType<WraithWarriorBoss>()))
                throw new InvalidOperationException($"invalid daytime ItemCheck consumed/spawned: stack={beforeDay}->{held.stack}");

            // A previously active boss is also rejected without a charge.
            Main.dayTime = false;
            NPC existing = new();
            existing.SetDefaults(ModContent.NPCType<WraithWarriorBoss>());
            existing.whoAmI = 20; existing.active = true;
            Main.npc[20] = existing;
            actor.itemAnimation = actor.itemTime = 0;
            actor.releaseUseItem = true;
            actor.controlUseItem = true;
            int beforeExisting = held.stack;
            actor.ItemCheck();
            if (held.stack != beforeExisting || Main.npc.Count(n => n.active && n.type == existing.type) != 1)
                throw new InvalidOperationException($"existing-boss ItemCheck rejection failed: stack={beforeExisting}->{held.stack}");

            // Fill all native NPC slots with a non-boss type. CanUseItem still
            // accepts the location, so this reaches NPC.NewNPC failure rather
            // than the duplicate-boss precondition.
            Main.npc = Enumerable.Range(0, Math.Max(oldNpcs.Length, Main.maxNPCs + 1)).Select(_ => new NPC()).ToArray();
            for (int i = 0; i < Main.maxNPCs; i++)
            {
                Main.npc[i].SetDefaults(NPCID.BlueSlime);
                Main.npc[i].whoAmI = i;
                Main.npc[i].active = true;
            }
            actor.itemAnimation = actor.itemTime = 0;
            actor.releaseUseItem = true;
            actor.controlUseItem = true;
            int beforeFullPool = held.stack;
            actor.ItemCheck();
            if (held.stack != beforeFullPool || Main.npc.Any(n => n.active && n.type == ModContent.NPCType<WraithWarriorBoss>()))
                throw new InvalidOperationException($"full NPC pool ItemCheck consumed/spawned: stack={beforeFullPool}->{held.stack}");

            // Invoke the exact server transaction used by the packet handler. The
            // second request must observe the live boss and leave the stack alone.
            // Exercise the exact packet-handler transaction in an isolated
            // single-player process; the production handler runs it only on the
            // server, and the SyncEquipment branch is guarded by Server mode.
            Main.netMode = NetmodeID.SinglePlayer;
            Main.npc = Enumerable.Range(0, Math.Max(oldNpcs.Length, Main.maxNPCs + 1)).Select(_ => new NPC()).ToArray();
            held.stack = 2;
            actor.selectedItem = 0;
            bool first = BossSummonItem.TrySpawnFromServer(actor, ModContent.NPCType<WraithWarriorBoss>());
            int afterFirst = held.stack;
            bool duplicate = BossSummonItem.TrySpawnFromServer(actor, ModContent.NPCType<WraithWarriorBoss>());
            int serverBosses = Main.npc.Count(n => n.active && n.type == ModContent.NPCType<WraithWarriorBoss>());
            if (!first || duplicate || afterFirst != 1 || held.stack != 1 || serverBosses != 1)
                throw new InvalidOperationException($"server summon transaction mismatch: first={first},duplicate={duplicate},stack=2->{afterFirst}->{held.stack},bosses={serverBosses}");
            return "boss-summon-native=PASS real SP ItemCheck success stack 2->1; daytime/existing-boss/full-NPC-pool failure consume 0; exact packet-handler authoritative transaction created one NPC/decremented once; duplicate request created/decremented 0 (network SyncEquipment branch executes only on Server)";
        }
        finally
        {
            Main.player = oldPlayers;
            Main.npc = oldNpcs;
            Main.item = oldItems;
            Main.myPlayer = oldMyPlayer;
            Main.netMode = oldMode;
            Main.dayTime = oldDay;
            NPC.downedBoss2 = oldDownedBoss2;
            Main.gameMenu = oldMenu;
            Main.gamePaused = oldPaused;
            Main.worldSurface = oldSurface;
            Main.rand = oldRandom;
            Main.mouseX = oldMouseX; Main.mouseY = oldMouseY;
        }
    }
}
