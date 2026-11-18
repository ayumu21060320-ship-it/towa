// モノクロハート — ステージのデータ（Unity に依存しない C#）
// ステージの難しさ・敵・ギミックはここで調整する。セリフは Assets/Resources/Story.txt にある。
using System.Collections.Generic;

namespace MonochroHeart
{
    // 敵の撃ち方
    public enum Pattern
    {
        Aim,     // 盤のまわりを回る敵が、ハートをねらって撃つ
        Rain,    // 上から雨のように降る
        Ring,    // 敵が全方向にまとめて撃つ
        Word,    // メガホンから悪口の文字が横に飛んでくる（大きい）
        FlipAim, // 当たるとまわりのマスが反転する弾（反転攻撃）。ライフは減らない
        Wall,    // すき間が1つだけある弾の列が横から迫ってくる
    }

    public class EmitterDef
    {
        public Pattern Pattern;
        public float Interval; // 何秒ごとに撃つか
        public float Start;    // ステージ開始から何秒後に撃ち始めるか
        public float Speed;    // 弾の速さ（1 = 1秒で1マス）
        public int Count;      // Ring の弾の数、Wall の弾の数など

        public EmitterDef(Pattern p, float interval, float speed, float start = 0, int count = 0)
        {
            Pattern = p; Interval = interval; Speed = speed; Start = start; Count = count;
        }
    }

    public class StageDef
    {
        public int Number;          // 1..12
        public int Chapter;         // 1..4
        public string Title;        // 悩みの種
        public float TimeLimit;     // 秒
        public int Quota;           // ノルマ（★1）。これ未満で時間切れならゲームオーバー
        public int Star2, Star3;    // ★2・★3 に必要な一致の数
        public bool Islands;        // 小島盤面
        public List<(float start, float duration)> Expansions = new List<(float, float)>(); // 盤面拡張の時間
        public List<EmitterDef> Emitters = new List<EmitterDef>();
        public string[] Words;      // Word の弾に書く言葉
        public string Hint;         // パズル開始前にシロが言うヒント
    }

    public static class Stages
    {
        public const int Count = 12;
        public const int MaxLife = 4;
        public static readonly string[] ChapterNames = { "擬態", "同調", "揺らぎ", "決意" };

        // エンディングの境目（企画書: 18以下 未練／19〜27 曖昧／28以上 決意）
        public const int EndingVague = 19;
        public const int EndingResolve = 28;

        public static readonly StageDef[] All = Build();

        static StageDef[] Build()
        {
            var s = new StageDef[Count];
            string[] gossip = { "バカ", "ウザ", "空気読めよ", "キモ", "ノリ悪", "は？" };
            string[] future = { "現実見ろ", "普通は", "安定が一番", "で、どうするの", "みんなは", "甘い" };

            // ---- 1章 擬態 ----
            s[0] = Stage(1, "自己紹介", 120, 2, 4, 6,
                "盤面の白黒を、上のテレビのお題と同じにするんだ。矢印キーで動いて、Sキーでマスを裏返す。",
                E(Pattern.Aim, 2.6f, 2.2f, 5));
            s[1] = Stage(2, "グループ決め", 120, 3, 5, 7,
                "上からも不安が降ってくる。弾をよけながら、少しずつ整えればいい。",
                E(Pattern.Aim, 2.4f, 2.4f, 3), E(Pattern.Rain, 1.1f, 2.0f, 8));
            s[2] = Stage(3, "遊びの誘い", 120, 3, 5, 7,
                "敵がまわりにまとめて撃ってくる。撃つ前に少し光るから、すき間をよく見て。",
                E(Pattern.Aim, 2.6f, 2.4f, 3), E(Pattern.Ring, 3.6f, 2.0f, 10, 10));

            // ---- 2章 同調 ----
            s[3] = Stage(4, "頼み事", 120, 3, 5, 7,
                "ひし形の弾に当たると、まわりのマスがひっくり返る。ライフは減らないけど、整えた形が崩れるぞ。",
                E(Pattern.FlipAim, 2.8f, 2.2f, 4), E(Pattern.Rain, 1.2f, 2.0f, 10));
            s[4] = Stage(5, "LINEの空気", 120, 3, 5, 7,
                "言葉の弾は大きい。でも、まっすぐにしか飛んでこない。行の外に逃げれば当たらない。",
                E(Pattern.Word, 3.0f, 2.6f, 3), E(Pattern.Aim, 2.8f, 2.4f, 10));
            s[4].Words = gossip;
            s[5] = Stage(6, "悪口の同調", 120, 3, 6, 8,
                "悪口と反転攻撃が同時に来る。あわてず、崩れたところから直していこう。",
                E(Pattern.Word, 2.8f, 2.8f, 3), E(Pattern.FlipAim, 3.2f, 2.4f, 8), E(Pattern.Rain, 1.4f, 2.2f, 20));
            s[5].Words = gossip;

            // ---- 3章 揺らぎ ----
            s[6] = Stage(7, "文化祭", 120, 3, 5, 7,
                "盤面が4つの島に分かれた。島から島へは、大砲のマスから大砲の向きに進むと飛んで行ける。",
                E(Pattern.Aim, 2.4f, 2.4f, 4), E(Pattern.Ring, 4.0f, 2.0f, 12, 10));
            s[6].Islands = true;
            s[7] = Stage(8, "進路希望", 120, 3, 4, 6,
                "心が混乱すると盤面が広がる。広がった外側のマスは全部、白に戻して。",
                E(Pattern.Aim, 2.4f, 2.5f, 3), E(Pattern.Rain, 1.2f, 2.2f, 10));
            s[7].Expansions.Add((30, 25)); s[7].Expansions.Add((80, 25));
            s[8] = Stage(9, "親との進路相談", 120, 2, 4, 5,
                "広がる盤面に、反転攻撃と言葉の弾。いちばん苦しい所だ。それでも、一つずつ。",
                E(Pattern.Word, 3.4f, 2.6f, 4), E(Pattern.FlipAim, 3.4f, 2.4f, 8), E(Pattern.Aim, 3.0f, 2.6f, 14));
            s[8].Words = future;
            s[8].Expansions.Add((25, 25)); s[8].Expansions.Add((75, 30));

            // ---- 4章 決意 ----
            s[9] = Stage(10, "本音の相手", 120, 3, 5, 7,
                "島に分かれた盤面に、言葉の弾が飛んでくる。大砲で逃げることもできる。",
                E(Pattern.Word, 3.0f, 2.8f, 3), E(Pattern.Aim, 2.6f, 2.6f, 6), E(Pattern.Wall, 7.0f, 1.6f, 20, 0));
            s[9].Islands = true;
            s[9].Words = gossip;
            s[10] = Stage(11, "距離感の管理", 120, 3, 5, 6,
                "迫ってくる弾の壁には、必ず1か所すき間がある。そこへ動こう。",
                E(Pattern.Wall, 5.5f, 1.8f, 5, 0), E(Pattern.Ring, 4.0f, 2.2f, 10, 12), E(Pattern.FlipAim, 3.6f, 2.4f, 15));
            s[10].Expansions.Add((45, 25));
            s[11] = Stage(12, "最後の別れ", 120, 3, 5, 7,
                "全部が来る。でも、ここまで来た心なら整えられる。……いってらっしゃい。",
                E(Pattern.Word, 3.2f, 3.0f, 3), E(Pattern.FlipAim, 3.4f, 2.6f, 6), E(Pattern.Ring, 4.6f, 2.2f, 12, 12),
                E(Pattern.Wall, 8.0f, 1.8f, 30, 0), E(Pattern.Rain, 1.6f, 2.2f, 40));
            s[11].Words = new[] { "本当は", "言えない", "ごめん", "ありがとう", "またね", "嫌だ" };
            s[11].Expansions.Add((60, 25));
            return s;
        }

        static StageDef Stage(int n, string title, float time, int quota, int star2, int star3, string hint, params EmitterDef[] em)
        {
            return new StageDef
            {
                Number = n, Chapter = (n - 1) / 3 + 1, Title = title, TimeLimit = time,
                Quota = quota, Star2 = star2, Star3 = star3, Hint = hint,
                Emitters = new List<EmitterDef>(em), Words = new[] { "バカ" },
            };
        }

        static EmitterDef E(Pattern p, float interval, float speed, float start, int count = 0)
            => new EmitterDef(p, interval, speed, start, count);

        public static int StarsFor(StageDef st, int score)
            => score >= st.Star3 ? 3 : score >= st.Star2 ? 2 : score >= st.Quota ? 1 : 0;

        // 0 = 未練エンド, 1 = 曖昧エンド, 2 = 決意エンド
        public static int EndingFor(int totalStars)
            => totalStars >= EndingResolve ? 2 : totalStars >= EndingVague ? 1 : 0;

        public static readonly string[] EndingNames = { "未練エンド", "曖昧エンド", "決意エンド" };
        public static readonly string[] EndingKeys = { "end-miren", "end-aimai", "end-ketsui" };

        // ---- お題（6×6 の白黒の絵）。# が黒、. が白 ----
        public static readonly string[][] Pictures =
        {
            new[] { ".#..#.", "######", "######", ".####.", "..##..", "......" }, // ハート
            new[] { "......", ".#..#.", ".#..#.", "......", "#....#", ".####." }, // 笑顔
            new[] { "######", "#....#", "#....#", "#....#", "#....#", "######" }, // 枠
            new[] { "..##..", "..##..", "######", "######", "..##..", "..##.." }, // 十字
            new[] { "#....#", ".#..#.", "..##..", "..##..", ".#..#.", "#....#" }, // バツ
            new[] { "..##..", ".####.", "######", "..##..", "..##..", "..##.." }, // 上向きの矢印
            new[] { "..##..", ".####.", "######", ".#..#.", ".#..#.", ".####." }, // 家
            new[] { "##..##", "##..##", "..##..", "..##..", "##..##", "##..##" }, // 市松
            new[] { "#..#..", ".#..#.", "..#..#", "#..#..", ".#..#.", "..#..#" }, // ななめの縞
            new[] { "..###.", "..#.##", "..#...", "..#...", "###...", "###..." }, // 音符
            new[] { "..#...", "..##..", ".####.", ".####.", ".####.", "..##.." }, // しずく
            new[] { "#....#", "##..##", "######", "#.##.#", "######", ".####." }, // ねこ
            new[] { "######", "#....#", "#....#", "######", ".##...", ".#...." }, // ふきだし
            new[] { "..##..", ".#..#.", "#....#", "#....#", ".#..#.", "..##.." }, // ひし形
            new[] { "......", ".#..#.", ".#..#.", "......", ".####.", "#....#" }, // 泣き顔
            new[] { ".####.", ".#..#.", ".#..#.", ".#..#.", ".####.", "..##.." }, // スマホ
            new[] { ".####.", "#....#", "....#.", "...#..", "......", "...#.." }, // はてな
            new[] { "..##..", "..##..", "..##..", "..##..", "......", "..##.." }, // びっくり
            new[] { "..##..", "..##..", "######", ".####.", ".#..#.", "#....#" }, // 星
            new[] { "###...", "###...", "###...", "###...", "###...", "###..." }, // 白と黒
            new[] { "..###.", ".##...", "##....", "##....", ".##...", "..###." }, // 月
            new[] { ".#..#.", "###.##", "##.###", ".##.#.", "..##..", "......" }, // 割れたハート
        };

        public static bool[,] Picture(int index)
        {
            var p = Pictures[index];
            var b = new bool[6, 6];
            for (int r = 0; r < 6; r++)
                for (int c = 0; c < 6; c++)
                    b[r, c] = p[r][c] == '#';
            return b;
        }
    }
}
