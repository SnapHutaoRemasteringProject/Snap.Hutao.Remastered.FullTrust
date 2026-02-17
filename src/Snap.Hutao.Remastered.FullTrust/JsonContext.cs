using System.Text.Json.Serialization;
using Snap.Hutao.Remastered.FullTrust.Models;

namespace Snap.Hutao.Remastered.FullTrust;

[JsonSerializable(typeof(FullTrustProcessStartInfoRequest))]
[JsonSerializable(typeof(FullTrustLoadLibraryRequest))]
[JsonSerializable(typeof(FullTrustGenericResult))]
[JsonSerializable(typeof(FullTrustStartProcessResult))]
[JsonSourceGenerationOptions(
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = false)]
internal partial class AppJsonContext : JsonSerializerContext
{
}
