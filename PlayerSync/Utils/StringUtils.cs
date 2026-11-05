
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
            ArgumentException.ThrowIfNullOrWhiteSpace(path);

            if (path.Contains('\0'))
            {
                throw new ArgumentException("Path contains a NUL character, how did this happen??", nameof(path));
            }

            return string.Join("\\", path.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();
        }
    }
}
