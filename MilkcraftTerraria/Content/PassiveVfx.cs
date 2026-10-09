using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.DataStructures;
using Terraria.ModLoader;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.Localization;

namespace MilkcraftTerraria.Content;

internal sealed class PassiveVfxTextureSystem : ModSystem
{
    internal static Texture2D RadialGlow { get; private set; }
    private static volatile int _generation;

    public override void Load()
    {
        if (Main.dedServ) return;
        const int size = 48;
        Color[] pixels = new Color[size * size];
        Vector2 center = new(size * .5f, size * .5f);
        float radius = size * .5f;
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float normalized = Vector2.Distance(new Vector2(x + .5f, y + .5f), center) / radius;
            float glow = Math.Max(0f, 1f - normalized);
            byte alpha = (byte)(Math.Clamp(glow * glow * .72f, 0f, 1f) * 255f);
            pixels[y * size + x] = new Color(alpha, alpha, alpha, alpha);
        }
        int generation = ++_generation;
        Main.RunOnMainThread(() =>
        {
            if (generation != _generation || Main.dedServ || Main.graphics?.GraphicsDevice is null) return;
            Texture2D texture = new(Main.graphics.GraphicsDevice, size, size, false, SurfaceFormat.Color);
            texture.SetData(pixels);
            RadialGlow = texture;
        }).GetAwaiter().GetResult();
    }

    public override void Unload()
    {
        int generation = ++_generation;
        Main.RunOnMainThread(() =>
        {
            if (generation != _generation) return;
            Texture2D texture = RadialGlow;
            RadialGlow = null;
            texture?.Dispose();
        }).GetAwaiter().GetResult();
    }

    internal static void AddPlayerGlow(PlayerDrawSet drawInfo, Vector2 position, Color color, float scale, float alpha, float rotation = 0f)
    {
        Texture2D glow = RadialGlow;
        if (glow is null || alpha <= 0f) return;
        drawInfo.DrawDataCache.Add(new DrawData(glow, position, null, color * alpha, rotation,
            glow.Size() * .5f, scale, SpriteEffects.None, 0));
    }

    internal static void DrawProjectileGlow(SpriteBatch batch, Vector2 position, Color color, float scale, float alpha, float rotation = 0f)
    {
        Texture2D glow = RadialGlow;
        if (glow is null || alpha <= 0f) return;
        batch.Draw(glow, position, null, color * alpha, rotation, glow.Size() * .5f, scale,
            SpriteEffects.None, 0f);
    }
}

/// <summary>Small, read-only visual feedback for the already-running 0.2.5 suit states.</summary>
public sealed class MilkcraftPassiveDrawLayer : PlayerDrawLayer
{
    internal static int DiagnosticLineCount { get; private set; }
    internal static bool DiagnosticCapture { get; set; }
    internal static float DiagnosticMinX { get; private set; }
    internal static float DiagnosticMinY { get; private set; }
    internal static float DiagnosticMaxX { get; private set; }
    internal static float DiagnosticMaxY { get; private set; }

    internal static void ResetDiagnosticBounds()
    {
        DiagnosticCapture = true;
        DiagnosticLineCount = 0;
        DiagnosticMinX = DiagnosticMinY = float.PositiveInfinity;
        DiagnosticMaxX = DiagnosticMaxY = float.NegativeInfinity;
    }

    public override Position GetDefaultPosition() => new AfterParent(PlayerDrawLayers.Torso);

    public override bool GetDefaultVisibility(PlayerDrawSet drawInfo)
    {
        Player player = drawInfo.drawPlayer;
        if (Main.dedServ || drawInfo.shadow != 0f || player.dead || player.ghost || !player.active)
            return false;
        return player.GetModPlayer<MilkcraftPassivePlayer>().Suit.Length > 0;
    }

    protected override void Draw(ref PlayerDrawSet drawInfo)
    {
        Player player = drawInfo.drawPlayer;
        MilkcraftPassivePlayer state = player.GetModPlayer<MilkcraftPassivePlayer>();
        string suit = state.Suit;
        if (suit.Length == 0) return;

        Color color = MilkcraftPassiveVfx.SuitColor(suit);
        float time = (float)Main.GameUpdateCount / 60f;
        float pulse = .84f + .16f * (float)Math.Sin(time * 4.2f);
        Vector2 center = drawInfo.Position - Main.screenPosition + new Vector2(player.width * .5f, player.height * .62f + player.gfxOffY);
        Texture2D pixel = TextureAssets.MagicPixel.Value;
        float eventFlash = state.VfxPulse > 0 ? Math.Min(1f, state.VfxPulse / 14f) : 0f;

        switch (suit)
        {
            case "Iron":
                if (state.VfxShield > 0 || state.VfxShieldCooldown > 0 || eventFlash > 0)
                {
                    Color steel = state.VfxShield > 0 ? color : new Color(140, 157, 169);
                    DrawCoreGlow(drawInfo, center, steel, 1.05f, .13f + eventFlash * .20f);
                    DrawShieldPlates(drawInfo, pixel, center + new Vector2(0, 2), 18f, steel, .55f + eventFlash * .38f, time * 1.35f, 3, new Color(232, 182, 75));
                    if (eventFlash > .5f) DrawRadialBurst(drawInfo, pixel, center, new Color(232, 182, 75), eventFlash * .72f, 6, time);
                }
                break;
            case "Leather":
                if (state.VfxBuffTime > 0)
                {
                    Vector2 feet = center + new Vector2(0, 15);
                    DrawGlow(drawInfo, feet, color, .82f, .12f * pulse);
                    DrawWindBlades(drawInfo, pixel, feet, color, new Color(232, 170, 72), time);
                    float motion = Math.Min(1f, player.velocity.Length() / 3f);
                    if (motion > .05f)
                        DrawMotionGusts(drawInfo, pixel, center, color, motion, player.direction, time);
                }
                break;
            case "Cloth":
                if ((state.VfxManaWindow > 0 && state.VfxManaPaid > 0) || eventFlash > 0)
                {
                    Vector2 chest = center + new Vector2(0, -9);
                    float alpha = .48f + eventFlash * .42f;
                    DrawGlow(drawInfo, chest, color, .74f, .20f * eventFlash + .07f);
                    DrawDoubleRuneTracks(drawInfo, pixel, chest, color, new Color(201, 127, 53), alpha, time);
                    DrawOrbitNodes(drawInfo, pixel, chest, color, 3, time * 1.8f, alpha);
                }
                break;
            case "Knight":
                if (state.VfxShield > 0 || state.VfxShieldCooldown > 0 || state.VfxBuffTime > 0)
                {
                    Color guard = state.VfxShield > 0 ? color : new Color(172, 117, 73);
                    DrawCoreGlow(drawInfo, center, guard, 1.1f, .12f + eventFlash * .16f);
                    DrawShieldPlates(drawInfo, pixel, center + new Vector2(0, -1), 19f, guard, .49f + eventFlash * .35f, -time * 1.8f, 3, new Color(233, 56, 56));
                    if (state.VfxBuffTime > 0) DrawFlameCrest(drawInfo, pixel, center, color, new Color(233, 56, 56), time);
                    if (eventFlash > .5f) DrawRadialBurst(drawInfo, pixel, center, new Color(245, 183, 47), eventFlash, 8, time);
                }
                break;
            case "Hunting":
                if (state.VfxHuntTarget >= 0)
                {
                    Vector2 shoulder = center + new Vector2(player.direction * -9f, -15f);
                    DrawShoulderCrystals(drawInfo, pixel, shoulder, color, time, state.VfxHuntHits);
                    DrawGlow(drawInfo, shoulder, color, .58f, .14f + eventFlash * .28f);
                    DrawTargetReticle(drawInfo, pixel, center + new Vector2(player.direction * 19f, -14f), color,
                        state.VfxHuntProcFlash > 0 ? 1f : .56f, time * 1.5f, state.VfxHuntProcFlash > 0);
                    if (state.VfxHuntProcFlash > 0) DrawRadialBurst(drawInfo, pixel, shoulder, new Color(96, 219, 238), eventFlash, 6, time);
                }
                break;
            case "Energy":
                if (state.VfxShield > 0 || state.VfxEnergyPaid > 0 || state.VfxShieldCooldown > 0)
                {
                    Color violet = state.VfxShield > 0 ? color : new Color(216, 139, 255);
                    DrawCoreGlow(drawInfo, center, violet, 1.08f, state.VfxShield > 0 ? .17f * pulse : .09f + eventFlash * .16f);
                    if (state.VfxShield > 0) DrawCrystalAegis(drawInfo, pixel, center, violet, new Color(216, 139, 255), time, pulse);
                    else DrawChargingNodes(drawInfo, pixel, center, violet, state.VfxEnergyPaid, time);
                    if (state.VfxShieldCooldown > 0 && eventFlash > 0) DrawRadialBurst(drawInfo, pixel, center, new Color(216, 139, 255), eventFlash * .72f, 6, time);
                }
                break;
            case "Templar":
                if (state.VfxShield > 0 || state.VfxShieldCooldown > 0 || eventFlash > 0)
                {
                    Vector2 feet = center + new Vector2(0, 16);
                    DrawCoreGlow(drawInfo, feet, color, .98f, .14f + eventFlash * .24f);
                    DrawSixPointSigil(drawInfo, pixel, feet, color, new Color(51, 203, 232), state.VfxShield > 0 ? .66f : .30f, time * .24f);
                    DrawShieldPlates(drawInfo, pixel, center + new Vector2(0, -6), 15f, color,
                        state.VfxShield > 0 ? .56f : .26f, -time * .92f, 3, new Color(51, 203, 232));
                    DrawOrbitNodes(drawInfo, pixel, feet, new Color(51, 203, 232), 3, -time * 1.1f, .72f);
                    if (eventFlash > .15f) DrawRadialBurst(drawInfo, pixel, center, color, eventFlash * .8f, 8, time * 1.2f);
                }
                break;
            case "Nightmare":
                if (eventFlash > 0)
                {
                    Vector2 rear = center + new Vector2(-player.direction * 11f, -8f);
                    DrawCoreGlow(drawInfo, rear, color, 1.04f, eventFlash * .22f);
                    DrawCrescentBlades(drawInfo, pixel, rear, color, new Color(246, 200, 88), eventFlash, time);
                    DrawRadialBurst(drawInfo, pixel, rear, new Color(246, 200, 88), eventFlash * .74f, 7, time);
                }
                break;
            case "Fallen":
                if (eventFlash > 0)
                {
                    Vector2 shoulders = center + new Vector2(0, -7f);
                    DrawCoreGlow(drawInfo, shoulders, color, .95f, eventFlash * .18f);
                    DrawFeatherWings(drawInfo, pixel, shoulders, color,
                        MilkcraftPassiveVfx.SuitAccentColor(suit), eventFlash, time);
                    DrawDoubleRuneTracks(drawInfo, pixel, center + new Vector2(0, 1), new Color(81, 205, 232), color,
                        eventFlash * .52f, time * .8f);
                }
                break;
            case "Soulforged":
                if (state.VfxShield > 0 || state.VfxBuffTime > 0 || eventFlash > 0)
                {
                    Color soul = color;
                    DrawCoreGlow(drawInfo, center, soul, 1.12f, (state.VfxShield > 0 ? .17f : .07f) * pulse + eventFlash * .20f);
                    DrawShieldPlates(drawInfo, pixel, center, 18f, soul, (state.VfxShield > 0 ? .62f : .30f) * pulse + eventFlash * .34f,
                        time * .82f, 3, new Color(227, 175, 54));
                    if (state.VfxBuffTime > 0) DrawSoulFlames(drawInfo, pixel, center, time, soul, new Color(227, 175, 54));
                    if (eventFlash > .55f) DrawRadialBurst(drawInfo, pixel, center, new Color(227, 175, 54), eventFlash * .88f, 9, time);
                }
                break;
            case "Abyss":
                if (eventFlash > 0)
                {
                    Vector2 mark = center + new Vector2(0, 1);
                    Color indigo = new Color(66, 70, 181);
                    DrawCoreGlow(drawInfo, mark, color, .98f, eventFlash * .24f);
                    DrawAbyssSpiral(drawInfo, pixel, mark, color, indigo, eventFlash, time);
                    DrawRadialBurst(drawInfo, pixel, mark, color, eventFlash * .60f, 4, time * .7f);
                }
                break;
            case "Dark":
                if (eventFlash > 0)
                {
                    Vector2 rune = center + new Vector2(0, -15);
                    Color violet = new Color(103, 64, 183);
                    DrawCoreGlow(drawInfo, rune, color, .88f, eventFlash * .25f);
                    DrawFourRayEcho(drawInfo, pixel, rune, color, violet, eventFlash, time);
                }
                break;
        }
    }

    private const float PerimeterExtent = 1.62f;

    private static Vector2 GetSuitVfxAnchor(PlayerDrawSet drawInfo)
    {
        Player player = drawInfo.drawPlayer;
        return drawInfo.Position - Main.screenPosition + new Vector2(player.width * .5f,
            player.height * .62f + player.gfxOffY);
    }

    private static void DrawGlow(PlayerDrawSet drawInfo, Vector2 center, Color color, float scale, float alpha)
    {
        Vector2 anchor = GetSuitVfxAnchor(drawInfo);
        Vector2 outerPosition = anchor + (center - anchor) * PerimeterExtent;
        PassiveVfxTextureSystem.AddPlayerGlow(drawInfo, outerPosition, color, scale, alpha);
    }

    private static void DrawCoreGlow(PlayerDrawSet drawInfo, Vector2 center, Color color, float scale, float alpha)
        => PassiveVfxTextureSystem.AddPlayerGlow(drawInfo, center, color, scale * .52f, alpha * .5f);

    private static void DrawShieldPlates(PlayerDrawSet drawInfo, Texture2D pixel, Vector2 center, float radius,
        Color main, float alpha, float phase, int count, Color accent)
    {
        for (int i = 0; i < count; i++)
        {
            float angle = phase + MathHelper.TwoPi * i / count;
            Vector2 radial = new((float)Math.Cos(angle), (float)Math.Sin(angle) * .60f);
            radial = radial.SafeNormalize(Vector2.UnitX);
            Vector2 tangent = new(-radial.Y, radial.X);
            Vector2 plate = center + radial * radius;
            DrawGlow(drawInfo, plate, i == 1 ? accent : main, .42f, alpha * .20f);
            Vector2 a = plate - radial * 5f - tangent * 4f;
            Vector2 b = plate + radial * 4f - tangent * 4f;
            Vector2 c = plate + radial * 7f;
            Vector2 d = plate + radial * 3f + tangent * 4f;
            Vector2 e = plate - radial * 5f + tangent * 4f;
            Color edge = i == 1 ? accent : main;
            DrawLine(drawInfo, pixel, a, b, edge * alpha, 2.1f);
            DrawLine(drawInfo, pixel, b, c, edge * alpha, 2.1f);
            DrawLine(drawInfo, pixel, c, d, edge * alpha, 2.1f);
            DrawLine(drawInfo, pixel, d, e, main * (alpha * .72f), 1.5f);
            DrawLine(drawInfo, pixel, e, a, edge * alpha, 2.1f);
            DrawLine(drawInfo, pixel, a, c, Color.White * (alpha * .50f), .8f);
        }
    }

    private static void DrawWindBlades(PlayerDrawSet drawInfo, Texture2D pixel, Vector2 center, Color green, Color amber, float time)
    {
        for (int side = -1; side <= 1; side += 2)
        {
            for (int blade = 0; blade < 2; blade++)
            {
                float phase = time * 3.4f + blade * 1.5f + (side > 0 ? .7f : 0f);
            Vector2 root = center + new Vector2(side * (4f + blade * 2f), 2f + blade * 2f);
            Vector2 tip = root + new Vector2(side * (10f + (float)Math.Sin(phase) * 2f), -6f - blade * 4f);
            Vector2 bend = Vector2.Lerp(root, tip, .58f) + new Vector2(-side * 2f, -2f);
                Color color = blade == 0 ? green : amber;
                DrawGlow(drawInfo, tip, color, .32f, .15f);
                DrawLine(drawInfo, pixel, root, bend, color * .68f, 1.7f);
                DrawLine(drawInfo, pixel, bend, tip, color * .52f, 1.2f);
                DrawLine(drawInfo, pixel, bend, bend + new Vector2(side * -3f, -2f), Color.White * .42f, .8f);
            }
        }
    }

    private static void DrawMotionGusts(PlayerDrawSet drawInfo, Texture2D pixel, Vector2 center, Color color, float motion, int direction, float time)
    {
        float travel = direction == 0 ? 1f : -direction;
        for (int i = 0; i < 3; i++)
        {
            float y = 6f + i * 3f + (float)Math.Sin(time * 9f + i * 1.4f) * 1.8f;
            Vector2 a = center + new Vector2(travel * (9f + i * 2f), y);
            Vector2 b = center + new Vector2(travel * (14f + i * 3f), y - 1.2f);
            Vector2 c = center + new Vector2(travel * (18f + i * 3f), y - 3.3f);
            DrawLine(drawInfo, pixel, a, b, color * (.44f * motion), 1.8f - i * .25f);
            DrawLine(drawInfo, pixel, b, c, color * (.22f * motion), 1.1f);
        }
    }

    private static void DrawDoubleRuneTracks(PlayerDrawSet drawInfo, Texture2D pixel, Vector2 center, Color main, Color accent, float alpha, float time)
    {
        DrawEllipticTrack(drawInfo, pixel, center, 10f, 5f, main, alpha * .65f, time);
        DrawEllipticTrack(drawInfo, pixel, center, 14f, 7f, accent, alpha * .40f, -time * .82f + .4f);
        for (int i = 0; i < 3; i++)
        {
            float angle = time * 1.65f + i * MathHelper.TwoPi / 3f;
            Vector2 gem = center + new Vector2((float)Math.Cos(angle) * 11f, (float)Math.Sin(angle) * 5.5f);
            DrawDiamond(drawInfo, pixel, gem, 2.2f, 3.5f, i == 1 ? accent : main, alpha * .86f, angle);
            DrawGlow(drawInfo, gem, i == 1 ? accent : main, .26f, alpha * .18f);
        }
    }

    private static void DrawEllipticTrack(PlayerDrawSet drawInfo, Texture2D pixel, Vector2 center,
        float radiusX, float radiusY, Color color, float alpha, float phase)
    {
        const int pieces = 18;
        for (int i = 0; i < pieces; i++)
        {
            float a = phase + MathHelper.TwoPi * i / pieces;
            float b = phase + MathHelper.TwoPi * (i + 1) / pieces;
            Vector2 p0 = center + new Vector2((float)Math.Cos(a) * radiusX, (float)Math.Sin(a) * radiusY);
            Vector2 p1 = center + new Vector2((float)Math.Cos(b) * radiusX, (float)Math.Sin(b) * radiusY);
            DrawLine(drawInfo, pixel, p0, p1, color * alpha, 1.1f);
        }
    }

    private static void DrawCrystalAegis(PlayerDrawSet drawInfo, Texture2D pixel, Vector2 center, Color main, Color accent, float time, float pulse)
    {
        const int count = 6;
        float phase = -MathHelper.PiOver2 + time * .16f;
        for (int i = 0; i < count; i++)
        {
            float a = phase + MathHelper.TwoPi * i / count;
            float b = phase + MathHelper.TwoPi * (i + 1) / count;
            Vector2 point = center + new Vector2((float)Math.Cos(a) * 18f, (float)Math.Sin(a) * 10f);
            Vector2 next = center + new Vector2((float)Math.Cos(b) * 18f, (float)Math.Sin(b) * 10f);
            DrawDiamond(drawInfo, pixel, point, 3f, 5f, i % 2 == 0 ? main : accent, .74f, a);
            DrawGlow(drawInfo, point, i % 2 == 0 ? main : accent, .36f, .19f * pulse);
            DrawLine(drawInfo, pixel, point, next, main * (.30f * pulse), 1.1f);
        }
        DrawDiamond(drawInfo, pixel, center, 2.5f, 3f, accent, .72f, time * .2f);
    }

    private static void DrawChargingNodes(PlayerDrawSet drawInfo, Texture2D pixel, Vector2 center, Color color, int mana, float time)
    {
        int nodes = Math.Clamp(1 + mana / 4, 1, 5);
        for (int i = 0; i < nodes; i++)
        {
            float angle = time * (i % 2 == 0 ? 1.3f : -1.1f) + i * MathHelper.TwoPi / nodes;
            Vector2 direction = new((float)Math.Cos(angle), (float)Math.Sin(angle));
            Vector2 node = center + new Vector2(direction.X * 18f, direction.Y * 10f);
            DrawDiamond(drawInfo, pixel, node, 2.3f, 4f, color, .72f, angle);
            DrawGlow(drawInfo, node, color, .34f, .18f);
            if (mana >= 20) DrawLine(drawInfo, pixel, node, center + direction * 14f, color * .22f, .8f);
        }
    }

    private static void DrawSixPointSigil(PlayerDrawSet drawInfo, Texture2D pixel, Vector2 center,
        Color main, Color accent, float alpha, float phase)
    {
        for (int i = 0; i < 6; i++)
        {
            float a = phase + MathHelper.TwoPi * i / 6f;
            float b = phase + MathHelper.TwoPi * (i + 1) / 6f;
            Vector2 outer = center + new Vector2((float)Math.Cos(a) * 20f, (float)Math.Sin(a) * 7f);
            Vector2 nextOuter = center + new Vector2((float)Math.Cos(b) * 20f, (float)Math.Sin(b) * 7f);
            Vector2 inner = center + new Vector2((float)Math.Cos(a) * 9f, (float)Math.Sin(a) * 3.4f);
            Vector2 nextInner = center + new Vector2((float)Math.Cos(b) * 9f, (float)Math.Sin(b) * 3.4f);
            DrawLine(drawInfo, pixel, inner, outer, (i % 2 == 0 ? main : accent) * alpha, 1.6f);
            DrawDiamond(drawInfo, pixel, outer, 2.5f, 4f, i % 2 == 0 ? main : accent, alpha, a);
            DrawLine(drawInfo, pixel, outer, nextOuter, main * (alpha * .68f), 1.5f);
            DrawLine(drawInfo, pixel, inner, nextInner, accent * (alpha * .66f), 1.1f);
        }
        DrawDiamond(drawInfo, pixel, center, 4f, 5f, accent, alpha, phase);
    }

    private static void DrawShoulderCrystals(PlayerDrawSet drawInfo, Texture2D pixel, Vector2 center, Color color, float time, int hits)
    {
        for (int i = 0; i < 3; i++)
        {
            float angle = -1.35f + i * .58f + (float)Math.Sin(time * 3f + i) * .08f;
            Vector2 gem = center + new Vector2((float)Math.Cos(angle) * (5f + i * 3f), (float)Math.Sin(angle) * (4f + i * 2f));
            DrawDiamond(drawInfo, pixel, gem, 2.2f, 5f, i < hits ? color : new Color(98, 219, 238),
                i < hits ? .92f : .68f, angle);
            DrawGlow(drawInfo, gem, color, .34f, .16f);
        }
    }

    private static void DrawTargetReticle(PlayerDrawSet drawInfo, Texture2D pixel, Vector2 center, Color color, float alpha, float time, bool burst)
    {
        float r = burst ? 9f + (float)Math.Sin(time * 5f) * 2f : 7f;
        DrawDiamond(drawInfo, pixel, center, r, r * .72f, color, alpha, time * .22f);
        for (int i = 0; i < 4; i++)
        {
            float a = time * .16f + i * MathHelper.PiOver2;
            Vector2 dir = new((float)Math.Cos(a), (float)Math.Sin(a));
            Vector2 p = center + dir * (r + 2f);
            DrawLine(drawInfo, pixel, p, p + dir * (burst ? 4f : 2.5f), color * alpha, burst ? 1.8f : 1.1f);
        }
        DrawGlow(drawInfo, center, color, burst ? .48f : .28f, alpha * .24f);
    }

    private static void DrawCrescentBlades(PlayerDrawSet drawInfo, Texture2D pixel, Vector2 center, Color main, Color accent, float alpha, float time)
    {
        for (int i = 0; i < 8; i++)
        {
            float a = -.9f + MathHelper.Pi * i / 7f + time * .18f;
            float b = -.9f + MathHelper.Pi * (i + 1) / 7f + time * .18f;
            Vector2 p0 = center + new Vector2((float)Math.Cos(a) * 15f, (float)Math.Sin(a) * 9f);
            Vector2 p1 = center + new Vector2((float)Math.Cos(b) * 15f, (float)Math.Sin(b) * 9f);
            DrawLine(drawInfo, pixel, p0, p1, main * alpha, 2f);
        }
        for (int i = 0; i < 3; i++)
        {
            float a = time * 1.8f + i * MathHelper.TwoPi / 3f;
            Vector2 tip = center + new Vector2((float)Math.Cos(a) * 21f, (float)Math.Sin(a) * 12f);
            DrawDiamond(drawInfo, pixel, tip, 2.8f, 6f, i == 1 ? accent : main, alpha, a);
            DrawGlow(drawInfo, tip, i == 1 ? accent : main, .40f, alpha * .24f);
        }
    }

    private static void DrawFlameCrest(PlayerDrawSet drawInfo, Texture2D pixel, Vector2 center, Color main, Color accent, float time)
    {
        for (int side = -1; side <= 1; side += 2)
        for (int flame = 0; flame < 2; flame++)
        {
            float wave = (float)Math.Sin(time * 8f + side + flame * 1.7f);
            Vector2 root = center + new Vector2(side * (11f + flame * 2f), 4f - flame * 5f);
            Vector2 mid = root + new Vector2(side * (2f + wave), -4f - flame * 1.3f);
            Vector2 tip = root + new Vector2(side * (3f + wave * 1.7f), -9f - flame * 2f);
            DrawLine(drawInfo, pixel, root, mid, main * .64f, 2f);
            DrawLine(drawInfo, pixel, mid, tip, accent * .68f, 1.3f);
        }
    }

    private static void DrawSoulFlames(PlayerDrawSet drawInfo, Texture2D pixel, Vector2 center, float time, Color main, Color accent)
    {
        for (int side = -1; side <= 1; side += 2)
        for (int flame = 0; flame < 3; flame++)
        {
            float phase = time * 4.2f + flame * 1.45f + side;
            Vector2 root = center + new Vector2(side * (14f + flame), 5f - flame * 4f);
            Vector2 middle = root + new Vector2(side * (3f + (float)Math.Sin(phase) * 2f), -5f - flame);
            Vector2 tip = root + new Vector2(side * (1.5f + (float)Math.Cos(phase) * 2f), -11f - flame * 2f);
            DrawGlow(drawInfo, tip, flame % 2 == 0 ? main : accent, .34f, .18f);
            DrawLine(drawInfo, pixel, root, middle, main * .72f, 2.1f);
            DrawLine(drawInfo, pixel, middle, tip, accent * .60f, 1.2f);
        }
    }

    private static void DrawAbyssSpiral(PlayerDrawSet drawInfo, Texture2D pixel, Vector2 center, Color red, Color blue, float alpha, float time)
    {
        for (int strand = 0; strand < 2; strand++)
        {
            Vector2 previous = center;
            for (int step = 1; step <= 12; step++)
            {
                float t = step / 12f;
                float angle = time * (strand == 0 ? 1.4f : -1.2f) + t * MathHelper.TwoPi * 1.15f + strand * MathHelper.Pi;
                float radius = 2f + 15f * t;
                Vector2 point = center + new Vector2((float)Math.Cos(angle) * radius, (float)Math.Sin(angle) * radius * .56f);
                if (step > 1) DrawLine(drawInfo, pixel, previous, point, (strand == 0 ? red : blue) * (alpha * (.74f - t * .25f)), 1.6f - t * .5f);
                previous = point;
            }
        }
        for (int i = 0; i < 4; i++)
        {
            float angle = time * .7f + i * MathHelper.PiOver2;
            Vector2 p = center + new Vector2((float)Math.Cos(angle) * 20f, (float)Math.Sin(angle) * 9f);
            DrawDiamond(drawInfo, pixel, p, 2.2f, 5f, i % 2 == 0 ? red : blue, alpha, angle);
        }
        DrawDiamond(drawInfo, pixel, center, 4f, 6f, red, alpha, time * -.2f);
    }

    private static void DrawFourRayEcho(PlayerDrawSet drawInfo, Texture2D pixel, Vector2 center, Color red, Color violet, float alpha, float time)
    {
        float contraction = 1f - .18f * (float)Math.Sin(time * 8f);
        DrawEllipticTrack(drawInfo, pixel, center, 12f * contraction, 6f * contraction, violet, alpha * .64f, time * .7f);
        for (int i = 0; i < 4; i++)
        {
            float angle = time * .35f + i * MathHelper.PiOver2;
            Vector2 axis = new((float)Math.Cos(angle), (float)Math.Sin(angle));
            Vector2 normal = new(-axis.Y, axis.X);
            float length = 13f + (float)Math.Sin(time * 5f + i) * 2f;
            Vector2 tip = center + axis * length;
            DrawLine(drawInfo, pixel, center - axis * 2f, tip, (i % 2 == 0 ? red : violet) * alpha, 1.8f);
            DrawLine(drawInfo, pixel, tip, tip - axis * 4f + normal * 2.5f, red * alpha, 1.1f);
            DrawLine(drawInfo, pixel, tip, tip - axis * 4f - normal * 2.5f, red * alpha, 1.1f);
            DrawGlow(drawInfo, tip, i % 2 == 0 ? red : violet, .42f, alpha * .22f);
        }
        DrawRadialBurst(drawInfo, pixel, center, red, alpha * .52f, 8, time * 1.4f);
    }

    private static void DrawRadialBurst(PlayerDrawSet drawInfo, Texture2D pixel, Vector2 center, Color color, float alpha, int rays, float phase)
    {
        for (int i = 0; i < rays; i++)
        {
            float angle = phase + MathHelper.TwoPi * i / rays;
            Vector2 direction = new((float)Math.Cos(angle), (float)Math.Sin(angle));
            float inner = 5f + (i % 2) * 2f;
            float outer = 12f + (i % 3) * 3f;
            DrawLine(drawInfo, pixel, center + direction * inner, center + direction * outer, color * alpha, i % 2 == 0 ? 1.6f : 1f);
        }
    }

    private static void DrawDiamond(PlayerDrawSet drawInfo, Texture2D pixel, Vector2 center, float width, float height, Color color, float alpha, float rotation)
    {
        Vector2 axis = new((float)Math.Cos(rotation), (float)Math.Sin(rotation));
        Vector2 normal = new(-axis.Y, axis.X);
        Vector2 top = center - normal * height;
        Vector2 right = center + axis * width;
        Vector2 bottom = center + normal * height;
        Vector2 left = center - axis * width;
        DrawLine(drawInfo, pixel, top, right, color * alpha, 1.7f);
        DrawLine(drawInfo, pixel, right, bottom, color * alpha, 1.7f);
        DrawLine(drawInfo, pixel, bottom, left, color * alpha, 1.7f);
        DrawLine(drawInfo, pixel, left, top, color * alpha, 1.7f);
        DrawLine(drawInfo, pixel, top, bottom, Color.White * (alpha * .44f), .7f);
    }

    internal static void DrawRing(PlayerDrawSet drawInfo, Texture2D pixel, Vector2 center, float radius, Color color, float alpha, int segments, float phase)
    {
        if (drawInfo.shadow != 0f) return;
        for (int i = 0; i < segments; i++)
        {
            float a = phase + MathHelper.TwoPi * i / segments;
            float b = phase + MathHelper.TwoPi * (i + 1) / segments;
            DrawLine(drawInfo, pixel, center + new Vector2((float)Math.Cos(a), (float)Math.Sin(a) * .55f) * radius,
                center + new Vector2((float)Math.Cos(b), (float)Math.Sin(b) * .55f) * radius, color * alpha, 1.3f);
        }
    }

    private static void DrawFlatRing(PlayerDrawSet drawInfo, Texture2D pixel, Vector2 center, float radius, Color color, float alpha, int segments, float phase)
    {
        for (int i = 0; i < segments; i++)
        {
            float a = phase + MathHelper.TwoPi * i / segments;
            float b = phase + MathHelper.TwoPi * (i + 1) / segments;
            DrawLine(drawInfo, pixel, center + new Vector2((float)Math.Cos(a), (float)Math.Sin(a) * .20f) * radius,
                center + new Vector2((float)Math.Cos(b), (float)Math.Sin(b) * .20f) * radius, color * alpha, 1.5f);
        }
    }

    private static void DrawArc(PlayerDrawSet drawInfo, Texture2D pixel, Vector2 center, float radius, Color color, float alpha, float start, float sweep)
    {
        const int pieces = 9;
        for (int i = 0; i < pieces; i++)
        {
            float a = start + sweep * i / pieces;
            float b = start + sweep * (i + 1) / pieces;
            DrawLine(drawInfo, pixel, center + new Vector2((float)Math.Cos(a), (float)Math.Sin(a) * .58f) * radius,
                center + new Vector2((float)Math.Cos(b), (float)Math.Sin(b) * .58f) * radius, color * alpha, 2f);
        }
    }

    private static void DrawBurst(PlayerDrawSet drawInfo, Texture2D pixel, Vector2 center, Color color, float alpha, int rays, float phase)
    {
        for (int i = 0; i < rays; i++)
        {
            float angle = phase + MathHelper.TwoPi * i / rays;
            Vector2 direction = new((float)Math.Cos(angle), (float)Math.Sin(angle));
            DrawLine(drawInfo, pixel, center + direction * 7f, center + direction * 17f, color * alpha, 1.7f);
            DrawLine(drawInfo, pixel, center + direction * 17f, center + direction * 20f, Color.White * (alpha * .72f), .9f);
        }
    }

    private static void DrawFeatherWings(PlayerDrawSet drawInfo, Texture2D pixel, Vector2 center,
        Color gold, Color cyan, float pulse, float time)
    {
        float spread = 7f + pulse * 12f + (float)Math.Sin(time * 9f) * 1.5f;
        for (int side = -1; side <= 1; side += 2)
        for (int feather = 0; feather < 4; feather++)
        {
            Vector2 root = center + new Vector2(side * (2f + feather * .5f), feather * 1.4f);
            Vector2 tip = root + new Vector2(side * (spread * (.62f + feather * .14f)), -11f + feather * 3.3f);
            Color plume = feather % 2 == 0 ? gold : cyan;
            DrawLine(drawInfo, pixel, root, tip, plume * (.64f * pulse), 2f - feather * .12f);
            Vector2 axis = tip - root;
            Vector2 normal = axis.SafeNormalize(Vector2.UnitX).RotatedBy(MathHelper.PiOver2) * side;
            for (int barb = 1; barb <= 2; barb++)
            {
                Vector2 at = Vector2.Lerp(root, tip, barb / 3f);
                DrawLine(drawInfo, pixel, at, at + normal * (2.5f + feather * .35f), cyan * (.58f * pulse), 1f);
            }
        }
    }

    internal static void DrawLine(PlayerDrawSet drawInfo, Texture2D pixel, Vector2 a, Vector2 b, Color color, float width)
    {
        Vector2 anchor = GetSuitVfxAnchor(drawInfo);
        a = anchor + (a - anchor) * PerimeterExtent;
        b = anchor + (b - anchor) * PerimeterExtent;
        Vector2 delta = b - a;
        float length = delta.Length();
        if (length < .1f) return;
        if (DiagnosticCapture)
        {
            DiagnosticLineCount++;
            DiagnosticMinX = Math.Min(DiagnosticMinX, Math.Min(a.X, b.X));
            DiagnosticMinY = Math.Min(DiagnosticMinY, Math.Min(a.Y, b.Y));
            DiagnosticMaxX = Math.Max(DiagnosticMaxX, Math.Max(a.X, b.X));
            DiagnosticMaxY = Math.Max(DiagnosticMaxY, Math.Max(a.Y, b.Y));
        }
        drawInfo.DrawDataCache.Add(new DrawData(pixel, (a + b) * .5f, new Rectangle(0, 0, 1, 1), color,
            delta.ToRotation(), new Vector2(.5f), new Vector2(length, width), SpriteEffects.None, 0));
    }

    private static void DrawOrbitNodes(PlayerDrawSet drawInfo, Texture2D pixel, Vector2 center, Color color, int nodes, float phase, float alpha)
    {
        for (int i = 0; i < nodes; i++)
        {
            float a = phase + MathHelper.TwoPi * i / Math.Max(1, nodes);
            Vector2 p = center + new Vector2((float)Math.Cos(a) * 13f, (float)Math.Sin(a) * 5f);
            DrawLine(drawInfo, pixel, p + new Vector2(-2, 0), p + new Vector2(2, 0), color * alpha, 2f);
            DrawLine(drawInfo, pixel, p + new Vector2(0, -2), p + new Vector2(0, 2), Color.White * (alpha * .65f), 1.4f);
        }
    }

    private static void DrawSigil(PlayerDrawSet drawInfo, Texture2D pixel, Vector2 center, Color color, float alpha, float phase)
    {
        DrawRing(drawInfo, pixel, center, 8f, color, alpha * .75f, 4, phase);
        for (int i = 0; i < 4; i++)
        {
            float a = phase + MathHelper.PiOver2 * i;
            Vector2 p = center + new Vector2((float)Math.Cos(a), (float)Math.Sin(a)) * 12f;
            DrawLine(drawInfo, pixel, p, p - new Vector2((float)Math.Cos(a), (float)Math.Sin(a)) * 4f, color * alpha, 1.8f);
        }
    }

    private static void DrawSideFlames(PlayerDrawSet drawInfo, Texture2D pixel, Vector2 center, float time, Color color)
    {
        for (int side = -1; side <= 1; side += 2)
        for (int i = 0; i < 2; i++)
        {
            float phase = time * 3f + i * 1.9f + side;
            Vector2 start = center + new Vector2(side * 12f, 4f - i * 5f);
            Vector2 end = start + new Vector2(side * (3f + (float)Math.Sin(phase) * 2f), -7f - i * 2f);
            DrawLine(drawInfo, pixel, start, end, (i == 0 ? color : new Color(196, 78, 70)) * .42f, 1.7f);
        }
    }
}

/// <summary>Palette, tooltip tint, and the single existing-target marker used by passive visuals.</summary>
public static class MilkcraftPassiveVfx
{
    private static readonly string[] Suits = { "Iron", "Leather", "Cloth", "Knight", "Hunting", "Energy", "Templar", "Nightmare", "Fallen", "Soulforged", "Abyss", "Dark" };
    public static Color SuitColor(string suit) => suit switch
    {
        "Iron" => new Color(96, 207, 232), "Leather" => new Color(81, 217, 120), "Cloth" => new Color(238, 149, 43),
        "Knight" => new Color(245, 183, 47), "Hunting" => new Color(39, 164, 255), "Energy" => new Color(172, 82, 234),
        "Templar" => new Color(253, 201, 59), "Nightmare" => new Color(230, 60, 90), "Fallen" => new Color(237, 186, 61),
        "Soulforged" => new Color(242, 56, 50), "Abyss" => new Color(217, 37, 66), "Dark" => new Color(218, 36, 64), _ => Color.White
    };

    public static Color SuitAccentColor(string suit) => suit switch
    {
        "Iron" => new Color(232, 182, 75), "Leather" => new Color(232, 170, 72), "Cloth" => new Color(201, 127, 53),
        "Knight" => new Color(233, 56, 56), "Hunting" => new Color(96, 219, 238), "Energy" => new Color(216, 139, 255),
        "Templar" => new Color(51, 203, 232), "Nightmare" => new Color(246, 200, 88), "Fallen" => new Color(81, 205, 232),
        "Soulforged" => new Color(227, 175, 54), "Abyss" => new Color(66, 70, 181), "Dark" => new Color(103, 64, 183), _ => Color.White
    };

    public static void TintSetBonus(System.Collections.Generic.List<TooltipLine> lines, string suit)
    {
        if (Main.dedServ || Main.myPlayer < 0 || Main.myPlayer >= Main.maxPlayers) return;
        bool active = Main.LocalPlayer.active && !Main.LocalPlayer.dead && MilkcraftPassivePlayer.ReadCompleteSuit(Main.LocalPlayer) == suit;
        Color activeColor = SuitColor(suit);
        Color inactiveColor = new Color(145, 149, 158);
        foreach (TooltipLine line in lines)
            if (line.Name == "SetBonus") line.OverrideColor = active ? activeColor : inactiveColor;
    }

    public static void TintArmorTooltip(System.Collections.Generic.List<TooltipLine> lines, Mod mod, string suit)
    {
        bool active = !Main.dedServ && Main.LocalPlayer.active && !Main.LocalPlayer.dead && MilkcraftPassivePlayer.ReadCompleteSuit(Main.LocalPlayer) == suit;
        Color stateColor = active ? SuitColor(suit) : new Color(145, 149, 158);
        foreach (TooltipLine line in lines)
            if (line.Name == "SetBonus") line.OverrideColor = stateColor;
        if (!lines.Exists(line => line.Name == "PassiveDescription") && !lines.Exists(line => line.Name == "SetBonus"))
            lines.Add(new TooltipLine(mod, "PassiveDescription", Language.GetTextValue($"Mods.{mod.Name}.SetBonuses.{suit}"))
            { OverrideColor = active ? SuitColor(suit) : new Color(145, 149, 158) });
        if (!lines.Exists(line => line.Name == "PassiveState"))
            lines.Add(new TooltipLine(mod, "PassiveState", Language.GetTextValue(active
                ? $"Mods.{mod.Name}.PassiveVfxUi.ArmorActive"
                : $"Mods.{mod.Name}.PassiveVfxUi.ArmorInactive"))
            { OverrideColor = stateColor });
        if (!lines.Exists(line => line.Name == "PassivePairState"))
        {
            MilkcraftPassivePlayer state = !Main.dedServ ? Main.LocalPlayer.GetModPlayer<MilkcraftPassivePlayer>() : null;
            bool paired = active && state != null && state.Suit == suit && state.IsPaired;
            lines.Add(new TooltipLine(mod, "PassivePairState", Language.GetTextValue(paired
                ? $"Mods.{mod.Name}.PassiveVfxUi.PairActive"
                : $"Mods.{mod.Name}.PassiveVfxUi.PairInactive"))
            { OverrideColor = paired ? new Color(113, 222, 142) : new Color(145, 149, 158) });
        }
    }

    public static void TintAccessoryTooltip(System.Collections.Generic.List<TooltipLine> lines, Mod mod, string itemName)
    {
        string suit = "";
        foreach (string candidate in Suits)
            if (MilkcraftPassivePlayer.AccessoryForSuit(candidate) == itemName) { suit = candidate; break; }
        if (suit.Length == 0 || Main.dedServ || Main.myPlayer < 0 || Main.myPlayer >= Main.maxPlayers) return;

        MilkcraftPassivePlayer state = Main.LocalPlayer.GetModPlayer<MilkcraftPassivePlayer>();
        bool functional = Main.LocalPlayer.active && !Main.LocalPlayer.dead && state.HasFunctionalAccessory(itemName);
        bool paired = functional && state.Suit == suit;
        Color baseColor = functional ? new Color(232, 232, 220) : new Color(145, 149, 158);
        foreach (TooltipLine line in lines)
            if (line.Name.StartsWith("Tooltip", StringComparison.Ordinal)) line.OverrideColor = baseColor;
        if (!lines.Exists(line => line.Name == "PassiveFunctional"))
            lines.Add(new TooltipLine(mod, "PassiveFunctional", Language.GetTextValue($"Mods.{mod.Name}.PassiveVfxUi.Accessory{(functional ? "Active" : "Inactive")}"))
            { OverrideColor = baseColor });
        if (!lines.Exists(line => line.Name == "PassivePair"))
            lines.Add(new TooltipLine(mod, "PassivePair", Language.GetTextValue($"Mods.{mod.Name}.PassiveVfxUi.Pair{(paired ? "Active" : "Inactive")}"))
            { OverrideColor = paired ? new Color(113, 222, 142) : new Color(145, 149, 158) });
    }

    public static int StatusBuffType(string suit) => suit switch
    {
        "Iron" => ModContent.BuffType<PassiveStatusIron>(), "Leather" => ModContent.BuffType<PassiveStatusLeather>(),
        "Cloth" => ModContent.BuffType<PassiveStatusCloth>(), "Knight" => ModContent.BuffType<PassiveStatusKnight>(),
        "Hunting" => ModContent.BuffType<PassiveStatusHunting>(), "Energy" => ModContent.BuffType<PassiveStatusEnergy>(),
        "Templar" => ModContent.BuffType<PassiveStatusTemplar>(), "Nightmare" => ModContent.BuffType<PassiveStatusNightmare>(),
        "Fallen" => ModContent.BuffType<PassiveStatusFallen>(), "Soulforged" => ModContent.BuffType<PassiveStatusSoulforged>(),
        "Abyss" => ModContent.BuffType<PassiveStatusAbyss>(), "Dark" => ModContent.BuffType<PassiveStatusDark>(), _ => 0
    };

    public static void UpdateStatusBuff(Player player, string suit)
    {
        int desired = player.dead ? 0 : StatusBuffType(suit);
        bool alreadyPresent = false;
        for (int i = 0; i < player.buffType.Length; i++)
        {
            int existing = player.buffType[i];
            if (existing == desired && desired != 0) { alreadyPresent = true; continue; }
            if (!IsStatusBuff(existing)) continue;
            player.DelBuff(i);
            i--;
        }
        if (desired > 0)
        {
            if (alreadyPresent) player.AddBuff(desired, 2);
            else
            {
                bool hasFreeSlot = false;
                for (int i = 0; i < player.buffType.Length; i++)
                    if (player.buffType[i] == 0) { hasFreeSlot = true; break; }
                if (hasFreeSlot) player.AddBuff(desired, 2);
            }
        }
    }

    private static bool IsStatusBuff(int type)
    {
        foreach (string suit in Suits)
            if (type == StatusBuffType(suit)) return true;
        return false;
    }
}

public abstract class MilkcraftPassiveStatusBuff : ModBuff
{
    protected abstract string Suit { get; }
    protected abstract int IconBuff { get; }
    public override string Texture => $"Terraria/Images/Buff_{IconBuff}";
    public override void SetStaticDefaults()
    {
        Main.buffNoTimeDisplay[Type] = true;
        Main.buffNoSave[Type] = true;
        Main.debuff[Type] = false;
    }
    public override void Update(Player player, ref int buffIndex)
    {
        if (player.dead)
        {
            player.DelBuff(buffIndex);
            buffIndex--;
        }
    }
    public override void ModifyBuffText(ref string buffName, ref string tip, ref int rare)
    {
        buffName = Language.GetTextValue($"Mods.{Mod.Name}.PassiveBuffs.{Suit}");
        Player player = Main.LocalPlayer;
        tip = player.active && !player.dead && player.GetModPlayer<MilkcraftPassivePlayer>().Suit == Suit
            ? player.setBonus
            : Language.GetTextValue($"Mods.{Mod.Name}.PassiveVfxUi.ArmorInactive");
    }
}

public sealed class PassiveStatusIron : MilkcraftPassiveStatusBuff { protected override string Suit => "Iron"; protected override int IconBuff => BuffID.Ironskin; }
public sealed class PassiveStatusLeather : MilkcraftPassiveStatusBuff { protected override string Suit => "Leather"; protected override int IconBuff => BuffID.Swiftness; }
public sealed class PassiveStatusCloth : MilkcraftPassiveStatusBuff { protected override string Suit => "Cloth"; protected override int IconBuff => BuffID.ManaRegeneration; }
public sealed class PassiveStatusKnight : MilkcraftPassiveStatusBuff { protected override string Suit => "Knight"; protected override int IconBuff => BuffID.Endurance; }
public sealed class PassiveStatusHunting : MilkcraftPassiveStatusBuff { protected override string Suit => "Hunting"; protected override int IconBuff => BuffID.Hunter; }
public sealed class PassiveStatusEnergy : MilkcraftPassiveStatusBuff { protected override string Suit => "Energy"; protected override int IconBuff => BuffID.MagicPower; }
public sealed class PassiveStatusTemplar : MilkcraftPassiveStatusBuff { protected override string Suit => "Templar"; protected override int IconBuff => BuffID.Shine; }
public sealed class PassiveStatusNightmare : MilkcraftPassiveStatusBuff { protected override string Suit => "Nightmare"; protected override int IconBuff => BuffID.Wrath; }
public sealed class PassiveStatusFallen : MilkcraftPassiveStatusBuff { protected override string Suit => "Fallen"; protected override int IconBuff => BuffID.Regeneration; }
public sealed class PassiveStatusSoulforged : MilkcraftPassiveStatusBuff { protected override string Suit => "Soulforged"; protected override int IconBuff => BuffID.Rage; }
public sealed class PassiveStatusAbyss : MilkcraftPassiveStatusBuff { protected override string Suit => "Abyss"; protected override int IconBuff => BuffID.ShadowDodge; }
public sealed class PassiveStatusDark : MilkcraftPassiveStatusBuff { protected override string Suit => "Dark"; protected override int IconBuff => BuffID.Inferno; }

/// <summary>Marks are drawn only while the actual hunt stack or homing proc still targets this NPC.</summary>
public sealed class MilkcraftPassiveTargetVfx : GlobalNPC
{
    public override bool PreDraw(NPC npc, SpriteBatch spriteBatch, Vector2 screenPos, Color drawColor)
    {
        if (Main.dedServ || !npc.active || npc.friendly || npc.dontTakeDamage || npc.immortal) return true;
        Player player = Main.LocalPlayer;
        if (!player.active || player.dead) return true;
        MilkcraftPassivePlayer state = player.GetModPlayer<MilkcraftPassivePlayer>();
        string suit = state.Suit;
        bool hunting = suit == "Hunting" && state.VfxHuntTarget == (npc.realLife >= 0 ? npc.realLife : npc.whoAmI);
        bool projectileTarget = false;
        int variant = 0;
        if (suit is "Nightmare" or "Abyss" or "Dark")
        {
            for (int i = 0; i < Main.maxProjectiles; i++)
            {
                Projectile projectile = Main.projectile[i];
                if (!projectile.active || projectile.owner != Main.myPlayer || projectile.ModProjectile is not SuitPassiveProjectile || (int)projectile.ai[0] != npc.whoAmI) continue;
                variant = (int)projectile.ai[1];
                if ((suit == "Nightmare" && variant == 1) || (suit == "Abyss" && variant == 2) || (suit == "Dark" && variant == 3))
                { projectileTarget = true; break; }
            }
        }
        if (!hunting && !projectileTarget) return true;

        float intensity = hunting ? (state.VfxHuntProcFlash > 0 ? 1f : .55f + Math.Min(3, state.VfxHuntHits) * .10f) : .72f;
        Color color = hunting ? new Color(113, 205, 255) : MilkcraftPassiveVfx.SuitColor(suit);
        Vector2 center = npc.Center - screenPos + new Vector2(0, -npc.height * .46f);
        Texture2D pixel = TextureAssets.MagicPixel.Value;
        for (int i = 0; i < 4; i++)
        {
            float angle = MathHelper.PiOver4 + MathHelper.PiOver2 * i;
            Vector2 direction = new((float)Math.Cos(angle), (float)Math.Sin(angle));
            Vector2 start = center + direction * 5f;
            Vector2 end = center + direction * (hunting && state.VfxHuntProcFlash > 0 ? 15f : 11f);
            DrawNpcLine(spriteBatch, pixel, start, end, color * intensity, hunting && state.VfxHuntProcFlash > 0 ? 2.2f : 1.5f);
        }
        return true;
    }

    private static void DrawNpcLine(SpriteBatch spriteBatch, Texture2D pixel, Vector2 a, Vector2 b, Color color, float width)
    {
        Vector2 delta = b - a;
        spriteBatch.Draw(pixel, (a + b) * .5f, new Rectangle(0, 0, 1, 1), color, delta.ToRotation(), new Vector2(.5f),
            new Vector2(delta.Length(), width), SpriteEffects.None, 0f);
    }
}
