namespace Snap.Hutao.Remastered.FullTrust.Models;

public abstract class FullTrustResult
{
    public bool Succeeded { get; set; }

    public string? ErrorMessage { get; set; }
}
