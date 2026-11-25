namespace SmartLMS.Infrastructure.Options;

public sealed class EmailOptions
{
    public const string SectionName = "Email";

    /// <summary>Off by default. Nothing is ever sent until you set this to true AND provide a server. No provider account exists in this project.</summary>
    public bool Enabled { get; set; }
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 587;
    public bool UseSsl { get; set; } = true;
    public string UserName { get; set; } = string.Empty;

    /// <summary>User-secrets / environment variable only (Email__Password).</summary>
    public string Password { get; set; } = string.Empty;
    public string FromAddress { get; set; } = string.Empty;
    public string FromName { get; set; } = "SmartLMS";

    /// <summary>Local development outbox: used only when Enabled is true AND Host is empty. Messages are written as .eml files into this folder instead of using SMTP. No server or account needed; open the newest file to read the code. Relative paths resolve against the API's working directory.</summary>
    public string PickupDirectory { get; set; } = string.Empty;
}

public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    /// <summary>Local folder for uploaded files. Relative paths resolve against the API's working directory. Git-ignored by default.</summary>
    public string Root { get; set; } = "storage";
    public long MaxLogoBytes { get; set; } = 2 * 1024 * 1024;
    public long MaxMediaBytes { get; set; } = 200L * 1024 * 1024;
    public long MaxResourceBytes { get; set; } = 25L * 1024 * 1024;
}
