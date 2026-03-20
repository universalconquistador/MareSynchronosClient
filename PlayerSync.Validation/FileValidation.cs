using PlayerSync.Validation.Tmb;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Text;

namespace PlayerSync.Validation;

public static class FileValidation
{
    /// <summary>
    /// Checks whether the given file fails validation according to any validation rules.
    /// </summary>
    /// <param name="fileData">The contents of the file to check.</param>
    /// <param name="extension">The extension of the file, as a testing hint. Pass a blank string if unknown.</param>
    /// <param name="validatePath">A callback used to determine whether the given game path exists.</param>
    /// <param name="failure">The first validation failure that was found, if any.</param>
    /// <returns>True if the file failed validation, or false if there were no failures.</returns>
    public static bool IsInvalidFile(byte[] fileData, string extension, Func<string, bool>? validatePath, [NotNullWhen(true)] out ValidationFailure? failure)
    {
        if (extension.Equals(".tmb", StringComparison.OrdinalIgnoreCase)
            || (fileData.Length >= sizeof(uint) && MemoryMarshal.Read<uint>(fileData) == TmbValidation.Magic))
        {
            if (TmbValidation.IsInvalidFile(fileData, validatePath, out failure))
            {
                return true;
            }
        }

        failure = null;
        return false;
    }
}
