using Dalamud.Plugin;
using Lumina.Excel;
using PlayerSync.Validation.Tmb;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace PlayerSync.Validation;

public static class FileValidation
{
    public static void Initialize(IDalamudPluginInterface? pluginInterface)
    {
        if (pluginInterface != null)
        {
            pluginInterface.Inject(new VfxEditor.Dalamud());
        }
    }

    /// <summary>
    /// Checks the given file for any validation messages.
    /// </summary>
    /// <param name="fileData">The contents of the file to check.</param>
    /// <param name="extension">The extension of the file, as a testing hint. Pass a blank string if unknown.</param>
    /// <param name="validatePath">A callback used to determine whether the given game path exists.</param>
    /// <returns>The validation messages.</returns>
    public static IEnumerable<ValidationMessage> ValidateFile(ExcelModule excelModule, byte[] fileData, string extension, Func<string, bool>? validatePath)
    {
        if (extension.Equals(".tmb", StringComparison.OrdinalIgnoreCase)
            || (fileData.Length >= sizeof(uint) && MemoryMarshal.Read<uint>(fileData) == TmbValidation.TmbMagic))
        {
            return TmbValidation.ValidateTmbFile(excelModule, fileData, validatePath);
        }
        else
        {
            return Enumerable.Empty<ValidationMessage>();
        }
    }
}
