using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;
using MilkcraftTerraria.Content.Items;

namespace MilkcraftTerraria.Content;

/// <summary>
/// Opt-in, client-only balance snapshot. Gear is applied through Player.UpdateEquips
/// and Player.UpdateArmorSets; a naked actor measured through the same path is the baseline.
/// Set MILKCRAFT_TERRARIA_BALANCE_DIAGNOSTIC=1 in the isolated test profile.
/// </summary>
internal static class BalanceRuntimeDiagnostics
{
    private sealed record StatRow(
        string Name, string Category, string Slot, int ItemType, int Defense,
        double GenericDamage, double MeleeDamage, double RangedDamage, double MagicDamage,
        double MeleeCrit, double RangedCrit, double MagicCrit,
        double MeleeSpeed, double RangedSpeed, double MagicSpeed,
        double ManaCost, int ManaMax, double Endurance, double MoveSpeed,
        double ArrowDamage, bool AmmoCost80, bool AmmoCost75, bool AmmoPotion, bool AmmoBox,
        bool MagicQuiver, bool ManaFlower, bool NoKnockback, int Shield, int ShieldCooldown,
        int SharedCooldown, string PassiveSuit, bool Paired, string SetBonus);

    private sealed record WeaponRow(
        string Name, string Origin, int Type, int Damage, string DamageClass, int Crit, int RawItemCrit,
        int UseTime, int UseAnimation, double AttackSpeedMultiplier,
        bool AttackSpeedOnlyAnimation, bool AutoReuse, int ReuseDelay,
        int Mana, double ShootSpeed, int UseAmmo, int Ammo, int ProjectileType,
        int ProjectileWidth, int ProjectileHeight, int ProjectileTimeLeft,
        int ProjectilePenetrate, bool ProjectileTileCollide, int ProjectileExtraUpdates,
        double ProjectileScale, double ProjectileKnockbackMultiplier);

    private sealed record ExportDocument(
        string Version, DateTimeOffset CreatedUtc, StatRow Baseline,
        IReadOnlyList<StatRow> Armors, IReadOnlyList<StatRow> Accessories,
        IReadOnlyList<WeaponRow> Weapons, IReadOnlyList<string> Notes);

    private sealed record ArmorCase(string Name, int Head, int Body, int Legs, bool Modded);
    private sealed record AccessoryCase(string Name, int Item, string Suit, bool Modded);

    private static readonly string[] VanillaWeaponMembers =
    {
        "IronBroadsword", "GoldBroadsword", "PlatinumBroadsword",
        "IronBow", "GoldBow", "PlatinumBow",
        "AmethystStaff", "TopazStaff", "EmeraldStaff", "RubyStaff", "DiamondStaff",
        "LightsBane", "BloodButcherer", "BladeofGrass", "FieryGreatsword", "MoltenFury",
        "DemonBow", "TendonBow", "WaterBolt", "DemonScythe", "HallowedRepeater",
        "Excalibur", "TrueExcalibur", "CrystalSerpent", "SkyFracture", "TitaniumRepeater",
        "TerraBlade", "InfluxWaver", "ChlorophyteShotbow", "Tsunami", "StaffofEarth",
        "SpectreStaff", "HeatRay", "Zenith"
    };

    private static readonly (string Name, string Head, string Body, string Legs)[] VanillaArmorMembers =
    {
        ("VanillaIron", "IronHelmet", "IronChainmail", "IronGreaves"),
        ("VanillaGold", "GoldHelmet", "GoldChainmail", "GoldGreaves"),
        ("VanillaPlatinum", "PlatinumHelmet", "PlatinumChainmail", "PlatinumGreaves"),
        ("Fossil", "FossilHelm", "FossilShirt", "FossilPants"),
        ("Jungle", "JungleHat", "JungleShirt", "JunglePants"),
        ("Meteor", "MeteorHelmet", "MeteorSuit", "MeteorLeggings"),
        ("Molten", "MoltenHelmet", "MoltenBreastplate", "MoltenGreaves"),
        ("Shadow", "ShadowHelmet", "ShadowScalemail", "ShadowGreaves"),
        ("Crimson", "CrimsonHelmet", "CrimsonScalemail", "CrimsonGreaves"),
        ("HallowedMelee", "HallowedMask", "HallowedPlateMail", "HallowedGreaves"),
        ("HallowedRanged", "HallowedHelmet", "HallowedPlateMail", "HallowedGreaves"),
        ("HallowedMagic", "HallowedHeadgear", "HallowedPlateMail", "HallowedGreaves"),
        ("ChlorophyteMelee", "ChlorophyteMask", "ChlorophytePlateMail", "ChlorophyteGreaves"),
        ("ChlorophyteRanged", "ChlorophyteHelmet", "ChlorophytePlateMail", "ChlorophyteGreaves"),
        ("ChlorophyteMagic", "ChlorophyteHeadgear", "ChlorophytePlateMail", "ChlorophyteGreaves"),
        ("BeetleScaleMail", "BeetleHelmet", "BeetleScaleMail", "BeetleLeggings"),
        ("BeetleShell", "BeetleHelmet", "BeetleShell", "BeetleLeggings"),
        ("ShroomiteArrow", "ShroomiteHeadgear", "ShroomiteBreastplate", "ShroomiteLeggings"),
        ("SpectreMask", "SpectreMask", "SpectreRobe", "SpectrePants"),
        ("SpectreHood", "SpectreHood", "SpectreRobe", "SpectrePants")
    };

    private static readonly (string Name, string Item, string Suit)[] ModAccessoryMembers =
    {
        ("IronShield", "IronShield", "Iron"), ("IronQuiver", "IronQuiver", "Leather"),
        ("BeginnerTome", "BeginnerTome", "Cloth"), ("KnightBulwark", "KnightBulwark", "Knight"),
        ("HuntingQuiver", "HuntingQuiver", "Hunting"), ("EnergyTome", "EnergyTome", "Energy"),
        ("TemplarShield", "TemplarShield", "Templar"), ("NightmareQuiver", "NightmareQuiver", "Nightmare"),
        ("FallenTome", "FallenTome", "Fallen"), ("SoulforgedShield", "SoulforgedShield", "Soulforged"),
        ("AbyssQuiver", "AbyssQuiver", "Abyss"), ("DarkTome", "DarkTome", "Dark")
    };

    private static readonly (string Name, string Item)[] VanillaAccessoryMembers =
    {
        ("ObsidianShield", "ObsidianShield"), ("AnkhShield", "AnkhShield"),
        ("FrozenShield", "FrozenShield"), ("WormScarf", "WormScarf"),
        ("MagicQuiver", "MagicQuiver"), ("MoltenQuiver", "MoltenQuiver"),
        ("SniperScope", "SniperScope"), ("ManaFlower", "ManaFlower"),
        ("ManaCloak", "ManaCloak"), ("CelestialEmblem", "CelestialEmblem")
    };

    private static readonly (string Name, string Head, string Body, string Legs)[] ModArmorMembers =
    {
        ("Iron", "IronHelmet", "IronChestplate", "IronGreaves"),
        ("Leather", "LeatherHeadband", "LeatherTunic", "LeatherLeggings"),
        ("Cloth", "ClothHood", "ClothRobe", "ClothLeggings"),
        ("Knight", "KnightHelmet", "KnightChestplate", "KnightGreaves"),
        ("Hunting", "HuntingHood", "HuntingChestplate", "HuntingLeggings"),
        ("Energy", "EnergyHood", "EnergyRobe", "EnergyLeggings"),
        ("Templar", "TemplarHelmet", "TemplarChestplate", "TemplarGreaves"),
        ("Nightmare", "NightmareHood", "NightmareChestplate", "NightmareLeggings"),
        ("Fallen", "FallenHood", "FallenRobe", "FallenLeggings"),
        ("Soulforged", "SoulforgedHelmet", "SoulforgedChestplate", "SoulforgedGreaves"),
        ("Abyss", "AbyssHood", "AbyssChestplate", "AbyssLeggings"),
        ("Dark", "DarkHood", "DarkRobe", "DarkLeggings")
    };

    internal static string Export(string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        int index = Math.Clamp(Main.myPlayer, 0, Main.player.Length - 1);
        Player original = Main.player[index];
        bool oldMenu = Main.gameMenu;
        var armorRows = new List<StatRow>();
        var accessoryRows = new List<StatRow>();
        var notes = new List<string>();
        StatRow baseline;
        try
        {
            Main.gameMenu = false;
            Player naked = NewActor(index);
            Main.player[index] = naked;
            baseline = Capture(naked, "Baseline", "naked", 0);

            foreach (var suit in ModArmorMembers)
            {
                StatRow row = CaptureArmor(index, suit.Name,
                    ModItemType(suit.Head), ModItemType(suit.Body), ModItemType(suit.Legs), true, baseline);
                armorRows.Add(row);
            }
            foreach (var suit in VanillaArmorMembers)
            {
                StatRow row = CaptureArmor(index, suit.Name,
                    VanillaItemType(suit.Head), VanillaItemType(suit.Body), VanillaItemType(suit.Legs), false, baseline);
                armorRows.Add(row);
            }

            foreach (var accessory in ModAccessoryMembers)
            {
                int acc = ModItemType(accessory.Item);
                accessoryRows.Add(CaptureAccessory(index, accessory.Name, acc, null, true, baseline));
                var suit = ModArmorMembers.Single(entry => entry.Name == accessory.Suit);
                accessoryRows.Add(CaptureAccessory(index, accessory.Name + "+" + suit.Name,
                    acc, suit, true, baseline));
            }
            foreach (var accessory in VanillaAccessoryMembers)
                accessoryRows.Add(CaptureAccessory(index, accessory.Name, VanillaItemType(accessory.Item), null, false, baseline));
        }
        finally
        {
            Main.player[index] = original;
            Main.gameMenu = oldMenu;
        }

        List<WeaponRow> weapons = ExportWeapons(notes);
        var doc = new ExportDocument("0.2.10-candidate", DateTimeOffset.UtcNow, baseline,
            armorRows, accessoryRows, weapons, notes);
        File.WriteAllText(Path.Combine(outputDirectory, "balance-runtime.json"),
            JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = true }));
        WriteCsv(Path.Combine(outputDirectory, "armor-stats.csv"), armorRows);
        WriteCsv(Path.Combine(outputDirectory, "accessory-stats.csv"), accessoryRows);
        WriteWeaponCsv(Path.Combine(outputDirectory, "weapon-runtime.csv"), weapons);
        File.WriteAllText(Path.Combine(outputDirectory, "balance-summary.txt"), Summary(baseline, armorRows, accessoryRows, weapons, notes));
        return $"balance-runtime=PASS armor={armorRows.Count} accessories={accessoryRows.Count} weapons={weapons.Count} baseline={baseline.Name} files=balance-runtime.json,armor-stats.csv,accessory-stats.csv,weapon-runtime.csv,balance-summary.txt";
    }

    private static Player NewActor(int index)
    {
        // Use a fresh Player rather than cloning the local character. A clone
        // carries persistent ModPlayer state (for example the Iron shield's
        // saved 10/10 charge) that ResetEffects intentionally does not clear.
        // This would contaminate every vanilla row with mod-only state.
        Player actor = new()
        {
            active = true,
            dead = false,
            ghost = false,
            whoAmI = index,
            width = 20,
            height = 42,
            position = new Microsoft.Xna.Framework.Vector2(400f, 400f),
            velocity = Microsoft.Xna.Framework.Vector2.Zero,
            statLife = 400,
            statLifeMax = 400,
            statMana = 20,
            statManaMax = 20
        };
        for (int i = 0; i < actor.armor.Length; i++)
            actor.armor[i] = new Item();
        for (int i = 0; i < actor.dye.Length; i++)
            actor.dye[i] = new Item();
        for (int i = 0; i < actor.inventory.Length; i++)
            actor.inventory[i] = new Item();
        for (int i = 0; i < actor.buffType.Length; i++)
        {
            actor.buffType[i] = 0;
            actor.buffTime[i] = 0;
        }
        actor.selectedItem = 0;
        actor.GetModPlayer<Tier3T4ProgressPlayer>().ApplyNetworkState(true, true, false);
        return actor;
    }

    private static StatRow CaptureArmor(int index, string name, int head, int body, int legs, bool modded, StatRow baseline)
    {
        Player actor = NewActor(index);
        actor.armor[0].SetDefaults(head);
        actor.armor[1].SetDefaults(body);
        actor.armor[2].SetDefaults(legs);
        actor.head = actor.armor[0].headSlot;
        actor.body = actor.armor[1].bodySlot;
        actor.legs = actor.armor[2].legSlot;
        bool robes = false;
        if (actor.armor[1].ModItem is not null)
            actor.armor[1].ModItem.SetMatch(actor.Male, ref actor.legs, ref robes);
        Main.player[index] = actor;
        StatRow row = Capture(actor, name, "complete-armor-set", head, modded);
        return Delta(row, baseline);
    }

    private static StatRow CaptureAccessory(int index, string name, int accessory, (string Name, string Head, string Body, string Legs)? suit, bool modded, StatRow baseline)
    {
        Player actor = NewActor(index);
        if (suit.HasValue)
        {
            var s = suit.Value;
            actor.armor[0].SetDefaults(ModItemType(s.Head));
            actor.armor[1].SetDefaults(ModItemType(s.Body));
            actor.armor[2].SetDefaults(ModItemType(s.Legs));
            actor.head = actor.armor[0].headSlot;
            actor.body = actor.armor[1].bodySlot;
            actor.legs = actor.armor[2].legSlot;
            bool robes = false;
            actor.armor[1].ModItem?.SetMatch(actor.Male, ref actor.legs, ref robes);
        }
        actor.armor[3].SetDefaults(accessory);
        Main.player[index] = actor;
        StatRow row = Capture(actor, name, suit.HasValue ? "paired-accessory" : "accessory-only", accessory, modded);
        return Delta(row, baseline);
    }

    private static StatRow Capture(Player actor, string name, string category, int type, bool modded = false)
    {
        actor.ResetEffects();
        actor.UpdateEquips(actor.whoAmI);
        actor.UpdateArmorSets(actor.whoAmI);
        PlayerLoader.PostUpdateEquips(actor);
        MilkcraftPassivePlayer passive = actor.GetModPlayer<MilkcraftPassivePlayer>();
        return new StatRow(name, category, modded ? "mod" : "vanilla", type,
            actor.statDefense.Positive - actor.statDefense.Negative,
            Damage(actor, DamageClass.Generic), Damage(actor, DamageClass.Melee),
            Damage(actor, DamageClass.Ranged), Damage(actor, DamageClass.Magic),
            actor.GetTotalCritChance(DamageClass.Melee), actor.GetTotalCritChance(DamageClass.Ranged), actor.GetTotalCritChance(DamageClass.Magic),
            actor.GetTotalAttackSpeed(DamageClass.Melee), actor.GetTotalAttackSpeed(DamageClass.Ranged), actor.GetTotalAttackSpeed(DamageClass.Magic),
            actor.manaCost, actor.statManaMax2, actor.endurance, actor.moveSpeed,
            actor.arrowDamage.ApplyTo(100f), actor.ammoCost80, actor.ammoCost75, actor.ammoPotion,
            actor.ammoBox, actor.magicQuiver, actor.manaFlower, actor.noKnockback,
            passive.VfxShield, passive.VfxShieldCooldown, passive.VfxSharedCooldown, passive.Suit, passive.IsPaired, actor.setBonus ?? "");
    }

    private static StatRow Delta(StatRow value, StatRow baseline) => value with
    {
        Defense = value.Defense - baseline.Defense,
        GenericDamage = value.GenericDamage - baseline.GenericDamage + 100d,
        MeleeDamage = value.MeleeDamage - baseline.MeleeDamage + 100d,
        RangedDamage = value.RangedDamage - baseline.RangedDamage + 100d,
        MagicDamage = value.MagicDamage - baseline.MagicDamage + 100d,
        MeleeCrit = value.MeleeCrit - baseline.MeleeCrit,
        RangedCrit = value.RangedCrit - baseline.RangedCrit,
        MagicCrit = value.MagicCrit - baseline.MagicCrit,
        MeleeSpeed = value.MeleeSpeed - baseline.MeleeSpeed,
        RangedSpeed = value.RangedSpeed - baseline.RangedSpeed,
        MagicSpeed = value.MagicSpeed - baseline.MagicSpeed,
        ManaCost = value.ManaCost - baseline.ManaCost + 1d,
        ManaMax = value.ManaMax - baseline.ManaMax,
        Endurance = value.Endurance - baseline.Endurance,
        MoveSpeed = value.MoveSpeed - baseline.MoveSpeed,
        ArrowDamage = value.ArrowDamage - baseline.ArrowDamage + 100d
    };

    private static double Damage(Player actor, DamageClass damageClass) =>
        Math.Round(actor.GetTotalDamage(damageClass).ApplyTo(100f), 3);

    private static List<WeaponRow> ExportWeapons(List<string> notes)
    {
        Mod mod = ModContent.GetInstance<MilkcraftTerrariaMod>();
        List<WeaponRow> result = new();
        foreach (ModItem modItem in mod.GetContent<ModItem>().Where(item => item.Item.damage > 0).OrderBy(item => item.Name, StringComparer.Ordinal))
            result.Add(Weapon(modItem.Item, modItem.Name, "mod"));
        Require(result.Count == 13, $"expected all 13 mod weapons, found {result.Count}: {string.Join(",", result.Select(item => item.Name))}");

        foreach (string member in VanillaWeaponMembers)
        {
            int type = VanillaItemType(member);
            Item item = new();
            item.SetDefaults(type);
            string label = member == "FieryGreatsword" ? "Volcano [ItemID.FieryGreatsword]"
                : member == "LightsBane" ? "Light's Bane [ItemID.LightsBane]" : member;
            result.Add(Weapon(item, label, "vanilla"));
        }
        notes.Add("Local ItemID spellings verified by reflection: Volcano is FieryGreatsword and Light's Bane is LightsBane; exported labels document these mappings.");
        notes.Add("Weapon rows contain local Item defaults and projectile defaults. Actual random multishot counts are not inferred from nominal damage and require the native ItemCheck extension described in the report.");
        return result;
    }

    private static WeaponRow Weapon(Item item, string name, string origin)
    {
        Player bare = NewActor(Math.Clamp(Main.myPlayer, 0, Main.player.Length - 1));
        bare.ResetEffects();
        bare.UpdateEquips(bare.whoAmI);
        bare.UpdateArmorSets(bare.whoAmI);
        PlayerLoader.PostUpdateEquips(bare);
        int effectiveCrit = bare.GetWeaponCrit(item);
        Projectile projectile = new();
        if (item.shoot > 0)
            projectile.SetDefaults(item.shoot);
        else
            projectile.SetDefaults(ProjectileID.None);
        return new WeaponRow(name, origin, item.type, item.damage, item.DamageType?.Name ?? "unknown",
            effectiveCrit, item.crit, item.useTime, item.useAnimation,
            item.type >= 0 && item.type < ItemID.Sets.BonusAttackSpeedMultiplier.Length
                ? ItemID.Sets.BonusAttackSpeedMultiplier[item.type] : 1d,
            item.attackSpeedOnlyAffectsWeaponAnimation, item.autoReuse, item.reuseDelay,
            item.mana, item.shootSpeed, item.useAmmo, item.ammo, item.shoot,
            projectile.width, projectile.height, projectile.timeLeft, projectile.penetrate,
            projectile.tileCollide, projectile.extraUpdates, projectile.scale,
            projectile.knockBack == 0f ? 1d : projectile.knockBack);
    }

    private static int ModItemType(string name) => ModContent.GetInstance<MilkcraftTerrariaMod>().Find<ModItem>(name).Type;

    private static int VanillaItemType(string member)
    {
        FieldInfo field = typeof(ItemID).GetField(member, BindingFlags.Public | BindingFlags.Static)
            ?? throw new InvalidOperationException($"Local ItemID member not found: {member}");
        return Convert.ToInt32(field.GetRawConstantValue(), CultureInfo.InvariantCulture);
    }

    private static void WriteCsv(string path, IReadOnlyList<StatRow> rows)
    {
        var b = new StringBuilder("name,category,slot,itemType,defense,genericDamage,meleeDamage,rangedDamage,magicDamage,meleeCrit,rangedCrit,magicCrit,meleeSpeed,rangedSpeed,magicSpeed,manaCost,manaMax,endurance,moveSpeed,arrowDamage,ammoCost80,ammoCost75,ammoPotion,ammoBox,magicQuiver,manaFlower,noKnockback,shield,shieldCooldown,sharedCooldown,passiveSuit,paired,setBonus\n");
        foreach (StatRow r in rows)
            b.AppendLine(string.Join(",", new object[] { Csv(r.Name), Csv(r.Category), Csv(r.Slot), r.ItemType, r.Defense,
                F(r.GenericDamage), F(r.MeleeDamage), F(r.RangedDamage), F(r.MagicDamage), F(r.MeleeCrit), F(r.RangedCrit), F(r.MagicCrit),
                F(r.MeleeSpeed), F(r.RangedSpeed), F(r.MagicSpeed), F(r.ManaCost), r.ManaMax, F(r.Endurance), F(r.MoveSpeed),
                F(r.ArrowDamage), r.AmmoCost80, r.AmmoCost75, r.AmmoPotion, r.AmmoBox, r.MagicQuiver, r.ManaFlower,
                r.NoKnockback, r.Shield, r.ShieldCooldown, r.SharedCooldown, Csv(r.PassiveSuit), r.Paired, Csv(r.SetBonus) }));
        File.WriteAllText(path, b.ToString());
    }

    private static void WriteWeaponCsv(string path, IReadOnlyList<WeaponRow> rows)
    {
        var b = new StringBuilder("name,origin,type,damage,damageClass,effectiveWeaponCrit,rawItemCrit,useTime,useAnimation,attackSpeedMultiplier,attackSpeedOnlyAffectsAnimation,autoReuse,reuseDelay,mana,shootSpeed,useAmmo,ammo,projectileType,projectileWidth,projectileHeight,projectileTimeLeft,projectilePenetrate,projectileTileCollide,projectileExtraUpdates,projectileScale,projectileKnockback\n");
        foreach (WeaponRow r in rows)
            b.AppendLine(string.Join(",", new object[] { Csv(r.Name), Csv(r.Origin), r.Type, r.Damage, Csv(r.DamageClass), r.Crit, r.RawItemCrit, r.UseTime, r.UseAnimation,
                F(r.AttackSpeedMultiplier), r.AttackSpeedOnlyAnimation, r.AutoReuse, r.ReuseDelay, r.Mana, F(r.ShootSpeed), r.UseAmmo, r.Ammo,
                r.ProjectileType, r.ProjectileWidth, r.ProjectileHeight, r.ProjectileTimeLeft, r.ProjectilePenetrate,
                r.ProjectileTileCollide, r.ProjectileExtraUpdates, F(r.ProjectileScale), F(r.ProjectileKnockbackMultiplier) }));
        File.WriteAllText(path, b.ToString());
    }

    private static string Summary(StatRow baseline, IReadOnlyList<StatRow> armors,
        IReadOnlyList<StatRow> accessories, IReadOnlyList<WeaponRow> weapons, IReadOnlyList<string> notes)
    {
        var b = new StringBuilder();
        b.AppendLine("0.2.10 opt-in native balance snapshot — local Terraria/tModLoader runtime defaults");
        b.AppendLine($"Baseline: defense={baseline.Defense}; damage generic/melee/ranged/magic={F(baseline.GenericDamage)}/{F(baseline.MeleeDamage)}/{F(baseline.RangedDamage)}/{F(baseline.MagicDamage)}; crit=0; mana={baseline.ManaMax}; moveSpeed={F(baseline.MoveSpeed)}; endurance={F(baseline.Endurance)}.");
        b.AppendLine($"Rows: armor={armors.Count} (12 mod + {armors.Count - 12} vanilla), accessories={accessories.Count} (12 mod standalone+paired, 10 vanilla), weapons={weapons.Count} (13 mod + {weapons.Count - 13} vanilla).");
        b.AppendLine();
        b.AppendLine("Armors: defense | damage M/R/Ma | crit M/R/Ma | mana | endurance | move | arrow | passive.");
        foreach (StatRow r in armors)
            b.AppendLine($"{r.Name,-20} def {r.Defense,3} | dmg {F(r.MeleeDamage)}/{F(r.RangedDamage)}/{F(r.MagicDamage)} | crit {F(r.MeleeCrit)}/{F(r.RangedCrit)}/{F(r.MagicCrit)} | mana {F(r.ManaCost)}/{r.ManaMax:+#;-#;0} | end {F(r.Endurance)} | move {F(r.MoveSpeed)} | arrow {F(r.ArrowDamage)} | ammo80={r.AmmoCost80} ammoPotion={r.AmmoPotion} | shield {r.Shield}/{r.ShieldCooldown} pair={r.Paired}");
        b.AppendLine();
        b.AppendLine("Accessories: defense | damage M/R/Ma | crit R | mana | endurance | move | arrow | set/pair/passive.");
        foreach (StatRow r in accessories)
            b.AppendLine($"{r.Name,-28} def {r.Defense,3} | dmg {F(r.MeleeDamage)}/{F(r.RangedDamage)}/{F(r.MagicDamage)} | rCrit {F(r.RangedCrit)} | mana {F(r.ManaCost)}/{r.ManaMax:+#;-#;0} | end {F(r.Endurance)} | move {F(r.MoveSpeed)} | arrow {F(r.ArrowDamage)} | ammo80={r.AmmoCost80} magicQuiver={r.MagicQuiver} manaFlower={r.ManaFlower} | {r.PassiveSuit}/{r.Paired}");
        b.AppendLine();
        b.AppendLine("Weapons: name | damage/class | effective weapon crit (raw Item.crit) | use time/animation | mana/ammo | shoot speed | projectile type/size/lifetime/penetration.");
        foreach (WeaponRow r in weapons)
            b.AppendLine($"{r.Name,-36} {r.Damage,3}/{r.DamageClass,-8} | crit {r.Crit,2} (raw {r.RawItemCrit,2}) | {r.UseTime,2}/{r.UseAnimation,2} | mana {r.Mana,2} ammo {r.UseAmmo,2}/{r.Ammo,4} | speed {F(r.ShootSpeed),5} | proj {r.ProjectileType,4} {r.ProjectileWidth}x{r.ProjectileHeight} life {r.ProjectileTimeLeft} pen {r.ProjectilePenetrate}");
        b.AppendLine();
        foreach (string note in notes) b.AppendLine("Note: " + note);
        b.AppendLine("Interpretation: all equipment deltas are relative to the bare actor and captured after UpdateEquips, UpdateArmorSets, and PostUpdateEquips. Weapon table is per-shot/default metadata; multi-shot counts and per-target hit multiplicity require native ItemCheck measurement.");
        return b.ToString();
    }

    private static string Csv(string value) => "\"" + (value ?? "").Replace("\"", "\"\"") + "\"";
    private static string F(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
