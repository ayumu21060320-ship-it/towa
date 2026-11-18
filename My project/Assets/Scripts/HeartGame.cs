// モノクロハート — 画面と操作。シーンを再生すると自動で起動する（シーンに置く必要なし）
// 絵は Assets/Resources/Images/ に png を置くと差し替わる（名前は docs/決めたこと.md の「絵の差し替え」）。
// 置いていない絵は、コードで描いた白黒の仮の絵になる。
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using MonochroHeart;

public class HeartGame : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (FindAnyObjectByType<HeartGame>() == null) new GameObject("HeartGame").AddComponent<HeartGame>();
    }

    // ---- 色 ----
    static readonly Color White = new Color(0.93f, 0.93f, 0.93f);
    static readonly Color Black = new Color(0.07f, 0.07f, 0.07f);
    static readonly Color Gray = new Color(0.5f, 0.5f, 0.5f);
    static readonly Color DarkGray = new Color(0.18f, 0.18f, 0.18f);
    static readonly Color Panel = new Color(0.1f, 0.1f, 0.1f, 0.92f);
    static readonly Color Pink = new Color(1f, 0.55f, 0.7f); // 一致したときだけ使う差し色

    // ---- 部品 ----
    Font font;
    Transform canvas;
    GameObject screen; // 今の画面。画面を切り替えるときに丸ごと消す
    Sprite sqSprite, circleSprite, heartSprite, lifeSprite, dotSprite, flipSprite, blobSprite, megaSprite, starSprite, frameSprite, checkerSprite;
    readonly Dictionary<string, Sprite> images = new Dictionary<string, Sprite>();
    Dictionary<string, List<StoryLine>> story;
    AudioSource sfx, bgm;
    AudioClip seFlip, seMatch, seHit, seFlipHit, seSelect, seLaunch, seBump, seClear, seFail;

    // ---- メニュー（キーボードとマウスの両方で選べるボタン）----
    readonly List<(Button b, Image bg, Text label)> menu = new List<(Button, Image, Text)>();
    int menuSel;
    Vector2 lastMouse;

    // ---- 画面ごとの状態 ----
    enum Mode { Title, Select, Novel, Puzzle, Result }
    Mode mode;
    Action update; // 今の画面の毎フレームの処理

    void Start()
    {
        Application.targetFrameRate = 60;
        var uiFonts = Resources.LoadAll<Font>("UIFont");
        font = uiFonts.Length > 0 ? uiFonts[0]
             : Font.CreateDynamicFontFromOSFont(new[] { "Yu Gothic UI", "Yu Gothic", "Meiryo", "MS Gothic" }, 64);

        if (Camera.main == null)
        {
            var cam = new GameObject("Main Camera").AddComponent<Camera>();
            cam.tag = "MainCamera";
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            cam.orthographic = true;
        }
        if (FindAnyObjectByType<EventSystem>() == null)
        {
            var es = new GameObject("EventSystem");
            es.AddComponent<EventSystem>();
            es.AddComponent<InputSystemUIInputModule>();
        }

        var cgo = new GameObject("Canvas");
        var cv = cgo.AddComponent<Canvas>();
        cv.renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = cgo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        cgo.AddComponent<GraphicRaycaster>();
        canvas = cgo.transform;

        MakeSprites();
        MakeSounds();
        var st = Resources.Load<TextAsset>("Story");
        story = Story.Parse(st != null ? st.text : "");
        if (st == null) Debug.LogWarning("Assets/Resources/Story.txt が見つからないので、ノベルパートは飛ばします");

        sfx = gameObject.AddComponent<AudioSource>();
        bgm = gameObject.AddComponent<AudioSource>();
        var bgms = Resources.LoadAll<AudioClip>("BGM");
        if (bgms.Length > 0) { bgm.clip = bgms[0]; bgm.loop = true; bgm.volume = 0.35f; bgm.Play(); }

        ShowTitle();
    }

    void Update()
    {
        UpdateMenu();
        // 画面を切り替えたフレームでは、新しい画面に同じキー入力を渡さない
        if (Time.frameCount != screenFrame) update?.Invoke();
    }

    // 画面が出てすぐの決定キーは無視する（前の画面で連打していたキーで勝手に進まないように）
    int screenFrame;
    float screenTime;
    bool Settled => Time.unscaledTime - screenTime > 0.4f;

    // =====================================================================
    // 入力
    // =====================================================================
    static Keyboard Kb => Keyboard.current;
    static bool Down(Key k) => Kb != null && Kb[k].wasPressedThisFrame;
    static bool Held(Key k) => Kb != null && Kb[k].isPressed;
    static bool ConfirmDown() => Down(Key.Enter) || Down(Key.NumpadEnter) || Down(Key.Space) || Down(Key.S) || Down(Key.Z);
    static bool BackDown() => Down(Key.Escape) || Down(Key.X) || Down(Key.Backspace);
    static bool ClickDown() => Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;

    // 矢印キー（押しっぱなしで連続して動く）
    readonly float[] repeatTimer = new float[4];
    static readonly Key[] ArrowKeys = { Key.UpArrow, Key.DownArrow, Key.LeftArrow, Key.RightArrow };
    static readonly Vector2Int[] ArrowDirs = { new Vector2Int(-1, 0), new Vector2Int(1, 0), new Vector2Int(0, -1), new Vector2Int(0, 1) };

    IEnumerable<Vector2Int> ArrowPresses(float dt)
    {
        for (int i = 0; i < 4; i++)
        {
            if (Down(ArrowKeys[i])) { repeatTimer[i] = 0.2f; yield return ArrowDirs[i]; }
            else if (Held(ArrowKeys[i]))
            {
                repeatTimer[i] -= dt;
                if (repeatTimer[i] <= 0) { repeatTimer[i] = 0.08f; yield return ArrowDirs[i]; }
            }
        }
    }

    // =====================================================================
    // メニュー
    // =====================================================================
    void UpdateMenu()
    {
        if (menu.Count == 0) return;
        menu.RemoveAll(m => m.b == null);
        if (menu.Count == 0) return;
        menuSel = Mathf.Clamp(menuSel, 0, menu.Count - 1);

        // マウスが乗ったボタンを選ぶ
        if (Mouse.current != null)
        {
            var mp = Mouse.current.position.ReadValue();
            if ((mp - lastMouse).sqrMagnitude > 1)
                for (int i = 0; i < menu.Count; i++)
                    if (menu[i].b.interactable && RectTransformUtility.RectangleContainsScreenPoint((RectTransform)menu[i].b.transform, mp, null))
                    { if (menuSel != i) { menuSel = i; } break; }
            lastMouse = mp;
        }

        if (mode != Mode.Puzzle || paused)
        {
            Vector2 dir = Vector2.zero;
            if (Down(Key.UpArrow)) dir = Vector2.up;
            else if (Down(Key.DownArrow)) dir = Vector2.down;
            else if (Down(Key.LeftArrow)) dir = Vector2.left;
            else if (Down(Key.RightArrow)) dir = Vector2.right;
            if (dir != Vector2.zero) MoveMenu(dir);
            if (ConfirmDown() && Settled && menu[menuSel].b.interactable) { menu[menuSel].b.onClick.Invoke(); return; }
        }

        for (int i = 0; i < menu.Count; i++)
        {
            bool sel = i == menuSel;
            var m = menu[i];
            if (m.bg == null) continue;
            m.bg.color = !m.b.interactable ? new Color(0.12f, 0.12f, 0.12f, 0.9f) : sel ? White : new Color(0.16f, 0.16f, 0.16f, 0.95f);
            m.label.color = !m.b.interactable ? new Color(0.4f, 0.4f, 0.4f) : sel ? Black : White;
        }
    }

    // 矢印の向きにいちばん近いボタンへ移る
    void MoveMenu(Vector2 dir)
    {
        var from = ((RectTransform)menu[menuSel].b.transform).position;
        int best = -1; float bestScore = float.MaxValue;
        for (int i = 0; i < menu.Count; i++)
        {
            if (i == menuSel || !menu[i].b.interactable) continue;
            Vector2 d = ((RectTransform)menu[i].b.transform).position - from;
            float along = Vector2.Dot(d, dir);
            if (along <= 1) continue;
            float side = Mathf.Abs(d.x * dir.y - d.y * dir.x);
            float score = along + side * 2.5f;
            if (score < bestScore) { bestScore = score; best = i; }
        }
        if (best >= 0) { menuSel = best; Play(seSelect, 0.5f); }
    }

    // =====================================================================
    // 画面の部品を作る
    // =====================================================================
    void NewScreen(Mode m)
    {
        if (screen != null) Destroy(screen);
        menu.Clear();
        menuSel = 0;
        update = null;
        mode = m;
        paused = false;
        screenFrame = Time.frameCount;
        screenTime = Time.unscaledTime;
        screen = new GameObject(m.ToString(), typeof(RectTransform));
        var rt = (RectTransform)screen.transform;
        rt.SetParent(canvas, false);
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }

    RectTransform Rect(Transform parent, string name, Vector2 pos, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        return rt;
    }

    Image Img(Transform parent, Vector2 pos, Vector2 size, Color color, Sprite sprite = null, string name = "Image")
    {
        var img = Rect(parent, name, pos, size).gameObject.AddComponent<Image>();
        img.sprite = sprite != null ? sprite : sqSprite;
        img.color = color;
        img.raycastTarget = false;
        return img;
    }

    Text Txt(Transform parent, Vector2 pos, Vector2 size, string s, int fontSize, Color color, TextAnchor anchor = TextAnchor.MiddleCenter)
    {
        var t = Rect(parent, "Text", pos, size).gameObject.AddComponent<Text>();
        t.font = font;
        t.text = s;
        t.fontSize = fontSize;
        t.color = color;
        t.alignment = anchor;
        t.horizontalOverflow = HorizontalWrapMode.Wrap;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.lineSpacing = 1.15f;
        t.raycastTarget = false;
        return t;
    }

    void Outlined(Graphic g, Color c, float d = 2)
    {
        var o = g.gameObject.AddComponent<Outline>();
        o.effectColor = c;
        o.effectDistance = new Vector2(d, -d);
    }

    Button Btn(Transform parent, Vector2 pos, Vector2 size, string label, Action onClick, int fontSize = 36, bool interactable = true)
    {
        var bg = Img(parent, pos, size, DarkGray, null, "Button");
        bg.raycastTarget = true;
        Outlined(bg, Gray, 2);
        var b = bg.gameObject.AddComponent<Button>();
        b.transition = Selectable.Transition.None;
        b.interactable = interactable;
        b.onClick.AddListener(() => { Play(seSelect, 0.6f); onClick(); });
        var t = Txt(bg.transform, Vector2.zero, size - new Vector2(16, 0), label, fontSize, White);
        menu.Add((b, bg, t));
        return b;
    }

    void Background(Transform parent, string imageKey = null)
    {
        var bg = Img(parent, Vector2.zero, new Vector2(1920, 1080), Color.white, checkerSprite, "Background");
        var sp = imageKey != null ? LoadImage(imageKey) : null;
        if (sp != null) { bg.sprite = sp; bg.preserveAspect = false; }
        var rt = bg.rectTransform;
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero;
    }

    Sprite LoadImage(string key)
    {
        if (images.TryGetValue(key, out var s)) return s;
        var tex = Resources.Load<Texture2D>("Images/" + key);
        s = tex != null ? Sprite.Create(tex, new UnityEngine.Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100) : null;
        images[key] = s;
        return s;
    }

    // =====================================================================
    // タイトル
    // =====================================================================
    bool confirmErase;

    void ShowTitle()
    {
        NewScreen(Mode.Title);
        var root = screen.transform;
        Background(root, "title");
        Img(root, new Vector2(0, 200), new Vector2(240, 240), Color.white, heartSprite);
        Txt(root, new Vector2(-400, 200), new Vector2(500, 200), "モノクロ", 120, White, TextAnchor.MiddleRight).fontStyle = FontStyle.Bold;
        Txt(root, new Vector2(400, 200), new Vector2(500, 200), "ハート", 120, White, TextAnchor.MiddleLeft).fontStyle = FontStyle.Bold;
        Txt(root, new Vector2(0, 40), new Vector2(1200, 60), "— MONOCHRO HEART —", 40, Gray);

        Btn(root, new Vector2(0, -130), new Vector2(460, 80), "はじめる", ShowSelect);
        confirmErase = false;
        Button erase = null;
        erase = Btn(root, new Vector2(0, -230), new Vector2(460, 80), "データを消す", () =>
        {
            var label = erase.GetComponentInChildren<Text>();
            if (!confirmErase) { confirmErase = true; label.text = "本当に消す？（もう一度押す）"; return; }
            for (int i = 1; i <= Stages.Count; i++) PlayerPrefs.DeleteKey(BestKey(i));
            PlayerPrefs.DeleteKey("mh_ending");
            PlayerPrefs.Save();
            confirmErase = false;
            label.text = "消しました";
        }, 30);
        Btn(root, new Vector2(0, -330), new Vector2(460, 80), "おわる", () =>
        {
            Application.Quit();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#endif
        });
        Txt(root, new Vector2(0, -470), new Vector2(1600, 50), "矢印キーで選んで Sキー／Enter で決定（マウスでも選べます）", 26, Gray);
    }

    // =====================================================================
    // セーブ
    // =====================================================================
    static string BestKey(int n) => "mh_best_" + n;
    static int Best(int n) => PlayerPrefs.GetInt(BestKey(n), 0);
    static bool Unlocked(int n) => n == 1 || Best(n - 1) > 0;
    static int TotalStars()
    {
        int s = 0;
        for (int i = 1; i <= Stages.Count; i++) s += Best(i);
        return s;
    }

    static string StarText(int n) => new string('★', n) + new string('☆', 3 - n);

    // =====================================================================
    // ステージ選択（悩みの種）
    // =====================================================================
    int lastStage = 1;

    void ShowSelect()
    {
        NewScreen(Mode.Select);
        var root = screen.transform;
        Background(root);
        Txt(root, new Vector2(0, 450), new Vector2(1200, 90), "悩みの種", 64, White).fontStyle = FontStyle.Bold;
        Txt(root, new Vector2(0, 375), new Vector2(1200, 50), $"心の余裕　★ {TotalStars()} / {Stages.Count * 3}", 34, Gray);

        int select = 0;
        for (int ch = 0; ch < 4; ch++)
        {
            float y = 230 - ch * 165;
            Txt(root, new Vector2(-720, y), new Vector2(340, 120), $"{ch + 1}章\n{Stages.ChapterNames[ch]}", 38, White);
            for (int k = 0; k < 3; k++)
            {
                int n = ch * 3 + k + 1;
                var def = Stages.All[n - 1];
                bool open = Unlocked(n);
                string label = open ? $"{n}. {def.Title}\n{StarText(Best(n))}" : $"{n}. ？？？";
                int stage = n;
                Btn(root, new Vector2(-300 + k * 420, y), new Vector2(390, 130), label, () => StartStage(stage), 32, open);
                if (n == lastStage && open) select = menu.Count - 1;
            }
        }
        Btn(root, new Vector2(-260, -470), new Vector2(420, 70), "タイトルへ", ShowTitle, 30);
        if (PlayerPrefs.GetInt("mh_ending", 0) == 1)
            Btn(root, new Vector2(260, -470), new Vector2(420, 70), "エンディングを見る", ShowEnding, 30);
        menuSel = select;
        update = () => { if (BackDown()) ShowTitle(); };
    }

    void StartStage(int n)
    {
        lastStage = n;
        PlayNovel(n + "-before", () => StartPuzzle(n));
    }

    // =====================================================================
    // ノベルパート
    // =====================================================================
    void PlayNovel(string key, Action onEnd)
    {
        if (!story.TryGetValue(key, out var lines) || lines.Count == 0) { onEnd(); return; }

        NewScreen(Mode.Novel);
        var root = screen.transform;
        var bg = Img(root, Vector2.zero, new Vector2(1920, 1080), new Color(0.2f, 0.2f, 0.2f), null, "NovelBg");
        bg.rectTransform.anchorMin = Vector2.zero; bg.rectTransform.anchorMax = Vector2.one;
        bg.rectTransform.offsetMin = bg.rectTransform.offsetMax = Vector2.zero;
        var place = Txt(root, new Vector2(-660, 470), new Vector2(560, 60), "", 32, White, TextAnchor.MiddleLeft);

        // 立ち絵（話した人だけ出す）
        bool swapped = false; // 決意エンド: シロとクロが本来の姿（色）に戻る
        var portraits = new Dictionary<string, (Image img, Text label)>();
        Func<string, (Image img, Text label)> Portrait = who =>
        {
            if (portraits.TryGetValue(who, out var p)) return p;
            string imgKey = who == "シロ" ? "shiro" : who == "クロ" ? "kuro" : who == "人間さん" ? "ningen" : null;
            if (imgKey == null) return (null, null);
            float x = imgKey == "shiro" ? -560 : imgKey == "kuro" ? 560 : 0;
            var sp = LoadImage(swapped ? imgKey + "_true" : imgKey) ?? LoadImage(imgKey);
            var img = Img(root, new Vector2(x, 60), sp != null ? new Vector2(520, 620) : new Vector2(380, 380), Color.white, sp ?? circleSprite, "Portrait");
            img.preserveAspect = true;
            Text label = null;
            if (sp == null)
            {
                bool dark = (imgKey == "shiro") != swapped || imgKey == "ningen";
                img.color = imgKey == "ningen" ? Gray : dark ? Black : White;
                Outlined(img, dark ? White : Black, 3);
                label = Txt(img.transform, Vector2.zero, new Vector2(360, 120), who, 56, dark ? White : Black);
            }
            img.transform.SetSiblingIndex(2);
            p = (img, label);
            portraits[who] = p;
            return p;
        };

        var box = Img(root, new Vector2(0, -380), new Vector2(1640, 290), Panel, null, "TextBox");
        Outlined(box, Gray, 3);
        var nameBg = Img(root, new Vector2(-640, -205), new Vector2(340, 64), White, null, "NameBox");
        var nameText = Txt(nameBg.transform, Vector2.zero, new Vector2(320, 64), "", 34, Black);
        var body = Txt(box.transform, new Vector2(0, -10), new Vector2(1540, 240), "", 38, White, TextAnchor.UpperLeft);
        var card = Txt(root, new Vector2(0, 40), new Vector2(1800, 300), "", 96, White);
        card.fontStyle = FontStyle.Bold;
        Txt(root, new Vector2(560, -500), new Vector2(760, 40), "Sキー／Enter／クリック: 次へ　　Esc: スキップ", 24, Gray, TextAnchor.MiddleRight);

        int index = -1;
        float shown = 0;
        string full = "";
        bool isCard = false;

        Action Next = null;
        Next = () =>
        {
            while (true)
            {
                index++;
                if (index >= lines.Count) { onEnd(); return; }
                var l = lines[index];
                if (l.Command == null) break;
                if (l.Command == "背景")
                {
                    place.text = l.Text.Length > 0 ? "― " + l.Text + " ―" : "";
                    var sp = LoadImage("bg_" + l.Text);
                    bg.sprite = sp != null ? sp : sqSprite;
                    bg.color = sp != null ? Color.white : l.Text.Contains("心") ? new Color(0.06f, 0.06f, 0.06f) : new Color(0.22f, 0.22f, 0.22f);
                }
                else if (l.Command == "本来の姿")
                {
                    swapped = true;
                    foreach (var p in portraits.Values) Destroy(p.img.gameObject);
                    portraits.Clear();
                }
                else if (l.Command == "タイトル")
                {
                    isCard = true;
                    full = l.Text.Replace("\\n", "\n");
                    shown = full.Length;
                    card.text = full;
                    body.text = ""; nameBg.gameObject.SetActive(false); box.gameObject.SetActive(false);
                    return;
                }
            }
            isCard = false;
            card.text = "";
            box.gameObject.SetActive(true);
            var line = lines[index];
            full = line.Text.Replace("\\n", "\n");
            shown = 0;
            nameBg.gameObject.SetActive(line.Speaker.Length > 0);
            nameText.text = line.Speaker;
            if (line.Speaker.Length > 0) Portrait(line.Speaker);
            foreach (var kv in portraits)
            {
                var c = kv.Value.img.color;
                c.a = kv.Key == line.Speaker ? 1f : 0.45f;
                kv.Value.img.color = c;
            }
        };

        Next();
        update = () =>
        {
            if (mode != Mode.Novel) return;
            if (Down(Key.Escape)) { onEnd(); return; }
            bool fast = Held(Key.LeftCtrl) || Held(Key.RightCtrl);
            if (!isCard)
            {
                shown += Time.deltaTime * (fast ? 400 : 45);
                body.text = full.Substring(0, Mathf.Min(full.Length, (int)shown));
            }
            if (!Settled) return;
            if (ConfirmDown() || ClickDown() || (fast && Time.frameCount % 4 == 0))
            {
                if (!isCard && shown < full.Length) shown = full.Length;
                else Next();
            }
        };
    }

    // =====================================================================
    // パズルパート
    // =====================================================================
    Puzzle pz;
    int pzStage;
    bool paused;
    float countdown, overTimer, popupTimer, kuroTimer, hitFace, matchFace;
    bool warned10;
    float unit;          // 1マスの大きさ（ピクセル）
    Vector2 fieldCenter; // 弾の範囲の真ん中（マスの座標）
    RectTransform field;
    Image[,] cellImgs;
    Text[,] cannonMarks;
    Image heartImg, cursorImg;
    Image[,] targetImgs;
    RectTransform targetRoot;
    readonly List<Image> lifeImgs = new List<Image>();
    Text scoreText, timeText, faceText, popupText, kuroText, quotaText;
    Image tvFlash;
    readonly List<Image> enemyImgs = new List<Image>();
    readonly List<Image> dotPool = new List<Image>(), flipPool = new List<Image>();
    readonly List<Text> wordPool = new List<Text>();
    GameObject pauseRoot;

    void StartPuzzle(int n)
    {
        pzStage = n;
        var def = Stages.All[n - 1];
        pz = new Puzzle(def, Environment.TickCount);
        NewScreen(Mode.Puzzle);
        var root = screen.transform;
        Background(root, "bg_puzzle");
        dotPool.Clear(); flipPool.Clear(); wordPool.Clear(); enemyImgs.Clear(); lifeImgs.Clear();

        // ---- 弾の範囲（フィールド）と盤 ----
        float span = pz.Right - pz.Left;
        unit = Mathf.Floor(850f / span);
        fieldCenter = new Vector2((pz.Left + pz.Right) / 2, (pz.Top + pz.Bottom) / 2);
        var fieldBg = Img(root, new Vector2(0, -105), new Vector2(span * unit, span * unit), new Color(0.05f, 0.05f, 0.05f, 0.9f), null, "Field");
        Outlined(fieldBg, Gray, 3);
        field = fieldBg.rectTransform;
        field.gameObject.AddComponent<RectMask2D>();

        cellImgs = new Image[8, 8];
        cannonMarks = new Text[8, 8];
        for (int r = -1; r <= Puzzle.N; r++)
            for (int c = -1; c <= Puzzle.N; c++)
            {
                var img = Img(field, FieldPos(CellCenterAny(r, c)), Vector2.one * (unit - 5), White, null, "Cell");
                cellImgs[r + 1, c + 1] = img;
                int k = Puzzle.CannonAt(r, c);
                if (def.Islands && k >= 0)
                {
                    var cn = Puzzle.Cannons[k];
                    string arrow = cn.dc > 0 ? "▶" : cn.dc < 0 ? "◀" : cn.dr > 0 ? "▼" : "▲";
                    var mark = Txt(img.transform, new Vector2(cn.dc * unit * 0.3f, -cn.dr * unit * 0.3f), Vector2.one * unit, arrow, (int)(unit * 0.4f), Gray);
                    Outlined(mark, Black, 1);
                    cannonMarks[r + 1, c + 1] = mark;
                }
            }
        cursorImg = Img(field, Vector2.zero, Vector2.one * (unit + 6), Gray, frameSprite, "Cursor");
        for (int i = 0; i < def.Emitters.Count; i++)
        {
            var e = Img(field, Vector2.zero, Vector2.one * unit * 0.9f, Color.white,
                        pz.Enemies[i].Kind == EnemyKind.Megaphone ? (LoadImage("megaphone") ?? megaSprite) : (LoadImage("enemy") ?? blobSprite), "Enemy");
            e.preserveAspect = true;
            enemyImgs.Add(e);
        }
        heartImg = Img(field, Vector2.zero, Vector2.one * unit * 0.85f, Color.white, LoadImage("heart") ?? heartSprite, "Heart");
        heartImg.preserveAspect = true;

        // ---- 上のテレビ ----
        var tv = Img(root, new Vector2(0, 440), new Vector2(1180, 190), new Color(0.13f, 0.13f, 0.13f), null, "TV");
        Outlined(tv, Gray, 4);
        tvFlash = Img(tv.transform, Vector2.zero, new Vector2(1180, 190), new Color(1, 1, 1, 0), null, "Flash");
        Txt(tv.transform, new Vector2(-490, 30), new Vector2(180, 40), "STAGE", 28, Gray);
        Txt(tv.transform, new Vector2(-490, -25), new Vector2(180, 80), n.ToString("00"), 64, White).fontStyle = FontStyle.Bold;
        Txt(tv.transform, new Vector2(-330, 65), new Vector2(140, 36), "Target", 26, Gray);
        targetRoot = Rect(tv.transform, "Target", new Vector2(-250, -10), new Vector2(160, 160));
        targetImgs = new Image[8, 8];
        for (int r = 0; r < 8; r++)
            for (int c = 0; c < 8; c++)
                targetImgs[r, c] = Img(targetRoot, Vector2.zero, Vector2.one * 10, White, null, "T");
        Txt(tv.transform, new Vector2(-30, 55), new Vector2(160, 36), "LIFE", 26, Gray);
        for (int i = 0; i < Stages.MaxLife; i++)
            lifeImgs.Add(Img(tv.transform, new Vector2(-70 + i * 50, 10), new Vector2(44, 44), White, lifeSprite, "Life"));
        quotaText = Txt(tv.transform, new Vector2(10, -55), new Vector2(380, 40), "", 26, White);
        var faceBg = Img(tv.transform, new Vector2(430, 0), new Vector2(260, 160), new Color(0.75f, 0.75f, 0.75f), null, "Face");
        faceText = Txt(faceBg.transform, Vector2.zero, new Vector2(260, 160), "", 48, Black);

        // ---- スコアと時間 ----
        var sbox = Img(root, new Vector2(-760, -420), new Vector2(260, 150), Panel, null, "Score");
        Outlined(sbox, Gray, 3);
        Txt(sbox.transform, new Vector2(0, 40), new Vector2(240, 50), "SCORE", 32, Gray);
        scoreText = Txt(sbox.transform, new Vector2(0, -20), new Vector2(240, 80), "0", 64, White);
        var tbox = Img(root, new Vector2(760, -420), new Vector2(260, 150), Panel, null, "Time");
        Outlined(tbox, Gray, 3);
        Txt(tbox.transform, new Vector2(0, 40), new Vector2(240, 50), "TIME", 32, Gray);
        timeText = Txt(tbox.transform, new Vector2(0, -20), new Vector2(240, 80), "", 64, White);

        // ---- シロとクロ ----
        CharacterBox(root, new Vector2(-740, 150), "シロ", "shiro", true, def.Hint);
        kuroText = CharacterBox(root, new Vector2(740, 150), "クロ", "kuro", false, "テキトーでいいって。どうせ誰も見てないし？");
        Txt(root, new Vector2(-740, -200), new Vector2(380, 110), "移動: 矢印キー\n塗り替え: Sキー\n一時停止: Esc", 26, Gray);

        popupText = Txt(root, new Vector2(0, -105), new Vector2(1200, 200), "", 110, White);
        popupText.fontStyle = FontStyle.Bold;
        Outlined(popupText, Black, 4);

        countdown = 3.2f; overTimer = 0; popupTimer = 0; kuroTimer = 6; hitFace = 0; matchFace = 0; warned10 = false;
        RefreshBoard();
        update = PuzzleUpdate;
    }

    Text CharacterBox(Transform root, Vector2 pos, string who, string key, bool dark, string line)
    {
        var sp = LoadImage(key);
        var img = Img(root, pos + new Vector2(0, 120), new Vector2(200, 200), Color.white, sp ?? circleSprite, who);
        img.preserveAspect = true;
        if (sp == null)
        {
            img.color = dark ? Black : White;
            Outlined(img, dark ? White : Black, 3);
            Txt(img.transform, Vector2.zero, new Vector2(200, 80), who, 44, dark ? White : Black);
        }
        var bubble = Img(root, pos + new Vector2(0, -90), new Vector2(380, 190), new Color(0.92f, 0.92f, 0.92f, 0.95f), null, "Bubble");
        return Txt(bubble.transform, Vector2.zero, new Vector2(350, 170), line, 26, Black, TextAnchor.MiddleLeft);
    }

    // 盤面拡張のときにだけ出るマス（-1 と 6）も含めて、マスの中心を返す
    (float, float) CellCenterAny(int r, int c) => pz.CellCenter(r, c);

    Vector2 FieldPos((float x, float y) p) => new Vector2((p.x - fieldCenter.x) * unit, -(p.y - fieldCenter.y) * unit);

    static readonly string[] KuroHit = { "あーあ、気にしすぎだって。", "それ、効いてる？　笑っとけば平気平気。", "ほら、みんなに合わせとけばよかったのに。" };
    static readonly string[] KuroMatch = { "へえ、やるじゃん。", "ま、空気は読めてるね。", "その調子で合わせときな。" };
    static readonly string[] KuroFlip = { "ほら、ひっくり返った。", "せっかく整えたのにねえ。" };
    static readonly string[] KuroExpand = { "うわ、考えること増えた。", "全部気にしてたらキリないよ？" };

    void Kuro(string[] lines)
    {
        kuroText.text = lines[UnityEngine.Random.Range(0, lines.Length)];
        kuroTimer = 5;
    }

    void Popup(string s)
    {
        popupText.text = s;
        popupTimer = 1.1f;
    }

    void PuzzleUpdate()
    {
        float dt = Time.deltaTime;
        if (paused) { if (BackDown()) ResumePuzzle(); return; }
        if (Down(Key.Escape) && !pz.Over) { PausePuzzle(); return; }

        if (countdown > 0)
        {
            countdown -= dt;
            popupText.text = countdown > 2.2f ? "3" : countdown > 1.2f ? "2" : countdown > 0.2f ? "1" : "スタート！";
            popupTimer = countdown > 0 ? 1 : 0.6f;
            if (countdown <= 0 && pz.Islands) Popup("小島盤面！");
            RefreshBoard();
            return;
        }

        if (!pz.Over)
        {
            foreach (var d in ArrowPresses(dt)) pz.Move(d.x, d.y);
            if (Down(Key.S) || Down(Key.Space) || Down(Key.Z)) pz.Flip();
            pz.Update(dt);
        }
        else
        {
            overTimer += dt;
            if (overTimer > 1.6f) { ShowResult(); return; }
        }

        foreach (var e in pz.Events)
        {
            switch (e)
            {
                case Ev.Flip: Play(seFlip, 0.5f); break;
                case Ev.Bump: Play(seBump, 0.3f); break;
                case Ev.Launch: Play(seLaunch, 0.7f); break;
                case Ev.Match: Play(seMatch, 0.8f); Popup("一致！"); matchFace = 1.5f; tvFlash.color = new Color(Pink.r, Pink.g, Pink.b, 0.6f); Kuro(KuroMatch); break;
                case Ev.Hit: Play(seHit, 0.8f); hitFace = 1f; Kuro(KuroHit); break;
                case Ev.FlipHit: Play(seFlipHit, 0.8f); Popup("反転攻撃！"); Kuro(KuroFlip); break;
                case Ev.Expand: Play(seLaunch, 0.8f); Popup("盤面拡張！"); Kuro(KuroExpand); break;
                case Ev.Shrink: Play(seFlipHit, 0.5f); Popup("落ち着いた…"); break;
                case Ev.Clear: Play(seClear, 0.9f); Popup("TIME UP"); break;
                case Ev.Fail: Play(seFail, 0.9f); Popup("GAME OVER"); break;
            }
        }
        pz.Events.Clear();

        if (!warned10 && pz.Remaining <= 10 && !pz.Over)
        {
            warned10 = true;
            kuroText.text = pz.Score >= pz.Def.Quota ? "あと10秒。もう十分じゃない？" : "あと10秒〜。間に合う？";
            kuroTimer = 5;
        }
        if (kuroTimer > 0) kuroTimer -= dt;
        if (popupTimer > 0) popupTimer -= dt;
        if (hitFace > 0) hitFace -= dt;
        if (matchFace > 0) matchFace -= dt;
        RefreshBoard();
    }

    void RefreshBoard()
    {
        // マス
        for (int r = -1; r <= Puzzle.N; r++)
            for (int c = -1; c <= Puzzle.N; c++)
            {
                var img = cellImgs[r + 1, c + 1];
                bool on = pz.InBoard(r, c);
                img.gameObject.SetActive(on);
                if (!on) continue;
                bool black = pz.Get(r, c);
                img.color = black ? Black : White;
                if (pz.IsRing(r, c)) img.color = black ? new Color(0.2f, 0.2f, 0.2f) : new Color(0.78f, 0.78f, 0.78f);
                var mark = cannonMarks[r + 1, c + 1];
                if (mark != null) mark.color = black ? White : Black;
            }

        // ハート
        heartImg.rectTransform.anchoredPosition = FieldPos((pz.HeartX, pz.HeartY));
        bool blink = pz.Invuln > 0 && pz.Invuln < Puzzle.InvulnTime && (int)(Time.time * 12) % 2 == 0;
        heartImg.color = new Color(1, 1, 1, blink ? 0.3f : 1);
        heartImg.rectTransform.localScale = Vector3.one * (pz.FlyLeft > 0 ? 1.4f : 1f);
        cursorImg.rectTransform.anchoredPosition = FieldPos(pz.CellCenter(pz.HeartR, pz.HeartC));
        cursorImg.color = new Color(1f, 0.55f, 0.7f, 0.9f);

        // 敵
        for (int i = 0; i < enemyImgs.Count; i++)
        {
            var en = pz.Enemies[i];
            var img = enemyImgs[i];
            img.gameObject.SetActive(en.Visible && countdown <= 0);
            if (!en.Visible) continue;
            img.rectTransform.anchoredPosition = FieldPos((en.X, en.Y));
            float s = 1 + 0.25f * en.Charge;
            img.rectTransform.localScale = new Vector3(en.Kind == EnemyKind.Megaphone && en.X > fieldCenter.x ? -s : s, s, 1);
            img.color = Color.Lerp(Color.white, Pink, en.Charge * en.Charge);
        }

        // 弾
        int nd = 0, nf = 0, nw = 0;
        foreach (var b in pz.Bullets)
        {
            var pos = FieldPos((b.X, b.Y));
            if (b.Kind == BulletKind.Dot)
            {
                var img = Pooled(dotPool, nd++, dotSprite);
                img.rectTransform.anchoredPosition = pos;
                img.rectTransform.sizeDelta = Vector2.one * b.R * 2.4f * unit;
            }
            else if (b.Kind == BulletKind.Flip)
            {
                var img = Pooled(flipPool, nf++, flipSprite);
                img.rectTransform.anchoredPosition = pos;
                img.rectTransform.sizeDelta = Vector2.one * b.R * 2.6f * unit;
                img.rectTransform.localRotation = Quaternion.Euler(0, 0, Time.time * 360);
            }
            else
            {
                if (nw >= wordPool.Count)
                {
                    var t = Txt(field, Vector2.zero, new Vector2(10, 10), "", 10, Black);
                    t.fontStyle = FontStyle.Bold;
                    t.horizontalOverflow = HorizontalWrapMode.Overflow;
                    Outlined(t, White, 3);
                    wordPool.Add(t);
                }
                var w = wordPool[nw++];
                w.gameObject.SetActive(true);
                w.text = b.Text;
                w.fontSize = (int)(unit * 0.55f);
                w.rectTransform.anchoredPosition = pos;
                w.rectTransform.sizeDelta = new Vector2(b.HalfW * 2 * unit, b.HalfH * 2 * unit);
            }
        }
        for (int i = nd; i < dotPool.Count; i++) dotPool[i].gameObject.SetActive(false);
        for (int i = nf; i < flipPool.Count; i++) flipPool[i].gameObject.SetActive(false);
        for (int i = nw; i < wordPool.Count; i++) wordPool[i].gameObject.SetActive(false);
        heartImg.transform.SetAsLastSibling();

        // テレビ: お題（拡張中は 8×8、外側は白）
        int size = pz.Expanded ? 8 : 6, off = pz.Expanded ? 1 : 0;
        float cell = 150f / size;
        for (int r = 0; r < 8; r++)
            for (int c = 0; c < 8; c++)
            {
                var img = targetImgs[r, c];
                bool on = r < size && c < size;
                img.gameObject.SetActive(on);
                if (!on) continue;
                int tr = r - off, tc = c - off;
                bool black = tr >= 0 && tr < 6 && tc >= 0 && tc < 6 && pz.Target[tr, tc];
                img.color = black ? Black : White;
                img.rectTransform.anchoredPosition = new Vector2((c - (size - 1) / 2f) * cell, -(r - (size - 1) / 2f) * cell);
                img.rectTransform.sizeDelta = Vector2.one * (cell - 2);
            }
        for (int i = 0; i < lifeImgs.Count; i++) lifeImgs[i].color = i < pz.Life ? White : new Color(0.3f, 0.3f, 0.3f);
        quotaText.text = $"ノルマ {pz.Def.Quota}　★2: {pz.Def.Star2}　★3: {pz.Def.Star3}";
        if (tvFlash.color.a > 0) tvFlash.color = new Color(Pink.r, Pink.g, Pink.b, Mathf.Max(0, tvFlash.color.a - Time.deltaTime));

        // 人間さんの様子（現実世界の反映）
        faceText.text = hitFace > 0 ? "(＞＿＜)" : matchFace > 0 ? "(・▽・)" : pz.Life >= 3 ? "(・_・)" : pz.Life == 2 ? "(；・_・)" : "(；_；)";

        scoreText.text = pz.Score.ToString();
        scoreText.color = pz.Score >= pz.Def.Quota ? White : new Color(1f, 0.7f, 0.7f);
        int sec = Mathf.CeilToInt(pz.Remaining);
        timeText.text = $"{sec / 60}:{sec % 60:00}";
        timeText.color = sec <= 10 ? new Color(1f, 0.6f, 0.6f) : White;
        popupText.gameObject.SetActive(popupTimer > 0);
        popupText.color = new Color(1, 1, 1, Mathf.Clamp01(popupTimer * 2));
    }

    Image Pooled(List<Image> pool, int i, Sprite sprite)
    {
        if (i >= pool.Count) pool.Add(Img(field, Vector2.zero, Vector2.one * 10, Color.white, sprite, "Bullet"));
        pool[i].gameObject.SetActive(true);
        return pool[i];
    }

    void PausePuzzle()
    {
        paused = true;
        pauseRoot = Img(screen.transform, Vector2.zero, new Vector2(1920, 1080), new Color(0, 0, 0, 0.75f), null, "Pause").gameObject;
        Txt(pauseRoot.transform, new Vector2(0, 220), new Vector2(800, 100), "一時停止", 64, White);
        Btn(pauseRoot.transform, new Vector2(0, 60), new Vector2(460, 80), "つづける", ResumePuzzle);
        Btn(pauseRoot.transform, new Vector2(0, -50), new Vector2(460, 80), "最初からやり直す", () => StartPuzzle(pzStage));
        Btn(pauseRoot.transform, new Vector2(0, -160), new Vector2(460, 80), "ステージ選択へ", ShowSelect);
    }

    void ResumePuzzle()
    {
        if (pauseRoot != null) Destroy(pauseRoot);
        menu.Clear();
        paused = false;
    }

    // =====================================================================
    // 結果
    // =====================================================================
    void ShowResult()
    {
        var def = pz.Def;
        int stars = pz.Cleared ? pz.Stars : 0;
        int n = pzStage;
        bool newBest = stars > Best(n);
        if (newBest) { PlayerPrefs.SetInt(BestKey(n), stars); PlayerPrefs.Save(); }

        NewScreen(Mode.Result);
        var root = screen.transform;
        Background(root);
        Txt(root, new Vector2(0, 330), new Vector2(1400, 140), stars > 0 ? "STAGE CLEAR" : "GAME OVER", 110, White).fontStyle = FontStyle.Bold;
        Txt(root, new Vector2(0, 230), new Vector2(1400, 60), $"{n}. {def.Title}" + (stars > 0 ? "" : $"　—　{pz.FailReason}"), 38, Gray);
        for (int i = 0; i < 3; i++)
            Img(root, new Vector2(-180 + i * 180, 90), new Vector2(150, 150), i < stars ? White : new Color(0.25f, 0.25f, 0.25f), starSprite, "Star");
        Txt(root, new Vector2(0, -50), new Vector2(1400, 60), $"SCORE {pz.Score}　（ノルマ {def.Quota}／★2 {def.Star2}／★3 {def.Star3}）", 36, White);
        Txt(root, new Vector2(0, -120), new Vector2(1400, 60),
            $"心の余裕　★ {TotalStars()} / {Stages.Count * 3}" + (newBest ? "　（記録更新）" : ""), 36, newBest ? Pink : Gray);

        if (stars > 0)
        {
            Btn(root, new Vector2(0, -260), new Vector2(460, 80), "つづける", () =>
                PlayNovel(n + (stars == 3 && story.ContainsKey(n + "-after-good") ? "-after-good" : "-after"), () =>
                {
                    if (n == Stages.Count) ShowEnding();
                    else { lastStage = n + 1; ShowSelect(); }
                }));
            Btn(root, new Vector2(0, -360), new Vector2(460, 80), "もう一度", () => StartPuzzle(n));
        }
        else
        {
            Btn(root, new Vector2(0, -260), new Vector2(460, 80), "もう一度", () => StartPuzzle(n));
            Btn(root, new Vector2(0, -360), new Vector2(460, 80), "ステージ選択へ", ShowSelect);
        }
    }

    // =====================================================================
    // エンディング
    // =====================================================================
    void ShowEnding()
    {
        int total = TotalStars();
        int e = Stages.EndingFor(total);
        PlayerPrefs.SetInt("mh_ending", 1);
        PlayerPrefs.Save();
        PlayNovel(Stages.EndingKeys[e], () =>
        {
            NewScreen(Mode.Result);
            var root = screen.transform;
            var bg = Img(root, Vector2.zero, new Vector2(1920, 1080), Color.white, LoadImage(Stages.EndingKeys[e]) ?? checkerSprite, "EndingBg");
            bg.rectTransform.anchorMin = Vector2.zero; bg.rectTransform.anchorMax = Vector2.one;
            bg.rectTransform.offsetMin = bg.rectTransform.offsetMax = Vector2.zero;
            var title = Txt(root, new Vector2(0, 120), new Vector2(1600, 160), Stages.EndingNames[e], 120, White);
            title.fontStyle = FontStyle.Bold;
            Outlined(title, Black, 4);
            Txt(root, new Vector2(0, -20), new Vector2(1600, 60), $"心の余裕　★ {total} / {Stages.Count * 3}", 40, White);
            Txt(root, new Vector2(0, -90), new Vector2(1600, 60), "Thank you for playing", 34, Gray);
            Btn(root, new Vector2(0, -300), new Vector2(460, 80), "タイトルへ", ShowTitle);
        });
    }

    // =====================================================================
    // 仮の絵（コードで描く）
    // =====================================================================
    void MakeSprites()
    {
        var wt = new Texture2D(4, 4);
        var px = new Color[16];
        for (int i = 0; i < 16; i++) px[i] = Color.white;
        wt.SetPixels(px); wt.Apply();
        sqSprite = Sprite.Create(wt, new UnityEngine.Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 100);

        Color clear = new Color(1, 1, 1, 0), w = Color.white, b = new Color(0.05f, 0.05f, 0.05f), g = new Color(0.55f, 0.55f, 0.55f);

        circleSprite = Shape(256, (u, v) => u * u + v * v < 0.96f ? w : clear);

        // ハート: 左が白、右が黒（灰色のふち）
        Func<float, float, float, bool> InHeart = (u, v, s) =>
        {
            float x = u / s * 1.25f, y = v / s * 1.25f + 0.2f;
            float a = x * x + y * y - 1;
            return a * a * a - x * x * y * y * y <= 0;
        };
        heartSprite = Shape(192, (u, v) => !InHeart(u, v, 1f) ? clear : !InHeart(u, v, 0.86f) ? g : u < 0 ? w : b);
        lifeSprite = Shape(96, (u, v) => InHeart(u, v, 1f) ? w : clear);

        // 弾: 黒い玉に白いふち（白いマスの上でも黒いマスの上でも見える）
        dotSprite = Shape(64, (u, v) => { float r = u * u + v * v; return r > 0.96f ? clear : r > 0.45f ? w : b; });
        flipSprite = Shape(64, (u, v) => { float d = Mathf.Abs(u) + Mathf.Abs(v); return d > 0.98f ? clear : d > 0.62f ? w : d > 0.3f ? b : w; });

        // 敵: とげとげの黒い玉に白い目
        blobSprite = Shape(128, (u, v) =>
        {
            float r = Mathf.Sqrt(u * u + v * v), th = Mathf.Atan2(v, u);
            float edge = 0.78f + 0.17f * Mathf.Cos(th * 9);
            if (r > edge) return clear;
            if (r > edge - 0.1f) return w;
            if ((u + 0.25f) * (u + 0.25f) + (v - 0.08f) * (v - 0.08f) < 0.017f) return w;
            if ((u - 0.25f) * (u - 0.25f) + (v - 0.08f) * (v - 0.08f) < 0.017f) return w;
            if (v < -0.25f && v > -0.33f && Mathf.Abs(u) < 0.25f) return w;
            return b;
        });

        // メガホン（右向き）
        Func<float, float, bool> InMega = (u, v) =>
        {
            if (u > -0.9f && u < -0.4f && Mathf.Abs(v) < 0.22f) return true;
            return u >= -0.4f && u < 0.85f && Mathf.Abs(v) < 0.22f + (u + 0.4f) * 0.5f;
        };
        megaSprite = Shape(128, (u, v) => !InMega(u, v) ? clear : InMega(u * 1.12f + 0.02f, v * 1.15f) ? b : w);

        // 星
        var star = new Vector2[10];
        for (int i = 0; i < 10; i++)
        {
            float a = Mathf.PI / 2 + i * Mathf.PI / 5, rad = i % 2 == 0 ? 0.98f : 0.42f;
            star[i] = new Vector2(Mathf.Cos(a) * rad, Mathf.Sin(a) * rad - 0.05f);
        }
        starSprite = Shape(128, (u, v) => InPoly(star, u, v) ? w : clear);

        frameSprite = Shape(64, (u, v) => Mathf.Max(Mathf.Abs(u), Mathf.Abs(v)) > 0.8f ? w : clear);

        // 背景の市松模様
        var ct = new Texture2D(16, 9) { filterMode = FilterMode.Point };
        var cp = new Color[16 * 9];
        for (int y = 0; y < 9; y++)
            for (int x = 0; x < 16; x++)
                cp[y * 16 + x] = (x + y) % 2 == 0 ? new Color(0.13f, 0.13f, 0.13f) : new Color(0.17f, 0.17f, 0.17f);
        ct.SetPixels(cp); ct.Apply();
        checkerSprite = Sprite.Create(ct, new UnityEngine.Rect(0, 0, 16, 9), new Vector2(0.5f, 0.5f), 100);
    }

    static bool InPoly(Vector2[] p, float x, float y)
    {
        bool inside = false;
        for (int i = 0, j = p.Length - 1; i < p.Length; j = i++)
            if ((p[i].y > y) != (p[j].y > y) && x < (p[j].x - p[i].x) * (y - p[i].y) / (p[j].y - p[i].y) + p[i].x)
                inside = !inside;
        return inside;
    }

    // u, v は -1..1（v は上が +）。3×3 点の平均でふちをなめらかにする
    static Sprite Shape(int size, Func<float, float, Color> f)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var px = new Color[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                Color sum = Color.clear;
                for (int sy = 0; sy < 3; sy++)
                    for (int sx = 0; sx < 3; sx++)
                    {
                        float u = (x + (sx + 0.5f) / 3) / size * 2 - 1, v = (y + (sy + 0.5f) / 3) / size * 2 - 1;
                        var c = f(u, v);
                        sum += new Color(c.r * c.a, c.g * c.a, c.b * c.a, c.a);
                    }
                sum /= 9;
                px[y * size + x] = sum.a > 0 ? new Color(sum.r / sum.a, sum.g / sum.a, sum.b / sum.a, sum.a) : Color.clear;
            }
        tex.SetPixels(px);
        tex.Apply();
        return Sprite.Create(tex, new UnityEngine.Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100);
    }

    // =====================================================================
    // 効果音（Assets/Resources/Sounds/ に同じ名前の音があればそれを使う。無ければコードで作った音）
    // =====================================================================
    void MakeSounds()
    {
        seFlip = LoadSound("flip") ?? Tone(0.06f, t => Square(t, 880) * Env(t, 0.06f));
        seBump = LoadSound("bump") ?? Tone(0.05f, t => Square(t, 140) * Env(t, 0.05f));
        seSelect = LoadSound("select") ?? Tone(0.05f, t => Square(t, 660) * Env(t, 0.05f));
        seMatch = LoadSound("match") ?? Tone(0.5f, t =>
        {
            float f = t < 0.1f ? 523 : t < 0.2f ? 659 : t < 0.3f ? 784 : 1047;
            return Square(t, f) * 0.6f * Env(t, 0.5f);
        });
        var rnd = new System.Random(1);
        seHit = LoadSound("hit") ?? Tone(0.3f, t => ((float)rnd.NextDouble() * 2 - 1) * Env(t, 0.3f) + Square(t, 90 - t * 150) * 0.5f * Env(t, 0.3f));
        seFlipHit = LoadSound("fliphit") ?? Tone(0.3f, t => Square(t, 300 + 900 * t) * 0.5f * Env(t, 0.3f));
        seLaunch = LoadSound("launch") ?? Tone(0.3f, t => Square(t, 200 + 1400 * t) * 0.4f * Env(t, 0.3f));
        seClear = LoadSound("clear") ?? Tone(0.9f, t =>
        {
            float f = t < 0.15f ? 523 : t < 0.3f ? 659 : t < 0.45f ? 784 : 1047;
            return Square(t, f) * 0.5f * Env(t, 0.9f);
        });
        seFail = LoadSound("fail") ?? Tone(0.9f, t => Square(t, t < 0.3f ? 392 : t < 0.6f ? 330 : 262) * 0.5f * Env(t, 0.9f));
    }

    static AudioClip LoadSound(string name) => Resources.Load<AudioClip>("Sounds/" + name);

    static float Square(float t, float f) => Mathf.Sin(2 * Mathf.PI * f * t) > 0 ? 0.3f : -0.3f;
    static float Env(float t, float len) => Mathf.Clamp01(1 - t / len);

    static AudioClip Tone(float len, Func<float, float> f)
    {
        const int rate = 44100;
        int n = (int)(len * rate);
        var data = new float[n];
        for (int i = 0; i < n; i++) data[i] = f((float)i / rate);
        var clip = AudioClip.Create("se", n, 1, rate, false);
        clip.SetData(data, 0);
        return clip;
    }

    void Play(AudioClip c, float vol)
    {
        if (c != null && sfx != null) sfx.PlayOneShot(c, vol);
    }
}
