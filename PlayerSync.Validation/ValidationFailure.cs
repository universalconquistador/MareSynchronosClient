using System;
using System.Collections.Generic;
using System.Text;

namespace PlayerSync.Validation;

public record class ValidationFailure(string ID, string Title, string Description);
