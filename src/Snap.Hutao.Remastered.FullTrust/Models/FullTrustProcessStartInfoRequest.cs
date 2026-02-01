using System.Text.Json.Serialization;

namespace Snap.Hutao.Remastered.FullTrust.Models;

public class FullTrustProcessStartInfoRequest
{
    public required string ApplicationName { get; set; }
    public required string CommandLine { get; set; }
    public uint CreationFlags { get; set; }
    public required string CurrentDirectory { get; set; }
}
