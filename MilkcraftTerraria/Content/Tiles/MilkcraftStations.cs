using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.DataStructures;
using Terraria.GameContent;
using Terraria.Enums;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;
using Terraria.ObjectData;
using ReLogic.Content;
using System.Linq;
using MilkcraftTerraria.Content.Items;

namespace MilkcraftTerraria.Content.Tiles;

public abstract class MilkcraftStationTile : ModTile
{
    public override string Texture => $"{Mod.Name}/Content/Tiles/{GetType().Name}";
    protected abstract int WidthTiles { get; }
    protected abstract int HeightTiles { get; }
    protected virtual int[] VanillaAdjacency => System.Array.Empty<int>();

    public override void SetStaticDefaults()
    {
        if (GetType().Name == "MilkcraftWorkbenchTile")
        {
            Main.tileTable[Type] = true;
            Main.tileSolidTop[Type] = true;
            AddToArray(ref TileID.Sets.RoomNeeds.CountsAsTable);
        }
        Main.tileFrameImportant[Type] = true;
        Main.tileSolid[Type] = false;
        Main.tileNoAttach[Type] = true;
        Main.tileLavaDeath[Type] = true;
        TileObjectData.newTile.CopyFrom(WidthTiles == 3 && HeightTiles == 2 ? TileObjectData.Style3x2 : TileObjectData.Style3x3);
        TileObjectData.newTile.Width = WidthTiles;
        TileObjectData.newTile.Height = HeightTiles;
        TileObjectData.newTile.Origin = new Point16(1, HeightTiles - 1);
        TileObjectData.newTile.CoordinateHeights = Enumerable.Repeat(16, HeightTiles).ToArray();
        TileObjectData.addTile(Type);
        AddMapEntry(new Color(165, 132, 92), Language.GetText("Mods.MilkcraftTerraria.MapObject." + GetType().Name));
        AdjTiles = VanillaAdjacency;
        RegisterItemDrop(GetType().Name switch
        {
            "MilkcraftWorkbenchTile" => ModContent.ItemType<MilkcraftWorkbench>(),
            "MilkcraftFurnaceTile" => ModContent.ItemType<MilkcraftFurnace>(),
            "MilkcraftLoomTile" => ModContent.ItemType<MilkcraftLoom>(),
            "MilkcraftForgeTile" => ModContent.ItemType<MilkcraftForge>(),
            "MilkcraftDistilleryTile" => ModContent.ItemType<MilkcraftDistillery>(),
            "MilkcraftAlchemyTableTile" => ModContent.ItemType<MilkcraftAlchemyTable>(),
            "MilkcraftEnchantingTableTile" => ModContent.ItemType<MilkcraftEnchantingTable>(),
            _ => 0
        });
        DustType = DustID.WoodFurniture;
        HitSound = SoundID.Dig;
        MineResist = 1f;
        MinPick = 0;
    }

}

public sealed class MilkcraftWorkbenchTile : MilkcraftStationTile { protected override int WidthTiles => 3; protected override int HeightTiles => 2; protected override int[] VanillaAdjacency => new[] { (int)TileID.WorkBenches }; }
public sealed class MilkcraftFurnaceTile : MilkcraftStationTile { protected override int WidthTiles => 3; protected override int HeightTiles => 3; }
public sealed class MilkcraftLoomTile : MilkcraftStationTile { protected override int WidthTiles => 3; protected override int HeightTiles => 3; }
public sealed class MilkcraftForgeTile : MilkcraftStationTile { protected override int WidthTiles => 3; protected override int HeightTiles => 2; }
public sealed class MilkcraftDistilleryTile : MilkcraftStationTile { protected override int WidthTiles => 3; protected override int HeightTiles => 3; }
public sealed class MilkcraftAlchemyTableTile : MilkcraftStationTile
{
    public const int WidthInTiles = 3;
    public const int HeightInTiles = 3;
    protected override int WidthTiles => WidthInTiles;
    protected override int HeightTiles => HeightInTiles;

    public override void SetStaticDefaults()
    {
        base.SetStaticDefaults();
        Main.tileLighted[Type] = true;
    }

    public override bool RightClick(int i, int j)
    {
        MilkcraftAlchemyWorldSystem.TryActivateFromLocalClick(i, j);
        return true;
    }

    public override void KillMultiTile(int i, int j, int frameX, int frameY)
    {
        int localX = (frameX / 18) % WidthInTiles;
        int localY = (frameY / 18) % HeightInTiles;
        MilkcraftAlchemyWorldSystem.Deactivate(new Point16(i - localX, j - localY));
    }

    public override void ModifyLight(int i, int j, ref float r, ref float g, ref float b)
    {
        if (MilkcraftAlchemyWorldSystem.IsActiveAt(i, j))
        {
            r += 0.035f;
            g += 0.145f;
            b += 0.19f;
        }
    }

    // Active tables use a complete replacement atlas rather than a translucent
    // overlay, so the original table art is not double-rendered underneath it.
    public override bool PreDraw(int i, int j, SpriteBatch spriteBatch)
        => !MilkcraftAlchemyWorldSystem.DrawActiveCell(i, j, spriteBatch);
}
public sealed class MilkcraftEnchantingTableTile : MilkcraftStationTile
{
    private const int CubeDrawYOffset = 12;
    private static Asset<Texture2D> _pedestal;
    protected override int WidthTiles => 3;
    protected override int HeightTiles => 3;

    public override bool PreDraw(int i, int j, SpriteBatch spriteBatch)
    {
        Tile tile = Main.tile[i, j];
        int cellX = (tile.TileFrameX / 18) % 3;
        int cellY = (tile.TileFrameY / 18) % 3;
        if (cellX == 0 && cellY == 0)
        {
            Texture2D cube = TextureAssets.Tile[Type].Value;
            int baseX = tile.TileFrameX;
            int baseY = tile.TileFrameY;
            int topLeftX = i;
            int topLeftY = j;
            Vector2 offscreen = Main.drawToScreen ? Vector2.Zero : new Vector2(Main.offScreenRange);
            for (int row = 0; row < 3; row++)
            for (int col = 0; col < 3; col++)
            {
                Rectangle source = new(baseX + col * 18, baseY + row * 18, 16, 16);
                Vector2 position = new((topLeftX + col) * 16f, (topLeftY + row) * 16f - CubeDrawYOffset);
                spriteBatch.Draw(cube, position - Main.screenPosition + offscreen, source,
                    Lighting.GetColor(i + col, j + row));
            }
        }
        // The 3x3 cube is rendered as a single lifted atlas; drawing individual
        // native cells as well would duplicate it and cover the support gap.
        return false;
    }

    public override void PostDraw(int i, int j, SpriteBatch spriteBatch)
    {
        Tile tile = Main.tile[i, j];
        int cellX = (tile.TileFrameX / 18) % 3;
        int cellY = (tile.TileFrameY / 18) % 3;
        if (cellX != 0 || cellY != 0 || Main.dedServ)
            return;
        _pedestal ??= ModContent.Request<Texture2D>($"{Mod.Name}/Content/Tiles/MilkcraftEnchantingPedestal", AssetRequestMode.ImmediateLoad);
        int topLeftX = i;
        int topLeftY = j;
        Vector2 position = new(topLeftX * 16f + 24f, topLeftY * 16f + 39f);
        Vector2 offscreen = Main.drawToScreen ? Vector2.Zero : new Vector2(Main.offScreenRange);
        spriteBatch.Draw(_pedestal.Value, position - Main.screenPosition + offscreen, null,
            Lighting.GetColor(i, j + 2), 0f, new Vector2(_pedestal.Width() * 0.5f, 0f), 1f,
            SpriteEffects.None, 0f);
    }

    public override bool RightClick(int i, int j)
    {
        MilkcraftEnchantingSystem.Open();
        return true;
    }
}
