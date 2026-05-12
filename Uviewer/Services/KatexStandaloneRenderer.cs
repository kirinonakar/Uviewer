using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Uviewer.Services
{
    public static class KatexStandaloneRenderer
    {
        private static readonly Dictionary<string, string> Commands = new()
        {
            ["alpha"] = "α", ["beta"] = "β", ["gamma"] = "γ", ["delta"] = "δ",
            ["epsilon"] = "ε", ["varepsilon"] = "ε", ["zeta"] = "ζ", ["eta"] = "η",
            ["theta"] = "θ", ["vartheta"] = "ϑ", ["iota"] = "ι", ["kappa"] = "κ",
            ["lambda"] = "λ", ["mu"] = "μ", ["nu"] = "ν", ["xi"] = "ξ",
            ["pi"] = "π", ["rho"] = "ρ", ["sigma"] = "σ", ["tau"] = "τ",
            ["upsilon"] = "υ", ["phi"] = "φ", ["varphi"] = "ϕ", ["chi"] = "χ",
            ["psi"] = "ψ", ["omega"] = "ω", ["Gamma"] = "Γ", ["Delta"] = "Δ",
            ["Theta"] = "Θ", ["Lambda"] = "Λ", ["Xi"] = "Ξ", ["Pi"] = "Π",
            ["Sigma"] = "Σ", ["Phi"] = "Φ", ["Psi"] = "Ψ", ["Omega"] = "Ω",
            ["times"] = "×", ["cdot"] = "·", ["pm"] = "±", ["mp"] = "∓",
            ["le"] = "≤", ["leq"] = "≤", ["ge"] = "≥", ["geq"] = "≥",
            ["neq"] = "≠", ["approx"] = "≈", ["sim"] = "∼", ["infty"] = "∞",
            ["sum"] = "∑", ["prod"] = "∏", ["int"] = "∫", ["oint"] = "∮",
            ["partial"] = "∂", ["nabla"] = "∇", ["sqrt"] = "√",
            ["rightarrow"] = "→", ["to"] = "→", ["leftarrow"] = "←",
            ["leftrightarrow"] = "↔", ["Rightarrow"] = "⇒", ["Leftarrow"] = "⇐",
            ["in"] = "∈", ["notin"] = "∉", ["subset"] = "⊂", ["subseteq"] = "⊆",
            ["cup"] = "∪", ["cap"] = "∩", ["forall"] = "∀", ["exists"] = "∃",
            ["land"] = "∧", ["lor"] = "∨", ["neg"] = "¬", ["div"] = "÷"
        };

        private static readonly Dictionary<char, char> Superscripts = new()
        {
            ['0'] = '⁰', ['1'] = '¹', ['2'] = '²', ['3'] = '³', ['4'] = '⁴',
            ['5'] = '⁵', ['6'] = '⁶', ['7'] = '⁷', ['8'] = '⁸', ['9'] = '⁹',
            ['+'] = '⁺', ['-'] = '⁻', ['='] = '⁼', ['('] = '⁽', [')'] = '⁾',
            ['n'] = 'ⁿ', ['i'] = 'ⁱ'
        };

        private static readonly Dictionary<char, char> Subscripts = new()
        {
            ['0'] = '₀', ['1'] = '₁', ['2'] = '₂', ['3'] = '₃', ['4'] = '₄',
            ['5'] = '₅', ['6'] = '₆', ['7'] = '₇', ['8'] = '₈', ['9'] = '₉',
            ['+'] = '₊', ['-'] = '₋', ['='] = '₌', ['('] = '₍', [')'] = '₎',
            ['a'] = 'ₐ', ['e'] = 'ₑ', ['h'] = 'ₕ', ['i'] = 'ᵢ', ['j'] = 'ⱼ',
            ['k'] = 'ₖ', ['l'] = 'ₗ', ['m'] = 'ₘ', ['n'] = 'ₙ', ['o'] = 'ₒ',
            ['p'] = 'ₚ', ['r'] = 'ᵣ', ['s'] = 'ₛ', ['t'] = 'ₜ', ['u'] = 'ᵤ',
            ['v'] = 'ᵥ', ['x'] = 'ₓ'
        };

        public static string RenderToText(string tex)
        {
            if (string.IsNullOrWhiteSpace(tex)) return "";

            string text = tex.Trim();
            text = text.Replace(@"\left", "").Replace(@"\right", "");
            text = ReplaceFractions(text);
            text = ReplaceSquareRoots(text);
            text = ReplaceScripts(text, '^', Superscripts);
            text = ReplaceScripts(text, '_', Subscripts);
            text = Regex.Replace(text, @"\\([A-Za-z]+)", m =>
                Commands.TryGetValue(m.Groups[1].Value, out var value) ? value : m.Groups[1].Value);
            text = text.Replace(@"\{", "{").Replace(@"\}", "}").Replace(@"\,", " ");
            text = text.Replace("{", "").Replace("}", "");
            return Regex.Replace(text, @"\s+", " ").Trim();
        }

        private static string ReplaceFractions(string text)
        {
            return Regex.Replace(text, @"\\frac\s*\{([^{}]+)\}\s*\{([^{}]+)\}", m =>
                $"{RenderToText(m.Groups[1].Value)}⁄{RenderToText(m.Groups[2].Value)}");
        }

        private static string ReplaceSquareRoots(string text)
        {
            return Regex.Replace(text, @"\\sqrt\s*(?:\[[^\]]+\])?\s*\{([^{}]+)\}", m =>
                $"√({RenderToText(m.Groups[1].Value)})");
        }

        private static string ReplaceScripts(string text, char marker, Dictionary<char, char> map)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] != marker || i + 1 >= text.Length)
                {
                    sb.Append(text[i]);
                    continue;
                }

                if (text[i + 1] == '{')
                {
                    int close = FindMatchingBrace(text, i + 1);
                    if (close > i + 1)
                    {
                        sb.Append(ConvertScript(text.Substring(i + 2, close - i - 2), map));
                        i = close;
                        continue;
                    }
                }

                sb.Append(ConvertScript(text[i + 1].ToString(), map));
                i++;
            }

            return sb.ToString();
        }

        private static string ConvertScript(string value, Dictionary<char, char> map)
        {
            var sb = new StringBuilder();
            foreach (char c in RenderToText(value))
            {
                sb.Append(map.TryGetValue(c, out var converted) ? converted : c);
            }
            return sb.ToString();
        }

        private static int FindMatchingBrace(string text, int openIndex)
        {
            int depth = 0;
            for (int i = openIndex; i < text.Length; i++)
            {
                if (text[i] == '{') depth++;
                else if (text[i] == '}')
                {
                    depth--;
                    if (depth == 0) return i;
                }
            }
            return -1;
        }
    }
}
