using System;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Windows.Storage;

namespace Uviewer
{
    public sealed partial class MainWindow
    {

        private async Task<string> ReadTextFileWithEncodingAsync(StorageFile file)
        {
            // Read as buffer
            var buffer = await FileIO.ReadBufferAsync(file);
            using var dataReader = Windows.Storage.Streams.DataReader.FromBuffer(buffer);
            byte[] bytes = new byte[buffer.Length];
            dataReader.ReadBytes(bytes);

            // Detect Encoding
            Encoding encoding = GetTextEncoding(bytes);
            return encoding.GetString(bytes);
        }

        private Encoding DetectEncoding(byte[] bytes)
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
            // Compare scores for EUC-KR, SJIS, and Johab.
            // Johab needs to be in the competition because it overlaps with both (CP949 Ext & SJIS).
            int eucKrScore = GetEucKrScore(bytes);
            int sjisScore = GetSjisScore(bytes);
            int johabScore = GetJohabScore(bytes);

            // Winner takes all
            if (sjisScore > eucKrScore && sjisScore > johabScore && sjisScore > 0) return Encoding.GetEncoding(932);
            if (eucKrScore > sjisScore && eucKrScore > johabScore && eucKrScore > 0) return Encoding.GetEncoding(949);
            if (johabScore > sjisScore && johabScore > eucKrScore && johabScore > 0) return Encoding.GetEncoding(1361);

            // Default preference if scores match
            // Johab is rarest, so lowest priority in tie-break
            if (eucKrScore > 0 && eucKrScore >= sjisScore) return Encoding.GetEncoding(949);
            if (sjisScore > 0) return Encoding.GetEncoding(932);
            if (johabScore > 0) return Encoding.GetEncoding(1361);

            // 5. Try Johab (Korean Combination, CP1361) - heuristic fallback
            // (Redundant with scoring, but serves as final check)
            if (ContainsJohabPattern(bytes)) return Encoding.GetEncoding(1361);

            // 5. Default Fallbacks
            try { return Encoding.GetEncoding(51949); } catch { }
            try { return Encoding.GetEncoding(932); } catch { }

            return Encoding.Default;
        }

        private int GetSjisScore(byte[] bytes)
        {
            // Calculate a score for likelihood of SJIS.
            // +2 for Kana (strong signal)
            // +1 for Kanji

            int score = 0;
            int i = 0;
            int len = bytes.Length;

            while (i < len)
            {
                byte b = bytes[i];

                // ASCII - skip
                if (b < 0x80)
                {
                    i++;
                    continue;
                }

                // Half-width Katakana (0xA1-0xDF)
                // This overlaps with EUC-KR first byte.
                // But if followed by ASCII, it's a strong SJIS signal.
                if (b >= 0xA1 && b <= 0xDF)
                {
                    if (i + 1 < len && bytes[i + 1] < 0x80) score += 1;
                    i++;
                    continue;
                }

                // Need 2 bytes
                if (i + 1 >= len) break;

                byte b2 = bytes[i + 1];

                // SJIS First Byte: 0x81-0x9F, 0xE0-0xFC
                // Includes Level 1 Kanji (0x81-0x9F) and Level 2 (0xE0-0xFC)
                if ((b >= 0x81 && b <= 0x9F) || (b >= 0xE0 && b <= 0xFC))
                {
                    // Valid SJIS second byte: 0x40-0x7E or 0x80-0xFC
                    bool validSecond = (b2 >= 0x40 && b2 <= 0x7E) || (b2 >= 0x80 && b2 <= 0xFC);
                    if (validSecond)
                    {
                        // 0x82, 0x83 are Hiragana and Katakana - VERY strong signal for Japanese
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

        private int GetEucKrScore(byte[] bytes)
        {
            // Calculate a score for likelihood of EUC-KR.
            // +2 for Standard Hangul (0xB0-0xC8) - strong signal
            // +1 for Symbols or CP949 Extended

            int score = 0;
            int i = 0;
            int len = bytes.Length;

            while (i < len)
            {
                byte b1 = bytes[i];

                // ASCII - skip
                if (b1 < 0x80)
                {
                    i++;
                    continue;
                }

                // Need 2 bytes
                if (i + 1 >= len) break;

                byte b2 = bytes[i + 1];

                // Standard EUC-KR Hangul: 0xB0-0xC8 first, 0xA1-0xFE second
                // This is the strongest signal for Korean text.
                if (b1 >= 0xB0 && b1 <= 0xC8 && b2 >= 0xA1 && b2 <= 0xFE)
                {
                    score += 2;
                    i += 2;
                    continue;
                }

                // NOTE: We specifically DO NOT count CP949 Extended Range (0x81-0xA0) here.
                // NOTE: We ALSO removed 0xA1-0xAF (Symbols) because it overlaps with SJIS Half-width Katakana.
                // This makes EUC-KR detection purely based on Standard Hangul (0xB0+), which is safest.

                // NOTE: We specifically DO NOT count CP949 Extended Range (0x81-0xA0) here.
                // Reason: This range completely overlaps with SJIS (Lev 1 Kanji & Kana) and Johab.

                i++;
            }

            return score;
        }

        private int GetJohabScore(byte[] bytes)
        {
            // Calculate a score for likelihood of Johab.
            // +5 for Johab-ONLY second bytes (smoking gun)
            // +1 for valid Johab sequences

            int score = 0;
            int i = 0;
            int len = bytes.Length;

            while (i < len)
            {
                byte b = bytes[i];

                // ASCII - skip
                if (b < 0x80)
                {
                    i++;
                    continue;
                }

                // Need 2 bytes
                if (i + 1 >= len) break;

                byte b2 = bytes[i + 1];

                // Johab First Byte: 0x84-0xD3
                if (b >= 0x84 && b <= 0xD3)
                {
                    // Check for Johab-ONLY second byte ranges: 0x5B-0x60, 0x7B-0x7E
                    // These are NOT used in CP949 or standard SJIS
                    if ((b2 >= 0x5B && b2 <= 0x60) || (b2 >= 0x7B && b2 <= 0x7E))
                    {
                        score += 3; // Reduced from 5 to avoid false positives with SJIS Kanji
                        i += 2;
                        continue;
                    }

                    // Normal Johab second byte: 0x41-0x7E or 0x81-0xFE
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

        private bool ContainsJohabPattern(byte[] bytes)
        {
            // Check for Korean Johab (조합형) encoding patterns.
            // 
            // IMPORTANT: CP949 (Korean Windows codepage) also uses first bytes 0x81-0xA0
            // for extended Hangul characters! This overlaps with Johab.
            // 
            // Johab (CP1361):
            // - First byte: 0x84-0xD3
            // - Second byte: 0x41-0x7E, 0x81-0xFE
            // 
            // CP949 extended characters:
            // - First byte: 0x81-0xA0  
            // - Second byte: 0x41-0x5A (A-Z), 0x61-0x7A (a-z), 0x81-0xFE
            // 
            // KEY DIFFERENCE: Johab uses 0x5B-0x60 and 0x7B-0x7E as second bytes,
            // but CP949 does NOT use these ranges.
            // 
            // Strategy: Only detect Johab if we find second bytes in 0x5B-0x60 or 0x7B-0x7E
            // (ranges used by Johab but not by CP949)

            int johabOnlyPairCount = 0;
            int i = 0;
            int len = bytes.Length;

            while (i < len)
            {
                byte b = bytes[i];

                // ASCII byte - just skip and continue
                if (b < 0x80)
                {
                    i++;
                    continue;
                }

                // Need at least 2 bytes for multibyte character
                if (i + 1 >= len) break;

                byte b2 = bytes[i + 1];

                // Check for Johab-ONLY patterns:
                // First byte 0x84-0xD3, second byte in ranges CP949 doesn't use
                if (b >= 0x84 && b <= 0xD3)
                {
                    // Johab-only second byte ranges: 0x5B-0x60, 0x7B-0x7E
                    // These are NOT used by CP949 extended characters
                    bool johabOnlySecond = (b2 >= 0x5B && b2 <= 0x60) || (b2 >= 0x7B && b2 <= 0x7E);
                    if (johabOnlySecond)
                    {
                        johabOnlyPairCount++;
                        i += 2;

                        // If we found enough Johab-only pairs, it's definitely Johab
                        if (johabOnlyPairCount >= 2) return true;
                        continue;
                    }
                }

                // For any high byte, skip as 2-byte sequence to maintain alignment
                if (b >= 0x81)
                {
                    if ((b2 >= 0x41 && b2 <= 0x7E) || (b2 >= 0x81 && b2 <= 0xFE))
                    {
                        i += 2;
                        continue;
                    }
                }

                // Unknown byte pattern, advance by 1
                i++;
            }

            return false;
        }

        private bool IsStrictJohab(byte[] bytes)
        {
            // Detect Johab encoding by looking for Johab-specific first byte patterns.
            // 
            // KEY INSIGHT: The ONLY reliable way to distinguish Johab from EUC-KR/CP949 is:
            // - Johab uses first bytes 0x84-0xA0 (EUC-KR uses 0xA1+)
            // 
            // We DON'T count second byte patterns because they can cause false positives
            // due to byte alignment issues.

            int i = 0;
            int len = bytes.Length;
            int johabFirstByteCount = 0;  // Count of first bytes in 0x84-0xA0 range
            int totalMultibyte = 0;

            while (i < len)
            {
                byte b = bytes[i];

                // ASCII - skip
                if (b < 0x80)
                {
                    i++;
                    continue;
                }

                // Single byte in 0x80-0x83 range - skip
                if (b >= 0x80 && b <= 0x83)
                {
                    i++;
                    continue;
                }

                // Need 2 bytes for multibyte
                if (i + 1 >= len)
                {
                    i++;
                    continue;
                }

                byte b2 = bytes[i + 1];
                totalMultibyte++;

                // First byte 0x84-0xA0: Johab-only range (EUC-KR doesn't use this for first byte)
                if (b >= 0x84 && b <= 0xA0)
                {
                    // Valid Johab second byte: 0x41-0x7E or 0x81-0xFE
                    if ((b2 >= 0x41 && b2 <= 0x7E) || (b2 >= 0x81 && b2 <= 0xFE))
                    {
                        johabFirstByteCount++;
                        i += 2;
                        continue;
                    }
                    // Invalid second byte - skip as single
                    i++;
                    continue;
                }

                // First byte 0xA1-0xFE: Could be EUC-KR or Johab - skip as 2-byte
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

                // Other bytes - skip
                i++;
            }

            // VERY STRICT criteria: Only detect as Johab if we have MANY Johab-only first bytes
            // This is extremely conservative to avoid false positives with EUC-KR/CP949 files
            // Since IsStrictEucKr runs before this, we only get here if EUC-KR validation failed
            // Require at least 50 Johab-specific first bytes, or 15% of total multibyte chars
            if (johabFirstByteCount >= 50)
                return true;
            if (totalMultibyte >= 100 && johabFirstByteCount >= (totalMultibyte * 15 / 100)) // At least 15%
                return true;

            return false;
        }

        private Encoding? DetectHtmlCharset(byte[] bytes)
        {
            try
            {
                // Read head of file (first 2KB) as ASCII string to find meta tags
                int len = Math.Min(bytes.Length, 2048);
                string head = Encoding.ASCII.GetString(bytes, 0, len);

                // Regex for <meta charset="...">
                var match = Regex.Match(head, @"<meta\s+charset=[""']?([a-zA-Z0-9-_]+)[""']?", RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    string charset = match.Groups[1].Value;
                    return GetEncodingFromCharset(charset);
                }

                // Regex for <meta http-equiv="Content-Type" content="...; charset=...">
                match = Regex.Match(head, @"charset\s*=\s*([a-zA-Z0-9-_]+)", RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    string charset = match.Groups[1].Value;
                    return GetEncodingFromCharset(charset);
                }
            }
            catch { }
            return null;
        }

        private Encoding? GetEncodingFromCharset(string charset)
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

        private bool IsStrictEucKr(byte[] bytes)
        {
            int i = 0;
            int len = bytes.Length;
            while (i < len)
            {
                byte b = bytes[i];
                if (b < 0x80)
                {
                    // ASCII
                    i++;
                }
                else
                {
                    // EUC-KR 2-byte char: 1st [0xA1-0xFE], 2nd [0xA1-0xFE]
                    // (Actually standard usually starts around 0xB0 for Hangul, but spec allows A1+)
                    if (i + 1 >= len) return false; // Incomplete
                    byte b2 = bytes[i + 1];
                    if (b >= 0xA1 && b <= 0xFE && b2 >= 0xA1 && b2 <= 0xFE)
                    {
                        i += 2;
                    }
                    else
                    {
                        return false; // Invalid EUC-KR sequence
                    }
                }
            }
            return true;
        }

        private bool IsStrictSjis(byte[] bytes)
        {
            int i = 0;
            int len = bytes.Length;
            while (i < len)
            {
                byte b = bytes[i];
                if (b < 0x80)
                {
                    // ASCII
                    i++;
                }
                else if (b >= 0xA1 && b <= 0xDF)
                {
                    // Half-width Katakana
                    i++;
                }
                else
                {
                    // SJIS 2-byte: 1st [0x81-0x9F, 0xE0-0xFC]
                    if ((b >= 0x81 && b <= 0x9F) || (b >= 0xE0 && b <= 0xFC))
                    {
                        if (i + 1 >= len) return false;
                        byte b2 = bytes[i + 1];
                        // 2nd [0x40-0x7E, 0x80-0xFC]
                        if ((b2 >= 0x40 && b2 <= 0x7E) || (b2 >= 0x80 && b2 <= 0xFC))
                        {
                            i += 2;
                        }
                        else
                        {
                            return false;
                        }
                    }
                    else
                    {
                        return false;
                    }
                }
            }
            return true;
        }

        private bool IsValidUtf8(byte[] bytes)
        {
            try
            {
                // Using a strict decoder to check for invalid sequences
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

        private string ParseHtml(string html)
        {
            // Very basic stripper
            // 1. Remove script/style
            string noScript = Regex.Replace(html, @"<script[^>]*>[\s\S]*?</script>", "", RegexOptions.IgnoreCase);
            string noStyle = Regex.Replace(noScript, @"<style[^>]*>[\s\S]*?</style>", "", RegexOptions.IgnoreCase);

            // 2. Strip tags
            string textOnly = Regex.Replace(noStyle, @"<[^>]+>", "\n"); // Replace tags with newlines

            // 3. Decode HTML entities
            textOnly = System.Net.WebUtility.HtmlDecode(textOnly);

            // 4. Remove excessive newlines
            return Regex.Replace(textOnly, @"\n\s+\n", "\n\n");
        }



    }
}