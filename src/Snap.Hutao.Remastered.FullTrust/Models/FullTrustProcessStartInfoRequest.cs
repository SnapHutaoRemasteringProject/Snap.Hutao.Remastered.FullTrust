using System.Text.Json.Serialization;

namespace Snap.Hutao.Remastered.FullTrust.Models;

public class FullTrustProcessStartInfoRequest
{
    [JsonPropertyName("applicationName")]
    public required string ApplicationName { get; set; }

    [JsonPropertyName("commandLine")]
    public required string CommandLine { get; set; }

    [JsonPropertyName("creationFlags")]
    public uint CreationFlags { get; set; }

    [JsonPropertyName("currentDirectory")]
    public required string CurrentDirectory { get; set; }
}
