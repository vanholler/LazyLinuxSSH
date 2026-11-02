namespace LazyLinuxSSH.Infrastructure.Data;

public static class AppPaths
{
    public static string RootDirectory
    {
        get
        {
            var configured = Environment.GetEnvironmentVariable("LAZYLINUXSSH_DATA_DIR");
            var path = !string.IsNullOrWhiteSpace(configured)
                ? configured
                : Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "LazyLinuxSSH");
            Directory.CreateDirectory(path);
            return path;
        }
    }

    public static string DatabasePath => Path.Combine(RootDirectory, "connections.db");
    public static string LocalKeyPath => Path.Combine(RootDirectory, ".vault-key");
}
