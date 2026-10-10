using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;
using Terraria.ObjectData;
using MilkcraftTerraria.Content.Items;
using ReLogic.Content;

namespace MilkcraftTerraria.Content.Tiles;

public abstract class MilkcraftBossDisplayTile : ModTile
{
    protected abstract int WidthTiles { get; }
    protected abstract int HeightTiles { get; }
    protected abstract int DropItem { get; }
    protected virtual int DustKind => DustID.Stone;
    protected virtual bool WallMounted => true;
    public override string Texture => $"{Mod.Name}/Content/Tiles/{GetType().Name}";
    public override void SetStaticDefaults()
    {
        Main.tileFrameImportant[Type] = true;
        Main.tileNoAttach[Type] = true;
        Main.tileLavaDeath[Type] = true;
        TileObjectData.newTile.CopyFrom(TileObjectData.Style3x3);
        TileObjectData.newTile.Width = WidthTiles;
        TileObjectData.newTile.Height = HeightTiles;
        TileObjectData.newTile.Origin = new Point16(WidthTiles / 2, HeightTiles - 1);
        TileObjectData.newTile.CoordinateHeights = Enumerable.Repeat(16, HeightTiles).ToArray();
        TileObjectData.newTile.CoordinateWidth = 16;
        TileObjectData.newTile.CoordinatePadding = 2;
        if (WallMounted)
        {
            TileObjectData.newTile.AnchorBottom = AnchorData.Empty;
            TileObjectData.newTile.AnchorWall = true;
        }
        TileObjectData.addTile(Type);
        AddMapEntry(new Color(175, 155, 120), Language.GetText("Mods.MilkcraftTerraria.MapObject." + GetType().Name));
        DustType = DustKind;
        RegisterItemDrop(DropItem);
        TileID.Sets.DisableSmartCursor[Type] = true;
    }
}

/// <summary>Draws the independent full statue over its native 3x4 gold pedestal.</summary>
public abstract class MilkcraftBossRelicTile : MilkcraftBossDisplayTile
{
    protected abstract string RelicName { get; }
    protected override int WidthTiles => 3;
    protected override int HeightTiles => 4;
    protected override bool WallMounted => false;

    public override void PostDraw(int i, int j, SpriteBatch spriteBatch)
    {
        Tile tile = Main.tile[i, j];
        // Multitile framing starts at (0,0) for its top-left cell. Drawing only
        // there prevents one statue copy per pedestal tile.
        if (tile.TileFrameX != 0 || tile.TileFrameY != 0)
            return;

        Texture2D statue = ModContent.Request<Texture2D>($"{Mod.Name}/Content/Tiles/{RelicName}RelicSpecial", AssetRequestMode.ImmediateLoad).Value;
        // Tile is 3x4 (64px tall); the fourth row is the visible gold pedestal.
        // The separate statue stands on the pedestal's top edge at row three.
        Vector2 bottomCenter = new(i * 16f + 24f, j * 16f + 48f);
        Vector2 offscreen = Main.drawToScreen ? Vector2.Zero : new Vector2(Main.offScreenRange);
        spriteBatch.Draw(statue, bottomCenter - Main.screenPosition + offscreen, null, Lighting.GetColor(i, j), 0f,
            new Vector2(statue.Width * 0.5f, statue.Height), 1f, SpriteEffects.None, 0f);
    }
}

public sealed class WraithWarriorTrophyTile : MilkcraftBossDisplayTile { protected override int WidthTiles => 3; protected override int HeightTiles => 3; protected override int DropItem => ModContent.ItemType<WraithWarriorTrophy>(); }
public sealed class JungleLordTrophyTile : MilkcraftBossDisplayTile { protected override int WidthTiles => 3; protected override int HeightTiles => 3; protected override int DropItem => ModContent.ItemType<JungleLordTrophy>(); }
public sealed class BansheeTrophyTile : MilkcraftBossDisplayTile { protected override int WidthTiles => 3; protected override int HeightTiles => 3; protected override int DropItem => ModContent.ItemType<BansheeTrophy>(); }
public sealed class ObsidianGuardianTrophyTile : MilkcraftBossDisplayTile { protected override int WidthTiles => 3; protected override int HeightTiles => 3; protected override int DropItem => ModContent.ItemType<ObsidianGuardianTrophy>(); }
public sealed class SphinxTrophyTile : MilkcraftBossDisplayTile { protected override int WidthTiles => 3; protected override int HeightTiles => 3; protected override int DropItem => ModContent.ItemType<SphinxTrophy>(); }
public sealed class FrostWitchTrophyTile : MilkcraftBossDisplayTile { protected override int WidthTiles => 3; protected override int HeightTiles => 3; protected override int DropItem => ModContent.ItemType<FrostWitchTrophy>(); }
public sealed class CastleCoreTrophyTile : MilkcraftBossDisplayTile { protected override int WidthTiles => 3; protected override int HeightTiles => 3; protected override int DropItem => ModContent.ItemType<CastleCoreTrophy>(); }
public sealed class WraithWarriorRelicTile : MilkcraftBossRelicTile { protected override string RelicName => "WraithWarrior"; protected override int DropItem => ModContent.ItemType<WraithWarriorRelic>(); }
public sealed class JungleLordRelicTile : MilkcraftBossRelicTile { public override string Texture => $"{Mod.Name}/Content/Tiles/WraithWarriorRelicTile"; protected override string RelicName => "JungleLord"; protected override int DropItem => ModContent.ItemType<JungleLordRelic>(); }
public sealed class BansheeRelicTile : MilkcraftBossRelicTile { public override string Texture => $"{Mod.Name}/Content/Tiles/WraithWarriorRelicTile"; protected override string RelicName => "Banshee"; protected override int DropItem => ModContent.ItemType<BansheeRelic>(); }
public sealed class ObsidianGuardianRelicTile : MilkcraftBossRelicTile { public override string Texture => $"{Mod.Name}/Content/Tiles/WraithWarriorRelicTile"; protected override string RelicName => "ObsidianGuardian"; protected override int DropItem => ModContent.ItemType<ObsidianGuardianRelic>(); }
public sealed class SphinxRelicTile : MilkcraftBossRelicTile { protected override string RelicName => "Sphinx"; protected override int DropItem => ModContent.ItemType<SphinxRelic>(); }
public sealed class FrostWitchRelicTile : MilkcraftBossRelicTile { protected override string RelicName => "FrostWitch"; protected override int DropItem => ModContent.ItemType<FrostWitchRelic>(); }
public sealed class CastleCoreRelicTile : MilkcraftBossRelicTile { protected override string RelicName => "CastleCore"; protected override int DropItem => ModContent.ItemType<CastleCoreRelic>(); }
