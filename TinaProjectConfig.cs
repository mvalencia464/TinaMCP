namespace TinaMcpServer;

/// <summary>
/// Configuration class holding the path to the TinaCMS project.
/// </summary>
public class TinaProjectConfig
{
    /// <summary>
    /// Gets or sets the absolute or relative path to the root directory 
    /// of the target TinaCMS project.
    /// </summary>
    public string RootPath { get; set; } = string.Empty;
} 