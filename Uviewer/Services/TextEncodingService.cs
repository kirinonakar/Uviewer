using System;
using System.Text;
using System.Text.RegularExpressions;

namespace Uviewer.Services
{
    public class TextEncodingService
    {
        static TextEncodingService()
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        }

        public static Encoding GetTextEncoding(byte[] bytes, string encodingName)
        {
            switch (encodingName)
            {
                case "UTF-8": return Encoding.UTF8;
                case "EUC-KR": return Encoding.GetEncoding(949);
                case "Shift-JIS": return Encoding.GetEncoding(932);
                case "Johab": return Encoding.GetEncoding(1361);
                case "JIS":
                case "ISO-2022-JP": return Encoding.GetEncoding(50220);
                case "GB18030": return Encoding.GetEncoding(54936);
                case "GBK": return Encoding.GetEncoding(936);
                case "Big5": return Encoding.GetEncoding(950);
                case "Auto":
                default:
                    return DetectEncoding(bytes);
            }
        }

        public static Encoding DetectEncoding(byte[] bytes)
        {
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF) return new UTF8Encoding(true);
            if (bytes.Length >= 4 && bytes[0] == 0xFF && bytes[1] == 0xFE && bytes[2] == 0 && bytes[3] == 0) return Encoding.UTF32;
            if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE) return Encoding.Unicode;
            if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF) return Encoding.BigEndianUnicode;

            var htmlCharset = DetectHtmlCharset(bytes);
            if (htmlCharset != null) return htmlCharset;

            if (ContainsSampleJisEscapeSequences(bytes)) return Encoding.GetEncoding(50220);

            if (IsValidUtf8(bytes)) return new UTF8Encoding(false);

            int eucKrScore = GetEucKrScore(bytes);
            int sjisScore = GetSjisScore(bytes);
            int johabScore = GetJohabScore(bytes);
            int johabMarkerPairCount = CountJohabMarkerPairs(bytes);
            int gbkScore = GetGbkScore(bytes);
            int gb18030Score = GetGb18030Score(bytes);
            int big5Score = GetBig5Score(bytes);

            int maxScore = Math.Max(sjisScore, Math.Max(eucKrScore, Math.Max(johabScore, Math.Max(gbkScore, Math.Max(gb18030Score, big5Score)))));

            if (maxScore > 0)
            {
                if (maxScore == eucKrScore) return Encoding.GetEncoding(949);
                if (maxScore == sjisScore) return Encoding.GetEncoding(932);
                bool gbkFamilyScoreIsWinning = maxScore == gbkScore || maxScore == gb18030Score;
                if (gbkFamilyScoreIsWinning &&
                    ShouldPreferJohabOverChineseScores(bytes.Length, johabScore, johabMarkerPairCount, gbkScore, gb18030Score))
                {
                    return Encoding.GetEncoding(1361);
                }

                bool chineseScoreIsWinning = gbkFamilyScoreIsWinning || maxScore == big5Score;
                if (chineseScoreIsWinning &&
                    ShouldPreferEucKrOverChineseScores(bytes, eucKrScore))
                {
                    return Encoding.GetEncoding(949);
                }

                if (maxScore == gb18030Score && gb18030Score > gbkScore) return Encoding.GetEncoding(54936);
                if (maxScore == gbkScore) return Encoding.GetEncoding(936);
                if (maxScore == big5Score) return Encoding.GetEncoding(950);
                if (maxScore == johabScore) return Encoding.GetEncoding(1361);
            }

            if (johabScore > 0 || johabMarkerPairCount >= 2) return Encoding.GetEncoding(1361);

            return new UTF8Encoding(false);
        }

        private static int GetSjisScore(byte[] bytes)
        {
            int score = 0;
            int i = 0;
            while (i < bytes.Length)
            {
                byte b = bytes[i];
                if (b < 0x80)
                {
                    i++;
                    continue;
                }

                if (b >= 0xA1 && b <= 0xDF)
                {
                    if (i + 1 < bytes.Length && bytes[i + 1] < 0x80) score += 1;
                    i++;
                    continue;
                }

                if (i + 1 >= bytes.Length) break;
                byte b2 = bytes[i + 1];
                if (((b >= 0x81 && b <= 0x9F) || (b >= 0xE0 && b <= 0xFC)) &&
                    ((b2 >= 0x40 && b2 <= 0x7E) || (b2 >= 0x80 && b2 <= 0xFC)))
                {
                    score += (b == 0x82 || b == 0x83) ? 5 : 1;
                    i += 2;
                    continue;
                }

                i++;
            }

            return score;
        }

        private static int GetEucKrScore(byte[] bytes)
        {
            int score = 0;
            int i = 0;
            while (i < bytes.Length)
            {
                byte b1 = bytes[i];
                if (b1 < 0x80)
                {
                    i++;
                    continue;
                }

                if (i + 1 >= bytes.Length) break;
                byte b2 = bytes[i + 1];
                if (b1 >= 0xB0 && b1 <= 0xC8 && b2 >= 0xA1 && b2 <= 0xFE)
                {
                    score += 2;
                    i += 2;
                    continue;
                }

                i++;
            }

            return score;
        }

        private static int GetJohabScore(byte[] bytes)
        {
            int score = 0;
            int i = 0;
            while (i < bytes.Length)
            {
                byte b = bytes[i];
                if (b < 0x80)
                {
                    i++;
                    continue;
                }

                if (i + 1 >= bytes.Length) break;
                byte b2 = bytes[i + 1];
                if (b >= 0x84 && b <= 0xD3)
                {
                    if ((b2 >= 0x5B && b2 <= 0x60) || (b2 >= 0x7B && b2 <= 0x7E))
                    {
                        score += 3;
                        i += 2;
                        continue;
                    }

                    if ((b2 >= 0x41 && b2 <= 0x7E) || (b2 >= 0x81 && b2 <= 0xFE))
                    {
                        score += 1;
                        i += 2;
                        continue;
                    }
                }

                i++;
            }

            return score;
        }

        private static int CountJohabMarkerPairs(byte[] bytes)
        {
            int johabOnlyPairCount = 0;
            int i = 0;
            while (i < bytes.Length)
            {
                byte b = bytes[i];
                if (b < 0x80)
                {
                    i++;
                    continue;
                }

                if (i + 1 >= bytes.Length) break;
                byte b2 = bytes[i + 1];
                if (b >= 0x84 && b <= 0xD3)
                {
                    bool johabOnlySecond = (b2 >= 0x5B && b2 <= 0x60) || (b2 >= 0x7B && b2 <= 0x7E);
                    if (johabOnlySecond)
                    {
                        johabOnlyPairCount++;
                        i += 2;
                        continue;
                    }

                    if ((b2 >= 0x41 && b2 <= 0x7E) || (b2 >= 0x81 && b2 <= 0xFE))
                    {
                        i += 2;
                        continue;
                    }
                }

                i++;
            }

            return johabOnlyPairCount;
        }

        private static bool ShouldPreferJohabOverChineseScores(
            int byteCount,
            int johabScore,
            int johabMarkerPairCount,
            int gbkScore,
            int gb18030Score)
        {
            int requiredMarkerPairs = byteCount < 1024 ? 1 : byteCount >= 16 * 1024 ? 8 : 2;
            if (johabScore <= 0 || johabMarkerPairCount < requiredMarkerPairs)
            {
                return false;
            }

            int chineseScore = Math.Max(gbkScore, gb18030Score);
            return chineseScore <= 0 || (long)johabScore * 4 >= (long)chineseScore * 3;
        }

        private static bool ShouldPreferEucKrOverChineseScores(byte[] bytes, int eucKrScore)
        {
            if (eucKrScore <= 0)
            {
                return false;
            }

            TextScriptProfile profile = GetTextScriptProfile(bytes, Encoding.GetEncoding(949));
            int requiredHangulCount = bytes.Length < 1024 ? 8 : 32;
            if (profile.HangulCount < requiredHangulCount)
            {
                return false;
            }

            if (profile.CjkCount * 3 > profile.HangulCount)
            {
                return false;
            }

            return profile.BadCharacterCount <= Math.Max(2, profile.HangulCount / 6);
        }

        private static TextScriptProfile GetTextScriptProfile(byte[] bytes, Encoding encoding)
        {
            const int sampleLimit = 128 * 1024;
            int sampleLength = Math.Min(bytes.Length, sampleLimit);
            string text = encoding.GetString(bytes, 0, sampleLength);

            int hangulCount = 0;
            int cjkCount = 0;
            int badCharacterCount = 0;
            foreach (char ch in text)
            {
                if (IsHangul(ch))
                {
                    hangulCount++;
                }
                else if (IsCjk(ch))
                {
                    cjkCount++;
                }
                else if (ch == '\uFFFD' || ch == '?')
                {
                    badCharacterCount++;
                }
            }

            return new TextScriptProfile(hangulCount, cjkCount, badCharacterCount);
        }

        private static bool IsHangul(char ch)
        {
            return (ch >= '\uAC00' && ch <= '\uD7A3') ||
                   (ch >= '\u1100' && ch <= '\u11FF') ||
                   (ch >= '\u3130' && ch <= '\u318F');
        }

        private static bool IsCjk(char ch)
        {
            return (ch >= '\u4E00' && ch <= '\u9FFF') ||
                   (ch >= '\u3400' && ch <= '\u4DBF');
        }

        private readonly struct TextScriptProfile
        {
            public TextScriptProfile(int hangulCount, int cjkCount, int badCharacterCount)
            {
                HangulCount = hangulCount;
                CjkCount = cjkCount;
                BadCharacterCount = badCharacterCount;
            }

            public int HangulCount { get; }

            public int CjkCount { get; }

            public int BadCharacterCount { get; }
        }

        private static Encoding? DetectHtmlCharset(byte[] bytes)
        {
            try
            {
                int len = Math.Min(bytes.Length, 2048);
                string head = Encoding.ASCII.GetString(bytes, 0, len);

                var match = Regex.Match(head, @"<meta\s+charset=[""']?([a-zA-Z0-9-_]+)[""']?", RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    return GetEncodingFromCharset(match.Groups[1].Value);
                }

                match = Regex.Match(head, @"charset\s*=\s*([a-zA-Z0-9-_]+)", RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    return GetEncodingFromCharset(match.Groups[1].Value);
                }
            }
            catch { }

            return null;
        }

        private static Encoding? GetEncodingFromCharset(string charset)
        {
            try
            {
                if (charset.Equals("shift_jis", StringComparison.OrdinalIgnoreCase) ||
                    charset.Equals("sjis", StringComparison.OrdinalIgnoreCase) ||
                    charset.Equals("x-sjis", StringComparison.OrdinalIgnoreCase))
                {
                    return Encoding.GetEncoding(932);
                }

                if (charset.Equals("euc-kr", StringComparison.OrdinalIgnoreCase) ||
                    charset.Equals("ks_c_5601-1987", StringComparison.OrdinalIgnoreCase))
                {
                    return Encoding.GetEncoding(949);
                }

                if (IsJisCharset(charset))
                {
                    return null;
                }

                return Encoding.GetEncoding(charset);
            }
            catch
            {
                return null;
            }
        }

        private static bool IsJisCharset(string charset)
        {
            return charset.Equals("iso-2022-jp", StringComparison.OrdinalIgnoreCase) ||
                   charset.Equals("csISO2022JP", StringComparison.OrdinalIgnoreCase) ||
                   charset.Equals("iso-2022-jp-1", StringComparison.OrdinalIgnoreCase) ||
                   charset.Equals("iso-2022-jp-2", StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsValidUtf8(byte[] bytes)
        {
            try
            {
                var decoder = Encoding.UTF8.GetDecoder();
                decoder.Fallback = new DecoderExceptionFallback();
                char[] chars = new char[decoder.GetCharCount(bytes, 0, bytes.Length)];
                decoder.GetChars(bytes, 0, bytes.Length, chars, 0);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool ContainsSampleJisEscapeSequences(byte[] bytes)
        {
            int len = bytes.Length;
            for (int i = 0; i < len - 2; i++)
            {
                if (bytes[i] == 0x1B) // ESC
                {
                    byte b1 = bytes[i + 1];
                    byte b2 = bytes[i + 2];
                    if ((b1 == 0x24 && (b2 == 0x40 || b2 == 0x42)) ||
                        (b1 == 0x28 && (b2 == 0x42 || b2 == 0x4A || b2 == 0x49)))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private static int GetGbkScore(byte[] bytes)
        {
            int score = 0;
            int i = 0;
            while (i < bytes.Length)
            {
                byte b1 = bytes[i];
                if (b1 < 0x80)
                {
                    i++;
                    continue;
                }

                if (i + 1 >= bytes.Length) break;
                byte b2 = bytes[i + 1];
                if (b1 >= 0x81 && b1 <= 0xFE && ((b2 >= 0x40 && b2 <= 0x7E) || (b2 >= 0x80 && b2 <= 0xFE)))
                {
                    if (b1 >= 0xB0 && b1 <= 0xF7 && b2 >= 0xA1 && b2 <= 0xFE)
                    {
                        score += 2;
                    }
                    else
                    {
                        score += 1;
                    }
                    i += 2;
                    continue;
                }

                i++;
            }

            return score;
        }

        private static int GetGb18030Score(byte[] bytes)
        {
            int score = 0;
            int i = 0;
            while (i < bytes.Length)
            {
                byte b1 = bytes[i];
                if (b1 < 0x80)
                {
                    i++;
                    continue;
                }

                if (i + 3 < bytes.Length)
                {
                    byte b2 = bytes[i + 1];
                    byte b3 = bytes[i + 2];
                    byte b4 = bytes[i + 3];
                    if (b1 >= 0x81 && b1 <= 0xFE &&
                        b2 >= 0x30 && b2 <= 0x39 &&
                        b3 >= 0x81 && b3 <= 0xFE &&
                        b4 >= 0x30 && b4 <= 0x39)
                    {
                        score += 8;
                        i += 4;
                        continue;
                    }
                }

                if (i + 1 >= bytes.Length) break;
                byte tb2 = bytes[i + 1];
                if (b1 >= 0x81 && b1 <= 0xFE && ((tb2 >= 0x40 && tb2 <= 0x7E) || (tb2 >= 0x80 && tb2 <= 0xFE)))
                {
                    if (b1 >= 0xB0 && b1 <= 0xF7 && tb2 >= 0xA1 && tb2 <= 0xFE)
                    {
                        score += 2;
                    }
                    else
                    {
                        score += 1;
                    }
                    i += 2;
                    continue;
                }

                i++;
            }

            return score;
        }

        private static int GetBig5Score(byte[] bytes)
        {
            int score = 0;
            int i = 0;
            while (i < bytes.Length)
            {
                byte b1 = bytes[i];
                if (b1 < 0x80)
                {
                    i++;
                    continue;
                }

                if (i + 1 >= bytes.Length) break;
                byte b2 = bytes[i + 1];
                if (b1 >= 0xA1 && b1 <= 0xF9 && ((b2 >= 0x40 && b2 <= 0x7E) || (b2 >= 0xA1 && b2 <= 0xFE)))
                {
                    if ((b1 >= 0xA4 && b1 <= 0xC6) || (b1 >= 0xC9 && b1 <= 0xF9))
                    {
                        score += 2;
                    }
                    else
                    {
                        score += 1;
                    }
                    i += 2;
                    continue;
                }

                i++;
            }

            return score;
        }

        public static bool IsStrictEucKr(byte[] bytes)
        {
            int i = 0;
            int len = bytes.Length;
            while (i < len)
            {
                byte b = bytes[i];
                if (b < 0x80)
                {
                    i++;
                }
                else
                {
                    if (i + 1 >= len) return false;
                    byte b2 = bytes[i + 1];
                    if (b >= 0xA1 && b <= 0xFE && b2 >= 0xA1 && b2 <= 0xFE)
                    {
                        i += 2;
                    }
                    else
                    {
                        return false;
                    }
                }
            }
            return true;
        }

        public static bool IsStrictSjis(byte[] bytes)
        {
            int i = 0;
            int len = bytes.Length;
            while (i < len)
            {
                byte b = bytes[i];
                if (b < 0x80)
                {
                    i++;
                }
                else if (b >= 0xA1 && b <= 0xDF)
                {
                    i++;
                }
                else
                {
                    if ((b >= 0x81 && b <= 0x9F) || (b >= 0xE0 && b <= 0xFC))
                    {
                        if (i + 1 >= len) return false;
                        byte b2 = bytes[i + 1];
                        if ((b2 >= 0x40 && b2 <= 0x7E) || (b2 >= 0x80 && b2 <= 0xFC))
                        {
                            i += 2;
                        }
                        else return false;
                    }
                    else return false;
                }
            }
            return true;
        }

        public static bool IsStrictJohab(byte[] bytes)
        {
            int i = 0;
            int len = bytes.Length;
            int johabFirstByteCount = 0;
            int totalMultibyte = 0;

            while (i < len)
            {
                byte b = bytes[i];
                if (b < 0x80 || (b >= 0x80 && b <= 0x83))
                {
                    i++;
                    continue;
                }
                if (i + 1 >= len)
                {
                    i++;
                    continue;
                }
                byte b2 = bytes[i + 1];
                totalMultibyte++;

                if (b >= 0x84 && b <= 0xA0)
                {
                    if ((b2 >= 0x41 && b2 <= 0x7E) || (b2 >= 0x81 && b2 <= 0xFE))
                    {
                        johabFirstByteCount++;
                        i += 2;
                        continue;
                    }
                    i++;
                    continue;
                }

                if (b >= 0xA1 && b <= 0xFE)
                {
                    if ((b2 >= 0x41 && b2 <= 0xFE) && b2 != 0x7F)
                    {
                        i += 2;
                        continue;
                    }
                    i++;
                    continue;
                }
                i++;
            }

            if (johabFirstByteCount >= 50) return true;
            if (totalMultibyte >= 100 && johabFirstByteCount >= (totalMultibyte * 15 / 100)) return true;

            return false;
        }
    }
}
