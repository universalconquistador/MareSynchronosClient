using System;
using System.Collections.Generic;
using System.Text;

namespace PlayerSync.Validation;

public static class ValidationUtils
{
    // Yoinked from Penumbra's ResourceType. Might not be 100% exhaustive.
    private static HashSet<string> _validExtensions =
    [
        "aet",
        "amb",
        "atch",
        "atex",
        "avfx",
        "awt",
        "cmp",
        "dic",
        "eid",
        "envb",
        "eqdp",
        "eqp",
        "essb",
        "est",
        "evp",
        "exd",
        "exh",
        "exl",
        "fdt",
        "gfd",
        "ggd",
        "gmp",
        "gzd",
        "imc",
        "lcb",
        "lgb",
        "luab",
        "lvb",
        "mdl",
        "mlt",
        "mtrl",
        "obsb",
        "pap",
        "pbd",
        "pcb",
        "phyb",
        "plt",
        "scd",
        "sgb",
        "shcd",
        "shpk",
        "sklb",
        "skp",
        "stm",
        "svb",
        "tera",
        "tex",
        "tmb",
        "ugd",
        "uld",
        "waoe",
        "wtd",
        "bklb",
        "cutb",
        "eanb",
        "eslb",
        "fpeb",
        "kdb",
        "kdlb",
    ];

    /// <summary>
    /// Determines whether the given string can be parsed by FFXIV as a game path without crashing.
    /// </summary>
    /// <remarks>
    /// NOTE: This does not determine whether the game path actually exists.
    /// </remarks>
    /// <param name="path">The string to check for parseability.</param>
    /// <param name="validateExtension">Whether to validate that the path ends with one of the known extensions.</param>
    /// <returns>True if the game should be able to parse the given string as a game path without crashing.</returns>
    public static bool IsPathParseable(string path, bool validateExtension)
    {
        // Path must have only "a-zA-Z0-9_-. "
        for (int i = 0; i < path.Length; i++)
        {
            var ch = path[i];
            if (!char.IsAsciiLetterOrDigit(ch) && ch != '/' && ch != '_' && ch != '.' && ch != '-' && ch != ' ')
            {
                return false;
            }
        }

        // The game does a very abbreviated check to determine the category from the start of the path.
        // Some modders use other root directories that pass the same abbreviated check (e.g. 'audiox' seen used rather than 'audio')
        // and we want accurately reflect the fact that these will be successfully parsed and won't crash the game.
        if (path.Length < 3)
        {
            return false;
        }
        else
        {
            switch (Char.ToUpperInvariant(path[0]))
            {
                case 'C':
                    {
                        var char1 = Char.ToUpperInvariant(path[1]);
                        if (char1 != 'O' // common category
                            && char1 != 'U' // cut category
                            && char1 != 'H') // chara category
                        {
                            return false;
                        }
                        break;
                    }
                case 'B':
                    {
                        if (Char.ToUpperInvariant(path[2]) != 'C' // bgcommon category
                            && path[2] != '/') // bg category
                        {
                            return false;
                        }
                        break;
                    }
                case 'S':
                    {
                        var char1 = Char.ToUpperInvariant(path[1]);
                        if (char1 != 'H' // shader category
                            && char1 != 'O' // sound category
                            && char1 != 'Q') // sqpack_test category
                        {
                            return false;
                        }
                        break;
                    }
                case 'U':
                    {
                        var char2 = Char.ToUpperInvariant(path[2]);
                        if (char2 != '/' // ui category
                            && char2 != 'S') // ui_script category
                        {
                            return false;
                        }
                        break;
                    }
                case 'V': // vfx category
                case 'E': // exd category
                case 'G': // game_script category
                case 'M': // music category
                    break;
                default:
                    return false;
            }
        }

        if (validateExtension)
        {
            // Path must end with one of the known extensions, e.g. ".tex", ".tmb"
            var lastDot = path.LastIndexOf('.');
            if (lastDot == -1 || lastDot >= path.Length - 1)
            {
                return false;
            }
            var extension = path.Substring(lastDot + 1);
            if (!_validExtensions.Contains(extension))
            {
                return false;
            }
        }

        return true;
    }
}
