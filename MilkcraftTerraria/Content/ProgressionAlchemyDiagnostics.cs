using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;
using Terraria.ObjectData;
using MilkcraftTerraria.Content.Items;
using MilkcraftTerraria.Content.Tiles;

namespace MilkcraftTerraria.Content;

internal static class ProgressionAlchemyDiagnostics
{
    public static string Run()
    {
        const int floorY = 100;
        const int firstX = 420;
        const int lastX = 600;
        const int activeFarOffset = 14;
        int tileType = ModContent.TileType<MilkcraftAlchemyTableTile>();
        int alcoholType = ModContent.ItemType<Alcohol>();
        Player actor = Main.LocalPlayer;
        MilkcraftAlchemyWorldSystem system = ModContent.GetInstance<MilkcraftAlchemyWorldSystem>();

        bool oldMenu = Main.gameMenu;
        int oldNetMode = Main.netMode;
        int oldMyPlayer = Main.myPlayer;
        bool oldGen = WorldGen.gen;
        bool oldGenerating = WorldGen.generatingWorld;
        bool oldNoTileActions = WorldGen.noTileActions;
        bool oldDestroy = WorldGen.destroyObject;
        Vector2 oldScreen = Main.screenPosition;
        int oldMouseX = Main.mouseX, oldMouseY = Main.mouseY;
        bool oldActive = actor.active, oldDead = actor.dead;
        Vector2 oldPosition = actor.position;
        int oldSelected = actor.selectedItem;
        Item oldSelectedItem = actor.inventory[0].Clone();
        TagCompound oldActiveState = new();
        system.SaveWorldData(oldActiveState);
        Item[] oldItems = Main.item;
        var placedOrigins = new HashSet<Point16>();
        var spawnedDropsBefore = Main.item.Count(item => item.active && item.type == ModContent.ItemType<MilkcraftAlchemyTable>());

        try
        {
            if (Main.maxTilesX <= lastX + 20 || Main.maxTilesY <= floorY + 20)
                throw new InvalidOperationException($"test tilemap too small: {Main.maxTilesX}x{Main.maxTilesY}");
            for (int x = firstX - 4; x <= lastX + 8; x++)
            for (int y = floorY - 8; y <= floorY + 2; y++)
            {
                Tile tile = Main.tile[x, y];
                if (tile.HasTile || tile.WallType != WallID.None)
                    throw new InvalidOperationException($"alchemy fixture arena is occupied at {x},{y}; refusing to alter it");
            }

            Main.item = Enumerable.Range(0, Math.Max(400, Main.maxItems + 1)).Select(_ => new Item()).ToArray();
            Main.gameMenu = false;
            Main.netMode = NetmodeID.SinglePlayer;
            Main.myPlayer = actor.whoAmI;
            Main.screenPosition = Vector2.Zero;
            WorldGen.gen = false;
            WorldGen.generatingWorld = false;
            WorldGen.noTileActions = false;
            WorldGen.destroyObject = false;

            for (int x = firstX - 4; x <= lastX + 8; x++)
            {
                Tile floor = Main.tile[x, floorY];
                floor.HasTile = true;
                floor.TileType = TileID.Stone;
            }

            int dormantPlaceX = firstX;
            int farPlaceX = firstX + activeFarOffset;
            int partialPlaceX = firstX + 42;
            Point16 dormant = PlaceAndFind(dormantPlaceX, floorY - 1, tileType, placedOrigins);
            Point16 far = PlaceAndFind(farPlaceX, floorY - 1, tileType, placedOrigins);
            Point16 partial = PlaceAndFind(partialPlaceX, floorY - 1, tileType, placedOrigins);

            actor.active = true;
            actor.dead = false;
            actor.selectedItem = 0;
            actor.inventory[0].SetDefaults(alcoholType);
            actor.inventory[0].stack = 2;
            actor.position = new Vector2((far.X + 1) * 16f, (far.Y + 1) * 16f);
            Point16 farCell = new(far.X + 1, far.Y + 1);
            AimAt(farCell);
            Alcohol alcohol = ModContent.GetInstance<Alcohol>();
            if (!alcohol.CanUseItem(actor) || alcohol.UseItem(actor) != true)
                throw new InvalidOperationException("Alcohol left-click did not activate a targeted complete table");
            if (!MilkcraftAlchemyWorldSystem.IsActive(far) || actor.inventory[0].stack != 1)
                throw new InvalidOperationException($"left-click activation/consumption mismatch: active={MilkcraftAlchemyWorldSystem.IsActive(far)} remaining={actor.inventory[0].stack}");
            if (!Main.tileLighted[tileType])
                throw new InvalidOperationException("active alchemy tile is not registered for native tile-light evaluation");
            float lightR = 0f, lightG = 0f, lightB = 0f;
            ModContent.GetInstance<MilkcraftAlchemyTableTile>().ModifyLight(farCell.X, farCell.Y, ref lightR, ref lightG, ref lightB);
            if (lightR <= 0f || lightG <= lightR || lightB <= lightR)
                throw new InvalidOperationException($"active alchemy RGB light is not cyan: {lightR:F3},{lightG:F3},{lightB:F3}");

            var persisted = new TagCompound();
            system.SaveWorldData(persisted);
            MilkcraftAlchemyWorldSystem.ApplyActivationSync(far, false);
            system.LoadWorldData(persisted);
            if (!MilkcraftAlchemyWorldSystem.IsActive(far))
                throw new InvalidOperationException("active table did not survive ModSystem save/load round-trip");

            actor.position = new Vector2((dormant.X + 1) * 16f, (dormant.Y + 1) * 16f);
            if (MilkcraftAlchemyWorldSystem.PlayerNearActiveTable(actor))
                throw new InvalidOperationException("a farther active table incorrectly unlocked crafting beside a nearby dormant table");
            actor.position = new Vector2((far.X + 1) * 16f, (far.Y + 1) * 16f);
            if (!MilkcraftAlchemyWorldSystem.PlayerNearActiveTable(actor))
                throw new InvalidOperationException("near active table failed the Player.AdjTiles-range test");

            // Leave a deliberately incomplete footprint and verify Alcohol cannot consume or activate it.
            int missingX = partial.X + 2, missingY = partial.Y + 1;
            Main.tile[missingX, missingY].ClearTile();
            actor.position = new Vector2((partial.X + 1) * 16f, (partial.Y + 1) * 16f);
            Point16 partialCell = new(partial.X + 1, partial.Y + 1);
            AimAt(partialCell);
            if (alcohol.CanUseItem(actor) || MilkcraftAlchemyWorldSystem.CanUseAlcoholOnTable(actor, partialCell.X, partialCell.Y)
                || MilkcraftAlchemyWorldSystem.IsActive(partial) || actor.inventory[0].stack != 1)
                throw new InvalidOperationException("incomplete 3x3 table accepted Alcohol or consumed an item");

            // Right-click remains a supported interaction and consumes one inventory item.
            actor.position = new Vector2((dormant.X + 1) * 16f, (dormant.Y + 1) * 16f);
            actor.inventory[0].stack = 1;
            MilkcraftAlchemyWorldSystem.TryActivateFromLocalClick(dormant.X + 1, dormant.Y + 1);
            if (!MilkcraftAlchemyWorldSystem.IsActive(dormant) || actor.inventory[0].stack != 0)
                throw new InvalidOperationException("right-click table interaction did not activate and consume exactly one inventory Alcohol");

            // Break a non-origin cell through the native tile path; the replacement must be dormant.
            WorldGen.KillTile(far.X + 2, far.Y + 1, false, false, false);
            WorldGen.destroyObject = false;
            InvokeNativeMultitileCleanup(tileType, far.X, far.Y);
            if (MilkcraftAlchemyWorldSystem.IsActive(far))
                throw new InvalidOperationException("breaking a non-origin table cell left the old activation registered");
            placedOrigins.Remove(far);
            Point16 replacement = PlaceAndFind(farPlaceX, floorY - 1, tileType, placedOrigins);
            if (replacement != far || MilkcraftAlchemyWorldSystem.IsActive(replacement))
                throw new InvalidOperationException("replacing a broken table did not create a dormant table at the same footprint");

            // Exercise native KillTile/KillMultiTile cleanup at each corner and the center.
            (int X, int Y)[] breakCells = { (0, 0), (2, 0), (0, 2), (2, 2), (1, 1) };
            for (int n = 0; n < breakCells.Length; n++)
            {
                Point16 origin = PlaceAndFind(firstX + 84 + n * 10, floorY - 1, tileType, placedOrigins);
                MilkcraftAlchemyWorldSystem.ApplyActivationSync(origin, true);
                var (dx, dy) = breakCells[n];
                WorldGen.KillTile(origin.X + dx, origin.Y + dy, false, false, false);
                WorldGen.destroyObject = false;
                InvokeNativeMultitileCleanup(tileType, origin.X, origin.Y);
                if (MilkcraftAlchemyWorldSystem.IsActive(origin))
                    throw new InvalidOperationException($"native KillTile at cell ({dx},{dy}) left an active-table entry");
                placedOrigins.Remove(origin);
            }

            int emissivePixels = MilkcraftAlchemyWorldSystem.BuildEmissionMaskForDiagnostics();
            if (emissivePixels <= 0)
                throw new InvalidOperationException("active alchemy atlas generated no local emissive liquid/rune pixels");

            return "alchemy=PASS alcohol-left-use=PASS right-click=PASS invalid-left-click-no-consume=PASS " +
                "partial-3x3-rejected=PASS exact-AdjTiles-range=PASS near-dormant+far-active-does-not-unlock=PASS " +
                $"world-save-load=PASS break-nonorigin-and-corners-center-clears=PASS replacement-dormant=PASS active-tileLighted=PASS active-atlas-emissive-pixels={emissivePixels}";
        }
        finally
        {
            foreach (Point16 origin in placedOrigins)
                RemoveFootprint(origin, tileType);
            for (int x = firstX - 4; x <= lastX + 8; x++)
            for (int y = floorY - 8; y <= floorY + 2; y++)
                Main.tile[x, y].ClearEverything();
            for (int i = 0; i < Main.item.Length; i++)
            {
                if (Main.item[i].active && Main.item[i].type == ModContent.ItemType<MilkcraftAlchemyTable>())
                    Main.item[i].TurnToAir();
            }
            Main.item = oldItems;
            actor.active = oldActive;
            actor.dead = oldDead;
            actor.position = oldPosition;
            actor.selectedItem = oldSelected;
            actor.inventory[0] = oldSelectedItem;
            Main.gameMenu = oldMenu;
            Main.netMode = oldNetMode;
            Main.myPlayer = oldMyPlayer;
            Main.screenPosition = oldScreen;
            Main.mouseX = oldMouseX;
            Main.mouseY = oldMouseY;
            WorldGen.gen = oldGen;
            WorldGen.generatingWorld = oldGenerating;
            WorldGen.noTileActions = oldNoTileActions;
            WorldGen.destroyObject = oldDestroy;
            system.LoadWorldData(oldActiveState);
            int dropsNow = Main.item.Count(item => item.active && item.type == ModContent.ItemType<MilkcraftAlchemyTable>());
            if (dropsNow != spawnedDropsBefore)
                throw new InvalidOperationException($"alchemy fixture leaked item drops: before={spawnedDropsBefore} after={dropsNow}");
        }
    }

    private static Point16 PlaceAndFind(int placeX, int placeY, int tileType, HashSet<Point16> placedOrigins)
    {
        if (!WorldGen.PlaceObject(placeX, placeY, tileType, true, 0))
            throw new InvalidOperationException($"WorldGen.PlaceObject failed for alchemy table at {placeX},{placeY}");
        for (int x = placeX - 4; x <= placeX + 4; x++)
        for (int y = placeY - 4; y <= placeY + 4; y++)
        {
            if (!Main.tile[x, y].HasTile || Main.tile[x, y].TileType != tileType)
                continue;
            Point16 origin = MilkcraftAlchemyWorldSystem.OriginAt(x, y);
            if (MilkcraftAlchemyWorldSystem.IsCompleteTable(origin.X, origin.Y))
            {
                placedOrigins.Add(origin);
                return origin;
            }
        }
        throw new InvalidOperationException("native placement did not produce a complete alchemy table footprint");
    }

    private static void AimAt(Point16 tile)
    {
        Main.mouseX = tile.X * 16 + 8;
        Main.mouseY = tile.Y * 16 + 8;
    }

    private static void InvokeNativeMultitileCleanup(int tileType, int originX, int originY)
    {
        for (int x = originX - 1; x <= originX + 3; x++)
        for (int y = originY - 1; y <= originY + 3; y++)
            if (WorldGen.InWorld(x, y, 1) && Main.tile[x, y].HasTile && Main.tile[x, y].TileType == tileType)
                TileLoader.CheckModTile(x, y, tileType);
    }

    private static void RemoveFootprint(Point16 origin, int tileType)
    {
        for (int x = origin.X; x < origin.X + MilkcraftAlchemyTableTile.WidthInTiles; x++)
        for (int y = origin.Y; y < origin.Y + MilkcraftAlchemyTableTile.HeightInTiles; y++)
            if (WorldGen.InWorld(x, y, 1) && Main.tile[x, y].HasTile && Main.tile[x, y].TileType == tileType)
                Main.tile[x, y].ClearTile();
        MilkcraftAlchemyWorldSystem.Deactivate(origin);
    }
}
