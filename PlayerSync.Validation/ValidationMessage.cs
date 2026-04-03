using System;
using System.Collections.Generic;
using System.Text;

namespace PlayerSync.Validation;

public enum MessageLevel
{
    Warning,
    Crash,
}

public record class ValidationMessage(string ID, string Title, string Description, MessageLevel Level);
