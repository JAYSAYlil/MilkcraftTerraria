using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;
using MilkcraftTerraria.Content.Items;

namespace MilkcraftTerraria.Content;

/// <summary>Opt-in inventory renderer capture for comparing the 12 head/body/legs icons.</summary>
internal static class InventoryIconDiagnostics
{
    private const int IconScale = 8;
    private const float NativeIconMaxSize = 32f;
    private const int CellWidth = 400;
    private const int CellHeight = 420;
    private const int HeaderHeight = 76;

    private sealed record SuitIcons(string Name, string Head, int HeadType, string Body, int BodyType, string Legs, int LegsType);

    private static SuitIcons[] Suits => new[]
    {
        new SuitIcons("铁甲", "头盔", ModContent.ItemType<IronHelmet>(), "胸甲", ModContent.ItemType<IronChestplate>(), "护腿", ModContent.ItemType<IronGreaves>()),
        new SuitIcons("皮革", "头带", ModContent.ItemType<LeatherHeadband>(), "皮衣", ModContent.ItemType<LeatherTunic>(), "护腿", ModContent.ItemType<LeatherLeggings>()),
        new SuitIcons("布甲", "兜帽", ModContent.ItemType<ClothHood>(), "法袍", ModContent.ItemType<ClothRobe>(), "护腿", ModContent.ItemType<ClothLeggings>()),
        new SuitIcons("骑士", "头盔", ModContent.ItemType<KnightHelmet>(), "胸甲", ModContent.ItemType<KnightChestplate>(), "护腿", ModContent.ItemType<KnightGreaves>()),
        new SuitIcons("狩猎", "兜帽", ModContent.ItemType<HuntingHood>(), "胸甲", ModContent.ItemType<HuntingChestplate>(), "护腿", ModContent.ItemType<HuntingLeggings>()),
        new SuitIcons("能量", "兜帽", ModContent.ItemType<EnergyHood>(), "法袍", ModContent.ItemType<EnergyRobe>(), "护腿", ModContent.ItemType<EnergyLeggings>()),
        new SuitIcons("圣殿", "头盔", ModContent.ItemType<TemplarHelmet>(), "胸甲", ModContent.ItemType<TemplarChestplate>(), "护腿", ModContent.ItemType<TemplarGreaves>()),
        new SuitIcons("梦魇", "兜帽", ModContent.ItemType<NightmareHood>(), "胸甲", ModContent.ItemType<NightmareChestplate>(), "护腿", ModContent.ItemType<NightmareLeggings>()),
        new SuitIcons("堕落", "兜帽", ModContent.ItemType<FallenHood>(), "法袍", ModContent.ItemType<FallenRobe>(), "护腿", ModContent.ItemType<FallenLeggings>()),
        new SuitIcons("魂铸", "头盔", ModContent.ItemType<SoulforgedHelmet>(), "胸甲", ModContent.ItemType<SoulforgedChestplate>(), "护腿", ModContent.ItemType<SoulforgedGreaves>()),
        new SuitIcons("深渊", "兜帽", ModContent.ItemType<AbyssHood>(), "胸甲", ModContent.ItemType<AbyssChestplate>(), "护腿", ModContent.ItemType<AbyssLeggings>()),
        new SuitIcons("黑暗", "兜帽", ModContent.ItemType<DarkHood>(), "法袍", ModContent.ItemType<DarkRobe>(), "护腿", ModContent.ItemType<DarkLeggings>())
    };

    public static string Render(string outputDirectory)
    {
        SuitIcons[] suits = Suits;
        int width = CellWidth * 3;
        int height = HeaderHeight + CellHeight * 6;
        GraphicsDevice device = Main.graphics.GraphicsDevice;
        RenderTargetBinding[] oldTargets = device.GetRenderTargets();
        Vector2 oldZoom = Main.GameViewMatrix.Zoom;
        Color background = new(20, 23, 31);
        List<string> audit = new() { $"nativePreDrawInventory=enabled nearest={IconScale}x normalizedMax={NativeIconMaxSize:0}px suites={suits.Length} items={suits.Length * 3}" };
        int rendered = 0;
        try
        {
            Main.GameViewMatrix.Zoom = Vector2.One;
            for (int page = 0; page < 2; page++)
            {
                using RenderTarget2D target = new(device, width, height, false, SurfaceFormat.Color,
                    DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
                device.SetRenderTarget(target);
                device.Clear(background);
                Main.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp,
                    DepthStencilState.None, RasterizerState.CullNone);

                DrawLabel($"装备物品栏图标原生渲染 · 第 {page + 1}/2 页 · 最近邻 {IconScale}×", new Vector2(18, 10), Color.White, width - 36);
                string[] columns = { "头盔 / 头部", "独立胸甲", "完整护腿（含鞋）" };
                for (int column = 0; column < 3; column++)
                    DrawLabel(columns[column], new Vector2(column * CellWidth + 18, 45), new Color(210, 215, 228), CellWidth - 36);

                for (int row = 0; row < 6; row++)
                {
                    SuitIcons suit = suits[page * 6 + row];
                    int y = HeaderHeight + row * CellHeight;
                    for (int column = 0; column < 3; column++)
                    {
                        (string part, int type) = column switch
                        {
                            0 => (suit.Head, suit.HeadType),
                            1 => (suit.Body, suit.BodyType),
                            _ => (suit.Legs, suit.LegsType)
                        };
                        Item item = new();
                        item.SetDefaults(type);
                        Texture2D texture = ModContent.Request<Texture2D>(item.ModItem.Texture,
                            ReLogic.Content.AssetRequestMode.ImmediateLoad).Value;
                        if (texture.Width <= 1 || texture.Height <= 1)
                            throw new InvalidOperationException($"Inventory icon texture was not loaded: {item.ModItem.Texture} ({texture.Width}x{texture.Height}).");
                        Rectangle source = ArmorInventoryIconComposer.GetVisibleBounds(item, texture);
                        Vector2 origin = new(source.Width * .5f, source.Height * .5f);
                        Vector2 center = new(column * CellWidth + CellWidth * .5f, y + 225);
                        string label = $"{suit.Name} · {part} · {item.Name}";
                        DrawLabel(label, new Vector2(column * CellWidth + 14, y + 8), Color.White, CellWidth - 28);

                        Color lightColor = Color.White;
                        Color itemColor = item.GetColor(lightColor);
                        Color drawColor = item.GetAlpha(lightColor);
                        float nativeFit = NativeIconMaxSize / Math.Max(texture.Width, texture.Height);
                        float drawScale = nativeFit * IconScale;
                        bool drawVanilla = ItemLoader.PreDrawInInventory(item, Main.spriteBatch, center, source,
                            drawColor, itemColor, origin, drawScale);
                        if (drawVanilla)
                            Main.spriteBatch.Draw(texture, center, source, drawColor, 0f, origin, drawScale,
                                SpriteEffects.None, 0f);
                        ItemLoader.PostDrawInInventory(item, Main.spriteBatch, center, source,
                            drawColor, itemColor, origin, drawScale);
                        float visibleScale = drawScale * Math.Max(texture.Width, texture.Height) /
                            Math.Max(source.Width, source.Height);
                        float displayWidth = source.Width * visibleScale;
                        float displayHeight = source.Height * visibleScale;
                        audit.Add($"{suit.Name}/{part}: item={item.Name}; type={item.type}; texture={texture.Width}x{texture.Height}; visibleSource=({source.X},{source.Y},{source.Width},{source.Height}); normalizedMax={NativeIconMaxSize:0} nativePx; displayed={displayWidth:0.#}x{displayHeight:0.#}; nativeDrawScale={drawScale:0.####}; preDraw={drawVanilla}");
                        rendered++;
                    }
                }

                Main.spriteBatch.End();
                device.SetRenderTargets(oldTargets);
                string filename = page == 0 ? "native-armor-icons-T1-T2-8x.png" : "native-armor-icons-T3-T4-8x.png";
                using FileStream stream = File.Create(Path.Combine(outputDirectory, filename));
                target.SaveAsPng(stream, width, height);
            }

            int worldDrops = RenderWorldDropIcons(outputDirectory, audit);

            if (rendered != 36)
                throw new InvalidOperationException($"Expected 36 armor icons, rendered {rendered}.");
            audit.Insert(1, $"rendered={rendered}/36 inventory icons; worldDropSamples={worldDrops}/8; sheets=native-armor-icons-T1-T2-8x.png,native-armor-icons-T3-T4-8x.png,native-armor-world-drops-8x.png");
            File.WriteAllLines(Path.Combine(outputDirectory, "native-armor-icons-8x.log"), audit);
            return $"inventory-icon-diagnostic=PASS nativePreDrawInventory={rendered}/36 nativePreDrawWorld={worldDrops}/8 nearest={IconScale}x sheets=3";
        }
        finally
        {
            try { Main.spriteBatch.End(); } catch { }
            device.SetRenderTargets(oldTargets);
            Main.GameViewMatrix.Zoom = oldZoom;
        }

        void DrawLabel(string text, Vector2 position, Color color, float maxWidth)
            => Terraria.UI.Chat.ChatManager.DrawColorCodedStringWithShadow(Main.spriteBatch, FontAssets.MouseText.Value,
                text, position, color, 0f, Vector2.Zero, Vector2.One, maxWidth, 1f);
    }

    private static int RenderWorldDropIcons(string outputDirectory, List<string> audit)
    {
        const int width = 640, height = 1120, cellWidth = 320, cellHeight = 280, renderScale = 8;
        (string Label, int Type)[] samples =
        {
            ("铁甲·掉落", ModContent.ItemType<IronChestplate>()),
            ("布袍·掉落", ModContent.ItemType<ClothRobe>()),
            ("狩猎·掉落", ModContent.ItemType<HuntingChestplate>()),
            ("黑暗·掉落", ModContent.ItemType<DarkRobe>()),
            ("布甲护腿·掉落", ModContent.ItemType<ClothLeggings>()),
            ("能量护腿·掉落", ModContent.ItemType<EnergyLeggings>()),
            ("堕落护腿·掉落", ModContent.ItemType<FallenLeggings>()),
            ("黑暗护腿·掉落", ModContent.ItemType<DarkLeggings>())
        };
        GraphicsDevice device = Main.graphics.GraphicsDevice;
        RenderTargetBinding[] oldTargets = device.GetRenderTargets();
        Vector2 oldScreen = Main.screenPosition;
        using RenderTarget2D target = new(device, width, height, false, SurfaceFormat.Color,
            DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
        try
        {
            Main.screenPosition = Vector2.Zero;
            device.SetRenderTarget(target);
            device.Clear(new Color(20, 23, 31));
            Main.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp,
                DepthStencilState.None, RasterizerState.CullNone);
            for (int i = 0; i < samples.Length; i++)
            {
                (string label, int type) = samples[i];
                Item item = new();
                item.SetDefaults(type);
                Texture2D texture = ModContent.Request<Texture2D>(item.ModItem.Texture,
                    ReLogic.Content.AssetRequestMode.ImmediateLoad).Value;
                Rectangle visible = ArmorInventoryIconComposer.GetVisibleBounds(item, texture);
                float nativeMax = 32f;
                float nativeHeight = visible.Height * nativeMax / Math.Max(visible.Width, visible.Height);
                float nativeWidth = visible.Width * nativeMax / Math.Max(visible.Width, visible.Height);
                int x = (i % 2) * cellWidth;
                int y = (i / 2) * cellHeight;
                Vector2 anchor = new(x + cellWidth * .5f, y + 225f);
                item.position = anchor - new Vector2(item.width * .5f, item.height);
                DrawLabel($"{label} · nativeMax={nativeMax:0}px", new Vector2(x + 12, y + 12), Color.White, cellWidth - 24);
                float rotation = 0f;
                float scale = renderScale;
                bool vanilla = ItemLoader.PreDrawInWorld(item, Main.spriteBatch, Color.White, Color.White,
                    ref rotation, ref scale, i);
                if (vanilla)
                    Main.spriteBatch.Draw(texture, anchor - new Vector2(0, visible.Height * renderScale * .5f), visible,
                        Color.White, rotation, new Vector2(visible.Width, visible.Height) * .5f,
                        renderScale * 32f / Math.Max(texture.Width, texture.Height), SpriteEffects.None, 0f);
                RequireWorldBounds(nativeWidth, nativeHeight, label);
                audit.Add($"worldDrop={label}; texture={texture.Width}x{texture.Height}; visibleSource=({visible.X},{visible.Y},{visible.Width},{visible.Height}); nativeVisible={nativeWidth:0.#}x{nativeHeight:0.#}; renderScale={renderScale}x; hookDrawsVanilla={vanilla}");
            }
            Main.spriteBatch.End();
            device.SetRenderTargets(oldTargets);
            using FileStream stream = File.Create(Path.Combine(outputDirectory, "native-armor-world-drops-8x.png"));
            target.SaveAsPng(stream, width, height);
            return samples.Length;
        }
        finally
        {
            try { Main.spriteBatch.End(); } catch { }
            device.SetRenderTargets(oldTargets);
            Main.screenPosition = oldScreen;
        }

        static void RequireWorldBounds(float visibleWidth, float visibleHeight, string label)
        {
            if (visibleWidth > 32.01f || visibleHeight > 32.01f || visibleWidth <= 0f || visibleHeight <= 0f)
                throw new InvalidOperationException($"world icon normalized dimensions outside 32px bound: {label} {visibleWidth:0.#}x{visibleHeight:0.#}");
        }

        void DrawLabel(string text, Vector2 position, Color color, float maxWidth)
            => Terraria.UI.Chat.ChatManager.DrawColorCodedStringWithShadow(Main.spriteBatch, FontAssets.MouseText.Value,
                text, position, color, 0f, Vector2.Zero, Vector2.One, maxWidth, 1f);
    }
}
