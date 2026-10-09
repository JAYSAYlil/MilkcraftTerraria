using System;
using System.Linq;
using System.Reflection;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;
using Terraria.Utilities;
using MilkcraftTerraria.Content.Items;

namespace MilkcraftTerraria.Content;

internal static class ProgressionEnchantPrefixDiagnostics
{
    private readonly record struct WeaponCase(string Name, int Type, int Prefix, MilkcraftEnchant Enchant, int ExpectedPrefix);
    // Verified against the installed tML 2026.8.3 PrefixID fields, Lang.prefix, and Item.CanApplyPrefix.
    private const int MenacingAccessoryPrefix = 72;
    private const int LuckyAccessoryPrefix = 68;
    private const int WardingAccessoryPrefix = 65;
    private const int QuickAccessoryPrefix = 76;

    internal static string Run()
    {
        Player[] oldPlayers = Main.player;
        Projectile[] oldProjectiles = Main.projectile;
        Item[] oldItems = Main.item;
        Dust[] oldDust = Main.dust;
        NPC[] oldNpcs = Main.npc;
        int oldMyPlayer = Main.myPlayer, oldNetMode = Main.netMode;
        bool oldMenu = Main.gameMenu, oldWorldGen = WorldGen.gen;
        UnifiedRandom oldRandom = Main.rand;
        const int playerIndex = 0;
        Player actor = oldPlayers[playerIndex].SerializedClone();
        MethodInfo setup = typeof(PlayerLoader).GetMethod("SetupPlayer", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("PlayerLoader.SetupPlayer is unavailable");
        setup.Invoke(null, new object[] { actor });
        actor.whoAmI = playerIndex;
        try
        {
            Main.player = (Player[])oldPlayers.Clone();
            Main.player[playerIndex] = actor;
            Main.projectile = Enumerable.Range(0, Math.Max(oldProjectiles.Length, Main.maxProjectiles + 1)).Select(_ => new Projectile()).ToArray();
            Main.item = Enumerable.Range(0, Math.Max(oldItems.Length, Main.maxItems + 1)).Select(_ => new Item()).ToArray();
            Main.dust = Enumerable.Range(0, Math.Max(oldDust.Length, Main.maxDust + 1)).Select(_ => new Dust()).ToArray();
            Main.npc = Enumerable.Range(0, Math.Max(oldNpcs.Length, Main.maxNPCs + 1)).Select(_ => new NPC()).ToArray();
            Main.myPlayer = playerIndex;
            Main.netMode = NetmodeID.SinglePlayer;
            Main.gameMenu = false;
            WorldGen.gen = false;
            Main.rand = new UnifiedRandom(20261008);
            actor.active = true;
            actor.dead = false;

            string weaponRows = VerifyWeapons(actor);
            string accessoryRows = VerifyAccessories(actor);
            string reforge = VerifyReforgeAndPersistence();
            string shot = VerifyRealItemCheckShot(actor);
            return $"prefix-stack-weapons=PASS\n{weaponRows}\nprefix-stack-accessories=PASS\n{accessoryRows}\nreforge-save-clone=PASS {reforge}\nitemcheck-prefix-enchant=PASS {shot}";
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
            Main.gameMenu = oldMenu;
            WorldGen.gen = oldWorldGen;
            Main.rand = oldRandom;
        }
    }

    private static string VerifyWeapons(Player actor)
    {
        WeaponCase[] cases =
        {
            new("IronSword", ModContent.ItemType<IronSword>(), PrefixID.Legendary, MilkcraftEnchant.Sharpness, PrefixID.Legendary),
            new("DazhouIronSword", ModContent.ItemType<DazhouIronSword>(), PrefixID.Legendary, MilkcraftEnchant.Sharpness, PrefixID.Legendary),
            new("KnightSword", ModContent.ItemType<KnightSword>(), PrefixID.Legendary, MilkcraftEnchant.Sharpness, PrefixID.Legendary),
            new("IronBow", ModContent.ItemType<IronBow>(), PrefixID.Unreal, MilkcraftEnchant.Power, PrefixID.Unreal),
            new("NightmareBow", ModContent.ItemType<NightmareBow>(), PrefixID.Unreal, MilkcraftEnchant.Power, PrefixID.Unreal),
            new("IronStaff", ModContent.ItemType<IronStaff>(), PrefixID.Mythical, MilkcraftEnchant.Spell, PrefixID.Mythical),
            new("FallenStaff", ModContent.ItemType<FallenStaff>(), PrefixID.Mythical, MilkcraftEnchant.Spell, PrefixID.Mythical)
        };
        var rows = new System.Collections.Generic.List<string>();
        Item vanillaZenith = NewItem(ItemID.Zenith, MilkcraftEnchant.None);
        Item customZenith = NewItem(ModContent.ItemType<DazhouIronSword>(), MilkcraftEnchant.None);
        string zenithPrefixes = string.Join(',', Enumerable.Range(1, 100).Where(vanillaZenith.CanApplyPrefix));
        string customPrefixes = string.Join(',', Enumerable.Range(1, 100).Where(customZenith.CanApplyPrefix));
        bool vanillaZenithLegendary = NewItem(ItemID.Zenith, MilkcraftEnchant.None).Prefix(PrefixID.Legendary);
        bool customDazhouLegendary = NewItem(ModContent.ItemType<DazhouIronSword>(), MilkcraftEnchant.None).Prefix(PrefixID.Legendary);
        bool vanillaZenithGodly = NewItem(ItemID.Zenith, MilkcraftEnchant.None).Prefix(PrefixID.Godly);
        bool customDazhouGodly = NewItem(ModContent.ItemType<DazhouIronSword>(), MilkcraftEnchant.None).Prefix(PrefixID.Godly);
        int[] vanillaLegalIds = Enumerable.Range(1, 100).Where(vanillaZenith.CanApplyPrefix).ToArray();
        int[] customLegalIds = Enumerable.Range(1, 100).Where(customZenith.CanApplyPrefix).ToArray();
        rows.Add($"prefix-category-compare:Legendary-apply vanillaZenith={vanillaZenithLegendary} customDazhou={customDazhouLegendary}; Godly-apply vanillaZenith={vanillaZenithGodly} customDazhou={customDazhouGodly}; vanillaCanApplyLegendary={vanillaZenith.CanApplyPrefix(PrefixID.Legendary)} customCanApplyLegendary={customZenith.CanApplyPrefix(PrefixID.Legendary)} vanillaLegal=[{zenithPrefixes}] customLegal=[{customPrefixes}]");
        if (!vanillaZenithLegendary || !customDazhouLegendary || !vanillaLegalIds.SequenceEqual(customLegalIds))
            throw new InvalidOperationException($"Dazhou prefix category still differs from Zenith: Legendary vanilla/custom={vanillaZenithLegendary}/{customDazhouLegendary}; vanilla=[{zenithPrefixes}] custom=[{customPrefixes}]");
        foreach (WeaponCase test in cases)
        {
            Item baseline = NewItem(test.Type, MilkcraftEnchant.None);
            if (!baseline.Prefix(test.Prefix))
                throw new InvalidOperationException($"vanilla {PrefixLoader.GetPrefix(test.Prefix)?.Name} prefix rejected by {test.Name}");
            Item enchanted = baseline.Clone();
            enchanted.GetGlobalItem<MilkcraftEnchantedItem>().Kind = test.Enchant;
            if (enchanted.prefix != test.ExpectedPrefix)
                throw new InvalidOperationException($"enchant assignment changed {test.Name} prefix {test.ExpectedPrefix}->{enchanted.prefix}");

            ResetPlayer(actor);
            int baseDamage = actor.GetWeaponDamage(baseline);
            float baseCrit = actor.GetWeaponCrit(baseline);
            float baseSpeed = actor.GetWeaponAttackSpeed(baseline);
            float baseMana = actor.GetManaCost(baseline);
            float baseKnockback = actor.GetKnockback(baseline.DamageType).ApplyTo(baseline.knockBack);
            ResetPlayer(actor);
            int enchDamage = actor.GetWeaponDamage(enchanted);
            float enchCrit = actor.GetWeaponCrit(enchanted);
            float enchSpeed = actor.GetWeaponAttackSpeed(enchanted);
            float enchMana = actor.GetManaCost(enchanted);
            float enchKnockback = actor.GetKnockback(enchanted.DamageType).ApplyTo(enchanted.knockBack);
            if (Math.Abs(enchDamage - baseDamage * 1.06f) > 1.1f || Math.Abs(enchCrit - baseCrit) > 0.01f
                || Math.Abs(enchSpeed - baseSpeed) > 0.001f || Math.Abs(enchMana - baseMana) > 0.001f
                || Math.Abs(enchKnockback - baseKnockback) > 0.001f)
                throw new InvalidOperationException($"{test.Name}/{PrefixLoader.GetPrefix(test.Prefix)?.Name}: prefix/enchant stat conflict damage={baseDamage}->{enchDamage}, crit={baseCrit}->{enchCrit}, speed={baseSpeed}->{enchSpeed}, mana={baseMana}->{enchMana}, knockback={baseKnockback}->{enchKnockback}");
            rows.Add($"{test.Name}:{Lang.prefix[test.Prefix].Value}/kind={test.Enchant}/prefix={enchanted.prefix}/damage={baseDamage}->{enchDamage}/crit={baseCrit:0.##}->{enchCrit:0.##}/speed={baseSpeed:0.####}->{enchSpeed:0.####}/mana={baseMana:0.####}->{enchMana:0.####}/knockback={baseKnockback:0.####}->{enchKnockback:0.####}");
        }
        return string.Join("\n", rows);
    }

    private static string VerifyAccessories(Player actor)
    {
        var rows = new System.Collections.Generic.List<string>();
        VerifyAccessory(ModContent.ItemType<IronQuiver>(), MenacingAccessoryPrefix, MilkcraftEnchant.Power, DamageClass.Ranged, actor, rows);
        VerifyAccessory(ModContent.ItemType<BeginnerTome>(), LuckyAccessoryPrefix, MilkcraftEnchant.Critical, DamageClass.Magic, actor, rows);
        VerifyAccessory(ModContent.ItemType<IronShield>(), WardingAccessoryPrefix, MilkcraftEnchant.Armor, DamageClass.Melee, actor, rows);
        VerifyAccessory(ModContent.ItemType<IronQuiver>(), QuickAccessoryPrefix, MilkcraftEnchant.Power, DamageClass.Ranged, actor, rows);
        VerifyHeldStealth(actor, rows);
        Item vanillaQuiver = NewItem(ItemID.MagicQuiver, MilkcraftEnchant.None);
        bool vanillaMenacingAccepted = vanillaQuiver.Prefix(MenacingAccessoryPrefix);
        float vanillaMenacingDamage = 0f, vanillaBaseDamage = 0f;
        if (vanillaMenacingAccepted)
        {
            Item vanillaBase = NewItem(ItemID.MagicQuiver, MilkcraftEnchant.None);
            Evaluate(vanillaBase, actor, DamageClass.Ranged, out vanillaBaseDamage, out _, out _, out _, out _, out _, out _, out float baseGeneric);
            Evaluate(vanillaQuiver, actor, DamageClass.Ranged, out vanillaMenacingDamage, out _, out _, out _, out _, out int actualPrefix, out bool functionalAccessory, out float prefixGeneric);
            rows.Add($"vanilla-MagicQuiver-debug:slotPrefix={actualPrefix},accessory={functionalAccessory},genericDamage={baseGeneric:0.####}->{prefixGeneric:0.####}");
        }
        rows.Add($"vanilla-MagicQuiver:{Lang.prefix[MenacingAccessoryPrefix].Value} accepted={vanillaMenacingAccepted} damage={vanillaBaseDamage:0.##}->{vanillaMenacingDamage:0.##}");
        return string.Join("\n", rows);
    }

    private static void VerifyAccessory(int type, int prefix, MilkcraftEnchant enchant, DamageClass damageClass, Player actor, System.Collections.Generic.List<string> rows)
    {
        Item plain = NewItem(type, MilkcraftEnchant.None);
        if (!plain.Prefix(prefix))
        {
            string legal = string.Join(',', Enumerable.Range(1, 100).Where(plain.CanApplyPrefix)
                .Select(id => $"{id}:{Lang.prefix[id].Value}"));
            throw new InvalidOperationException($"vanilla accessory prefix {prefix}:{Lang.prefix[prefix].Value} rejected by {plain.ModItem?.Name}; CanHavePrefixes={plain.CanHavePrefixes()} legalVanillaIDs=[{legal}]");
        }
        Item enchanted = plain.Clone();
        enchanted.GetGlobalItem<MilkcraftEnchantedItem>().Kind = enchant;
        Item unprefixed = NewItem(type, MilkcraftEnchant.None);
        float noPrefixDamage, noPrefixCrit, noPrefixSpeed, noPrefixMove;
        int noPrefixDefense;
        Evaluate(unprefixed, actor, damageClass, out noPrefixDamage, out noPrefixCrit, out noPrefixSpeed, out noPrefixDefense, out noPrefixMove, out _, out _, out _);
        float prefixDamage, prefixCrit, prefixSpeed, prefixMove;
        int prefixDefense;
        Evaluate(plain, actor, damageClass, out prefixDamage, out prefixCrit, out prefixSpeed, out prefixDefense, out prefixMove, out int equippedPrefix, out bool equippedAccessory, out float genericDamage);
        float enchDamage, enchCrit, enchSpeed, enchMove;
        int enchDefense;
        Evaluate(enchanted, actor, damageClass, out enchDamage, out enchCrit, out enchSpeed, out enchDefense, out enchMove, out _, out _, out _);
        if (plain.prefix != enchanted.prefix || Math.Abs(prefixSpeed - enchSpeed) > 0.001f || Math.Abs(prefixMove - enchMove) > 0.001f)
            throw new InvalidOperationException($"accessory {plain.ModItem?.Name} enchant altered prefix/speed unexpectedly");
        if (prefix == MenacingAccessoryPrefix && Math.Abs((prefixDamage - noPrefixDamage) - 4f) > 0.1f)
            throw new InvalidOperationException($"Menacing prefix benefit missing: {noPrefixDamage}->{prefixDamage}; requested={prefix}, slot={equippedPrefix}, accessory={equippedAccessory}, genericDamage={genericDamage:0.####}");
        if (prefix == LuckyAccessoryPrefix && Math.Abs((prefixCrit - noPrefixCrit) - 4f) > 0.05f)
            throw new InvalidOperationException($"Lucky prefix benefit missing: {noPrefixCrit}->{prefixCrit}; requested={prefix}, slot={equippedPrefix}, accessory={equippedAccessory}");
        if (prefix == WardingAccessoryPrefix && prefixDefense - noPrefixDefense != 4)
            throw new InvalidOperationException($"Warding prefix benefit missing: {noPrefixDefense}->{prefixDefense}; requested={prefix}, slot={equippedPrefix}, accessory={equippedAccessory}");
        if (prefix == QuickAccessoryPrefix && Math.Abs((prefixMove - noPrefixMove) - 0.04f) > 0.001f)
            throw new InvalidOperationException($"Quick2 prefix benefit missing: {noPrefixMove}->{prefixMove}; requested={prefix}, slot={equippedPrefix}, accessory={equippedAccessory}");
        switch (enchant)
        {
            case MilkcraftEnchant.Power:
                if (Math.Abs((enchDamage - prefixDamage) - 6f) > 0.05f) throw new InvalidOperationException($"Power accessory enchant damage delta mismatch: {prefixDamage}->{enchDamage}");
                break;
            case MilkcraftEnchant.Critical:
                if (Math.Abs((enchCrit - prefixCrit) - 4f) > 0.05f) throw new InvalidOperationException($"Critical accessory enchant delta mismatch: {prefixCrit}->{enchCrit}");
                break;
            case MilkcraftEnchant.Armor:
                if (enchDefense - prefixDefense != 4) throw new InvalidOperationException($"Armor accessory enchant delta mismatch: {prefixDefense}->{enchDefense}");
                break;
        }
        rows.Add($"{plain.ModItem?.Name}:{Lang.prefix[prefix].Value}+{enchant}/actualPrefix={plain.prefix}/equippedPrefix={equippedPrefix}/accessory={equippedAccessory}/genericDamage={genericDamage:0.####}/damage={noPrefixDamage:0.##}->{prefixDamage:0.##}->{enchDamage:0.##}/crit={noPrefixCrit:0.##}->{prefixCrit:0.##}->{enchCrit:0.##}/move={noPrefixMove:0.####}->{prefixMove:0.####}->{enchMove:0.####}/defense={noPrefixDefense}->{prefixDefense}->{enchDefense}");
    }

    private static void Evaluate(Item accessory, Player actor, DamageClass damageClass, out float damage, out float crit, out float speed, out int defense, out float moveSpeed, out int equippedPrefix, out bool equippedAccessory, out float genericDamage)
    {
        ResetPlayer(actor);
        actor.armor[3] = accessory.Clone();
        actor.UpdateEquips(actor.whoAmI);
        actor.UpdateArmorSets(actor.whoAmI);
        PlayerLoader.PostUpdateEquips(actor);
        equippedPrefix = actor.armor[3].prefix;
        equippedAccessory = actor.armor[3].accessory && !actor.armor[3].IsAir;
        genericDamage = actor.GetTotalDamage(DamageClass.Generic).ApplyTo(100f);
        Item probe = NewItem(ModContent.ItemType<IronBow>(), MilkcraftEnchant.None);
        probe.DamageType = damageClass;
        damage = actor.GetTotalDamage(damageClass).ApplyTo(100f);
        crit = actor.GetTotalCritChance(damageClass);
        speed = actor.GetWeaponAttackSpeed(probe);
        defense = actor.statDefense.Positive - actor.statDefense.Negative;
        moveSpeed = actor.moveSpeed;
    }

    private static void VerifyHeldStealth(Player actor, System.Collections.Generic.List<string> rows)
    {
        Item stealthBow = NewItem(ModContent.ItemType<IronBow>(), MilkcraftEnchant.Stealth);
        if (!stealthBow.Prefix(PrefixID.Unreal)) throw new InvalidOperationException("Unreal prefix rejected by stealth bow");
        ResetPlayer(actor);
        float baseline = actor.moveSpeed;
        Item quickAccessory = NewItem(ModContent.ItemType<IronQuiver>(), MilkcraftEnchant.None);
        if (!quickAccessory.Prefix(QuickAccessoryPrefix)) throw new InvalidOperationException("Quick2 accessory prefix rejected by IronQuiver");
        actor.armor[3] = quickAccessory.Clone();
        actor.inventory[0] = stealthBow.Clone();
        actor.selectedItem = 1;
        actor.UpdateEquips(actor.whoAmI);
        PlayerLoader.PostUpdateEquips(actor);
        float quickOnly = actor.moveSpeed;
        ResetPlayer(actor);
        actor.armor[3] = quickAccessory.Clone();
        actor.inventory[0] = stealthBow.Clone();
        actor.selectedItem = 0;
        actor.UpdateEquips(actor.whoAmI);
        PlayerLoader.PostUpdateEquips(actor);
        float heldSpeed = actor.moveSpeed;
        if (Math.Abs(quickOnly - baseline - 0.04f) > 0.001f || Math.Abs(heldSpeed - quickOnly - 0.04f) > 0.001f)
            throw new InvalidOperationException($"Quick2+held Stealth movement did not stack: base={baseline}->{quickOnly}->{heldSpeed}");
        ResetPlayer(actor);
        actor.inventory[0] = stealthBow.Clone();
        actor.selectedItem = 1;
        actor.UpdateEquips(actor.whoAmI);
        PlayerLoader.PostUpdateEquips(actor);
        if (Math.Abs(actor.moveSpeed - baseline) > 0.001f)
            throw new InvalidOperationException("Stealth enchant incorrectly applied while enchanted bow was only in inventory");
        rows.Add($"held-Stealth+Unreal:moveSpeed={baseline:0.####}->{heldSpeed:0.####};inventory-only={actor.moveSpeed:0.####}");
    }

    private static string VerifyReforgeAndPersistence()
    {
        Item item = NewItem(ModContent.ItemType<IronBow>(), MilkcraftEnchant.Explosive);
        if (!item.Prefix(PrefixID.Unreal)) throw new InvalidOperationException("Unreal prefix cannot be applied to reforge test bow");
        int initialPrefix = item.prefix;
        for (int i = 0; i < 32; i++)
        {
            ItemLoader.PreReforge(item);
            item.ResetPrefix();
            item.Prefix(-2);
            ItemLoader.PostReforge(item);
            if (item.GetGlobalItem<MilkcraftEnchantedItem>().Kind != MilkcraftEnchant.Explosive)
                throw new InvalidOperationException($"native reforge cycle {i + 1} discarded enchant kind");
            if (item.prefix == 0) throw new InvalidOperationException($"native reforge cycle {i + 1} returned no prefix");
        }
        Item clone = item.Clone();
        if (clone.prefix != item.prefix || clone.GetGlobalItem<MilkcraftEnchantedItem>().Kind != MilkcraftEnchant.Explosive)
            throw new InvalidOperationException("Item.Clone lost prefix or enchant");
        TagCompound saved = ItemIO.Save(item);
        Item loaded = ItemIO.Load(saved);
        if (loaded.prefix != item.prefix || loaded.GetGlobalItem<MilkcraftEnchantedItem>().Kind != MilkcraftEnchant.Explosive)
            throw new InvalidOperationException("ItemIO Save/Load lost prefix or enchant");
        return $"native-ItemLoader.PreReforge/ResetPrefix/Prefix(-2)/PostReforge=32/32 kind=Explosive prefix=Unreal({initialPrefix})->{item.prefix}; clone+ItemIO.Save/Load retained both";
    }

    private static string VerifyRealItemCheckShot(Player actor)
    {
        Main.projectile = Enumerable.Range(0, Math.Max(Main.maxProjectiles + 1, Main.projectile.Length)).Select(_ => new Projectile()).ToArray();
        actor.active = true; actor.dead = false; actor.position = new Vector2(500f, 500f); actor.velocity = Vector2.Zero;
        actor.direction = 1; actor.noItems = false; actor.heldProj = -1; actor.itemTime = actor.itemAnimation = 0;
        actor.selectedItem = 0; actor.inventory[0] = NewItem(ModContent.ItemType<IronBow>(), MilkcraftEnchant.Explosive);
        if (!actor.inventory[0].Prefix(PrefixID.Unreal)) throw new InvalidOperationException("Unreal prefix could not be applied before ItemCheck");
        actor.inventory[1].SetDefaults(ItemID.WoodenArrow); actor.inventory[1].stack = 20;
        actor.controlUseItem = true; actor.controlUseTile = false; actor.releaseUseItem = false; actor.channel = false;
        actor.lastVisualizedSelectedItem = actor.inventory[0];
        actor.ResetEffects(); actor.UpdateEquips(actor.whoAmI); actor.UpdateArmorSets(actor.whoAmI); PlayerLoader.PostUpdateEquips(actor);
        actor.ammoCost80 = false; actor.ammoCost75 = false; actor.ammoPotion = false; actor.ammoBox = false; actor.magicQuiver = false;
        Main.rand = new UnifiedRandom(20261008);
        Main.mouseX = (int)(actor.MountedCenter.X + 400); Main.mouseY = (int)actor.MountedCenter.Y;
        int ammoBefore = actor.inventory[1].stack;
        actor.ItemCheck();
        Projectile shot = Main.projectile.FirstOrDefault(p => p.active && p.owner == actor.whoAmI
            && p.GetGlobalProjectile<MilkcraftEnchantedProjectile>().DiagnosticKind == MilkcraftEnchant.Explosive);
        if (shot is null) throw new InvalidOperationException("native ItemCheck failed to spawn prefix+Explosive enchanted arrow");
        if (shot.damage <= 0 || shot.velocity.LengthSquared() <= 0.01f || actor.inventory[0].prefix != PrefixID.Unreal
            || actor.inventory[0].GetGlobalItem<MilkcraftEnchantedItem>().Kind != MilkcraftEnchant.Explosive
            || actor.inventory[1].stack > ammoBefore)
            throw new InvalidOperationException($"ItemCheck changed enchant/prefix or shot metadata: kind={shot.GetGlobalProjectile<MilkcraftEnchantedProjectile>().DiagnosticKind} prefix={actor.inventory[0].prefix} dmg={shot.damage} velocity={shot.velocity} ammo={ammoBefore}->{actor.inventory[1].stack}");
        return $"IronBow prefix=Unreal({actor.inventory[0].prefix}) enchant=Explosive shot={shot.type} damage={shot.damage} knockback={shot.knockBack:0.##} owner={shot.owner} velocity={shot.velocity} ammo={ammoBefore}->{actor.inventory[1].stack} conservation=(ammoCost80:{actor.ammoCost80},ammoCost75:{actor.ammoCost75},ammoPotion:{actor.ammoPotion},ammoBox:{actor.ammoBox},magicQuiver:{actor.magicQuiver})";
    }

    private static Item NewItem(int type, MilkcraftEnchant kind)
    {
        Item item = new();
        item.SetDefaults(type);
        item.GetGlobalItem<MilkcraftEnchantedItem>().Kind = kind;
        return item;
    }

    private static void ResetPlayer(Player player)
    {
        for (int i = 0; i < player.armor.Length; i++)
        {
            player.armor[i] ??= new Item();
            player.armor[i].TurnToAir();
        }
        player.ResetEffects();
        player.selectedItem = 0;
        player.inventory[0].TurnToAir();
    }
}
