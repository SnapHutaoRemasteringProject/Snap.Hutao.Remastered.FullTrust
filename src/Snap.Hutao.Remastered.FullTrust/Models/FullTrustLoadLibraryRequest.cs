namespace Snap.Hutao.Remastered.FullTrust.Models;

public sealed class FullTrustLoadLibraryRequest
{
    public required string LibraryName { get; set; }

    public required string LibraryPath { get; set; }

    public string? FunctionName { get; set; }

    public static FullTrustLoadLibraryRequest Create(string libraryName, string libraryPath)
    {
        return new FullTrustLoadLibraryRequest()
        {
            LibraryName = libraryName,
            LibraryPath = libraryPath,
        };
    }
}
