// モノクロハート — ノベルパートの文章を読む（Unity に依存しない C#）
// 文章は Assets/Resources/Story.txt に書く。書き方はそのファイルの先頭を見る。
using System.Collections.Generic;

namespace MonochroHeart
{
    public class StoryLine
    {
        public string Command; // "背景" など。セリフの行では null
        public string Speaker; // 話す人。地の文では ""
        public string Text;
    }

    public static class Story
    {
        public static Dictionary<string, List<StoryLine>> Parse(string text)
        {
            var result = new Dictionary<string, List<StoryLine>>();
            List<StoryLine> cur = null;
            foreach (var raw in text.Replace("\r", "").Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                if (line.StartsWith("[") && line.EndsWith("]"))
                {
                    cur = new List<StoryLine>();
                    result[line.Substring(1, line.Length - 2).Trim()] = cur;
                    continue;
                }
                if (cur == null) continue;
                if (line.StartsWith("@"))
                {
                    string body = line.Substring(1).Trim();
                    int sp = body.IndexOfAny(new[] { ' ', '　' });
                    cur.Add(new StoryLine
                    {
                        Command = sp < 0 ? body : body.Substring(0, sp),
                        Text = sp < 0 ? "" : body.Substring(sp + 1).Trim(),
                    });
                    continue;
                }
                int colon = line.IndexOf('：');
                if (colon < 0) colon = line.IndexOf(':');
                // 「：」が前の方（12文字以内）にあれば、その前を名前とみなす
                if (colon > 0 && colon <= 12)
                    cur.Add(new StoryLine { Speaker = line.Substring(0, colon).Trim(), Text = line.Substring(colon + 1).Trim() });
                else
                    cur.Add(new StoryLine { Speaker = "", Text = line });
            }
            return result;
        }
    }
}
