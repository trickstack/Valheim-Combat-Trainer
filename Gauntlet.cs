// Combat Trainer gauntlet: random, realistic encounters from any biome you know, one after another (arena only).
// - Your gear doesn't matter: each encounter hands you a rolled loadout of that biome's tier (your 3 weapon
//   categories in pick order, the tier's shield, armor of your style, tier food, skills set for the biome).
// - Health doesn't matter either: you can take 3 hits per encounter (Hard 2, Very hard 1). A hit is a creature's
//   attack that lands (not blocked, parried or dodged); burning/poison ticks never count. Clear it = new budget.
// - Marked enemies (violet aura, orange name, bar always shown) can't be blocked, parried or staggered.
// - Dying shows a report of every hit; Retry replays the same encounter with the same loadout and spawn spots.
using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using Random = UnityEngine.Random;

namespace CombatTrainer {
    class GGroup { public string Prefab; public int Count = 1, Stars; public bool Marked; public char When = 't'; public float Arg; }
    class GEncounter { public string Name; public int Biome, Weight; public List<GGroup> Groups = new List<GGroup>(); }
    class GItem { public string Prefab; public int Quality = 1; }
    class GHit { public float Time; public string Who, With; public bool Marked; }

    static class Gauntlet {
        // biome index: 0 Black Forest, 1 Swamp, 2 Mountains, 3 Plains, 4 Mistlands, 5 Ashlands, 6 Deep North
        static readonly string[] BiomeNames = { "Black Forest", "Swamp", "Mountains", "Plains", "Mistlands", "Ashlands", "Deep North" };
        static readonly string[] BiomeKeys = { "blackforest", "swamp", "mountain", "plains", "mistlands", "ashlands", "deepnorth" };
        static readonly int[] SkillLevel = { 18, 27, 35, 42, 50, 60, 70 };

        // weapon categories: the best item of each biome's tier (the one before = the previous entry)
        static readonly string[] Cats = { "Sword", "Axe", "Club", "Knife", "Spear", "Polearm", "Battleaxe", "Sledge", "Fists", "Bow", "Crossbow", "Staff" };
        static readonly string[][] Tier = {
            new[] { "SwordBronze", "SwordIron", "SwordSilver", "SwordBlackmetal", "SwordMistwalker", "SwordNiedhogg", "SwordGold" },
            new[] { "AxeBronze", "AxeIron", "AxeIron", "AxeBlackMetal", "AxeJotunBane", "AxeJotunBane", "AxeGold" },
            new[] { "MaceBronze", "MaceIron", "MaceSilver", "MaceNeedle", "MaceNeedle", "MaceEldner", "MaceGold" },
            new[] { "KnifeCopper", "KnifeChitin", "KnifeSilver", "KnifeBlackMetal", "KnifeSkollAndHati", "KnifeSkollAndHati", "KnifeGold" },
            new[] { "SpearBronze", "SpearElderbark", "SpearWolfFang", "SpearWolfFang", "SpearCarapace", "SpearSplitner", "SpearGold" },
            new[] { "AtgeirBronze", "AtgeirIron", "AtgeirIron", "AtgeirBlackmetal", "AtgeirHimminAfl", "AtgeirHimminAfl", "AtgeirGold" },
            new[] { "AxeEarly", "Battleaxe", "BattleaxeCrystal", "BattleaxeBlackmetal", "BattleaxeSkullSplittur", "AxeBerzerkr", "BattleaxeGold" },
            new[] { "SledgeStagbreaker", "SledgeIron", "SledgeIron", "SledgeIron", "SledgeDemolisher", "SledgeDemolisher", "SledgeGold" },
            new[] { "FistBjornClaw", "FistBjornClaw", "FistFenrirClaw", "FistBjornUndeadClaw", "FistBjornUndeadClaw", "FistBjornUndeadClaw", "FistGold" },
            new[] { "BowFineWood", "BowHuntsman", "BowDraugrFang", "BowDraugrFang", "BowSpineSnap", "BowAshlands", "BowGold" },
            new[] { null, null, null, "CrossbowArbalest", "CrossbowArbalest", "CrossbowRipper", "CrossbowGold" },   // Mistlands on
            new[] { null, null, null, "StaffIceShards", "StaffFireball", "StaffClusterbomb", "StaffThunderBlood" }, // Mistlands on
        };
        const int BowCat = 9, CrossbowCat = 10, StaffCat = 11;
        static readonly string[] Arrows = { "ArrowBronze", "ArrowIron", "ArrowObsidian", "ArrowNeedle", "ArrowCarapace", "ArrowCharred", "ArrowBloodGold" };
        static readonly string[] Bolts = { null, null, null, null, "BoltCarapace", "BoltCharred", "BoltBloodGold" };
        // parry shields: the tier's buckler where one exists
        static readonly string[] Shields = { "ShieldBronzeBuckler", "ShieldIronBuckler", "ShieldSilver", "ShieldBlackmetal", "ShieldCarapaceBuckler", "ShieldFlametal", "ShieldGoldBuckler" };
        static readonly string[] ArmorStyles = { "Light", "Heavy", "Mage" };
        static readonly string[][][] Armor = { // [style][biome] = helmet, chest, legs, cape
            new[] {
                new[] { "HelmetTrollLeather", "ArmorTrollLeatherChest", "ArmorTrollLeatherLegs", "CapeTrollHide" },
                new[] { "HelmetRoot", "ArmorRootChest", "ArmorRootLegs", "CapeTrollHide" },
                new[] { "HelmetFenring", "ArmorFenringChest", "ArmorFenringLegs", "CapeWolf" },
                new[] { "HelmetFenring", "ArmorFenringChest", "ArmorFenringLegs", "CapeLox" },
                new[] { "HelmetFenring", "ArmorFenringChest", "ArmorFenringLegs", "CapeFeather" },
                new[] { "HelmetAshlandsMediumHood", "ArmorAshlandsMediumChest", "ArmorAshlandsMediumlegs", "CapeAsh" },
                new[] { "HelmetDNMediumHood", "ArmorDeepNorthMediumChest", "ArmorDeepNorthMediumlegs", "CapeDeepNorth" } },
            new[] {
                new[] { "HelmetBronze", "ArmorBronzeChest", "ArmorBronzeLegs", "CapeTrollHide" },
                new[] { "HelmetIron", "ArmorIronChest", "ArmorIronLegs", "CapeTrollHide" },
                new[] { "HelmetDrake", "ArmorWolfChest", "ArmorWolfLegs", "CapeWolf" },
                new[] { "HelmetPadded", "ArmorPaddedCuirass", "ArmorPaddedGreaves", "CapeLox" },
                new[] { "HelmetCarapace", "ArmorCarapaceChest", "ArmorCarapaceLegs", "CapeFeather" },
                new[] { "HelmetFlametal", "ArmorFlametalChest", "ArmorFlametalLegs", "CapeAsh" },
                new[] { "HelmetDNHeavy", "ArmorDeepNorthHeavyChest", "ArmorDeepNorthHeavylegs", "CapeDeepNorth" } },
            new[] { // mage armor exists from Mistlands; before that mage players wear light armor
                null, null, null, null,
                new[] { "HelmetMage", "ArmorMageChest", "ArmorMageLegs", "CapeFeather" },
                new[] { "HelmetMage_Ashlands", "ArmorMageChest_Ashlands", "ArmorMageLegs_Ashlands", "CapeAsh" },
                new[] { "HelmetDNMage", "ArmorDeepNorthMageChest", "ArmorDeepNorthMagelegs", "CapeDeepNorthMage" } },
        };
        static readonly string[][] Foods = {
            new[] { "DeerStew", "MinceMeatSauce", "CarrotSoup" },
            new[] { "Sausages", "BlackSoup", "TurnipStew" },
            new[] { "WolfMeatSkewer", "Eyescream", "OnionSoup" },
            new[] { "LoxPie", "FishWraps", "BloodPudding" },
            new[] { "MisthareSupreme", "HoneyGlazedChicken", "MushroomOmelette" },
            new[] { "FierySvinstew", "MashedMeat", "ScorchingMedley" },
            new[] { "MooseKebab", "SmokedMooseMeat", "Pancakes" },
        };
        static readonly string[] EitrFoods = { null, null, null, null, "YggdrasilPorridge", "SizzlingBerryBroth", "MeatballsMashedPoteitr" };

        // encounters: name|weight|group;group;...   group = prefab count [m=marked] [s1/s2=stars] [when]
        // when (relative to the group before): tN = N s after it spawned, hN = once its health is down to N%,
        // c = once everything before is dead. No when = together with the group before.
        static readonly string[][] Defs = {
            new[] { "Troll on the path|4|Troll 1;Greydwarf 3 h60", "Bear and a shaman|4|Bjorn 1;Greydwarf_Shaman 1 p65;Greydwarf 2",
                    "Brute escort|3|Greydwarf_Elite 1 m;Greydwarf 2 h50", "Crypt rush|3|Skeleton 3;Bjorn 1 m p50", "Troll AND bear|1|Troll 1;Bjorn 1 p75" },
            new[] { "Abomination|3|Abomination 1;Draugr 2 h60", "Elite pair|4|Draugr_Elite 1 m;Draugr_Elite 1;Draugr_Ranged 2 p60",
                    "Wraith at night|3|Wraith 1 m;Draugr 3 p65", "Bog crawl|3|Draugr 3;BlobElite 1 p75;Draugr_Elite 1 m h50" },
            new[] { "Drakes, wolves, golem|3|Hatchling 2;Wolf 3 p50;StoneGolem 1 m c;Wolf 3 h50", "Cultist and ulvs|4|Fenring_Cultist 1;Ulv 3 p80",
                    "Fenring pack|3|Fenring 1 m;Fenring 1;Wolf 2 p65", "Golem and drakes|3|StoneGolem 1;Hatchling 2 h60" },
            new[] { "Lox and a berserker|3|Lox 1;GoblinBrute 1 h60", "Lox and fulings|3|Lox 1;Goblin 3 h60",
                    "Fuling camp|4|Goblin 3;GoblinShaman 1;GoblinBrute 1 m p50", "Deathsquitos|2|Deathsquito 2;Goblin 2 p80", "Vile and archers|3|Unbjorn 1 m;GoblinArcher 2 p65" },
            new[] { "Seeker, then a soldier|4|Seeker 1;SeekerBrute 1 m p60", "Soldier, then a seeker|4|SeekerBrute 1;Seeker 1 m h60",
                    "Gjall overhead|2|Seeker 2;Tick 2;Gjall 1 p40", "Brood swarm|3|SeekerBrood 4;SeekerBrute 1 p75" },
            new[] { "Charred patrol|4|Charred_Melee 2;Charred_Archer 1;Charred_Mage 1 p50", "Asksvin herd|3|Asksvin 2;Charred_Melee 1 m p65",
                    "Morgen|3|Morgen 1;Charred_Twitcher 2 h60", "Fallen Valkyrie|2|FallenValkyrie 1;Volture 2 h50" },
            new[] { "Jotun warrior|3|JotunWarrior 1;Greydwarf_Frozen 3 h60", "Witch's guard|3|JotunWitch 1;Skeleton_DeepNorth 2;JotunWarriorDualWield 1 m p50",
                    "Frost troll|3|TrollFrost 1;Frysling 3 h50", "Elk and a moose|3|Elaking 2;Moose 1 m p60", "Barka|2|Barka 1;ElakingMole 1 h50" },
        };
        static List<GEncounter> encounters;
        static List<GEncounter> All() {
            if (encounters != null) return encounters;
            encounters = new List<GEncounter>();
            for (int b = 0; b < Defs.Length; b++)
                foreach (var d in Defs[b]) {
                    var f = d.Split('|');
                    var e = new GEncounter { Name = f[0], Weight = int.Parse(f[1]), Biome = b };
                    foreach (var gs in f[2].Split(';')) {
                        var t = gs.Trim().Split(' ');
                        var g = new GGroup { Prefab = t[0], Count = int.Parse(t[1]) };
                        foreach (var tok in t.Skip(2)) {
                            if (tok == "m") g.Marked = true;
                            else if (tok == "c") g.When = 'c';
                            else if (tok[0] == 's') g.Stars = int.Parse(tok.Substring(1));
                            else { g.When = tok[0]; g.Arg = float.Parse(tok.Substring(1), System.Globalization.CultureInfo.InvariantCulture); }
                        }
                        if (g.Marked) g.Stars = 0; // marked are never starred: a marked 2-star is just a slow kill
                        e.Groups.Add(g);
                    }
                    encounters.Add(e);
                }
            return encounters;
        }

        // ---- settings ----
        static readonly string[] DiffNames = { "Normal", "Hard", "Very Hard" };
        static readonly int[] HitBudget = { 3, 2, 1 }, GearBudget = { 6, 5, 4 };
        static bool onlyFound; // off: every biome from Black Forest up
        static int diff, style, runDiff, runStyle; // runDiff/runStyle: fixed for the run, so Retry is exact
        static readonly List<int> picks = new List<int>();
        static List<int> runPicks = new List<int>(); // frozen at the start of a run

        // ---- run state ----
        internal static bool Running;
        static GEncounter enc;
        static List<GItem> loadout;
        static int seed, cleared, hitsLeft, nextGroup;
        static float breakLeft, encTime, groupTime;
        static bool retrying, needSetup;
        static readonly List<List<Character>> live = new List<List<Character>>();
        internal static readonly HashSet<Character> Marked = new HashSet<Character>();
        static readonly List<GHit> hits = new List<GHit>();
        static string result = "";
        static List<GHit> lastHits = new List<GHit>();
        static Vector3 anchorPos, anchorFwd;
        static Character lastAttacker; static float lastHitAt;

        // every creature near you, so adds that enemies spawn (Gjall ticks, summons) count too
        // what we spawned, wherever it ran off to, plus anything else (adds) within 60 m
        static IEnumerable<Character> Alive() {
            var p = Player.m_localPlayer;
            return Character.GetAllCharacters().Where(c => c != null && !(c is Player) && !c.IsDead() && c.GetComponent<BaseAI>() != null
                && (live.Any(l => l.Contains(c)) || (p != null && Vector3.Distance(c.transform.position, p.transform.position) < 60f))).ToList();
        }
        static IEnumerable<Character> AllCreatures() { return Character.GetAllCharacters().Where(c => c != null && !(c is Player) && c.GetComponent<BaseAI>() != null).ToList(); }
        internal static bool Owns(Character c) { return Running && c != null && !(c is Player); }

        // flyers a melee-only loadout can't reach: those encounters need a bow, crossbow, staff or spear
        static readonly string[] Flyers = { "Gjall", "Hatchling", "FallenValkyrie", "Volture" };
        static bool HaveRanged() { return runPicks.Any(i => i == BowCat || i == CrossbowCat || i == StaffCat || Cats[i] == "Spear"); }

        static List<int> KnownBiomes() {
            var known = Traverse.Create(Player.m_localPlayer).Field("m_knownBiome").GetValue<HashSet<string>>();
            var list = new List<int> { 0 }; // Black Forest is always in (Meadows never is)
            for (int b = 1; b < BiomeKeys.Length; b++) {
                if (!onlyFound) { list.Add(b); continue; }
                string key = "$biome_" + BiomeKeys[b], loc = Localization.instance.Localize(key);
                if (known != null && known.Any(k => k.Contains(key) || k.Contains(loc))) list.Add(b);
            }
            return list;
        }

        static void StartRun() {
            Trainer.Clear();
            cleared = 0; result = ""; enc = null; runDiff = diff; runStyle = style; runPicks = picks.ToList();
            Running = true; breakLeft = 0f; retrying = false; needSetup = true;
            Trainer.SetMenu(false);
        }

        internal static void Stop() { Trainer.Clear(); }

        internal static void Reset() { // all trainer creatures were just removed
            Running = false; Dying = false;
            live.Clear(); Marked.Clear();
            Feedback.Off();
        }

        internal static void Fell() {
            lastHits = hits.ToList();
            result = enc == null ? "" : "You fell to \"" + enc.Name + "\" (" + BiomeNames[enc.Biome] + ", " + DiffNames[runDiff] + ") after clearing " + cleared + " encounter" + (cleared == 1 ? "" : "s") + ".";
        }

        static void Retry() {
            var e = enc; var l = loadout; int s = seed, c = cleared;
            Trainer.Clear();
            enc = e; loadout = l; seed = s; cleared = c; result = "";
            Running = true; retrying = true; needSetup = true; breakLeft = 0f;
            Trainer.SetMenu(false);
        }

        // ---- encounter flow ----
        internal static void Update() {
            if (!Running || !Trainer.Live()) return;
            if (enc != null) Feedback.Tick(hitsLeft, HitBudget[runDiff], Shields[enc.Biome], breakLeft <= 0f && !Dying);
            if (Trainer.MenuOpen) return; // F8 pauses
            foreach (var c in Marked) if (c != null) Glow(c);
            if (needSetup) { // run start, retry, or the last encounter was cleared
                needSetup = false;
                if (!retrying) Roll();
                retrying = false;
                Begin();
                breakLeft = 4f;
                return;
            }
            if (toEquip.Count > 0) EquipPending();
            if (breakLeft > 0f) { breakLeft -= Time.deltaTime; if (breakLeft <= 0f) { encTime = 0f; groupTime = 0f; var t = Player.m_localPlayer.transform; anchorPos = t.position; anchorFwd = t.forward; anchorFwd.y = 0f; anchorFwd.Normalize(); } return; }
            encTime += Time.deltaTime; groupTime += Time.deltaTime;
            while (nextGroup < enc.Groups.Count && Ready(enc.Groups[nextGroup])) SpawnGroup(nextGroup++);
            if (nextGroup >= enc.Groups.Count && !Alive().Any()) { cleared++; needSetup = true; Feedback.Cleared(); }
        }

        static bool Ready(GGroup g) {
            if (nextGroup == 0) return true;
            var prev = live[nextGroup - 1];
            if (g.When == 'c') return !Alive().Any();
            if (g.When == 'p') { // progress into the fight so far: no clock, kiting never brings the next wave
                float hp = 0f, max = 0f;
                foreach (var grp in live) foreach (var c in grp) { if (c == null) continue; max += c.GetMaxHealth(); if (!c.IsDead()) hp += c.GetHealth(); }
                return max <= 0f || hp <= max * g.Arg / 100f;
            }
            if (g.When == 'h') {
                float hp = 0f, max = 0f;
                foreach (var c in prev) { if (c == null) continue; max += c.GetMaxHealth(); if (!c.IsDead()) hp += c.GetHealth(); }
                return max <= 0f || hp <= max * g.Arg / 100f;
            }
            return groupTime >= g.Arg; // 't' (only t0 is used: spawns together with the group before)
        }

        static void Roll() {
            var biomes = KnownBiomes();
            if (biomes.Count > 1 && enc != null) biomes.Remove(enc.Biome); // no back-to-back biome
            int b = biomes[Random.Range(0, biomes.Count)];
            bool ranged = HaveRanged();
            Func<GEncounter, bool> ok = e => ZNetScene.instance.GetPrefab(e.Groups[0].Prefab) != null && (ranged || !e.Groups.Any(g => Flyers.Contains(g.Prefab)));
            var pool = All().Where(e => e.Biome == b && ok(e)).ToList();
            if (pool.Count == 0) pool = All().Where(e => e.Biome == 0 && ok(e)).ToList();
            if (pool.Count > 1 && enc != null) pool.Remove(enc); // never the same one twice in a row
            int r = Random.Range(0, pool.Sum(e => e.Weight));
            enc = pool.Last();
            foreach (var e in pool) { r -= e.Weight; if (r < 0) { enc = e; break; } }
            seed = Random.Range(1, int.MaxValue);
            loadout = RollLoadout(enc.Biome, new System.Random(seed));
        }

        // all chosen weapons start at the previous tier, quality 1; the budget then buys tier-ups (2 points)
        // and quality levels (1 point) at random, so you're rarely fully upgraded
        static List<GItem> RollLoadout(int b, System.Random r) {
            var cats = runPicks.Count > 0 ? runPicks.ToList() : new List<int> { 0 };
            var cur = new List<bool>(); var q = new List<int>();
            for (int i = 0; i < cats.Count; i++) {
                if ((cats[i] == CrossbowCat || cats[i] == StaffCat) && b < 4) cats[i] = BowCat; // crossbow/staff: else bow
                cur.Add(b == 0); q.Add(1); // Black Forest has no tier before it
            }
            int left = GearBudget[runDiff] - cats.Count;
            for (int guard = 0; left > 0 && guard < 50; guard++) {
                int i = r.Next(cats.Count);
                var prefab = ObjectDB.instance.GetItemPrefab(Tier[cats[i]][b]);
                int maxQ = prefab != null ? prefab.GetComponent<ItemDrop>().m_itemData.m_shared.m_maxQuality : 4;
                if (!cur[i] && left >= 2 && r.Next(2) == 0) { cur[i] = true; left -= 2; }
                else if (q[i] < maxQ) { q[i]++; left--; }
            }
            var list = new List<GItem>();
            for (int i = 0; i < cats.Count; i++) list.Add(new GItem { Prefab = Tier[cats[i]][cur[i] ? b : Mathf.Max(0, b - 1)], Quality = q[i] });
            return list;
        }

        static void Begin() {
            Trainer.Clear(); Running = true; // Clear resets us: we stay running
            foreach (var c in AllCreatures()) { var nv = c.GetComponent<ZNetView>(); if (nv != null && nv.IsValid() && nv.IsOwner()) ZNetScene.instance.Destroy(c.gameObject); } // untagged adds
            hits.Clear(); hitsLeft = HitBudget[runDiff]; nextGroup = 0;
            ApplyLoadout();
            Player.m_localPlayer.Message(MessageHud.MessageType.Center, enc.Name + "  ·  " + BiomeNames[enc.Biome]);
        }

        static List<ItemDrop.ItemData> toEquip = new List<ItemDrop.ItemData>();
        // EquipItem says no while you attack or dodge (the killing blow that cleared the last encounter), so retry
        static void EquipPending() {
            var p = Player.m_localPlayer; if (p == null) return;
            toEquip.RemoveAll(it => p.IsItemEquiped(it) || !p.GetInventory().ContainsItem(it) || p.EquipItem(it, false));
        }

        // what we handed out last time, by role: if you moved it, the next loadout puts that role's item where you put it
        static readonly Dictionary<string, ItemDrop.ItemData> given = new Dictionary<string, ItemDrop.ItemData>();

        static void ApplyLoadout() {
            var p = Player.m_localPlayer; var inv = p.GetInventory(); int b = enc.Biome;
            var moved = new Dictionary<string, Vector2i>();
            foreach (var kv in given) if (inv.ContainsItem(kv.Value)) moved[kv.Key] = kv.Value.m_gridPos;
            given.Clear();
            p.UnequipAllItems();
            inv.RemoveAll();
            var equip = new List<ItemDrop.ItemData>();
            Func<string, string, int, int, int, int, ItemDrop.ItemData> put = (role, name, quality, stack, x, y) => {
                var it = name == null ? null : Kits.Make(name, quality, stack);
                if (it == null) return null;
                var at = new Vector2i(x, y); Vector2i m;
                bool ok = moved.TryGetValue(role, out m) ? inv.AddItem(it, m) : !moved.ContainsValue(at) && inv.AddItem(it, at);
                if (!ok && !inv.AddItem(it)) return null; // slot taken: first free one
                given[role] = it;
                return it;
            };
            for (int i = 0; i < loadout.Count; i++) { var it = put("w" + i, loadout[i].Prefab, loadout[i].Quality, 1, i, 0); if (i == 0 && it != null) equip.Add(it); }
            var shield = put("shield", Shields[b], 3, 1, 3, 0);
            put("hammer", "Hammer", 1, 1, 4, 0);
            if (shield != null && equip.Count > 0 && equip[0].m_shared.m_itemType == ItemDrop.ItemData.ItemType.OneHandedWeapon) equip.Add(shield);
            bool bow = loadout.Any(l => l.Prefab.StartsWith("Bow")), xbow = loadout.Any(l => l.Prefab.StartsWith("Crossbow"));
            if (bow) put("arrows", Arrows[b], 1, 100, 0, 1);
            if (xbow) put("bolts", Bolts[b], 1, 100, 1, 1);
            var set = Armor[runStyle][b] ?? Armor[0][b];
            for (int i = 0; i < set.Length; i++) { var it = put("armor" + i, set[i], 1, 1, i, 2); if (it != null) equip.Add(it); }
            var trinket = put("trinket", "TrinketBronzeStamina", 1, 1, set.Length, 2); // Bronze Pendant, every tier
            if (trinket != null) equip.Add(trinket);
            toEquip = equip;
            EquipPending();
            // tier foods; a staff swaps one for the tier's eitr food
            var foods = Foods[b].ToList();
            if (loadout.Any(l => l.Prefab.StartsWith("Staff")) && EitrFoods[b] != null) foods[2] = EitrFoods[b];
            p.ClearFood();
            for (int i = 0; i < foods.Count; i++) { var f = put("food" + i, foods[i], 1, 10, i, 3); if (f != null) p.EatFood(f); }
            Trainer.LastFoods = foods;
            foreach (var s in Kits.AllSkills(p)) { s.m_level = SkillLevel[b]; s.m_accumulator = 0f; }
            p.SetHealth(p.GetMaxHealth()); p.AddStamina(p.GetMaxStamina()); p.AddEitr(p.GetMaxEitr());
            Trainer.HealSoon();
        }

        static void SpawnGroup(int gi) {
            var g = enc.Groups[gi];
            groupTime = 0f;
            var list = new List<Character>();
            live.Add(list);
            var prefab = ZNetScene.instance.GetPrefab(g.Prefab);
            if (prefab == null) { Debug.LogWarning("[CombatTrainer] gauntlet: no creature " + g.Prefab); return; }
            // openers: around where you stood when the encounter began; reinforcements: around where you are now.
            // Each creature's spot comes from the seed alone, so Retry puts them in the same places.
            var p = Player.m_localPlayer.transform;
            Vector3 fwd = gi == 0 ? anchorFwd : p.forward; fwd.y = 0f; fwd.Normalize();
            Vector3 origin = gi == 0 ? anchorPos : p.position;
            float radius = Trainer.RadiusOf(prefab);
            for (int n = 0; n < g.Count; n++) {
                Vector3 pos = origin;
                var rng = new System.Random(seed * 31 + gi * 101 + n);
                for (int t = 0; t < 30; t++) {
                    // openers in front of you, 10-16 m; reinforcements arrive from the sides and behind, 18-26 m
                    float ang = gi == 0 ? (float)(rng.NextDouble() * 70 - 35) : (float)(100 + rng.NextDouble() * 160);
                    float dist = gi == 0 ? 10f + (float)rng.NextDouble() * 6f : 18f + (float)rng.NextDouble() * 8f;
                    pos = origin + Quaternion.Euler(0f, ang, 0f) * fwd * dist;
                    if (Alive().All(c => Vector3.Distance(c.transform.position, pos) > radius + c.GetRadius() + 0.75f)) break;
                }
                var ch = Trainer.SpawnAt(prefab, g.Stars, Trainer.Ground(pos));
                if (ch == null) continue;
                list.Add(ch);
                if (g.Marked) Mark(ch);
                var ai = ch.GetComponent<BaseAI>();
                if (ai != null) { ai.Alert(); ai.SetHuntPlayer(true); }
            }
        }

        // ---- marked enemies ----
        static void Mark(Character c) {
            Marked.Add(c);
            var go = new GameObject("ct_aura");
            go.transform.SetParent(c.transform, false);
            go.transform.position = c.GetCenterPoint();
            var l = go.AddComponent<Light>();
            l.type = LightType.Point; l.color = new Color(0.72f, 0.5f, 1f); l.range = 3f + c.GetRadius() * 3f; l.intensity = 2f;
        }

        static readonly MaterialPropertyBlock block = new MaterialPropertyBlock();
        static void Glow(Character c) {
            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * 6f); // about once a second
            var l = c.GetComponentInChildren<Light>();
            if (l != null && l.name == "ct_aura") l.intensity = 3f + 5f * pulse;
            foreach (var r in c.GetComponentsInChildren<Renderer>()) {
                if (!(r is SkinnedMeshRenderer || r is MeshRenderer)) continue;
                r.GetPropertyBlock(block);
                block.SetFloat("_Saturation", -0.7f); block.SetFloat("_Value", 0.3f);
                block.SetColor("_EmissionColor", new Color(0.72f, 0.5f, 1f) * (0.6f + 1.4f * pulse));
                r.SetPropertyBlock(block);
            }
        }

        // ---- hits (called from the damage patches below) ----
        internal static bool Dying;
        static Character pendingAttacker; static bool pendingBlocked, pendingCounted; 

        internal static void BeforeDamage(HitData hit) {
            pendingAttacker = hit.GetAttacker(); pendingBlocked = false; pendingCounted = false;
            if (Marked.Contains(pendingAttacker)) hit.m_blockable = false; // marked: no block, no parry
        }
        // a block only counts as a block if it took damage off: a guard break (stagger) lets the whole hit through
        // (the stagger check inside BlockAttack tells us; a resisting shield still lowers a broken block's damage)
        internal static bool InBlock, BlockStaggered;
        internal static void BeforeBlock() { InBlock = true; BlockStaggered = false; }
        internal static void AfterBlock(Humanoid me, bool result) { InBlock = false; pendingBlocked = result && !BlockStaggered && me.HaveStamina(); }

        // reached only when the hit wasn't dodged: past the block check, before resistances and armor
        const float ChipFraction = 0.15f; // ponytail: one flat cut for every biome; per-biome table if it feels off
        internal static void Landed(HitData hit) {
            var d = hit.m_damage; float raw = d.GetTotalDamage() - d.m_fire - d.m_poison - d.m_spirit;
            if (raw < ChipFraction * Player.m_localPlayer.GetMaxHealth()) return;
            if (pendingBlocked || Trainer.MenuOpen || hit.m_hitType != HitData.HitType.EnemyHit || !Owns(pendingAttacker) || hit.GetTotalDamage() <= 0f) return;
            if (pendingAttacker == lastAttacker && Time.time - lastHitAt < 0.15f) return; // one attack, two hit events (projectile + its splash)
            lastAttacker = pendingAttacker; lastHitAt = Time.time;
            pendingCounted = true;
            hitsLeft--;
            var h = pendingAttacker as Humanoid; var w = h != null ? h.GetCurrentWeapon() : null;
            string with = w == null ? "" : Localization.instance.Localize(w.m_shared.m_name);
            if (with.StartsWith("[") || with.Length == 0) with = w != null ? w.m_shared.m_name.TrimStart('$') : "";
            hits.Add(new GHit { Time = encTime, Who = Localization.instance.Localize(pendingAttacker.m_name), With = with, Marked = Marked.Contains(pendingAttacker) });
            if (hitsLeft <= 0) Dying = true;
            Feedback.Lost(pendingAttacker);
        }

        internal static void AfterDamage(Player p) {
            if (Dying && pendingCounted) { Dying = false; p.SetHealth(0f); } // dies on its next update
        }

        // ---- HUD ----
        internal static void Hud() {
            if (!Running || enc == null || !Trainer.Live()) return;
            string text = breakLeft > 0f ? enc.Name + "  ·  " + BiomeNames[enc.Biome] + "  ·  starts in " + Mathf.CeilToInt(breakLeft)
                : enc.Name + "  ·  cleared " + cleared;
            var st = new GUIStyle(Trainer.sHead) { fontSize = breakLeft > 0f ? 22 : 15, alignment = TextAnchor.MiddleCenter };
            var r = new Rect(0, 60, Screen.width, 34);
            var c = st.normal.textColor; st.normal.textColor = Color.black;
            GUI.Label(new Rect(r.x + 2, r.y + 2, r.width, r.height), text, st);
            st.normal.textColor = c; GUI.Label(r, text, st);
        }

        // ---- menu ----
        static Vector2 scrollReport;
        static string Label(string prefab) {
            var go = ObjectDB.instance.GetItemPrefab(prefab);
            return go == null ? prefab : Localization.instance.Localize(go.GetComponent<ItemDrop>().m_itemData.m_shared.m_name);
        }

        internal static void Draw() {
            if (!Trainer.IsTrainer(Player.m_localPlayer)) {
                GUILayout.Label("The gauntlet hands out gear, so it only works on trainer characters. Copy a character above first.", Trainer.sLbl);
                return;
            }
            if (Running) {
                GUILayout.BeginHorizontal();
                GUILayout.Label("Running: " + (enc != null ? enc.Name : "starting") + ", " + cleared + " cleared. Close the menu to carry on.", Trainer.sLbl);
                if (GUILayout.Button("Skip", Trainer.sBtn, GUILayout.Width(70), GUILayout.Height(30))) { Trainer.Clear(); Running = true; needSetup = true; breakLeft = 0f; }
                if (GUILayout.Button("Stop", Trainer.sBtn, GUILayout.Width(70), GUILayout.Height(30))) Stop();
                GUILayout.EndHorizontal();
            }
            if (result.Length > 0 && !Running) {
                GUILayout.Label("Report", Trainer.sHead);
                GUILayout.Label(result, Trainer.sLbl);
                if (loadout != null) GUILayout.Label("Your gear: " + string.Join(", ", loadout.Select(l => Label(l.Prefab) + " q" + l.Quality).ToArray()), Trainer.sDim);
                scrollReport = GUILayout.BeginScrollView(scrollReport, GUILayout.Height(110));
                foreach (var h in lastHits)
                    GUILayout.Label(string.Format("{0}:{1:00}   {2}{3}{4}", (int)h.Time / 60, (int)h.Time % 60, h.Who, h.Marked ? " <color=#c9a0ff>(marked)</color>" : "", h.With.Length > 0 ? "  ·  " + h.With : ""), Trainer.sLbl);
                GUILayout.EndScrollView();
                if (GUILayout.Button("Retry this encounter (same enemies, gear, and spots relative to you)", Trainer.sOn, GUILayout.Height(32))) Retry();
            }
            GUILayout.Label("Difficulty: hits you can take per encounter", Trainer.sHead);
            GUILayout.BeginHorizontal();
            for (int i = 0; i < DiffNames.Length; i++) if (GUILayout.Button(DiffNames[i] + " (" + HitBudget[i] + ")", diff == i ? Trainer.sOn : Trainer.sBtn, GUILayout.Height(30))) diff = i;
            GUILayout.EndHorizontal();
            GUILayout.Label("Armor", Trainer.sHead);
            GUILayout.BeginHorizontal();
            for (int i = 0; i < ArmorStyles.Length; i++) if (GUILayout.Button(ArmorStyles[i], style == i ? Trainer.sOn : Trainer.sBtn, GUILayout.Height(30))) style = i;
            GUILayout.EndHorizontal();
            onlyFound = GUILayout.Toggle(onlyFound, " Only biomes this character has found");
            GUILayout.Label("Weapons: pick 3, in hotbar order (" + (picks.Count == 0 ? "none yet" : string.Join(", ", picks.Select(p => Cats[p]).ToArray())) + ")", Trainer.sHead);
            for (int row = 0; row < Cats.Length; row += 6) {
                GUILayout.BeginHorizontal();
                for (int i = row; i < Mathf.Min(row + 6, Cats.Length); i++) {
                    int at = picks.IndexOf(i);
                    if (GUILayout.Button(at >= 0 ? (at + 1) + ". " + Cats[i] : Cats[i], at >= 0 ? Trainer.sOn : Trainer.sBtn, GUILayout.Height(30))) {
                        if (at >= 0) picks.RemoveAt(at); else if (picks.Count < 3) picks.Add(i);
                    }
                }
                GUILayout.EndHorizontal();
            }
            GUILayout.Label("Crossbow and Staff only show up from the Mistlands; before that you get a bow instead. Slot 4 is the tier's shield (a buckler where there is one).", Trainer.sDim);
            GUILayout.Space(6);
            if (picks.Count == 3) { if (GUILayout.Button(Running ? "Restart the gauntlet" : "Start the gauntlet", Trainer.sOn, GUILayout.Height(36))) StartRun(); }
            else GUILayout.Label("Pick 3 weapon types to start.", Trainer.sDim);
            GUILayout.Label("Encounters come from the biomes your copied character has been to. Each one replaces your inventory and sets your skills for its biome; Re-copy brings your own back. Marked enemies glow violet with an orange name: you can't block, parry or stagger them.", Trainer.sDim);
        }
    }

    // ---- damage patches (gauntlet only: everything checks Running) ----
    [HarmonyPatch(typeof(Character), "RPC_Damage")]
    static class GauntletDamage {
        static void Prefix(Character __instance, HitData hit) { if (Gauntlet.Running && __instance == Player.m_localPlayer) Gauntlet.BeforeDamage(hit); }
        static void Postfix(Character __instance) { if (Gauntlet.Running && __instance == Player.m_localPlayer) Gauntlet.AfterDamage(Player.m_localPlayer); }
    }

    [HarmonyPatch(typeof(Humanoid), "BlockAttack")]
    static class GauntletBlock {
        static void Prefix(Humanoid __instance) { if (Gauntlet.Running && __instance == Player.m_localPlayer) Gauntlet.BeforeBlock(); }
        static void Postfix(Humanoid __instance, bool __result) { if (Gauntlet.Running && __instance == Player.m_localPlayer) Gauntlet.AfterBlock(__instance, __result); }
    }

    [HarmonyPatch(typeof(Character), "AddStaggerDamage")]
    static class GauntletGuardBreak {
        static void Postfix(Character __instance, bool __result) { if (Gauntlet.InBlock && __result && __instance == Player.m_localPlayer) Gauntlet.BlockStaggered = true; }
    }

    [HarmonyPatch(typeof(Character), "ApplyPushback", new[] { typeof(HitData) })]
    static class GauntletLanded {
        static void Prefix(Character __instance, HitData hit) { if (Gauntlet.Running && __instance == Player.m_localPlayer) Gauntlet.Landed(hit); }
    }

    // health never drops in the gauntlet: hits are counted instead, and only the last one kills
    [HarmonyPatch(typeof(Character), "ApplyDamage")]
    static class GauntletHealth {
        static void Prefix(Character __instance, HitData hit) {
            if (!Gauntlet.Running || __instance != Player.m_localPlayer || Gauntlet.Dying) return;
            float total = hit.GetTotalDamage(), hp = __instance.GetHealth();
            if (total >= hp && total > 0f) hit.ApplyModifier(Mathf.Max(0f, hp - 1f) / total);
        }
        static void Postfix(Character __instance) {
            if (Gauntlet.Running && __instance == Player.m_localPlayer && !Gauntlet.Dying && !__instance.IsDead()) __instance.SetHealth(__instance.GetMaxHealth());
        }
    }

    [HarmonyPatch(typeof(Character), "RPC_Stagger")]
    static class MarkedNoStagger { static bool Prefix(Character __instance) { return !Gauntlet.Marked.Contains(__instance); } }

    [HarmonyPatch(typeof(Character), "GetHoverName")]
    static class MarkedName {
        static void Postfix(Character __instance, ref string __result) { if (Gauntlet.Marked.Contains(__instance)) __result = "<color=orange>" + __result + "</color>"; }
    }

    // marked enemies: their health bar is always up (normal bars only show within 10 m and for 60 s after you look at one)
    [HarmonyPatch(typeof(EnemyHud), "TestShow")]
    static class MarkedBarShown {
        static void Postfix(Character c, ref bool __result) {
            if (!__result && Gauntlet.Marked.Contains(c) && Player.m_localPlayer != null && Vector3.Distance(c.transform.position, Player.m_localPlayer.transform.position) < 50f) __result = true;
        }
    }

    [HarmonyPatch(typeof(EnemyHud), "UpdateHuds")]
    static class MarkedBarTimer {
        static System.Reflection.FieldInfo huds, hover, who;
        static void Prefix(EnemyHud __instance) {
            if (Gauntlet.Marked.Count == 0) return;
            if (huds == null) {
                huds = AccessTools.Field(typeof(EnemyHud), "m_huds");
                var t = AccessTools.Inner(typeof(EnemyHud), "HudData");
                hover = AccessTools.Field(t, "m_hoverTimer"); who = AccessTools.Field(t, "m_character");
            }
            var d = huds.GetValue(__instance) as System.Collections.IDictionary;
            if (d == null) return;
            foreach (var v in d.Values) if (Gauntlet.Marked.Contains(who.GetValue(v) as Character)) hover.SetValue(v, 0f);
        }
    }
}

namespace CombatTrainer {
    // hits left, shown with the game's own HUD pieces: a status-effect icon (the tier shield) with "2/3" on it,
    // the red damage flash held longer when a hit counts, the lava warning edge glow + a blinking icon on the
    // last hit, and the player's own crit-hit / biome-found banner + stinger on a clear
    class GauntletHitsSE : StatusEffect {
        internal static string Text = "";
        public override string GetIconText() { return Text; }
    }

    static class Feedback {
        const string SEName = "CT_GauntletHits";
        static StatusEffect se; static string iconFor;
        static float flashHold; static bool glowOn;

        internal static void Tick(int left, int max, string shieldPrefab, bool fighting) {
            var p = Player.m_localPlayer; if (p == null) return;
            GauntletHitsSE.Text = Mathf.Max(0, left) + "/" + max;
            if (se == null || p.GetSEMan().GetStatusEffect(SEName.GetStableHashCode()) == null) {
                var proto = ScriptableObject.CreateInstance<GauntletHitsSE>();
                proto.name = SEName; proto.m_name = "Hits"; proto.m_ttl = 0f;
                se = p.GetSEMan().AddStatusEffect(proto); iconFor = null;
            }
            if (se == null) return;
            if (iconFor != shieldPrefab) {
                var go = ObjectDB.instance.GetItemPrefab(shieldPrefab);
                if (go != null) se.m_icon = go.GetComponent<ItemDrop>().m_itemData.GetIcon();
                iconFor = shieldPrefab;
            }
            bool last = fighting && left == 1;
            se.m_flashIcon = last;
            if (flashHold > 0f) { flashHold -= Time.deltaTime; Hud.instance.DamageFlash(); }
            var lava = Hud.instance != null ? Hud.instance.m_lavaWarningScreen : null;
            if (lava != null && (last || glowOn)) {
                glowOn = last;
                lava.gameObject.SetActive(last);
                var c = lava.color; c.a = last ? 0.35f + 0.25f * Mathf.Sin(Time.time * 4f) : 0f; lava.color = c;
            }
        }

        internal static void Lost(Character attacker) {
            var p = Player.m_localPlayer; if (p == null) return;
            flashHold = 0.35f;
            if (se != null) se.m_isNew = true; // the icon's own flash animation
            var fx = p.m_critHitEffects.m_effectPrefabs.Length > 0 || attacker == null ? p.m_critHitEffects : attacker.m_critHitEffects;
            fx.Create(p.GetCenterPoint(), Quaternion.identity, p.transform);
        }

        internal static void Cleared() {
            var p = Player.m_localPlayer; if (p == null) return;
            MessageHud.instance.ShowBiomeFoundMsg("Cleared", true);
            if (se != null) se.m_isNew = true;
        }

        internal static void Off() {
            var p = Player.m_localPlayer;
            if (p != null) p.GetSEMan().RemoveStatusEffect(SEName.GetStableHashCode(), true);
            se = null; flashHold = 0f;
            var lava = Hud.instance != null ? Hud.instance.m_lavaWarningScreen : null;
            if (glowOn && lava != null) lava.gameObject.SetActive(false);
            glowOn = false;
        }
    }
}
