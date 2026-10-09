using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.Localization;
using Terraria.ModLoader;
using Terraria.ModLoader.IO;
using MilkcraftTerraria.Content.Items;

namespace MilkcraftTerraria.Content;

/// <summary>Persistent character-owned state for the 0.2.5 suit passives.</summary>
public sealed class MilkcraftPassivePlayer : ModPlayer
{
    private string _suit = "";
    private bool _hasIronShield, _hasKnightBulwark, _hasTemplarShield, _hasSoulforgedShield;
    private bool _hasIronQuiver, _hasHuntingQuiver, _hasNightmareQuiver, _hasAbyssQuiver;
    private bool _hasBeginnerTome, _hasEnergyTome, _hasFallenTome, _hasDarkTome;
    private bool _paired;
    private readonly int[] _shield = new int[12], _shieldCooldown = new int[12], _manaPaid = new int[12], _manaWindow = new int[12], _energyPaid = new int[12], _buffTime = new int[12];
    private readonly int[] _visualPulse = new int[12];
    private readonly bool[] _manaReady = new bool[12];
    private int _sharedCooldown;
    private int _huntTarget = -1, _huntHits, _huntGap, _huntMiss;
    private int _huntProcFlash;
    public string Suit => _suit;
    public bool IsPaired => _paired;
    internal int VfxShield => _shield[SuitIndex(_suit)];
    internal int VfxShieldCooldown => _shieldCooldown[SuitIndex(_suit)];
    internal int VfxSharedCooldown => _sharedCooldown;
    internal int VfxBuffTime => _buffTime[SuitIndex(_suit)];
    internal int VfxManaWindow => _manaWindow[SuitIndex(_suit)];
    internal int VfxManaPaid => _manaPaid[SuitIndex(_suit)];
    internal int VfxEnergyPaid => _energyPaid[SuitIndex(_suit)];
    internal int VfxPulse => _visualPulse[SuitIndex(_suit)];
    internal int VfxHuntTarget => _huntTarget;
    internal int VfxHuntHits => _huntHits;
    internal int VfxHuntProcFlash => _huntProcFlash;
    internal int DiagnosticShield(string suit) => _shield[SuitIndex(suit)];
    internal int DiagnosticShieldCooldown(string suit) => _shieldCooldown[SuitIndex(suit)];
    internal int DiagnosticManaPaid(string suit) => _manaPaid[SuitIndex(suit)];

    public override void ResetEffects()
    {
        _suit = ""; _paired = false;
        _hasIronShield = _hasKnightBulwark = _hasTemplarShield = _hasSoulforgedShield = false;
        _hasIronQuiver = _hasHuntingQuiver = _hasNightmareQuiver = _hasAbyssQuiver = false;
        _hasBeginnerTome = _hasEnergyTome = _hasFallenTome = _hasDarkTome = false;
    }
    public void RegisterFunctionalAccessory(string name)
    {
        switch (name)
        {
            case "IronShield": _hasIronShield = true; break; case "KnightBulwark": _hasKnightBulwark = true; break;
            case "TemplarShield": _hasTemplarShield = true; break; case "SoulforgedShield": _hasSoulforgedShield = true; break;
            case "IronQuiver": _hasIronQuiver = true; break; case "HuntingQuiver": _hasHuntingQuiver = true; break;
            case "NightmareQuiver": _hasNightmareQuiver = true; break; case "AbyssQuiver": _hasAbyssQuiver = true; break;
            case "BeginnerTome": _hasBeginnerTome = true; break; case "EnergyTome": _hasEnergyTome = true; break;
            case "FallenTome": _hasFallenTome = true; break; case "DarkTome": _hasDarkTome = true; break;
        }
    }
    public bool HasFunctionalAccessory(string name) => name switch
    {
        "IronShield" => _hasIronShield, "KnightBulwark" => _hasKnightBulwark, "TemplarShield" => _hasTemplarShield, "SoulforgedShield" => _hasSoulforgedShield,
        "IronQuiver" => _hasIronQuiver, "HuntingQuiver" => _hasHuntingQuiver, "NightmareQuiver" => _hasNightmareQuiver, "AbyssQuiver" => _hasAbyssQuiver,
        "BeginnerTome" => _hasBeginnerTome, "EnergyTome" => _hasEnergyTome, "FallenTome" => _hasFallenTome, "DarkTome" => _hasDarkTome, _ => false
    };
    public static string AccessoryForSuit(string suit) => suit switch
    {
        "Iron" => "IronShield", "Leather" => "IronQuiver", "Cloth" => "BeginnerTome", "Knight" => "KnightBulwark", "Hunting" => "HuntingQuiver", "Energy" => "EnergyTome",
        "Templar" => "TemplarShield", "Nightmare" => "NightmareQuiver", "Fallen" => "FallenTome", "Soulforged" => "SoulforgedShield", "Abyss" => "AbyssQuiver", "Dark" => "DarkTome", _ => ""
    };
    public static bool SuitPairMatches(string suit, string accessory) => AccessoryForSuit(suit) == accessory;
    public override void PostUpdateEquips()
    {
        _suit = ReadCompleteSuit(Player);
        _paired = IsCorrectPair(_suit, this);
        MilkcraftPassiveVfx.UpdateStatusBuff(Player, _suit);
        if (_suit.Length > 0 && !string.IsNullOrEmpty(Player.setBonus))
            Player.setBonus = ColorizeStatus(Player.setBonus, MilkcraftPassiveVfx.SuitColor(_suit));
        if (_sharedCooldown > 0) _sharedCooldown--;
        if (_huntProcFlash > 0) _huntProcFlash--;
        for (int i = 0; i < 12; i++)
        {
            if (_shieldCooldown[i] > 0) _shieldCooldown[i]--;
            if (_manaWindow[i] > 0 && --_manaWindow[i] == 0) _manaReady[i] = _manaPaid[i] > 0;
            if (_buffTime[i] > 0) _buffTime[i]--;
            if (_visualPulse[i] > 0) _visualPulse[i]--;
        }
        if (_huntGap > 0) _huntGap--;
        if (_huntMiss < 360) _huntMiss++;
        if (_huntMiss >= 360) { _huntTarget = -1; _huntHits = 0; _huntGap = 0; }
        int suitIndex = SuitIndex(_suit);
        if (IsShieldSuit(_suit) && _suit != "Energy" && _shield[suitIndex] <= 0 && _shieldCooldown[suitIndex] <= 0) _shield[suitIndex] = ShieldCapacity(_suit, _paired);
        if (IsShieldSuit(_suit) && _shield[suitIndex] > ShieldCapacity(_suit, _paired)) _shield[suitIndex] = ShieldCapacity(_suit, _paired);
        if (_suit == "Leather" && _buffTime[suitIndex] > 0) Player.moveSpeed += _paired ? .12f : .08f;
        if (_suit == "Knight" && _buffTime[suitIndex] > 0) Player.GetDamage(DamageClass.Melee) += _paired ? .08f : .06f;
        if (_suit == "Soulforged" && _buffTime[suitIndex] > 0) Player.GetDamage(DamageClass.Melee) += _paired ? .15f : .10f;
        if (IsShieldSuit(_suit))
        {
            int capacity = ShieldCapacity(_suit, _paired);
            string status;
            Color statusColor;
            if (_suit == "Energy" && _shield[suitIndex] <= 0 && _shieldCooldown[suitIndex] <= 0)
            {
                if (_energyPaid[suitIndex] >= 20)
                {
                    status = Language.GetTextValue("Mods.MilkcraftTerraria.PassiveVfxUi.EnergyWaiting", _energyPaid[suitIndex], 20);
                    statusColor = new Color(113, 222, 142);
                }
                else
                {
                    status = Language.GetTextValue("Mods.MilkcraftTerraria.PassiveVfxUi.EnergyCharge", _energyPaid[suitIndex], 20);
                    statusColor = new Color(176, 174, 190);
                }
            }
            else if (_shieldCooldown[suitIndex] > 0)
            {
                status = Language.GetTextValue("Mods.MilkcraftTerraria.PassiveVfxUi.BarrierCooldown", _shield[suitIndex], capacity, (int)Math.Ceiling(_shieldCooldown[suitIndex] / 60d));
                statusColor = new Color(221, 177, 92);
            }
            else
            {
                status = Language.GetTextValue("Mods.MilkcraftTerraria.PassiveVfxUi.BarrierReady", _shield[suitIndex], capacity);
                statusColor = _shield[suitIndex] > 0 ? new Color(113, 222, 142) : new Color(176, 174, 190);
            }
            AppendStatus(status, statusColor);
        }
        if (_paired)
        {
            string status = Language.GetTextValue("Mods.MilkcraftTerraria.PassiveVfxUi.PairActive");
            AppendStatus(status, new Color(113, 222, 142));
        }
        if (_sharedCooldown > 0 && (_suit is "Nightmare" or "Abyss" or "Dark"))
        {
            string status = Language.GetTextValue("Mods.MilkcraftTerraria.PassiveVfxUi.Cooldown", (int)Math.Ceiling(_sharedCooldown / 60d));
            AppendStatus(status, new Color(221, 177, 92));
        }
        string detail = _suit switch
        {
            "Hunting" when _huntTarget >= 0 => Language.GetTextValue("Mods.MilkcraftTerraria.PassiveVfxUi.Hunting", _huntProcFlash > 0 ? 3 : _huntHits, 3),
            "Leather" or "Knight" or "Soulforged" when _buffTime[suitIndex] > 0 => Language.GetTextValue("Mods.MilkcraftTerraria.PassiveVfxUi.Duration", (int)Math.Ceiling(_buffTime[suitIndex] / 60d)),
            "Cloth" or "Fallen" when _manaWindow[suitIndex] > 0 => Language.GetTextValue("Mods.MilkcraftTerraria.PassiveVfxUi.ManaWindow", _manaPaid[suitIndex], (int)Math.Ceiling(_manaWindow[suitIndex] / 60d)),
            "Cloth" or "Fallen" when _manaReady[suitIndex] && _manaPaid[suitIndex] > 0 => Language.GetTextValue("Mods.MilkcraftTerraria.PassiveVfxUi.ManaReady", _manaPaid[suitIndex]),
            "Energy" when _energyPaid[suitIndex] > 0 && _shield[suitIndex] > 0 => Language.GetTextValue("Mods.MilkcraftTerraria.PassiveVfxUi.EnergyCharge", _energyPaid[suitIndex], 20),
            _ => ""
        };
        if (_suit == "Leather" && _buffTime[suitIndex] <= 0 && _sharedCooldown > 0)
            detail = Language.GetTextValue("Mods.MilkcraftTerraria.PassiveVfxUi.Cooldown", (int)Math.Ceiling(_sharedCooldown / 60d));
        if (detail.Length > 0)
        {
            Color detailColor = _suit is "Cloth" or "Fallen" && _manaReady[suitIndex]
                ? new Color(113, 222, 142)
                : _suit == "Leather" && _buffTime[suitIndex] <= 0 && _sharedCooldown > 0
                    ? new Color(221, 177, 92)
                    : MilkcraftPassiveVfx.SuitColor(_suit);
            AppendStatus(detail, detailColor);
        }

        if (!Main.dedServ && !Player.dead && !Player.ghost && _suit.Length > 0)
        {
            bool active = _visualPulse[suitIndex] > 0 || _buffTime[suitIndex] > 0 || _huntTarget >= 0 && _suit == "Hunting" ||
                IsShieldSuit(_suit) && _shield[suitIndex] > 0 || _suit == "Energy" && _energyPaid[suitIndex] > 0 ||
                (_suit is "Cloth" or "Fallen") && _manaWindow[suitIndex] > 0 && _manaPaid[suitIndex] > 0;
            if (active)
            {
                Vector3 light = MilkcraftPassiveVfx.SuitColor(_suit).ToVector3() * .12f;
                Lighting.AddLight(Player.Center, light);
                Lighting.AddLight(Player.Center, MilkcraftPassiveVfx.SuitAccentColor(_suit).ToVector3() * .035f);
            }
        }
    }

    public override void UpdateDead()
    {
        Array.Clear(_visualPulse, 0, _visualPulse.Length);
        _huntProcFlash = 0;
        MilkcraftPassiveVfx.UpdateStatusBuff(Player, "");
    }

    private void AppendStatus(string text, Color color)
    {
        string colored = ColorizeStatus(text, color);
        Player.setBonus = string.IsNullOrEmpty(Player.setBonus) ? colored : Player.setBonus + "\n" + colored;
    }

    private static string ColorizeStatus(string text, Color color)
        => $"[c/{color.R:X2}{color.G:X2}{color.B:X2}:{text}]";

    public override void ModifyHurt(ref Player.HurtModifiers modifiers)
    {
        if (!IsShieldSuit(_suit)) return;
        int idx = SuitIndex(_suit);
        if (_shield[idx] <= 0) return;
        modifiers.ModifyHurtInfo += (ref Player.HurtInfo info) =>
        {
            if (info.Cancelled || info.Damage <= 0) return;
            int absorbed = Math.Min(_shield[idx], info.Damage);
            int remainder = info.Damage - absorbed;
            _shield[idx] -= absorbed;
            if (absorbed > 0) _visualPulse[idx] = 32;
            if (remainder <= 0)
            {
                info.Cancelled = true;
                Player.immune = true;
                Player.immuneTime = Math.Max(Player.immuneTime, 20);
            }
            else info.Damage = remainder;
            if (_shield[idx] == 0)
            {
                _visualPulse[idx] = 44;
                _shieldCooldown[idx] = _suit == "Energy" ? 720 : _paired ? 600 : 720;
                if (_suit is "Knight" or "Soulforged") _buffTime[idx] = (_suit == "Knight" ? (_paired ? 4 : 3) : (_paired ? 5 : 4)) * 60;
                if (_suit == "Soulforged") EmitPassiveDust(Player, new[] { new Color(27, 55, 106), new Color(188, 49, 47), new Color(239, 183, 75) }, 12);
                else EmitPassiveDust(Player, new Color(255, 218, 102), 12);
            }
            else if (absorbed > 0) EmitPassiveDust(Player, new Color(123, 218, 255), 4);
        };
    }

    public void RecordManaConsumed(Item item, int manaConsumed)
    {
        if (manaConsumed <= 0 || !item.DamageType.CountsAsClass(DamageClass.Magic) || _suit is not ("Cloth" or "Fallen" or "Energy")) return;
        int idx = SuitIndex(_suit);
        _manaPaid[idx] = Math.Min(300, _manaPaid[idx] + manaConsumed);
            if (_suit == "Energy") _energyPaid[idx] = Math.Min(20, _energyPaid[idx] + manaConsumed);
    }

    public override void OnHitNPCWithItem(Item item, NPC target, NPC.HitInfo hit, int damageDone) => HandleHit(item.DamageType, target, false, Player.GetWeaponDamage(item));
    public override void OnHitNPCWithProj(Projectile projectile, NPC target, NPC.HitInfo hit, int damageDone)
    {
        if (projectile.owner != Player.whoAmI) return;
        MilkcraftPairedProjectile gp = projectile.GetGlobalProjectile<MilkcraftPairedProjectile>();
        HandleHit(projectile.DamageType, target, gp.PassiveProjectile || projectile.ModProjectile is SuitPassiveProjectile, projectile.damage);
    }

    private void HandleHit(DamageClass cls, NPC npc, bool generated, int damage)
    {
        if (generated || damage <= 0 || !npc.active || npc.friendly || npc.dontTakeDamage || npc.immortal || npc.type == NPCID.TargetDummy || npc.lifeMax <= 5) return;
        if (Main.netMode == NetmodeID.Server || (Main.netMode == NetmodeID.MultiplayerClient && Player.whoAmI != Main.myPlayer)) return;
        if (cls.CountsAsClass(DamageClass.Ranged))
        {
            int suitIndex = SuitIndex(_suit);
            if (_suit == "Leather" && _sharedCooldown == 0) { _sharedCooldown = 360; _buffTime[suitIndex] = 180; _visualPulse[suitIndex] = 40; EmitPassiveDust(Player, new Color(104, 220, 162), 5); }
            if (_suit == "Hunting")
            {
                int targetKey = npc.realLife >= 0 ? npc.realLife : npc.whoAmI;
                if (_huntTarget != targetKey || _huntMiss >= 360) { _huntTarget = targetKey; _huntHits = 0; }
                if (_huntGap == 0) { _huntHits = Math.Min(3, _huntHits + 1); _huntGap = 15; }
                _huntMiss = 0;
                if (_huntHits == 3)
                {
                    _huntHits = 0;
                    _huntProcFlash = 20;
                    _visualPulse[suitIndex] = 28;
                    EmitPassiveDust(Player, new Color(123, 218, 255), 7);
                }
            }
            if (_suit == "Nightmare" && _sharedCooldown == 0) SpawnSuitShot(npc, damage, _paired ? .60f : .40f, _paired ? 90 : 120);
            if (_suit == "Abyss" && _sharedCooldown == 0) SpawnSuitShot(npc, damage, .65f, _paired ? 60 : 90);
        }
        if (cls.CountsAsClass(DamageClass.Magic))
        {
            int suitIndex = SuitIndex(_suit);
            if ((_suit is "Cloth" or "Fallen") && _sharedCooldown == 0)
            {
                if (_manaReady[suitIndex] && _manaPaid[suitIndex] > 0)
                {
                    int pct = _suit == "Fallen" ? (_paired ? 20 : 15) : (_paired ? 15 : 10);
                    int cap = _suit == "Fallen" ? (_paired ? 40 : 30) : (_paired ? 12 : 8);
                    int manaReturn = _manaPaid[suitIndex] > 0 ? Math.Min(cap, _manaPaid[suitIndex] * pct / 100) : 0;
                    int manaBefore = Player.statMana;
                    Player.statMana = Math.Min(Player.statManaMax2, Player.statMana + manaReturn);
                    int restoredMana = Player.statMana - manaBefore;
                    if (restoredMana > 0) Player.ManaEffect(restoredMana);
                    int healed = 0;
                    if (_suit == "Fallen")
                    {
                        int lifeBefore = Player.statLife;
                        Player.Heal(_paired ? 15 : 10);
                        healed = Player.statLife - lifeBefore;
                    }
                    if (restoredMana > 0 || healed > 0)
                    {
                        _visualPulse[suitIndex] = 38;
                        if (_suit == "Fallen") EmitPassiveDust(Player, new Color(255, 224, 164), 10);
                    }
                    _manaPaid[suitIndex] = 0; _manaReady[suitIndex] = false; _manaWindow[suitIndex] = 360; _sharedCooldown = 360;
                }
                else if (_manaWindow[suitIndex] == 0) { _manaReady[suitIndex] = false; _manaWindow[suitIndex] = 360; }
            }
            if (_suit == "Energy" && _energyPaid[suitIndex] >= 20 && _shieldCooldown[suitIndex] == 0 && _shield[suitIndex] == 0)
            {
                _shield[suitIndex] = _paired ? 18 : 12; _energyPaid[suitIndex] = 0; _visualPulse[suitIndex] = 40;
                EmitPassiveDust(Player, new[] { new Color(213, 180, 255), Color.White }, 7);
            }
            if (_suit == "Dark" && _sharedCooldown == 0) SpawnSuitShot(npc, damage, _paired ? .75f : .60f, _paired ? 90 : 120);
        }
    }

    private void SpawnSuitShot(NPC npc, int baseDamage, float fraction, int cooldown)
    {
        _sharedCooldown = cooldown;
        _visualPulse[SuitIndex(_suit)] = 40;
        Color[] procColors = _suit switch
        {
            "Nightmare" => new[] { new Color(224, 230, 241), new Color(198, 53, 75) },
            "Abyss" => new[] { new Color(42, 66, 143), new Color(198, 54, 48), new Color(234, 184, 78) },
            _ => new[] { new Color(80, 56, 142), new Color(201, 51, 103) }
        };
        EmitPassiveDust(Player, procColors, 6);
        int damage = Math.Max(1, (int)(baseDamage * fraction));
        float variant = _suit == "Nightmare" ? 1f : _suit == "Abyss" ? 2f : 3f;
        Projectile p = Projectile.NewProjectileDirect(Player.GetSource_OnHit(npc), Player.Center, Vector2.Zero,
            ModContent.ProjectileType<SuitPassiveProjectile>(), damage, 0f, Player.whoAmI, npc.whoAmI, variant);
        p.GetGlobalProjectile<MilkcraftPairedProjectile>().PassiveProjectile = true;
        p.netUpdate = true;
        p.DamageType = _suit == "Dark" ? DamageClass.Magic : DamageClass.Ranged;
        if (_suit == "Abyss") p.penetrate = _paired ? 3 : 2;
    }

    private static void EmitPassiveDust(Player player, Color color, int count)
        => EmitPassiveDust(player, new[] { color }, count);

    private static void EmitPassiveDust(Player player, Color[] colors, int count)
    {
        if (Main.dedServ || (Main.netMode == NetmodeID.MultiplayerClient && player.whoAmI != Main.myPlayer)) return;
        MilkcraftPassivePlayer passive = player.GetModPlayer<MilkcraftPassivePlayer>();
        if (!string.IsNullOrEmpty(passive.Suit))
            colors = new[] { MilkcraftPassiveVfx.SuitColor(passive.Suit), MilkcraftPassiveVfx.SuitAccentColor(passive.Suit) };
        for (int i = 0; i < count; i++)
        {
            Color color = colors[i % colors.Length];
            int index = Dust.NewDust(player.position, player.width, player.height, DustID.Torch,
                Main.rand.NextFloat(-2.4f, 2.4f), Main.rand.NextFloat(-2.4f, 2.4f), 0, color, Main.rand.NextFloat(.8f, 1.25f));
            Main.dust[index].noGravity = true;
            Main.dust[index].noLight = true;
        }
    }

    public bool WouldApplyHuntingBonus(NPC target) => _suit == "Hunting" && _huntTarget == (target.realLife >= 0 ? target.realLife : target.whoAmI) && _huntHits == 2 && _huntGap == 0;

    public static string ReadCompleteSuit(Player player)
    {
        string h = player.armor[0].ModItem?.Name ?? "", b = player.armor[1].ModItem?.Name ?? "", l = player.armor[2].ModItem?.Name ?? "";
        string[] suits = { "Iron", "Leather", "Cloth", "Knight", "Hunting", "Energy", "Templar", "Nightmare", "Fallen", "Soulforged", "Abyss", "Dark" };
        foreach (string s in suits)
        {
            string eh = s switch { "Leather" => "LeatherHeadband", "Cloth" => "ClothHood", "Hunting" => "HuntingHood", "Energy" => "EnergyHood", "Templar" => "TemplarHelmet", "Nightmare" => "NightmareHood", "Fallen" => "FallenHood", "Soulforged" => "SoulforgedHelmet", "Abyss" => "AbyssHood", "Dark" => "DarkHood", _ => s + "Helmet" };
            string eb = s switch { "Leather" => "LeatherTunic", "Cloth" => "ClothRobe", "Templar" => "TemplarChestplate", "Nightmare" => "NightmareChestplate", "Fallen" => "FallenRobe", "Soulforged" => "SoulforgedChestplate", "Abyss" => "AbyssChestplate", "Dark" => "DarkRobe", "Hunting" => "HuntingChestplate", "Energy" => "EnergyRobe", _ => s + "Chestplate" };
            string el = s switch { "Templar" => "TemplarGreaves", "Soulforged" => "SoulforgedGreaves", "Iron" or "Knight" => s + "Greaves", _ => s + "Leggings" };
            if (h == eh && b == eb && l == el)
            {
                int tier = s is "Templar" or "Nightmare" or "Fallen" ? 3 : s is "Soulforged" or "Abyss" or "Dark" ? 4 : 0;
                if (tier == 0 || player.GetModPlayer<Tier3T4ProgressPlayer>().HasLicense(tier)) return s;
            }
        }
        return "";
    }

    private static bool IsCorrectPair(string s, MilkcraftPassivePlayer p) => p.HasFunctionalAccessory(AccessoryForSuit(s));
    private static bool IsShieldSuit(string s) => s is "Iron" or "Knight" or "Templar" or "Soulforged" or "Energy";
    private static int SuitIndex(string s) => s switch { "Iron" => 0, "Leather" => 1, "Cloth" => 2, "Knight" => 3, "Hunting" => 4, "Energy" => 5, "Templar" => 6, "Nightmare" => 7, "Fallen" => 8, "Soulforged" => 9, "Abyss" => 10, "Dark" => 11, _ => 0 };
    private static int ShieldCapacity(string s, bool paired) => s switch { "Iron" => paired ? 15 : 10, "Knight" => paired ? 30 : 20, "Templar" => paired ? 50 : 30, "Soulforged" => paired ? 90 : 60, "Energy" => paired ? 18 : 12, _ => 0 };

    public override void SaveData(Terraria.ModLoader.IO.TagCompound tag) { tag["pShield"] = _shield; tag["pShieldCd"] = _shieldCooldown; tag["pSharedCd"] = _sharedCooldown; tag["pManaPaid"] = _manaPaid; tag["pManaWindow"] = _manaWindow; tag["pManaReady"] = Array.ConvertAll(_manaReady, value => value ? 1 : 0); tag["pEnergy"] = _energyPaid; tag["pBuff"] = _buffTime; }
    public override void LoadData(Terraria.ModLoader.IO.TagCompound tag) { Read(tag, "pShield", _shield); Read(tag, "pShieldCd", _shieldCooldown); _sharedCooldown = tag.GetInt("pSharedCd"); Read(tag, "pManaPaid", _manaPaid); Read(tag, "pManaWindow", _manaWindow); Read(tag, "pEnergy", _energyPaid); Read(tag, "pBuff", _buffTime); int[] ready = tag.ContainsKey("pManaReady") ? tag.GetIntArray("pManaReady") : Array.Empty<int>(); for (int i = 0; i < Math.Min(ready.Length, _manaReady.Length); i++) _manaReady[i] = ready[i] != 0; }
    private static void Read(Terraria.ModLoader.IO.TagCompound tag, string key, int[] values) { int[] saved = tag.ContainsKey(key) ? tag.GetIntArray(key) : Array.Empty<int>(); Array.Copy(saved, values, Math.Min(saved.Length, values.Length)); }
}

public sealed class SuitPassiveProjectile : ModProjectile
{
    private static readonly string[] Variants = { "", "NightmarePairedArrow", "AbyssPairedArrow", "DarkPairedBolt" };
    public override string Texture => Mod.Name + "/Content/Projectiles/DarkPairedBolt";
    public override void SetDefaults() { Projectile.width = 24; Projectile.height = 24; Projectile.friendly = true; Projectile.DamageType = DamageClass.Ranged; Projectile.penetrate = 1; Projectile.timeLeft = 60; Projectile.tileCollide = true; Projectile.usesLocalNPCImmunity = true; Projectile.localNPCHitCooldown = 10; }
    public override void PostAI()
    {
        bool ownerAuthority = Main.netMode == NetmodeID.SinglePlayer || Projectile.owner == Main.myPlayer;
        int id = (int)Projectile.ai[0];
        bool abyss = (int)Projectile.ai[1] == 2;
        if (id == -2 || !IsChaseTarget(id))
        {
            if (!abyss) { if (ownerAuthority) Projectile.Kill(); return; }
            if (!ownerAuthority) return;
            int oldTarget = id;
            id = FindAbyssTarget();
            Projectile.ai[0] = id >= 0 ? id : -1;
            if ((int)Projectile.ai[0] != oldTarget) Projectile.netUpdate = true;
            if (id < 0) { Projectile.rotation = Projectile.velocity.ToRotation(); return; }
        }
        Projectile.velocity = (Main.npc[id].Center - Projectile.Center).SafeNormalize(Vector2.UnitX) * 12f;
        Projectile.rotation = Projectile.velocity.ToRotation();
    }

    private bool IsChaseTarget(int id) => id >= 0 && id < Main.maxNPCs && Main.npc[id].active && Main.npc[id].CanBeChasedBy(Projectile);
    private int FindAbyssTarget()
    {
        int previousA = (int)Projectile.localAI[0] - 1, previousB = (int)Projectile.localAI[1] - 1;
        int bestId = -1;
        float bestDistance = 700f * 700f;
        foreach (NPC npc in Main.npc)
        {
            if (!npc.active || !npc.CanBeChasedBy(Projectile) || npc.whoAmI == previousA || npc.whoAmI == previousB) continue;
            float distance = Vector2.DistanceSquared(npc.Center, Projectile.Center);
            if (distance < bestDistance) { bestDistance = distance; bestId = npc.whoAmI; }
        }
        return bestId;
    }

    public override void OnHitNPC(NPC target, NPC.HitInfo hit, int damageDone)
    {
        if (!Main.dedServ && Projectile.owner == Main.myPlayer)
        {
            Color[] burst = (int)Projectile.ai[1] switch
            {
                1 => new[] { new Color(196, 210, 235), new Color(196, 53, 75) },
                2 => new[] { new Color(42, 66, 143), new Color(198, 54, 48), new Color(234, 184, 78) },
                _ => new[] { new Color(80, 56, 142), new Color(201, 51, 103) }
            };
            for (int i = 0; i < 8; i++)
            {
                int d = Dust.NewDust(target.position, target.width, target.height, DustID.Torch,
                    Main.rand.NextFloat(-2.8f, 2.8f), Main.rand.NextFloat(-2.8f, 2.8f), 0,
                    burst[i % burst.Length], Main.rand.NextFloat(.8f, 1.15f));
                Main.dust[d].noGravity = true;
                Main.dust[d].noLight = true;
            }
        }
        if ((int)Projectile.ai[1] != 2) return;
        Projectile.localAI[1] = Projectile.localAI[0];
        Projectile.localAI[0] = target.whoAmI + 1;
        Projectile.ai[0] = -2;
        Projectile.netUpdate = true;
    }

    public override void ModifyHitNPC(NPC target, ref NPC.HitModifiers modifiers) => modifiers.DisableCrit();
    public override bool PreDraw(ref Color lightColor)
    {
        int v = Math.Clamp((int)Projectile.ai[1], 1, Variants.Length - 1);
        Texture2D texture = ModContent.Request<Texture2D>(Mod.Name + "/Content/Projectiles/" + Variants[v], ReLogic.Content.AssetRequestMode.ImmediateLoad).Value;
        Projectile.GetGlobalProjectile<MilkcraftPairedProjectile>().DrawVfxTrail(Projectile, v == 1 ? 3 : v == 2 ? 4 : 8,
            Mod.Name + "/Content/Projectiles/" + Variants[v]);
        MilkcraftPairedProjectile.DrawBodyGlow(Projectile, texture, v == 1 ? 3 : v == 2 ? 4 : 8);
        return false;
    }
    public override bool? CanHitNPC(NPC target) => target.active && !target.friendly && !target.dontTakeDamage && !target.immortal && target.lifeMax > 5;
}

/// <summary>Instance marks preserve ammo projectile type and its native hit effects.</summary>
public sealed class MilkcraftPairedProjectile : GlobalProjectile
{
    internal static bool DiagnosticLightCapture { get; set; }
    internal static int DiagnosticLightCalls { get; private set; }
    internal static Vector2 DiagnosticLastLightPosition { get; private set; }
    internal static Vector3 DiagnosticLastMainLightColor { get; private set; }
    internal static Vector3 DiagnosticLastAccentLightColor { get; private set; }
    internal static void ResetDiagnosticLights() { DiagnosticLightCalls = 0; DiagnosticLastLightPosition = Vector2.Zero; DiagnosticLastMainLightColor = Vector3.Zero; DiagnosticLastAccentLightColor = Vector3.Zero; DiagnosticLightCapture = true; }
    internal static bool DiagnosticPostAiCapture { get; set; }
    public override bool InstancePerEntity => true;
    public bool Paired, Homing, PassiveProjectile;
    public int TextureVariant;
    private static readonly string[] TextureNames = { "", "IronPairedArrow", "HuntingPairedArrow", "NightmarePairedArrow", "AbyssPairedArrow", "IronPairedBolt", "EnergyPairedBolt", "FallenPairedBolt", "DarkPairedBolt" };
    private int _target = -1;
    private int _diagnosticPostAiCalls, _diagnosticFirstNumUpdates = int.MinValue, _diagnosticLastNumUpdates = int.MinValue, _diagnosticStyle, _vfxAppendCount;
    internal string DiagnosticPostAiTrace => $"calls={_diagnosticPostAiCalls} firstNumUpdates={_diagnosticFirstNumUpdates} lastNumUpdates={_diagnosticLastNumUpdates} style={_diagnosticStyle}";
    internal int DiagnosticPostAiCalls => _diagnosticPostAiCalls;
    internal int DiagnosticFirstNumUpdates => _diagnosticFirstNumUpdates;
    internal int DiagnosticLastNumUpdates => _diagnosticLastNumUpdates;
    internal void ResetDiagnosticPostAiTrace() { _diagnosticPostAiCalls = 0; _diagnosticFirstNumUpdates = _diagnosticLastNumUpdates = int.MinValue; _diagnosticStyle = 0; }
    internal int DiagnosticAppendCount => _vfxAppendCount;
    internal void ResetDiagnosticTrailHistory()
    {
        _vfxCount = 0;
        _vfxAppendCount = 0;
        Array.Clear(_vfxPositions, 0, _vfxPositions.Length);
        Array.Clear(_vfxRotations, 0, _vfxRotations.Length);
    }
    private readonly Vector2[] _vfxPositions = new Vector2[10];
    private readonly float[] _vfxRotations = new float[10];
    private int _vfxCount;
    internal int DiagnosticTarget => _target;
    public override void OnSpawn(Projectile projectile, IEntitySource source)
    {
        ResetDiagnosticPostAiTrace();
        ResetDiagnosticTrailHistory();
        DiagnosticPostAiCapture = Environment.GetEnvironmentVariable("MILKCRAFT_TERRARIA_PASSIVE_DIAGNOSTIC") == "1";
        if (source is not IEntitySource_WithStatsFromItem use || projectile.owner < 0 || projectile.owner >= Main.maxPlayers) return;
        Player player = Main.player[projectile.owner];
        string weapon = use.Item.ModItem?.Name ?? "";
        (string accessory, string texture, bool home) = ResolveWeaponPair(weapon);
        if (accessory == "" || !player.GetModPlayer<MilkcraftPassivePlayer>().HasFunctionalAccessory(accessory)) return;
        Paired = true; Homing = home; TextureVariant = Array.IndexOf(TextureNames, texture);
        // Use only the RGB light emitted by this paired variant; suppress the
        // inherited neutral/white light from the original ammo projectile.
        projectile.light = 0f;
        projectile.netUpdate = true;
    }
    public static (string Accessory, string Texture, bool Homing) ResolveWeaponPair(string weapon) => weapon switch
    {
        "IronBow" => ("IronQuiver", "IronPairedArrow", false), "HuntingBow" => ("HuntingQuiver", "HuntingPairedArrow", false),
        "NightmareBow" => ("NightmareQuiver", "NightmarePairedArrow", true), "AbyssBow" => ("AbyssQuiver", "AbyssPairedArrow", true),
        "IronStaff" => ("BeginnerTome", "IronPairedBolt", false), "EnergyStaff" => ("EnergyTome", "EnergyPairedBolt", false),
        "FallenStaff" => ("FallenTome", "FallenPairedBolt", true), "DarkStaff" => ("DarkTome", "DarkPairedBolt", true), _ => ("", "", false)
    };
    public static bool AccessoryMatchesWeapon(string weapon, string accessory) => ResolveWeaponPair(weapon).Accessory == accessory && accessory.Length > 0;
    private void UpdateHoming(Projectile projectile)
    {
        if (!Paired || !Homing || projectile.owner < 0 || projectile.owner >= Main.maxPlayers) return;
        if (Main.netMode == NetmodeID.Server || (Main.netMode == NetmodeID.MultiplayerClient && projectile.owner != Main.myPlayer)) return;
        int previousTarget = _target;
        NPC target = _target >= 0 && _target < Main.maxNPCs ? Main.npc[_target] : null;
        if (target == null || !target.active || !target.CanBeChasedBy(projectile) || Vector2.DistanceSquared(target.Center, projectile.Center) > 700f * 700f || !Collision.CanHitLine(projectile.Center, 1, 1, target.Center, 1, 1))
        {
            _target = -1; float best = 700f * 700f;
            foreach (NPC n in Main.npc) if (n.active && n.CanBeChasedBy(projectile) && Vector2.DistanceSquared(n.Center, projectile.Center) < best && Collision.CanHitLine(projectile.Center, 1, 1, n.Center, 1, 1)) { _target = n.whoAmI; target = n; best = Vector2.DistanceSquared(n.Center, projectile.Center); }
            if (_target != previousTarget) projectile.netUpdate = true;
        }
        if (target == null || _target < 0) return;
        float speed = projectile.velocity.Length(), angle = projectile.velocity.ToRotation(), desired = (target.Center - projectile.Center).ToRotation();
        if (speed < .1f) return;
        float turn = MathHelper.Clamp(MathHelper.WrapAngle(desired - angle), -.035f, .035f) / Math.Max(1, projectile.extraUpdates + 1);
        projectile.velocity = new Vector2((float)Math.Cos(angle + turn), (float)Math.Sin(angle + turn)) * speed;
        projectile.rotation = projectile.velocity.ToRotation();
    }
    public override bool PreDraw(Projectile projectile, ref Color lightColor)
    {
        int style = GetVfxStyle(projectile);
        if (style == 0 || projectile.ModProjectile is SuitPassiveProjectile) return true;
        string path = Paired && TextureVariant > 0 && TextureVariant < TextureNames.Length
            ? Mod.Name + "/Content/Projectiles/" + TextureNames[TextureVariant]
            : projectile.ModProjectile?.Texture;
        DrawTrail(projectile, style, path);
        if (IsDefaultMagicBolt(projectile) && !string.IsNullOrEmpty(path))
        {
            Texture2D boltTexture = ModContent.Request<Texture2D>(path, ReLogic.Content.AssetRequestMode.ImmediateLoad).Value;
            DrawBodyGlow(projectile, boltTexture, style);
            return false;
        }
        if (!Paired || TextureVariant <= 0 || TextureVariant >= TextureNames.Length) return true;
        Texture2D texture = ModContent.Request<Texture2D>(path, ReLogic.Content.AssetRequestMode.ImmediateLoad).Value;
        DrawBodyGlow(projectile, texture, style);
        return false;
    }
    public override void PostAI(Projectile projectile)
    {
        int style = GetVfxStyle(projectile);
        if (DiagnosticPostAiCapture)
        {
            if (_diagnosticPostAiCalls == 0) _diagnosticFirstNumUpdates = projectile.numUpdates;
            _diagnosticPostAiCalls++;
            _diagnosticLastNumUpdates = projectile.numUpdates;
            _diagnosticStyle = style;
        }
        UpdateHoming(projectile);
        if (style == 0) return;
        if (projectile.numUpdates != -1) return;
        Array.Copy(_vfxPositions, 0, _vfxPositions, 1, _vfxPositions.Length - 1);
        Array.Copy(_vfxRotations, 0, _vfxRotations, 1, _vfxRotations.Length - 1);
        _vfxPositions[0] = projectile.Center;
        _vfxRotations[0] = VfxRotation(projectile);
        _vfxCount = Math.Min(_vfxCount + 1, _vfxPositions.Length);
        _vfxAppendCount++;
        Vector3 color = VfxColor(style);
        Color accent = VfxAccentColor(style);
        Lighting.AddLight(projectile.Center, color * .38f);
        Lighting.AddLight(projectile.Center, accent.ToVector3() * .12f);
        if (DiagnosticLightCapture)
        {
            DiagnosticLightCalls++;
            DiagnosticLastLightPosition = projectile.Center;
            DiagnosticLastMainLightColor = color;
            DiagnosticLastAccentLightColor = accent.ToVector3();
        }
        if (Main.dedServ || !Main.rand.NextBool(3)) return;
        Color dustColor = Main.rand.NextBool(4) ? VfxAccentColor(style) : new Color(VfxColor(style));
        Vector2 back = projectile.velocity.SafeNormalize(Vector2.UnitX) * -4f;
        Vector2 jitter = new(Main.rand.NextFloat(-.6f, .6f), Main.rand.NextFloat(-.6f, .6f));
        Dust dust = Dust.NewDustPerfect(projectile.Center + back, DustID.Torch,
            projectile.velocity * -.08f + jitter, 0, dustColor, .85f);
        dust.noGravity = true;
        dust.noLight = true;
    }

    internal void DrawVfxTrail(Projectile projectile, int style, string texturePath)
        => DrawTrail(projectile, style, texturePath);

    internal static void DrawBodyGlow(Projectile projectile, Texture2D texture, int style)
    {
        Vector2 center = projectile.Center - Main.screenPosition;
        Vector2 origin = texture.Size() * .5f;
        float rotation = VfxRotation(projectile);
        Color main = new(VfxColor(style));
        Color accent = VfxAccentColor(style);
        float radialScale = Math.Max(texture.Width, texture.Height) / 48f * projectile.scale;
        PassiveVfxTextureSystem.DrawProjectileGlow(Main.spriteBatch, center, main, radialScale * 1.55f, .43f);
        PassiveVfxTextureSystem.DrawProjectileGlow(Main.spriteBatch, center, accent, radialScale * .86f, .26f);
        // Preserve the original projectile artwork at its native RGB values.
        Main.spriteBatch.Draw(texture, center, null, Color.White, rotation, origin, projectile.scale, SpriteEffects.None, 0f);
    }

    private static bool IsDefaultMagicBolt(Projectile projectile) => projectile.ModProjectile?.GetType().Name is "EmberBolt" or "EnergyBolt" or "FallenBolt" or "DarkBolt";
    private static float VfxRotation(Projectile projectile)
        => IsDefaultMagicBolt(projectile) && !projectile.GetGlobalProjectile<MilkcraftPairedProjectile>().Paired
            ? projectile.rotation
            : projectile.velocity.ToRotation();

    private void DrawTrail(Projectile projectile, int style, string texturePath)
    {
        if (Main.dedServ || style == 0 || _vfxCount < 2 || string.IsNullOrEmpty(texturePath)) return;
        Texture2D ghost = ModContent.Request<Texture2D>(texturePath, ReLogic.Content.AssetRequestMode.ImmediateLoad).Value;
        Texture2D pixel = Terraria.GameContent.TextureAssets.MagicPixel.Value;
        Color tintColor = new Color(VfxColor(style));
        Color accentColor = VfxAccentColor(style);
        Vector2 origin = ghost.Size() * .5f;
        int count = Math.Min(_vfxCount, 8);
        for (int i = count - 1; i >= 1; i--)
        {
            float fade = 1f - i / (float)count;
            Vector2 previous = _vfxPositions[i];
            Vector2 current = _vfxPositions[i - 1];
            Vector2 delta = current - previous;
            float length = delta.Length();
            if (length < .25f) continue;
            float angle = delta.ToRotation();
            Color lineColor = tintColor * (.10f + .21f * fade);
            Main.spriteBatch.Draw(pixel, (previous + current) * .5f - Main.screenPosition, new Rectangle(0, 0, 1, 1),
                lineColor, angle, new Vector2(.5f), new Vector2(length, 1.2f + 1.6f * fade), SpriteEffects.None, 0f);
            if (i % 2 == 0)
                Main.spriteBatch.Draw(pixel, (previous + current) * .5f - Main.screenPosition, new Rectangle(0, 0, 1, 1),
                    accentColor * (.10f + .12f * fade), angle, new Vector2(.5f), new Vector2(length * .72f, .65f + fade * .45f), SpriteEffects.None, 0f);
            Main.spriteBatch.Draw(ghost, previous - Main.screenPosition, null, tintColor * (.09f + .11f * fade),
            _vfxRotations[i], origin, projectile.scale * (.68f + .20f * fade), SpriteEffects.None, 0f);
        }
    }

    private int GetVfxStyle(Projectile projectile)
    {
        if (Paired && TextureVariant > 0) return TextureVariant;
        if (projectile.ModProjectile is SuitPassiveProjectile) return (int)projectile.ai[1] switch { 1 => 3, 2 => 4, 3 => 8, _ => 0 };
        return projectile.ModProjectile?.GetType().Name switch
        {
            "EmberBolt" => 9, "EnergyBolt" => 10, "FallenBolt" => 11, "DarkBolt" => 12,
            _ => 0
        };
    }

    private static Vector3 VfxColor(int style) => style switch
    {
        1 => new Vector3(.85f, .69f, .27f), 2 => new Vector3(.15f, .62f, .91f),
        3 => new Vector3(.93f, .75f, .23f), 4 => new Vector3(.86f, .15f, .24f),
        5 => new Vector3(.95f, .55f, .13f), 6 => new Vector3(.67f, .31f, .89f),
        7 => new Vector3(.92f, .73f, .22f), 8 => new Vector3(.86f, .16f, .27f),
        9 => new Vector3(.95f, .55f, .13f), 10 => new Vector3(.67f, .31f, .89f),
        11 => new Vector3(.92f, .73f, .22f), 12 => new Vector3(.86f, .16f, .27f),
        _ => Vector3.One
    };
    private static Color VfxAccentColor(int style) => style switch
    {
        1 => new Color(217, 212, 170), 2 => new Color(79, 208, 240),
        3 => new Color(222, 52, 79), 4 => new Color(68, 70, 165),
        5 => new Color(217, 75, 23), 6 => new Color(202, 129, 245),
        7 => new Color(54, 203, 232), 8 => new Color(98, 64, 178),
        9 => new Color(217, 75, 23), 10 => new Color(202, 129, 245),
        11 => new Color(54, 203, 232), 12 => new Color(98, 64, 178),
        _ => Color.White
    };
    public override void SendExtraAI(Projectile projectile, BitWriter bitWriter, System.IO.BinaryWriter writer) { writer.Write(Paired); writer.Write(Homing); writer.Write(PassiveProjectile); writer.Write(TextureVariant); writer.Write(_target); }
    public override void ReceiveExtraAI(Projectile projectile, BitReader bitReader, System.IO.BinaryReader reader) { Paired = reader.ReadBoolean(); Homing = reader.ReadBoolean(); PassiveProjectile = reader.ReadBoolean(); TextureVariant = reader.ReadInt32(); _target = reader.ReadInt32(); }
    public override void ModifyHitNPC(Projectile projectile, NPC target, ref NPC.HitModifiers modifiers)
    {
        if (PassiveProjectile) { modifiers.DisableCrit(); return; }
        if (projectile.owner < 0 || projectile.owner >= Main.maxPlayers || !projectile.DamageType.CountsAsClass(DamageClass.Ranged)) return;
        MilkcraftPassivePlayer p = Main.player[projectile.owner].GetModPlayer<MilkcraftPassivePlayer>();
        if (p.WouldApplyHuntingBonus(target)) modifiers.SourceDamage *= 1f + (p.IsPaired ? .25f : .15f);
    }
}

/// <summary>Uses the engine's real mana-consumed callback and applies quiver speed to any arrow bow.</summary>
public sealed class MilkcraftPassiveGlobalItem : GlobalItem
{
    public override void OnConsumeMana(Item item, Player player, int manaConsumed)
    {
        player.GetModPlayer<MilkcraftPassivePlayer>().RecordManaConsumed(item, manaConsumed);
    }

    public override void ModifyShootStats(Item item, Player player, ref Vector2 position, ref Vector2 velocity, ref int type, ref int damage, ref float knockback)
    {
        if (item.useAmmo != AmmoID.Arrow) return;
        MilkcraftPassivePlayer passive = player.GetModPlayer<MilkcraftPassivePlayer>();
        if (passive.HasFunctionalAccessory("IronQuiver") || passive.HasFunctionalAccessory("HuntingQuiver") ||
            passive.HasFunctionalAccessory("NightmareQuiver") || passive.HasFunctionalAccessory("AbyssQuiver"))
            velocity *= 1.10f;
    }
}
