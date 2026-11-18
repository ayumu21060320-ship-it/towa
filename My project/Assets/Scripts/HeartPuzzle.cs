// モノクロハート — パズルパートのルール（Unity に依存しない C#）
// 座標は「マス」単位。盤の左上の角が (0, 0)、x は右、y は下に増える。マス (r, c) の中心は (c + 0.5, r + 0.5)。
// 小島盤面のときは、右半分・下半分の島が Gap だけずれる。盤面拡張のときは、マス -1 と 6 の列・行が外側に増える。
using System;
using System.Collections.Generic;

namespace MonochroHeart
{
    public enum BulletKind { Dot, Word, Flip }

    public class Bullet
    {
        public BulletKind Kind;
        public float X, Y, VX, VY;
        public float R;            // 丸い弾の半径
        public float HalfW, HalfH; // 言葉の弾の大きさの半分
        public string Text;
        public bool Dead;
    }

    public enum EnemyKind { Blob, Megaphone }

    public class Enemy
    {
        public EnemyKind Kind;
        public float X, Y;
        public float PathT;    // 盤のまわりの道のどこにいるか
        public float Charge;   // 0..1。撃つ直前に 1 に近づく（光らせる）
        public bool Visible = true;
        public bool Armed;     // メガホン: 撃つ位置を決め終わった
    }

    public enum Ev { Flip, Match, Hit, FlipHit, Expand, Shrink, Launch, Bump, Fire, Clear, Fail }

    public class Puzzle
    {
        public const int N = 6;
        public const float Gap = 0.8f;          // 小島のすき間
        public const float HeartRadius = 0.25f;
        public const float InvulnTime = 1.5f;
        public const float FlyTime = 0.35f;

        // 大砲: (行, 列, 撃つ向き)。島を時計回りに渡る
        public static readonly (int r, int c, int dr, int dc)[] Cannons =
        {
            (1, 2, 0, 1),   // 左上の島 → 右上の島
            (2, 4, 1, 0),   // 右上の島 → 右下の島
            (4, 3, 0, -1),  // 右下の島 → 左下の島
            (3, 1, -1, 0),  // 左下の島 → 左上の島
        };

        public readonly StageDef Def;
        readonly bool[,] cells = new bool[N + 2, N + 2]; // 添え字 = マス + 1（拡張のぶん）
        public bool[,] Target { get; private set; }
        public int HeartR, HeartC;
        public float HeartX, HeartY;
        public float Time;
        public int Life, Score;
        public bool Expanded;
        public float Invuln, FlyLeft;
        public bool Over, Cleared;
        public string FailReason = "";
        public float Left, Top, Right, Bottom; // 弾が動ける範囲

        public readonly List<Bullet> Bullets = new List<Bullet>();
        public readonly List<Enemy> Enemies = new List<Enemy>(); // Emitters と同じ順（敵のいない撃ち方は Visible = false）
        public readonly List<Ev> Events = new List<Ev>();

        readonly Random rng;
        readonly float[] nextFire;
        readonly List<int> picOrder = new List<int>();
        int picPos;
        int expansionIndex = -1;

        public bool Islands => Def.Islands;
        public float Remaining => Math.Max(0, Def.TimeLimit - Time);
        public int Stars => Stages.StarsFor(Def, Score);
        public int Min => Expanded ? -1 : 0;
        public int Max => Expanded ? N : N - 1;
        public float BoardWidth => N + (Islands ? Gap : 0);

        public Puzzle(StageDef def, int seed)
        {
            Def = def;
            rng = new Random(seed);
            Life = Stages.MaxLife;
            HeartR = 2; HeartC = 2;
            (HeartX, HeartY) = CellCenter(HeartR, HeartC);

            float m = 2.6f;
            Left = -m; Top = -m; Right = BoardWidth + m; Bottom = BoardWidth + m;

            nextFire = new float[def.Emitters.Count];
            for (int i = 0; i < def.Emitters.Count; i++)
            {
                var e = def.Emitters[i];
                nextFire[i] = e.Start;
                var en = new Enemy();
                switch (e.Pattern)
                {
                    case Pattern.Aim: case Pattern.Ring: case Pattern.FlipAim:
                        en.Kind = EnemyKind.Blob;
                        en.PathT = (float)rng.NextDouble() * PathLength;
                        break;
                    case Pattern.Word:
                        en.Kind = EnemyKind.Megaphone;
                        en.Visible = false;
                        break;
                    default:
                        en.Visible = false;
                        break;
                }
                Enemies.Add(en);
            }
            UpdateEnemies(0);
            NextTarget();
        }

        // ---- 盤 ----
        public bool InBoard(int r, int c) => r >= Min && r <= Max && c >= Min && c <= Max;
        public bool Get(int r, int c) => cells[r + 1, c + 1];
        void Set(int r, int c, bool v) => cells[r + 1, c + 1] = v;
        public bool IsRing(int r, int c) => r < 0 || r >= N || c < 0 || c >= N;

        public (float x, float y) CellCenter(int r, int c)
        {
            float x = c + 0.5f, y = r + 0.5f;
            if (Islands)
            {
                if (c >= N / 2) x += Gap;
                if (r >= N / 2) y += Gap;
            }
            return (x, y);
        }

        static int Island(int r, int c) => (r < N / 2 ? 0 : 2) + (c < N / 2 ? 0 : 1);

        public static int CannonAt(int r, int c)
        {
            for (int i = 0; i < Cannons.Length; i++)
                if (Cannons[i].r == r && Cannons[i].c == c) return i;
            return -1;
        }

        // 今の盤面がお題と同じか（拡張中は外側が全部白であること）
        public bool Matches()
        {
            for (int r = Min; r <= Max; r++)
                for (int c = Min; c <= Max; c++)
                {
                    bool want = !IsRing(r, c) && Target[r, c];
                    if (Get(r, c) != want) return false;
                }
            return true;
        }

        // お題と違うマスの数
        public int Diff()
        {
            int d = 0;
            for (int r = Min; r <= Max; r++)
                for (int c = Min; c <= Max; c++)
                    if (Get(r, c) != (!IsRing(r, c) && Target[r, c])) d++;
            return d;
        }

        void NextTarget()
        {
            for (int tries = 0; tries < 50; tries++)
            {
                if (picPos >= picOrder.Count)
                {
                    picOrder.Clear();
                    for (int i = 0; i < Stages.Pictures.Length; i++) picOrder.Add(i);
                    for (int i = picOrder.Count - 1; i > 0; i--)
                    {
                        int j = rng.Next(i + 1);
                        (picOrder[i], picOrder[j]) = (picOrder[j], picOrder[i]);
                    }
                    picPos = 0;
                }
                var pic = Stages.Picture(picOrder[picPos++]);
                // 3章からは、白黒が逆のお題も出る
                if (Def.Chapter >= 3 && rng.NextDouble() < 0.3)
                    for (int r = 0; r < N; r++) for (int c = 0; c < N; c++) pic[r, c] = !pic[r, c];
                Target = pic;
                if (!Matches()) return;
            }
        }

        // ---- 操作 ----
        public void Move(int dr, int dc)
        {
            if (Over || FlyLeft > 0) return;
            int nr = HeartR + dr, nc = HeartC + dc;
            if (Islands && Island(nr, nc) != Island(HeartR, HeartC))
            {
                int k = CannonAt(HeartR, HeartC);
                if (k >= 0 && Cannons[k].dr == dr && Cannons[k].dc == dc)
                {
                    HeartR = nr; HeartC = nc;
                    FlyLeft = FlyTime;
                    Events.Add(Ev.Launch);
                }
                else Events.Add(Ev.Bump);
                return;
            }
            if (!InBoard(nr, nc)) { Events.Add(Ev.Bump); return; }
            HeartR = nr; HeartC = nc;
        }

        public void Flip()
        {
            if (Over || FlyLeft > 0) return;
            Set(HeartR, HeartC, !Get(HeartR, HeartC));
            Events.Add(Ev.Flip);
            CheckMatch();
        }

        void CheckMatch()
        {
            if (Over || !Matches()) return;
            Score++;
            Events.Add(Ev.Match);
            NextTarget();
        }

        // ---- 時間を進める ----
        public void Update(float dt)
        {
            if (Over) return;
            Time += dt;
            if (Invuln > 0) Invuln -= dt;

            // ハートの見た目の位置を、いるマスへ近づける
            var (tx, ty) = CellCenter(HeartR, HeartC);
            float speed = FlyLeft > 0 ? 14f : 16f;
            if (FlyLeft > 0) FlyLeft -= dt;
            float dx = tx - HeartX, dy = ty - HeartY;
            float dist = (float)Math.Sqrt(dx * dx + dy * dy);
            if (dist <= speed * dt) { HeartX = tx; HeartY = ty; }
            else { HeartX += dx / dist * speed * dt; HeartY += dy / dist * speed * dt; }

            UpdateExpansion();
            UpdateEnemies(dt);
            Fire();
            MoveBullets(dt);

            if (!Over && Time >= Def.TimeLimit)
            {
                Over = true;
                if (Score >= Def.Quota) { Cleared = true; Events.Add(Ev.Clear); }
                else { FailReason = "ノルマ未達成"; Events.Add(Ev.Fail); }
            }
        }

        void UpdateExpansion()
        {
            if (Islands) return;
            int now = -1;
            for (int i = 0; i < Def.Expansions.Count; i++)
            {
                var (s, d) = Def.Expansions[i];
                if (Time >= s && Time < s + d) now = i;
            }
            if (now == expansionIndex) return;
            if (now >= 0 && !Expanded)
            {
                Expanded = true;
                for (int r = -1; r <= N; r++)
                    for (int c = -1; c <= N; c++)
                        if (IsRing(r, c)) Set(r, c, rng.NextDouble() < 0.45);
                Events.Add(Ev.Expand);
            }
            else if (now < 0 && Expanded)
            {
                for (int r = -1; r <= N; r++)
                    for (int c = -1; c <= N; c++)
                        if (IsRing(r, c)) Set(r, c, false);
                Expanded = false;
                HeartR = Math.Max(0, Math.Min(N - 1, HeartR));
                HeartC = Math.Max(0, Math.Min(N - 1, HeartC));
                Events.Add(Ev.Shrink);
                CheckMatch();
            }
            expansionIndex = now;
        }

        // 敵が回る道（盤を囲む四角）
        float PathInset => 1.4f;
        float PathLength => 4 * (BoardWidth + 2 * PathInset);

        (float x, float y) PointOnPath(float t)
        {
            float side = BoardWidth + 2 * PathInset;
            t %= 4 * side; if (t < 0) t += 4 * side;
            float x0 = -PathInset, y0 = -PathInset;
            if (t < side) return (x0 + t, y0);
            t -= side;
            if (t < side) return (x0 + side, y0 + t);
            t -= side;
            if (t < side) return (x0 + side - t, y0 + side);
            t -= side;
            return (x0, y0 + side - t);
        }

        void UpdateEnemies(float dt)
        {
            for (int i = 0; i < Enemies.Count; i++)
            {
                var en = Enemies[i];
                var def = Def.Emitters[i];
                float until = nextFire[i] - Time;
                en.Charge = Time < def.Start - 0.6f ? 0 : Clamp01(1 - until / 0.6f);
                if (en.Kind == EnemyKind.Blob && en.Visible)
                {
                    en.PathT += dt * 0.7f * (i % 2 == 0 ? 1 : -1);
                    (en.X, en.Y) = PointOnPath(en.PathT);
                }
                else if (en.Kind == EnemyKind.Megaphone)
                {
                    // 撃つ 0.9 秒前に、どの行に撃つか決めて姿を見せる
                    if (!en.Armed && until <= 0.9f && Time >= def.Start - 0.9f)
                    {
                        en.Armed = true;
                        en.Visible = true;
                        bool left = rng.Next(2) == 0;
                        int row = rng.NextDouble() < 0.65 ? HeartR : rng.Next(Min, Max + 1);
                        en.Y = CellCenter(Math.Max(0, Math.Min(N - 1, row)), 0).y;
                        if (row < 0) en.Y = -0.5f; else if (row >= N) en.Y = CellCenter(N - 1, 0).y + 1;
                        en.X = left ? Left + 0.4f : Right - 0.4f;
                    }
                }
            }
        }

        void Fire()
        {
            for (int i = 0; i < Def.Emitters.Count; i++)
            {
                var def = Def.Emitters[i];
                if (Time < nextFire[i]) continue;
                var en = Enemies[i];
                float sp = def.Speed;
                switch (def.Pattern)
                {
                    case Pattern.Aim:
                        Shoot(BulletKind.Dot, en.X, en.Y, Aim(en.X, en.Y, sp), 0.15f);
                        break;
                    case Pattern.FlipAim:
                        Shoot(BulletKind.Flip, en.X, en.Y, Aim(en.X, en.Y, sp), 0.2f);
                        break;
                    case Pattern.Ring:
                    {
                        int n = Math.Max(4, def.Count);
                        double off = rng.NextDouble() * Math.PI * 2;
                        for (int k = 0; k < n; k++)
                        {
                            double a = off + Math.PI * 2 * k / n;
                            Shoot(BulletKind.Dot, en.X, en.Y, ((float)Math.Cos(a) * sp, (float)Math.Sin(a) * sp), 0.15f);
                        }
                        break;
                    }
                    case Pattern.Rain:
                    {
                        float x = Left + 0.5f + (float)rng.NextDouble() * (Right - Left - 1);
                        Shoot(BulletKind.Dot, x, Top, (((float)rng.NextDouble() - 0.5f) * 0.6f, sp), 0.15f);
                        break;
                    }
                    case Pattern.Word:
                    {
                        string w = Def.Words[rng.Next(Def.Words.Length)];
                        float dir = en.X < (Left + Right) / 2 ? 1 : -1;
                        Bullets.Add(new Bullet
                        {
                            Kind = BulletKind.Word, X = en.X, Y = en.Y, VX = dir * sp, VY = 0,
                            Text = w, HalfW = 0.27f * w.Length + 0.15f, HalfH = 0.3f,
                        });
                        en.Armed = false;
                        en.Visible = false;
                        break;
                    }
                    case Pattern.Wall:
                        FireWall(sp);
                        break;
                }
                Events.Add(Ev.Fire);
                nextFire[i] = Time + def.Interval * (0.85f + 0.3f * (float)rng.NextDouble());
            }
        }

        void FireWall(float sp)
        {
            // すき間は、ハートが入れる行・列の真ん中にする
            bool horizontal = rng.Next(2) == 0; // true: 左右から来る（縦の列）
            bool fromStart = rng.Next(2) == 0;
            int lane = rng.Next(Min, Max + 1);
            float gapCenter = horizontal ? CellCenter(lane, 0).y : CellCenter(0, lane).x;
            float from = horizontal ? Top : Left, to = horizontal ? Bottom : Right;
            for (float p = from; p <= to; p += 0.45f)
            {
                if (Math.Abs(p - gapCenter) < 0.75f) continue;
                if (horizontal)
                    Shoot(BulletKind.Dot, fromStart ? Left : Right, p, (fromStart ? sp : -sp, 0), 0.16f);
                else
                    Shoot(BulletKind.Dot, p, fromStart ? Top : Bottom, (0, fromStart ? sp : -sp), 0.16f);
            }
        }

        (float, float) Aim(float x, float y, float sp)
        {
            float dx = HeartX - x, dy = HeartY - y;
            float d = (float)Math.Sqrt(dx * dx + dy * dy);
            if (d < 0.001f) return (0, sp);
            return (dx / d * sp, dy / d * sp);
        }

        void Shoot(BulletKind k, float x, float y, (float vx, float vy) v, float r)
        {
            Bullets.Add(new Bullet { Kind = k, X = x, Y = y, VX = v.vx, VY = v.vy, R = r });
        }

        void MoveBullets(float dt)
        {
            for (int i = 0; i < Bullets.Count; i++)
            {
                var b = Bullets[i];
                b.X += b.VX * dt; b.Y += b.VY * dt;
                if (b.X < Left - 2 || b.X > Right + 2 || b.Y < Top - 2 || b.Y > Bottom + 2) { b.Dead = true; continue; }
                if (Over || Invuln > 0 || FlyLeft > 0 || !HitsHeart(b)) continue;
                b.Dead = true;
                if (b.Kind == BulletKind.Flip)
                {
                    for (int dr = -1; dr <= 1; dr++)
                        for (int dc = -1; dc <= 1; dc++)
                        {
                            if (dr == 0 && dc == 0) continue;
                            int r = HeartR + dr, c = HeartC + dc;
                            if (!InBoard(r, c)) continue;
                            if (Islands && Island(r, c) != Island(HeartR, HeartC)) continue;
                            Set(r, c, !Get(r, c));
                        }
                    Invuln = 0.5f;
                    Events.Add(Ev.FlipHit);
                    CheckMatch();
                }
                else
                {
                    Life--;
                    Invuln = InvulnTime;
                    Events.Add(Ev.Hit);
                    if (Life <= 0)
                    {
                        Over = true;
                        FailReason = "ライフがなくなった";
                        Events.Add(Ev.Fail);
                    }
                }
            }
            Bullets.RemoveAll(b => b.Dead);
        }

        bool HitsHeart(Bullet b)
        {
            if (b.Kind == BulletKind.Word)
            {
                float cx = Math.Max(b.X - b.HalfW, Math.Min(HeartX, b.X + b.HalfW));
                float cy = Math.Max(b.Y - b.HalfH, Math.Min(HeartY, b.Y + b.HalfH));
                float ddx = HeartX - cx, ddy = HeartY - cy;
                return ddx * ddx + ddy * ddy < HeartRadius * HeartRadius;
            }
            float dx = b.X - HeartX, dy = b.Y - HeartY, rr = b.R + HeartRadius;
            return dx * dx + dy * dy < rr * rr;
        }

        static float Clamp01(float v) => v < 0 ? 0 : v > 1 ? 1 : v;
    }
}
