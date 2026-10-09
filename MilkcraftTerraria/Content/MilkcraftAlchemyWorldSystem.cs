using System.Collections.Generic;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.IO;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;
using MilkcraftTerraria.Content.Items;
using MilkcraftTerraria.Content.Tiles;

namespace MilkcraftTerraria.Content;

/// <summary>Persistent, server-authoritative activation state for individual alchemy tables.</summary>
public sealed class MilkcraftAlchemyWorldSystem : ModSystem
{
    private const string SaveKey = "MilkcraftActiveAlchemyTables";
    private readonly HashSet<Point16> _activeTables = new();
    private static Asset<Texture2D> _activeOverlay;
    private static Texture2D _activeEmission;
    private static int _activeEmissionPixelCount;

    public override void ClearWorld() => _activeTables.Clear();

    public override void SaveWorldData(TagCompound tag)
    {
        var saved = new List<TagCompound>(_activeTables.Count);
        foreach (Point16 p in _activeTables)
            saved.Add(new TagCompound { ["x"] = (int)p.X, ["y"] = (int)p.Y });
        tag[SaveKey] = saved;
    }

    public override void LoadWorldData(TagCompound tag)
    {
        _activeTables.Clear();
        if (!tag.ContainsKey(SaveKey))
            return;
        foreach (TagCompound p in tag.GetList<TagCompound>(SaveKey))
            _activeTables.Add(new Point16(p.GetInt("x"), p.GetInt("y")));
    }

    public override void NetSend(BinaryWriter writer)
    {
        writer.Write((ushort)_activeTables.Count);
        foreach (Point16 p in _activeTables)
        {
            writer.Write(p.X);
            writer.Write(p.Y);
        }
    }

    public override void NetReceive(BinaryReader reader)
    {
        _activeTables.Clear();
        int count = reader.ReadUInt16();
        for (int i = 0; i < count; i++)
            _activeTables.Add(new Point16(reader.ReadInt16(), reader.ReadInt16()));
    }

    public override void Unload()
    {
        _activeOverlay = null;
        _activeEmission?.Dispose();
        _activeEmission = null;
        _activeEmissionPixelCount = 0;
    }

    public static bool IsActive(Point16 origin)
        => ModContent.GetInstance<MilkcraftAlchemyWorldSystem>()._activeTables.Contains(origin);

    public static Point16 OriginAt(int i, int j)
    {
        Tile tile = Main.tile[i, j];
        int localX = (tile.TileFrameX / 18) % MilkcraftAlchemyTableTile.WidthInTiles;
        int localY = (tile.TileFrameY / 18) % MilkcraftAlchemyTableTile.HeightInTiles;
        return new Point16(i - localX, j - localY);
    }

    public static bool IsActiveAt(int i, int j) => IsActive(OriginAt(i, j));

    public static bool PlayerNearActiveTable(Player player)
    {
        if (player is null || !player.active || player.dead || player.whoAmI < 0)
            return false;

        MilkcraftAlchemyWorldSystem system = ModContent.GetInstance<MilkcraftAlchemyWorldSystem>();
        GetNativeAdjTileBounds(player, out int minX, out int maxX, out int minY, out int maxY);
        // Player.AdjTiles scans this exact rectangle. Check for an active table
        // among those tiles so a farther active copy cannot unlock crafting.
        for (int x = minX; x <= maxX; x++)
        {
            for (int y = minY; y <= maxY; y++)
            {
                if (!WorldGen.InWorld(x, y, 1))
                    continue;
                Tile tile = Main.tile[x, y];
                if (!tile.HasTile || tile.TileType != ModContent.TileType<MilkcraftAlchemyTableTile>())
                    continue;

                Point16 origin = OriginAt(x, y);
                if (system._activeTables.Contains(origin) && IsCompleteTable(origin.X, origin.Y))
                    return true;
            }
        }
        return false;
    }

    private static void GetNativeAdjTileBounds(Player player, out int minX, out int maxX, out int minY, out int maxY)
    {
        // Mirrors Player.AdjTiles: player hitbox in tiles, expanded by the
        // crafting tile range (without tile-placement bonuses or table padding).
        minX = (int)(player.position.X / 16f) - Player.tileRangeX;
        maxX = (int)((player.position.X + player.width) / 16f) + Player.tileRangeX;
        minY = (int)(player.position.Y / 16f) - Player.tileRangeY;
        maxY = (int)((player.position.Y + player.height) / 16f) + Player.tileRangeY;
    }

    internal static bool IsInNativeAdjTileRange(Player player, Point16 origin)
    {
        GetNativeAdjTileBounds(player, out int minX, out int maxX, out int minY, out int maxY);
        int tableMaxX = origin.X + MilkcraftAlchemyTableTile.WidthInTiles - 1;
        int tableMaxY = origin.Y + MilkcraftAlchemyTableTile.HeightInTiles - 1;
        return origin.X <= maxX && tableMaxX >= minX && origin.Y <= maxY && tableMaxY >= minY;
    }

    public static bool TryActivateFromLocalClick(int i, int j)
    {
        Point16 origin = OriginAt(i, j);
        Player player = Main.LocalPlayer;
        if (!player.active || player.dead)
            return true;
        if (IsActive(origin))
        {
            Main.NewText(Terraria.Localization.Language.GetTextValue("Mods.MilkcraftTerraria.Messages.AlchemyTableAlreadyActive"), 100, 220, 230);
            return true;
        }
        bool hasCursorAlcohol = Main.netMode == NetmodeID.SinglePlayer && Main.mouseItem.type == ModContent.ItemType<Alcohol>() && Main.mouseItem.stack > 0;
        if (!hasCursorAlcohol && player.CountItem(ModContent.ItemType<Alcohol>()) <= 0)
        {
            Main.NewText(Terraria.Localization.Language.GetTextValue("Mods.MilkcraftTerraria.Messages.AlchemyTableNeedsAlcohol"), 220, 170, 100);
            return true;
        }
        if (!CanActivate(player, origin))
            return false;

        if (Main.netMode == NetmodeID.SinglePlayer)
            return ActivateAuthoritative(player, origin, hasCursorAlcohol ? -1 : -2);

        if (Main.netMode == NetmodeID.MultiplayerClient)
        {
            ModPacket packet = ModContent.GetInstance<MilkcraftTerrariaMod>().GetPacket();
            packet.Write(MilkcraftTerrariaMod.AlchemyActivationRequestMessage);
            packet.Write(origin.X);
            packet.Write(origin.Y);
            packet.Write(false); // Right-click/table path consumes inventory on the server.
            packet.Write(byte.MaxValue);
            packet.Send();
            return true;
        }
        return false;
    }

    public static bool TryActivateFromAlcoholUse(Player player, int i, int j)
    {
        if (!CanUseAlcoholOnTable(player, i, j))
            return false;
        Point16 origin = OriginAt(i, j);
        if (Main.netMode == NetmodeID.SinglePlayer)
            return ActivateAuthoritative(player, origin, player.selectedItem);
        if (Main.netMode == NetmodeID.MultiplayerClient)
        {
            ModPacket packet = ModContent.GetInstance<MilkcraftTerrariaMod>().GetPacket();
            packet.Write(MilkcraftTerrariaMod.AlchemyActivationRequestMessage);
            packet.Write(origin.X);
            packet.Write(origin.Y);
            packet.Write(true);
            packet.Write((byte)player.selectedItem);
            packet.Send();
            return true;
        }
        return false;
    }

    public static bool CanUseAlcoholOnTable(Player player, int i, int j)
    {
        if (player is null || !player.active || player.dead || !WorldGen.InWorld(i, j, 1))
            return false;
        Tile tile = Main.tile[i, j];
        if (!tile.HasTile || tile.TileType != ModContent.TileType<MilkcraftAlchemyTableTile>())
            return false;
        Point16 origin = OriginAt(i, j);
        return !IsActive(origin) && IsCompleteTable(origin.X, origin.Y)
            && IsInNativeAdjTileRange(player, origin);
    }

    public static bool HandleActivationRequest(Player player, Point16 origin, bool heldAlcohol, int inventorySlot)
        => ActivateAuthoritative(player, origin, heldAlcohol ? inventorySlot : -2);

    private static bool ActivateAuthoritative(Player player, Point16 origin, int alcoholSlot)
    {
        if (Main.netMode == NetmodeID.MultiplayerClient || player is null
            || !player.active || player.dead || !CanActivate(player, origin) || IsActive(origin))
            return false;

        int alcoholType = ModContent.ItemType<Alcohol>();
        bool consumed = false;
        if (alcoholSlot == -1 && Main.netMode == NetmodeID.SinglePlayer
            && Main.mouseItem.type == alcoholType && Main.mouseItem.stack > 0)
        {
            Main.mouseItem.stack--;
            if (Main.mouseItem.stack <= 0)
                Main.mouseItem.TurnToAir();
            consumed = true;
        }
        else if (alcoholSlot >= 0 && alcoholSlot < player.inventory.Length)
        {
            Item held = player.inventory[alcoholSlot];
            if (player.selectedItem == alcoholSlot && held.type == alcoholType && held.stack > 0)
            {
                held.stack--;
                if (held.stack <= 0)
                    held.TurnToAir();
                consumed = true;
                if (Main.netMode == NetmodeID.Server)
                    NetMessage.SendData(MessageID.SyncEquipment, -1, -1, null, player.whoAmI, alcoholSlot);
            }
        }
        else if (alcoholSlot == -2 && player.CountItem(alcoholType) >= 1)
        {
            for (int slot = 0; slot < player.inventory.Length; slot++)
            {
                Item stack = player.inventory[slot];
                if (stack.type != alcoholType || stack.stack <= 0)
                    continue;
                stack.stack--;
                if (stack.stack <= 0)
                    stack.TurnToAir();
                consumed = true;
                if (Main.netMode == NetmodeID.Server)
                    NetMessage.SendData(MessageID.SyncEquipment, -1, -1, null, player.whoAmI, slot);
                break;
            }
        }
        if (!consumed)
            return false;

        MilkcraftAlchemyWorldSystem system = ModContent.GetInstance<MilkcraftAlchemyWorldSystem>();
        system._activeTables.Add(origin);
        SendActivationState(origin, true);
        if (Main.netMode == NetmodeID.SinglePlayer)
            Main.NewText(Terraria.Localization.Language.GetTextValue("Mods.MilkcraftTerraria.Messages.AlchemyTableActivated"), 100, 220, 230);
        return true;
    }

    private static bool CanActivate(Player player, Point16 origin)
    {
        if (player is null || !player.active || player.dead
            || origin.X < 10 || origin.Y < 10
            || origin.X + MilkcraftAlchemyTableTile.WidthInTiles >= Main.maxTilesX - 10
            || origin.Y + MilkcraftAlchemyTableTile.HeightInTiles >= Main.maxTilesY - 10)
            return false;
        return IsCompleteTable(origin.X, origin.Y) && IsInNativeAdjTileRange(player, origin);
    }

    internal static bool IsCompleteTable(int x, int y)
    {
        int type = ModContent.TileType<MilkcraftAlchemyTableTile>();
        for (int dx = 0; dx < MilkcraftAlchemyTableTile.WidthInTiles; dx++)
            for (int dy = 0; dy < MilkcraftAlchemyTableTile.HeightInTiles; dy++)
                if (!Main.tile[x + dx, y + dy].HasTile || Main.tile[x + dx, y + dy].TileType != type)
                    return false;
        return true;
    }

    public static void Deactivate(Point16 origin)
    {
        if (Main.netMode == NetmodeID.MultiplayerClient)
            return;
        MilkcraftAlchemyWorldSystem system = ModContent.GetInstance<MilkcraftAlchemyWorldSystem>();
        if (system._activeTables.Remove(origin))
            SendActivationState(origin, false);
    }

    private static void SendActivationState(Point16 origin, bool active)
    {
        if (Main.netMode != NetmodeID.Server)
            return;
        ModPacket packet = ModContent.GetInstance<MilkcraftTerrariaMod>().GetPacket();
        packet.Write(MilkcraftTerrariaMod.AlchemyActivationSyncMessage);
        packet.Write(origin.X);
        packet.Write(origin.Y);
        packet.Write(active);
        packet.Send();
    }

    public static void ApplyActivationSync(Point16 origin, bool active)
    {
        MilkcraftAlchemyWorldSystem system = ModContent.GetInstance<MilkcraftAlchemyWorldSystem>();
        if (active)
            system._activeTables.Add(origin);
        else
            system._activeTables.Remove(origin);
    }

    public static bool DrawActiveCell(int i, int j, SpriteBatch spriteBatch)
        => DrawActiveCellCore(i, j, spriteBatch, Lighting.GetColor(i, j));

    internal static bool DrawActiveCellForDiagnostics(int i, int j, SpriteBatch spriteBatch, Color lighting)
        => DrawActiveCellCore(i, j, spriteBatch, lighting);

    private static bool DrawActiveCellCore(int i, int j, SpriteBatch spriteBatch, Color lighting)
    {
        if (Main.dedServ || !IsActiveAt(i, j))
            return false;
        _activeOverlay ??= ModContent.Request<Texture2D>("MilkcraftTerraria/Content/Tiles/MilkcraftAlchemyTableTile_Active", AssetRequestMode.ImmediateLoad);

        Tile tile = Main.tile[i, j];
        int sourceX = tile.TileFrameX % (MilkcraftAlchemyTableTile.WidthInTiles * 18);
        int sourceY = tile.TileFrameY % (MilkcraftAlchemyTableTile.HeightInTiles * 18);
        Rectangle source = new(sourceX, sourceY, 16, 16);
        Vector2 position = new(i * 16f - Main.screenPosition.X, j * 16f - Main.screenPosition.Y);
        if (!Main.drawToScreen)
            position += new Vector2(Main.offScreenRange);
        Texture2D texture = _activeOverlay.Value;
        if (texture.Width < MilkcraftAlchemyTableTile.WidthInTiles * 18
            || texture.Height < MilkcraftAlchemyTableTile.HeightInTiles * 18)
            throw new InvalidDataException($"Active alchemy atlas must cover a 3x3 tile object (at least 54x54); got {texture.Width}x{texture.Height}.");
        spriteBatch.Draw(texture, position, source, lighting);
        EnsureActiveEmission(texture);
        if (_activeEmission is not null)
            spriteBatch.Draw(_activeEmission, position, source, Color.White);
        return true;
    }

    internal static int DiagnosticActiveEmissionPixelCount => _activeEmissionPixelCount;

    internal static int BuildEmissionMaskForDiagnostics()
    {
        _activeOverlay ??= ModContent.Request<Texture2D>("MilkcraftTerraria/Content/Tiles/MilkcraftAlchemyTableTile_Active", AssetRequestMode.ImmediateLoad);
        EnsureActiveEmission(_activeOverlay.Value);
        return _activeEmissionPixelCount;
    }

    private static void EnsureActiveEmission(Texture2D source)
    {
        if (_activeEmission is not null)
            return;
        var pixels = new Color[source.Width * source.Height];
        source.GetData(pixels);
        var emission = new Color[pixels.Length];
        int count = 0;
        for (int index = 0; index < pixels.Length; index++)
        {
            Color pixel = pixels[index];
            if (pixel.A < 96)
                continue;

            bool cyanLiquidOrRune = pixel.B >= 95 && pixel.G >= 80
                && pixel.B >= pixel.R * 1.18f && pixel.G >= pixel.R * 1.12f
                && pixel.B >= pixel.G * 0.82f;
            bool goldRune = pixel.R >= 155 && pixel.G >= 105
                && pixel.R >= pixel.B * 1.18f && pixel.G >= pixel.B * 1.15f
                && pixel.R - pixel.B >= 35;
            if (!cyanLiquidOrRune && !goldRune)
                continue;

            float opacity = System.Math.Min(pixel.A / 255f, 0.48f);
            emission[index] = (cyanLiquidOrRune ? new Color(75, 190, 235) : new Color(245, 190, 95)) * opacity;
            count++;
        }
        _activeEmission = new Texture2D(source.GraphicsDevice, source.Width, source.Height, false, SurfaceFormat.Color);
        _activeEmission.SetData(emission);
        _activeEmissionPixelCount = count;
    }
}
