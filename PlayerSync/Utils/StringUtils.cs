
namespace PlayerSync.Utils
{
    public static class StringUtils
    {
        public static string NormalizeHash(string hash)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(hash);

            return hash.Trim().ToUpperInvariant();
        }

        public static string NormalizePath(string path)
        {
            return Path.GetFullPath(path).Replace('/', '\\').ToLowerInvariant();
        }

        public static string NormalizeFilePathWithinFileStore(string path)
        {
            return string.Join("\\", path.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();
        }
    }
}
