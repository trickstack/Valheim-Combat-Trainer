// Combat Trainer waves: endless, escalating waves of one biome's creatures from all around you (arena only).
// - A wave set is a list of creatures with a weight (how often), a tier (1-3: how much Hard/Very Hard favour it)
//   and the first wave it can show up in. Eight built-in sets, one per biome; edits and your own sets are saved as
//   .wave text files in BepInEx/config/CombatTrainer/waves/ (a built-in's edit is <id>.wave; Reset deletes it).
// - Wave n has 2+n creatures (x1.5 Hard, x2 Very Hard, max 30), spawned one by one at random spots 15-30 m around
//   you. Stars get likelier every wave. Clear a wave, short break, next one. F8 pauses; dying ends the run.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using BepInEx;
using UnityEngine;
using Random = UnityEngine.Random;

namespace CombatTrainer {
    class WaveEnemy { public string Prefab; public int Weight = 3, Tier = 1, From = 1; }
    class WaveSet { public string Id, Name; public List<WaveEnemy> Enemies = new List<WaveEnemy>(); public bool Builtin, Edited; }

    static class Waves {
        static string Dir { get { return Path.Combine(Path.Combine(Paths.ConfigPath, "CombatTrainer"), "waves"); } }

        // ---- built-in sets: prefab weight tier fromWave ----
        static readonly string[][] Defaults = {
            new[] { "meadows", "Meadows", "Greyling 6 1 1", "Boar 4 1 1", "Neck 4 1 1", "Greydwarf 2 2 3" },
            new[] { "blackforest", "Black Forest", "Greydwarf 6 1 1", "Skeleton 3 1 1", "Greydwarf_Shaman 2 2 2", "Greydwarf_Elite 2 2 3", "Bjorn 1 3 4", "Troll 1 3 5" },
            new[] { "swamp", "Swamp", "Draugr 5 1 1", "Blob 3 1 1", "Draugr_Ranged 2 1 2", "Surtling 2 1 2", "Skeleton_Poison 2 2 2", "Draugr_Elite 2 2 3", "BlobElite 1 2 3", "Wraith 1 3 4", "Abomination 1 3 6" },
            new[] { "mountain", "Mountain", "Wolf 6 1 1", "Hatchling 3 1 1", "Bat 2 1 1", "Ulv 2 2 2", "Fenring 2 2 3", "Fenring_Cultist 1 3 4", "StoneGolem 1 3 5" },
            new[] { "plains", "Plains", "Goblin 6 1 1", "GoblinArcher 3 1 1", "Deathsquito 3 1 1", "GoblinShaman 2 2 2", "BlobTar 2 2 2", "GoblinBrute 1 3 4", "Unbjorn 1 3 4", "Lox 1 3 6" },
            new[] { "mistlands", "Mistlands", "Seeker 6 1 1", "Tick 3 1 1", "SeekerBrood 2 1 1", "SeekerBrute 1 3 3", "Gjall 1 3 5" },
            new[] { "ashlands", "Ashlands", "Charred_Melee 5 1 1", "Charred_Archer 3 1 1", "Charred_Twitcher 3 1 1", "BlobLava 2 1 1", "Volture 2 1 2", "Asksvin 3 2 2", "Charred_Mage 2 2 3", "Morgen 1 3 5", "FallenValkyrie 1 3 6" },
            new[] { "deepnorth", "Deep North", "Greydwarf_Frozen 5 1 1", "Skeleton_DeepNorth 4 1 1", "Frysling 3 1 1", "BlobFrost 2 1 1", "Greydwarf_Shaman_Frozen 2 2 2", "Elaking 2 2 2", "Moose 1 2 3", "JotunWitch 1 3 4", "JotunWarrior 1 3 4", "JotunWarriorDualWield 1 3 5", "ElakingMole 1 3 5", "Barka 1 3 6", "TrollFrost 1 3 6" },
        };
        static readonly string[] DiffNames = { "Vanilla", "Hard", "Very Hard" };
        static readonly float[] CountMul = { 1f, 1.5f, 2f }, StarMul = { 1f, 1.5f, 2f };
        static readonly float[][] TierMul = { new[] { 1f, 1f, 1f }, new[] { 1f, 1.5f, 2f }, new[] { 1f, 2f, 3.5f } };
        static readonly int[] FromShift = { 0, 1, 2 }; // harder = tougher creatures show up earlier
        static int diff;

        static List<WaveSet> sets = new List<WaveSet>();

        // ---- storage ----
        static WaveSet Parse(string[] d) {
            var s = new WaveSet { Id = d[0], Name = d[1], Builtin = true };
            foreach (var e in d.Skip(2)) { var f = e.Split(' '); s.Enemies.Add(new WaveEnemy { Prefab = f[0], Weight = int.Parse(f[1]), Tier = int.Parse(f[2]), From = int.Parse(f[3]) }); }
            return s;
        }
        static WaveSet Read(string path) {
            try {
                var s = new WaveSet { Id = Path.GetFileNameWithoutExtension(path) };
                foreach (var line in File.ReadAllLines(path)) {
                    if (line.StartsWith("name=")) s.Name = line.Substring(5);
                    else if (line.StartsWith("enemy=")) {
                        var f = line.Substring(6).Split('|');
                        if (f.Length >= 4) s.Enemies.Add(new WaveEnemy { Prefab = f[0], Weight = int.Parse(f[1]), Tier = int.Parse(f[2]), From = int.Parse(f[3]) });
                    }
                }
                return s;
            } catch (Exception e) { Debug.LogError("[CombatTrainer] bad wave file " + path + ": " + e.Message); return null; }
        }
        static void Write(WaveSet s) {
            Directory.CreateDirectory(Dir);
            var lines = new List<string> { "name=" + s.Name.Replace("\n", " ") };
            foreach (var e in s.Enemies) lines.Add("enemy=" + e.Prefab + "|" + e.Weight + "|" + e.Tier + "|" + e.From);
            File.WriteAllLines(Path.Combine(Dir, s.Id + ".wave"), lines.ToArray());
        }

        internal static void Reload() {
            sets.Clear();
            foreach (var d in Defaults) {
                var s = Parse(d);
                string f = Path.Combine(Dir, s.Id + ".wave");
                if (File.Exists(f)) { var e = Read(f); if (e != null) { e.Builtin = true; e.Edited = true; s = e; } }
                sets.Add(s);
            }
            if (Directory.Exists(Dir))
                foreach (var f in Directory.GetFiles(Dir, "custom-*.wave").OrderBy(x => x)) { var s = Read(f); if (s != null) sets.Add(s); }
        }

        static WaveSet Clone(WaveSet s) {
            return new WaveSet { Id = s.Id, Name = s.Name, Builtin = s.Builtin, Edited = s.Edited,
                Enemies = s.Enemies.Select(e => new WaveEnemy { Prefab = e.Prefab, Weight = e.Weight, Tier = e.Tier, From = e.From }).ToList() };
        }

        // ---- running ----
        internal static bool Running;
        static WaveSet run;
        static int wave, toSpawn;
        static float spawnIn, breakLeft;
        static readonly List<Character> live = new List<Character>();
        static string result = "";
        static int lastWave;

        static int Alive() { live.RemoveAll(c => c == null || c.IsDead()); return live.Count; }
        static int CountFor(int n) { return Mathf.Min(30, Mathf.RoundToInt((2 + n) * CountMul[diff])); }

        static void Start(WaveSet s, int fromWave) {
            Trainer.Clear();
            Running = true; run = s; wave = fromWave - 1; toSpawn = 0; breakLeft = 3f; result = "";
            Trainer.SetMenu(false);
        }

        internal static void Stop() { Trainer.Clear(); }

        internal static void Reset() { // all trainer creatures were just removed
            if (Running && wave > 0) lastWave = wave;
            Running = false; live.Clear(); toSpawn = 0;
        }

        internal static void Fell() {
            result = "You fell on wave " + lastWave + " of " + (run != null ? run.Name : "?") + " (" + DiffNames[diff] + ").";
        }

        internal static void Update() {
            if (!Running || Trainer.MenuOpen || !Trainer.Live() || run == null) return; // F8 pauses everything
            if (toSpawn > 0) {
                spawnIn -= Time.deltaTime;
                if (spawnIn <= 0f) { spawnIn = 0.35f; toSpawn--; SpawnOne(); }
            } else if (Alive() == 0) {
                breakLeft -= Time.deltaTime;
                if (breakLeft <= 0f) {
                    wave++; toSpawn = CountFor(wave); spawnIn = 0f; breakLeft = 6f;
                    Player.m_localPlayer.Message(MessageHud.MessageType.Center, "Wave " + wave);
                }
            }
        }

        static WaveEnemy Pick() {
            var pool = run.Enemies.Where(e => e.Weight > 0 && Mathf.Max(1, e.From - FromShift[diff]) <= wave && ZNetScene.instance.GetPrefab(e.Prefab) != null).ToList();
            if (pool.Count == 0) return null;
            float total = pool.Sum(e => e.Weight * TierMul[diff][Mathf.Clamp(e.Tier, 1, 3) - 1]), r = Random.value * total;
            foreach (var e in pool) { r -= e.Weight * TierMul[diff][Mathf.Clamp(e.Tier, 1, 3) - 1]; if (r <= 0f) return e; }
            return pool[pool.Count - 1];
        }

        static void SpawnOne() {
            var e = Pick();
            if (e == null) { toSpawn = 0; return; }
            var prefab = ZNetScene.instance.GetPrefab(e.Prefab);
            float star = Mathf.Min(0.6f, (wave - 1) * 0.06f * StarMul[diff]), roll = Random.value;
            int stars = roll < star * 0.35f && wave >= 5 ? 2 : roll < star ? 1 : 0;
            // a free spot 15-30 m away in any direction
            var p = Player.m_localPlayer.transform.position;
            float radius = Trainer.RadiusOf(prefab);
            Vector3 pos = p;
            for (int t = 0; t < 30; t++) {
                pos = p + Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * Vector3.forward * Random.Range(15f, 30f);
                if (live.All(c => c == null || Vector3.Distance(c.transform.position, pos) > radius + c.GetRadius() + 0.75f)) break;
            }
            var ch = Trainer.SpawnAt(prefab, stars, Trainer.Ground(pos));
            if (ch == null) return;
            live.Add(ch);
            var ai = ch.GetComponent<BaseAI>();
            if (ai != null) { ai.Alert(); ai.SetHuntPlayer(true); }
        }

        internal static void Hud() {
            if (!Running) return;
            string text = toSpawn > 0 || Alive() > 0 ? run.Name + "  ·  Wave " + wave + "  ·  " + (Alive() + toSpawn) + " left"
                : wave == 0 ? run.Name + "  ·  Wave 1 in " + Mathf.CeilToInt(breakLeft) : "Wave " + wave + " cleared  ·  next in " + Mathf.CeilToInt(breakLeft);
            var st = new GUIStyle(Trainer.sHead) { fontSize = 22, alignment = TextAnchor.MiddleCenter };
            var r = new Rect(0, 60, Screen.width, 34);
            var c = st.normal.textColor; st.normal.textColor = Color.black;
            GUI.Label(new Rect(r.x + 2, r.y + 2, r.width, r.height), text, st);
            st.normal.textColor = c; GUI.Label(r, text, st);
        }

        // ---- UI ----
        static WaveSet editing, draft;
        static string addSearch = "", armDelete;
        static Vector2 scrollSets, scrollEdit, scrollAdd;
        static Dictionary<string, string> names;

        static string Label(string prefab) {
            if (names == null) { names = new Dictionary<string, string>(); foreach (var kv in Trainer.Catalog()) names[kv.Key] = kv.Value; }
            string n; return names.TryGetValue(prefab, out n) ? n : prefab;
        }

        internal static void Draw() {
            if (draft != null) { DrawEditor(); return; }
            if (Running) {
                GUILayout.BeginHorizontal();
                GUILayout.Label("Running: " + run.Name + ", wave " + wave + " (" + DiffNames[diff] + "). Close the menu to carry on.", Trainer.sLbl);
                if (GUILayout.Button("Stop", Trainer.sBtn, GUILayout.Width(80), GUILayout.Height(30))) Stop();
                GUILayout.EndHorizontal();
            }
            if (result.Length > 0) {
                GUILayout.BeginHorizontal();
                GUILayout.Label(result, Trainer.sLbl);
                if (!Running && run != null && lastWave > 1 && GUILayout.Button("Retry wave " + lastWave, Trainer.sOn, GUILayout.Width(140), GUILayout.Height(30))) Start(run, lastWave);
                GUILayout.EndHorizontal();
            }
            GUILayout.Label("Difficulty", Trainer.sHead);
            GUILayout.BeginHorizontal();
            for (int i = 0; i < DiffNames.Length; i++) if (GUILayout.Button(DiffNames[i], diff == i ? Trainer.sOn : Trainer.sBtn, GUILayout.Height(30))) diff = i;
            GUILayout.EndHorizontal();
            GUILayout.Label("Hard and Very Hard send more creatures per wave, favour the tougher ones and bring them in earlier.", Trainer.sDim);

            GUILayout.Label("Wave sets", Trainer.sHead);
            scrollSets = GUILayout.BeginScrollView(scrollSets, GUILayout.Height(330));
            foreach (var s in sets.ToList()) {
                GUILayout.BeginHorizontal(Trainer.sRow);
                GUILayout.Label(s.Name + "  <size=12><color=#b4a990>" + (s.Builtin ? (s.Edited ? "built-in, edited" : "built-in") : "yours") + " · " + s.Enemies.Count + " creatures</color></size>", Trainer.sLbl, GUILayout.Width(300));
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Start", Trainer.sOn, GUILayout.Width(64))) Start(s, 1);
                if (GUILayout.Button("Edit", Trainer.sBtn, GUILayout.Width(56))) { editing = s; draft = Clone(s); }
                if (s.Builtin) {
                    if (s.Edited && GUILayout.Button("Reset to Default", Trainer.sBtn, GUILayout.Width(140))) { File.Delete(Path.Combine(Dir, s.Id + ".wave")); Reload(); }
                    if (!s.Edited) GUILayout.Space(144);
                } else {
                    bool armed = armDelete == s.Id;
                    if (GUILayout.Button(armed ? "Sure?" : "Delete", armed ? Trainer.sOn : Trainer.sBtn, GUILayout.Width(70))) {
                        if (armed) { File.Delete(Path.Combine(Dir, s.Id + ".wave")); armDelete = null; Reload(); } else armDelete = s.Id;
                    }
                    GUILayout.Space(70);
                }
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();
            if (GUILayout.Button("New wave set", Trainer.sBtn, GUILayout.Height(30))) {
                editing = null; draft = new WaveSet { Id = "custom-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture), Name = "My waves" };
            }
            GUILayout.Label("Creatures come from every side, 15-30 m out. F8 pauses; dying ends the run. Food and meads aren't used up in the Arena.", Trainer.sDim);
        }

        static void Stepper(string label, ref int v, int min, int max, float labelW) {
            GUILayout.Label(label, Trainer.sDim, GUILayout.Width(labelW));
            if (GUILayout.Button("-", Trainer.sBtn, GUILayout.Width(24)) && v > min) v--;
            GUILayout.Label(v.ToString(), Trainer.sNum, GUILayout.Width(26));
            if (GUILayout.Button("+", Trainer.sBtn, GUILayout.Width(24)) && v < max) v++;
        }

        static void DrawEditor() {
            GUILayout.BeginHorizontal();
            GUILayout.Label("Name", Trainer.sDim, GUILayout.Width(50));
            draft.Name = GUILayout.TextField(draft.Name, Trainer.sField);
            GUILayout.EndHorizontal();
            GUILayout.Label("Weight: how often it shows up. Tier: how much Hard and Very Hard favour it. From: the first wave it can appear in.", Trainer.sDim);
            scrollEdit = GUILayout.BeginScrollView(scrollEdit, GUILayout.Height(250));
            if (draft.Enemies.Count == 0) GUILayout.Label("No creatures yet: add some below.", Trainer.sDim);
            for (int i = 0; i < draft.Enemies.Count; i++) {
                var e = draft.Enemies[i];
                GUILayout.BeginHorizontal(Trainer.sRow);
                GUILayout.Label(Label(e.Prefab), Trainer.sLbl, GUILayout.Width(150));
                Stepper("Weight", ref e.Weight, 0, 20, 50);
                GUILayout.Space(6);
                GUILayout.Label("Tier", Trainer.sDim, GUILayout.Width(30));
                for (int t = 1; t <= 3; t++) if (GUILayout.Button(t.ToString(), e.Tier == t ? Trainer.sOn : Trainer.sBtn, GUILayout.Width(26))) e.Tier = t;
                GUILayout.Space(6);
                Stepper("From", ref e.From, 1, 50, 40);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("X", Trainer.sBtn, GUILayout.Width(28))) { draft.Enemies.RemoveAt(i); i--; }
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Add", Trainer.sDim, GUILayout.Width(40));
            addSearch = GUILayout.TextField(addSearch, Trainer.sField);
            GUILayout.EndHorizontal();
            string q = addSearch.Trim().ToLowerInvariant();
            if (q.Length >= 2) {
                scrollAdd = GUILayout.BeginScrollView(scrollAdd, GUILayout.Height(120));
                foreach (var kv in Trainer.Catalog().Where(k => k.Value.ToLowerInvariant().Contains(q) || k.Key.ToLowerInvariant().Contains(q)).Take(30)) {
                    GUILayout.BeginHorizontal(Trainer.sRow);
                    GUILayout.Label(kv.Value, Trainer.sLbl, GUILayout.Width(220));
                    GUILayout.Label(kv.Key, Trainer.sDim);
                    if (GUILayout.Button("Add", Trainer.sBtn, GUILayout.Width(60)) && !draft.Enemies.Any(x => x.Prefab == kv.Key)) draft.Enemies.Add(new WaveEnemy { Prefab = kv.Key });
                    GUILayout.EndHorizontal();
                }
                GUILayout.EndScrollView();
            }

            GUILayout.Space(6);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Save", Trainer.sOn, GUILayout.Height(32))) { Write(draft); draft = null; editing = null; Reload(); }
            if (GUILayout.Button("Cancel", Trainer.sBtn, GUILayout.Height(32))) { draft = null; editing = null; }
            GUILayout.EndHorizontal();
        }
    }
}
