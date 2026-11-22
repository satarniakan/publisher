namespace Publisher;

public sealed class ToolException(string message) : Exception(message);

public sealed class UserCancelException(string message) : Exception(message);

public sealed class AppConfig
{
    public int SchemaVersion { get; set; } = 1;

    public List<Profile> Profiles { get; set; } = new();
}

public sealed class Profile
{
    public string Name { get; set; } = "";
    public string GitUrl { get; set; } = "";
    public string Branch { get; set; } = "";
    public string Auth { get; set; } = "https"; // https | ssh | none
    public string GitUsername { get; set; } = "x-access-token";
    public string Token { get; set; } = "";
    public string WorkingDir { get; set; } = "";
    public string BackupDir { get; set; } = "";
    public string TargetDir { get; set; } = "";
    public string PublishCommand { get; set; } = "";
    public int BackupRetention { get; set; } = 5;
    public int PublishTimeoutMinutes { get; set; } = 30;
    public string BeforeReplaceCommand { get; set; } = "";
    public string AfterReplaceCommand { get; set; } = "";

    public bool AutoUpdate { get; set; }
    public int AutoUpdateIntervalMinutes { get; set; } = 5;

    public string LastDeployedCommit { get; set; } = "";
    public DateTimeOffset? LastDeployedAt { get; set; }
    public string LastBackupPath { get; set; } = "";
}
