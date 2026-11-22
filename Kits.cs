// Combat Trainer kits: whole-inventory loadouts you can apply in one click (trainer characters, arena only).
// - Preset kits ship with the mod as .kit text files in <plugin folder>/kits/. Editing one saves your version to
//   BepInEx/config/CombatTrainer/kits-preset-edits/ (Reset to Default deletes it). Author Mode (config) saves
//   edits straight into the shipped files instead, and adds "Ship with mod" to your own kits.
// - Your own kits live in BepInEx/config/CombatTrainer/kits/.
// - The editor is a mock 8x6 inventory (both pocket upgrades): click a slot, search, set quality/stack/equip.
// - Mead packs and skill presets sit next to the kit list.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace CombatTrainer {
    [Serializable] public class KitItem { public string prefab; public int quality = 1; public int stack = 1; public int x; public int y; public bool equip; }
    [Serializable] public class Kit { public string name = "New kit"; public List<KitItem> items = new List<KitItem>(); }

    class KitEntry { public Kit Kit; public bool Preset; public bool Modified; public string File, Src; }

    static class Kits {
        internal static ConfigEntry<bool> AuthorMode;
        const int W = 8, H = 6; // 4 rows + deeper pockets + wider pockets
        const string SkillsKey = "combattrainer_skills";

        // mod managers may flatten the shipped kits/ folder, so presets are any *.kit anywhere in the mod's own folder
        static string ModDir { get { return Path.GetDirectoryName(typeof(Plugin).Assembly.Location); } }
        static string PresetDir { get { return Path.Combine(ModDir, "kits"); } }
        static string Root { get { return Path.Combine(Paths.ConfigPath, "CombatTrainer"); } }
        static string EditDir { get { return Path.Combine(Root, "kits-preset-edits"); } }
        static string UserDir { get { return Path.Combine(Root, "kits"); } }

        static List<KitEntry> entries = new List<KitEntry>();
        static string msg = "";

        // ---- storage ----
        // .kit text files: "name=..." then one "item=prefab|quality|stack|x|y|equip" line per item
        // (JsonUtility silently dropped the item list, so kits saved empty)
        static Kit Read(string path) {
            try {
                var k = new Kit();
                foreach (var line in File.ReadAllLines(path)) {
                    if (line.StartsWith("name=")) k.name = line.Substring(5);
                    else if (line.StartsWith("item=")) {
                        var f = line.Substring(5).Split('|');
                        if (f.Length < 6) continue;
                        k.items.Add(new KitItem { prefab = f[0], quality = int.Parse(f[1]), stack = int.Parse(f[2]), x = int.Parse(f[3]), y = int.Parse(f[4]), equip = f[5] == "1" });
                    }
                }
                return k;
            } catch (Exception e) { Debug.LogError("[CombatTrainer] bad kit file " + path + ": " + e.Message); return null; }
        }
        static void Write(string path, Kit k) {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var lines = new List<string> { "name=" + k.name.Replace("\n", " ") };
            foreach (var i in k.items) lines.Add("item=" + i.prefab + "|" + i.quality + "|" + i.stack + "|" + i.x + "|" + i.y + "|" + (i.equip ? 1 : 0));
            File.WriteAllLines(path, lines.ToArray());
        }

        internal static void Reload() {
            entries.Clear();
                foreach (var f in Directory.GetFiles(ModDir, "*.kit", SearchOption.AllDirectories).OrderBy(x => Path.GetFileName(x))) {
                    string name = Path.GetFileName(f), edit = Path.Combine(EditDir, name);
                    bool mod = !AuthorMode.Value && File.Exists(edit);
                    var k = Read(mod ? edit : f);
                    if (k != null) entries.Add(new KitEntry { Kit = k, Preset = true, Modified = mod, File = name, Src = f });
                }
            if (Directory.Exists(UserDir))
                foreach (var f in Directory.GetFiles(UserDir, "*.kit").OrderBy(x => x)) {
                    var k = Read(f);
                    if (k != null) entries.Add(new KitEntry { Kit = k, Preset = false, File = Path.GetFileName(f) });
                }
        }

        static string NewFileName(string name) {
            string safe = new string((name ?? "kit").Where(c => char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == ' ').ToArray()).Trim().Replace(' ', '-');
            if (safe.Length == 0) safe = "kit";
            return safe + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".kit";
        }

        static void Save(KitEntry e, Kit k) {
            if (e.Preset) Write(AuthorMode.Value ? e.Src : Path.Combine(EditDir, e.File), k);
            else Write(Path.Combine(UserDir, e.File), k);
        }

        // ---- applying ----
        static ItemDrop.ItemData Make(string prefabName, int quality, int stack) {
            var prefab = ObjectDB.instance.GetItemPrefab(prefabName);
            if (prefab == null) return null;
            var it = prefab.GetComponent<ItemDrop>().m_itemData.Clone();
            it.m_dropPrefab = prefab;
            if (it.m_shared.m_maxQuality > 1) it.m_quality = Mathf.Max(1, quality);
            it.m_stack = Mathf.Clamp(stack, 1, it.m_shared.m_maxStackSize);
            it.m_durability = it.GetMaxDurability();
            return it;
        }

        static void Use(Kit k) {
            var p = Player.m_localPlayer; var inv = p.GetInventory();
            p.UnequipAllItems();
            inv.RemoveAll();
            p.SetInventorySize(H);
            var equip = new List<ItemDrop.ItemData>();
            int missing = 0;
            foreach (var ki in k.items) {
                var it = Make(ki.prefab, ki.quality, ki.stack);
                if (it == null || !inv.AddItem(it, new Vector2i(ki.x, ki.y))) { missing++; continue; }
                if (ki.equip) equip.Add(it);
            }
            foreach (var it in equip) p.EquipItem(it, false);
            // the kit's foods replace what you've eaten (first 3 kinds in slot order, not taken from the stack);
            // a kit with no food leaves your belly as it is
            var foods = k.items.OrderBy(i => i.y).ThenBy(i => i.x).Select(i => Make(i.prefab, 1, 1))
                .Where(f => f != null && (f.m_shared.m_food > 0f || f.m_shared.m_foodStamina > 0f || f.m_shared.m_foodEitr > 0f))
                .GroupBy(f => f.m_dropPrefab.name).Select(g => g.First()).Take(3).ToList();
            if (foods.Count > 0) {
                p.ClearFood();
                foreach (var f in foods) p.EatFood(f);
                Trainer.LastFoods = foods.Select(f => f.m_dropPrefab.name).ToList();
            }
            Trainer.HealSoon();
            msg = "Using kit \"" + k.name + "\"." + (missing > 0 ? " " + missing + " item(s) couldn't be placed (unknown or blocked slot)." : "") + " Re-copy brings your own gear back.";
        }

        static Kit FromInventory(Player p) {
            var k = new Kit { name = "My kit " + DateTime.Now.ToString("MMM d HH:mm") };
            foreach (var it in p.GetInventory().GetAllItems()) {
                if (it.m_dropPrefab == null || it.m_gridPos.y >= H) continue;
                k.items.Add(new KitItem { prefab = it.m_dropPrefab.name, quality = it.m_quality, stack = it.m_stack, x = it.m_gridPos.x, y = it.m_gridPos.y, equip = it.m_equipped });
            }
            return k;
        }

        static void Give(string prefabName, int stack) {
            var it = Make(prefabName, 1, stack);
            if (it != null && !Player.m_localPlayer.GetInventory().AddItem(it)) msg = "Inventory full.";
        }

        // ---- meads and skills ----
        static readonly string[] Healing = { "MeadHealthMinor", "MeadHealthMedium", "MeadHealthMajor", "MeadHealthLingering", "MeadStaminaMinor", "MeadStaminaMedium", "MeadStaminaLingering", "MeadEitrMinor", "MeadEitrLingering", "MeadTasty" };
        static readonly string[] Resist = { "BarleyWine", "MeadFrostResist", "MeadPoisonResist" };
        static readonly string[] Combat = { "MeadBzerker", "MeadHasty", "MeadLightfoot", "MeadStrength", "MeadSwimmer", "MeadTrollPheromones", "MeadBugRepellent", "MeadTamer" };

        static IEnumerable<Skills.Skill> AllSkills(Player p) {
            var sk = p.GetSkills(); var tr = Traverse.Create(sk);
            foreach (var def in sk.m_skills) {
                Skills.Skill s = null;
                try { s = tr.Method("GetSkill", new[] { typeof(Skills.SkillType) }, new object[] { def.m_skill }).GetValue<Skills.Skill>(); } catch { }
                if (s != null) yield return s;
            }
        }

        static void SetSkills(Player p, float level) {
            foreach (var s in AllSkills(p)) { s.m_level = level; s.m_accumulator = 0f; }
            msg = "All skills set to " + level + ".";
        }

        internal static void RememberSkills(Player p) { // called after a character copy
            p.m_customData[SkillsKey] = string.Join(";", p.GetSkills().GetSkillList().Select(s => (int)s.m_info.m_skill + "=" + s.m_level.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToArray());
        }

        static void RestoreSkills(Player p) {
            string data;
            if (!p.m_customData.TryGetValue(SkillsKey, out data)) { msg = "No imported skills stored yet: Re-copy your character once."; return; }
            var map = new Dictionary<int, float>();
            foreach (var part in data.Split(';')) {
                var kv = part.Split('='); int t; float v;
                if (kv.Length == 2 && int.TryParse(kv[0], out t) && float.TryParse(kv[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v)) map[t] = v;
            }
            foreach (var s in AllSkills(p)) { float v; s.m_level = map.TryGetValue((int)s.m_info.m_skill, out v) ? v : 0f; s.m_accumulator = 0f; }
            msg = "Skills set back to your imported character's.";
        }

        // ---- UI ----
        static KitEntry editing; static Kit draft; static int sel = -1; static string slotSearch = "";
        static bool focusSearch; static int dragFrom = -1; static Vector2 dragStart; static Sprite dragIcon;
        static Vector2 scrollList, scrollPick;
        static string armDelete;

        internal static void Draw() {
            var p = Player.m_localPlayer;
            if (!Trainer.IsTrainer(p)) {
                GUILayout.Label("Kits only work on trainer characters, so their gear can never leave the arena. Copy a character above first.", Trainer.sLbl);
                return;
            }
            if (draft != null) { DrawEditor(); return; }

            GUILayout.Label("Kits (replace your whole inventory)", Trainer.sHead);
            scrollList = GUILayout.BeginScrollView(scrollList, GUILayout.Height(250));
            if (entries.Count == 0) GUILayout.Label("No kits yet. Make one below.", Trainer.sDim);
            foreach (var e in entries.ToList()) {
                GUILayout.BeginHorizontal(Trainer.sRow);
                GUILayout.Label(e.Kit.name + "  <size=12><color=#b4a990>" + (e.Preset ? (e.Modified ? "preset, edited" : "preset") : "yours") + " · " + e.Kit.items.Count + " items</color></size>", Trainer.sLbl, GUILayout.Width(300));
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Use", Trainer.sOn, GUILayout.Width(56))) Use(e.Kit);
                if (GUILayout.Button("Edit", Trainer.sBtn, GUILayout.Width(56))) { editing = e; draft = Clone(e.Kit); sel = -1; }
                if (e.Preset) {
                    if (e.Modified && GUILayout.Button("Reset to Default", Trainer.sBtn, GUILayout.Width(140))) { File.Delete(Path.Combine(EditDir, e.File)); Reload(); msg = "Reset \"" + e.Kit.name + "\" to the shipped version."; }
                    if (!e.Modified) GUILayout.Space(144);
                } else {
                    bool armed = armDelete == e.File;
                    if (GUILayout.Button(armed ? "Sure?" : "Delete", armed ? Trainer.sOn : Trainer.sBtn, GUILayout.Width(70))) {
                        if (armed) { File.Delete(Path.Combine(UserDir, e.File)); armDelete = null; Reload(); } else armDelete = e.File;
                    }
                    if (AuthorMode.Value && GUILayout.Button("Ship with mod", Trainer.sBtn, GUILayout.Width(120))) {
                        Directory.CreateDirectory(PresetDir);
                        File.Move(Path.Combine(UserDir, e.File), Path.Combine(PresetDir, e.File)); Reload();
                    }
                }
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("New empty kit", Trainer.sBtn, GUILayout.Height(30))) { editing = new KitEntry { Preset = false, File = null }; draft = new Kit(); sel = -1; }
            if (GUILayout.Button("Save my current inventory as a kit", Trainer.sBtn, GUILayout.Height(30))) { editing = new KitEntry { Preset = false, File = null }; draft = FromInventory(p); sel = -1; }
            GUILayout.EndHorizontal();

            GUILayout.Label("Meads (10 of each)", Trainer.sHead);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Health, stamina, eitr", Trainer.sBtn)) foreach (var m in Healing) Give(m, 10);
            if (GUILayout.Button("Resistances", Trainer.sBtn)) foreach (var m in Resist) Give(m, 10);
            if (GUILayout.Button("Utility", Trainer.sBtn)) foreach (var m in Combat) Give(m, 10);
            if (GUILayout.Button("All", Trainer.sBtn)) foreach (var m in Healing.Concat(Resist).Concat(Combat)) Give(m, 10);
            GUILayout.EndHorizontal();

            GUILayout.Label("Skills", Trainer.sHead);
            GUILayout.BeginHorizontal();
            foreach (int lv in new[] { 25, 50, 75, 100 }) if (GUILayout.Button("All " + lv, Trainer.sBtn)) SetSkills(p, lv);
            if (GUILayout.Button("Use imported character skills", Trainer.sBtn, GUILayout.Width(250))) RestoreSkills(p);
            GUILayout.EndHorizontal();
            if (msg.Length > 0) GUILayout.Label(msg, Trainer.sDim);
        }

        static bool IsArmor(ItemDrop.ItemData.ItemType t) { // worn pieces start out equipped; weapons are your choice
            return t == ItemDrop.ItemData.ItemType.Helmet || t == ItemDrop.ItemData.ItemType.Chest || t == ItemDrop.ItemData.ItemType.Legs
                || t == ItemDrop.ItemData.ItemType.Shoulder || t == ItemDrop.ItemData.ItemType.Utility || t == ItemDrop.ItemData.ItemType.Trinket;
        }

        static Kit Clone(Kit k) { return new Kit { name = k.name, items = k.items.Select(i => new KitItem { prefab = i.prefab, quality = i.quality, stack = i.stack, x = i.x, y = i.y, equip = i.equip }).ToList() }; }

        static KitItem At(int x, int y) { return draft.items.FirstOrDefault(i => i.x == x && i.y == y); }

        static void DrawIcon(Rect r, Sprite s) {
            if (s == null) return;
            var t = s.texture; var tr = s.textureRect;
            GUI.DrawTextureWithTexCoords(r, t, new Rect(tr.x / t.width, tr.y / t.height, tr.width / t.width, tr.height / t.height));
        }

        static void DrawEditor() {
            GUILayout.BeginHorizontal();
            GUILayout.Label("Kit name", Trainer.sDim, GUILayout.Width(70));
            draft.name = GUILayout.TextField(draft.name, Trainer.sField);
            GUILayout.EndHorizontal();
            GUILayout.Label("Click a slot and type to fill it. Drag items to move or swap them. Gold border = equipped when the kit is used.", Trainer.sDim);

            const float cell = 66f, gap = 4f;
            var area = GUILayoutUtility.GetRect(W * (cell + gap), H * (cell + gap));
            var ev = Event.current;
            bool dragging = dragFrom >= 0 && (ev.mousePosition - dragStart).magnitude > 6f;
            if (ev.type == EventType.Repaint && !Input.GetMouseButton(0)) dragFrom = -1; // released outside the window
            if (ev.type == EventType.MouseUp && dragFrom >= 0) {
                int tx = Mathf.FloorToInt((ev.mousePosition.x - area.x) / (cell + gap)), ty = Mathf.FloorToInt((ev.mousePosition.y - area.y) / (cell + gap));
                int to = ty * W + tx;
                if (dragging && tx >= 0 && tx < W && ty >= 0 && ty < H && to != dragFrom) {
                    var a = At(dragFrom % W, dragFrom / W); var b = At(tx, ty);
                    if (b != null) { b.x = dragFrom % W; b.y = dragFrom / W; }
                    if (a != null) { a.x = tx; a.y = ty; }
                    sel = to; focusSearch = true;
                }
                dragFrom = -1;
            }
            for (int y = 0; y < H; y++) for (int x = 0; x < W; x++) {
                var r = new Rect(area.x + x * (cell + gap), area.y + y * (cell + gap), cell, cell);
                int idx = y * W + x;
                var ki = At(x, y);
                if (ev.type == EventType.MouseDown && ev.button == 0 && ki != null && r.Contains(ev.mousePosition)) { dragFrom = idx; dragStart = ev.mousePosition; }
                if (GUI.Button(r, "", idx == sel ? Trainer.sOn : Trainer.sBtn)) { sel = idx; slotSearch = ""; focusSearch = true; }
                if (ki != null) {
                    var prefab = ObjectDB.instance.GetItemPrefab(ki.prefab);
                    var d = prefab != null ? prefab.GetComponent<ItemDrop>() : null;
                    bool lifted = dragging && idx == dragFrom;
                    if (d != null) {
                        var c0 = GUI.color; if (lifted) GUI.color = new Color(1f, 1f, 1f, 0.3f);
                        DrawIcon(new Rect(r.x + 8, r.y + 6, cell - 16, cell - 16), d.m_itemData.GetIcon());
                        GUI.color = c0;
                        if (lifted && ev.type == EventType.Repaint) dragIcon = d.m_itemData.GetIcon();
                    }
                    else GUI.Label(r, ki.prefab, Trainer.sDim);
                    string tag = (d != null && d.m_itemData.m_shared.m_maxStackSize > 1) ? ki.stack.ToString() : (d != null && d.m_itemData.m_shared.m_maxQuality > 1 ? "q" + ki.quality : "");
                    GUI.Label(new Rect(r.x + 4, r.y + cell - 22, cell - 8, 20), tag, Trainer.sNum);
                    if (ki.equip) { var c = GUI.color; GUI.color = new Color(1f, 0.72f, 0.3f); GUI.Box(new Rect(r.x, r.y, cell, 3), ""); GUI.Box(new Rect(r.x, r.y + cell - 3, cell, 3), ""); GUI.color = c; }
                }
                if (y == 0) GUI.Label(new Rect(r.x + 3, r.y + 1, 16, 16), (x + 1).ToString(), Trainer.sDim);
            }
            if (dragging && dragIcon != null) DrawIcon(new Rect(ev.mousePosition.x - 25, ev.mousePosition.y - 25, 50, 50), dragIcon);

            if (sel >= 0) DrawSlot(sel % W, sel / W);

            GUILayout.Space(6);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Save kit", Trainer.sOn, GUILayout.Height(32))) {
                if (editing.File == null) editing.File = NewFileName(draft.name);
                Save(editing, draft); draft = null; editing = null; Reload(); msg = "Saved.";
            }
            if (GUILayout.Button("Cancel", Trainer.sBtn, GUILayout.Height(32))) { draft = null; editing = null; }
            GUILayout.EndHorizontal();
        }

        static void DrawSlot(int x, int y) {
            var ki = At(x, y);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Slot " + (x + 1) + "," + (y + 1) + ": ", Trainer.sHead, GUILayout.Width(110));
            if (ki != null) {
                var prefab = ObjectDB.instance.GetItemPrefab(ki.prefab);
                var sh = prefab != null ? prefab.GetComponent<ItemDrop>().m_itemData.m_shared : null;
                GUILayout.Label(sh != null ? Localization.instance.Localize(sh.m_name) : ki.prefab, Trainer.sLbl, GUILayout.Width(170));
                if (sh != null && sh.m_maxQuality > 1) {
                    GUILayout.Label("Quality", Trainer.sDim, GUILayout.Width(52));
                    if (GUILayout.Button("-", Trainer.sBtn, GUILayout.Width(26)) && ki.quality > 1) ki.quality--;
                    GUILayout.Label(ki.quality.ToString(), Trainer.sNum, GUILayout.Width(30));
                    if (GUILayout.Button("+", Trainer.sBtn, GUILayout.Width(26))) ki.quality++;
                }
                if (sh != null && sh.m_maxStackSize > 1) {
                    GUILayout.Label("Amount", Trainer.sDim, GUILayout.Width(56));
                    if (GUILayout.Button("-", Trainer.sBtn, GUILayout.Width(26)) && ki.stack > 1) ki.stack--;
                    GUILayout.Label(ki.stack.ToString(), Trainer.sNum, GUILayout.Width(40));
                    if (GUILayout.Button("+", Trainer.sBtn, GUILayout.Width(26)) && ki.stack < sh.m_maxStackSize) ki.stack++;
                    if (GUILayout.Button("Max", Trainer.sBtn, GUILayout.Width(44))) ki.stack = sh.m_maxStackSize;
                }
                GUILayout.FlexibleSpace();
                if (sh != null && prefab.GetComponent<ItemDrop>().m_itemData.IsEquipable())
                    if (GUILayout.Button(ki.equip ? "Equipped" : "Equip", ki.equip ? Trainer.sOn : Trainer.sBtn, GUILayout.Width(84))) ki.equip = !ki.equip;
                if (GUILayout.Button("Empty", Trainer.sBtn, GUILayout.Width(64))) draft.items.Remove(ki);
            } else GUILayout.Label("empty", Trainer.sDim);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label(ki == null ? "Put in:" : "Replace:", Trainer.sDim, GUILayout.Width(62));
            GUI.SetNextControlName("ct_slotsearch");
            slotSearch = GUILayout.TextField(slotSearch, Trainer.sField);
            GUILayout.EndHorizontal();
            if (focusSearch && Event.current.type == EventType.Repaint) { GUI.FocusControl("ct_slotsearch"); focusSearch = false; }
            string q = slotSearch.Trim().ToLowerInvariant();
            if (q.Length < 2) { GUILayout.Label("Type at least 2 letters to search all items.", Trainer.sDim); return; }
            scrollPick = GUILayout.BeginScrollView(scrollPick, GUILayout.Height(150));
            foreach (var kv in Trainer.Items().Where(k => k.Value.ToLowerInvariant().Contains(q) || k.Key.name.ToLowerInvariant().Contains(q)).Take(40)) {
                GUILayout.BeginHorizontal(Trainer.sRow);
                GUILayout.Label(kv.Value, Trainer.sLbl, GUILayout.Width(300));
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Put here", Trainer.sBtn, GUILayout.Width(90))) {
                    if (ki != null) draft.items.Remove(ki);
                    var sh = kv.Key.GetComponent<ItemDrop>().m_itemData.m_shared;
                    draft.items.Add(new KitItem { prefab = kv.Key.name, x = x, y = y, quality = 1, stack = sh.m_maxStackSize > 1 ? sh.m_maxStackSize : 1,
                        equip = IsArmor(sh.m_itemType) });
                    slotSearch = "";
                }
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();
        }
    }
}
