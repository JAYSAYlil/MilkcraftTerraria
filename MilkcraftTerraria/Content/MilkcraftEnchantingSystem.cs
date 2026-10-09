using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.DataStructures;
using Terraria.GameContent;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;
using Terraria.UI;
using Microsoft.Xna.Framework.Input;
using System.Reflection;
using MilkcraftTerraria.Content.Items;
using MilkcraftTerraria.Content.Tiles;
using Terraria.GameInput;

namespace MilkcraftTerraria.Content;

public enum MilkcraftEnchant : byte
{
    None,
    Sharpness,
    Power,
    Spell,
    Critical,
    Provocation,
    Stealth,
    ShieldWall,
    Armor,
    Resistance,
    Ice,
    Healing,
    Explosive
}

/// <summary>Server-validated UI and item-referenced enchanting operations.</summary>
public sealed class MilkcraftEnchantingSystem : ModSystem
{
    private static bool _visible;
    private static int _selectedSlot = -1;
    private static int _selectedKind = -1;
    private static int _page;
    private static bool _mouseWasDown;
    private static bool _ownsItemTooltip;
    private static bool _diagnosticEscapePressed;
    internal static bool DiagnosticVisible => _visible;
    private const int ItemsPerPage = 8;
    private const int WindowWidth = 690;
    private const int WindowHeight = 520;
    private static float _uiScale = 1f;
    private static Vector2 _uiOffset;
    private static int _diagnosticWidth;
    private static int _diagnosticHeight;
    // In the native UI update/draw phase, SetZoom_Scaled has already changed
    // Main.screenWidth/Height and mouseX/Y to UI-space values. Never divide
    // those values by UIScale a second time.
    private static int UiWidth => _diagnosticWidth > 0 ? _diagnosticWidth : Main.screenWidth;
    private static int UiHeight => _diagnosticHeight > 0 ? _diagnosticHeight : Main.screenHeight;

    internal static void DiagnosticDrawPreview(int width, int height, float uiScale, bool emptyState = false)
    {
        bool oldVisible = _visible;
        int oldSlot = _selectedSlot, oldKind = _selectedKind;
        int oldWidth = _diagnosticWidth, oldHeight = _diagnosticHeight;
        try
        {
            _diagnosticWidth = Math.Max(1, (int)MathF.Round(width / Math.Max(0.1f, uiScale)));
            _diagnosticHeight = Math.Max(1, (int)MathF.Round(height / Math.Max(0.1f, uiScale)));
            _visible = true;
            _selectedSlot = emptyState ? -1 : Array.FindIndex(Main.LocalPlayer.inventory,
                item => MilkcraftEnchantedItem.AllowedKinds(item).Count > 0);
            _selectedKind = _selectedSlot >= 0
                ? (int)MilkcraftEnchantedItem.AllowedKinds(Main.LocalPlayer.inventory[_selectedSlot])[0]
                : -1;
            DrawDiagnosticThroughNativeLayer();
        }
        finally
        {
            _visible = oldVisible;
            _selectedSlot = oldSlot;
            _selectedKind = oldKind;
            _diagnosticWidth = oldWidth;
            _diagnosticHeight = oldHeight;
        }
    }

    internal static bool DiagnosticValidateLayouts()
    {
        foreach ((int width, int height, float uiScale) in new[]
        {
            (1280, 720, 1f), (1280, 720, 1.25f), (1280, 720, 1.5f),
            (1920, 1080, 1f), (1920, 1080, 1.25f), (1920, 1080, 1.5f),
            (2560, 1494, 1.6296296f)
        })
        {
            float logicalWidth = width / uiScale, logicalHeight = height / uiScale;
            ComputeLayout((int)logicalWidth, (int)logicalHeight, out float scale, out Vector2 offset);
            int left = (int)((logicalWidth - WindowWidth) / 2f);
            int top = (int)((logicalHeight - WindowHeight) / 2f);
            Vector2 topLeft = offset + new Vector2(left, top) * scale;
            Vector2 bottomRight = offset + new Vector2(left + WindowWidth, top + WindowHeight) * scale;
            Rectangle closeLogical = new(left + 574, top + 474, 98, 32);
            Rectangle close = TransformRect(closeLogical, offset, scale);
            Vector2 uiMouse = new(close.Center.X, close.Center.Y);
            Vector2 mappedMouse = (uiMouse - offset) / scale;
            if (topLeft.X < 0 || topLeft.Y < 0 || bottomRight.X > logicalWidth || bottomRight.Y > logicalHeight
                || !closeLogical.Contains(mappedMouse.ToPoint()))
                return false;
        }
        return true;
    }

    internal static Point DiagnosticWindowPoint(int physicalWidth, int physicalHeight, float actualUiScale, int x, int y)
    {
        int logicalWidth = Math.Max(1, (int)MathF.Floor(physicalWidth / Math.Max(0.1f, actualUiScale)));
        int logicalHeight = Math.Max(1, (int)MathF.Floor(physicalHeight / Math.Max(0.1f, actualUiScale)));
        ComputeLayout(logicalWidth, logicalHeight, out float scale, out Vector2 offset);
        int left = (logicalWidth - WindowWidth) / 2;
        int top = (logicalHeight - WindowHeight) / 2;
        return (offset + new Vector2(left + x, top + y) * scale).ToPoint();
    }

    private static void DrawDiagnosticThroughNativeLayer()
    {
        List<GameInterfaceLayer> layers = new();
        ModContent.GetInstance<MilkcraftEnchantingSystem>().ModifyInterfaceLayers(layers);
        GameInterfaceLayer layer = layers.FirstOrDefault(entry => entry.Name == "Milkcraft: Enchanting");
        if (layer is null || !layer.Draw())
            throw new InvalidOperationException("native enchanting interface layer was not drawn");
    }

    internal static void DiagnosticUpdateUIFrame(int logicalMouseX, int logicalMouseY, bool leftDown, bool escapeDown = false)
    {
        Main.mouseX = logicalMouseX;
        Main.mouseY = logicalMouseY;
        Main.mouseLeft = leftDown;
        _diagnosticEscapePressed = escapeDown;
        ModContent.GetInstance<MilkcraftEnchantingSystem>().UpdateUI(new GameTime());
    }

    // Reproduce the native input zoom stage before invoking the real ModSystem.UpdateUI
    // hook. Coordinates passed here are the desired logical UI point; they are converted
    // to physical input, cached, and then transformed by PlayerInput.SetZoom_UI exactly
    // as in Main.DoUpdate.
    internal static void DiagnosticNativeUpdateUIFrame(int logicalMouseX, int logicalMouseY, bool leftDown,
        int logicalWidth, int logicalHeight, bool escapeDown = false)
    {
        int oldScreenWidth = Main.screenWidth, oldScreenHeight = Main.screenHeight;
        int oldMouseX = Main.mouseX, oldMouseY = Main.mouseY;
        int oldLastMouseX = Main.lastMouseX, oldLastMouseY = Main.lastMouseY;
        bool oldMouseLeft = Main.mouseLeft;
        KeyboardState oldKeys = Main.keyState, oldOldKeys = Main.oldKeyState;
        const BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        Dictionary<FieldInfo, object> inputCache = typeof(PlayerInput).GetFields(flags)
            .Where(field => field.Name.StartsWith("_original", StringComparison.Ordinal) && field.FieldType == typeof(int))
            .ToDictionary(field => field, field => field.GetValue(null));
        try
        {
            float zoom = 1f / Math.Max(0.1f, Main.UIScale);
            int physicalWidth = Math.Max(1, (int)MathF.Ceiling(logicalWidth / zoom));
            int physicalHeight = Math.Max(1, (int)MathF.Ceiling(logicalHeight / zoom));
            Main.screenWidth = physicalWidth;
            Main.screenHeight = physicalHeight;
            Main.mouseX = (int)MathF.Ceiling(logicalMouseX / zoom);
            Main.mouseY = (int)MathF.Ceiling(logicalMouseY / zoom);
            Main.mouseLeft = leftDown;
            Main.oldKeyState = new KeyboardState();
            Main.keyState = escapeDown ? new KeyboardState(Keys.Escape) : new KeyboardState();

            MethodInfo cacheScreen = typeof(PlayerInput).GetMethod("CacheOriginalScreenDimensions", flags)
                ?? throw new InvalidOperationException("native PlayerInput.CacheOriginalScreenDimensions method was not found");
            MethodInfo cache = typeof(PlayerInput).GetMethod("CacheOriginalInput", flags)
                ?? throw new InvalidOperationException("native PlayerInput.CacheOriginalInput method was not found");
            MethodInfo setUiZoom = typeof(PlayerInput).GetMethod("SetZoom_UI", flags)
                ?? throw new InvalidOperationException("native PlayerInput.SetZoom_UI method was not found");
            cacheScreen.Invoke(null, null);
            cache.Invoke(null, null);
            setUiZoom.Invoke(null, null);
            if (Math.Abs(Main.mouseX - logicalMouseX) > 1 || Math.Abs(Main.mouseY - logicalMouseY) > 1
                || Math.Abs(Main.screenWidth - logicalWidth) > 1 || Math.Abs(Main.screenHeight - logicalHeight) > 1)
                throw new InvalidOperationException($"native SetZoom_UI mapping mismatch: mouse={Main.mouseX},{Main.mouseY} screen={Main.screenWidth}x{Main.screenHeight}");
            ModContent.GetInstance<MilkcraftEnchantingSystem>().UpdateUI(new GameTime());
        }
        finally
        {
            Main.screenWidth = oldScreenWidth;
            Main.screenHeight = oldScreenHeight;
            Main.mouseX = oldMouseX;
            Main.mouseY = oldMouseY;
            Main.lastMouseX = oldLastMouseX;
            Main.lastMouseY = oldLastMouseY;
            Main.mouseLeft = oldMouseLeft;
            Main.keyState = oldKeys;
            Main.oldKeyState = oldOldKeys;
            foreach ((FieldInfo field, object value) in inputCache)
                field.SetValue(null, value);
        }
    }

    public static void Open()
    {
        Player player = Main.LocalPlayer;
        if (!player.active || player.dead || !HasNearbyTable(player))
            return;
        // The panel owns the click surface. Close the vanilla inventory before
        // it opens so one click cannot hit both inventory slots and this UI.
        Main.playerInventory = false;
        _visible = true;
        _selectedSlot = -1;
        _selectedKind = -1;
        _page = 0;
        _mouseWasDown = Main.mouseLeft;
    }

    public static void Close()
    {
        _visible = false;
        _selectedSlot = -1;
        _selectedKind = -1;
        _mouseWasDown = Main.mouseLeft;
        if (_ownsItemTooltip)
        {
            Main.HoverItem = new Item();
            Main.hoverItemName = string.Empty;
            _ownsItemTooltip = false;
        }
    }

    public override void OnWorldUnload() => Close();

    public override void ModifyInterfaceLayers(List<GameInterfaceLayer> layers)
    {
        int index = layers.FindIndex(layer => layer.Name == "Vanilla: Mouse Text");
        if (index < 0)
            index = layers.Count;
        layers.Insert(index, new LegacyGameInterfaceLayer("Milkcraft: Enchanting", DrawInterface, InterfaceScaleType.UI));
    }

    public override void UpdateUI(GameTime gameTime)
    {
        if (!_visible || Main.dedServ)
            return;
        Player player = Main.LocalPlayer;
        if (!player.active || player.dead || !HasNearbyTable(player))
        {
            Close();
            return;
        }
        // If the player opens the vanilla inventory while the station panel is
        // visible, return to the inventory instead of allowing both interfaces
        // to process the same mouse event.
        if (Main.playerInventory)
        {
            Close();
            return;
        }
        player.mouseInterface = true;
        bool escapePressed = _diagnosticEscapePressed ||
            (Main.keyState.IsKeyDown(Keys.Escape) && Main.oldKeyState.IsKeyUp(Keys.Escape));
        _diagnosticEscapePressed = false;
        if (escapePressed)
        {
            Close();
            return;
        }
        if (Main.mouseLeft && !_mouseWasDown)
            HandleClick(player);
        _mouseWasDown = Main.mouseLeft;
    }

    private static bool DrawInterface()
    {
        if (!_visible || Main.dedServ)
            return true;
        Player player = Main.LocalPlayer;
        player.mouseInterface = true;
        RefreshLayout();
        int logicalWidth = UiWidth;
        int logicalHeight = UiHeight;
        int left = (logicalWidth - WindowWidth) / 2;
        int top = (logicalHeight - WindowHeight) / 2;
        SpriteBatch batch = Main.spriteBatch;
        Texture2D pixel = TextureAssets.MagicPixel.Value;
        batch.Draw(pixel, TransformRect(new Rectangle(left, top, WindowWidth, WindowHeight)), new Color(28, 23, 43, 238));
        batch.Draw(pixel, TransformRect(new Rectangle(left, top, WindowWidth, 3)), new Color(176, 114, 229));
        DrawText("Mods.MilkcraftTerraria.Enchanting.Title", left + 18, top + 12, Color.White);
        bool hasSelectedItem = _selectedSlot >= 0 && _selectedSlot < player.inventory.Length
            && !player.inventory[_selectedSlot].IsAir
            && MilkcraftEnchantedItem.AllowedKinds(player.inventory[_selectedSlot]).Count > 0;
        bool sameEnchant = hasSelectedItem && _selectedKind > 0
            && player.inventory[_selectedSlot].GetGlobalItem<MilkcraftEnchantedItem>().Kind == (MilkcraftEnchant)_selectedKind;
        long cost = hasSelectedItem && !sameEnchant
            ? GetReforgeCost(player.inventory[_selectedSlot], player.discountAvailable)
            : 0L;
        bool canAfford = hasSelectedItem && (cost == 0L || player.CanAfford(cost));
        string costText = sameEnchant
            ? Language.GetTextValue("Mods.MilkcraftTerraria.Enchanting.NoCharge")
            : hasSelectedItem
                ? Language.GetTextValue("Mods.MilkcraftTerraria.Enchanting.Cost", FormatCoins(cost))
                : Language.GetTextValue("Mods.MilkcraftTerraria.Enchanting.CostSelectItem");
        DrawRaw(costText, left + 18, top + 42, new Color(226, 203, 247), 315f);
        string balanceKey = !hasSelectedItem
            ? "Mods.MilkcraftTerraria.Enchanting.SelectItem"
            : sameEnchant
                ? "Mods.MilkcraftTerraria.Enchanting.BalanceEnough"
                : canAfford
                    ? "Mods.MilkcraftTerraria.Enchanting.BalanceEnough"
                    : "Mods.MilkcraftTerraria.Enchanting.BalanceInsufficient";
        string balanceText = Language.GetTextValue(balanceKey);
        DrawRaw(balanceText, left + 18, top + 72, canAfford ? new Color(166, 231, 180) : new Color(242, 136, 136), 315f);
        DrawText("Mods.MilkcraftTerraria.Enchanting.ItemHeader", left + 18, top + 110, new Color(229, 213, 245));
        DrawText("Mods.MilkcraftTerraria.Enchanting.TypeHeader", left + 346, top + 110, new Color(229, 213, 245));

        List<int> eligible = EligibleSlots(player);
        int hoveredSlot = -1;
        Point mouseLogical = ((new Vector2(Main.mouseX, Main.mouseY) - _uiOffset) / _uiScale).ToPoint();
        int pageCount = Math.Max(1, (eligible.Count + ItemsPerPage - 1) / ItemsPerPage);
        _page = Math.Clamp(_page, 0, pageCount - 1);
        for (int row = 0; row < ItemsPerPage; row++)
        {
            int index = _page * ItemsPerPage + row;
            int y = top + 137 + row * 31;
            Rectangle logicalRect = new(left + 14, y, 310, 28);
            Rectangle rect = TransformRect(logicalRect);
            bool selected = index < eligible.Count && eligible[index] == _selectedSlot;
            batch.Draw(pixel, rect, selected ? new Color(91, 57, 126, 245) : new Color(50, 43, 65, 225));
            if (index < eligible.Count)
            {
                int slot = eligible[index];
                Item item = player.inventory[slot];
                Texture2D itemTexture = TextureAssets.Item[item.type].Value;
                Rectangle itemFrame = Main.itemAnimations[item.type]?.GetFrame(itemTexture) ?? itemTexture.Bounds;
                Vector2 iconOrigin = itemFrame.Size() * 0.5f;
                float iconScale = Math.Min(20f / Math.Max(1, itemFrame.Width), 20f / Math.Max(1, itemFrame.Height)) * _uiScale;
                Vector2 iconCenter = TransformPoint(logicalRect.X + 20, logicalRect.Y + 14);
                bool drawIcon = ItemLoader.PreDrawInInventory(item, batch, iconCenter, itemFrame, Color.White, Color.White, iconOrigin, iconScale);
                if (drawIcon)
                    batch.Draw(itemTexture, iconCenter, itemFrame, Color.White, 0f, iconOrigin, iconScale, SpriteEffects.None, 0f);
                ItemLoader.PostDrawInInventory(item, batch, iconCenter, itemFrame, Color.White, Color.White, iconOrigin, iconScale);
                string text = $"[{slot + 1}] {Lang.GetItemNameValue(item.type)} · {EnchantName(item.GetGlobalItem<MilkcraftEnchantedItem>().Kind)}";
                DrawRaw(TrimToWidth(text, 265f), logicalRect.X + 37, logicalRect.Y + 5, Color.White, 265f);
                if (logicalRect.Contains(mouseLogical))
                    hoveredSlot = slot;
            }
        }
        DrawRaw($"‹   {_page + 1}/{pageCount}   ›", left + 115, top + 389, Color.White);
        DrawRaw(Language.GetTextValue("Mods.MilkcraftTerraria.Enchanting.Instructions"), left + 18, top + 421, new Color(184, 174, 198), 315f);

        if (hoveredSlot >= 0)
        {
            Item hovered = player.inventory[hoveredSlot];
            Main.HoverItem = hovered.Clone();
            Main.hoverItemName = hovered.HoverName;
            _ownsItemTooltip = true;
        }
        else if (_ownsItemTooltip)
        {
            Main.HoverItem = new Item();
            Main.hoverItemName = string.Empty;
            _ownsItemTooltip = false;
        }

        if (_selectedSlot >= 0 && _selectedSlot < player.inventory.Length)
        {
            Item selectedItem = player.inventory[_selectedSlot];
            List<MilkcraftEnchant> kinds = MilkcraftEnchantedItem.AllowedKinds(selectedItem);
            for (int index = 0; index < kinds.Count; index++)
            {
                int column = index % 2;
                int row = index / 2;
                Rectangle logicalRect = new(left + 344 + column * 160, top + 137 + row * 48, 151, 40);
                Rectangle rect = TransformRect(logicalRect);
                bool selected = (int)kinds[index] == _selectedKind;
                batch.Draw(pixel, rect, selected ? new Color(93, 59, 131, 245) : new Color(50, 43, 65, 225));
                DrawRaw(EnchantName(kinds[index]), logicalRect.X + 9, logicalRect.Y + 12, Color.White);
            }
            string current = EnchantName(selectedItem.GetGlobalItem<MilkcraftEnchantedItem>().Kind);
            DrawText("Mods.MilkcraftTerraria.Enchanting.Current", left + 346, top + 353, Color.White, current);
            if (_selectedKind > 0)
            {
                string effect = Language.GetTextValue($"Mods.MilkcraftTerraria.Enchanting.Tooltip.{(MilkcraftEnchant)_selectedKind}");
                Terraria.UI.Chat.ChatManager.DrawColorCodedStringWithShadow(batch, FontAssets.MouseText.Value,
                    effect, TransformPoint(left + 346, top + 380), new Color(225, 206, 242), 0f,
                    Vector2.Zero, new Vector2(_uiScale), 315f * _uiScale, 2f * _uiScale);
            }
        }
        else
        {
            DrawText("Mods.MilkcraftTerraria.Enchanting.SelectItem", left + 350, top + 145, new Color(180, 170, 193));
        }

        DrawButton("Mods.MilkcraftTerraria.Enchanting.Apply", TransformRect(new Rectangle(left + 421, top + 474, 145, 32)), new Color(84, 117, 99));
        DrawButton("Mods.MilkcraftTerraria.Enchanting.Close", TransformRect(new Rectangle(left + 574, top + 474, 98, 32)), new Color(92, 67, 74));
        return true;
    }

    private static void HandleClick(Player player)
    {
        RefreshLayout();
        int logicalWidth = UiWidth;
        int logicalHeight = UiHeight;
        int left = (logicalWidth - WindowWidth) / 2;
        int top = (logicalHeight - WindowHeight) / 2;
        // Main.mouseX/Y are already converted by PlayerInput.SetZoom_UI before
        // ModSystem.UpdateUI runs. Only undo our fit scale and offset here.
        Point mouse = ((new Vector2(Main.mouseX, Main.mouseY) - _uiOffset) / _uiScale).ToPoint();
        if (new Rectangle(left + 574, top + 474, 98, 32).Contains(mouse))
        {
            Close();
            return;
        }
        if (new Rectangle(left + 421, top + 474, 145, 32).Contains(mouse))
        {
            Submit(player);
            return;
        }

        List<int> eligible = EligibleSlots(player);
        int pageCount = Math.Max(1, (eligible.Count + ItemsPerPage - 1) / ItemsPerPage);
        if (new Rectangle(left + 105, top + 385, 125, 30).Contains(mouse))
        {
            if (mouse.X < left + 140) _page = (_page - 1 + pageCount) % pageCount;
            else _page = (_page + 1) % pageCount;
            return;
        }
        for (int row = 0; row < ItemsPerPage; row++)
        {
            Rectangle rect = new(left + 14, top + 137 + row * 31, 310, 28);
            int index = _page * ItemsPerPage + row;
            if (!rect.Contains(mouse) || index >= eligible.Count)
                continue;
            _selectedSlot = eligible[index];
            _selectedKind = -1;
            return;
        }
        if (_selectedSlot < 0 || _selectedSlot >= player.inventory.Length)
            return;
        List<MilkcraftEnchant> kinds = MilkcraftEnchantedItem.AllowedKinds(player.inventory[_selectedSlot]);
        for (int index = 0; index < kinds.Count; index++)
        {
            int column = index % 2;
            int row = index / 2;
            Rectangle rect = new(left + 344 + column * 160, top + 137 + row * 48, 151, 40);
            if (rect.Contains(mouse))
            {
                _selectedKind = (int)kinds[index];
                return;
            }
        }
    }

    private static void Submit(Player player)
    {
        if (_selectedSlot < 0 || _selectedSlot >= player.inventory.Length || _selectedKind <= 0)
            return;
        Item item = player.inventory[_selectedSlot];
        MilkcraftEnchantedItem enchant = item.GetGlobalItem<MilkcraftEnchantedItem>();
        MilkcraftEnchant next = (MilkcraftEnchant)_selectedKind;
        if (!MilkcraftEnchantedItem.AllowedKinds(item).Contains(next))
            return;
        if (enchant.Kind == next)
        {
            Main.NewText(Language.GetTextValue("Mods.MilkcraftTerraria.Enchanting.AlreadyApplied"), 191, 160, 228);
            return;
        }
        long price = GetReforgeCost(item, player.discountAvailable);
        if (!player.CanAfford(price))
        {
            Main.NewText(Language.GetTextValue("Mods.MilkcraftTerraria.Enchanting.BalanceInsufficient"), Color.OrangeRed);
            return;
        }
        if (Main.netMode == NetmodeID.SinglePlayer)
        {
            bool applied = ApplyRequest(player, _selectedSlot, item.type, (byte)item.prefix, (byte)enchant.Kind, (byte)next);
            Main.NewText(Language.GetTextValue(applied
                ? "Mods.MilkcraftTerraria.Enchanting.Applied"
                : "Mods.MilkcraftTerraria.Enchanting.ApplyFailed"), applied ? Color.LightGreen : Color.OrangeRed);
            return;
        }
        ModPacket packet = ModContent.GetInstance<MilkcraftTerrariaMod>().GetPacket();
        packet.Write(MilkcraftTerrariaMod.EnchantApplyRequestMessage);
        packet.Write((byte)_selectedSlot);
        packet.Write(item.type);
        packet.Write((byte)item.prefix);
        packet.Write((byte)enchant.Kind);
        packet.Write((byte)next);
        packet.Send();
    }

    internal static bool HandleApplyRequest(Player player, byte slot, int itemType, byte prefix, byte oldKind, byte newKind)
        => ApplyRequest(player, slot, itemType, prefix, oldKind, newKind);

    internal static bool DiagnosticApplyRequest(Player player, int slot, int itemType, byte prefix, byte oldKind, byte newKind)
        => ApplyRequest(player, slot, itemType, prefix, oldKind, newKind, syncNetwork: false);

    private static bool ApplyRequest(Player player, int slot, int itemType, byte prefix, byte oldKind, byte newKind, bool syncNetwork = true)
    {
        if (Main.netMode == NetmodeID.MultiplayerClient || player is null || !player.active || player.dead
            || slot < 0 || slot >= player.inventory.Length || !HasNearbyTable(player))
            return false;
        Item item = player.inventory[slot];
        if (item.IsAir || item.type != itemType || item.prefix != prefix)
            return false;
        MilkcraftEnchantedItem data = item.GetGlobalItem<MilkcraftEnchantedItem>();
        if ((byte)data.Kind != oldKind || newKind <= (byte)MilkcraftEnchant.None || newKind > (byte)MilkcraftEnchant.Explosive)
            return false;
        MilkcraftEnchant kind = (MilkcraftEnchant)newKind;
        if (!MilkcraftEnchantedItem.AllowedKinds(item).Contains(kind))
            return false;
        if (data.Kind == kind)
            return true;
        long cost = GetReforgeCost(item, player.discountAvailable);
        if (cost <= 0L || !player.CanAfford(cost))
            return false;
        var before = new (int Type, int Stack)[player.inventory.Length];
        for (int i = 0; i < player.inventory.Length; i++)
            before[i] = (player.inventory[i].type, player.inventory[i].stack);
        BankSnapshot bankBefore = Main.netMode == NetmodeID.Server ? BankSnapshot.Capture(player) : default;
        if (!player.BuyItem(cost))
            return false;
        data.Kind = kind;
        if (Main.netMode == NetmodeID.Server && syncNetwork)
        {
            for (int i = 0; i < player.inventory.Length; i++)
                if (before[i].Type != player.inventory[i].type || before[i].Stack != player.inventory[i].stack || i == slot)
                    NetMessage.SendData(MessageID.SyncEquipment, -1, -1, null, player.whoAmI, i);
            if (bankBefore is not null)
                SendChangedBankSlots(player, bankBefore);
        }
        return true;
    }

    internal static long GetReforgeCost(Item item, bool discount)
    {
        if (item is null || item.IsAir)
            return 0L;
        // Local tModLoader ReforgePrice docs: start with Item.value, apply 20% discount when eligible, then divide by three.
        long value = Math.Max(0, item.value);
        long discountedValue = discount ? value * 4L / 5L : value;
        return Math.Max(1L, discountedValue / 3L);
    }

    internal static string FormatCoins(long copper)
    {
        copper = Math.Max(0L, copper);
        long platinum = copper / 1_000_000L;
        long gold = (copper / 10_000L) % 100L;
        long silver = (copper / 100L) % 100L;
        long copperCoins = copper % 100L;
        List<string> parts = new();
        if (platinum > 0) parts.Add(Language.GetTextValue("Mods.MilkcraftTerraria.Enchanting.CoinPlatinum", platinum));
        if (gold > 0) parts.Add(Language.GetTextValue("Mods.MilkcraftTerraria.Enchanting.CoinGold", gold));
        if (silver > 0) parts.Add(Language.GetTextValue("Mods.MilkcraftTerraria.Enchanting.CoinSilver", silver));
        if (copperCoins > 0 || parts.Count == 0) parts.Add(Language.GetTextValue("Mods.MilkcraftTerraria.Enchanting.CoinCopper", copperCoins));
        return string.Join(Language.GetTextValue("Mods.MilkcraftTerraria.Enchanting.CoinSeparator"), parts);
    }

    private sealed class BankSnapshot
    {
        public Item[][] Banks = Array.Empty<Item[]>();

        public static BankSnapshot Capture(Player player)
        {
            Item[][] banks = { player.bank.item, player.bank2.item, player.bank3.item, player.bank4.item };
            return new BankSnapshot { Banks = banks.Select(items => items.Select(item => item.Clone()).ToArray()).ToArray() };
        }
    }

    private static void SendChangedBankSlots(Player player, BankSnapshot before)
    {
        Item[][] after = { player.bank.item, player.bank2.item, player.bank3.item, player.bank4.item };
        List<(byte Bank, byte Slot, int Type, int Stack)> changes = new();
        for (int bank = 0; bank < after.Length; bank++)
        for (int slot = 0; slot < after[bank].Length; slot++)
        {
            Item oldItem = before.Banks[bank][slot];
            Item newItem = after[bank][slot];
            if (oldItem.type != newItem.type || oldItem.stack != newItem.stack)
            {
                if (!IsCoin(oldItem.type) && !IsCoin(newItem.type))
                    throw new InvalidOperationException("coin payment unexpectedly changed a non-coin bank slot");
                changes.Add(((byte)bank, (byte)slot, newItem.IsAir ? ItemID.None : newItem.type, newItem.IsAir ? 0 : newItem.stack));
            }
        }
        if (changes.Count == 0)
            return;
        ModPacket packet = ModContent.GetInstance<MilkcraftTerrariaMod>().GetPacket();
        packet.Write(MilkcraftTerrariaMod.EnchantBankSyncMessage);
        packet.Write((byte)changes.Count);
        foreach ((byte bank, byte slot, int type, int stack) in changes)
        {
            packet.Write(bank);
            packet.Write(slot);
            packet.Write(type);
            packet.Write(stack);
        }
        packet.Send(player.whoAmI);
    }

    internal static void HandleBankSync(BinaryReader reader)
    {
        byte changeCount = reader.ReadByte();
        Player player = Main.LocalPlayer;
        Item[][] banks = { player.bank.item, player.bank2.item, player.bank3.item, player.bank4.item };
        for (int i = 0; i < changeCount; i++)
        {
            int bank = reader.ReadByte();
            int slot = reader.ReadByte();
            int type = reader.ReadInt32();
            int stack = reader.ReadInt32();
            if (bank >= banks.Length || slot >= banks[bank].Length)
                continue;
            if (type != ItemID.None && !IsCoin(type))
                continue;
            Item item = banks[bank][slot];
            if (type <= ItemID.None || stack <= 0)
                item.TurnToAir();
            else
            {
                if (item.type != type)
                    item.SetDefaults(type);
                item.stack = stack;
            }
        }
    }

    private static bool IsCoin(int type)
        => type == ItemID.CopperCoin || type == ItemID.SilverCoin || type == ItemID.GoldCoin || type == ItemID.PlatinumCoin;

    private static List<int> EligibleSlots(Player player)
    {
        List<int> slots = new();
        for (int i = 0; i < player.inventory.Length; i++)
            if (MilkcraftEnchantedItem.AllowedKinds(player.inventory[i]).Count > 0)
                slots.Add(i);
        return slots;
    }

    public static bool HasNearbyTable(Player player)
    {
        if (player is null || !player.active || player.dead)
            return false;
        int type = ModContent.TileType<MilkcraftEnchantingTableTile>();
        int minX = (int)(player.position.X / 16f) - Player.tileRangeX;
        int maxX = (int)((player.position.X + player.width) / 16f) + Player.tileRangeX;
        int minY = (int)(player.position.Y / 16f) - Player.tileRangeY;
        int maxY = (int)((player.position.Y + player.height) / 16f) + Player.tileRangeY;
        for (int x = minX; x <= maxX; x++)
        for (int y = minY; y <= maxY; y++)
        {
            if (!WorldGen.InWorld(x, y, 1))
                continue;
            Tile tile = Main.tile[x, y];
            if (!tile.HasTile || tile.TileType != type)
                continue;
            int localX = (tile.TileFrameX / 18) % 3;
            int localY = (tile.TileFrameY / 18) % 3;
            int originX = x - localX;
            int originY = y - localY;
            if (!MilkcraftAlchemyWorldSystem.IsInNativeAdjTileRange(player, new Point16(originX, originY)))
                continue;
            bool complete = true;
            for (int dx = 0; dx < 3 && complete; dx++)
            for (int dy = 0; dy < 3; dy++)
                if (!Main.tile[originX + dx, originY + dy].HasTile || Main.tile[originX + dx, originY + dy].TileType != type)
                {
                    complete = false;
                    break;
                }
            if (complete)
                return true;
        }
        return false;
    }

    private static void DrawButton(string key, Rectangle rect, Color color)
    {
        Main.spriteBatch.Draw(TextureAssets.MagicPixel.Value, rect, color);
        Vector2 point = new(rect.X + 11 * _uiScale, rect.Y + 8 * _uiScale);
        Terraria.UI.Chat.ChatManager.DrawColorCodedStringWithShadow(Main.spriteBatch, FontAssets.MouseText.Value,
            Language.GetTextValue(key), point, Color.White, 0f, Vector2.Zero, new Vector2(_uiScale), 650f * _uiScale, _uiScale);
    }

    private static void DrawText(string key, int x, int y, Color color, params object[] args)
        => DrawRaw(Language.GetTextValue(key, args), x, y, color);

    private static void DrawRaw(string text, int x, int y, Color color, float maxWidth = 650f)
        => Terraria.UI.Chat.ChatManager.DrawColorCodedStringWithShadow(Main.spriteBatch, FontAssets.MouseText.Value,
            text, TransformPoint(x, y), color, 0f, Vector2.Zero, new Vector2(_uiScale), maxWidth * _uiScale, _uiScale);

    private static void RefreshLayout() => ComputeLayout(UiWidth, UiHeight, out _uiScale, out _uiOffset);

    private static string TrimToWidth(string text, float maxWidth)
    {
        var font = FontAssets.MouseText.Value;
        if (font.MeasureString(text).X <= maxWidth)
            return text;
        const string ellipsis = "…";
        int length = text.Length;
        while (length > 0 && font.MeasureString(text[..length] + ellipsis).X > maxWidth)
            length--;
        return length == 0 ? ellipsis : text[..length] + ellipsis;
    }

    private static void ComputeLayout(int logicalWidth, int logicalHeight, out float scale, out Vector2 offset)
    {
        scale = Math.Min(1f, Math.Min((logicalWidth - 24f) / WindowWidth, (logicalHeight - 24f) / WindowHeight));
        scale = Math.Max(0.5f, scale);
        offset = new Vector2((logicalWidth - logicalWidth * scale) * 0.5f,
            (logicalHeight - logicalHeight * scale) * 0.5f);
    }

    private static Vector2 TransformPoint(float x, float y) => _uiOffset + new Vector2(x, y) * _uiScale;

    private static Rectangle TransformRect(Rectangle rect) => TransformRect(rect, _uiOffset, _uiScale);

    private static Rectangle TransformRect(Rectangle rect, Vector2 offset, float scale)
    {
        Vector2 position = offset + new Vector2(rect.X, rect.Y) * scale;
        return new Rectangle((int)MathF.Round(position.X), (int)MathF.Round(position.Y),
            (int)MathF.Round(rect.Width * scale), (int)MathF.Round(rect.Height * scale));
    }

    private static string EnchantName(MilkcraftEnchant kind)
        => kind == MilkcraftEnchant.None ? Language.GetTextValue("Mods.MilkcraftTerraria.Enchanting.None")
            : Language.GetTextValue($"Mods.MilkcraftTerraria.Enchanting.Types.{kind}");
}

public sealed class MilkcraftEnchantedItem : GlobalItem
{
    public override bool InstancePerEntity => true;
    public MilkcraftEnchant Kind;

    public override GlobalItem Clone(Item from, Item to)
    {
        var clone = (MilkcraftEnchantedItem)base.Clone(from, to);
        clone.Kind = Kind;
        return clone;
    }

    public override void SaveData(Item item, Terraria.ModLoader.IO.TagCompound tag)
    {
        if (Kind != MilkcraftEnchant.None)
            tag["MilkcraftEnchant"] = (byte)Kind;
    }

    public override void LoadData(Item item, Terraria.ModLoader.IO.TagCompound tag)
        => Kind = tag.ContainsKey("MilkcraftEnchant") ? (MilkcraftEnchant)tag.GetByte("MilkcraftEnchant") : MilkcraftEnchant.None;

    public override void NetSend(Item item, BinaryWriter writer) => writer.Write((byte)Kind);
    public override void NetReceive(Item item, BinaryReader reader) => Kind = (MilkcraftEnchant)reader.ReadByte();

    public override bool CanStack(Item destination, Item source)
        => destination.GetGlobalItem<MilkcraftEnchantedItem>().Kind == source.GetGlobalItem<MilkcraftEnchantedItem>().Kind;

    public override void ModifyTooltips(Item item, List<TooltipLine> tooltips)
    {
        if (Kind == MilkcraftEnchant.None)
            return;
        string value = Language.GetTextValue($"Mods.MilkcraftTerraria.Enchanting.Tooltip.{Kind}");
        tooltips.Add(new TooltipLine(Mod, "MilkcraftEnchant", $"✦ {EnchantLabel(Kind)} · {value}") { OverrideColor = new Color(194, 124, 255) });
    }

    public override void ModifyWeaponDamage(Item item, Player player, ref StatModifier damage)
    {
        if (Kind == MilkcraftEnchant.Sharpness && IsSword(item))
            damage *= 1.06f;
        else if (Kind == MilkcraftEnchant.Power && IsBow(item))
            damage *= 1.06f;
        else if (Kind == MilkcraftEnchant.Spell && IsStaff(item))
            damage *= 1.06f;
    }

    public override void ModifyWeaponCrit(Item item, Player player, ref float crit)
    {
        if (Kind == MilkcraftEnchant.Critical && (IsSword(item) || IsBow(item) || IsStaff(item)))
            crit += 4f;
    }

    public override void UpdateInventory(Item item, Player player)
    {
        if (player.HeldItem != item)
            return;
        if (Kind == MilkcraftEnchant.Provocation && IsSword(item))
        {
            player.aggro += 400;
            player.statDefense += 4;
        }
        else if (Kind == MilkcraftEnchant.Stealth && (IsBow(item) || IsStaff(item)))
        {
            player.aggro -= 400;
            player.moveSpeed += 0.04f;
        }
    }

    public override void UpdateAccessory(Item item, Player player, bool hideVisual)
    {
        if (!IsAccessory(item))
            return;
        if (Kind == MilkcraftEnchant.Power && IsQuiver(item))
            player.GetDamage(DamageClass.Ranged) += 0.06f;
        else if (Kind == MilkcraftEnchant.Spell && IsTome(item))
            player.GetDamage(DamageClass.Magic) += 0.06f;
        else if (Kind == MilkcraftEnchant.Critical)
        {
            if (IsQuiver(item)) player.GetCritChance(DamageClass.Ranged) += 4f;
            else if (IsTome(item)) player.GetCritChance(DamageClass.Magic) += 4f;
            else if (IsShield(item)) player.GetCritChance(DamageClass.Melee) += 4f;
        }
        else if (Kind == MilkcraftEnchant.ShieldWall && IsShield(item))
            player.endurance += 0.04f;
        else if (Kind == MilkcraftEnchant.Armor)
            player.statDefense += 4;
        else if (Kind == MilkcraftEnchant.Resistance)
            player.GetModPlayer<MilkcraftEnchantedPlayer>().ProjectileResistance = true;
    }

    public static List<MilkcraftEnchant> AllowedKinds(Item item)
    {
        List<MilkcraftEnchant> result = new();
        if (item is null || item.IsAir || item.ModItem is null)
            return result;
        if (IsSword(item)) result.AddRange(new[] { MilkcraftEnchant.Sharpness, MilkcraftEnchant.Provocation });
        if (IsBow(item)) result.AddRange(new[] { MilkcraftEnchant.Power, MilkcraftEnchant.Stealth, MilkcraftEnchant.Explosive });
        if (IsStaff(item)) result.AddRange(new[] { MilkcraftEnchant.Spell, MilkcraftEnchant.Stealth, MilkcraftEnchant.Ice, MilkcraftEnchant.Healing, MilkcraftEnchant.Explosive });
        if (IsShield(item)) result.AddRange(new[] { MilkcraftEnchant.Critical, MilkcraftEnchant.ShieldWall, MilkcraftEnchant.Armor, MilkcraftEnchant.Resistance });
        if (IsQuiver(item)) result.AddRange(new[] { MilkcraftEnchant.Power, MilkcraftEnchant.Critical, MilkcraftEnchant.Armor, MilkcraftEnchant.Resistance });
        if (IsTome(item)) result.AddRange(new[] { MilkcraftEnchant.Spell, MilkcraftEnchant.Critical, MilkcraftEnchant.Armor, MilkcraftEnchant.Resistance });
        return result;
    }

    public static bool IsSword(Item item) => NameIs(item, "IronSword", "KnightSword", "TemplarSword", "SoulforgedSword", "DazhouIronSword");
    public static bool IsBow(Item item) => NameIs(item, "IronBow", "HuntingBow", "NightmareBow", "AbyssBow");
    public static bool IsStaff(Item item) => NameIs(item, "IronStaff", "EnergyStaff", "FallenStaff", "DarkStaff");
    public static bool IsShield(Item item) => NameIs(item, "IronShield", "KnightBulwark", "TemplarShield", "SoulforgedShield");
    public static bool IsQuiver(Item item) => NameIs(item, "IronQuiver", "HuntingQuiver", "NightmareQuiver", "AbyssQuiver");
    public static bool IsTome(Item item) => NameIs(item, "BeginnerTome", "EnergyTome", "FallenTome", "DarkTome");
    private static bool IsAccessory(Item item) => IsShield(item) || IsQuiver(item) || IsTome(item);
    private static bool NameIs(Item item, params string[] names)
        => item?.ModItem is not null && item.ModItem.Mod?.Name == "MilkcraftTerraria"
            && Array.IndexOf(names, item.ModItem.GetType().Name) >= 0;
    private static string EnchantLabel(MilkcraftEnchant kind) => Language.GetTextValue($"Mods.MilkcraftTerraria.Enchanting.Types.{kind}");
}

public sealed class MilkcraftEnchantedPlayer : ModPlayer
{
    public bool ProjectileResistance;
    public int HealingCooldown;
    public int ExplosiveCooldown;
    public bool ExplosiveProcActive;

    public override void ResetEffects() => ProjectileResistance = false;
    public override void PostUpdate()
    {
        if (HealingCooldown > 0) HealingCooldown--;
        if (ExplosiveCooldown > 0) ExplosiveCooldown--;
    }
}

public sealed class MilkcraftEnchantedProjectile : GlobalProjectile
{
    public override bool InstancePerEntity => true;
    private MilkcraftEnchant _kind;
    private byte _sourceItemPrefix;
    internal MilkcraftEnchant DiagnosticKind => _kind;

    public override void ModifyHitPlayer(Projectile projectile, Player target, ref Player.HurtModifiers modifiers)
    {
        // PlayerDeathReason.TryGetCausingEntity can resolve hostile owner-255
        // projectiles as Player[255]. Here the engine supplies the actual
        // projectile directly, so resistance only affects projectile hits.
        if (target.GetModPlayer<MilkcraftEnchantedPlayer>().ProjectileResistance)
            modifiers.FinalDamage *= 0.94f;
    }

    public override void OnSpawn(Projectile projectile, IEntitySource source)
    {
        if (source is not EntitySource_ItemUse itemSource || itemSource.Item is null)
            return;
        Item item = itemSource.Item;
        MilkcraftEnchant kind = item.GetGlobalItem<MilkcraftEnchantedItem>().Kind;
        if (kind is not (MilkcraftEnchant.Ice or MilkcraftEnchant.Healing or MilkcraftEnchant.Explosive))
            return;
        if (!(MilkcraftEnchantedItem.IsStaff(item) || (kind == MilkcraftEnchant.Explosive && MilkcraftEnchantedItem.IsBow(item))))
            return;
        _kind = kind;
        _sourceItemPrefix = (byte)item.prefix;
    }

    public override void SendExtraAI(Projectile projectile, BitWriter bitWriter, BinaryWriter binaryWriter)
    {
        bitWriter.WriteBit(_kind != MilkcraftEnchant.None);
        if (_kind != MilkcraftEnchant.None)
        {
            binaryWriter.Write((byte)_kind);
            binaryWriter.Write(_sourceItemPrefix);
        }
    }

    public override void ReceiveExtraAI(Projectile projectile, BitReader bitReader, BinaryReader binaryReader)
    {
        _kind = bitReader.ReadBit() ? (MilkcraftEnchant)binaryReader.ReadByte() : MilkcraftEnchant.None;
        _sourceItemPrefix = _kind == MilkcraftEnchant.None ? (byte)0 : binaryReader.ReadByte();
    }

    public override void OnHitNPC(Projectile projectile, NPC target, NPC.HitInfo hit, int damageDone)
    {
        if (_kind == MilkcraftEnchant.None || damageDone <= 0 || target.friendly || target.townNPC
            || target.type == NPCID.TargetDummy || target.lifeMax <= 5)
            return;
        // Ice needs a living target to carry its slow; Healing is awarded for
        // any successful damaging hit, including the lethal hit. Explosive
        // similarly resolves after CheckDead on a killing blow.
        if (_kind == MilkcraftEnchant.Ice
            && (!target.active || target.life <= 0 || target.dontTakeDamage || target.immortal))
            return;
        if (_kind == MilkcraftEnchant.Healing && (target.dontTakeDamage || target.immortal)) return;
        // Friendly projectile hit hooks run on the owning client or on the
        // server, depending on who owns the projectile. Only reject remote
        // clients; rejecting every client would disable owner-authoritative
        // Ice/Healing/Explosive effects in multiplayer.
        if (Main.netMode == NetmodeID.MultiplayerClient && projectile.owner != Main.myPlayer)
            return;
        if (projectile.owner < 0 || projectile.owner >= Main.maxPlayers)
            return;
        Player player = Main.player[projectile.owner];
        if (!player.active)
            return;
        if (_kind == MilkcraftEnchant.Ice)
        {
            int frostType = ModContent.BuffType<MilkcraftFrosted>();
            if (target.boss || target.immortal || target.buffImmune[frostType])
                return;
            target.AddBuff(frostType, 120);
            if (!Main.dedServ)
            {
                for (int i = 0; i < 3; i++)
                {
                    Vector2 point = target.Center + Main.rand.NextVector2Circular(target.width * 0.22f, target.height * 0.22f);
                    Dust dust = Dust.NewDustPerfect(point, DustID.IceTorch, Main.rand.NextVector2Circular(1.1f, 1.1f), 0, new Color(140, 220, 255), 0.75f);
                    dust.noGravity = true;
                }
            }
        }
        else if (_kind == MilkcraftEnchant.Healing)
        {
            MilkcraftEnchantedPlayer state = player.GetModPlayer<MilkcraftEnchantedPlayer>();
            if (state.HealingCooldown == 0 && player.lifeSteal >= 1f && player.statLife < player.statLifeMax2)
            {
                state.HealingCooldown = 120;
                player.lifeSteal -= 1f;
                player.Heal(2);
                if (!Main.dedServ)
                {
                    for (int i = 0; i < 3; i++)
                    {
                        Vector2 point = player.Center + Main.rand.NextVector2Circular(14f, 18f);
                        Dust dust = Dust.NewDustPerfect(point, DustID.GreenTorch, Main.rand.NextVector2Circular(0.7f, 0.7f), 0, new Color(150, 225, 150), 0.7f);
                        dust.noGravity = true;
                    }
                }
            }
        }
        else if (_kind == MilkcraftEnchant.Explosive)
        {
            MilkcraftEnchantedPlayer state = player.GetModPlayer<MilkcraftEnchantedPlayer>();
            if (state.ExplosiveCooldown > 0 || state.ExplosiveProcActive)
                return;
            state.ExplosiveCooldown = 30;
            state.ExplosiveProcActive = true;
            try
            {
                int areaDamage = Math.Max(1, (int)(damageDone * 0.30f));
                foreach (NPC other in Main.npc)
                {
                    if (!other.active || other.whoAmI == target.whoAmI || other.friendly || other.townNPC
                        || other.type == NPCID.TargetDummy || other.lifeMax <= 5 || other.life <= 0
                        || other.dontTakeDamage || other.immortal)
                        continue;
                    if (Vector2.DistanceSquared(other.Center, target.Center) > 48f * 48f
                        || !CanExplosiveSplashSeeHitbox(target.Center, other.Hitbox))
                        continue;
                    NPC.HitInfo areaHit = new()
                    {
                        Damage = areaDamage,
                        Knockback = 0f,
                        HitDirection = Math.Sign(other.Center.X - target.Center.X),
                        Crit = false
                    };
                    other.StrikeNPC(areaHit);
                    if (Main.netMode != NetmodeID.SinglePlayer)
                        NetMessage.SendStrikeNPC(other, in areaHit, projectile.owner);
                }
                if (!Main.dedServ)
                {
                    // Keep the proc legible on a lone boss: the persistent,
                    // harmless visual ring lasts 12 frames while these native
                    // particles provide a short radial burst.
                    Projectile.NewProjectile(new EntitySource_Misc("MilkcraftExplosiveEnchant"), target.Center,
                        Vector2.Zero, ModContent.ProjectileType<MilkcraftEnchantExplosionVfx>(), 0, 0f, projectile.owner);
                    Lighting.AddLight(target.Center, 0.48f, 0.25f, 0.07f);
                    for (int i = 0; i < 14; i++)
                    {
                        float angle = MathHelper.TwoPi * i / 14f;
                        Vector2 velocity = angle.ToRotationVector2() * (2.5f + (i % 3) * 0.55f);
                        Dust dust = Dust.NewDustPerfect(target.Center + angle.ToRotationVector2() * 8f, DustID.GoldFlame, velocity,
                            0, new Color(255, 205, 125), 0.95f);
                        dust.noGravity = true;
                    }
                }
            }
            finally { state.ExplosiveProcActive = false; }
        }
    }

    internal static bool CanExplosiveSplashSeeHitbox(Vector2 origin, Rectangle targetHitbox)
    {
        Vector2[] originSamples =
        {
            origin,
            new Vector2(origin.X, origin.Y - 8f),
            new Vector2(origin.X, origin.Y + 8f),
            new Vector2(origin.X - 8f, origin.Y),
            new Vector2(origin.X + 8f, origin.Y)
        };
        Vector2[] targetSamples =
        {
            new(targetHitbox.Center.X, targetHitbox.Top + 2),
            new(targetHitbox.Left + 2, targetHitbox.Center.Y),
            new(targetHitbox.Right - 2, targetHitbox.Center.Y),
            new(targetHitbox.Center.X, targetHitbox.Center.Y)
        };
        foreach (Vector2 from in originSamples)
        foreach (Vector2 to in targetSamples)
            if (Collision.CanHitLine(from, 1, 1, to, 1, 1))
                return true;
        return false;
    }
}

/// <summary>Purely visual, non-damaging flash for a successful explosive enchant proc.</summary>
public sealed class MilkcraftEnchantExplosionVfx : ModProjectile
{
    internal static int DiagnosticLightCalls { get; private set; }
    internal static Vector3 DiagnosticLastLight { get; private set; }
    internal static void ResetDiagnosticLight() { DiagnosticLightCalls = 0; DiagnosticLastLight = Vector3.Zero; }

    public override string Texture => "Terraria/Images/MagicPixel";

    public override void SetDefaults()
    {
        Projectile.width = 2;
        Projectile.height = 2;
        Projectile.timeLeft = 12;
        Projectile.penetrate = -1;
        Projectile.tileCollide = false;
        Projectile.ignoreWater = true;
        Projectile.friendly = false;
        Projectile.hostile = false;
        Projectile.netImportant = true;
    }

    public override bool? CanDamage() => false;

    public override void AI()
    {
        float progress = 1f - Projectile.timeLeft / 12f;
        float strength = (1f - progress) * 0.8f;
        Vector3 color = new(0.48f * strength, 0.25f * strength, 0.07f * strength);
        DiagnosticLightCalls++;
        DiagnosticLastLight = color;
        Lighting.AddLight(Projectile.Center, color);
    }

    public override bool PreDraw(ref Color lightColor)
    {
        Texture2D pixel = TextureAssets.MagicPixel.Value;
        Vector2 center = Projectile.Center - Main.screenPosition;
        float progress = MathHelper.Clamp(1f - Projectile.timeLeft / 12f, 0f, 1f);
        float fade = MathF.Pow(1f - progress, 0.62f);
        float radius = MathHelper.Lerp(8f, 42f, progress);
        Color gold = new Color(255, 184, 76) * (fade * 0.9f);
        Color pale = new Color(255, 238, 184) * fade;

        // Expanding segmented ring plus radial sparks; the MagicPixel is
        // premultiplied by SpriteBatch and the underlying projectile cannot hit.
        for (int i = 0; i < 20; i++)
        {
            float angle = MathHelper.TwoPi * i / 20f + progress * 0.12f;
            float segmentLength = Math.Max(2f, (i % 5 == 0 ? 11f : 8f) * fade);
            Vector2 ringPoint = center + angle.ToRotationVector2() * radius;
            Main.spriteBatch.Draw(pixel, ringPoint, new Rectangle(0, 0, 1, 1), i % 5 == 0 ? pale : gold, angle + MathHelper.PiOver2,
                new Vector2(0.5f, 0.5f), new Vector2(segmentLength, Math.Max(1f, (i % 5 == 0 ? 2.8f : 2.2f) * fade)),
                SpriteEffects.None, 0f);
        }
        // A short center flash and eight outward rays make the proc readable
        // even when the target is a single large boss with no splash target.
        for (int i = 0; i < 8; i++)
        {
            float angle = MathHelper.TwoPi * i / 8f;
            float rayLength = Math.Max(2f, (13f * (1f - progress) + 3f) * fade);
            Vector2 start = center + angle.ToRotationVector2() * 4f;
            Main.spriteBatch.Draw(pixel, start, new Rectangle(0, 0, 1, 1), pale, angle,
                new Vector2(0f, 0.5f), new Vector2(rayLength, Math.Max(1f, 2.4f * fade)), SpriteEffects.None, 0f);
        }
        PassiveVfxTextureSystem.DrawProjectileGlow(Main.spriteBatch, center, new Color(255, 196, 90),
            1.1f, fade * 0.58f);
        float core = Math.Max(0.01f, 15f * (1f - progress * 0.82f));
        Main.spriteBatch.Draw(pixel, center, new Rectangle(0, 0, 1, 1), pale, 0f, new Vector2(0.5f, 0.5f),
            new Vector2(core, core), SpriteEffects.None, 0f);
        return false;
    }
}

/// <summary>Visible Chilled-style icon with a deterministic 20% NPC movement reduction.</summary>
public sealed class MilkcraftFrosted : ModBuff
{
    public override string Texture => $"Terraria/Images/Buff_{BuffID.Chilled}";
    public override void SetStaticDefaults()
    {
        Main.debuff[Type] = true;
        Main.buffNoSave[Type] = true;
        Main.buffNoTimeDisplay[Type] = false;
    }
}

public sealed class MilkcraftFrostedGlobalNPC : GlobalNPC
{
    public override bool InstancePerEntity => true;

    public override void Load() => On_NPC.UpdateNPC += ScaleFrostedNpcFrameDisplacement;

    public override void Unload() => On_NPC.UpdateNPC -= ScaleFrostedNpcFrameDisplacement;

    private static void ScaleFrostedNpcFrameDisplacement(On_NPC.orig_UpdateNPC orig, NPC npc, int i)
    {
        int frostType = ModContent.BuffType<MilkcraftFrosted>();
        bool slowed = npc.active && !npc.boss && !npc.immortal && npc.HasBuff(frostType);
        Vector2 start = npc.position;
        Vector2 velocityBeforeUpdate = npc.velocity;
        orig(npc, i);
        if (slowed && npc.active)
        {
            // Scale normal collision-resolved movement, but preserve discrete
            // teleports performed by vanilla NPC AI (for example mage jumps).
            npc.position = start + ApplyMovementSlowdown(npc.position - start, velocityBeforeUpdate, npc.width, npc.height);
        }
    }

    private static Vector2 ApplyMovementSlowdown(Vector2 frameDelta, Vector2 velocityBeforeUpdate, int width, int height)
    {
        float teleportThreshold = Math.Max(64f, Math.Max(width, height) + velocityBeforeUpdate.Length() * 3f + 24f);
        return frameDelta.LengthSquared() > teleportThreshold * teleportThreshold ? frameDelta : frameDelta * 0.8f;
    }

    internal static Vector2 DiagnosticRunOrigDisplacement(Vector2 displacement, Vector2 velocity, int width, int height)
    {
        NPC fixture = new() { active = true, position = new Vector2(100f, 100f), velocity = velocity, width = width, height = height };
        fixture.AddBuff(ModContent.BuffType<MilkcraftFrosted>(), 120);
        Vector2 start = fixture.position;
        ScaleFrostedNpcFrameDisplacement((npc, _) => npc.position = start + displacement, fixture, fixture.whoAmI);
        return fixture.position - start;
    }

}
