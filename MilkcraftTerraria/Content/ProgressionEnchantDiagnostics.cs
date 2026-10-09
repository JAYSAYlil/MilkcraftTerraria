using System;
using System.Linq;
using System.Reflection;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;
using Terraria.Utilities;
using MilkcraftTerraria.Content.Items;
using MilkcraftTerraria.Content.Tiles;

namespace MilkcraftTerraria.Content;

internal static class ProgressionEnchantDiagnostics
{
    internal static string RunStableOnly()
    {
        const int playerIndex = 0;
        Player[] oldPlayers = (Player[])Main.player.Clone();
        int oldMyPlayer = Main.myPlayer;
        bool oldMenu = Main.gameMenu;
        int oldNetMode = Main.netMode;
        Player actor = Main.player[playerIndex].SerializedClone();
        MethodInfo setup = typeof(PlayerLoader).GetMethod("SetupPlayer", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("tML PlayerLoader.SetupPlayer reflection entry point is missing");
        setup.Invoke(null, new object[] { actor });
        actor.whoAmI = playerIndex;
        try
        {
            Main.player[playerIndex] = actor;
            Main.myPlayer = playerIndex;
            Main.gameMenu = false;
            Main.netMode = NetmodeID.SinglePlayer;
            actor.active = true;
            actor.dead = false;
            return TestStableEnchantStatDeltas(actor);
        }
        finally
        {
            Main.player = oldPlayers;
            Main.myPlayer = oldMyPlayer;
            Main.gameMenu = oldMenu;
            Main.netMode = oldNetMode;
        }
    }

    internal static string RunEffectsOnly()
    {
        const int playerIndex = 0;
        Player[] oldPlayers = Main.player;
        NPC[] oldNpcs = Main.npc;
        Projectile[] oldProjectiles = Main.projectile;
        Item[] oldItems = Main.item;
        Dust[] oldDust = Main.dust;
        int oldMyPlayer = Main.myPlayer;
        bool oldMenu = Main.gameMenu;
        int oldNetMode = Main.netMode;
        bool oldWorldGen = WorldGen.gen;
        UnifiedRandom oldRandom = Main.rand;
        Player actor = oldPlayers[playerIndex].SerializedClone();
        MethodInfo setup = typeof(PlayerLoader).GetMethod("SetupPlayer", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("tML PlayerLoader.SetupPlayer reflection entry point is missing");
        setup.Invoke(null, new object[] { actor });
        actor.whoAmI = playerIndex;
        try
        {
            Main.player = (Player[])oldPlayers.Clone();
            Main.player[playerIndex] = actor;
            Main.npc = Enumerable.Range(0, oldNpcs.Length).Select(_ => new NPC()).ToArray();
            Main.projectile = Enumerable.Range(0, oldProjectiles.Length).Select(_ => new Projectile()).ToArray();
            Main.item = Enumerable.Range(0, Math.Max(400, Main.maxItems + 1)).Select(_ => new Item()).ToArray();
            Main.dust = Enumerable.Range(0, oldDust.Length).Select(_ => new Dust()).ToArray();
            Main.myPlayer = playerIndex;
            Main.gameMenu = false;
            Main.netMode = NetmodeID.SinglePlayer;
            WorldGen.gen = false;
            Main.rand = new UnifiedRandom(20261008);
            actor.active = true;
            actor.dead = false;
            string values = TestStableEnchantStatDeltas(actor);
            string effects = TestNativeProjectileEnchantHits(actor);
            return values + " " + effects;
        }
        finally
        {
            Main.player = oldPlayers;
            Main.npc = oldNpcs;
            Main.projectile = oldProjectiles;
            Main.item = oldItems;
            Main.dust = oldDust;
            Main.myPlayer = oldMyPlayer;
            Main.gameMenu = oldMenu;
            Main.netMode = oldNetMode;
            WorldGen.gen = oldWorldGen;
            Main.rand = oldRandom;
        }
    }

    public static string Run()
    {
        if (!MilkcraftEnchantingSystem.DiagnosticValidateLayouts())
            throw new InvalidOperationException("enchanting UI geometry escaped 720p/1080p or 2560x1494 at UIScale 1.0/1.25/1.5/1.6296296");
        const int placeX = 760;
        const int floorY = 120;
        int tileType = ModContent.TileType<MilkcraftEnchantingTableTile>();
        int oldMyPlayer = Main.myPlayer;
        const int playerIndex = 0;
        Player[] oldPlayerArray = (Player[])Main.player.Clone();
        Player player = Main.player[playerIndex].SerializedClone();
        MethodInfo setup = typeof(PlayerLoader).GetMethod("SetupPlayer", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("tML PlayerLoader.SetupPlayer reflection entry point is missing");
        setup.Invoke(null, new object[] { player });
        player.whoAmI = playerIndex;
        bool oldMenu = Main.gameMenu;
        bool oldPlayerInventory = Main.playerInventory;
        float oldUIScale = Main.UIScale;
        int oldNetMode = Main.netMode;
        bool oldGen = WorldGen.gen, oldGenerating = WorldGen.generatingWorld;
        bool oldNoActions = WorldGen.noTileActions, oldDestroy = WorldGen.destroyObject;
        int oldSelected = player.selectedItem;
        bool oldActive = player.active, oldDead = player.dead;
        Vector2 oldPosition = player.position;
        int oldLife = player.statLife, oldLifeMax2 = player.statLifeMax2;
        float oldLifeSteal = player.lifeSteal;
        bool oldDiscount = player.discountAvailable;
        MilkcraftEnchantedPlayer oldEnchantState = player.GetModPlayer<MilkcraftEnchantedPlayer>();
        int oldHealingCooldown = oldEnchantState.HealingCooldown, oldExplosiveCooldown = oldEnchantState.ExplosiveCooldown;
        Item[] oldInventory = player.inventory.Select(item => item.Clone()).ToArray();
        Item[][] oldBanks = new[] { player.bank.item, player.bank2.item, player.bank3.item, player.bank4.item }
            .Select(items => items.Select(item => item.Clone()).ToArray()).ToArray();
        NPC[] oldNpcs = (NPC[])Main.npc.Clone();
        Item[] oldItems = Main.item;
        Projectile[] oldProjectiles = Main.projectile;
        var preserved = new System.Collections.Generic.List<Point>();
        try
        {
            if (!WorldGen.InWorld(placeX + 10, floorY + 10, 10))
                throw new InvalidOperationException("enchanting fixture is out of world bounds");
            for (int x = placeX - 4; x <= placeX + 6; x++)
            for (int y = floorY - 6; y <= floorY + 2; y++)
            {
                if (Main.tile[x, y].HasTile || Main.tile[x, y].WallType != WallID.None)
                    throw new InvalidOperationException($"enchanting fixture arena occupied at {x},{y}");
                preserved.Add(new Point(x, y));
            }
            Main.item = Enumerable.Range(0, Math.Max(400, Main.maxItems + 1)).Select(_ => new Item()).ToArray();
            Main.projectile = Enumerable.Range(0, oldProjectiles.Length).Select(_ => new Projectile()).ToArray();
            Main.gameMenu = false;
            Main.netMode = NetmodeID.SinglePlayer;
            Main.myPlayer = playerIndex;
            Main.player[playerIndex] = player;
            player.active = true;
            player.dead = false;
            WorldGen.gen = false;
            WorldGen.generatingWorld = false;
            WorldGen.noTileActions = false;
            WorldGen.destroyObject = false;
            for (int x = placeX - 4; x <= placeX + 6; x++)
            {
                Tile floor = Main.tile[x, floorY];
                floor.HasTile = true;
                floor.TileType = TileID.Stone;
            }

            if (!WorldGen.PlaceObject(placeX, floorY - 1, tileType, true, 0))
                throw new InvalidOperationException("native enchanting station placement failed");
            int tileX = -1, tileY = -1;
            for (int x = placeX - 3; x <= placeX + 3 && tileX < 0; x++)
            for (int y = floorY - 4; y <= floorY && tileY < 0; y++)
                if (Main.tile[x, y].HasTile && Main.tile[x, y].TileType == tileType)
                { tileX = x; tileY = y; }
            if (tileX < 0)
                throw new InvalidOperationException("placed enchanting station footprint was not found");
            int originX = tileX - (Main.tile[tileX, tileY].TileFrameX / 18) % 3;
            int originY = tileY - (Main.tile[tileX, tileY].TileFrameY / 18) % 3;
            player.position = new Vector2((originX + 1) * 16f, (originY + 1) * 16f);
            if (!MilkcraftEnchantingSystem.HasNearbyTable(player))
                throw new InvalidOperationException("native Player.AdjTiles-range enchanting station was not detected");
            Item inventoryMarker = new();
            inventoryMarker.SetDefaults(ItemID.Gel);
            inventoryMarker.stack = 7;
            player.inventory[8] = inventoryMarker;
            Main.playerInventory = true;
            ModContent.GetInstance<MilkcraftEnchantingTableTile>().RightClick(tileX, tileY);
            if (!MilkcraftEnchantingSystem.DiagnosticVisible)
                throw new InvalidOperationException("real enchanting-table RightClick hook did not open the UI");
            if (Main.playerInventory || player.inventory[8].type != ItemID.Gel || player.inventory[8].stack != 7)
                throw new InvalidOperationException("opening enchanting from the vanilla inventory did not close it cleanly or moved an inventory item");

            (int actualPhysicalWidth, int actualPhysicalHeight) = GetNativePhysicalScreenSize();
            float actualUiScale = Main.UIScale;
            var uiViewports = new (int Width, int Height, float Scale, string Name)[]
            {
                (actualPhysicalWidth, actualPhysicalHeight, actualUiScale, "runtime"),
                (2560, 1494, 1.6296296f, "user-config"),
                (1280, 720, 1.5f, "720p-ui-1.5")
            };
            var uiViewportResults = new System.Collections.Generic.List<string>();
            Item sword = null;
            for (int viewport = 0; viewport < uiViewports.Length; viewport++)
            {
                if (viewport > 0)
                {
                    MilkcraftEnchantingSystem.Close();
                    for (int slot = 0; slot < player.inventory.Length; slot++) player.inventory[slot].TurnToAir();
                    foreach (Item[] bank in new[] { player.bank.item, player.bank2.item, player.bank3.item, player.bank4.item })
                        foreach (Item coin in bank) coin.TurnToAir();
                    Main.UIScale = uiViewports[viewport].Scale;
                    player.position = new Vector2((originX + 1) * 16f, (originY + 1) * 16f);
                    player.inventory[8].SetDefaults(ItemID.Gel);
                    player.inventory[8].stack = 7;
                    Main.playerInventory = true;
                    ModContent.GetInstance<MilkcraftEnchantingTableTile>().RightClick(tileX, tileY);
                    if (!MilkcraftEnchantingSystem.DiagnosticVisible || Main.playerInventory)
                        throw new InvalidOperationException($"{uiViewports[viewport].Name}: opening panel did not isolate it from vanilla inventory");
                }

                for (int slot = 0; slot < player.inventory.Length; slot++)
                    if (slot != 8) player.inventory[slot].TurnToAir();
                foreach (Item[] bank in new[] { player.bank.item, player.bank2.item, player.bank3.item, player.bank4.item })
                    foreach (Item coin in bank) coin.TurnToAir();
                player.discountAvailable = false;
                player.selectedItem = 0;
                sword = new Item();
                sword.SetDefaults(ModContent.ItemType<KnightSword>());
                sword.prefix = PrefixID.Keen;
                player.inventory[0] = sword;
                player.inventory[8].SetDefaults(ItemID.Gel);
                player.inventory[8].stack = 7;
                player.inventory[50].SetDefaults(ItemID.GoldCoin);
                player.inventory[50].stack = 50;
                long uiExpectedFee = MilkcraftEnchantingSystem.GetReforgeCost(sword, player.discountAvailable);
                long uiCoinsBefore = CountCoins(player);
                TestNativeEnchantUIInteractions(player, tileX, tileY, uiViewports[viewport].Width,
                    uiViewports[viewport].Height, uiViewports[viewport].Scale);
                if (player.inventory[0].GetGlobalItem<MilkcraftEnchantedItem>().Kind != MilkcraftEnchant.Sharpness
                    || CountCoins(player) != uiCoinsBefore - uiExpectedFee
                    || player.inventory[8].type != ItemID.Gel || player.inventory[8].stack != 7)
                    throw new InvalidOperationException($"{uiViewports[viewport].Name}: UI did not apply/charge correctly or changed unrelated inventory");
                uiViewportResults.Add($"{uiViewports[viewport].Name}={uiViewports[viewport].Width}x{uiViewports[viewport].Height}@{uiViewports[viewport].Scale:0.####}");
            }
            Main.UIScale = actualUiScale;

            // The remaining fee validation cases start with an unenchanted item and fresh coins.
            for (int slot = 0; slot < player.inventory.Length; slot++)
                player.inventory[slot].TurnToAir();
            Item[] uiBanks = { player.bank.item[0], player.bank2.item[0], player.bank3.item[0], player.bank4.item[0] };
            foreach (Item coin in uiBanks) coin.TurnToAir();
            sword = new Item();
            sword.SetDefaults(ModContent.ItemType<KnightSword>());
            sword.prefix = PrefixID.Keen;
            player.inventory[0] = sword;
            byte prefix = (byte)player.inventory[0].prefix;
            player.inventory[50].SetDefaults(ItemID.GoldCoin);
            player.inventory[50].stack = 50;
            long regularCost = MilkcraftEnchantingSystem.GetReforgeCost(sword, discount: false);
            Item minimumPriceProbe = sword.Clone();
            minimumPriceProbe.value = 0;
            if (MilkcraftEnchantingSystem.GetReforgeCost(minimumPriceProbe, discount: false) != 1L)
                throw new InvalidOperationException("minimum reforge fee must remain one copper");
            long beforeCoins = CountCoins(player);
            Main.netMode = NetmodeID.Server;
            if (!MilkcraftEnchantingSystem.DiagnosticApplyRequest(player, 0, sword.type, prefix,
                    (byte)MilkcraftEnchant.None, (byte)MilkcraftEnchant.Sharpness))
                throw new InvalidOperationException("authoritative server validation rejected a valid sword enchantment request");
            Main.netMode = NetmodeID.SinglePlayer;
            MilkcraftEnchantedItem enchanted = player.inventory[0].GetGlobalItem<MilkcraftEnchantedItem>();
            if (enchanted.Kind != MilkcraftEnchant.Sharpness || player.inventory[0].prefix != prefix
                || CountCoins(player) != beforeCoins - regularCost)
                throw new InvalidOperationException($"server coin payment/state mismatch: kind={enchanted.Kind} prefix={player.inventory[0].prefix}/{prefix} coins={beforeCoins}->{CountCoins(player)} cost={regularCost}");
            long regularPaid = beforeCoins - CountCoins(player);
            long afterFirstEnchantCoins = CountCoins(player);
            if (!MilkcraftEnchantingSystem.HandleApplyRequest(player, 0, sword.type, prefix,
                    (byte)MilkcraftEnchant.Sharpness, (byte)MilkcraftEnchant.Sharpness)
                || CountCoins(player) != afterFirstEnchantCoins)
                throw new InvalidOperationException("reapplying the same enchant should succeed without another payment");

            for (int slot = 50; slot <= 53; slot++) player.inventory[slot].TurnToAir();
            foreach (Item[] bank in new[] { player.bank.item, player.bank2.item, player.bank3.item, player.bank4.item })
                foreach (Item coin in bank) coin.TurnToAir();
            Item poorSword = new();
            poorSword.SetDefaults(ModContent.ItemType<KnightSword>());
            player.inventory[1] = poorSword;
            long beforePoorAttempt = CountCoins(player);
            if (MilkcraftEnchantingSystem.HandleApplyRequest(player, 1, poorSword.type, (byte)poorSword.prefix,
                    (byte)MilkcraftEnchant.None, (byte)MilkcraftEnchant.Sharpness)
                || poorSword.GetGlobalItem<MilkcraftEnchantedItem>().Kind != MilkcraftEnchant.None
                || CountCoins(player) != beforePoorAttempt)
                throw new InvalidOperationException("insufficient money changed enchant state or coin balance");

            for (int slot = 50; slot <= 53; slot++) player.inventory[slot].TurnToAir();
            player.discountAvailable = true;
            Item discountedSword = new();
            discountedSword.SetDefaults(ModContent.ItemType<KnightSword>());
            player.inventory[2] = discountedSword;
            player.inventory[50].SetDefaults(ItemID.GoldCoin);
            player.inventory[50].stack = 50;
            long discountedCost = MilkcraftEnchantingSystem.GetReforgeCost(discountedSword, discount: true);
            long expectedDiscountedCost = Math.Max(1L, ((long)discountedSword.value * 4L / 5L) / 3L);
            long beforeDiscountedPayment = CountCoins(player);
            if (discountedCost != expectedDiscountedCost
                || !MilkcraftEnchantingSystem.HandleApplyRequest(player, 2, discountedSword.type, (byte)discountedSword.prefix,
                    (byte)MilkcraftEnchant.None, (byte)MilkcraftEnchant.Sharpness)
                || CountCoins(player) != beforeDiscountedPayment - discountedCost)
                throw new InvalidOperationException("native 20% reforge discount did not produce the expected discounted coin payment");
            long discountedPaid = beforeDiscountedPayment - CountCoins(player);

            player.discountAvailable = false;
            for (int slot = 50; slot <= 53; slot++) player.inventory[slot].TurnToAir();
            Item bankPaidSword = new();
            bankPaidSword.SetDefaults(ModContent.ItemType<KnightSword>());
            player.inventory[3] = bankPaidSword;
            player.bank.item[0].SetDefaults(ItemID.GoldCoin);
            player.bank.item[0].stack = 50;
            player.bank.item[2].SetDefaults(ItemID.DirtBlock);
            player.bank.item[2].stack = 17;
            long bankCost = MilkcraftEnchantingSystem.GetReforgeCost(bankPaidSword, discount: false);
            long beforeBankPayment = CountCoins(player);
            Main.netMode = NetmodeID.Server;
            if (!MilkcraftEnchantingSystem.DiagnosticApplyRequest(player, 3, bankPaidSword.type, (byte)bankPaidSword.prefix,
                    (byte)MilkcraftEnchant.None, (byte)MilkcraftEnchant.Sharpness))
                throw new InvalidOperationException("server did not accept payment from the player's Piggy Bank");
            Main.netMode = NetmodeID.SinglePlayer;
            if (CountCoins(player) != beforeBankPayment - bankCost
                || player.bank.item[2].type != ItemID.DirtBlock || player.bank.item[2].stack != 17)
                throw new InvalidOperationException("bank coin balance did not decrease exactly or a non-coin bank slot changed");
            long bankPaid = beforeBankPayment - CountCoins(player);

            Item clientGuardSword = new();
            clientGuardSword.SetDefaults(ModContent.ItemType<KnightSword>());
            player.inventory[4] = clientGuardSword;
            player.bank.item[0].stack = 50;
            long beforeClientRequest = CountCoins(player);
            Main.netMode = NetmodeID.MultiplayerClient;
            bool clientAccepted = MilkcraftEnchantingSystem.HandleApplyRequest(player, 4, clientGuardSword.type, (byte)clientGuardSword.prefix,
                (byte)MilkcraftEnchant.None, (byte)MilkcraftEnchant.Sharpness);
            Main.netMode = NetmodeID.SinglePlayer;
            if (clientAccepted || clientGuardSword.GetGlobalItem<MilkcraftEnchantedItem>().Kind != MilkcraftEnchant.None
                || CountCoins(player) != beforeClientRequest)
                throw new InvalidOperationException("client-side direct enchant request bypassed authoritative payment validation");

            var itemTag = new TagCompound();
            enchanted.SaveData(player.inventory[0], itemTag);
            Item roundTrip = player.inventory[0].Clone();
            roundTrip.GetGlobalItem<MilkcraftEnchantedItem>().Kind = MilkcraftEnchant.None;
            roundTrip.GetGlobalItem<MilkcraftEnchantedItem>().LoadData(roundTrip, itemTag);
            if (roundTrip.GetGlobalItem<MilkcraftEnchantedItem>().Kind != MilkcraftEnchant.Sharpness)
                throw new InvalidOperationException("item enchantment did not survive item save/load");
            if (MilkcraftEnchantedItem.AllowedKinds(player.inventory[0]).Count == 0)
                throw new InvalidOperationException("integrated KnightSword was not recognized as an enchantable mod item");
            if (MilkcraftEnchantingSystem.HandleApplyRequest(player, 50, player.inventory[50].type, 0,
                    (byte)MilkcraftEnchant.None, (byte)MilkcraftEnchant.Power))
                throw new InvalidOperationException("non-equipment coin item accepted an enchantment");

            string stableDeltas = TestStableEnchantStatDeltas(player);
            string projectileResult = TestNativeProjectileEnchantHits(player);
            return $"native-RightClick-open=PASS native-SetZoom_UI-click/select/apply/fee=PASS button-close=PASS Escape-close=PASS walk-away-close=PASS inventory-isolated/unrelated-slot-preserved=PASS layout-720p/1080p-and-user-2560x1494-UIScale1.6296296=PASS exact-player-range=PASS server-authoritative-reforge-price-and-BuyItem=PASS itemvalue/3 min1c discount20%=PASS inventory+PiggyBank-payment=PASS insufficient-funds-no-mutation=PASS repeat-same-enchant-free=PASS prefix-preserved=PASS client-cannot-apply-directly=PASS item-save-load=PASS nativeViewports=[{string.Join(";", uiViewportResults)}] fee-copper(base/paid,discounted,bank)={regularCost}/{regularPaid},{discountedCost}/{discountedPaid},{bankCost}/{bankPaid} " + stableDeltas + " " + projectileResult;
        }
        finally
        {
            MilkcraftEnchantingSystem.Close();
            for (int i = 0; i < player.inventory.Length; i++)
                player.inventory[i] = oldInventory[i];
            Item[][] banks = { player.bank.item, player.bank2.item, player.bank3.item, player.bank4.item };
            for (int bankIndex = 0; bankIndex < banks.Length; bankIndex++)
                for (int slot = 0; slot < banks[bankIndex].Length; slot++)
                    banks[bankIndex][slot] = oldBanks[bankIndex][slot];
            player.discountAvailable = oldDiscount;
            player.selectedItem = oldSelected;
            player.active = oldActive;
            player.dead = oldDead;
            player.position = oldPosition;
            player.statLife = oldLife;
            player.statLifeMax2 = oldLifeMax2;
            player.lifeSteal = oldLifeSteal;
            oldEnchantState.HealingCooldown = oldHealingCooldown;
            oldEnchantState.ExplosiveCooldown = oldExplosiveCooldown;
            Main.npc = oldNpcs;
            Main.item = oldItems;
            Main.projectile = oldProjectiles;
            Main.player = oldPlayerArray;
            foreach (Point p in preserved)
                Main.tile[p.X, p.Y].ClearEverything();
            Main.gameMenu = oldMenu;
            Main.playerInventory = oldPlayerInventory;
            Main.UIScale = oldUIScale;
            Main.netMode = oldNetMode;
            Main.myPlayer = oldMyPlayer;
            WorldGen.gen = oldGen;
            WorldGen.generatingWorld = oldGenerating;
            WorldGen.noTileActions = oldNoActions;
            WorldGen.destroyObject = oldDestroy;
        }
    }

    private static (int Width, int Height) GetNativePhysicalScreenSize()
    {
        const BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        int Read(string name, int fallback)
        {
            PropertyInfo property = typeof(Terraria.GameInput.PlayerInput).GetProperty(name, flags);
            object target = property?.GetMethod?.IsStatic == true ? null : Main.instance;
            object value = property?.GetValue(target);
            return value is int result && result > 0 ? result : fallback;
        }
        int width = Read("RealScreenWidth", Math.Max(1, (int)MathF.Round(Main.screenWidth * Main.UIScale)));
        int height = Read("RealScreenHeight", Math.Max(1, (int)MathF.Round(Main.screenHeight * Main.UIScale)));
        return (width, height);
    }

    private static void TestNativeEnchantUIInteractions(Player player, int tileX, int tileY,
        int physicalWidth, int physicalHeight, float uiScale)
    {
        int logicalWidth = Math.Max(1, (int)MathF.Floor(physicalWidth / uiScale));
        int logicalHeight = Math.Max(1, (int)MathF.Floor(physicalHeight / uiScale));
        void Frame(int x, int y, bool down = false, bool escape = false)
        {
            Point point = MilkcraftEnchantingSystem.DiagnosticWindowPoint(physicalWidth, physicalHeight, uiScale, x, y);
            MilkcraftEnchantingSystem.DiagnosticNativeUpdateUIFrame(point.X, point.Y, down, logicalWidth, logicalHeight, escape);
        }
        void Click(int x, int y)
        {
            Frame(x, y, true);
            Frame(x, y, false);
        }

        // One press/release sequence through PlayerInput.CacheOriginalInput → SetZoom_UI → UpdateUI.
        Frame(4, 4);
        Click(100, 150); // first eligible item row
        Click(400, 155); // first legal enchant button
        Click(493, 490); // Apply
        if (player.inventory[0].GetGlobalItem<MilkcraftEnchantedItem>().Kind != MilkcraftEnchant.Sharpness)
            throw new InvalidOperationException("native transformed mouse coordinates missed item/type/apply hitboxes");

        Click(623, 490); // Close button
        if (MilkcraftEnchantingSystem.DiagnosticVisible)
            throw new InvalidOperationException("native close-button mouse click did not close the enchanting UI");

        ModContent.GetInstance<MilkcraftEnchantingTableTile>().RightClick(tileX, tileY);
        Frame(5, 5, escape: true);
        if (MilkcraftEnchantingSystem.DiagnosticVisible)
            throw new InvalidOperationException("Escape key edge did not close the enchanting UI through UpdateUI");

        ModContent.GetInstance<MilkcraftEnchantingTableTile>().RightClick(tileX, tileY);
        Vector2 oldPosition = player.position;
        try
        {
            player.position += new Vector2(10000f, 0f);
            Frame(5, 5);
            if (MilkcraftEnchantingSystem.DiagnosticVisible)
                throw new InvalidOperationException("walking out of range did not close the enchanting UI");
        }
        finally
        {
            player.position = oldPosition;
        }
    }

    private static long CountCoins(Player player)
    {
        static long InventoryValue(Item[] items)
        {
            long total = 0L;
            foreach (Item item in items)
            {
                long unitValue = item.type switch
                {
                    ItemID.CopperCoin => 1L,
                    ItemID.SilverCoin => 100L,
                    ItemID.GoldCoin => 10_000L,
                    ItemID.PlatinumCoin => 1_000_000L,
                    _ => 0L
                };
                total += unitValue * Math.Max(0, item.stack);
            }
            return total;
        }

        return InventoryValue(player.inventory) + InventoryValue(player.bank.item) + InventoryValue(player.bank2.item)
            + InventoryValue(player.bank3.item) + InventoryValue(player.bank4.item);
    }

    private static string TestStableEnchantStatDeltas(Player actor)
    {
        static void ResetActor(Player player)
        {
            foreach (Item armorItem in player.armor) armorItem.TurnToAir();
            foreach (Item inventoryItem in player.inventory) inventoryItem.TurnToAir();
            Array.Clear(player.buffType, 0, player.buffType.Length);
            Array.Clear(player.buffTime, 0, player.buffTime.Length);
            player.selectedItem = 0;
            player.GetModPlayer<MilkcraftEnchantedPlayer>().ProjectileResistance = false;
            player.ResetEffects();
            player.UpdateEquips(player.whoAmI);
            player.UpdateArmorSets(player.whoAmI);
            PlayerLoader.PostUpdateEquips(player);
        }

        static Item Create(int type, MilkcraftEnchant kind)
        {
            Item item = new();
            item.SetDefaults(type);
            item.GetGlobalItem<MilkcraftEnchantedItem>().Kind = kind;
            return item;
        }

        static int WeaponDamage(Player player, int type, MilkcraftEnchant kind)
        {
            ResetActor(player);
            Item item = Create(type, kind);
            player.inventory[0] = item;
            player.selectedItem = 0;
            return player.GetWeaponDamage(item);
        }

        static float WeaponDamageHook(Player player, int type, MilkcraftEnchant kind)
        {
            ResetActor(player);
            Item item = Create(type, kind);
            StatModifier damage = StatModifier.Default;
            ItemLoader.ModifyWeaponDamage(item, player, ref damage);
            return damage.ApplyTo(100f);
        }

        static (float Damage, float Crit, float Endurance, int Defense, bool Resistance) Accessory(
            Player player, int type, MilkcraftEnchant kind, DamageClass damageClass)
        {
            ResetActor(player);
            Item item = Create(type, kind);
            ItemLoader.UpdateAccessory(item, player, false);
            return (player.GetTotalDamage(damageClass).ApplyTo(100f), player.GetTotalCritChance(damageClass),
                player.endurance, player.statDefense, player.GetModPlayer<MilkcraftEnchantedPlayer>().ProjectileResistance);
        }

        static void Near(float expected, float actual, string label, float tolerance = 1.25f)
        {
            if (Math.Abs(expected - actual) > tolerance)
                throw new InvalidOperationException($"enchantment {label} delta mismatch expected={expected:0.###} actual={actual:0.###}");
        }

        int swordType = ModContent.ItemType<KnightSword>();
        int bowType = ModContent.ItemType<IronBow>();
        int staffType = ModContent.ItemType<EnergyStaff>();
        int swordBase = WeaponDamage(actor, swordType, MilkcraftEnchant.None);
        int sharpDamage = WeaponDamage(actor, swordType, MilkcraftEnchant.Sharpness);
        int bowBase = WeaponDamage(actor, bowType, MilkcraftEnchant.None);
        int powerDamage = WeaponDamage(actor, bowType, MilkcraftEnchant.Power);
        int staffBase = WeaponDamage(actor, staffType, MilkcraftEnchant.None);
        int spellDamage = WeaponDamage(actor, staffType, MilkcraftEnchant.Spell);
        Near(swordBase * 1.06f, sharpDamage, "Sharpness weapon damage");
        Near(bowBase * 1.06f, powerDamage, "Power weapon damage");
        Near(staffBase * 1.06f, spellDamage, "Spell weapon damage");
        Near(100f, WeaponDamageHook(actor, swordType, MilkcraftEnchant.None), "Sharpness base weapon modifier", 0.001f);
        Near(106f, WeaponDamageHook(actor, swordType, MilkcraftEnchant.Sharpness), "Sharpness hook multiplier", 0.001f);
        Near(106f, WeaponDamageHook(actor, bowType, MilkcraftEnchant.Power), "Power hook multiplier", 0.001f);
        Near(106f, WeaponDamageHook(actor, staffType, MilkcraftEnchant.Spell), "Spell hook multiplier", 0.001f);
        var validKinds = new System.Collections.Generic.HashSet<MilkcraftEnchant>();
        void VerifyAllowed(int type, params MilkcraftEnchant[] expected)
        {
            Item candidate = Create(type, MilkcraftEnchant.None);
            MilkcraftEnchant[] actual = MilkcraftEnchantedItem.AllowedKinds(candidate).ToArray();
            if (!actual.OrderBy(kind => (byte)kind).SequenceEqual(expected.OrderBy(kind => (byte)kind)))
                throw new InvalidOperationException($"enchant menu availability mismatch for {candidate.ModItem?.Name}: expected={string.Join(',', expected)} actual={string.Join(',', actual)}");
            foreach (MilkcraftEnchant kind in actual) validKinds.Add(kind);
        }
        VerifyAllowed(swordType, MilkcraftEnchant.Sharpness, MilkcraftEnchant.Provocation);
        VerifyAllowed(bowType, MilkcraftEnchant.Power, MilkcraftEnchant.Stealth, MilkcraftEnchant.Explosive);
        VerifyAllowed(staffType, MilkcraftEnchant.Spell, MilkcraftEnchant.Stealth, MilkcraftEnchant.Ice, MilkcraftEnchant.Healing, MilkcraftEnchant.Explosive);

        int quiverType = ModContent.ItemType<IronQuiver>();
        int tomeType = ModContent.ItemType<BeginnerTome>();
        int shieldType = ModContent.ItemType<IronShield>();
        var quiverPower = Accessory(actor, quiverType, MilkcraftEnchant.Power, DamageClass.Ranged);
        var quiverBase = Accessory(actor, quiverType, MilkcraftEnchant.None, DamageClass.Ranged);
        var tomeSpell = Accessory(actor, tomeType, MilkcraftEnchant.Spell, DamageClass.Magic);
        var tomeBase = Accessory(actor, tomeType, MilkcraftEnchant.None, DamageClass.Magic);
        Near(6f, quiverPower.Damage - quiverBase.Damage, "quiver Power");
        Near(6f, tomeSpell.Damage - tomeBase.Damage, "tome Spell");
        var quiverCrit = Accessory(actor, quiverType, MilkcraftEnchant.Critical, DamageClass.Ranged);
        Near(4f, quiverCrit.Crit - quiverBase.Crit, "quiver Critical");
        var tomeCrit = Accessory(actor, tomeType, MilkcraftEnchant.Critical, DamageClass.Magic);
        Near(4f, tomeCrit.Crit - tomeBase.Crit, "tome Critical");
        var shieldCrit = Accessory(actor, shieldType, MilkcraftEnchant.Critical, DamageClass.Melee);
        var shieldBase = Accessory(actor, shieldType, MilkcraftEnchant.None, DamageClass.Melee);
        Near(4f, shieldCrit.Crit - shieldBase.Crit, "shield melee Critical");
        var shieldWall = Accessory(actor, shieldType, MilkcraftEnchant.ShieldWall, DamageClass.Melee);
        Near(0.04f, shieldWall.Endurance - shieldBase.Endurance, "Shield Wall endurance", 0.001f);
        var armor = Accessory(actor, shieldType, MilkcraftEnchant.Armor, DamageClass.Melee);
        Near(4f, armor.Defense - shieldBase.Defense, "Armor defense");
        var resistance = Accessory(actor, shieldType, MilkcraftEnchant.Resistance, DamageClass.Melee);
        if (!resistance.Resistance || shieldBase.Resistance)
            throw new InvalidOperationException("Resistance accessory did not toggle only its functional projectile-resistance flag");
        VerifyAllowed(shieldType, MilkcraftEnchant.Critical, MilkcraftEnchant.ShieldWall, MilkcraftEnchant.Armor, MilkcraftEnchant.Resistance);
        VerifyAllowed(quiverType, MilkcraftEnchant.Power, MilkcraftEnchant.Critical, MilkcraftEnchant.Armor, MilkcraftEnchant.Resistance);
        VerifyAllowed(tomeType, MilkcraftEnchant.Spell, MilkcraftEnchant.Critical, MilkcraftEnchant.Armor, MilkcraftEnchant.Resistance);
        if (validKinds.Count != 12 || validKinds.Contains(MilkcraftEnchant.None))
            throw new InvalidOperationException($"valid equipment paths do not expose all 12 enchantments: {string.Join(',', validKinds)}");

        ResetActor(actor);
        Item heldSword = Create(swordType, MilkcraftEnchant.Provocation);
        actor.inventory[0] = heldSword; actor.selectedItem = 0;
        ItemLoader.UpdateInventory(heldSword, actor);
        int defenseAfterProvocation = actor.statDefense.Positive - actor.statDefense.Negative;
        if (actor.aggro != 400 || defenseAfterProvocation != 4)
            throw new InvalidOperationException($"Provocation held-only delta mismatch aggro={actor.aggro} defense={defenseAfterProvocation}");
        ResetActor(actor);
        float baselineMoveSpeed = actor.moveSpeed;
        Item heldBow = Create(bowType, MilkcraftEnchant.Stealth);
        actor.inventory[0] = heldBow; actor.selectedItem = 0;
        ItemLoader.UpdateInventory(heldBow, actor);
        if (actor.aggro != -400 || Math.Abs(actor.moveSpeed - baselineMoveSpeed - 0.04f) > 0.001f)
            throw new InvalidOperationException($"Stealth held-only delta mismatch aggro={actor.aggro} moveSpeed={baselineMoveSpeed:0.###}->{actor.moveSpeed:0.###}");

        return $"stable-enchant-deltas=PASS damage-hook=100->106% sharpness/power/spell;actualIntegerWeaponDamage={swordBase}->{sharpDamage},{bowBase}->{powerDamage},{staffBase}->{spellDamage} (integer rounding) allowedKinds=12/12 category-valid paths=PASS accessoryCrit+4=PASS powerSpellAccessories+6pct=PASS shieldWall+0.04=PASS armor+4=PASS resistance-flag=PASS provocation-held=400-aggro/+4def stealth-held=-400-aggro/+0.04speed";
    }

    private static string TestNativeProjectileEnchantHits(Player owner)
    {
        owner.statLifeMax2 = 100;
        owner.statLife = 50;
        owner.lifeSteal = 2f;
        owner.GetModPlayer<MilkcraftEnchantedPlayer>().HealingCooldown = 0;

        Item staff = new();
        staff.SetDefaults(ModContent.ItemType<EnergyStaff>());
        staff.GetGlobalItem<MilkcraftEnchantedItem>().Kind = MilkcraftEnchant.Ice;
        NPC frost = MakeNpc(90, NPCID.BlueSlime, new Vector2(820f, 220f));
        Main.npc[90] = frost;
        Projectile iceProjectile = SpawnNativeUseProjectile(owner, staff, staff.shoot, frost.Center, 10, ItemID.None);
        if (iceProjectile.GetGlobalProjectile<MilkcraftEnchantedProjectile>().DiagnosticKind != MilkcraftEnchant.Ice)
            throw new InvalidOperationException("actual NewProjectile/EntitySource_ItemUse_WithAmmo did not bind Ice");
        iceProjectile.Damage();
        if (!frost.HasBuff(ModContent.BuffType<MilkcraftFrosted>()))
            throw new InvalidOperationException("actual Projectile.Damage hit did not apply Frosted");
        NPC immune = MakeNpc(91, NPCID.BlueSlime, new Vector2(850f, 220f));
        immune.boss = true;
        Main.npc[91] = immune;
        staff.GetGlobalItem<MilkcraftEnchantedItem>().Kind = MilkcraftEnchant.Ice;
        SpawnNativeUseProjectile(owner, staff, staff.shoot, immune.Center, 10, ItemID.None).Damage();
        if (immune.HasBuff(ModContent.BuffType<MilkcraftFrosted>()))
            throw new InvalidOperationException("Ice enchant applied Frosted to an immune boss");
        string frostMotion = TestFrostedNpcMotion();

        staff.GetGlobalItem<MilkcraftEnchantedItem>().Kind = MilkcraftEnchant.Healing;
        NPC healTarget = MakeNpc(94, NPCID.BlueSlime, new Vector2(820f, 270f));
        NPC healCooldownTarget = MakeNpc(95, NPCID.BlueSlime, new Vector2(860f, 270f));
        Main.npc[94] = healTarget;
        Main.npc[95] = healCooldownTarget;
        Projectile healProjectile = SpawnNativeUseProjectile(owner, staff, staff.shoot, healTarget.Center, 10, ItemID.None);
        if (healProjectile.GetGlobalProjectile<MilkcraftEnchantedProjectile>().DiagnosticKind != MilkcraftEnchant.Healing)
            throw new InvalidOperationException("actual staff projectile did not inherit Healing enchant");
        healProjectile.Damage();
        int afterHeal = owner.statLife;
        SpawnNativeUseProjectile(owner, staff, staff.shoot, healCooldownTarget.Center, 10, ItemID.None).Damage();
        if (afterHeal != 52 || owner.statLife != afterHeal || owner.GetModPlayer<MilkcraftEnchantedPlayer>().HealingCooldown <= 0)
            throw new InvalidOperationException($"actual Projectile.Damage healing failed 2-life/120-tick cooldown check: life={50}->{afterHeal}->{owner.statLife} cooldown={owner.GetModPlayer<MilkcraftEnchantedPlayer>().HealingCooldown}");

        // A lethal damaging hit is still a valid healing proc; full-health and
        // exhausted life-steal cases must leave cooldown and budget untouched.
        owner.statLife = 50;
        owner.lifeSteal = 2f;
        owner.GetModPlayer<MilkcraftEnchantedPlayer>().HealingCooldown = 0;
        NPC lethalHealTarget = MakeNpc(99, NPCID.BlueSlime, new Vector2(820f, 320f));
        lethalHealTarget.lifeMax = 25;
        lethalHealTarget.life = 1;
        Main.npc[99] = lethalHealTarget;
        Projectile lethalHealProjectile = SpawnNativeUseProjectile(owner, staff, staff.shoot, lethalHealTarget.Center, 20, ItemID.None);
        lethalHealProjectile.Damage();
        int afterLethalHeal = owner.statLife;
        int lethalHealCooldown = owner.GetModPlayer<MilkcraftEnchantedPlayer>().HealingCooldown;
        if (afterLethalHeal != 52 || lethalHealTarget.active || lethalHealCooldown != 120)
            throw new InvalidOperationException($"healing enchant missed a real lethal hit: ownerLife=50->{afterLethalHeal}, targetActive={lethalHealTarget.active}, cooldown={lethalHealCooldown}");

        owner.statLife = owner.statLifeMax2;
        owner.lifeSteal = 2f;
        owner.GetModPlayer<MilkcraftEnchantedPlayer>().HealingCooldown = 0;
        NPC fullHealthTarget = MakeNpc(100, NPCID.BlueSlime, new Vector2(850f, 320f));
        Main.npc[100] = fullHealthTarget;
        SpawnNativeUseProjectile(owner, staff, staff.shoot, fullHealthTarget.Center, 10, ItemID.None).Damage();
        if (owner.GetModPlayer<MilkcraftEnchantedPlayer>().HealingCooldown != 0 || owner.lifeSteal != 2f)
            throw new InvalidOperationException("healing at full health consumed cooldown or life-steal budget");

        owner.statLife = 50;
        owner.lifeSteal = 0.5f;
        owner.GetModPlayer<MilkcraftEnchantedPlayer>().HealingCooldown = 0;
        NPC noBudgetTarget = MakeNpc(101, NPCID.BlueSlime, new Vector2(880f, 320f));
        Main.npc[101] = noBudgetTarget;
        SpawnNativeUseProjectile(owner, staff, staff.shoot, noBudgetTarget.Center, 10, ItemID.None).Damage();
        if (owner.GetModPlayer<MilkcraftEnchantedPlayer>().HealingCooldown != 0 || owner.lifeSteal != 0.5f || owner.statLife != 50)
            throw new InvalidOperationException("healing without a full life-steal point changed player state");

        Item bow = new();
        bow.SetDefaults(ModContent.ItemType<IronBow>());
        bow.GetGlobalItem<MilkcraftEnchantedItem>().Kind = MilkcraftEnchant.Explosive;
        NPC mainTarget = MakeNpc(92, NPCID.BlueSlime, new Vector2(900f, 220f));
        NPC splashTarget = MakeNpc(93, NPCID.BlueSlime, new Vector2(940f, 220f));
        NPC immuneSplash = MakeNpc(98, NPCID.BlueSlime, new Vector2(930f, 260f));
        mainTarget.lifeMax = mainTarget.life = 1000;
        splashTarget.lifeMax = splashTarget.life = 1000;
        immuneSplash.lifeMax = immuneSplash.life = 1000;
        immuneSplash.dontTakeDamage = true;
        Main.npc[92] = mainTarget;
        Main.npc[93] = splashTarget;
        Main.npc[98] = immuneSplash;
        int splashLife = splashTarget.life;
        int immuneSplashLife = immuneSplash.life;
        Projectile explosiveProjectile = SpawnNativeUseProjectile(owner, bow, ProjectileID.WoodenArrowFriendly, mainTarget.Center, 100, ItemID.WoodenArrow);
        if (explosiveProjectile.GetGlobalProjectile<MilkcraftEnchantedProjectile>().DiagnosticKind != MilkcraftEnchant.Explosive)
            throw new InvalidOperationException("actual bow projectile did not inherit Explosive enchant");
        explosiveProjectile.Damage();
        int afterSplash = splashTarget.life;
        if (afterSplash >= splashLife || owner.GetModPlayer<MilkcraftEnchantedPlayer>().ExplosiveCooldown <= 0
            || immuneSplash.life != immuneSplashLife)
            throw new InvalidOperationException($"actual bow Projectile.Damage did not deal 48px owner-authoritative splash: {splashLife}->{afterSplash}");
        NPC secondMain = MakeNpc(96, NPCID.BlueSlime, new Vector2(1000f, 220f));
        NPC cooldownSplash = MakeNpc(97, NPCID.BlueSlime, new Vector2(1040f, 220f));
        secondMain.lifeMax = secondMain.life = 1000;
        cooldownSplash.lifeMax = cooldownSplash.life = 1000;
        Main.npc[96] = secondMain;
        Main.npc[97] = cooldownSplash;
        int blockedSplashLife = cooldownSplash.life;
        SpawnNativeUseProjectile(owner, bow, ProjectileID.WoodenArrowFriendly, secondMain.Center, 100, ItemID.WoodenArrow).Damage();
        if (cooldownSplash.life != blockedSplashLife)
            throw new InvalidOperationException($"second explosive hit ignored the shared cooldown: splash={blockedSplashLife}->{cooldownSplash.life}");
        string resistanceMotion = TestProjectileResistanceHurt(owner);
        return $"actual-NewProjectile+EntitySource_ItemUse_WithAmmo+Projectile.Damage=PASS ice-buff=PASS boss-immune=PASS continuousFrostMotion={frostMotion} heal=PASS nonlethal=50->{afterHeal} lethal=50->{afterLethalHeal} fullHealth-no-spend=PASS no-budget-no-spend=PASS sharedCD=PASS explosive-areaHit=PASS splashLife={splashLife}->{afterSplash} invulnerableSplashSkipped=PASS secondSplashBlockedByCD=PASS {resistanceMotion} owner-local-singleplayer=PASS";
    }

    private static Projectile SpawnNativeUseProjectile(Player owner, Item item, int projectileType, Vector2 center, int damage, int ammoItemType)
    {
        IEntitySource source = new EntitySource_ItemUse_WithAmmo(owner, item, ammoItemType, "ProgressionEnchantDiagnostics");
        int index = Projectile.NewProjectile(source, center, Vector2.Zero, projectileType, damage, 0f, owner.whoAmI);
        if (index < 0 || index >= Main.projectile.Length || !Main.projectile[index].active)
            throw new InvalidOperationException($"native Projectile.NewProjectile failed: type={projectileType}, item={item.type}");
        Projectile projectile = Main.projectile[index];
        projectile.Center = center;
        projectile.velocity = Vector2.Zero;
        projectile.width = 8;
        projectile.height = 8;
        projectile.damage = damage;
        projectile.knockBack = 0f;
        projectile.owner = owner.whoAmI;
        projectile.friendly = true;
        projectile.hostile = false;
        projectile.tileCollide = false;
        projectile.timeLeft = 60;
        return projectile;
    }

    private static string TestFrostedNpcMotion()
    {
        const int normalSlot = 110;
        const int frostedSlot = 111;
        int buffType = ModContent.BuffType<MilkcraftFrosted>();
        NPC normal = new();
        NPC frosted = new();
        normal.SetDefaults(NPCID.BlueSlime);
        frosted.SetDefaults(NPCID.BlueSlime);
        normal.whoAmI = normalSlot;
        frosted.whoAmI = frostedSlot;
        normal.active = frosted.active = true;
        normal.aiStyle = frosted.aiStyle = 0; // isolate vanilla NPC update physics from random slime jumps
        normal.noGravity = frosted.noGravity = true;
        normal.noTileCollide = frosted.noTileCollide = true;
        normal.target = frosted.target = Main.myPlayer;
        normal.velocity = frosted.velocity = new Vector2(10f, 0f);
        normal.position = new Vector2(800f, 360f);
        frosted.position = new Vector2(900f, 360f);
        frosted.AddBuff(buffType, 120);
        Main.npc[normalSlot] = normal;
        Main.npc[frostedSlot] = frosted;

        MethodInfo updateNpc = typeof(NPC).GetMethod("UpdateNPC", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null, types: new[] { typeof(int) }, modifiers: null);
        if (updateNpc is null)
        {
            string available = string.Join(";", typeof(NPC).GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(method => method.Name.Contains("UpdateNPC", StringComparison.OrdinalIgnoreCase))
                .Select(method => method.Name + "(" + string.Join(",", method.GetParameters().Select(p => p.ParameterType.Name)) + ")"));
            throw new InvalidOperationException("actual NPC.UpdateNPC(int) entry unavailable: " + available);
        }

        float normalStart = normal.Center.X;
        float frostStart = frosted.Center.X;
        for (int tick = 0; tick < 12; tick++)
        {
            updateNpc.Invoke(normal, new object[] { normalSlot });
            updateNpc.Invoke(frosted, new object[] { frostedSlot });
        }
        float normalFirst = normal.Center.X - normalStart;
        float frostFirst = frosted.Center.X - frostStart;
        float ratio = frostFirst / normalFirst;
        if (ratio < 0.75f || ratio > 0.85f)
            throw new InvalidOperationException($"actual NPC.UpdateNPC movement slow was not 20% over 12 consecutive frames: normalDx={normalFirst:0.###} frostedDx={frostFirst:0.###} ratio={ratio:0.###} buffTime={frosted.buffTime[frosted.FindBuffIndex(buffType)]}");

        for (int tick = 12; tick < 120; tick++)
        {
            updateNpc.Invoke(normal, new object[] { normalSlot });
            updateNpc.Invoke(frosted, new object[] { frostedSlot });
        }
        bool expired = !frosted.HasBuff(buffType);
        float normalBeforeRecovery = normal.Center.X;
        float frostBeforeRecovery = frosted.Center.X;
        for (int tick = 0; tick < 8; tick++)
        {
            // Reset identical horizontal drive immediately before each real
            // native update. The normal-AI fixture may otherwise choose to
            // stop autonomously after 120 frames, leaving a meaningless 0/0.
            normal.velocity = new Vector2(10f, 0f);
            frosted.velocity = new Vector2(10f, 0f);
            updateNpc.Invoke(normal, new object[] { normalSlot });
            updateNpc.Invoke(frosted, new object[] { frostedSlot });
        }
        float normalRecovery = normal.Center.X - normalBeforeRecovery;
        float frostRecovery = frosted.Center.X - frostBeforeRecovery;
        float recoveryRatio = frostRecovery / normalRecovery;
        if (!expired || float.IsNaN(recoveryRatio) || recoveryRatio < 0.95f || recoveryRatio > 1.05f)
            throw new InvalidOperationException($"actual NPC.UpdateNPC Frosted expiry/recovery failed: expired={expired} recoveryRatio={recoveryRatio:0.###} time={frosted.buffTime[frosted.FindBuffIndex(buffType)]}");
        string defaultSlime = TestDefaultBlueSlimeJumpLanding(updateNpc, buffType);
        Vector2 normalOrigStep = MilkcraftFrostedGlobalNPC.DiagnosticRunOrigDisplacement(new Vector2(10f, 0f), new Vector2(10f, 0f), 24, 18);
        Vector2 teleportOrigStep = MilkcraftFrostedGlobalNPC.DiagnosticRunOrigDisplacement(new Vector2(200f, 0f), Vector2.Zero, 24, 18);
        if (Vector2.Distance(normalOrigStep, new Vector2(8f, 0f)) > 0.01f || Vector2.Distance(teleportOrigStep, new Vector2(200f, 0f)) > 0.01f)
            throw new InvalidOperationException($"frost orig-callback displacement guard failed: normal={normalOrigStep} teleport={teleportOrigStep}");
        return $"NPC.UpdateNPC-physics=PASS isolated-physics 12f normalDx={normalFirst:0.###} frostedDx={frostFirst:0.###} ratio={ratio:0.###}; 120f debuff-expired={expired}; 8f recoveryRatio={recoveryRatio:0.###}; default-AI={defaultSlime}; orig-callback-normal-step={normalOrigStep} teleport-preserved={teleportOrigStep}";
    }

    private static string TestDefaultBlueSlimeJumpLanding(MethodInfo updateNpc, int buffType)
    {
        const int floorY = 100;
        const int leftX = 45;
        const int rightX = 85;
        int npcArrayLength = Main.npc.Length;
        int projectileArrayLength = Main.projectile.Length;
        var floorCells = new System.Collections.Generic.List<Point>();
        for (int x = leftX; x <= rightX; x++)
        {
            if (!WorldGen.InWorld(x, floorY + 1, 10))
                throw new InvalidOperationException("Frosted BlueSlime floor is outside the native fixture world");
            if (Main.tile[x, floorY].HasTile)
                throw new InvalidOperationException($"Frosted BlueSlime floor fixture would overwrite occupied tile {x},{floorY}");
            floorCells.Add(new Point(x, floorY));
            Tile tile = Main.tile[x, floorY];
            tile.HasTile = true;
            tile.TileType = TileID.Stone;
        }

        try
        {
            // Synthetic fixtures do not have generated world layers. Keep the
            // default AI/gravity/collision intact but model normal surface
            // gravity explicitly for this isolated movement test.
            (float Dx, float Dy, float Path, float MinY, float MaxY, int Jumps, bool Landed, int AiStyle, bool Active, bool Expired) Run(bool chilled)
            {
                Main.npc = Enumerable.Range(0, npcArrayLength).Select(_ => new NPC()).ToArray();
                Main.projectile = Enumerable.Range(0, projectileArrayLength).Select(_ => new Projectile()).ToArray();
                Main.rand = new UnifiedRandom(20261008);
                Player actor = Main.player[Main.myPlayer];
                actor.active = true;
                actor.dead = false;
                // Keep the player target far enough to one side that the
                // default BlueSlime AI produces measurable horizontal travel.
                actor.position = new Vector2(1120f, floorY * 16f - actor.height);

                NPC slime = new();
                slime.SetDefaults(NPCID.BlueSlime);
                slime.whoAmI = 112;
                slime.active = true;
                slime.GravityIgnoresSpace = true;
                slime.target = Main.myPlayer;
                slime.Center = new Vector2(760f, floorY * 16f - slime.height * 0.5f);
                Main.npc[slime.whoAmI] = slime;
                if (chilled)
                    slime.AddBuff(buffType, 120);

                Vector2 start = slime.Center;
                float minY = slime.position.Y;
                float maxY = minY;
                float path = 0f;
                int jumps = 0;
                bool landed = false;
                float lastVy = slime.velocity.Y;
                for (int tick = 0; tick < 120; tick++)
                {
                    Vector2 before = slime.position;
                    updateNpc.Invoke(slime, new object[] { slime.whoAmI });
                    path += Vector2.Distance(before, slime.position);
                    minY = Math.Min(minY, slime.position.Y);
                    maxY = Math.Max(maxY, slime.position.Y);
                    if (lastVy >= 0f && slime.velocity.Y < -0.5f) jumps++;
                    landed |= jumps > 0 && slime.collideY;
                    lastVy = slime.velocity.Y;
                    if (!slime.active) break;
                }
                float dx = slime.Center.X - start.X;
                float dy = slime.Center.Y - start.Y;
                bool expired = !slime.HasBuff(buffType);
                if (chilled && !expired)
                    throw new InvalidOperationException($"Frosted survived its configured 120 native NPC.UpdateNPC ticks; remaining={slime.buffTime[slime.FindBuffIndex(buffType)]}");
                return (dx, dy, path, minY, maxY, jumps, landed, slime.aiStyle, slime.active, expired);
            }

            var normal = Run(chilled: false);
            var chilled = Run(chilled: true);
            // The default slime's AI chooses vertical hops but remains in place
            // horizontally in this fixture. Compare its actual jump/gravity
            // displacement; horizontal travel is measured by the straight-AI
            // 12-frame case above.
            float ratio = normal.Path > 0.1f ? chilled.Path / normal.Path : float.NaN;
            bool landedAndJumped = normal.Jumps > 0 && chilled.Jumps > 0
                && normal.Landed && chilled.Landed && normal.MaxY - normal.MinY > 4f && chilled.MaxY - chilled.MinY > 4f;
            if (!normal.Active || !chilled.Active || !landedAndJumped || float.IsNaN(ratio)
                || ratio < 0.72f || ratio > 0.88f || !chilled.Expired)
                throw new InvalidOperationException($"default BlueSlime 120-frame native update comparison failed: normal={normal} frost={chilled} dxRatio={ratio:0.###} jumped/landed={landedAndJumped}");
            return $"default BlueSlime aiStyle={normal.AiStyle} 120 native updates, jumps={normal.Jumps}/{chilled.Jumps}, landed={normal.Landed}/{chilled.Landed}, verticalSpan={normal.MaxY-normal.MinY:0.##}/{chilled.MaxY-chilled.MinY:0.##}, path={normal.Path:0.##}/{chilled.Path:0.##}, netXY=({normal.Dx:0.##},{normal.Dy:0.##})/({chilled.Dx:0.##},{chilled.Dy:0.##}), pathRatio={ratio:0.###}, Frosted expired=PASS";
        }
        finally
        {
            foreach (Point point in floorCells)
                Main.tile[point.X, point.Y].ClearEverything();
        }
    }

    private static string TestProjectileResistanceHurt(Player player)
    {
        UnifiedRandom previousRandom = Main.rand;
        int Hit(int projectileSlot, MilkcraftEnchant enchant, bool projectileSource)
        {
            player.active = true;
            player.dead = false;
            player.ResetEffects();
            foreach (Item armorItem in player.armor)
                armorItem.TurnToAir();
            player.statLifeMax2 = 1000;
            player.statLife = 1000;
            player.statDefense = default;
            player.endurance = 0f;
            player.immune = false;
            player.immuneTime = 0;
            Array.Clear(player.hurtCooldowns);
            Item shield = new();
            shield.SetDefaults(ModContent.ItemType<IronShield>());
            shield.GetGlobalItem<MilkcraftEnchantedItem>().Kind = enchant;
            ItemLoader.UpdateAccessory(shield, player, false);
            Main.rand = new UnifiedRandom(20261008); // pair identical native damage-variance rolls

            int before = player.statLife;
            if (projectileSource)
            {
                Projectile source = new();
                source.SetDefaults(ProjectileID.Bullet);
                source.whoAmI = projectileSlot;
                source.active = true;
                source.hostile = true;
                source.friendly = false;
                source.owner = Main.maxPlayers; // native hostile, ownerless projectile sentinel
                source.Center = player.Center;
                source.velocity = Vector2.Zero;
                source.damage = 100;
                source.knockBack = 0f;
                source.width = 8;
                source.height = 8;
                source.tileCollide = false;
                source.timeLeft = 10;
                Main.projectile[projectileSlot] = source;
                source.Damage(); // goes through GlobalProjectile.ModifyHitPlayer and native Player.Hurt
            }
            else
            {
                NPC source = new();
                source.SetDefaults(NPCID.BlueSlime);
                source.whoAmI = 127;
                source.active = true;
                source.Center = player.Center + new Vector2(30f, 0f);
                Main.npc[source.whoAmI] = source;
                player.Hurt(PlayerDeathReason.ByNPC(source.whoAmI), 100, 1, pvp: false, quiet: true,
                    cooldownCounter: 0, dodgeable: false, armorPenetration: 0f, scalingArmorPenetration: 0f, knockback: 0f);
            }
            return before - player.statLife;
        }
        int normalDamage = Hit(122, MilkcraftEnchant.None, projectileSource: true);
        int reducedDamage = Hit(123, MilkcraftEnchant.Resistance, projectileSource: true);
        float projectileRatio = normalDamage > 0 ? reducedDamage / (float)normalDamage : 0f;
        int contactBase = Hit(124, MilkcraftEnchant.None, projectileSource: false);
        int contactResisted = Hit(125, MilkcraftEnchant.Resistance, projectileSource: false);
        float contactRatio = contactBase > 0 ? contactResisted / (float)contactBase : 0f;
        int contactShieldWallBase = Hit(126, MilkcraftEnchant.None, projectileSource: false);
        int contactShieldWall = Hit(127, MilkcraftEnchant.ShieldWall, projectileSource: false);
        float shieldWallRatio = contactShieldWallBase > 0 ? contactShieldWall / (float)contactShieldWallBase : 0f;
        Main.rand = previousRandom;
        if (normalDamage < 90 || reducedDamage <= 0 || projectileRatio < 0.92f || projectileRatio > 0.96f
            || contactBase < 90 || contactResisted < 90 || contactRatio < 0.98f || contactRatio > 1.02f
            || shieldWallBaseInvalid(contactShieldWallBase, contactShieldWall, shieldWallRatio))
            throw new InvalidOperationException($"Resistance/ShieldWall actual-hit mismatch: hostile owner255 Projectile.Damage {normalDamage}->{reducedDamage} ratio={projectileRatio:0.###}; ByNPC {contactBase}->{contactResisted} ratio={contactRatio:0.###}; ShieldWall ByNPC {contactShieldWallBase}->{contactShieldWall} ratio={shieldWallRatio:0.###}");
        return $"actual-Projectile.Damage-to-Player resistance=PASS owner255 hostile projectile {normalDamage}->{reducedDamage} ratio={projectileRatio:0.###}; ByNPC unaffected={contactBase}->{contactResisted} ratio={contactRatio:0.###}; ShieldWall actual-Hurt ratio={shieldWallRatio:0.###}";
    }

    private static bool shieldWallBaseInvalid(int baseline, int reduced, float ratio)
        => baseline < 90 || reduced <= 0 || ratio < 0.94f || ratio > 0.98f;

    private static NPC MakeNpc(int index, int type, Vector2 position)
    {
        NPC npc = new();
        npc.whoAmI = index;
        npc.SetDefaults(type);
        npc.whoAmI = index;
        npc.position = position;
        npc.active = true;
        npc.life = npc.lifeMax;
        return npc;
    }
}
