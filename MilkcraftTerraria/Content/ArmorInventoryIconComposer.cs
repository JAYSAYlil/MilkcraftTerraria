using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;

namespace MilkcraftTerraria.Content;

/// <summary>
/// Draws body armor inventory icons from the accepted source pixels while
/// omitting the long robe hems. This only affects inventory/world item icons;
/// wearable textures and their UV layouts are left untouched.
/// </summary>
internal static class ArmorInventoryIconComposer
{
    private sealed record IconLayout(params Rectangle[] Pieces);
    private static readonly Dictionary<string, Rectangle> CroppedIcons = new(StringComparer.Ordinal)
    {
        ["ClothLeggings"] = new Rectangle(277, 377, 547, 795),
        ["EnergyLeggings"] = new Rectangle(254, 351, 578, 822),
        ["FallenLeggings"] = new Rectangle(204, 289, 670, 967),
        ["DarkLeggings"] = new Rectangle(245, 369, 597, 791)
    };

    private static readonly Dictionary<string, IconLayout> Layouts = new(StringComparer.Ordinal)
    {
        ["IronChestplate"] = new(new Rectangle(0, 0, 31, 17), new Rectangle(0, 17, 12, 9), new Rectangle(23, 17, 8, 9)),
        ["LeatherTunic"] = new(new Rectangle(0, 0, 24, 18), new Rectangle(0, 18, 6, 2), new Rectangle(18, 18, 6, 2)),
        ["ClothRobe"] = new(new Rectangle(0, 0, 23, 17), new Rectangle(0, 17, 8, 5), new Rectangle(18, 17, 5, 5)),
        ["KnightChestplate"] = new(new Rectangle(0, 0, 24, 16), new Rectangle(0, 16, 9, 4), new Rectangle(18, 16, 6, 4)),
        ["HuntingChestplate"] = new(new Rectangle(0, 0, 23, 15), new Rectangle(0, 15, 7, 6), new Rectangle(18, 15, 5, 4), new Rectangle(13, 15, 6, 6)),
        ["EnergyRobe"] = new(new Rectangle(0, 0, 24, 17), new Rectangle(0, 17, 7, 4), new Rectangle(19, 17, 5, 4)),
        ["NightmareChestplate"] = new(new Rectangle(0, 0, 30, 19)),
        ["FallenRobe"] = new(new Rectangle(0, 0, 28, 14)),
        ["SoulforgedChestplate"] = new(new Rectangle(0, 0, 30, 19), new Rectangle(0, 19, 10, 5), new Rectangle(22, 19, 8, 7)),
        ["AbyssChestplate"] = new(new Rectangle(0, 0, 30, 18), new Rectangle(0, 18, 9, 6), new Rectangle(20, 18, 10, 7)),
        ["DarkRobe"] = new(new Rectangle(0, 0, 24, 20), new Rectangle(0, 20, 8, 1), new Rectangle(17, 20, 7, 1))
    };

    /// <returns>true when the original full item icon should be drawn.</returns>
    public static bool PreDrawInInventory(Item item, SpriteBatch spriteBatch, Vector2 position,
        Rectangle frame, Color drawColor, float scale)
    {
        if (item?.ModItem is null)
            return true;

        Texture2D texture = TextureAssets.Item[item.type].Value;
        if (texture.Width <= 1 || texture.Height <= 1)
            return true;

        Rectangle[] pieces;
        pieces = GetPieces(item, texture);

        Rectangle bounds = Rectangle.Empty;
        foreach (Rectangle piece in pieces)
        {
            if (piece.X < 0 || piece.Y < 0 || piece.Width <= 0 || piece.Height <= 0 ||
                piece.Right > texture.Width || piece.Bottom > texture.Height)
                return true;
            bounds = bounds.IsEmpty ? piece : Rectangle.Union(bounds, piece);
        }

        // tML's incoming inventory scale is based on the full source frame.
        // Compensate for cropped transparent margins so the visible silhouette
        // keeps the same canonical max-size as an uncropped 32px item icon.
        float sourceMax = Math.Max(texture.Width, texture.Height);
        float visibleMax = Math.Max(bounds.Width, bounds.Height);
        float sourceFit = sourceMax / visibleMax;
        float drawScale = scale * sourceFit;
        Vector2 center = position;
        Vector2 boundsCenter = new(bounds.Center.X, bounds.Center.Y);

        foreach (Rectangle piece in pieces)
        {
            Vector2 pieceCenter = new(piece.Center.X, piece.Center.Y);
            Vector2 drawPosition = center + (pieceCenter - boundsCenter) * drawScale;
            spriteBatch.Draw(texture, drawPosition, piece, drawColor, 0f,
                new Vector2(piece.Width, piece.Height) * 0.5f, drawScale, SpriteEffects.None, 0f);
        }

        return false;
    }

    public static bool PreDrawInWorld(Item item, SpriteBatch spriteBatch, Color alphaColor,
        ref float rotation, ref float scale)
    {
        if (item?.ModItem is null)
            return true;

        Texture2D texture = TextureAssets.Item[item.type].Value;
        if (texture.Width <= 1 || texture.Height <= 1)
            return true;

        Rectangle[] pieces = GetPieces(item, texture);
        Rectangle bounds = GetVisibleBounds(item, texture);
        if (bounds.IsEmpty || bounds.X < 0 || bounds.Y < 0 || bounds.Right > texture.Width || bounds.Bottom > texture.Height)
            return true;

        float renderScale = scale * Math.Min(Math.Max(texture.Width, texture.Height), 32f) /
            Math.Max(bounds.Width, bounds.Height);
        Vector2 boundsCenter = new(bounds.Center.X, bounds.Center.Y);
        Vector2 drawCenter = item.Bottom - Main.screenPosition - new Vector2(0f, bounds.Height * renderScale * .5f);
        Matrix rotationMatrix = Matrix.CreateRotationZ(rotation);
        foreach (Rectangle piece in pieces)
        {
            Vector2 pieceCenter = new(piece.Center.X, piece.Center.Y);
            Vector2 localOffset = (pieceCenter - boundsCenter) * renderScale;
            Vector2 rotatedOffset = Vector2.Transform(localOffset, rotationMatrix);
            spriteBatch.Draw(texture, drawCenter + rotatedOffset, piece, alphaColor, rotation,
                new Vector2(piece.Width, piece.Height) * .5f, renderScale, SpriteEffects.None, 0f);
        }

        return false;
    }

    internal static Rectangle GetVisibleBounds(Item item, Texture2D texture)
    {
        Rectangle bounds = Rectangle.Empty;
        foreach (Rectangle piece in GetPieces(item, texture))
            bounds = bounds.IsEmpty ? piece : Rectangle.Union(bounds, piece);
        return bounds;
    }

    private static Rectangle[] GetPieces(Item item, Texture2D texture)
    {
        if (item?.ModItem is not null && Layouts.TryGetValue(item.ModItem.Name, out IconLayout layout))
            return layout.Pieces;
        if (item?.ModItem is not null && CroppedIcons.TryGetValue(item.ModItem.Name, out Rectangle crop))
            return new[] { crop };
        return new[] { texture.Bounds };
    }
}
