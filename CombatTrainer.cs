// Valheim Combat Trainer: training world (M1) + enemy menu (M2).
// M2, arena worlds only: F8 opens a menu to pick creatures (stars, count) and spawn them frozen in front
// of you; closing it starts a 3 s countdown, then they fight with their normal AI. No drops. Death is
// free here: no tombstone, no skill loss, and you respawn fed with the foods you last ate. Rested never
// runs out, and you always carry a stack of pukeberries to change food.
// A world ticked as "Combat Trainer Arena" on the New World screen (or named CombatTrainer)
// generates as one flat, empty Meadows plain: no trees, rocks,
// dungeons or spawners, no natural spawns, no raids, clear midday weather. Only the start temple
// (the spawn point) is placed. Every other world is untouched: all patches check Active first.
// The arena marker is the world's uid in BepInEx/config/CombatTrainer.worlds.txt, not a world key:
// unknown world keys make the game treat the world as having cheated modifiers.
// Written in C# 5 so it builds with the csc.exe that ships with Windows.
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CombatTrainer {
    [BepInPlugin("trickstack.CombatTrainer", "CombatTrainer", "0.6.1")]
    public class Plugin : BaseUnityPlugin {
        internal static ConfigEntry<string> WorldName;
        internal static ConfigEntry<float> GroundHeight;
        internal static bool Active; // set per world load; read from the terrain worker thread
        static string ListPath { get { return Path.Combine(Paths.ConfigPath, "CombatTrainer.worlds.txt"); } }
        static string Norm(string s) { return (s ?? "").Replace(" ", "").ToLowerInvariant(); }
        internal static bool IsArena(World w) {
            return w != null && !w.m_menu && ((File.Exists(ListPath) && File.ReadAllLines(ListPath).Contains(w.m_uid.ToString()))
                || Norm(w.m_name) == Norm(WorldName.Value));
        }
        // profile files the player agreed (on the main menu) may enter arenas as trainers
        static string TrainersPath { get { return Path.Combine(Paths.ConfigPath, "CombatTrainer.trainers.txt"); } }
        internal static bool IsTrainerProfile(string file) { return File.Exists(TrainersPath) && File.ReadAllLines(TrainersPath).Contains(file); }
        internal static void AddTrainerProfile(string file) { if (!IsTrainerProfile(file)) File.AppendAllText(TrainersPath, file + System.Environment.NewLine); }
        internal static void MarkArena(World w) {
            if (!IsArena(w)) File.AppendAllText(ListPath, w.m_uid + System.Environment.NewLine);
            Debug.Log("[CombatTrainer] marked as arena: " + w.m_name + " (" + w.m_uid + ")");
        }

        private void Awake() {
            WorldName = Config.Bind("World", "Training World Name", "CombatTrainer", "Only a world with this name becomes the training arena (case and spaces ignored).");
            GroundHeight = Config.Bind("World", "Ground Height", 34f, "Arena ground height in metres (water is at 30).");
            Kits.AuthorMode = Config.Bind("Kits", "Author Mode", false, "Edits to preset kits save into the mod's own kits folder (to ship them), and your kits get a Ship with mod button.");
            Game.isModded = true;
            new Harmony("local.combattrainer").PatchAll();
        }

        private void Update() { Trainer.Update(); }
        private void OnGUI() { Trainer.OnGUI(); }
    }

    [HarmonyPatch(typeof(WorldGenerator), "Initialize")]
    static class DetectWorld {
        static void Postfix(World world) {
            Plugin.Active = Plugin.IsArena(world);
            if (Plugin.Active) Debug.Log("[CombatTrainer] training world loaded: " + world.m_name);
        }
    }

    // Start on an arena world with a character that isn't a trainer yet: ask on the main menu, before anything loads.
    // No = nothing happens; the character never enters the world.
    [HarmonyPatch(typeof(FejdStartup), "OnWorldStart")]
    static class AskBeforeArena {
        static bool allowOnce;
        static bool Prefix(FejdStartup __instance) {
            if (allowOnce) { allowOnce = false; return true; }
            var t = Traverse.Create(__instance);
            var world = t.Field("m_world").GetValue<World>();
            var profiles = t.Field("m_profiles").GetValue<List<PlayerProfile>>();
            int i = t.Field("m_profileIndex").GetValue<int>();
            if (!Plugin.IsArena(world) || profiles == null || i < 0 || i >= profiles.Count) return true;
            var pr = profiles[i];
            if (Plugin.IsTrainerProfile(pr.GetFilename())) return true;
            var data = Traverse.Create(pr).Field("m_playerData").GetValue<byte[]>();
            // trainers made before the list existed: the trainer tag is a custom-data key inside the saved player
            if (data != null && System.Text.Encoding.UTF8.GetString(data).Contains(Trainer.TrainerKey)) { Plugin.AddTrainerProfile(pr.GetFilename()); return true; }
            bool played = data != null;
            string text = world.m_name + " is a Combat Trainer Arena. Characters that enter it become trainers and can never join normal worlds again.\n\n"
                + (played ? "<color=#ff6060>" + pr.GetName() + " has already been played: this looks like a real character. Make a new character for training instead.</color>\n\n" : "")
                + "Turn " + pr.GetName() + " into a trainer and enter?";
            UnifiedPopup.Push(new YesNoPopup("Combat Trainer Arena", text,
                () => {
                    UnifiedPopup.Pop();
                    UnifiedPopup.Push(new YesNoPopup("Are you absolutely sure?",
                        pr.GetName() + " will be a trainer forever and can never join a normal world again. This can't be undone.\n\nEnter " + world.m_name + " with " + pr.GetName() + "?",
                        () => { UnifiedPopup.Pop(); Plugin.AddTrainerProfile(pr.GetFilename()); allowOnce = true; __instance.OnWorldStart(); },
                        () => UnifiedPopup.Pop(), false));
                },
                () => UnifiedPopup.Pop(), false));
            return false;
        }
    }

    [HarmonyPatch(typeof(WorldGenerator), "Deitialize")]
    static class LeaveWorld {
        static void Postfix() { Plugin.Active = false; }
    }

    // flat Meadows everywhere (called from the HeightmapBuilder thread: keep these pure)
    [HarmonyPatch(typeof(WorldGenerator), "GetBiome", new[] { typeof(float), typeof(float), typeof(float), typeof(bool) })]
    static class FlatBiome {
        static bool Prefix(ref Heightmap.Biome __result) {
            if (!Plugin.Active) return true;
            __result = Heightmap.Biome.Meadows;
            return false;
        }
    }

    [HarmonyPatch(typeof(WorldGenerator), "GetBiomeHeight")]
    static class FlatHeight {
        static bool Prefix(ref float __result, out Color mask) {
            mask = Color.black;
            if (!Plugin.Active) return true;
            __result = Plugin.GroundHeight.Value;
            return false;
        }
    }

    // only the start temple: it is where the game puts new players
    [HarmonyPatch(typeof(ZoneSystem), "GenerateLocations")]
    static class OnlyStartTemple {
        static void Prefix(ZoneSystem __instance) {
            if (!Plugin.Active) return;
            foreach (var loc in __instance.m_locations)
                if (loc.m_prefabName != Game.instance.m_StartLocation) loc.m_enable = false;
        }
    }

    [HarmonyPatch(typeof(ZoneSystem), "PlaceVegetation")]
    static class NoVegetation {
        static bool Prefix() { return !Plugin.Active; }
    }

    [HarmonyPatch(typeof(SpawnSystem), "UpdateSpawning")]
    static class NoNaturalSpawns {
        static bool Prefix() { return !Plugin.Active; }
    }

    [HarmonyPatch(typeof(RandEventSystem), "UpdateRandomEvent")]
    static class NoRaids {
        static bool Prefix() { return !Plugin.Active; }
    }

    [HarmonyPatch(typeof(Player), "OnSpawned")]
    static class ClearMidday {
        static void Postfix(Player __instance) {
            if (!Plugin.Active || __instance != Player.m_localPlayer || EnvMan.instance == null) return;
            EnvMan.instance.SetForceEnvironment("Clear");
            EnvMan.instance.m_debugTimeOfDay = true;
            EnvMan.instance.m_debugTime = 0.5f;
        }
    }

    // "Combat Trainer Arena" checkbox on the New World screen: a clone of the menu's public-server toggle
    [HarmonyPatch(typeof(FejdStartup), "OnWorldNew")]
    static class ArenaCheckbox {
        internal static Toggle Box;
        static void Postfix(FejdStartup __instance) {
            if (Box == null) {
                var seed = (RectTransform)__instance.m_newWorldSeed.transform;
                var go = Object.Instantiate(__instance.m_publicServerToggle.gameObject, seed.parent, false);
                go.name = "CombatTrainerToggle";
                foreach (var mb in go.GetComponentsInChildren<MonoBehaviour>(true))
                    if (mb.GetType().Name == "Localize") Object.Destroy(mb); // would overwrite our label
                Box = go.GetComponent<Toggle>();
                Box.onValueChanged = new Toggle.ToggleEvent(); // drop the cloned public-server listeners
                Box.onValueChanged.AddListener(on => {
                    if (!on) return;
                    UnifiedPopup.Push(new YesNoPopup("Combat Trainer Arena",
                        "This world becomes a flat training arena.\n\nPlay it with a NEW character made just for training. Any character that enters becomes a trainer and can never join normal worlds again.\n\nYour real characters stay safe: the trainer copies them read-only from the F8 menu.\n\nMake this an arena world?",
                        () => UnifiedPopup.Pop(), () => { UnifiedPopup.Pop(); Box.isOn = false; }, false));
                });
                foreach (var t in go.GetComponentsInChildren<TMP_Text>(true)) t.text = "Combat Trainer Arena";
                var rt = (RectTransform)go.transform;
                rt.anchorMin = seed.anchorMin; rt.anchorMax = seed.anchorMax; rt.pivot = new Vector2(0f, 1f);
                // left edge and bottom edge of the seed field, in the parent's anchor space
                rt.anchoredPosition = seed.anchoredPosition + new Vector2(-seed.rect.width * seed.pivot.x, -seed.rect.height * seed.pivot.y - 12f);
                go.SetActive(true);
            }
            // the cloned public-server toggle may be greyed out and disabled; make ours live and bright
            Box.interactable = true;
            var col = Traverse.Create(__instance).Field("m_toggleColor").GetValue<Color>();
            foreach (var t in Box.GetComponentsInChildren<TMP_Text>(true)) t.color = col;
            Box.isOn = false;
        }
    }

    [HarmonyPatch(typeof(FejdStartup), "OnNewWorldDone")]
    static class MarkNewWorld {
        static void Prefix(FejdStartup __instance, out World __state) {
            __state = Traverse.Create(__instance).Field("m_world").GetValue<World>();
        }
        static void Postfix(FejdStartup __instance, World __state) {
            var w = Traverse.Create(__instance).Field("m_world").GetValue<World>();
            if (w != null && w != __state && ArenaCheckbox.Box != null && ArenaCheckbox.Box.isOn) Plugin.MarkArena(w);
        }
    }

    class Entry { public string Prefab, Label; public int Stars, Count = 1; public List<Character> Live = new List<Character>(); }

    static class Trainer {
        internal static bool MenuOpen;
        internal static bool Frozen;                    // menu open or countdown running
        internal static readonly HashSet<BaseAI> Spawned = new HashSet<BaseAI>();
        static readonly List<Entry> Setup = new List<Entry>();
        static List<KeyValuePair<string, string>> catalog; // prefab, label
        static string search = "";
        static float distance = 12f, countdown = -1f, nextTick;
        static Vector2 scrollA, scrollB;
        static Rect win = new Rect(40, 60, 680, 700);
        internal static List<string> LastFoods = new List<string>();
        internal static bool DiedHere;
        const string Tag = "combattrainer_spawn";

        // full heal ~5 s after eating / respawning / copying, when the food's max health has kicked in
        static float healAt = -1f;
        internal static void HealSoon() { healAt = Time.time + 5f; }

        // arena: everything you carry back to full durability after each kill and on respawn
        internal static void RepairAll(Player p) {
            foreach (var it in p.GetInventory().GetAllItems())
                if (it.m_shared.m_useDurability) it.m_durability = it.GetMaxDurability();
        }

        internal static bool Live() { return Plugin.Active && Player.m_localPlayer != null && !Player.m_localPlayer.IsDead(); }

        // ---- trainer characters (copy from a real character, read-only) ----
        internal const string TrainerKey = "combattrainer_trainer", SourceKey = "combattrainer_source";
        internal static bool KickPending;
        static List<PlayerProfile> profiles;
        static string confirmFile; // first click on "Copy from" arms, second click copies
        static string status = "";

        internal static bool IsTrainer(Player p) { return p != null && p.m_customData.ContainsKey(TrainerKey); }

        static void CopyFrom(PlayerProfile src) {
            var p = Player.m_localPlayer;
            // the profile was only read from disk; we take its saved player data and never call Save on it
            var data = Traverse.Create(src).Field("m_playerData").GetValue<byte[]>();
            if (data == null) { status = src.GetName() + " has no saved character data."; return; }
            p.UnequipAllItems();
            // Player.Load reads the inventory before the "invrows" unique key (extra rows bought from the trader),
            // so open all rows first or items in the extra rows are dropped, then apply the source's row count
            while (p.RemoveUniqueKeyValue("invrows")) { }
            p.GetInventory().SetHeight(Mathf.Max(p.GetInventory().GetHeight(), 9)); // never shrink: inventory mods keep slots below
            // Player.Load only adds to these, and m_knownStations is a Dictionary that throws on a repeat copy:
            // start them empty so the trainer ends up with exactly the source's
            var tp = Traverse.Create(p);
            foreach (var f in new[] { "m_knownRecipes", "m_knownStations", "m_knownMaterial", "m_shownTutorials", "m_uniques", "m_trophies", "m_knownBiome", "m_knownTexts" })
                tp.Field(f).Method("Clear").GetValue();
            bool ok = true;
            try { p.Load(new ZPackage(data)); } // inventory, equipped, skills, foods + timers, recipes, Forsaken power
            catch (System.Exception ex) { ok = false; Debug.LogError("[CombatTrainer] copy failed: " + ex); }
            finally { tp.Field("m_isLoading").SetValue(false); } // Load sets this first and clears it last
            string rowsText; int rows;
            p.SetInventorySize(p.TryGetUniqueKeyValue("invrows", out rowsText) && int.TryParse(rowsText, out rows) ? rows : 4);
            if (!ok) { status = "Copy failed partway (see BepInEx log). Try Re-copy; " + src.GetName() + "'s save was not touched."; return; }
            Traverse.Create(p).Method("EquipInventoryItems").GetValue();
            p.m_customData[TrainerKey] = "1";
            p.m_customData[SourceKey] = src.GetFilename();
            Kits.RememberSkills(p);
            p.SetHealth(p.GetMaxHealth()); p.AddStamina(p.GetMaxStamina());
            HealSoon();
            Game.instance.GetPlayerProfile().SavePlayerData(p);
            status = "Copied " + src.GetName() + ". " + src.GetName() + "'s own save was not touched.";
            confirmFile = null;
        }

        static void DrawCharacter() {
            var p = Player.m_localPlayer;
            GUILayout.Label("Your character", sHead);
            if (profiles == null) profiles = SaveSystem.GetAllPlayerProfiles();
            string me = Game.instance.GetPlayerProfile().GetFilename();
            string bound; p.m_customData.TryGetValue(SourceKey, out bound);
            if (IsTrainer(p)) {
                var src = profiles.FirstOrDefault(x => x.GetFilename() == bound);
                GUILayout.BeginHorizontal();
                GUILayout.Label(bound == null ? "Fresh trainer: copy one of your characters to bring its gear, skills and food." : "Trainer of " + (src != null ? src.GetName() : bound), sLbl);
                if (src != null && GUILayout.Button("Re-copy " + src.GetName(), sBtn, GUILayout.Width(200))) CopyFrom(src);
                GUILayout.EndHorizontal();
                GUILayout.Label("Or copy a different character instead (replaces this trainer's gear, skills and food):", sDim);
            } else {
                GUILayout.Label("Copying replaces this character's gear, skills and food with the other character's, and turns it into a trainer that can only enter arena worlds. Use a fresh character for this.", sDim);
            }
            var others = profiles.Where(x => x.GetFilename() != me && x.GetFilename() != bound).ToList();
            for (int i = 0; i < others.Count; i += 3) { // 3 per row so the window never stretches
                GUILayout.BeginHorizontal();
                foreach (var pr in others.Skip(i).Take(3)) {
                    bool armed = confirmFile == pr.GetFilename();
                    if (GUILayout.Button(armed ? "Click again: copy " + pr.GetName() : "Copy from " + pr.GetName(), armed ? sOn : sBtn, GUILayout.Width(200))) {
                        if (armed) CopyFrom(pr); else confirmFile = pr.GetFilename();
                    }
                }
                GUILayout.EndHorizontal();
            }
            if (status.Length > 0) GUILayout.Label(status, sDim);
        }

        internal static void Update() {
            if (KickPending && Player.m_localPlayer != null && Game.instance != null) { KickPending = false; Game.instance.Logout(); }
            if (!Plugin.Active) { MenuOpen = false; Frozen = false; countdown = -1f; return; }
            // F8/Esc are read in OnGUI: the game's own key reader is muted while the menu is open
            if (countdown > 0f) {
                countdown -= Time.deltaTime;
                if (countdown <= 0f) Fight();
            }
            if (Time.time >= nextTick && Live() && IsTrainer(Player.m_localPlayer)) { nextTick = Time.time + 1f; Upkeep(Player.m_localPlayer); }
            Waves.Update();
            if (healAt > 0f && Time.time >= healAt && Live()) { healAt = -1f; var hp = Player.m_localPlayer; hp.SetHealth(hp.GetMaxHealth()); hp.AddStamina(hp.GetMaxStamina()); }
        }

        // opening freezes every trainer creature; closing (X or F8) leaves them frozen; only Fight! starts the fight
        // a fight in progress pauses while the menu is open and resumes at once when it closes;
        // a fresh setup waits for Fight! (with its countdown)
        internal static bool FightOn;
        internal static void SetMenu(bool open) {
            MenuOpen = open;
            if (open) { Frozen = true; countdown = -1f; profiles = null; } // re-read saves: the source may have progressed
            else if (AliveCount() == 0) { Frozen = false; FightOn = false; }
            else if (FightOn) Frozen = false;
        }

        static void StartFight() {
            MenuOpen = false;
            if (AliveCount() > 0) countdown = 3f; else Frozen = false;
        }

        static IEnumerable<Character> Alive() { return Setup.SelectMany(e => e.Live).Where(c => c != null && !c.IsDead()); }
        static int AliveCount() { return Alive().Count(); }

        static void Fight() {
            countdown = -1f; Frozen = false; FightOn = true;
            foreach (var ai in Spawned) if (ai != null) { ai.Alert(); ai.SetHuntPlayer(true); }
        }

        // rested forever; remember the current foods for respawns
        static void Upkeep(Player p) {
            var se = p.GetSEMan();
            var rested = se.GetStatusEffect(SEMan.s_statusEffectRested) ?? se.AddStatusEffect(SEMan.s_statusEffectRested, true);
            if (rested != null) rested.m_ttl = 0f; // 0 = never expires
            var foods = Traverse.Create(p).Field("m_foods").GetValue<List<Player.Food>>();
            if (foods != null && foods.Count > 0)
                LastFoods = foods.Where(f => f.m_item != null && f.m_item.m_dropPrefab != null).Select(f => f.m_item.m_dropPrefab.name).ToList();
            // trainer creatures saved in the world from an earlier session load in late: remove any we don't own
            foreach (var c in Character.GetAllCharacters().ToList()) {
                if (c == null || c is Player) continue;
                var ai = c.GetComponent<BaseAI>();
                if (ai != null && Spawned.Contains(ai)) continue;
                var nv = c.GetComponent<ZNetView>();
                if (nv != null && nv.IsValid() && nv.IsOwner() && nv.GetZDO().GetBool(Tag)) ZNetScene.instance.Destroy(c.gameObject);
            }
        }

        internal static void OnArrive(Player p) {
            string file = Game.instance.GetPlayerProfile().GetFilename();
            if (IsTrainer(p)) Plugin.AddTrainerProfile(file);
            else if (Plugin.IsTrainerProfile(file)) p.m_customData[TrainerKey] = "1"; // agreed on the main menu
            else { // got past the menu question somehow (e.g. joined a server): out at once, before anything is given
                p.Message(MessageHud.MessageType.Center, "Only trainer characters can play Combat Trainer Arenas");
                KickPending = true;
                return;
            }
            bool waveRun = Waves.Running;
            Clear(); // leftovers from a previous session or the fight you just lost
            if (DiedHere) {
                DiedHere = false;
                foreach (var name in LastFoods) {
                    var prefab = ObjectDB.instance.GetItemPrefab(name);
                    if (prefab == null) continue;
                    var item = prefab.GetComponent<ItemDrop>().m_itemData.Clone();
                    item.m_dropPrefab = prefab;
                    p.EatFood(item);
                }
                p.SetHealth(p.GetMaxHealth());
                HealSoon(); // again once the foods' max health has settled
                RepairAll(p);
                p.AddStamina(p.GetMaxStamina());
                if (waveRun) { Waves.Fell(); tab = 1; SetMenu(true); }
                else if (Setup.Count > 0) { RespawnAll(); SetMenu(true); } // same fight again, frozen, ready
            }
            var berries = ObjectDB.instance.GetItemPrefab("Pukeberries");
            if (berries != null) {
                var b = berries.GetComponent<ItemDrop>().m_itemData.Clone();
                b.m_dropPrefab = berries;
                if (p.GetInventory().CountItems(b.m_shared.m_name) == 0) { b.m_stack = b.m_shared.m_maxStackSize; p.GetInventory().AddItem(b); }
            }
        }

        internal static void Clear() {
            foreach (var c in Character.GetAllCharacters().ToList()) {
                if (c == null || c is Player) continue;
                var nv = c.GetComponent<ZNetView>();
                if (nv != null && nv.IsValid() && nv.GetZDO().GetBool(Tag)) ZNetScene.instance.Destroy(c.gameObject);
            }
            Spawned.Clear();
            foreach (var e in Setup) e.Live.Clear();
            FightOn = false;
            Waves.Reset();
        }

        internal static float RadiusOf(GameObject prefab) {
            var cap = prefab.GetComponent<CapsuleCollider>();
            return cap != null ? Mathf.Max(0.4f, cap.radius * prefab.transform.localScale.x) : 1f;
        }

        // a free spot in a 70° cone in front of you, distance ±25%, never overlapping another creature
        static Vector3 PickSpot(float radius) {
            var p = Player.m_localPlayer;
            Vector3 fwd = p.transform.forward; fwd.y = 0f; fwd.Normalize();
            var taken = Alive().Select(c => new KeyValuePair<Vector3, float>(c.transform.position, c.GetRadius())).ToList();
            Vector3 pos = p.transform.position + fwd * distance;
            for (int tries = 0; tries < 60; tries++) {
                float spread = 1f + tries / 20f; // widen if the cone is crowded
                float d = distance * Random.Range(1f - 0.25f * spread, 1f + 0.25f * spread);
                pos = p.transform.position + Quaternion.Euler(0f, Random.Range(-35f, 35f) * spread, 0f) * fwd * Mathf.Max(3f, d);
                bool free = true;
                foreach (var t in taken) {
                    Vector3 a = pos - t.Key; a.y = 0f;
                    if (a.magnitude < radius + t.Value + 0.75f) { free = false; break; }
                }
                if (free) break;
            }
            float h;
            if (ZoneSystem.instance.GetGroundHeight(pos, out h)) pos.y = h;
            return pos + Vector3.up * 0.3f;
        }

        static void SpawnOne(Entry e) {
            var prefab = ZNetScene.instance.GetPrefab(e.Prefab);
            if (prefab == null) return;
            var ch = SpawnAt(prefab, e.Stars, PickSpot(RadiusOf(prefab)));
            if (ch != null) e.Live.Add(ch);
            Frozen = true; FightOn = false; // new creatures wait for Fight!
        }

        // a trainer creature: tagged (so it's cleaned up), drops nothing, facing you
        internal static Character SpawnAt(GameObject prefab, int stars, Vector3 pos) {
            Vector3 look = Player.m_localPlayer.transform.position - pos; look.y = 0f;
            var go = Object.Instantiate(prefab, pos, Quaternion.LookRotation(look.sqrMagnitude > 0.01f ? look : Vector3.forward));
            var nv = go.GetComponent<ZNetView>();
            if (nv != null && nv.IsValid()) nv.GetZDO().Set(Tag, true);
            var drop = go.GetComponent<CharacterDrop>();
            if (drop != null) Object.Destroy(drop);
            var ch = go.GetComponent<Character>();
            if (ch != null && stars > 0) ch.SetLevel(stars + 1);
            var ai = go.GetComponent<BaseAI>();
            if (ai != null) Spawned.Add(ai);
            return ch;
        }

        internal static Vector3 Ground(Vector3 pos) {
            float h;
            if (ZoneSystem.instance.GetGroundHeight(pos, out h)) pos.y = h;
            return pos + Vector3.up * 0.3f;
        }

        static void Despawn(Character c) {
            if (c == null) return;
            var ai = c.GetComponent<BaseAI>();
            if (ai != null) Spawned.Remove(ai);
            ZNetScene.instance.Destroy(c.gameObject);
        }

        static void DespawnEntry(Entry e) { foreach (var c in e.Live) Despawn(c); e.Live.Clear(); }

        static void SpawnEntry(Entry e) { DespawnEntry(e); for (int i = 0; i < e.Count; i++) SpawnOne(e); }

        static void RespawnAll() {
            foreach (var e in Setup) DespawnEntry(e);
            foreach (var e in Setup) for (int i = 0; i < e.Count; i++) SpawnOne(e);
        }

        internal static List<KeyValuePair<string, string>> Catalog() {
            if (catalog != null) return catalog;
            catalog = new List<KeyValuePair<string, string>>();
            foreach (var go in ZNetScene.instance.m_prefabs) {
                if (go == null) continue;
                var ch = go.GetComponent<Character>();
                if (ch == null || ch is Player || go.GetComponent<BaseAI>() == null) continue;
                string label = Localization.instance.Localize(ch.m_name);
                // unnamed, untranslated ("[enemy_x]") and Test prefabs are leftovers the game never spawns
                if (string.IsNullOrEmpty(label) || label.StartsWith("[") || go.name.Contains("Test")) continue;
                catalog.Add(new KeyValuePair<string, string>(go.name, label));
            }
            catalog = catalog.OrderBy(k => k.Value).ThenBy(k => k.Key).ToList();
            return catalog;
        }

        internal static void OnGUI() {
            if (!Plugin.Active) return;
            var ev = Event.current;
            if (ev.type == EventType.KeyDown && Live() && IsTrainer(Player.m_localPlayer) && (ev.keyCode == KeyCode.F8 || (ev.keyCode == KeyCode.Escape && MenuOpen))) {
                SetMenu(ev.keyCode == KeyCode.F8 && !MenuOpen); ev.Use();
            }
            if (countdown > 0f) { // big gold number with a black outline, readable on sky and grass
                var st = new GUIStyle(GUI.skin.label) { fontSize = 110, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
                var r = new Rect(0, Screen.height * 0.28f, Screen.width, 150);
                string n = Mathf.CeilToInt(countdown).ToString();
                st.normal.textColor = Color.black;
                for (int dx = -3; dx <= 3; dx += 3) for (int dy = -3; dy <= 3; dy += 3)
                    if (dx != 0 || dy != 0) GUI.Label(new Rect(r.x + dx, r.y + dy, r.width, r.height), n, st);
                st.normal.textColor = new Color(1f, 0.78f, 0.3f);
                GUI.Label(r, n, st);
            }
            Styles();
            Waves.Hud();
            if (MenuOpen && Live()) {
                win.width = 680; // fixed: long text wraps instead of stretching the window
                win.height = 10;  // shrink-wraps to the content each frame
                win = GUILayout.Window(735112, win, Draw, "Combat Trainer   ·   X or F8 closes, Fight! starts", sWin, GUILayout.Width(680));
            }
        }

        // solid dark panel, larger light text: the default IMGUI skin is see-through and tiny
        internal static GUIStyle sWin, sHead, sLbl, sDim, sBtn, sOn, sField, sRow, sNum;
        // HideAndDontSave: the game unloads unused assets when a world loads, which would wipe these backgrounds
        static Texture2D Tex(Color c) { var t = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave }; t.SetPixel(0, 0, c); t.Apply(); return t; }
        static void Styles() {
            if (sWin != null && sWin.normal.background != null) return; // rebuilt if a texture was ever destroyed
            Color ink = new Color(0.93f, 0.9f, 0.84f), dim = new Color(0.74f, 0.7f, 0.63f), gold = new Color(1f, 0.72f, 0.3f);
            sWin = new GUIStyle(GUI.skin.window) { fontSize = 18, fontStyle = FontStyle.Bold, padding = new RectOffset(14, 14, 34, 12) };
            sWin.normal.background = sWin.onNormal.background = Tex(new Color(0.09f, 0.08f, 0.07f, 0.97f));
            sWin.normal.textColor = sWin.onNormal.textColor = gold;
            sHead = new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold, margin = new RectOffset(0, 0, 10, 4) };
            sHead.normal.textColor = gold;
            sLbl = new GUIStyle(GUI.skin.label) { fontSize = 16, richText = true, alignment = TextAnchor.MiddleLeft, wordWrap = true };
            sLbl.normal.textColor = ink;
            sDim = new GUIStyle(sLbl) { fontSize = 13 };
            sDim.normal.textColor = dim;
            sBtn = new GUIStyle(GUI.skin.button) { fontSize = 15 };
            sBtn.normal.background = Tex(new Color(0.22f, 0.19f, 0.16f));
            sBtn.hover.background = Tex(new Color(0.32f, 0.27f, 0.21f));
            sBtn.active.background = Tex(new Color(0.42f, 0.33f, 0.2f));
            sBtn.normal.textColor = sBtn.hover.textColor = sBtn.active.textColor = ink;
            sOn = new GUIStyle(sBtn);
            sOn.normal.background = sOn.hover.background = Tex(new Color(0.62f, 0.42f, 0.16f));
            sOn.normal.textColor = sOn.hover.textColor = Color.white;
            sField = new GUIStyle(GUI.skin.textField) { fontSize = 16, padding = new RectOffset(8, 8, 6, 6) };
            sField.normal.background = sField.focused.background = Tex(new Color(0.03f, 0.03f, 0.03f));
            sField.normal.textColor = sField.focused.textColor = ink;
            sRow = new GUIStyle { margin = new RectOffset(0, 0, 2, 2) };
            sNum = new GUIStyle(sLbl) { alignment = TextAnchor.MiddleCenter, wordWrap = false };
        }

        // ---- items tab (trainer characters only, so spawned gear can never reach a real world) ----
        internal static int tab; // 0 1v1, 1 waves, 2 items, 3 kits
        static string itemSearch = "";
        static int itemQuality = 1;
        static Vector2 scrollI;
        static List<KeyValuePair<GameObject, string>> items;

        internal static List<KeyValuePair<GameObject, string>> Items() {
            if (items != null) return items;
            items = new List<KeyValuePair<GameObject, string>>();
            foreach (var go in ObjectDB.instance.m_items) {
                var d = go != null ? go.GetComponent<ItemDrop>() : null;
                if (d == null || go.name.StartsWith("FW_") || go.name.StartsWith("SP_")) continue; // internal copies (3 Fenris coats)
                string label = Localization.instance.Localize(d.m_itemData.m_shared.m_name);
                if (string.IsNullOrEmpty(label) || label.StartsWith("[") || d.m_itemData.m_shared.m_icons.Length == 0) continue;
                items.Add(new KeyValuePair<GameObject, string>(go, label));
            }
            items = items.OrderBy(k => k.Value).ToList();
            return items;
        }

        static void Give(GameObject prefab, bool stack) {
            var p = Player.m_localPlayer;
            var it = prefab.GetComponent<ItemDrop>().m_itemData.Clone();
            it.m_dropPrefab = prefab;
            if (it.m_shared.m_maxQuality > 1) it.m_quality = itemQuality; // past max = forge of potential levels
            it.m_stack = stack ? it.m_shared.m_maxStackSize : 1;
            it.m_durability = it.GetMaxDurability();
            if (!p.GetInventory().AddItem(it)) p.Message(MessageHud.MessageType.Center, "Inventory full");
        }

        static void DrawItems() {
            if (!IsTrainer(Player.m_localPlayer)) {
                GUILayout.Label("Item spawning only works on trainer characters, so spawned gear can never leave the arena. Copy a character above first.", sLbl);
                return;
            }
            GUILayout.BeginHorizontal();
            GUILayout.Label("Search", sDim, GUILayout.Width(56));
            itemSearch = GUILayout.TextField(itemSearch, sField);
            GUILayout.Space(12);
            GUILayout.Label("Quality", sDim, GUILayout.Width(52));
            if (GUILayout.Button("-", sBtn, GUILayout.Width(26)) && itemQuality > 1) itemQuality--;
            GUILayout.Label(itemQuality.ToString(), sNum, GUILayout.Width(30));
            if (GUILayout.Button("+", sBtn, GUILayout.Width(26))) itemQuality++;
            GUILayout.EndHorizontal();
            scrollI = GUILayout.BeginScrollView(scrollI, GUILayout.Height(470));
            string q = itemSearch.Trim().ToLowerInvariant();
            if (q.Length < 2) GUILayout.Label("Type at least 2 letters to search all items.", sDim);
            else foreach (var kv in Items()) {
                if (!kv.Value.ToLowerInvariant().Contains(q) && !kv.Key.name.ToLowerInvariant().Contains(q)) continue;
                var sh = kv.Key.GetComponent<ItemDrop>().m_itemData.m_shared;
                GUILayout.BeginHorizontal(sRow);
                GUILayout.Label(kv.Value, sLbl, GUILayout.Width(260));
                GUILayout.Label(sh.m_maxQuality > 1 ? "q1-" + sh.m_maxQuality : sh.m_maxStackSize > 1 ? "stack " + sh.m_maxStackSize : "", sDim, GUILayout.Width(90));
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Give 1", sBtn, GUILayout.Width(70))) Give(kv.Key, false);
                if (sh.m_maxStackSize > 1 && GUILayout.Button("Stack", sBtn, GUILayout.Width(70))) Give(kv.Key, true);
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();
            GUILayout.Label("Quality applies to weapons, shields and armor; above an item's max it counts as forge levels.", sDim);
        }

        static void Draw(int id) {
            if (GUI.Button(new Rect(win.width - 40, 6, 30, 26), "X", sBtn)) { SetMenu(false); return; } // close; creatures stay frozen
            DrawCharacter();
            GUILayout.Space(8);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("1v1", tab == 0 ? sOn : sBtn, GUILayout.Height(30))) tab = 0;
            if (GUILayout.Button("Waves", tab == 1 ? sOn : sBtn, GUILayout.Height(30))) { tab = 1; Waves.Reload(); }
            if (GUILayout.Button("Items", tab == 2 ? sOn : sBtn, GUILayout.Height(30))) tab = 2;
            if (GUILayout.Button("Kits", tab == 3 ? sOn : sBtn, GUILayout.Height(30))) { tab = 3; Kits.Reload(); }
            GUILayout.EndHorizontal();
            if (tab == 1) { Waves.Draw(); GUI.DragWindow(); return; }
            if (tab == 2 || tab == 3) {
                if (tab == 2) DrawItems(); else Kits.Draw();
                GUILayout.Space(6);
                DrawFightButton();
                GUI.DragWindow();
                return;
            }
            GUILayout.Label("Add creatures", sHead);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Search", sDim, GUILayout.Width(56));
            search = GUILayout.TextField(search, sField);
            GUILayout.EndHorizontal();
            scrollA = GUILayout.BeginScrollView(scrollA, GUILayout.Height(240));
            string q = search.Trim().ToLowerInvariant();
            foreach (var kv in Catalog()) {
                if (q.Length > 0 && !kv.Value.ToLowerInvariant().Contains(q) && !kv.Key.ToLowerInvariant().Contains(q)) continue;
                GUILayout.BeginHorizontal(sRow);
                GUILayout.Label(kv.Value, sLbl, GUILayout.Width(220));
                GUILayout.Label(kv.Key, sDim);
                if (GUILayout.Button("Add", sBtn, GUILayout.Width(60))) {
                    if (Waves.Running) Waves.Stop();
                    var e = Setup.FirstOrDefault(x => x.Prefab == kv.Key && x.Stars == 0);
                    if (e != null) { e.Count++; SpawnOne(e); }           // same creature again: one more
                    else { e = new Entry { Prefab = kv.Key, Label = kv.Value }; Setup.Add(e); SpawnOne(e); }
                }
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();

            GUILayout.Label("In the arena (frozen until Fight!)", sHead);
            scrollB = GUILayout.BeginScrollView(scrollB, GUILayout.Height(170));
            if (Setup.Count == 0) GUILayout.Label("Nothing yet: press Add on a creature above and it appears in front of you.", sDim);
            for (int i = 0; i < Setup.Count; i++) {
                var e = Setup[i];
                GUILayout.BeginHorizontal(sRow);
                GUILayout.Label(e.Label, sLbl, GUILayout.Width(190));
                GUILayout.Label("Stars", sDim, GUILayout.Width(40));
                for (int s = 0; s <= 2; s++) if (GUILayout.Button(s.ToString(), e.Stars == s ? sOn : sBtn, GUILayout.Width(30)) && e.Stars != s) { e.Stars = s; SpawnEntry(e); }
                GUILayout.Space(12);
                GUILayout.Label("Count", sDim, GUILayout.Width(44));
                if (GUILayout.Button("-", sBtn, GUILayout.Width(26)) && e.Count > 1) {
                    e.Count--;
                    var last = e.Live.LastOrDefault(c => c != null);
                    if (last != null) { e.Live.Remove(last); Despawn(last); }
                }
                GUILayout.Label(e.Count.ToString(), sNum, GUILayout.Width(30));
                if (GUILayout.Button("+", sBtn, GUILayout.Width(26)) && e.Count < 20) { e.Count++; SpawnOne(e); }
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Remove", sBtn, GUILayout.Width(74))) { DespawnEntry(e); Setup.RemoveAt(i); i--; }
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Distance " + distance.ToString("0") + " m", sLbl, GUILayout.Width(120));
            distance = Mathf.Round(GUILayout.HorizontalSlider(distance, 4f, 40f, GUILayout.ExpandWidth(true)));
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Respawn all", sBtn, GUILayout.Height(30))) RespawnAll();
            if (GUILayout.Button("Remove all", sBtn, GUILayout.Height(30))) { foreach (var e in Setup) DespawnEntry(e); Setup.Clear(); Clear(); }
            if (GUILayout.Button("Heal me", sBtn, GUILayout.Height(30))) { var p = Player.m_localPlayer; p.SetHealth(p.GetMaxHealth()); p.AddStamina(p.GetMaxStamina()); }
            GUILayout.EndHorizontal();
            GUILayout.Space(6);
            DrawFightButton();
            GUI.DragWindow();
        }

        static void DrawFightButton() {
            int n = AliveCount();
            if (n > 0) { if (GUILayout.Button("Fight!  (" + n + " creature" + (n == 1 ? "" : "s") + ", 3 s countdown)", sOn, GUILayout.Height(36))) StartFight(); }
            else GUILayout.Label("Add a creature to start a fight. X or F8 closes the menu.", sDim);
        }
    }

    // scrolling the menu must not zoom the camera: the camera's scroll read returns 0 while the menu is open
    [HarmonyPatch(typeof(GameCamera), "UpdateCamera")]
    static class NoZoomInMenu {
        static float Scroll() { return Trainer.MenuOpen ? 0f : ZInput.GetMouseScrollWheel(); }
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> code) {
            var orig = AccessTools.Method(typeof(ZInput), "GetMouseScrollWheel");
            var mine = AccessTools.Method(typeof(NoZoomInMenu), "Scroll");
            foreach (var ins in code) {
                if ((ins.opcode == System.Reflection.Emit.OpCodes.Call || ins.opcode == System.Reflection.Emit.OpCodes.Callvirt) && Equals(ins.operand, orig))
                    ins.operand = mine;
                yield return ins;
            }
        }
    }

    [HarmonyPatch(typeof(MonsterAI), "UpdateAI")]
    static class FreezeMonsters {
        static bool Prefix(MonsterAI __instance, ref bool __result) {
            if (!Trainer.Frozen || !Trainer.Spawned.Contains(__instance)) return true;
            __instance.StopMoving(); // skipping the AI alone keeps its last move order: it walked on
            __result = false; return false;
        }
    }

    [HarmonyPatch(typeof(AnimalAI), "UpdateAI")]
    static class FreezeAnimals {
        static bool Prefix(AnimalAI __instance, ref bool __result) {
            if (!Trainer.Frozen || !Trainer.Spawned.Contains(__instance)) return true;
            __instance.StopMoving();
            __result = false; return false;
        }
    }

    [HarmonyPatch(typeof(Player), "OnSpawned")]
    static class Arrive {
        static void Postfix(Player __instance) {
            if (__instance != Player.m_localPlayer) return;
            if (Plugin.Active) Trainer.OnArrive(__instance);
            else if (Trainer.IsTrainer(__instance)) { // trainers never enter real worlds: their gear may be spawned
                __instance.Message(MessageHud.MessageType.Center, "Trainer characters can only enter Combat Trainer Arena worlds");
                Trainer.KickPending = true;
            }
        }
    }

    // arena: eating and drinking never uses the item up, so long wave runs don't stop for restocking
    [HarmonyPatch(typeof(Player), "ConsumeItem")]
    static class FreeConsumables {
        internal static ItemDrop.ItemData Item;
        static void Prefix(Player __instance, ItemDrop.ItemData item) { if (Plugin.Active && __instance == Player.m_localPlayer) Item = item; }
        static System.Exception Finalizer(System.Exception __exception) { Item = null; return __exception; }
    }

    [HarmonyPatch(typeof(Inventory), "RemoveOneItem")]
    static class KeepConsumed {
        static bool Prefix(ItemDrop.ItemData item, ref bool __result) {
            if (FreeConsumables.Item == null || item != FreeConsumables.Item) return true;
            __result = true; return false;
        }
    }

    [HarmonyPatch(typeof(Player), "OnDeath")]
    static class RememberDeath {
        static void Prefix(Player __instance) {
            if (!Plugin.Active || __instance != Player.m_localPlayer) return;
            Trainer.DiedHere = true; Trainer.MenuOpen = false; Trainer.Frozen = false;
            DeathSpot.Pos = __instance.transform.position;
            // repair before the respawn save: the game won't re-equip anything at 0 durability, so armor that
            // broke in the fight would come back unequipped
            Trainer.RepairAll(__instance);
        }
    }

    // Player.Load reads the inventory before the saved row count ("invrows", the pocket upgrades), so in the
    // arena open every row first and apply the saved count afterwards: nothing in the extra rows gets dropped
    // Only ever grows the inventory: inventory mods (AzuEPI, ExtraSlots...) keep equipment/quick slots in rows
    // below the normal ones, and shrinking past those would drop the items in them.
    [HarmonyPatch(typeof(Player), "Load")]
    static class KeepExtraRows {
        static void Prefix(Player __instance, out int __state) {
            var inv = __instance.GetInventory(); __state = inv.GetHeight();
            if (Plugin.Active) inv.SetHeight(Mathf.Max(__state, 9));
        }
        static void Postfix(Player __instance, int __state) {
            if (!Plugin.Active) return;
            string v; int rows;
            int saved = __instance.TryGetUniqueKeyValue("invrows", out v) && int.TryParse(v, out rows) ? Mathf.Clamp(rows, 4, 9) : 4;
            __instance.GetInventory().SetHeight(Mathf.Max(__state, saved));
        }
    }

    // typing in the menu must not reach the game (M opened the map, T danced): while it is open, every
    // ZInput key/button query answers "not pressed". The menu itself reads keys through Unity's GUI events.
    [HarmonyPatch]
    static class MuteGameKeys {
        static IEnumerable<System.Reflection.MethodBase> TargetMethods() {
            var names = new[] { "GetButton", "GetButtonDown", "GetButtonUp", "GetKey", "GetKeyDown", "GetKeyUp" };
            return AccessTools.GetDeclaredMethods(typeof(ZInput)).Where(m => m.IsStatic && m.ReturnType == typeof(bool) && names.Contains(m.Name)).Cast<System.Reflection.MethodBase>();
        }
        static void Postfix(ref bool __result) { if (Trainer.MenuOpen) __result = false; }
    }

    // a creature died in the arena (your kill or not): repair your gear. Player.OnDeath overrides this, so
    // your own death doesn't land here; respawning repairs instead (OnArrive).
    [HarmonyPatch(typeof(Character), "OnDeath")]
    static class RepairOnKill {
        static void Postfix(Character __instance) {
            if (Plugin.Active && !(__instance is Player) && Player.m_localPlayer != null) Trainer.RepairAll(Player.m_localPlayer);
        }
    }

    [HarmonyPatch(typeof(Player), "EatFood")]
    static class HealAfterEating {
        static void Postfix(Player __instance, bool __result) { if (__result && Plugin.Active && __instance == Player.m_localPlayer) Trainer.HealSoon(); }
    }

    // arena deaths: respawn after 2 s (vanilla 10 s) where you died, not at the world spawn
    [HarmonyPatch(typeof(Game), "RequestRespawn")]
    static class QuickRespawn {
        static void Prefix(ref float delay, bool afterDeath) { if (Plugin.Active && afterDeath) delay = Mathf.Min(delay, 2f); }
    }

    [HarmonyPatch(typeof(Game), "FindSpawnPoint")]
    static class DeathSpot {
        internal static Vector3? Pos;
        static bool Prefix(ref bool __result, out Vector3 point, out bool usedLogoutPoint) {
            point = Vector3.zero; usedLogoutPoint = false;
            if (!Plugin.Active || Pos == null) return true;
            point = Pos.Value;
            float h;
            if (ZoneSystem.instance.GetGroundHeight(point, out h) && point.y < h) point.y = h;
            point.y += 0.25f;
            usedLogoutPoint = true; // keeps the game from making this your home point
            Pos = null;
            __result = true;
            return false;
        }
    }

    [HarmonyPatch(typeof(Player), "CreateTombStone")]
    static class NoTombstone { static bool Prefix() { return !Plugin.Active; } } // gear stays on you

    [HarmonyPatch(typeof(Skills), "OnDeath")]
    static class NoSkillLoss { static bool Prefix() { return !Plugin.Active; } }

    // menu open: no walking/attacking/looking, free cursor
    [HarmonyPatch(typeof(Player), "TakeInput")]
    static class BlockPlayerInput { static void Postfix(ref bool __result) { if (Trainer.MenuOpen) __result = false; } }

    [HarmonyPatch(typeof(PlayerController), "TakeInput")]
    static class BlockControllerInput { static void Postfix(ref bool __result) { if (Trainer.MenuOpen) __result = false; } }

    [HarmonyPatch(typeof(GameCamera), "UpdateMouseCapture")]
    static class FreeCursor {
        static void Postfix() { if (Trainer.MenuOpen) { ZCursor.LockState = CursorLockMode.None; ZCursor.Show(); } }
    }
}
