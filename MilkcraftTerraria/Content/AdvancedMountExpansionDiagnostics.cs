using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoMod.RuntimeDetour;
using Terraria;
using Terraria.DataStructures;
using Terraria.GameContent.ItemDropRules;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.Utilities;
using MilkcraftTerraria.Content.Items;

namespace MilkcraftTerraria.Content;

/// <summary>Small native PlayerRenderer pass for the five new mounts; only runs in the isolated smoke profile.</summary>
internal static class AdvancedMountExpansionDiagnostics
{
    private static bool _captureGeometry;
    private static bool _darkMountPreview;
    private static readonly List<string> GeometryRows = new();
    private delegate void OrigMountDraw(Mount self, List<DrawData> drawData, int drawType, Player drawPlayer,
        Vector2 position, Color drawColor, SpriteEffects spriteEffects, float shadow);
    private delegate void MountDrawDetour(OrigMountDraw orig, Mount self, List<DrawData> drawData, int drawType,
        Player drawPlayer, Vector2 position, Color drawColor, SpriteEffects spriteEffects, float shadow);

    private static void BrightMountDraw(OrigMountDraw orig, Mount self, List<DrawData> drawData, int drawType,
        Player drawPlayer, Vector2 position, Color drawColor, SpriteEffects spriteEffects, float shadow)
    {
        int start = drawData.Count;
        Color diagnosticColor = _darkMountPreview ? new Color(34, 38, 48, 255) : Color.White;
        orig(self, drawData, drawType, drawPlayer, position, diagnosticColor, spriteEffects, shadow);
        if (!_captureGeometry) return;
        Texture2D atlas = GetTexture(Mount.mounts[self.Type].backTexture);
        for (int i = start; i < drawData.Count; i++)
        {
            DrawData data = drawData[i];
            if (!ReferenceEquals(data.texture, atlas) || !data.sourceRect.HasValue) continue;
            Rectangle source = data.sourceRect.Value;
            if (source.Width <= 0 || source.Height <= 0) continue;
            var pixels = new Color[source.Width * source.Height];
            atlas.GetData(0, source, pixels, 0, pixels.Length);
            int opaqueBottom = -1;
            for (int y = source.Height - 1; y >= 0 && opaqueBottom < 0; y--)
                for (int x = 0; x < source.Width; x++)
                    if (pixels[y * source.Width + x].A > 0) { opaqueBottom = y; break; }
            float worldBottom = data.position.Y + Main.screenPosition.Y + (opaqueBottom - data.origin.Y) * data.scale.Y;
            float collisionBottom = drawPlayer.position.Y + drawPlayer.height;
            GeometryRows.Add($"drawType={drawType},frameY={source.Y},opaqueBottom={opaqueBottom},drawY={data.position.Y:0.0},originY={data.origin.Y:0.0},scaleY={data.scale.Y:0.00},visualBottom={worldBottom:0.0},collisionBottom={collisionBottom:0.0},delta={worldBottom - collisionBottom:0.0}");
        }
    }

    internal static string RenderStandingRiders(string outputDirectory)
    {
        var report = new StringBuilder();
        GraphicsDevice device = Main.graphics.GraphicsDevice;
        int playerIndex = Math.Clamp(Main.myPlayer, 0, Main.player.Length - 1);
        Player oldPlayer = Main.player[playerIndex];
        Vector2 oldScreen = Main.screenPosition;
        Vector2 oldZoom = Main.GameViewMatrix.Zoom;
        int oldScreenWidth = Main.screenWidth;
        int oldScreenHeight = Main.screenHeight;
        bool oldGameMenu = Main.gameMenu;
        bool oldLighting = SmokeSystem.ForcePreviewLighting;
        bool begun = false;
        const int width = 920, height = 1080;
        using var target = new RenderTarget2D(device, width, height, false, SurfaceFormat.Color,
            DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
        MethodInfo drawMethod = typeof(Mount).GetMethod("Draw",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null,
            new[] { typeof(List<DrawData>), typeof(int), typeof(Player), typeof(Vector2), typeof(Color), typeof(SpriteEffects), typeof(float) }, null)
            ?? throw new InvalidOperationException("Native Mount.Draw method was not found for the expansion mount preview.");
        using var hook = new Hook(drawMethod, (MountDrawDetour)BrightMountDraw);
        try
        {
            Main.gameMenu = false;
            Main.screenWidth = width;
            Main.screenHeight = height;
            Main.screenPosition = Vector2.Zero;
            Main.GameViewMatrix.Zoom = Vector2.One;
            SmokeSystem.ForcePreviewLighting = true;

            var mounts = new (string Name, int Type)[]
            {
                ("white-tiger", ModContent.MountType<WhiteTigerMount>()),
                ("white-nine-tailed-fox", ModContent.MountType<WhiteNineTailedFoxMount>()),
                ("rhino", ModContent.MountType<RhinoMount>()),
                ("flame-warhorse", ModContent.MountType<FlameWarhorseMount>()),
                ("white-unicorn", ModContent.MountType<WhiteUnicornMount>()),
                ("white-dodo", ModContent.MountType<WhiteDodoMount>())
            };

            device.SetRenderTarget(target);
            device.Clear(new Color(24, 28, 36));
            Main.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp,
                DepthStencilState.None, RasterizerState.CullCounterClockwise);
            begun = true;

            // Warm the native player renderer/shader projection before the real rows.
            Player warm = SmokeSystem.CreatePreviewPlayer(ModContent.ItemType<IronHelmet>(),
                ModContent.ItemType<IronChestplate>(), ModContent.ItemType<IronGreaves>());
            warm.whoAmI = playerIndex;
            warm.isDisplayDollOrInanimate = false;
            warm.mount.SetMount(mounts[0].Type, warm, true);
            SmokeSystem.DrawPreviewPlayer(warm, -600, -600, 1f);
            Main.spriteBatch.End();
            begun = false;
            device.Clear(new Color(24, 28, 36));
            Main.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp,
                DepthStencilState.None, RasterizerState.CullCounterClockwise);
            begun = true;

            for (int row = 0; row < mounts.Length; row++)
            {
                var entry = mounts[row];
                Mount.MountData data = Mount.mounts[entry.Type];
                string back = TextureSize(data.backTexture);
                string glow = TextureSize(data.backTextureGlow);
                if (back != "128x1536")
                    throw new InvalidOperationException($"{entry.Name} back atlas has unexpected dimensions {back}.");
                report.AppendLine($"{entry.Name}: atlas={back}, glow={glow}, frameBand=stand{data.standingFrameStart}/run{data.runningFrameStart}-{data.runningFrameStart + data.runningFrameCount - 1}/air{data.inAirFrameStart}-{data.inAirFrameStart + data.inAirFrameCount - 1}");

                for (int col = 0; col < 2; col++)
                {
                    int direction = col == 0 ? 1 : -1;
                    Player actor = SmokeSystem.CreatePreviewPlayer(ModContent.ItemType<IronHelmet>(),
                        ModContent.ItemType<IronChestplate>(), ModContent.ItemType<IronGreaves>());
                    actor.whoAmI = playerIndex;
                    actor.isDisplayDollOrInanimate = false;
                    actor.direction = direction;
                    actor.velocity = Vector2.Zero;
                    actor.mount.SetMount(entry.Type, actor, true);
                    for (int t = 0; t < 20; t++) actor.PlayerFrame();
                    int frame = ReadFrame(actor.mount);
                    if (frame != data.standingFrameStart)
                        throw new InvalidOperationException($"{entry.Name} did not settle on the standing frame: got {frame}, expected {data.standingFrameStart}.");
                    Main.player[playerIndex] = actor;
                    GeometryRows.Clear();
                    _captureGeometry = true;
                    try
                    {
                        SmokeSystem.DrawPreviewPlayer(actor, 145 + col * 430, 50 + row * 165, 1f);
                    }
                    finally { _captureGeometry = false; }
                    int groundLine = (int)(actor.position.Y + actor.height);
                    Main.spriteBatch.Draw(Terraria.GameContent.TextureAssets.MagicPixel.Value,
                        new Rectangle(100 + col * 430, groundLine, 370, 1), new Color(90, 215, 230, 220));
                    report.AppendLine($"draw:{entry.Name},direction={direction},frame={frame},actorBottom={actor.position.Y + actor.height:0.0},mount={actor.mount.Type}");
                    foreach (string geometry in GeometryRows)
                    {
                        if (geometry.Contains("delta=") && !GeometryDeltaWithinPixel(geometry))
                            throw new InvalidOperationException($"{entry.Name} opaque foot edge does not align with the native player collision bottom: {geometry}");
                        report.AppendLine($"geometry:{entry.Name},direction={direction},{geometry}");
                    }
                }
            }
            TestGroundAnimationRates(mounts, report, playerIndex);
            report.AppendLine(TestDashPreMovementHurt(playerIndex));
            report.AppendLine(TestNewMountSummonsAndWeapons(playerIndex));
            report.AppendLine(TestNewMountGroundAbilities(playerIndex));
            report.AppendLine(TestWhiteTigerChestGeneration());
            report.AppendLine(TestPlayKitMigration(playerIndex));
            report.AppendLine(TestAdvancedMountLootRegistration());
            report.AppendLine(ProgressionBossReleaseDiagnostics.Run());
            Main.spriteBatch.End();
            begun = false;
            device.SetRenderTarget(null);
            using FileStream stream = File.Create(Path.Combine(outputDirectory, "mount-expansion-standing-riders.png"));
            target.SaveAsPng(stream, width, height);
            report.AppendLine(RenderNativeActionPoses(outputDirectory, playerIndex, darkInput: false));
            report.AppendLine(RenderNativeActionPoses(outputDirectory, playerIndex, darkInput: true));
            report.AppendLine(TestWhiteUnicornSustainedHover(playerIndex));
            report.AppendLine(TestWhiteDodoLimitedFlight(playerIndex));
            report.AppendLine("native-standing-rider-render=PASS six mounts, left/right, 3-piece Iron armor, native PlayerRenderer, fixture-white mount lighting");
            return report.ToString();
        }
        finally
        {
            if (begun) Main.spriteBatch.End();
            device.SetRenderTarget(null);
            Main.player[playerIndex] = oldPlayer;
            Main.screenPosition = oldScreen;
            Main.GameViewMatrix.Zoom = oldZoom;
            Main.screenWidth = oldScreenWidth;
            Main.screenHeight = oldScreenHeight;
            Main.gameMenu = oldGameMenu;
            SmokeSystem.ForcePreviewLighting = oldLighting;
        }
    }

    private static string RenderNativeActionPoses(string outputDirectory, int playerIndex, bool darkInput)
    {
        GraphicsDevice device = Main.graphics.GraphicsDevice;
        const int width = 1280, height = 1150;
        Player oldPlayer = Main.player[playerIndex];
        Vector2 oldScreen = Main.screenPosition;
        Vector2 oldZoom = Main.GameViewMatrix.Zoom;
        int oldScreenWidth = Main.screenWidth, oldScreenHeight = Main.screenHeight;
        bool oldMenu = Main.gameMenu;
        bool oldForceLighting = SmokeSystem.ForcePreviewLighting;
        bool oldDarkMount = _darkMountPreview;
        bool begun = false;
        var mounts = new (string Name, int Type)[]
        {
            ("white-tiger", ModContent.MountType<WhiteTigerMount>()),
            ("white-nine-tailed-fox", ModContent.MountType<WhiteNineTailedFoxMount>()),
            ("rhino", ModContent.MountType<RhinoMount>()),
            ("flame-warhorse", ModContent.MountType<FlameWarhorseMount>()),
            ("white-unicorn", ModContent.MountType<WhiteUnicornMount>()),
            ("white-dodo", ModContent.MountType<WhiteDodoMount>())
        };
        ulong oldGameUpdateCount = Main.GameUpdateCount;
        var suits = new (string Name, int Head, int Body, int Legs)[]
        {
            ("iron", ModContent.ItemType<IronHelmet>(), ModContent.ItemType<IronChestplate>(), ModContent.ItemType<IronGreaves>()),
            ("templar", ModContent.ItemType<TemplarHelmet>(), ModContent.ItemType<TemplarChestplate>(), ModContent.ItemType<TemplarGreaves>()),
            ("cloth", ModContent.ItemType<ClothHood>(), ModContent.ItemType<ClothRobe>(), ModContent.ItemType<ClothLeggings>()),
            ("abyss", ModContent.ItemType<AbyssHood>(), ModContent.ItemType<AbyssChestplate>(), ModContent.ItemType<AbyssLeggings>())
        };
        var rows = new StringBuilder();
        using var target = new RenderTarget2D(device, width, height, false, SurfaceFormat.Color,
            DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
        try
        {
            Main.gameMenu = false;
            Main.screenWidth = width;
            Main.screenHeight = height;
            Main.screenPosition = Vector2.Zero;
            Main.GameViewMatrix.Zoom = Vector2.One;
            SmokeSystem.ForcePreviewLighting = true;
            _darkMountPreview = darkInput;
            device.SetRenderTarget(target);
            device.Clear(new Color(22, 26, 34));
            Main.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp,
                DepthStencilState.None, RasterizerState.CullCounterClockwise);
            begun = true;

            Player warm = SmokeSystem.CreatePreviewPlayer(suits[0].Head, suits[0].Body, suits[0].Legs);
            warm.whoAmI = playerIndex;
            warm.isDisplayDollOrInanimate = false;
            warm.mount.SetMount(mounts[0].Type, warm, true);
            SmokeSystem.DrawPreviewPlayer(warm, -700, -700, 1f);
            Main.spriteBatch.End();
            begun = false;
            device.Clear(new Color(22, 26, 34));
            Main.spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp,
                DepthStencilState.None, RasterizerState.CullCounterClockwise);
            begun = true;

            string[] poses = { "stand", "run", "jump", "fly", "glide" };
            foreach (var mountCase in mounts)
            {
                Mount.MountData data = Mount.mounts[mountCase.Type];
                for (int col = 0; col < 10; col++)
                {
                    int poseIndex = col / 2;
                    int direction = col % 2 == 0 ? 1 : -1;
                    bool unicorn = mountCase.Name == "white-unicorn";
                    bool limitedFlight = mountCase.Name == "white-dodo";
                    string pose = poses[poseIndex];
                    if (pose is "fly" or "glide" && !unicorn && !limitedFlight)
                        pose = mountCase.Name is "white-tiger" or "white-nine-tailed-fox" ? "dash" : "fast-run";
                    if (pose == "glide" && limitedFlight) pose = "fast-run";
                    var suit = suits[Array.IndexOf(mounts, mountCase) % suits.Length];
                    Player actor = SmokeSystem.CreatePreviewPlayer(suit.Head, suit.Body, suit.Legs,
                        walk: pose == "run" || pose == "fast-run", jump: pose is "jump" or "fly" or "glide");
                    actor.whoAmI = playerIndex;
                    actor.isDisplayDollOrInanimate = false;
                    actor.direction = direction;
                    actor.velocity = pose switch
                    {
                        "run" => new Vector2(direction * 5f, 0f),
                        "fast-run" => new Vector2(direction * Mount.mounts[mountCase.Type].dashSpeed, 0f),
                        "jump" => new Vector2(direction * 2f, -5f),
                        "fly" or "glide" => new Vector2(direction * 2f, -4f),
                        _ => Vector2.Zero
                    };
                    actor.oldVelocity = actor.velocity;
                    actor.mount.SetMount(mountCase.Type, actor, true);
                    if (pose is "fly" or "glide")
                    {
                        actor.mount.ResetFlightTime(0f);
                        actor.controlJump = true;
                        actor.controlUp = true;
                        actor.controlDown = pose == "glide";
                        actor.jump = 0;
                        actor.mount.UpdateEffects(actor);
                    }
                    if (pose == "dash")
                    {
                        actor.controlRight = direction > 0;
                        actor.controlLeft = direction < 0;
                        AdvancedMountAbilityPlayer ability = actor.GetModPlayer<AdvancedMountAbilityPlayer>();
                        ability.PreUpdateMovement();
                        actor.controlRight = actor.controlLeft = false;
                        ability.PreUpdateMovement();
                        actor.controlRight = direction > 0;
                        actor.controlLeft = direction < 0;
                        ability.PreUpdateMovement();
                        actor.controlRight = actor.controlLeft = false;
                    }
                    int ticks = pose switch { "run" => 48, "jump" => 1, "fly" or "glide" => 20, _ => 1 };
                    var poseFrames = new HashSet<int>();
                    for (int tick = 0; tick < ticks; tick++)
                    {
                        if (unicorn && pose is ("fly" or "glide")) SetDiagnosticGameUpdateCount(Main.GameUpdateCount + 1);
                        if (pose == "jump" && unicorn && tick == 0) actor.justJumped = true;
                        actor.PlayerFrame();
                        actor.justJumped = false;
                        poseFrames.Add(ReadFrame(actor.mount));
                    }
                    int frame = ReadFrame(actor.mount);
                    bool valid = pose switch
                    {
                        "stand" => frame == data.standingFrameStart,
                        "run" or "fast-run" => frame >= data.runningFrameStart && frame < data.runningFrameStart + data.runningFrameCount,
                        "jump" when data.usesHover => frame == 5,
                        "jump" => frame >= data.inAirFrameStart && frame < data.inAirFrameStart + data.inAirFrameCount,
                        "fly" => frame >= data.flyingFrameStart && frame < data.flyingFrameStart + data.flyingFrameCount,
                        "glide" when data.usesHover => frame >= data.swimFrameStart && frame < data.swimFrameStart + data.swimFrameCount,
                        "dash" => frame >= 10 && frame <= 15,
                        _ => false
                    };
                    if (!valid)
                        throw new InvalidOperationException($"{mountCase.Name}/{pose}/{direction} native frame {frame} did not match its pose band.");
                    if (pose == "fly" && poseFrames.Count < 3)
                        throw new InvalidOperationException($"WhiteUnicorn wing-flight animation did not advance through multiple native frames: {string.Join(",", poseFrames)}.");
                    if (pose == "glide" && (poseFrames.Any(f => f < data.swimFrameStart || f >= data.swimFrameStart + data.swimFrameCount)
                        || poseFrames.Count < 2))
                        throw new InvalidOperationException($"WhiteUnicorn glide did not animate within glide frames: {string.Join(",", poseFrames)}.");
                    Main.player[playerIndex] = actor;
                    int x = 64 + col * 120, y = 40 + Array.IndexOf(mounts, mountCase) * 178;
                    SmokeSystem.DrawPreviewPlayer(actor, x, y, 1f);
                    rows.AppendLine($"{mountCase.Name},{suit.Name},{pose},{direction},frame={frame},mount={actor.mount.Type}");
                }
            }
            Main.spriteBatch.End();
            begun = false;
            device.SetRenderTarget(null);
            string suffix = darkInput ? "dark-input" : "bright-input";
            using FileStream stream = File.Create(Path.Combine(outputDirectory, $"mount-expansion-action-poses-{suffix}.png"));
            target.SaveAsPng(stream, width, height);
            File.WriteAllText(Path.Combine(outputDirectory, $"mount-expansion-action-poses-{suffix}.csv"), rows.ToString());
            return $"native-action-pose-render-{suffix}=PASS actors={rows.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries).Length} poses=stand/run/jump/dash-or-flight/glide directions=left+right suits=Iron/Templar/Cloth/Abyss; Mount.DrawInput={(darkInput ? "RGB(34,38,48)" : "white")}; glowAtlases=loaded128x1536";
        }
        finally
        {
            if (begun) Main.spriteBatch.End();
            device.SetRenderTarget(null);
            Main.player[playerIndex] = oldPlayer;
            Main.screenPosition = oldScreen;
            Main.GameViewMatrix.Zoom = oldZoom;
            Main.screenWidth = oldScreenWidth;
            Main.screenHeight = oldScreenHeight;
            Main.gameMenu = oldMenu;
            SmokeSystem.ForcePreviewLighting = oldForceLighting;
            _darkMountPreview = oldDarkMount;
            SetDiagnosticGameUpdateCount(oldGameUpdateCount);
        }
    }

    private static void SetDiagnosticGameUpdateCount(ulong value)
    {
        PropertyInfo property = typeof(Main).GetProperty("GameUpdateCount",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new MissingMemberException(typeof(Main).FullName, "GameUpdateCount");
        MethodInfo setter = property.GetSetMethod(true);
        if (setter is not null)
        {
            setter.Invoke(null, new object[] { value });
            return;
        }

        FieldInfo backing = typeof(Main).GetField("<GameUpdateCount>k__BackingField",
            BindingFlags.Static | BindingFlags.NonPublic)
            ?? typeof(Main).GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                .FirstOrDefault(field => field.Name.Contains("GameUpdateCount", StringComparison.OrdinalIgnoreCase));
        if (backing is null)
            throw new MissingMemberException(typeof(Main).FullName, "GameUpdateCount backing storage");
        backing.SetValue(null, Convert.ChangeType(value, backing.FieldType));
    }

    private static string TestWhiteUnicornSustainedHover(int playerIndex)
    {
        Player actor = SmokeSystem.CreatePreviewPlayer(ModContent.ItemType<DarkHood>(),
            ModContent.ItemType<DarkRobe>(), ModContent.ItemType<DarkLeggings>());
        Player old = Main.player[playerIndex];
        Main.player[playerIndex] = actor;
        actor.whoAmI = playerIndex;
        int type = ModContent.MountType<WhiteUnicornMount>();
        try
        {
            actor.mount.SetMount(type, actor, true);
            actor.velocity = Vector2.Zero;
            actor.controlUp = actor.controlDown = actor.controlLeft = actor.controlRight = false;
            int minReserve = int.MaxValue;
            float maxNeutralY = 0f;
            for (int tick = 0; tick < 7200; tick++)
            {
                actor.mount.UpdateEffects(actor);
                actor.mount.UpdateFrame(actor, 2, actor.velocity);
                actor.mount.Hover(actor);
                actor.mount.Flight();
                if (!float.IsFinite(actor.velocity.X) || !float.IsFinite(actor.velocity.Y))
                    throw new InvalidOperationException($"WhiteUnicorn Hover generated non-finite velocity at tick {tick}: {actor.velocity}.");
                minReserve = Math.Min(minReserve, actor.mount.FlyTime);
                maxNeutralY = Math.Max(maxNeutralY, Math.Abs(actor.velocity.Y));
            }
            if (minReserve <= 0 || actor.mount.FlyTime <= 0 || maxNeutralY > 1.01f)
                throw new InvalidOperationException($"WhiteUnicorn sustained hover failed: minFlight={minReserve}, final={actor.mount.FlyTime}, neutralVy={maxNeutralY}.");
            var controlRows = new List<string>();
            foreach (var input in new (string Name, bool Up, bool Down, int Direction)[]
            {
                ("up", true, false, 1), ("down", false, true, 1),
                ("left", false, false, -1), ("right", false, false, 1)
            })
            {
                actor.controlUp = input.Up;
                actor.controlDown = input.Down;
                actor.controlLeft = input.Direction < 0;
                actor.controlRight = input.Direction > 0;
                actor.direction = input.Direction;
                actor.velocity = Vector2.Zero;
                float maxAbsY = 0f;
                for (int tick = 0; tick < 60; tick++)
                {
                    actor.mount.UpdateEffects(actor);
                    actor.mount.UpdateFrame(actor, 2, actor.velocity);
                    actor.mount.Hover(actor);
                    actor.mount.Flight();
                    if (!float.IsFinite(actor.velocity.X) || !float.IsFinite(actor.velocity.Y))
                        throw new InvalidOperationException($"WhiteUnicorn produced non-finite velocity under {input.Name}: {actor.velocity}.");
                    maxAbsY = Math.Max(maxAbsY, Math.Abs(actor.velocity.Y));
                }
                if (input.Up && actor.velocity.Y >= 0f)
                    throw new InvalidOperationException($"WhiteUnicorn did not ascend under Up: {actor.velocity}.");
                if (input.Down && actor.velocity.Y <= 0f)
                    throw new InvalidOperationException($"WhiteUnicorn did not descend under Down: {actor.velocity}.");
                if (actor.direction != input.Direction || actor.mount.FlyTime <= 0)
                    throw new InvalidOperationException($"WhiteUnicorn lost facing or reserve under {input.Name}: dir={actor.direction},reserve={actor.mount.FlyTime}.");
                controlRows.Add($"{input.Name}:v={actor.velocity},maxAbsVy={maxAbsY:0.00},reserve={actor.mount.FlyTime}");
            }
            actor.controlUp = actor.controlDown = actor.controlLeft = actor.controlRight = false;
            return $"white-unicorn-native-hover=PASS ticks=7200 noInputMaxAbsVy={maxNeutralY:0.00} minimumReserve={minReserve} finalReserve={actor.mount.FlyTime}; controls=[{string.Join(";", controlRows)}]";
        }
        finally
        {
            actor.mount.Dismount(actor);
            Main.player[playerIndex] = old;
        }
    }

    private static string TestWhiteDodoLimitedFlight(int playerIndex)
    {
        Player actor = SmokeSystem.CreatePreviewPlayer(ModContent.ItemType<IronHelmet>(),
            ModContent.ItemType<IronChestplate>(), ModContent.ItemType<IronGreaves>());
        Player old = Main.player[playerIndex];
        Main.player[playerIndex] = actor;
        actor.whoAmI = playerIndex;
        try
        {
            actor.mount.SetMount(ModContent.MountType<WhiteDodoMount>(), actor, true);
            actor.mount.ResetFlightTime(0f); // Start from the native grounded refill state.
            actor.controlJump = true;
            actor.jump = 0;
            actor.velocity = new Vector2(0f, -6f);
            int start = actor.mount.FlyTime;
            int ticks = 0;
            for (; ticks < start + 2 && actor.mount.FlyTime > 0; ticks++)
            {
                actor.mount.UpdateEffects(actor);
                actor.mount.Flight();
                if (!float.IsFinite(actor.velocity.X) || !float.IsFinite(actor.velocity.Y))
                    throw new InvalidOperationException($"WhiteDodo flight generated non-finite velocity at tick {ticks}: {actor.velocity}.");
            }
            if (start != 120 || ticks < 119 || actor.mount.FlyTime > 0)
                throw new InvalidOperationException($"WhiteDodo flight was not finite 120 ticks: start={start}, elapsed={ticks}, remaining={actor.mount.FlyTime}.");
            bool exhaustedBeforeReset = actor.mount.FlyTime == 0;
            actor.mount.ResetFlightTime(0f);
            if (actor.mount.FlyTime != 120)
                throw new InvalidOperationException($"WhiteDodo landing reset did not restore its 120-tick reserve: {actor.mount.FlyTime}.");
            return $"white-dodo-native-flight=PASS finiteReserve={start}, elapsed={ticks}, exhausted-before-ResetFlightTime={exhaustedBeforeReset}, reset={actor.mount.FlyTime} (native refill method; not full Player.Update landing fixture)";
        }
        finally
        {
            actor.mount.Dismount(actor);
            Main.player[playerIndex] = old;
        }
    }

    private static string TextureSize(object asset)
    {
        if (asset is null) return "none";
        object value = asset.GetType().GetProperty("Value")?.GetValue(asset);
        if (value is not Texture2D texture) return "missing";
        return $"{texture.Width}x{texture.Height}";
    }

    private static Texture2D GetTexture(object asset)
        => (Texture2D)(asset.GetType().GetProperty("Value")?.GetValue(asset)
            ?? throw new InvalidOperationException("Mount texture asset had no value."));

    private static int ReadFrame(Mount mount)
    {
        FieldInfo field = typeof(Mount).GetField("_frame", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Native Mount._frame was not found.");
        return (int)field.GetValue(mount)!;
    }

    private static void TestGroundAnimationRates((string Name, int Type)[] mounts, StringBuilder report, int playerIndex)
    {
        foreach (var entry in mounts)
        {
            Mount.MountData data = Mount.mounts[entry.Type];
            foreach (int direction in new[] { 1, -1 })
            {
                Player actor = SmokeSystem.CreatePreviewPlayer(ModContent.ItemType<IronHelmet>(),
                    ModContent.ItemType<IronChestplate>(), ModContent.ItemType<IronGreaves>());
                actor.whoAmI = playerIndex;
                actor.direction = direction;
                actor.mount.SetMount(entry.Type, actor, true);
                actor.mount.UpdateFrame(actor, 0, Vector2.Zero);
                int previous = ReadFrame(actor.mount);
                int transitions = 0;
                const int ticks = 600;
                for (int tick = 0; tick < ticks; tick++)
                {
                    actor.mount.UpdateFrame(actor, 1, new Vector2(direction * data.dashSpeed, 0f));
                    int current = ReadFrame(actor.mount);
                    if (current != previous) transitions++;
                    previous = current;
                }
                float frameRate = transitions * 60f / ticks;
                float targetMax = entry.Name == "white-unicorn" ? 8.5f : 12f;
                float targetMin = entry.Name == "white-unicorn" ? 7f : 8f;
                if (frameRate < targetMin || frameRate > targetMax)
                    throw new InvalidOperationException($"{entry.Name} ground animation rate out of target at dir={direction}: {frameRate:0.00} frame changes/s (target {targetMin:0.0}-{targetMax:0.0}).");
                report.AppendLine($"ground-animation:{entry.Name},direction={direction},dashSpeed={data.dashSpeed:0.0},delay={data.runningFrameDelay},frames={data.runningFrameCount},frameChangesPerSecond={frameRate:0.00}");
            }
        }
    }

    private static string TestDashPreMovementHurt(int playerIndex)
    {
        Player[] oldPlayers = Main.player;
        int oldMyPlayer = Main.myPlayer;
        int oldNetMode = Main.netMode;
        bool oldMenu = Main.gameMenu;
        bool oldPaused = Main.gamePaused;
        try
        {
            Main.gameMenu = false;
            Main.gamePaused = false;
            Main.myPlayer = playerIndex;
            Main.netMode = NetmodeID.SinglePlayer;
            Main.player = (Player[])oldPlayers.Clone();
            Player actor = oldPlayers[playerIndex].SerializedClone();
            MethodInfo setup = typeof(PlayerLoader).GetMethod("SetupPlayer", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("PlayerLoader.SetupPlayer was unavailable for dash fixture.");
            setup.Invoke(null, new object[] { actor });
            actor.active = true;
            actor.dead = false;
            actor.whoAmI = playerIndex;
            actor.statLife = actor.statLifeMax = 500;
            actor.statDefense = default;
            actor.immune = false;
            actor.immuneTime = 0;
            Array.Clear(actor.hurtCooldowns);
            actor.position = new Vector2(1200f, 900f);
            actor.velocity = Vector2.Zero;
            actor.controlLeft = actor.controlRight = false;
            actor.mount.SetMount(ModContent.MountType<WhiteTigerMount>(), actor, true);
            Main.player[playerIndex] = actor;

            // Exercise the actual tModLoader hook dispatcher at the documented
            // pre-position point: a second same-direction tap starts the dash.
            PlayerLoader.PreUpdateMovement(actor);
            actor.controlRight = true;
            PlayerLoader.PreUpdateMovement(actor);
            actor.controlRight = false;
            PlayerLoader.PreUpdateMovement(actor);
            actor.controlRight = true;
            PlayerLoader.PreUpdateMovement(actor);
            if (actor.velocity.X != 16f || actor.direction != 1 || !actor.immune || actor.immuneTime < 18)
                throw new InvalidOperationException($"Double-tap dash was not active before native position update: velocity={actor.velocity}, direction={actor.direction}, immune={actor.immune}, immuneTime={actor.immuneTime}.");
            int lifeBefore = actor.statLife;
            actor.Hurt(PlayerDeathReason.ByCustomReason(Terraria.Localization.NetworkText.FromLiteral("dash start immunity fixture")),
                20, 1, false, true);
            if (actor.statLife != lifeBefore)
                throw new InvalidOperationException($"Native Player.Hurt damaged a player on the dash-start frame: {lifeBefore}->{actor.statLife}.");

            actor.mount.Dismount(actor);
            Player longImmune = oldPlayers[playerIndex].SerializedClone();
            setup.Invoke(null, new object[] { longImmune });
            longImmune.active = true;
            longImmune.dead = false;
            longImmune.whoAmI = playerIndex;
            longImmune.immune = true;
            longImmune.immuneTime = 50;
            Array.Fill(longImmune.hurtCooldowns, 50);
            longImmune.mount.SetMount(ModContent.MountType<WhiteNineTailedFoxMount>(), longImmune, true);
            Main.player[playerIndex] = longImmune;
            longImmune.controlLeft = longImmune.controlRight = false;
            PlayerLoader.PreUpdateMovement(longImmune);
            longImmune.controlRight = true;
            PlayerLoader.PreUpdateMovement(longImmune);
            longImmune.controlRight = false;
            PlayerLoader.PreUpdateMovement(longImmune);
            longImmune.controlRight = true;
            PlayerLoader.PreUpdateMovement(longImmune);
            if (longImmune.immuneTime != 50 || longImmune.hurtCooldowns.Length == 0 || longImmune.hurtCooldowns[0] != 50)
                throw new InvalidOperationException($"Dash shortened longer native immunity: time={longImmune.immuneTime}, cooldown0={(longImmune.hurtCooldowns.Length == 0 ? -1 : longImmune.hurtCooldowns[0])}.");
            return $"double-tap-dash-pre-movement=PASS speed={actor.velocity.X:0.0} immunity=18 native-Hurt-life={lifeBefore}->{actor.statLife}; existing-long-immunity-preserved=50";
        }
        finally
        {
            Main.player = oldPlayers;
            Main.myPlayer = oldMyPlayer;
            Main.netMode = oldNetMode;
            Main.gameMenu = oldMenu;
            Main.gamePaused = oldPaused;
        }
    }

    private static string TestNewMountSummonsAndWeapons(int playerIndex)
    {
        Player[] oldPlayers = Main.player;
        Projectile[] oldProjectiles = Main.projectile;
        Item[] oldItems = Main.item;
        Dust[] oldDust = Main.dust;
        NPC[] oldNpcs = Main.npc;
        int oldMyPlayer = Main.myPlayer, oldNetMode = Main.netMode;
        int oldMouseX = Main.mouseX, oldMouseY = Main.mouseY;
        bool oldMenu = Main.gameMenu, oldPaused = Main.gamePaused, oldWorldGen = WorldGen.gen;
        try
        {
            Main.gameMenu = false;
            Main.gamePaused = false;
            WorldGen.gen = false;
            Main.myPlayer = playerIndex;
            Main.netMode = NetmodeID.SinglePlayer;
            Main.player = (Player[])oldPlayers.Clone();
            Main.projectile = Enumerable.Range(0, Math.Max(oldProjectiles.Length, Main.maxProjectiles + 1)).Select(_ => new Projectile()).ToArray();
            Main.item = Enumerable.Range(0, Math.Max(oldItems.Length, Main.maxItems + 1)).Select(_ => new Item()).ToArray();
            Main.dust = Enumerable.Range(0, Math.Max(oldDust.Length, Main.maxDust + 1)).Select(_ => new Dust()).ToArray();
            Main.npc = Enumerable.Range(0, Math.Max(oldNpcs.Length, Main.maxNPCs + 1)).Select(_ => new NPC()).ToArray();
            MethodInfo setup = typeof(PlayerLoader).GetMethod("SetupPlayer", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("PlayerLoader.SetupPlayer was unavailable for advanced mount fixture.");
            Player NewActor()
            {
                Player actor = oldPlayers[playerIndex].SerializedClone();
                setup.Invoke(null, new object[] { actor });
                actor.active = true;
                actor.dead = false;
                actor.whoAmI = playerIndex;
                actor.isDisplayDollOrInanimate = false;
                actor.position = new Vector2(300f, 300f);
                actor.direction = 1;
                actor.statLife = actor.statLifeMax = 200;
                actor.statMana = actor.statManaMax = 200;
                actor.immune = false;
                actor.immuneTime = 0;
                actor.noItems = false;
                actor.heldProj = -1;
                return actor;
            }

        var cases = new (string Name, int Mount, int Summon)[]
            {
                ("white-tiger", ModContent.MountType<WhiteTigerMount>(), ModContent.ItemType<WhiteTigerSummon>()),
                ("white-nine-tailed-fox", ModContent.MountType<WhiteNineTailedFoxMount>(), ModContent.ItemType<WhiteNineTailedFoxSummon>()),
                ("rhino", ModContent.MountType<RhinoMount>(), ModContent.ItemType<RhinoSummon>()),
                ("flame-warhorse", ModContent.MountType<FlameWarhorseMount>(), ModContent.ItemType<FlameWarhorseSummon>()),
                ("white-unicorn", ModContent.MountType<WhiteUnicornMount>(), ModContent.ItemType<WhiteUnicornSummon>()),
                ("white-dodo", ModContent.MountType<WhiteDodoMount>(), ModContent.ItemType<WhiteDodoSummon>())
            };

            foreach (var mountCase in cases)
            {
                Player actor = NewActor();
                Main.player[playerIndex] = actor;
                actor.inventory[0].SetDefaults(mountCase.Summon);
                actor.selectedItem = 0;
                actor.lastVisualizedSelectedItem = actor.inventory[0];
                actor.itemAnimation = actor.itemTime = 0;
                actor.controlUseItem = true;
                actor.releaseUseItem = true;
                Main.mouseX = (int)(actor.Center.X + 180f);
                Main.mouseY = (int)actor.Center.Y;
                for (int tick = 0; tick < 30 && !actor.mount.Active; tick++)
                {
                    actor.controlUseItem = true;
                    actor.ItemCheck();
                    actor.PlayerFrame();
                }
                if (!actor.mount.Active || actor.mount.Type != mountCase.Mount)
                    throw new InvalidOperationException($"Native ItemCheck did not summon {mountCase.Name}: active={actor.mount.Active},mount={actor.mount.Type},buff={actor.inventory[0].buffType},animation={actor.itemAnimation}.");
                actor.mount.Dismount(actor);
            }

            MethodInfo quickMount = typeof(Player).GetMethod("QuickMount", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("Native Player.QuickMount was unavailable for advanced mounts.");
            foreach (var mountCase in cases)
            {
                Player actor = NewActor();
                Main.player[playerIndex] = actor;
                actor.miscEquips[3].SetDefaults(mountCase.Summon);
                quickMount.Invoke(actor, null);
                if (!actor.mount.Active || actor.mount.Type != mountCase.Mount)
                    throw new InvalidOperationException($"Native QuickMount failed for {mountCase.Name}: active={actor.mount.Active},type={actor.mount.Type}.");
                quickMount.Invoke(actor, null);
                if (actor.mount.Active)
                    throw new InvalidOperationException($"Native QuickMount did not dismount {mountCase.Name} on toggle.");
            }

            int casesRun = 0;
            var weapons = new (string Name, int Item, bool Bow, bool Staff)[]
            {
                ("IronSword", ModContent.ItemType<IronSword>(), false, false),
                ("IronBow", ModContent.ItemType<IronBow>(), true, false),
                ("IronStaff", ModContent.ItemType<IronStaff>(), false, true)
            };
            foreach (var mountCase in cases)
            foreach (int direction in new[] { -1, 1 })
            foreach (var weapon in weapons)
            {
                Main.projectile = Enumerable.Range(0, Math.Max(oldProjectiles.Length, Main.maxProjectiles + 1)).Select(_ => new Projectile()).ToArray();
                Player actor = NewActor();
                Main.player[playerIndex] = actor;
                actor.mount.SetMount(mountCase.Mount, actor, true);
                actor.inventory[0].SetDefaults(weapon.Item);
                actor.selectedItem = 0;
                actor.lastVisualizedSelectedItem = actor.inventory[0];
                actor.itemAnimation = actor.itemTime = 0;
                actor.releaseUseItem = true;
                actor.controlUseItem = true;
                actor.direction = direction;
                actor.heldProj = -1;
                actor.statMana = actor.statManaMax = 200;
                if (weapon.Bow)
                {
                    actor.inventory[1].SetDefaults(ItemID.WoodenArrow);
                    actor.inventory[1].stack = 99;
                }
                int ammoBefore = actor.inventory[1].stack;
                int manaBefore = actor.statMana;
                actor.ResetEffects();
                actor.UpdateEquips(playerIndex);
                actor.UpdateArmorSets(playerIndex);
                PlayerLoader.PostUpdateEquips(actor);
                Main.mouseX = (int)(actor.Center.X + direction * 180f);
                Main.mouseY = (int)actor.Center.Y;
                actor.ItemCheck();
                actor.PlayerFrame();
                int shots = Main.projectile.Count(p => p.active && p.owner == playerIndex && p.friendly);
                if (!actor.mount.Active || actor.mount.Type != mountCase.Mount)
                    throw new InvalidOperationException($"Mounted {mountCase.Name}/{weapon.Name} changed mount.");
                if (weapon.Bow && shots == 0)
                    throw new InvalidOperationException($"Mounted {mountCase.Name}/{weapon.Name}/{direction} did not fire.");
                if (!weapon.Bow && !weapon.Staff && actor.itemAnimation <= 0)
                    throw new InvalidOperationException($"Mounted {mountCase.Name}/{weapon.Name}/{direction} did not start melee animation.");
                if (weapon.Staff && (shots == 0 || actor.statMana >= manaBefore))
                    throw new InvalidOperationException($"Mounted {mountCase.Name}/{weapon.Name}/{direction} did not fire or consume mana: shots={shots},mana={manaBefore}->{actor.statMana}.");
                if (weapon.Bow && actor.inventory[1].stack > ammoBefore)
                    throw new InvalidOperationException($"Mounted {mountCase.Name}/{weapon.Name}/{direction} increased arrows unexpectedly.");
                casesRun++;
            }
            return $"advanced-mount-itemcheck=PASS summon={cases.Length}/{cases.Length} quickmount-toggle={cases.Length}/{cases.Length} mounted-weapons={casesRun}/{cases.Length * 2 * weapons.Length} mounts={cases.Length} directions=2 IronSword/IronBow/IronStaff";
        }
        finally
        {
            Main.player = oldPlayers;
            Main.projectile = oldProjectiles;
            Main.item = oldItems;
            Main.dust = oldDust;
            Main.npc = oldNpcs;
            Main.myPlayer = oldMyPlayer;
            Main.netMode = oldNetMode;
            Main.mouseX = oldMouseX;
            Main.mouseY = oldMouseY;
            Main.gameMenu = oldMenu;
            Main.gamePaused = oldPaused;
            WorldGen.gen = oldWorldGen;
        }
    }

    private static string TestNewMountGroundAbilities(int playerIndex)
    {
        const int x0 = 120, floorY = 150;
        Player[] oldPlayers = Main.player;
        NPC[] oldNpcs = Main.npc;
        Projectile[] oldProjectiles = Main.projectile;
        Dust[] oldDust = Main.dust;
        Item[] oldItems = Main.item;
        int oldMyPlayer = Main.myPlayer, oldNetMode = Main.netMode;
        bool oldMenu = Main.gameMenu, oldPaused = Main.gamePaused, oldWorldGen = WorldGen.gen;
        var touched = new List<(Point Point, Tile Snapshot)>();
        try
        {
            if (!WorldGen.InWorld(x0 - 10, floorY - 8, 10) || !WorldGen.InWorld(x0 + 12, floorY + 1, 10))
                throw new InvalidOperationException("Advanced mount grounded ability fixture is outside the loaded native world.");
            for (int x = x0 - 10; x <= x0 + 12; x++)
            for (int y = floorY - 8; y <= floorY; y++)
            {
                if (Main.tile[x, y].HasTile || Main.tile[x, y].WallType != WallID.None)
                    throw new InvalidOperationException($"Advanced mount fixture arena is occupied at {x},{y}; refusing to modify existing tiles.");
                Tile snapshot = new();
                snapshot.CopyFrom(Main.tile[x, y]);
                touched.Add((new Point(x, y), snapshot));
            }

            Main.gameMenu = false;
            Main.gamePaused = false;
            WorldGen.gen = false;
            Main.myPlayer = playerIndex;
            Main.netMode = NetmodeID.SinglePlayer;
            Main.player = (Player[])oldPlayers.Clone();
            Main.npc = Enumerable.Range(0, Math.Max(oldNpcs.Length, Main.maxNPCs + 1)).Select(_ => new NPC()).ToArray();
            Main.projectile = Enumerable.Range(0, Math.Max(oldProjectiles.Length, Main.maxProjectiles + 1)).Select(_ => new Projectile()).ToArray();
            Main.dust = Enumerable.Range(0, Math.Max(oldDust.Length, Main.maxDust + 1)).Select(_ => new Dust()).ToArray();
            Main.item = Enumerable.Range(0, Math.Max(oldItems.Length, Main.maxItems + 1)).Select(_ => new Item()).ToArray();
            for (int x = x0 - 10; x <= x0 + 12; x++)
            {
                Tile floor = Main.tile[x, floorY];
                floor.HasTile = true;
                floor.TileType = TileID.Stone;
            }

            MethodInfo setup = typeof(PlayerLoader).GetMethod("SetupPlayer", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("PlayerLoader.SetupPlayer was unavailable for grounded mount fixture.");
            Player NewActor(int mountType)
            {
                Player actor = oldPlayers[playerIndex].SerializedClone();
                setup.Invoke(null, new object[] { actor });
                actor.active = true;
                actor.dead = false;
                actor.whoAmI = playerIndex;
                actor.statLife = actor.statLifeMax = 500;
                actor.isDisplayDollOrInanimate = false;
                actor.position = new Vector2((x0 - 4) * 16f, floorY * 16f - actor.height);
                actor.velocity = Vector2.Zero;
                actor.mount.SetMount(mountType, actor, true);
                Main.player[playerIndex] = actor;
                return actor;
            }
            NPC NewTarget(int index, Vector2 center)
            {
                NPC npc = new();
                npc.SetDefaults(NPCID.BlueSlime);
                npc.whoAmI = index;
                npc.active = true;
                npc.life = npc.lifeMax = 1000;
                npc.noTileCollide = false;
                npc.noGravity = false;
                npc.Center = center;
                Main.npc[index] = npc;
                return npc;
            }

            var dashRows = new List<string>();
            foreach (int direction in new[] { -1, 1 })
            {
                Player tiger = NewActor(ModContent.MountType<WhiteTigerMount>());
                AdvancedMountAbilityPlayer ability = tiger.GetModPlayer<AdvancedMountAbilityPlayer>();
                NPC target = NewTarget(0, tiger.Center + new Vector2(direction * 22f, -4f));
                tiger.controlLeft = tiger.controlRight = false;
                PlayerLoader.PreUpdateMovement(tiger);
                if (direction < 0) tiger.controlLeft = true; else tiger.controlRight = true;
                PlayerLoader.PreUpdateMovement(tiger);
                tiger.controlLeft = tiger.controlRight = false;
                PlayerLoader.PreUpdateMovement(tiger);
                if (direction < 0) tiger.controlLeft = true; else tiger.controlRight = true;
                PlayerLoader.PreUpdateMovement(tiger);
                int startLife = target.life;
                ability.PostUpdate();
                int afterHit = target.life;
                ability.PostUpdate();
                int afterRepeat = target.life;
                if (tiger.velocity.X != direction * 16f || afterHit >= startLife || afterRepeat != afterHit)
                    throw new InvalidOperationException($"Tiger dash contact failed on direction {direction}: velocity={tiger.velocity.X}, target={startLife}->{afterHit}->{afterRepeat}.");
                dashRows.Add($"dir={direction},vx={tiger.velocity.X:0.0},life={startLife}->{afterHit},same-target-repeat={afterRepeat}");
                tiger.mount.Dismount(tiger);
                Main.npc = Enumerable.Range(0, Math.Max(oldNpcs.Length, Main.maxNPCs + 1)).Select(_ => new NPC()).ToArray();
            }

            var foxRows = new List<string>();
            foreach (int direction in new[] { -1, 1 })
            {
                Main.npc = Enumerable.Range(0, Math.Max(oldNpcs.Length, Main.maxNPCs + 1)).Select(_ => new NPC()).ToArray();
                Player fox = NewActor(ModContent.MountType<WhiteNineTailedFoxMount>());
                AdvancedMountAbilityPlayer foxAbility = fox.GetModPlayer<AdvancedMountAbilityPlayer>();
                NPC target = NewTarget(0, fox.Center + new Vector2(direction * 22f, -4f));
                fox.controlLeft = fox.controlRight = false;
                PlayerLoader.PreUpdateMovement(fox);
                if (direction < 0) fox.controlLeft = true; else fox.controlRight = true;
                PlayerLoader.PreUpdateMovement(fox);
                fox.controlLeft = fox.controlRight = false;
                PlayerLoader.PreUpdateMovement(fox);
                if (direction < 0) fox.controlLeft = true; else fox.controlRight = true;
                PlayerLoader.PreUpdateMovement(fox);
                if (fox.velocity.X != direction * 18f || fox.immuneTime < 30)
                    throw new InvalidOperationException($"Fox dash did not activate with native speed/immunity on direction {direction}: vx={fox.velocity.X},immune={fox.immune},time={fox.immuneTime}.");
                int startLife = target.life;
                foxAbility.PostUpdate();
                int afterHit = target.life;
                int dashTicks = 1;
                while (dashTicks < 14)
                {
                    foxAbility.PostUpdate();
                    dashTicks++;
                }
                int afterDash = target.life;
                if (afterHit >= startLife || afterDash != afterHit)
                    throw new InvalidOperationException($"Fox 125-damage dash should hit each NPC once on direction {direction}: {startLife}->{afterHit}->{afterDash} over {dashTicks} frames.");
                fox.controlLeft = fox.controlRight = false;
                PlayerLoader.PreUpdateMovement(fox);
                for (int tick = 0; tick < 20; tick++)
                {
                    PlayerLoader.PreUpdateMovement(fox);
                    foxAbility.PostUpdate();
                }
                fox.velocity.X = 0f;
                if (direction < 0) fox.controlLeft = true; else fox.controlRight = true;
                PlayerLoader.PreUpdateMovement(fox);
                fox.controlLeft = fox.controlRight = false;
                PlayerLoader.PreUpdateMovement(fox);
                if (direction < 0) fox.controlLeft = true; else fox.controlRight = true;
                PlayerLoader.PreUpdateMovement(fox);
                if (Math.Abs(fox.velocity.X) >= 17.9f)
                    throw new InvalidOperationException($"Fox restarted its dash before the 90-frame cooldown expired: vx={fox.velocity.X}.");
                fox.mount.Dismount(fox);
                foxRows.Add($"dir={direction},vx={direction * 18f:0.0},immune=30,life={startLife}->{afterHit},same-dash-repeat={afterDash},cooldown-blocks-retry=PASS");
            }

            Player rhino = NewActor(ModContent.MountType<RhinoMount>());
            for (int i = 0; i < rhino.armor.Length; i++) rhino.armor[i] = new Item();
            Main.player[playerIndex] = rhino;
            int rhinoSummon = ModContent.ItemType<RhinoSummon>();
            rhino.inventory[0].SetDefaults(rhinoSummon);
            rhino.selectedItem = 0;
            rhino.lastVisualizedSelectedItem = rhino.inventory[0];
            rhino.itemAnimation = rhino.itemTime = 0;
            rhino.controlUseItem = true;
            rhino.releaseUseItem = true;
            Main.mouseX = (int)(rhino.Center.X + 180f);
            Main.mouseY = (int)rhino.Center.Y;
            for (int tick = 0; tick < 30 && !rhino.mount.Active; tick++)
            {
                rhino.controlUseItem = true;
                rhino.ItemCheck();
                rhino.PlayerFrame();
            }
            if (!rhino.mount.Active || rhino.mount.Type != ModContent.MountType<RhinoMount>() ||
                !rhino.HasBuff(Mount.mounts[ModContent.MountType<RhinoMount>()].buff))
                throw new InvalidOperationException($"Native Rhino ItemCheck summon did not retain its mount buff: active={rhino.mount.Active},mount={rhino.mount.Type},hasBuff={rhino.HasBuff(Mount.mounts[ModContent.MountType<RhinoMount>()].buff)}.");
            NPC rhinoTarget = NewTarget(0, rhino.Center + new Vector2(20f, -4f));
            AdvancedMountAbilityPlayer rhinoAbility = rhino.GetModPlayer<AdvancedMountAbilityPlayer>();
            int rhinoStill = rhinoTarget.life;
            rhinoAbility.PostUpdate();
            if (rhinoTarget.life != rhinoStill)
                throw new InvalidOperationException("A stationary rhino dealt contact damage.");
            rhino.velocity.X = 6f;
            int rhinoMoving = rhinoTarget.life;
            rhinoAbility.PostUpdate();
            int rhinoHit = rhinoTarget.life;
            for (int tick = 0; tick < 29; tick++) rhinoAbility.PostUpdate();
            int rhinoAfterCooldownWindow = rhinoTarget.life;
            if (rhinoHit >= rhinoMoving || rhinoAfterCooldownWindow != rhinoHit)
                throw new InvalidOperationException($"Rhino moving hit/cooldown mismatch: stationary={rhinoStill}, moving={rhinoMoving}->{rhinoHit}, after29={rhinoAfterCooldownWindow}.");
            rhino.ResetEffects();
            int defenseBefore = rhino.statDefense;
            MountLoader.GetMount(rhino.mount.Type).UpdateEffects(rhino);
            if (!rhino.mount.Active || rhino.statDefense - defenseBefore != 8)
                throw new InvalidOperationException($"Mounted rhino defense should add 8 once through native MountLoader dispatch: before={defenseBefore},after={rhino.statDefense}; active={rhino.mount.Active},mount={rhino.mount.Type},buff={rhino.HasBuff(Mount.mounts[ModContent.MountType<RhinoMount>()].buff)},resolvedMount={MountLoader.GetMount(rhino.mount.Type).GetType().Name},resolvedType={MountLoader.GetMount(rhino.mount.Type).Type},instanceType={ModContent.GetInstance<RhinoMount>().Type},expected={ModContent.MountType<RhinoMount>()}.");
            rhino.mount.Dismount(rhino);
            rhino.ResetEffects();
            if (rhino.statDefense != 0)
                throw new InvalidOperationException($"Rhino's +8 defense persisted after dismount/ResetEffects: {rhino.statDefense}.");

            Player flame = NewActor(ModContent.MountType<FlameWarhorseMount>());
            AdvancedMountAbilityPlayer flameAbility = flame.GetModPlayer<AdvancedMountAbilityPlayer>();
            int patchType = ModContent.ProjectileType<FlameWarhorseTrailFire>();
            int stationaryProjectiles = Main.projectile.Count(p => p.active && p.type == patchType);
            for (int tick = 0; tick < 10; tick++) flameAbility.PostUpdate();
            if (Main.projectile.Count(p => p.active && p.type == patchType) != stationaryProjectiles)
                throw new InvalidOperationException("Flame warhorse left a fire patch while stationary.");
            flame.velocity.X = 6f;
            flame.velocity.Y = 0f;
            for (int tick = 0; tick < 8; tick++) flameAbility.PostUpdate();
            Projectile patch = Main.projectile.FirstOrDefault(p => p.active && p.type == patchType)
                ?? throw new InvalidOperationException("Moving grounded flame warhorse did not create its native fire patch.");
            NPC fireTarget = NewTarget(0, new Vector2(patch.Center.X, floorY * 16f - 9f));
            int fireLife = fireTarget.life;
            for (int tick = 0; tick < 2; tick++) patch.Update(patch.whoAmI);
            int fireAfter = fireTarget.life;
            if (fireAfter >= fireLife || !fireTarget.HasBuff(BuffID.OnFire3))
                throw new InvalidOperationException($"Native fire patch did not hit/burn its nearby target: life={fireLife}->{fireAfter},OnFire3={fireTarget.HasBuff(BuffID.OnFire3)},patch={patch.Hitbox},active={patch.active},owner={patch.owner},type={patch.type},target={fireTarget.Hitbox},targetActive={fireTarget.active},targetImmortal={fireTarget.immortal},targetDontTakeDamage={fireTarget.dontTakeDamage},localImmunity={patch.localNPCImmunity[fireTarget.whoAmI]},playerCenter={flame.Center},bottom={flame.Bottom},ownerMe={Main.myPlayer}.");
            int fireBeforeSecondHit = fireTarget.life;
            for (int tick = 1; tick <= 31 && fireTarget.life == fireBeforeSecondHit; tick++) patch.Update(patch.whoAmI);
            int fireAfterCooldown = fireTarget.life;
            if (fireAfterCooldown >= fireBeforeSecondHit)
                throw new InvalidOperationException($"Fire patch did not damage again after its 30-frame local immunity: life={fireBeforeSecondHit}->{fireAfterCooldown},localImmunity={patch.localNPCImmunity[fireTarget.whoAmI]}.");
            int remainingLifetime = patch.timeLeft;
            for (int tick = 0; tick <= remainingLifetime && patch.active; tick++) patch.Update(patch.whoAmI);
            if (patch.active)
                throw new InvalidOperationException($"Fire patch did not expire after 180 frames: active={patch.active},timeLeft={patch.timeLeft}.");
            return $"mount-ground-abilities=PASS tiger-dash[{string.Join(';', dashRows)}]; fox-dash[{string.Join(';', foxRows)}] 125damage/30immunity/once-per-dash; rhino stationaryNoHit/movingDamage/cooldown/dismount=PASS life={rhinoStill}->{rhinoHit}->{rhinoAfterCooldownWindow} defense=0->8->0; flame stationaryNoPatch/movingPatch/nativeUpdateDamage/OnFire3=PASS life={fireLife}->{fireAfter}->{fireAfterCooldown} repeatedAfter<=31ticks; expires after180; patches={Main.projectile.Count(p => p.active && p.type == patchType)}";
        }
        finally
        {
            foreach ((Point point, Tile snapshot) in touched)
                Main.tile[point.X, point.Y].CopyFrom(snapshot);
            Main.player = oldPlayers;
            Main.npc = oldNpcs;
            Main.projectile = oldProjectiles;
            Main.dust = oldDust;
            Main.item = oldItems;
            Main.myPlayer = oldMyPlayer;
            Main.netMode = oldNetMode;
            Main.gameMenu = oldMenu;
            Main.gamePaused = oldPaused;
            WorldGen.gen = oldWorldGen;
        }
    }

    private static bool GeometryDeltaWithinPixel(string row)
    {
        int marker = row.LastIndexOf("delta=", StringComparison.Ordinal);
        return marker >= 0 && float.TryParse(row.AsSpan(marker + 6), out float delta) && Math.Abs(delta) <= 1f;
    }

    private static string TestWhiteTigerChestGeneration()
    {
        Chest[] oldChests = Main.chest;
        UnifiedRandom oldRandom = Main.rand;
        int centerX = Main.maxTilesX / 2;
        int centerY = Math.Clamp((int)Main.worldSurface + 100, 80, Main.maxTilesY - 250);
        int[] xs = { centerX, centerX + 28, centerX + 56, centerX + 84, 80 };
        int beachY = Math.Max((int)Main.worldSurface + 1, Math.Min(centerY, (int)WorldGen.oceanLevel - 1));
        if (!WorldGen.oceanDepths(xs[4], beachY))
            throw new InvalidOperationException($"Could not construct a native beach-depth chest case: beachDistance={WorldGen.beachDistance},oceanLevel={WorldGen.oceanLevel},worldSurface={Main.worldSurface},probeY={beachY}.");
        var touched = new Dictionary<Point, Tile>();
        try
        {
            for (int i = 0; i < xs.Length; i++)
            for (int y = (i == 4 ? beachY : centerY) - 6; y <= (i == 4 ? beachY : centerY) + 6; y++)
            for (int dx = -6; dx <= 6; dx++)
            {
                int tx = xs[i] + dx;
                if (!WorldGen.InWorld(tx, y, 10))
                    throw new InvalidOperationException("Tiger chest-generation fixture is too close to the world edge.");
                Point point = new(tx, y);
                if (!touched.ContainsKey(point))
                {
                    Tile snapshot = new();
                    snapshot.CopyFrom(Main.tile[tx, y]);
                    touched.Add(point, snapshot);
                }
                Tile tile = Main.tile[tx, y];
                tile.HasTile = false;
                tile.TileType = TileID.Stone;
                tile.WallType = WallID.None;
            }

            // Make one jungle region, one dungeon-brick region, and one
            // dungeon-wall region. Other windows remain ordinary stone.
            for (int dx = -6; dx <= 6; dx++)
            for (int dy = -6; dy <= 6; dy++)
            {
                Tile jungleTile = Main.tile[xs[1] + dx, centerY + dy];
                jungleTile.HasTile = true;
                jungleTile.TileType = TileID.Mud;
            }
            Tile dungeonBlock = Main.tile[xs[2], centerY];
            dungeonBlock.HasTile = true;
            dungeonBlock.TileType = TileID.BlueDungeonBrick;
            Tile dungeonWall = Main.tile[xs[3], centerY];
            dungeonWall.WallType = WallID.BlueDungeon;

            Main.chest = new Chest[Main.maxChests];
            Chest MakeChest(int x, int y, int chestItemType)
            {
                Chest chest = new() { x = x, y = y };
                chest.item = Enumerable.Range(0, 40).Select(_ => new Item()).ToArray();
                chest.item[0].SetDefaults(ItemID.Rope);
                chest.item[0].stack = 7;
                Item placed = new(chestItemType);
                if (placed.createTile != TileID.Containers && placed.createTile != TileID.Containers2)
                    throw new InvalidOperationException($"Chest item {Lang.GetItemNameValue(chestItemType)} has unexpected createTile={placed.createTile}.");
                Tile tile = Main.tile[x, y];
                tile.HasTile = true;
                tile.TileType = (ushort)placed.createTile;
                tile.TileFrameX = (short)(placed.placeStyle * 36);
                Main.chest[Array.FindIndex(Main.chest, c => c is null)] = chest;
                return chest;
            }

            Chest forestChest = MakeChest(xs[0], centerY, ItemID.Chest);
            Chest jungleIvyChest = MakeChest(xs[1], centerY, ItemID.IvyChest);
            Chest dungeonBrickChest = MakeChest(xs[2], centerY, ItemID.GoldChest);
            Chest dungeonWallChest = MakeChest(xs[3], centerY, ItemID.GoldChest);
            Chest beachChest = MakeChest(xs[4], beachY, ItemID.GoldChest);

            int seed = -1;
            for (int candidate = 0; candidate < 100000; candidate++)
            {
                var rng = new UnifiedRandom(candidate);
                if (rng.Next(10) == 0 && rng.Next(10) == 0) { seed = candidate; break; }
            }
            if (seed < 0) throw new InvalidOperationException("Could not select a deterministic 10% chest-roll seed.");
            Main.rand = new UnifiedRandom(seed);
            ModContent.GetInstance<AdvancedMountWorldLoot>().PostWorldGen();

            int summonType = ModContent.ItemType<WhiteTigerSummon>();
            int Count(Chest chest) => chest.item.Count(item => item is not null && item.type == summonType);
            bool preserved(Chest chest) => chest.item[0].type == ItemID.Rope && chest.item[0].stack == 7;
            if (Count(forestChest) != 1 || Count(jungleIvyChest) != 1
                || Count(dungeonBrickChest) != 0 || Count(dungeonWallChest) != 0 || Count(beachChest) != 0
                || !preserved(forestChest) || !preserved(jungleIvyChest))
                throw new InvalidOperationException($"Native PostWorldGen tiger chest selection failed: forest={Count(forestChest)},ivy={Count(jungleIvyChest)},dungeonBrick={Count(dungeonBrickChest)},dungeonWall={Count(dungeonWallChest)},beach={Count(beachChest)},preserved={preserved(forestChest)}/{preserved(jungleIvyChest)},seed={seed}.");

            return $"white-tiger-postworldgen-chests=PASS ordinaryForest={Count(forestChest)}; jungleIvy={Count(jungleIvyChest)}; excludedDungeonBrick={Count(dungeonBrickChest)}; excludedDungeonWall={Count(dungeonWallChest)}; excludedOceanShore={Count(beachChest)}; preexistingRopeStack=7 preserved; seeded10PercentRoll={seed}";
        }
        finally
        {
            foreach ((Point point, Tile tile) in touched)
                Main.tile[point.X, point.Y].CopyFrom(tile);
            Main.chest = oldChests;
            Main.rand = oldRandom;
        }
    }

    private static string TestPlayKitMigration(int playerIndex)
    {
        string oldKitEnv = Environment.GetEnvironmentVariable("MILKCRAFT_TERRARIA_PLAY_KIT");
        string oldPlayKitAlias = Environment.GetEnvironmentVariable("PLAY_KIT");
        Player oldPlayer = Main.player[playerIndex];
        Item[] oldItems = Main.item;
        int oldNetMode = Main.netMode;
        int oldMyPlayer = Main.myPlayer;
        bool oldMenu = Main.gameMenu;
        var setup = typeof(PlayerLoader).GetMethod("SetupPlayer", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("PlayerLoader.SetupPlayer is unavailable for advanced mount kit migration.");
        int[] mountTypes =
        {
            ModContent.ItemType<WhiteTigerSummon>(), ModContent.ItemType<WhiteNineTailedFoxSummon>(),
            ModContent.ItemType<RhinoSummon>(), ModContent.ItemType<FlameWarhorseSummon>(),
            ModContent.ItemType<WhiteUnicornSummon>()
        };
        try
        {
            Main.myPlayer = playerIndex;
            Main.netMode = NetmodeID.SinglePlayer;
            Main.gameMenu = false;
            Main.item = Enumerable.Range(0, Math.Max(oldItems.Length, Main.maxItems + 1)).Select(_ => new Item()).ToArray();
            Environment.SetEnvironmentVariable("MILKCRAFT_TERRARIA_PLAY_KIT", "1");
            Environment.SetEnvironmentVariable("PLAY_KIT", null);
            var oldFlags = new Terraria.ModLoader.IO.TagCompound
            {
                ["milkcraftPlayKitGranted"] = true,
                ["milkcraftT3T4PlayKitGranted"] = true,
                ["milkcraft025PassiveKitGranted"] = true,
                ["milkcraft0210BalanceKitGranted"] = true,
                ["milkcraft0212MountKitGranted"] = true
            };
            Player actor = oldPlayer.SerializedClone();
            setup.Invoke(null, new object[] { actor });
            actor.active = true;
            actor.dead = false;
            actor.whoAmI = playerIndex;
            Main.player[playerIndex] = actor;
            PlayKitPlayer kit = actor.GetModPlayer<PlayKitPlayer>();
            kit.LoadData(oldFlags); // Existing characters have the older kit markers but not the 0.2.13 marker.
            kit.OnEnterWorld();
            int[] first = mountTypes.Select(type => CountOwned(actor, type)).ToArray();
            if (first.Any(count => count != 1))
                throw new InvalidOperationException($"0.2.13 play-kit migration did not grant exactly one of each new mount: {string.Join(',', first)}.");
            var saved = new Terraria.ModLoader.IO.TagCompound();
            kit.SaveData(saved);
            if (!saved.GetBool("milkcraft0213MountKitGranted"))
                throw new InvalidOperationException("0.2.13 play-kit grant marker was not saved.");
            kit.LoadData(saved);
            kit.OnEnterWorld();
            int[] repeated = mountTypes.Select(type => CountOwned(actor, type)).ToArray();
            if (!first.SequenceEqual(repeated))
                throw new InvalidOperationException($"0.2.13 play-kit repeated after save/load: {string.Join(',', first)} -> {string.Join(',', repeated)}.");

            Main.item = Enumerable.Range(0, Math.Max(oldItems.Length, Main.maxItems + 1)).Select(_ => new Item()).ToArray();
            Player disabled = oldPlayer.SerializedClone();
            setup.Invoke(null, new object[] { disabled });
            disabled.active = true;
            disabled.dead = false;
            disabled.whoAmI = playerIndex;
            Main.player[playerIndex] = disabled;
            PlayKitPlayer disabledKit = disabled.GetModPlayer<PlayKitPlayer>();
            disabledKit.LoadData(oldFlags);
            Environment.SetEnvironmentVariable("MILKCRAFT_TERRARIA_PLAY_KIT", null);
            disabledKit.OnEnterWorld();
            int[] disabledCounts = mountTypes.Select(type => CountOwned(disabled, type)).ToArray();
            if (disabledCounts.Any(count => count != 0))
                throw new InvalidOperationException($"Advanced mounts were granted with play kit disabled: {string.Join(',', disabledCounts)}.");
            return "playkit-0213-migration=PASS existing-old-flags/missing-new-marker grants 5/5 exactly once; SaveData/LoadData prevents duplicate; disabled-env grants 0/5";
        }
        finally
        {
            Main.player[playerIndex] = oldPlayer;
            Main.item = oldItems;
            Main.netMode = oldNetMode;
            Main.myPlayer = oldMyPlayer;
            Main.gameMenu = oldMenu;
            Environment.SetEnvironmentVariable("MILKCRAFT_TERRARIA_PLAY_KIT", oldKitEnv);
            Environment.SetEnvironmentVariable("PLAY_KIT", oldPlayKitAlias);
        }

        int CountOwned(Player player, int type)
        {
            int inventoryCount = player.inventory.Where(item => item is not null && item.type == type).Sum(item => item.stack);
            int worldCount = Main.item.Where(item => item is { active: true } && item.type == type).Sum(item => item.stack);
            return inventoryCount + worldCount;
        }
    }

    private static string TestAdvancedMountLootAndBagPersistence(int playerIndex)
    {
        int tigerType = ModContent.ItemType<WhiteTigerSummon>();
        int foxType = ModContent.ItemType<WhiteNineTailedFoxSummon>();
        int unicornType = ModContent.ItemType<WhiteUnicornSummon>();

        int crateCount = 0;
        var checkedCrates = new List<int>();
        for (int type = 1; type < ItemID.Count; type++)
        {
            if (!ItemID.Sets.IsFishingCrate[type]) continue;
            List<IItemDropRule> rules = Main.ItemDropsDB.GetRulesForItemID(type);
            CommonDrop tigerRule = rules.OfType<CommonDrop>().FirstOrDefault(rule => rule.itemId == tigerType);
            if (tigerRule is null) continue;
            if (tigerRule.chanceDenominator != 10 || tigerRule.chanceNumerator != 1)
                throw new InvalidOperationException($"Fishing crate {type} has unexpected tiger mount drop rate {tigerRule.chanceNumerator}/{tigerRule.chanceDenominator}.");
            crateCount++;
            checkedCrates.Add(type);
        }
        if (crateCount == 0)
            throw new InvalidOperationException("No vanilla fishing crates registered the White Tiger summon drop.");

        List<IItemDropRule> planteraBagRules = Main.ItemDropsDB.GetRulesForItemID(ItemID.PlanteraBossBag);
        CommonDrop foxBagRule = planteraBagRules.OfType<CommonDrop>().FirstOrDefault(rule => rule.itemId == foxType);
        if (foxBagRule is null || foxBagRule.chanceDenominator != 4 || foxBagRule.chanceNumerator != 1)
            throw new InvalidOperationException("Plantera boss bag must register one 1/4 White Nine-Tailed Fox summon drop.");

        bool oldDownedGolem = NPC.downedGolemBoss;
        bool oldDayTime = Main.dayTime;
        UnifiedRandom oldRandom = Main.rand;
        Item[] oldItems = Main.item;
        Player oldPlayer = Main.player[playerIndex];
        int oldMyPlayer = Main.myPlayer;
        int oldNetMode = Main.netMode;
        NPC oldNpc = Main.npc[0];
        try
        {
            NPC.downedGolemBoss = true;
            Main.dayTime = false;
            Main.rand = new UnifiedRandom(19821213);
            Main.netMode = NetmodeID.SinglePlayer;
            Main.myPlayer = playerIndex;
            Main.item = Enumerable.Range(0, Math.Max(oldItems.Length, Main.maxItems + 1)).Select(_ => new Item()).ToArray();
            Player actor = SmokeSystem.CreatePreviewPlayer(ItemID.IronHelmet, ItemID.IronChainmail, ItemID.IronGreaves);
            actor.active = true;
            actor.dead = false;
            actor.whoAmI = playerIndex;
            actor.inventory = Enumerable.Range(0, oldPlayer.inventory.Length).Select(_ => new Item()).ToArray();
            Main.player[playerIndex] = actor;

            NPC queen = new();
            queen.SetDefaults(NPCID.HallowBoss);
            queen.whoAmI = 0;
            queen.ai[3] = 2f; // Native Hallow Boss genuinely-enraged phase flag.
            Main.npc[0] = queen;
            Item enragedBag = new();
            enragedBag.SetDefaults(ItemID.FairyQueenBossBag);
            AdvancedMountBagLoot enragedGlobal = enragedBag.GetGlobalItem<AdvancedMountBagLoot>();
            enragedGlobal.OnSpawn(enragedBag, queen.GetSource_Loot());

            var savedFlags = new Terraria.ModLoader.IO.TagCompound();
            enragedGlobal.SaveData(enragedBag, savedFlags);
            Item savedBag = new();
            savedBag.SetDefaults(ItemID.FairyQueenBossBag);
            savedBag.GetGlobalItem<AdvancedMountBagLoot>().LoadData(savedBag, savedFlags);
            using var stream = new MemoryStream();
            using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
                enragedGlobal.NetSend(enragedBag, writer);
            stream.Position = 0;
            Item networkBag = new();
            networkBag.SetDefaults(ItemID.FairyQueenBossBag);
            using (var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true))
                networkBag.GetGlobalItem<AdvancedMountBagLoot>().NetReceive(networkBag, reader);

            // Opening after the world clock changes must honor the stored kill-time state.
            Main.dayTime = true;
            savedBag.GetGlobalItem<AdvancedMountBagLoot>().RightClick(savedBag, actor);
            int enragedAward = CountOwned(actor, unicornType);
            if (enragedAward != 1)
                throw new InvalidOperationException($"An enraged-at-kill Empress bag did not guarantee a Unicorn after the clock changed: count={enragedAward}.");

            actor.inventory = Enumerable.Range(0, actor.inventory.Length).Select(_ => new Item()).ToArray();
            Main.item = Enumerable.Range(0, Math.Max(oldItems.Length, Main.maxItems + 1)).Select(_ => new Item()).ToArray();
            networkBag.GetGlobalItem<AdvancedMountBagLoot>().RightClick(networkBag, actor);
            if (CountOwned(actor, unicornType) != 1)
                throw new InvalidOperationException("The network-serialized enraged Empress bag lost its guaranteed mount award.");

            int hitSeed = Enumerable.Range(0, 10000).First(seed => new UnifiedRandom(seed).NextBool(4));
            int missSeed = Enumerable.Range(0, 10000).First(seed => !new UnifiedRandom(seed).NextBool(4));
            queen.ai[3] = 0f;
            NPC.downedGolemBoss = true;
            Item nonEnragedBag = new();
            nonEnragedBag.SetDefaults(ItemID.FairyQueenBossBag);
            nonEnragedBag.GetGlobalItem<AdvancedMountBagLoot>().OnSpawn(nonEnragedBag, queen.GetSource_Loot());
            actor.inventory = Enumerable.Range(0, actor.inventory.Length).Select(_ => new Item()).ToArray();
            Main.item = Enumerable.Range(0, Math.Max(oldItems.Length, Main.maxItems + 1)).Select(_ => new Item()).ToArray();
            Main.rand = new UnifiedRandom(hitSeed);
            nonEnragedBag.GetGlobalItem<AdvancedMountBagLoot>().RightClick(nonEnragedBag, actor);
            int nonEnragedWin = CountOwned(actor, unicornType);
            actor.inventory = Enumerable.Range(0, actor.inventory.Length).Select(_ => new Item()).ToArray();
            Main.item = Enumerable.Range(0, Math.Max(oldItems.Length, Main.maxItems + 1)).Select(_ => new Item()).ToArray();
            Main.rand = new UnifiedRandom(missSeed);
            nonEnragedBag.GetGlobalItem<AdvancedMountBagLoot>().RightClick(nonEnragedBag, actor);
            int nonEnragedMiss = CountOwned(actor, unicornType);
            if (nonEnragedWin != 1 || nonEnragedMiss != 0)
                throw new InvalidOperationException($"Non-enraged eligible Empress bag did not honor its 1/4 roll: winSeed={hitSeed}, win={nonEnragedWin}, missSeed={missSeed}, miss={nonEnragedMiss}.");

            Main.dayTime = false;
            NPC.downedGolemBoss = false;
            Item preGolemBag = new();
            preGolemBag.SetDefaults(ItemID.FairyQueenBossBag);
            preGolemBag.GetGlobalItem<AdvancedMountBagLoot>().OnSpawn(preGolemBag, queen.GetSource_Loot());
            int before = CountOwned(actor, unicornType);
            preGolemBag.GetGlobalItem<AdvancedMountBagLoot>().RightClick(preGolemBag, actor);
            int after = CountOwned(actor, unicornType);
            if (after != before)
                throw new InvalidOperationException("A Fairy Queen bag from a pre-Golem kill granted the White Unicorn.");

            if (!savedFlags.GetBool("MilkcraftEmpressMountEligible") || !savedFlags.GetBool("MilkcraftEmpressEnragedMountDrop"))
                throw new InvalidOperationException("Empress bag did not persist both eligibility and genuinely-enraged-at-kill flags.");
            return $"mount-loot-registration=PASS planteraBagFox=1/4; fishingCratesWithTiger=1/10 across {crateCount} native crate types [{string.Join(',', checkedCrates)}]; empressBag=OnSpawn(ai3=2)->Save/Load+NetSend/Receive->daytime-open guaranteed 1; normal eligible 1/4 seeds={hitSeed}/{missSeed}; pre-Golem bag=0";
        }
        finally
        {
            NPC.downedGolemBoss = oldDownedGolem;
            Main.dayTime = oldDayTime;
            Main.rand = oldRandom;
            Main.item = oldItems;
            Main.npc[0] = oldNpc;
            Main.player[playerIndex] = oldPlayer;
            Main.myPlayer = oldMyPlayer;
            Main.netMode = oldNetMode;
        }

        int CountOwned(Player player, int type)
        {
            int inventory = player.inventory.Where(item => item is not null && item.type == type).Sum(item => item.stack);
            int world = Main.item.Where(item => item is { active: true } && item.type == type).Sum(item => item.stack);
            return inventory + world;
        }
    }

    private static string TestAdvancedMountLootRegistration()
    {
        int tigerType = ModContent.ItemType<WhiteTigerSummon>();
        int foxType = ModContent.ItemType<WhiteNineTailedFoxSummon>();
        int crateCount = 0;
        for (int type = 1; type < ItemID.Count; type++)
        {
            if (!ItemID.Sets.IsFishingCrate[type]) continue;
            CommonDrop rule = Main.ItemDropsDB.GetRulesForItemID(type)
                .OfType<CommonDrop>().FirstOrDefault(drop => drop.itemId == tigerType);
            if (rule is null) continue;
            if (rule.chanceNumerator != 1 || rule.chanceDenominator != 10)
                throw new InvalidOperationException($"Fishing crate {type} has White Tiger drop {rule.chanceNumerator}/{rule.chanceDenominator}, expected 1/10.");
            crateCount++;
        }
        if (crateCount == 0) throw new InvalidOperationException("No vanilla fishing crate registered the White Tiger drop.");

        List<IItemDropRule> planteraRules = Main.ItemDropsDB.GetRulesForNPCID(NPCID.Plantera);
        CommonDrop foxNpcRule = planteraRules.OfType<CommonDrop>().FirstOrDefault(drop => drop.itemId == foxType);
        List<IItemDropRule> planteraBagRules = Main.ItemDropsDB.GetRulesForItemID(ItemID.PlanteraBossBag);
        CommonDrop foxBagRule = planteraBagRules.OfType<CommonDrop>().FirstOrDefault(drop => drop.itemId == foxType);
        if (foxNpcRule is null || foxNpcRule.chanceNumerator != 1 || foxNpcRule.chanceDenominator != 4)
            throw new InvalidOperationException("Plantera normal-mode White Nine-Tailed Fox drop is not 1/4.");
        if (foxBagRule is null || foxBagRule.chanceNumerator != 1 || foxBagRule.chanceDenominator != 4)
            throw new InvalidOperationException("Plantera bag White Nine-Tailed Fox drop is not 1/4.");

        int castleBag = ModContent.ItemType<CastleCoreBag>();
        if (!ItemID.Sets.BossBag[castleBag])
            throw new InvalidOperationException("Castle Core bag is not registered as a native boss bag.");
        return $"mount-loot-registration=PASS fishing-crates={crateCount} at 1/10; Plantera fox=normal 1/4 + bag 1/4; White Unicorn acquisition moved to Castle Core and verified by native ItemSlot bag diagnostic";
    }

}
