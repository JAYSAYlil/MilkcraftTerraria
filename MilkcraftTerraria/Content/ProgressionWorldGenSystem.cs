using System.Collections.Generic;
using Terraria;
using Terraria.IO;
using Terraria.GameContent.Generation;
using Terraria.ModLoader;
using Terraria.WorldBuilding;
using MilkcraftTerraria.Content.Items;
using MilkcraftTerraria.Content.Tiles;

namespace MilkcraftTerraria.Content;

/// <summary>Generates the two progression ores with the vanilla evil-ore attempt density.</summary>
public sealed class ProgressionWorldGenSystem : ModSystem
{
    private const double OreAttemptPerWorldTile = 0.0000425;

    public override void ModifyWorldGenTasks(List<GenPass> tasks, ref double totalWeight)
    {
        int shinies = tasks.FindIndex(pass => pass.Name == "Shinies");
        if (shinies < 0)
            return;

        tasks.Insert(shinies + 1, new PassLegacy("Milkcraft Progression Ores", GenerateProgressionOres));
    }

    private static void GenerateProgressionOres(GenerationProgress progress, GameConfiguration configuration)
    {
        progress.Message = "Placing Milkcraft progression ores";

        int attempts = (int)(Main.maxTilesX * (double)Main.maxTilesY * OreAttemptPerWorldTile);
        int minY = (int)Main.worldSurface;
        int maxY = (int)Main.rockLayer;
        if (Main.maxTilesX <= 200 || maxY <= minY)
            return;

        GenerateOre(ModContent.TileType<BlackIronOreTile>(), attempts, minY, maxY);
        GenerateOre(ModContent.TileType<PinkDiamondOreTile>(), attempts, minY, maxY);
    }

    private static void GenerateOre(int tileType, int attempts, int minY, int maxY)
    {
        for (int n = 0; n < attempts; n++)
        {
            // These bounds, depth range, strength, and step ranges match the
            // 1.4.4.9 Demonite/Crimtane calls in the vanilla Shinies pass.
            int x = WorldGen.genRand.Next(100, Main.maxTilesX - 100);
            int y = WorldGen.genRand.Next(minY, maxY);
            int strength = WorldGen.genRand.Next(3, 6);
            int steps = WorldGen.genRand.Next(4, 8);
            WorldGen.TileRunner(x, y, strength, steps, tileType);
        }
    }
}
