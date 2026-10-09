using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using ReLogic.Content;
using Terraria;
using Terraria.DataStructures;
using Terraria.GameContent;
using Terraria.Graphics;
using Terraria.ID;
using Terraria.ModLoader;
using MilkcraftTerraria.Content;

namespace MilkcraftTerraria.Content.Items;

/// <summary>
/// Zenith's item defaults and ItemCheck timing, with the gray-rimmed Dazhou
/// blade followed by the four intact swords used to craft it.
/// </summary>
public sealed class DazhouIronSword : ModItem
{
    private int _nextCraftedVisual = -1;

    public override string Texture => $"{Mod.Name}/Content/Items/IronSword";

    // CloneDefaults copies Zenith's combat defaults, but mod items do not
    // inherit its vanilla sword-prefix category membership from ItemID sets.
    public override bool MeleePrefix() => true;

    public override void SetDefaults()
    {
        Item.CloneDefaults(ItemID.Zenith);
        Item.shoot = ModContent.ProjectileType<DazhouZenithBlade>();
    }

    public override bool PreDrawInInventory(SpriteBatch spriteBatch, Vector2 position, Rectangle frame,
        Color drawColor, Color itemColor, Vector2 origin, float scale)
    {
        DazhouSwordEdge.DrawInventory(spriteBatch, position, frame, origin, scale);
        return true;
    }

    public override bool PreDrawInWorld(SpriteBatch spriteBatch, Color lightColor, Color alphaColor,
        ref float rotation, ref float scale, int whoAmI)
    {
        DazhouSwordEdge.DrawWorld(spriteBatch, Item, alphaColor, rotation, scale);
        return false;
    }

    public override bool Shoot(Player player, EntitySource_ItemUse_WithAmmo source, Vector2 position,
        Vector2 velocity, int type, int damage, float knockback)
    {
        int swingIndex = (player.itemAnimationMax - player.itemAnimation) / Math.Max(1, player.itemTime);
        // Preserve Zenith's one native profile-random draw and use it to seed
        // the visual cycle. This adds no RNG consumption before ai0/targeting.
        int nativeRandomProfile = FinalFractalHelper.GetRandomProfileIndex();
        if (_nextCraftedVisual < 0)
            _nextCraftedVisual = 1 + (nativeRandomProfile % 4);
        // ai[1] stays a real native profile key so FinalFractal AI and the
        // vanilla shader can resolve it. ai[2] is unused by AI_182 and carries
        // only the synced sword-art variant.
        int visual = 0;
        if (swingIndex != 0)
        {
            visual = _nextCraftedVisual;
            _nextCraftedVisual = visual == 4 ? 1 : visual + 1;
        }
        int profile = DazhouSwordVisualSystem.NativeProfileForVisual(visual);

        Vector2 reachableMouse = Main.MouseWorld;
        player.LimitPointToPlayerReachableArea(ref reachableMouse);
        Vector2 targetVector = reachableMouse - player.MountedCenter;
        bool addRandomOffset = swingIndex == 2;

        if (swingIndex is 1 or 2 && DazhouZenithBlade.TryGetZenithTarget(player, reachableMouse, out int targetIndex))
            targetVector = Main.npc[targetIndex].Center - player.MountedCenter;
        else if (swingIndex == 1)
            addRandomOffset = true;

        if (addRandomOffset)
            targetVector += Terraria.Utils.NextVector2Circular(Main.rand, 150f, 150f);

        Vector2 zenithVelocity = targetVector / 2f;
        float randomAi0 = Main.rand.Next(-100, 101);
        Projectile.NewProjectile(source, position, zenithVelocity, type, damage, knockback,
            player.whoAmI, randomAi0, profile, visual);
        return false;
    }
}

/// <summary>Non-usable Moon Lord material; it has no recipe by design.</summary>
public sealed class HiddenIronSword : ModItem
{
    internal const int VisibleSourceX = 314;
    internal const int VisibleSourceY = 331;
    internal const int VisibleSourceWidth = 545;
    internal const int VisibleSourceHeight = 627;
    private static readonly Rectangle VisibleSource = new(VisibleSourceX, VisibleSourceY, VisibleSourceWidth, VisibleSourceHeight);

    public override void SetDefaults()
    {
        Item.width = 20;
        Item.height = 24;
        Item.maxStack = Item.CommonMaxStack;
        Item.rare = ItemRarityID.White;
        Item.value = Item.sellPrice(silver: 20);
        Item.material = true;
    }

    public override bool PreDrawInInventory(SpriteBatch spriteBatch, Vector2 position, Rectangle frame,
        Color drawColor, Color itemColor, Vector2 origin, float scale)
    {
        Texture2D texture = TextureAssets.Item[Type].Value;
        ValidateSource(texture);
        float fit = Math.Max(texture.Width, texture.Height) / (float)Math.Max(VisibleSource.Width, VisibleSource.Height);
        float drawScale = scale * fit;
        spriteBatch.Draw(texture, position, VisibleSource, drawColor, 0f,
            new Vector2(VisibleSource.Width, VisibleSource.Height) * 0.5f, drawScale, SpriteEffects.None, 0f);
        return false;
    }

    public override bool PreDrawInWorld(SpriteBatch spriteBatch, Color lightColor, Color alphaColor,
        ref float rotation, ref float scale, int whoAmI)
    {
        Texture2D texture = TextureAssets.Item[Type].Value;
        ValidateSource(texture);
        float drawScale = scale * Math.Min(Math.Max(texture.Width, texture.Height), 32f) /
            Math.Max(VisibleSource.Width, VisibleSource.Height);
        Vector2 position = Item.Bottom - Main.screenPosition - new Vector2(0f, VisibleSource.Height * drawScale * 0.5f);
        spriteBatch.Draw(texture, position, VisibleSource, alphaColor, rotation,
            new Vector2(VisibleSource.Width, VisibleSource.Height) * 0.5f, drawScale, SpriteEffects.None, 0f);
        return false;
    }

    private static void ValidateSource(Texture2D texture)
    {
        if (VisibleSource.X < 0 || VisibleSource.Y < 0 || VisibleSource.Right > texture.Width ||
            VisibleSource.Bottom > texture.Height)
            throw new InvalidOperationException($"Broken Iron Sword crop {VisibleSource} outside {texture.Width}x{texture.Height}");
    }
}

/// <summary>
/// Uses the vanilla Final Fractal AI style and defaults, while drawing the
/// existing Iron Sword sprite with a restrained gray emissive rim.
/// </summary>
public sealed class DazhouZenithBlade : ModProjectile
{
    private static readonly MethodInfo GetZenithTargetMethod = typeof(Player).GetMethod(
        "GetZenithTarget", BindingFlags.Instance | BindingFlags.NonPublic);

    public override string Texture => $"{Mod.Name}/Content/Items/IronSword";

    public override void SetStaticDefaults()
    {
        ProjectileID.Sets.TrailCacheLength[Type] = ProjectileID.Sets.TrailCacheLength[ProjectileID.FinalFractal];
        ProjectileID.Sets.TrailingMode[Type] = ProjectileID.Sets.TrailingMode[ProjectileID.FinalFractal];
    }

    public override void SetDefaults()
    {
        Projectile.CloneDefaults(ProjectileID.FinalFractal);
        Projectile.aiStyle = ProjAIStyleID.Zenith;
        AIType = ProjectileID.FinalFractal;
    }

    // Final Fractal advances its blade position from its AI and suppresses
    // the engine's ordinary position += velocity pass. Preserve that exact
    // movement ownership when its AIType is hosted by this mod projectile.
    public override bool ShouldUpdatePosition() => false;

    internal static bool TryGetZenithTarget(Player player, Vector2 cursor, out int targetIndex)
    {
        targetIndex = -1;
        if (GetZenithTargetMethod is null)
            throw new MissingMethodException(typeof(Player).FullName, "GetZenithTarget(Vector2, Single, out Int32)");

        object[] arguments = { cursor, 400f, -1 };
        bool found = (bool)GetZenithTargetMethod.Invoke(player, arguments);
        targetIndex = (int)arguments[2];
        return found;
    }

    public override void AI()
    {
        // AI_182 reads ai[1] only as a real native Final Fractal visual profile;
        // this local accent light/particle supplements that native ribbon.
        if (Projectile.numUpdates != -1)
            return;

        int visual = DazhouSwordVisualSystem.NormalizeVisualId(Projectile.ai[2]);
        Color accent = DazhouSwordVisualSystem.AccentColor(visual);
        float opacity = Projectile.Opacity;
        Vector3 accentLight = accent.ToVector3() * (0.16f * opacity);
        Lighting.AddLight(Projectile.Center, accentLight);

        if (Main.netMode != NetmodeID.Server && Projectile.timeLeft % 8 == 0 && visual >= 3)
        {
            int dustType = visual switch
            {
                3 => DustID.GemSapphire,
                _ => DustID.GoldFlame
            };
            Dust dust = Dust.NewDustPerfect(Projectile.Center, dustType,
                -Projectile.velocity * 0.025f + new Vector2(0f, -0.12f),
                80, accent, 0.62f);
            dust.noGravity = true;
            dust.noLight = true;
        }
    }

    public override bool? Colliding(Rectangle projectileHitbox, Rectangle targetHitbox)
    {
        // Mirrors the local Terraria.Projectile.Colliding type=FinalFractal
        // branch: sampled 40px blade segments, 20px line thickness, and the
        // current segment. The 300px rectangle is only its native broadphase.
        const float bladeHalfLength = 40f;
        const float bladeWidth = 20f;
        const int broadphaseSize = 300;
        float collisionPoint = 0f;

        if (Projectile.oldPos is not null && Projectile.oldRot is not null)
        {
            for (int i = 14; i < Projectile.oldPos.Length; i += 15)
            {
                float sampleAge = Projectile.localAI[0] - i;
                if (sampleAge < 0f || sampleAge > 60f)
                    continue;

                Vector2 sampleTopLeft = Projectile.oldPos[i];
                Rectangle broadphase = new((int)sampleTopLeft.X - broadphaseSize / 2,
                    (int)sampleTopLeft.Y - broadphaseSize / 2, broadphaseSize, broadphaseSize);
                if (!broadphase.Intersects(targetHitbox))
                    continue;

                Vector2 center = sampleTopLeft + Projectile.Size * 0.5f;
                Vector2 direction = (Projectile.oldRot[i] + MathHelper.PiOver2).ToRotationVector2();
                if (Collision.CheckAABBvLineCollision(targetHitbox.TopLeft(), targetHitbox.Size(),
                    center - direction * bladeHalfLength, center + direction * bladeHalfLength,
                    bladeWidth, ref collisionPoint))
                    return true;
            }
        }

        Rectangle currentBroadphase = new((int)Projectile.position.X - broadphaseSize / 2,
            (int)Projectile.position.Y - broadphaseSize / 2, broadphaseSize, broadphaseSize);
        if (!currentBroadphase.Intersects(targetHitbox))
            return false;

        Vector2 currentDirection = (Projectile.rotation + MathHelper.PiOver2).ToRotationVector2();
        Vector2 currentCenter = Projectile.Center;
        return Collision.CheckAABBvLineCollision(targetHitbox.TopLeft(), targetHitbox.Size(),
            currentCenter - currentDirection * bladeHalfLength,
            currentCenter + currentDirection * bladeHalfLength, bladeWidth, ref collisionPoint);
    }

    public override bool PreDraw(ref Color lightColor)
    {
        // Vanilla draws the profile-specific Final Fractal vertex-strip before
        // the body. Call that same helper for the custom projectile type.
        new FinalFractalHelper().Draw(Projectile);
        DazhouSwordEdge.DrawProjectile(Main.spriteBatch, Projectile, lightColor);
        return false;
    }
}

/// <summary>One cached alpha silhouette supplies only a thin gray rim.</summary>
internal sealed class DazhouSwordVisualSystem : ModSystem
{
    internal static Texture2D EdgeMask { get; private set; }
    private static readonly Dictionary<int, Texture2D> ProjectileMasks = new();
    private static readonly Dictionary<int, Texture2D> SwordBodies = new();
    internal static int NormalizeVisualId(float visual) => Math.Clamp((int)MathF.Round(visual), 0, 4);

    internal static int NativeProfileForVisual(int visual) => NormalizeVisualId(visual) switch
    {
        2 => 795, // Native red Knight profile.
        3 => 121, // Native gold/orange Templar profile.
        4 => 795, // Native red Soulforged profile.
        _ => 0     // Native default silver-white profile; never random.
    };

    internal static Color AccentColor(int visual) => NormalizeVisualId(visual) switch
    {
        2 => new Color(238, 48, 62),
        3 => new Color(80, 177, 255),
        4 => new Color(255, 179, 62),
        _ => new Color(190, 198, 210)
    };

    internal static Color RimColor(int visual) => NormalizeVisualId(visual) switch
    {
        0 => new Color(185, 191, 202, 210),
        1 => Color.Transparent,
        2 => new Color(238, 48, 62, 210),
        3 => new Color(255, 199, 75, 210),
        _ => new Color(218, 48, 58, 210)
    };

    internal static Color SecondaryRimColor(int visual) => NormalizeVisualId(visual) switch
    {
        3 => new Color(76, 174, 255, 190),
        4 => new Color(255, 190, 76, 180),
        _ => Color.Transparent
    };

    internal static int ItemTypeForVisual(int visual) => NormalizeVisualId(visual) switch
    {
        2 => ModContent.ItemType<KnightSword>(),
        3 => ModContent.ItemType<TemplarSword>(),
        4 => ModContent.ItemType<SoulforgedSword>(),
        _ => ModContent.ItemType<IronSword>()
    };

    internal static Texture2D BodyForVisual(int visual) => SwordBodies[NormalizeVisualId(visual)];
    internal static Texture2D MaskForVisual(int visual) => ProjectileMasks[NormalizeVisualId(visual)];

    private static string TexturePathForVisual(int visual) => NormalizeVisualId(visual) switch
    {
        2 => "KnightSword",
        3 => "TemplarSword",
        4 => "SoulforgedSword",
        _ => "IronSword"
    };

    private static Texture2D CreateAlphaMask(Texture2D source, Rectangle? sourceRect)
    {
        Rectangle rect = sourceRect ?? new Rectangle(0, 0, source.Width, source.Height);
        if (rect.X < 0 || rect.Y < 0 || rect.Right > source.Width || rect.Bottom > source.Height)
            throw new InvalidOperationException($"Dazhou visual crop {rect} outside {source.Width}x{source.Height}");

        Color[] all = new Color[source.Width * source.Height];
        source.GetData(all);
        Color[] pixels = new Color[rect.Width * rect.Height];
        for (int y = 0; y < rect.Height; y++)
        for (int x = 0; x < rect.Width; x++)
        {
            float alpha = all[(rect.Y + y) * source.Width + rect.X + x].A / 255f;
            pixels[y * rect.Width + x] = Color.White * alpha;
        }

        Texture2D mask = new(Main.graphics.GraphicsDevice, rect.Width, rect.Height, false, SurfaceFormat.Color);
        mask.SetData(pixels);
        return mask;
    }

    public override void Load()
    {
        if (Main.dedServ)
            return;

        Main.RunOnMainThread(() =>
        {
            ProjectileMasks.Clear();
            SwordBodies.Clear();
            for (int visual = 0; visual < 5; visual++)
            {
                int itemType = ItemTypeForVisual(visual);
                string path = $"{Mod.Name}/Content/Items/{TexturePathForVisual(visual)}";
                Texture2D source = ModContent.Request<Texture2D>(path, AssetRequestMode.ImmediateLoad).Value;
                if (source.Width <= 1 || source.Height <= 1)
                    throw new InvalidOperationException($"Dazhou sword visual loaded placeholder: id={visual}, itemType={itemType}, path={path}, size={source.Width}x{source.Height}");
                SwordBodies[visual] = source;
                ProjectileMasks[visual] = CreateAlphaMask(source, null);
            }

            EdgeMask = ProjectileMasks[0];
        }).GetAwaiter().GetResult();
    }

    public override void Unload()
    {
        if (ProjectileMasks.Count == 0)
            return;
        if (Main.dedServ)
        {
            ProjectileMasks.Clear();
            EdgeMask = null;
            return;
        }
        Main.RunOnMainThread(() =>
        {
            foreach (Texture2D mask in ProjectileMasks.Values.Distinct())
                mask.Dispose();
            ProjectileMasks.Clear();
            SwordBodies.Clear();
            EdgeMask = null;
        }).GetAwaiter().GetResult();
    }
}

internal static class DazhouSwordEdge
{
    private static readonly Vector2[] RimOffsets =
    {
        new(-1f, -1f), new(0f, -1f), new(1f, -1f), new(-1f, 0f),
        new(1f, 0f), new(-1f, 1f), new(0f, 1f), new(1f, 1f)
    };
    private static readonly Color RimColor = new(185, 191, 202, 210);

    internal static void DrawInventory(SpriteBatch spriteBatch, Vector2 position,
        Rectangle frame, Vector2 origin, float scale)
    {
        Texture2D mask = DazhouSwordVisualSystem.EdgeMask;
        if (mask is null)
            return;
        foreach (Vector2 offset in RimOffsets)
            spriteBatch.Draw(mask, position + offset * scale, frame, RimColor, 0f, origin,
                scale, SpriteEffects.None, 0f);
    }

    internal static void DrawWorld(SpriteBatch spriteBatch, Item item, Color alphaColor,
        float rotation, float scale)
    {
        Texture2D mask = DazhouSwordVisualSystem.EdgeMask;
        Texture2D body = DazhouSwordVisualSystem.BodyForVisual(1);
        Vector2 position = item.Bottom - Main.screenPosition - new Vector2(0f, body.Height * scale * 0.5f);
        Vector2 origin = body.Size() * 0.5f;
        if (mask is not null)
        {
            Color tint = RimColor * (alphaColor.A / 255f);
            foreach (Vector2 offset in RimOffsets)
                spriteBatch.Draw(mask, position + offset * scale, null, tint, rotation, origin,
                    scale, SpriteEffects.None, 0f);
        }
        spriteBatch.Draw(body, position, null, alphaColor, rotation, origin,
                scale, SpriteEffects.None, 0f);
        
    }

    internal static void DrawProjectile(SpriteBatch spriteBatch, Projectile projectile, Color lightColor)
    {
        int visual = DazhouSwordVisualSystem.NormalizeVisualId(projectile.ai[2]);
        int itemType = DazhouSwordVisualSystem.ItemTypeForVisual(visual);
        Texture2D body = DazhouSwordVisualSystem.BodyForVisual(visual);
        Texture2D mask = DazhouSwordVisualSystem.MaskForVisual(visual);
        Rectangle? source = null;
        Vector2 artSize = body.Size();
        Vector2 position = projectile.Center - Main.screenPosition + new Vector2(0f, projectile.gfxOffY);
        Vector2 origin = artSize * 0.5f;
        // Match Main.DrawProj_DrawNormalProjs' Final Fractal body transform:
        // it rotates the selected sword art by -45 degrees per facing, flips
        // left-facing sprites, and draws at 1.25x projectile scale. The
        // Iron Sword art's blade axis is already diagonal, so this keeps it
        // aligned with the vanilla Final Fractal collision axis.
        float direction = projectile.spriteDirection;
        float rotation = projectile.rotation - MathHelper.PiOver4 * direction;
        SpriteEffects effects = projectile.spriteDirection == -1
            ? SpriteEffects.FlipHorizontally
            : SpriteEffects.None;
        float scale = projectile.scale * 1.25f;
        Color bodyColor = Color.Lerp(lightColor, Color.White, 0.10f);
        Color rim = DazhouSwordVisualSystem.RimColor(visual);
        Color accent = DazhouSwordVisualSystem.SecondaryRimColor(visual);
        if (mask is not null)
        {
            if (rim.A > 0)
                foreach (Vector2 offset in RimOffsets)
                    spriteBatch.Draw(mask, position + offset * scale, null, rim, rotation,
                        new Vector2(mask.Width, mask.Height) * 0.5f, scale, effects, 0f);

            if (accent.A > 0)
            {
                Vector2[] accentOffsets = visual == 3
                    ? new[] { new Vector2(0f, -1f), new Vector2(1f, 0f), new Vector2(0f, 1f) }
                    : new[] { new Vector2(-1f, 0f), new Vector2(0f, -1f) };
                foreach (Vector2 offset in accentOffsets)
                    spriteBatch.Draw(mask, position + offset * scale, null,
                        accent, rotation, new Vector2(mask.Width, mask.Height) * 0.5f, scale, effects, 0f);
            }
        }
        spriteBatch.Draw(body, position, source, bodyColor, rotation, origin,
            scale, effects, 0f);
    }
}
