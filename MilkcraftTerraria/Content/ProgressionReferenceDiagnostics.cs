using System.IO;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.ID;

namespace MilkcraftTerraria.Content;

internal static class ProgressionReferenceDiagnostics
{
    public static string ExportNativeEvilOreTiles(string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        string demonite = SaveTile("Demonite", TileID.Demonite, outputDirectory);
        string crimtane = SaveTile("Crimtane", TileID.Crimtane, outputDirectory);
        return $"native-ore-reference=PASS demonite={demonite} crimtane={crimtane} source=TextureAssets.Tile";
    }

    private static string SaveTile(string name, int tileType, string outputDirectory)
    {
        Main.instance.LoadTiles(tileType);
        Texture2D texture = TextureAssets.Tile[tileType].Value;
        if (texture.Width <= 1 || texture.Height <= 1)
            throw new InvalidDataException($"Vanilla {name} tile texture is not loaded ({texture.Width}x{texture.Height}).");
        string path = Path.Combine(outputDirectory, $"native-{name.ToLowerInvariant()}-tile.png");
        using FileStream stream = File.Create(path);
        texture.SaveAsPng(stream, texture.Width, texture.Height);
        return $"{name}:tileID={tileType};atlas={texture.Width}x{texture.Height};frameImportant={Main.tileFrameImportant[tileType]};png={path}";
    }
}
