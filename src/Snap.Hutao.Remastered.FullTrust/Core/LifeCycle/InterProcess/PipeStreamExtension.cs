using System.Buffers;
using System.IO.Hashing;
using System.IO.Pipes;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Snap.Hutao.Remastered.FullTrust;
using Snap.Hutao.Remastered.FullTrust.Models;

namespace Snap.Hutao.Remastered.FullTrust.Core.LifeCycle.InterProcess;

internal static class PipeStreamExtension
{
    public static TData? ReadJsonContent<TData>(this PipeStream stream, in PipePacketHeader header)
    {
        using (IMemoryOwner<byte> memoryOwner = MemoryPool<byte>.Shared.Rent(header.ContentLength))
        {
            Span<byte> content = memoryOwner.Memory.Span.Slice(0, header.ContentLength);
            stream.ReadExactly(content);
            
            if (XxHash64.HashToUInt64(content) != header.Checksum)
            {
                throw new InvalidOperationException("PipePacket Content Hash incorrect");
            }

            // 使用源生成器上下文进行反序列化
            return JsonSerializer.Deserialize<TData>(content, GetJsonTypeInfo<TData>());
        }
    }

    private static JsonTypeInfo<TData> GetJsonTypeInfo<TData>()
    {
        // 根据类型返回相应的JsonTypeInfo
        Type type = typeof(TData);
        if (type == typeof(FullTrustProcessStartInfoRequest))
        {
            return (JsonTypeInfo<TData>)(object)AppJsonContext.Default.FullTrustProcessStartInfoRequest;
        }
        else if (type == typeof(FullTrustLoadLibraryRequest))
        {
            return (JsonTypeInfo<TData>)(object)AppJsonContext.Default.FullTrustLoadLibraryRequest;
        }
        else if (type == typeof(FullTrustGenericResult))
        {
            return (JsonTypeInfo<TData>)(object)AppJsonContext.Default.FullTrustGenericResult;
        }
        else if (type == typeof(FullTrustStartProcessResult))
        {
            return (JsonTypeInfo<TData>)(object)AppJsonContext.Default.FullTrustStartProcessResult;
        }
        else
        {
            // 回退到默认选项
            JsonTypeInfo? typeInfo = AppJsonContext.Default.GetTypeInfo(type);
            if (typeInfo is JsonTypeInfo<TData> typedInfo)
            {
                return typedInfo;
            }
            throw new InvalidOperationException($"No JsonTypeInfo found for type {type.Name}");
        }
    }

    public static void ReadPacket<TData>(this PipeStream stream, out PipePacketHeader header, out TData? data)
        where TData : class
    {
        data = default;

        stream.ReadPacket(out header);
        if (header.ContentType == PipePacketContentType.Json)
        {
            data = stream.ReadJsonContent<TData>(in header);
        }
    }

    public static unsafe void ReadPacket(this PipeStream stream, out PipePacketHeader header)
    {
        header = default;
        fixed (PipePacketHeader* pHeader = &header)
        {
            Span<byte> headerSpan = new(pHeader, sizeof(PipePacketHeader));
            stream.ReadExactly(headerSpan);
        }
    }

    public static void WritePacketWithJsonContent<TData>(this PipeStream stream, byte version, PipePacketType type, PipePacketCommand command, TData data)
    {
        PipePacketHeader header = default;
        header.Version = version;
        header.Type = type;
        header.Command = command;
        header.ContentType = PipePacketContentType.Json;

        // 使用源生成器上下文进行序列化
        stream.WritePacket(ref header, JsonSerializer.SerializeToUtf8Bytes(data, GetJsonTypeInfo<TData>()));
    }

    public static void WritePacket(this PipeStream stream, ref PipePacketHeader header, ReadOnlySpan<byte> content)
    {
        header.ContentLength = content.Length;
        header.Checksum = XxHash64.HashToUInt64(content);

        stream.WritePacket(in header);
        stream.Write(content);
    }

    public static void WritePacket(this PipeStream stream, byte version, PipePacketType type, PipePacketCommand command)
    {
        PipePacketHeader header = default;
        header.Version = version;
        header.Type = type;
        header.Command = command;

        stream.WritePacket(in header);
    }

    public static unsafe void WritePacket(this PipeStream stream, in PipePacketHeader header)
    {
        fixed (PipePacketHeader* pHeader = &header)
        {
            Span<byte> headerSpan = new(pHeader, sizeof(PipePacketHeader));
            stream.Write(headerSpan);
        }
    }
}
