using PlayerSync.Validation;

namespace PlayerSync.ValidationTest;

[TestClass]
public sealed class ValidationTests
{
    public record class ValidationTest(string TestFilePath, ValidationFailure? ExpectedFailure);

    private static readonly ValidationTest[] Tests =
    {
        new("TMB001_a364d4e229a34968950610cd550e59a97cf2dfeb.tmb", Validation.Tmb.TmbValidation.TMB002),
        new("TMB001_Valid_CheerJumpRed.tmb", null),
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
        // This would report a valid TMB that references a penumbra-supplied file as invalid, which is correct
        const string gameDataPath = "D:\\Program Files\\FINAL FANTASY XIV - A Realm Reborn\\game\\sqpack\\";
        var gameData = new Lumina.GameData(gameDataPath);

        byte[] fileData = File.ReadAllBytes(Path.Combine("TestFiles", test.TestFilePath));
        var result = FileValidation.IsInvalidFile(fileData, Path.GetExtension(test.TestFilePath), path => gameData.FileExists(path), out var failure);

        Assert.AreSame(test.ExpectedFailure, failure);
        Assert.AreEqual(test.ExpectedFailure != null, result);
    }
}
