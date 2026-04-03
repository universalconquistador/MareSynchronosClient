using PlayerSync.Validation;
using PlayerSync.Validation.Tmb;

namespace PlayerSync.ValidationTest;

[TestClass]
public sealed class ValidationTests
{
    public record class ValidationTest(string TestFilePath, ValidationMessage? ExpectedFailure);

    private static readonly ValidationTest[] Tests =
    {
        new("TMB002A_Invalid_human_sp233.tmb", TmbValidation.TMB002A),
        new("TMB002B_Invalid_human_sp233.tmb", TmbValidation.TMB002B),
        new("TMB002_Valid_human_sp233.tmb", null),

        new("TMB010A_Invalid_CheerJumpRed.tmb", TmbValidation.TMB010A),
        new("TMB010B_Invalid_CheerJumpRed.tmb", TmbValidation.TMB010B),
        new("TMB010_Valid_CheerJumpRed.tmb", null),

        new("TMB012A_a364d4e229a34968950610cd550e59a97cf2dfeb.tmb", TmbValidation.TMB012A),
        new("TMB012B_a364d4e229a34968950610cd550e59a97cf2dfeb.tmb", TmbValidation.TMB012B),
        new("TMB012_Valid_human_sp233.tmb", null),

        new("TMB063A_Invalid_mon_sp001.tmb", TmbValidation.TMB063A),
        new("TMB063B_Invalid_mon_sp001.tmb", TmbValidation.TMB063B),
        new("TMB063_Valid_mon_sp001.tmb", null),
    };

    public static IEnumerable<object[]> GetTestData()
    {
        foreach (var test in Tests)
        {
            yield return [test];
        }
    }

    [TestMethod]
    [DynamicData(nameof(GetTestData))]
    public void TestValidation(ValidationTest test)
    {
        // For these tests we only check paths against the raw game data
        // This would report a valid TMB that references a penumbra-supplied file as invalid, which is correct for these tests
        const string gameDataPath = "D:\\Program Files\\FINAL FANTASY XIV - A Realm Reborn\\game\\sqpack\\";
        var gameData = new Lumina.GameData(gameDataPath);

        byte[] fileData = File.ReadAllBytes(Path.Combine("TestFiles", test.TestFilePath));
        var result = FileValidation.ValidateFile(gameData.Excel, fileData, Path.GetExtension(test.TestFilePath), path => path.Contains('/') && gameData.FileExists(path));

        Console.WriteLine($"{test.TestFilePath}: {string.Join(", ", result.Select(message => $"[{message.ID}]: {message.Title}"))}");
        if (test.ExpectedFailure != null)
        {
            Assert.Contains(test.ExpectedFailure, result);
        }
        else
        {
            Assert.IsEmpty(result.Where(message => message.Level == Validation.MessageLevel.Crash));
        }
    }
}
