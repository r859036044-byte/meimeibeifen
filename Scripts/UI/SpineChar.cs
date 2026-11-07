using Godot;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;

/// <summary>
/// Spine 立繪（純 C# 讀取 Spine 4.2 匯出的 JSON + atlas，不需要任何外掛）。
///
/// 支援範圍（對應「妹妹立繪」這份檔案用到的功能）：
///   骨骼 rotate / translate / scale 時間軸、加權網格、region、皮膚、欄位附件時間軸（stepped / linear）。
///   不支援：IK、物理、變形(deform)、事件、貝茲曲線、draw order 時間軸。
///
/// 圖層用法（皆可獨立切換，沒給的欄位維持上一個狀態）：
///   outfit  → 皮膚（school / school2 / shirt / sleep / sleep2 / nude ...）
///   hair    → 髮型動畫（twin / long；"none" 或 "off" = 不顯示後髮）
///   socks   → 襪子動畫（white / black / none）
///   face    → 表情動畫（angry1 / laugh / ...）
///   anim    → 身體基礎動作（預設 idle）
///
/// 注意：附件時間軸裡「空白（null）」的關鍵影格會被忽略。
///       也就是動畫不會再把其他欄位（衣服、身體…）藏起來，衣服是否顯示完全由皮膚決定。
///
/// 檔案位置：res://Resources/Spine/linai/linai.json、linai.atlas.txt、linai.png
/// </summary>
public partial class SpineChar : Control
{
    public const string DefaultDir  = "res://Resources/Spine/linai/";
    public const string DefaultName = "linai";

    // ══════════════ 資料結構 ══════════════
    private class Bone
    {
        public string Name; public int Parent = -1;
        public float X, Y, Rot, SX = 1f, SY = 1f, ShX, ShY;     // setup
        public float CX, CY, CRot, CSX, CSY;                    // 目前（套用動畫後）
        public float A, B, C, D, WX, WY;                        // world
    }

    private class Slot { public string Name; public int Bone; public string Setup; }

    private class AtlasRegion
    {
        public float X, Y, W, H;            // bounds（未旋轉的大小）
        public bool Rot;
        public float OffX, OffY, OrigW, OrigH;
    }

    private class Att
    {
        public int VertCount;
        public bool Weighted;
        public float[] Raw;                 // 加權：[n, bone, x, y, w, ...]；未加權：x,y 成對（slot 骨骼座標）
        public Vector2[] UV;                // 已換算成 atlas 貼圖的 0~1 座標
        public int[] Tris;
    }

    private struct Key { public float T, V1, V2; public bool Stepped; }

    private class BoneTL { public Key[] Rotate, Translate, Scale; }

    private class Anim
    {
        public string Name;
        public float Duration;
        public Dictionary<int, BoneTL> Bones = new Dictionary<int, BoneTL>();
        public Dictionary<int, List<KeyValuePair<float, string>>> Slots = new Dictionary<int, List<KeyValuePair<float, string>>>();
    }

    // ══════════════ 狀態 ══════════════
    private bool _loaded, _ok;
    private readonly List<Bone> _bones = new List<Bone>();
    private readonly List<Slot> _slots = new List<Slot>();
    private readonly Dictionary<string, Dictionary<string, Dictionary<string, Att>>> _skins =
        new Dictionary<string, Dictionary<string, Dictionary<string, Att>>>();
    private readonly Dictionary<string, Anim> _anims = new Dictionary<string, Anim>();
    private Texture2D _tex;
    private float _skelX = -280f, _skelY = 0f, _skelW = 560f, _skelH = 1152f;
    private float _time;
    private string[] _cur;                  // 每個 slot 目前的附件名稱

    // 目前選擇
    public string Outfit { get; private set; } = "school";
    public string Hair   { get; private set; } = "";        // "" = 無
    public string Socks  { get; private set; } = "";        // "" = 用 setup（預設白襪）
    public string Face   { get; private set; } = "";        // "" = 用 setup（預設表情）
    public string Body   { get; private set; } = "idle";

    public bool Ready => _ok;

    // 舊表情圖片名稱 → Spine 表情動畫名稱（chFace 兩種寫法都可以用）
    private static readonly Dictionary<string, string> FaceAlias =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        { "biaoqing1",      "face1"     },
        { "biaoqing2",      "face2"     },
        { "shengqi1",       "angry1"    },
        { "shengqi2",       "angry2"    },
        { "cry",            "cry"       },
        { "crylaugh",       "cry_laugh" },
        { "fanbaiyan",      "eye_roll"  },
        { "laugh1",         "laugh"     },
        { "miaoshi",        "scorn"     },
        { "xieyanmiaoshi",  "side_scorn"},
        { "shuijiao",       "sleepy"    },
        { "cixiaoguixiao",  "smirk"     },
        { "xingxingyan",    "star_eyes" },
    };

    /// <summary>動畫播放速度倍率</summary>
    public float Speed = 1f;

    public SpineChar()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        SetProcess(true);
    }

    public override void _Process(double delta)
    {
        if (!_ok || !IsVisibleInTree()) return;
        _time += (float)delta * Speed;
        float loop = LoopDuration();
        if (_time >= loop) _time %= loop;          // 以身體基礎動畫長度為準循環，避免數字越來越大
        QueueRedraw();
    }

    // ══════════════ 外部介面 ══════════════

    /// <summary>第一次使用時載入檔案；失敗回傳 false（並在輸出視窗印出原因）</summary>
    public bool EnsureLoaded()
    {
        if (_loaded) return _ok;
        _loaded = true;
        try
        {
            _ok = Load(DefaultDir, DefaultName);
        }
        catch (Exception e)
        {
            GD.PrintErr("[SpineChar] 載入失敗：" + e);
            _ok = false;
        }
        return _ok;
    }

    /// <summary>
    /// 設定外觀。傳 null 或空字串 = 維持目前設定。
    /// hair 傳 "none" / "off" = 清除後髮。
    /// </summary>
    public void Apply(string outfit, string hair, string socks, string face, string anim)
    {
        if (!EnsureLoaded()) return;

        if (!string.IsNullOrWhiteSpace(outfit))
        {
            string o = outfit.Trim();
            if (_skins.ContainsKey(o)) Outfit = o;
            else GD.PrintErr("[SpineChar] 找不到皮膚 chOutfit=" + o);
        }
        if (!string.IsNullOrWhiteSpace(hair))
        {
            string h = hair.Trim();
            string hl = h.ToLowerInvariant();
            if (hl == "none" || hl == "off" || hl == "-") Hair = "";
            else if (_anims.ContainsKey(h)) Hair = h;
            else GD.PrintErr("[SpineChar] 找不到髮型動畫 chHair=" + h);
        }
        if (!string.IsNullOrWhiteSpace(socks))
        {
            string s = socks.Trim();
            if (_anims.ContainsKey(s)) Socks = s;
            else GD.PrintErr("[SpineChar] 找不到襪子動畫 chSocks=" + s);
        }
        if (!string.IsNullOrWhiteSpace(face))
        {
            string f = face.Trim();
            if (FaceAlias.TryGetValue(f, out string real)) f = real;
            if (_anims.ContainsKey(f)) Face = f;
            else GD.PrintErr("[SpineChar] 找不到表情動畫 chFace=" + f);
        }
        if (!string.IsNullOrWhiteSpace(anim))
        {
            string a = anim.Trim();
            if (_anims.ContainsKey(a)) Body = a;
            else GD.PrintErr("[SpineChar] 找不到身體動畫 chAnim=" + a);
        }
        QueueRedraw();
    }

    // ══════════════ 載入 ══════════════

    private bool Load(string dir, string name)
    {
        string jsonPath  = dir + name + ".json";
        string atlasPath = dir + name + ".atlas.txt";
        string pngPath   = dir + name + ".png";

        if (!FileAccess.FileExists(jsonPath))  { GD.PrintErr("[SpineChar] 找不到 " + jsonPath);  return false; }
        if (!FileAccess.FileExists(atlasPath)) { GD.PrintErr("[SpineChar] 找不到 " + atlasPath); return false; }

        _tex = LoadTexture(pngPath);
        if (_tex == null) { GD.PrintErr("[SpineChar] 讀不到貼圖 " + pngPath); return false; }

        // atlas
        var regions = ParseAtlas(FileAccess.GetFileAsString(atlasPath), out float pageW, out float pageH);
        if (pageW <= 0f) { pageW = _tex.GetWidth(); pageH = _tex.GetHeight(); }

        using var doc = JsonDocument.Parse(FileAccess.GetFileAsString(jsonPath));
        JsonElement root = doc.RootElement;

        if (root.TryGetProperty("skeleton", out var sk))
        {
            _skelX = Fl(sk, "x", 0f); _skelY = Fl(sk, "y", 0f);
            _skelW = Fl(sk, "width", 560f); _skelH = Fl(sk, "height", 1152f);
        }

        // bones
        var boneIdx = new Dictionary<string, int>();
        foreach (var b in root.GetProperty("bones").EnumerateArray())
        {
            var bone = new Bone
            {
                Name = Str(b, "name"),
                X = Fl(b, "x", 0f), Y = Fl(b, "y", 0f), Rot = Fl(b, "rotation", 0f),
                SX = Fl(b, "scaleX", 1f), SY = Fl(b, "scaleY", 1f),
                ShX = Fl(b, "shearX", 0f), ShY = Fl(b, "shearY", 0f),
            };
            string parent = Str(b, "parent");
            bone.Parent = parent != null && boneIdx.TryGetValue(parent, out int pi) ? pi : -1;
            boneIdx[bone.Name] = _bones.Count;
            _bones.Add(bone);
        }

        // slots
        var slotIdx = new Dictionary<string, int>();
        foreach (var s in root.GetProperty("slots").EnumerateArray())
        {
            var slot = new Slot { Name = Str(s, "name"), Bone = boneIdx[Str(s, "bone")], Setup = Str(s, "attachment") };
            slotIdx[slot.Name] = _slots.Count;
            _slots.Add(slot);
        }
        _cur = new string[_slots.Count];

        // skins
        if (root.TryGetProperty("skins", out var skins))
        {
            foreach (var sk2 in skins.EnumerateArray())
            {
                string skinName = Str(sk2, "name");
                var bySlot = new Dictionary<string, Dictionary<string, Att>>();
                if (sk2.TryGetProperty("attachments", out var atts))
                {
                    foreach (var slotProp in atts.EnumerateObject())
                    {
                        var byName = new Dictionary<string, Att>();
                        foreach (var attProp in slotProp.Value.EnumerateObject())
                        {
                            Att a = ParseAtt(attProp.Name, attProp.Value, regions, pageW, pageH, boneIdx);
                            if (a != null) byName[attProp.Name] = a;
                        }
                        bySlot[slotProp.Name] = byName;
                    }
                }
                _skins[skinName] = bySlot;
            }
        }
        if (!_skins.ContainsKey("default")) _skins["default"] = new Dictionary<string, Dictionary<string, Att>>();
        if (!_skins.ContainsKey(Outfit))
        {
            foreach (var k in _skins.Keys) { if (k != "default") { Outfit = k; break; } }
        }

        // animations
        if (root.TryGetProperty("animations", out var anims))
        {
            foreach (var ap in anims.EnumerateObject())
                _anims[ap.Name] = ParseAnim(ap.Name, ap.Value, boneIdx, slotIdx);
        }
        if (!_anims.ContainsKey(Body))
        {
            foreach (var k in _anims.Keys) { Body = k; break; }
        }

        GD.Print("[SpineChar] 載入完成：骨骼 " + _bones.Count + "、欄位 " + _slots.Count +
                 "、皮膚 " + _skins.Count + "、動畫 " + _anims.Count);
        return true;
    }

    private static Texture2D LoadTexture(string path)
    {
        if (ResourceLoader.Exists(path))
        {
            var t = GD.Load<Texture2D>(path);
            if (t != null) return t;
        }
        // 還沒被編輯器匯入時的備援（僅限在編輯器／專案資料夾執行時有效）
        string abs = ProjectSettings.GlobalizePath(path);
        if (System.IO.File.Exists(abs))
        {
            var img = Image.LoadFromFile(abs);
            if (img != null) return ImageTexture.CreateFromImage(img);
        }
        return null;
    }

    private static Dictionary<string, AtlasRegion> ParseAtlas(string text, out float pageW, out float pageH)
    {
        pageW = 0f; pageH = 0f;
        var regions = new Dictionary<string, AtlasRegion>();
        bool pageNamed = false;
        AtlasRegion cur = null;
        foreach (string raw in text.Replace("\r", "").Split('\n'))
        {
            string l = raw.Trim();
            if (l.Length == 0) continue;
            int colon = l.IndexOf(':');
            if (colon < 0)
            {
                if (!pageNamed) { pageNamed = true; }
                else
                {
                    cur = new AtlasRegion();
                    regions[l] = cur;
                }
                continue;
            }
            string key = l.Substring(0, colon).Trim();
            string val = l.Substring(colon + 1).Trim();
            float[] n = Nums(val);
            if (cur == null)
            {
                if (key == "size" && n.Length >= 2) { pageW = n[0]; pageH = n[1]; }
                continue;
            }
            if (key == "bounds" && n.Length >= 4) { cur.X = n[0]; cur.Y = n[1]; cur.W = n[2]; cur.H = n[3]; }
            else if (key == "offsets" && n.Length >= 4) { cur.OffX = n[0]; cur.OffY = n[1]; cur.OrigW = n[2]; cur.OrigH = n[3]; }
            else if (key == "rotate") cur.Rot = (val == "true" || val == "90");
            else if (key == "xy" && n.Length >= 2) { cur.X = n[0]; cur.Y = n[1]; }       // 舊格式
            else if (key == "size" && n.Length >= 2) { cur.W = n[0]; cur.H = n[1]; }     // 舊格式
            else if (key == "orig" && n.Length >= 2) { cur.OrigW = n[0]; cur.OrigH = n[1]; }
            else if (key == "offset" && n.Length >= 2) { cur.OffX = n[0]; cur.OffY = n[1]; }
        }
        foreach (var r in regions.Values)
        {
            if (r.OrigW <= 0f) r.OrigW = r.W;
            if (r.OrigH <= 0f) r.OrigH = r.H;
        }
        return regions;
    }

    private static float[] Nums(string v)
    {
        var parts = v.Split(',');
        var res = new float[parts.Length];
        for (int i = 0; i < parts.Length; i++)
        {
            if (!float.TryParse(parts[i].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out res[i])) res[i] = 0f;
        }
        return res;
    }

    // 原圖座標 (0~1) → atlas 貼圖座標 (0~1)
    private static Vector2 MapUV(AtlasRegion r, float u, float v, float pw, float ph)
    {
        float px = u * r.OrigW - r.OffX;
        float py = v * r.OrigH - (r.OrigH - r.OffY - r.H);
        float ax, ay;
        if (!r.Rot) { ax = r.X + px; ay = r.Y + py; }
        else        { ax = r.X + py; ay = r.Y + (r.W - px); }
        return new Vector2(ax / pw, ay / ph);
    }

    private Att ParseAtt(string name, JsonElement e, Dictionary<string, AtlasRegion> regions,
                         float pw, float ph, Dictionary<string, int> boneIdx)
    {
        string type = Str(e, "type") ?? "region";
        string path = Str(e, "path") ?? name;
        if (!regions.TryGetValue(path, out var reg))
        {
            GD.PrintErr("[SpineChar] atlas 找不到圖片 " + path + "（附件 " + name + "）");
            return null;
        }

        if (type == "region")
        {
            float w = Fl(e, "width", reg.OrigW), h = Fl(e, "height", reg.OrigH);
            float x = Fl(e, "x", 0f), y = Fl(e, "y", 0f);
            float rot = Fl(e, "rotation", 0f) * Mathf.Pi / 180f;
            float sx = Fl(e, "scaleX", 1f), sy = Fl(e, "scaleY", 1f);
            float cos = Mathf.Cos(rot), sin = Mathf.Sin(rot);
            // 圖集會把圖片周圍的透明邊裁掉：四邊形只能蓋住「裁剪後」的那一塊，
            // 否則會把圖集裡旁邊的其他圖也畫出來（Spine 官方執行環境也是這樣處理）
            float u0 = reg.OffX / reg.OrigW, u1 = (reg.OffX + reg.W) / reg.OrigW;
            float v0 = (reg.OrigH - reg.OffY - reg.H) / reg.OrigH, v1 = (reg.OrigH - reg.OffY) / reg.OrigH;
            float[] cx = { -w * 0.5f + u0 * w, -w * 0.5f + u0 * w, -w * 0.5f + u1 * w, -w * 0.5f + u1 * w };
            float[] cy = {  h * 0.5f - v1 * h,  h * 0.5f - v0 * h,  h * 0.5f - v0 * h,  h * 0.5f - v1 * h };
            float[] uu = { u0, u0, u1, u1 };
            float[] vv = { v1, v0, v0, v1 };
            var att = new Att { VertCount = 4, Weighted = false, Raw = new float[8], UV = new Vector2[4], Tris = new[] { 0, 1, 2, 0, 2, 3 } };
            for (int i = 0; i < 4; i++)
            {
                float px = cx[i] * sx, py = cy[i] * sy;
                att.Raw[i * 2]     = px * cos - py * sin + x;
                att.Raw[i * 2 + 1] = px * sin + py * cos + y;
                att.UV[i] = MapUV(reg, uu[i], vv[i], pw, ph);
            }
            return att;
        }

        if (type == "mesh")
        {
            var uvs = Floats(e.GetProperty("uvs"));
            var verts = Floats(e.GetProperty("vertices"));
            var tris = Ints(e.GetProperty("triangles"));
            int n = uvs.Length / 2;
            var att = new Att
            {
                VertCount = n,
                Weighted = verts.Length != uvs.Length,
                Raw = verts,
                UV = new Vector2[n],
                Tris = tris,
            };
            for (int i = 0; i < n; i++) att.UV[i] = MapUV(reg, uvs[i * 2], uvs[i * 2 + 1], pw, ph);
            return att;
        }

        return null;   // 其他類型（clipping、boundingbox…）不畫
    }

    private Anim ParseAnim(string name, JsonElement e, Dictionary<string, int> boneIdx, Dictionary<string, int> slotIdx)
    {
        var anim = new Anim { Name = name };
        float dur = 0f;

        if (e.TryGetProperty("bones", out var bones))
        {
            foreach (var bp in bones.EnumerateObject())
            {
                if (!boneIdx.TryGetValue(bp.Name, out int bi)) continue;
                var tl = new BoneTL();
                if (bp.Value.TryGetProperty("rotate", out var rk))    tl.Rotate    = ParseKeys(rk, "value", null, 0f, 0f, ref dur);
                if (bp.Value.TryGetProperty("translate", out var tk)) tl.Translate = ParseKeys(tk, "x", "y", 0f, 0f, ref dur);
                if (bp.Value.TryGetProperty("scale", out var sk))     tl.Scale     = ParseKeys(sk, "x", "y", 1f, 1f, ref dur);
                anim.Bones[bi] = tl;
            }
        }

        if (e.TryGetProperty("slots", out var slots))
        {
            foreach (var sp in slots.EnumerateObject())
            {
                if (!slotIdx.TryGetValue(sp.Name, out int si)) continue;
                if (!sp.Value.TryGetProperty("attachment", out var ak)) continue;
                var list = new List<KeyValuePair<float, string>>();
                foreach (var k in ak.EnumerateArray())
                {
                    string nm = Str(k, "name");
                    if (nm == null) continue;           // 空白（null）影格忽略
                    float t = Fl(k, "time", 0f);
                    list.Add(new KeyValuePair<float, string>(t, nm));
                    if (t > dur) dur = t;
                }
                if (list.Count > 0) anim.Slots[si] = list;
            }
        }

        anim.Duration = dur > 0.0001f ? dur : 1f;
        return anim;
    }

    private static Key[] ParseKeys(JsonElement arr, string n1, string n2, float d1, float d2, ref float dur)
    {
        var list = new List<Key>();
        foreach (var k in arr.EnumerateArray())
        {
            var key = new Key
            {
                T = Fl(k, "time", 0f),
                V1 = Fl(k, n1, d1),
                V2 = n2 != null ? Fl(k, n2, d2) : 0f,
            };
            if (k.TryGetProperty("curve", out var c) && c.ValueKind == JsonValueKind.String && c.GetString() == "stepped")
                key.Stepped = true;
            list.Add(key);
            if (key.T > dur) dur = key.T;
        }
        return list.ToArray();
    }

    // ══════════════ JSON 小工具 ══════════════
    private static float Fl(JsonElement e, string n, float def)
        => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetSingle() : def;

    private static string Str(JsonElement e, string n)
        => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static float[] Floats(JsonElement arr)
    {
        var res = new float[arr.GetArrayLength()];
        int i = 0;
        foreach (var v in arr.EnumerateArray()) res[i++] = v.GetSingle();
        return res;
    }

    private static int[] Ints(JsonElement arr)
    {
        var res = new int[arr.GetArrayLength()];
        int i = 0;
        foreach (var v in arr.EnumerateArray()) res[i++] = v.GetInt32();
        return res;
    }

    // ══════════════ 姿勢計算 ══════════════

    private static void Sample(Key[] keys, float t, out float v1, out float v2)
    {
        v1 = 0f; v2 = 0f;
        if (keys == null || keys.Length == 0) return;
        if (t <= keys[0].T) { v1 = keys[0].V1; v2 = keys[0].V2; return; }
        int last = keys.Length - 1;
        if (t >= keys[last].T) { v1 = keys[last].V1; v2 = keys[last].V2; return; }
        int i = 0;
        while (i < last - 1 && keys[i + 1].T <= t) i++;
        Key a = keys[i], b = keys[i + 1];
        if (a.Stepped) { v1 = a.V1; v2 = a.V2; return; }
        float p = (t - a.T) / Mathf.Max(0.00001f, b.T - a.T);
        v1 = a.V1 + (b.V1 - a.V1) * p;
        v2 = a.V2 + (b.V2 - a.V2) * p;
    }

    private void ApplyAnim(Anim anim, float globalTime)
    {
        // globalTime 已經是「共用循環時間」：所有軌道吃同一個時間，換部件也不會從 0 幀重來
        float t = globalTime;

        foreach (var kv in anim.Bones)
        {
            Bone b = _bones[kv.Key];
            BoneTL tl = kv.Value;
            float a, c;
            if (tl.Rotate != null && tl.Rotate.Length > 0)
            {
                Sample(tl.Rotate, t, out a, out c);
                b.CRot = b.Rot + a;
            }
            if (tl.Translate != null && tl.Translate.Length > 0)
            {
                Sample(tl.Translate, t, out a, out c);
                b.CX = b.X + a; b.CY = b.Y + c;
            }
            if (tl.Scale != null && tl.Scale.Length > 0)
            {
                Sample(tl.Scale, t, out a, out c);
                b.CSX = b.SX * a; b.CSY = b.SY * c;
            }
        }

        foreach (var kv in anim.Slots)
        {
            string chosen = null;
            foreach (var key in kv.Value)
            {
                if (key.Key <= t + 0.00001f) chosen = key.Value;
                else break;
            }
            if (chosen != null) _cur[kv.Key] = chosen;
        }
    }

    private void ComputePose()
    {
        foreach (var b in _bones) { b.CX = b.X; b.CY = b.Y; b.CRot = b.Rot; b.CSX = b.SX; b.CSY = b.SY; }
        for (int i = 0; i < _slots.Count; i++) _cur[i] = _slots[i].Setup;

        // 套用順序：身體基礎 → 髮型 → 襪子 → 表情
        ApplyNamed(Body);
        ApplyNamed(Hair);
        ApplyNamed(Socks);
        ApplyNamed(Face);

        const float deg = Mathf.Pi / 180f;
        foreach (var b in _bones)
        {
            float rx = (b.CRot + b.ShX) * deg;
            float ry = (b.CRot + 90f + b.ShY) * deg;
            float la = Mathf.Cos(rx) * b.CSX, lb = Mathf.Cos(ry) * b.CSY;
            float lc = Mathf.Sin(rx) * b.CSX, ld = Mathf.Sin(ry) * b.CSY;
            if (b.Parent < 0)
            {
                b.A = la; b.B = lb; b.C = lc; b.D = ld; b.WX = b.CX; b.WY = b.CY;
            }
            else
            {
                Bone p = _bones[b.Parent];
                b.WX = p.A * b.CX + p.B * b.CY + p.WX;
                b.WY = p.C * b.CX + p.D * b.CY + p.WY;
                b.A = p.A * la + p.B * lc; b.B = p.A * lb + p.B * ld;
                b.C = p.C * la + p.D * lc; b.D = p.C * lb + p.D * ld;
            }
        }
    }

    /// <summary>共用循環長度＝身體基礎動畫的長度（所有部件動畫都用同一個時間軸）</summary>
    private float LoopDuration()
        => _anims.TryGetValue(Body, out var a) && a.Duration > 0.0001f ? a.Duration : 1f;

    private void ApplyNamed(string animName)
    {
        if (string.IsNullOrEmpty(animName)) return;
        if (_anims.TryGetValue(animName, out var a)) ApplyAnim(a, _time % LoopDuration());
    }

    private Att FindAtt(string slotName, string attName)
    {
        if (attName == null) return null;
        if (_skins.TryGetValue(Outfit, out var s1) && s1.TryGetValue(slotName, out var m1) && m1.TryGetValue(attName, out var a1))
            return a1;
        if (_skins.TryGetValue("default", out var s2) && s2.TryGetValue(slotName, out var m2) && m2.TryGetValue(attName, out var a2))
            return a2;
        return null;
    }

    // ══════════════ 繪製 ══════════════

    private static readonly Dictionary<int, Color[]> _whiteCache = new Dictionary<int, Color[]>();

    private static Color[] White(int n)
    {
        if (!_whiteCache.TryGetValue(n, out var arr))
        {
            arr = new Color[n];
            for (int i = 0; i < n; i++) arr[i] = Colors.White;
            _whiteCache[n] = arr;
        }
        return arr;
    }

    public override void _Draw()
    {
        if (!_ok || _tex == null) return;
        ComputePose();

        // 骨架範圍 → 控制項範圍（底部置中、等比縮放）
        float scale = Mathf.Min(Size.X / _skelW, Size.Y / _skelH);
        if (scale <= 0f) return;
        float ox = Size.X * 0.5f - (_skelX + _skelW * 0.5f) * scale;
        float oy = Size.Y + _skelY * scale;

        Rid item = GetCanvasItem();
        Rid texRid = _tex.GetRid();

        for (int si = 0; si < _slots.Count; si++)
        {
            Slot slot = _slots[si];
            Att att = FindAtt(slot.Name, _cur[si]);
            if (att == null) continue;

            var pts = new Vector2[att.VertCount];
            if (att.Weighted)
            {
                int p = 0;
                for (int v = 0; v < att.VertCount; v++)
                {
                    int bc = (int)att.Raw[p++];
                    float wx = 0f, wy = 0f;
                    for (int k = 0; k < bc; k++)
                    {
                        Bone b = _bones[(int)att.Raw[p++]];
                        float vx = att.Raw[p++], vy = att.Raw[p++], w = att.Raw[p++];
                        wx += (vx * b.A + vy * b.B + b.WX) * w;
                        wy += (vx * b.C + vy * b.D + b.WY) * w;
                    }
                    pts[v] = new Vector2(ox + wx * scale, oy - wy * scale);
                }
            }
            else
            {
                Bone b = _bones[slot.Bone];
                for (int v = 0; v < att.VertCount; v++)
                {
                    float vx = att.Raw[v * 2], vy = att.Raw[v * 2 + 1];
                    float wx = vx * b.A + vy * b.B + b.WX;
                    float wy = vx * b.C + vy * b.D + b.WY;
                    pts[v] = new Vector2(ox + wx * scale, oy - wy * scale);
                }
            }

            RenderingServer.CanvasItemAddTriangleArray(item, att.Tris, pts, White(att.VertCount), att.UV, null, null, texRid, -1);
        }
    }
}
