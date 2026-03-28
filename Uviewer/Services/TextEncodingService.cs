using System;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Windows.Storage;

namespace Uviewer.Services
{
    public class TextEncodingService
    {
        public static Encoding GetTextEncoding(byte[] bytes, string encodingName)
        {
            switch (encodingName)
            {
                case "UTF-8": return Encoding.UTF8;
                case "EUC-KR": return Encoding.GetEncoding(949);
                case "Shift-JIS": return Encoding.GetEncoding(932);
                case "Johab": return Encoding.GetEncoding(1361);
                case "Auto":
                default:
                    return DetectEncoding(bytes);
            }
        }

        public static Encoding DetectEncoding(byte[] bytes)
        {
            // Simple logic: Check for BOM, then UTF8 validation, then fallback to SJIS/EUC-KR
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF) return Encoding.UTF8;
            if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE) return Encoding.Unicode;
            if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF) return Encoding.BigEndianUnicode;

            // Is valid UTF8?
            if (IsValidUtf8(bytes)) return Encoding.UTF8;

            // 0. HTML Meta Charset Check (Strongest for HTML)
            var htmlCharset = DetectHtmlCharset(bytes);
            if (htmlCharset != null) return htmlCharset;

            // 1. Heuristic Scoring Comparison
            int eucKrScore = GetEucKrScore(bytes);
            int sjisScore = GetSjisScore(bytes);
            int johabScore = GetJohabScore(bytes);

            // Winner takes all
            if (sjisScore > eucKrScore && sjisScore > johabScore && sjisScore > 0) return Encoding.GetEncoding(932);
            if (eucKrScore > sjisScore && eucKrScore > johabScore && eucKrScore > 0) return Encoding.GetEncoding(949);
            if (johabScore > sjisScore && johabScore > eucKrScore && johabScore > 0) return Encoding.GetEncoding(1361);

            // Default preference if scores match
            if (eucKrScore > 0 && eucKrScore >= sjisScore) return Encoding.GetEncoding(949);
            if (sjisScore > 0) return Encoding.GetEncoding(932);
            if (johabScore > 0) return Encoding.GetEncoding(1361);

            // 5. Try Johab (Korean Combination, CP1361) - heuristic fallback
            if (ContainsJohabPattern(bytes)) return Encoding.GetEncoding(1361);

            // 5. Default Fallbacks
            try { return Encoding.GetEncoding(51949); } catch { }
            try { return Encoding.GetEncoding(932); } catch { }

            return Encoding.Default;
        }

        private static int GetSjisScore(byte[] bytes)
        {
            int score = 0;
            int i = 0;
            int len = bytes.Length;

            while (i < len)
            {
                byte b = bytes[i];
                if (b < 0x80)
                {
                    i++;
                    continue;
                }
                if (b >= 0xA1 && b <= 0xDF)
                {
                    if (i + 1 < len && bytes[i + 1] < 0x80) score += 1;
                    i++;
                    continue;
                }
                if (i + 1 >= len) break;
                byte b2 = bytes[i + 1];

                if ((b >= 0x81 && b <= 0x9F) || (b >= 0xE0 && b <= 0xFC))
                {
                    bool validSecond = (b2 >= 0x40 && b2 <= 0x7E) || (b2 >= 0x80 && b2 <= 0xFC);
                    if (validSecond)
                    {
                        if (b == 0x82 || b == 0x83) score += 5;
                        else score += 1;
                        i += 2;
                        continue;
                    }
                }
                i++;
            }
            return score;
        }

        private static int GetEucKrScore(byte[] bytes)
        {
            int score = 0;
            int i = 0;
            int len = bytes.Length;

            while (i < len)
            {
                byte b1 = bytes[i];
                if (b1 < 0x80)
                {
                    i++;
                    continue;
                }
                if (i + 1 >= len) break;
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
            int len = bytes.Length;

            while (i < len)
            {
                byte b = bytes[i];
                if (b < 0x80)
                {
                    i++;
                    continue;
                }
                if (i + 1 >= len) break;
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

        private static bool ContainsJohabPattern(byte[] bytes)
        {
            int johabOnlyPairCount = 0;
            int i = 0;
            int len = bytes.Length;

            while (i < len)
            {
                byte b = bytes[i];
                if (b < 0x80)
                {
                    i++;
                    continue;
                }
                if (i + 1 >= len) break;
                byte b2 = bytes[i + 1];

                if (b >= 0x84 && b <= 0xD3)
                {
                    bool johabOnlySecond = (b2 >= 0x5B && b2 <= 0x60) || (b2 >= 0x7B && b2 <= 0x7E);
                    if (johabOnlySecond)
                    {
                        johabOnlyPairCount++;
                        i += 2;
                        if (johabOnlyPairCount >= 2) return true;
                        continue;
                    }
                }
                if (b >= 0x81)
                {
                    if ((b2 >= 0x41 && b2 <= 0x7E) || (b2 >= 0x81 && b2 <= 0xFE))
                    {
                        i += 2;
                        continue;
                    }
                }
                i++;
            }
            return false;
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
                if (string.Equals(charset, "shift_jis", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(charset, "sjis", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(charset, "x-sjis", StringComparison.OrdinalIgnoreCase))
                    return Encoding.GetEncoding(932);

                if (string.Equals(charset, "euc-kr", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(charset, "ks_c_5601-1987", StringComparison.OrdinalIgnoreCase))
                    return Encoding.GetEncoding(51949);

                return Encoding.GetEncoding(charset);
            }
            catch { return null; }
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
    }
}
