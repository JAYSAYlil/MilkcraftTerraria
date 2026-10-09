using System;
using System.IO;
using System.Reflection;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.ID;

namespace MilkcraftTerraria.Content;

/// <summary>
/// Isolated vanilla reference exporter for the Mage Spectre Hood. Enabled only
/// by MILKCRAFT_TERRARIA_SPECTRE_HOOD_REFERENCE in the test profile.
/// </summary>
internal static class SpectreHoodReference
{
    public static void Export(string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        Item item = new();
        item.SetDefaults(ItemID.SpectreHood);
        MethodInfo loadHead = typeof(Main).GetMethod("LoadArmorHead",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (loadHead is null)
            throw new InvalidOperationException("Could not find Main.LoadArmorHead for the vanilla SpectreHood reference export.");
        loadHead.Invoke(Main.instance, new object[] { item.headSlot });

        FieldInfo field = typeof(TextureAssets).GetField("ArmorHead",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
        Array textures = field?.GetValue(null) as Array;
        object asset = textures is not null && item.headSlot >= 0 && item.headSlot < textures.Length
            ? textures.GetValue(item.headSlot)
            : null;
        Type assetType = asset?.GetType();
        Texture2D texture = assetType?.GetProperty("Value")?.GetValue(asset) as Texture2D;
        string assetName = assetType?.GetProperty("Name")?.GetValue(asset)?.ToString() ?? "<unknown>";
        if (texture is null)
            throw new InvalidOperationException(
                $"Could not resolve vanilla ArmorHead for SpectreHood itemType={ItemID.SpectreHood} headSlot={item.headSlot}; " +
                $"fieldPresent={field is not null}, arrayLength={textures?.Length ?? 0}");

        string atlasPath = Path.Combine(outputDirectory, "native-spectre-hood-armorhead.png");
        using (FileStream stream = File.Create(atlasPath))
            texture.SaveAsPng(stream, texture.Width, texture.Height);

        int frameHeight = Math.Min(56, texture.Height);
        if (texture.Width < 40 || frameHeight <= 0)
            throw new InvalidOperationException($"Unexpected vanilla SpectreHood ArmorHead dimensions {texture.Width}x{texture.Height}.");
        using (Texture2D frame = new(Main.graphics.GraphicsDevice, 40, frameHeight, false, SurfaceFormat.Color))
        {
            var pixels = new Color[40 * frameHeight];
            texture.GetData(0, new Rectangle(0, 0, 40, frameHeight), pixels, 0, pixels.Length);
            frame.SetData(pixels);
            using FileStream stream = File.Create(Path.Combine(outputDirectory, "native-spectre-hood-frame0.png"));
            frame.SaveAsPng(stream, 40, frameHeight);
        }

        const int width = 360;
        const int height = 150;
        const int scale = 3;
        GraphicsDevice device = Main.graphics.GraphicsDevice;
        using var fullTarget = new RenderTarget2D(device, Math.Max(width, Main.screenWidth),
            Math.Max(height, Main.screenHeight), false, SurfaceFormat.Color, DepthFormat.None, 0,
            RenderTargetUsage.PreserveContents);
        device.SetRenderTarget(fullTarget);
        device.Clear(new Color(28, 31, 39));
        Main.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp,
            DepthStencilState.None, RasterizerState.CullCounterClockwise);

        int localPlayerIndex = Math.Clamp(Main.myPlayer, 0, Main.player.Length - 1);
        Player previousPlayer = Main.player[localPlayerIndex];
        bool previousMenu = Main.gameMenu;
        bool previousForceLighting = SmokeSystem.ForcePreviewLighting;
        float previousUiScale = Main.UIScale;
        Vector2 previousZoom = Main.GameViewMatrix.Zoom;
        int previousMouseX = Main.mouseX;
        int previousMouseY = Main.mouseY;
        Projectile[] previousProjectiles = Main.projectile;
        try
        {
            Main.gameMenu = false;
            SmokeSystem.ForcePreviewLighting = true;
            Main.UIScale = 1f;
            Main.GameViewMatrix.Zoom = Vector2.One;
            Main.projectile = (Projectile[])previousProjectiles.Clone();
            DrawVanillaMale(item.headSlot, localPlayerIndex, 1, 105, "right");
            DrawVanillaMale(item.headSlot, localPlayerIndex, -1, 255, "left");
        }
        finally
        {
            Main.player[localPlayerIndex] = previousPlayer;
            Main.projectile = previousProjectiles;
            SmokeSystem.ForcePreviewLighting = previousForceLighting;
            Main.gameMenu = previousMenu;
            Main.UIScale = previousUiScale;
            Main.GameViewMatrix.Zoom = previousZoom;
            Main.mouseX = previousMouseX;
            Main.mouseY = previousMouseY;
            Main.spriteBatch.End();
        }

        SaveCrop(fullTarget, outputDirectory, width, height, scale);
        File.WriteAllText(Path.Combine(outputDirectory, "native-spectre-hood-reference.log"),
            $"item=SpectreHood itemType={ItemID.SpectreHood} headSlot={item.headSlot} asset={assetName} " +
            $"atlas={texture.Width}x{texture.Height} frame0=40x{frameHeight}\n" +
            "native-male-player-renderer=PASS idle-directions=right,left; naked vanilla body keeps focus on hood silhouette\n");
    }

    private static void DrawVanillaMale(int headSlot, int playerIndex, int direction, int x, string label)
    {
        Player player = Main.LocalPlayer.SerializedClone();
        player.active = true;
        player.whoAmI = playerIndex;
        player.position = Vector2.Zero;
        player.velocity = Vector2.Zero;
        player.fullRotation = 0f;
        player.direction = direction;
        player.Male = true;
        for (int i = 0; i < player.armor.Length; i++)
            player.armor[i] = new Item();
        player.armor[0].SetDefaults(ItemID.SpectreHood);
        for (int i = 0; i < player.inventory.Length; i++)
            player.inventory[i] = new Item();
        player.selectedItem = 0;
        player.lastVisualizedSelectedItem = new Item();
        player.itemAnimation = player.itemTime = 0;
        player.itemRotation = 0f;
        player.head = headSlot;
        player.body = 0;
        player.legs = 0;
        player.hair = 0;
        player.hairColor = new Color(88, 58, 35);
        player.skinColor = new Color(238, 190, 150);
        player.eyeColor = new Color(55, 95, 145);
        player.bodyFrame = new Rectangle(0, 0, 40, 56);
        player.legFrame = new Rectangle(0, 0, 40, 56);
        player.isDisplayDollOrInanimate = false;
        Vector2 position = new Vector2(x, 68) + Main.screenPosition;
        player.position = position;
        Main.player[playerIndex] = player;
        Main.PlayerRenderer.DrawPlayer(Main.Camera, player, position, 0f,
            new Vector2(player.width / 2f, player.height / 2f), 0f, 1f);
    }

    private static void SaveCrop(RenderTarget2D source, string outputDirectory, int width, int height, int scale)
    {
        GraphicsDevice device = source.GraphicsDevice;
        device.SetRenderTarget(null);
        var crop = new Color[width * height];
        source.GetData(0, new Rectangle(0, 0, width, height), crop, 0, crop.Length);
        using (Texture2D native = new(device, width, height, false, SurfaceFormat.Color))
        {
            native.SetData(crop);
            using FileStream stream = File.Create(Path.Combine(outputDirectory, "native-spectre-hood-idle-directions-native.png"));
            native.SaveAsPng(stream, width, height);
        }
        int enlargedWidth = width * scale;
        var enlarged = new Color[enlargedWidth * height * scale];
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            Color color = crop[y * width + x];
            for (int dy = 0; dy < scale; dy++)
            for (int dx = 0; dx < scale; dx++)
                enlarged[(y * scale + dy) * enlargedWidth + x * scale + dx] = color;
        }
        using Texture2D zoomed = new(device, enlargedWidth, height * scale, false, SurfaceFormat.Color);
        zoomed.SetData(enlarged);
        using FileStream output = File.Create(Path.Combine(outputDirectory, "native-spectre-hood-idle-directions.png"));
        zoomed.SaveAsPng(output, enlargedWidth, height * scale);
    }
}
