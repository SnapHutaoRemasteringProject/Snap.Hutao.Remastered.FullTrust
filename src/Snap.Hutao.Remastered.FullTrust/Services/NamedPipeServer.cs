using System.IO.Pipes;
using Snap.Hutao.Remastered.FullTrust.Core.LifeCycle.InterProcess;
using Snap.Hutao.Remastered.FullTrust.Models;

namespace Snap.Hutao.Remastered.FullTrust.Services;

public class NamedPipeServer : IDisposable
{
    private readonly NamedPipeServerStream serverStream;
    private readonly CancellationTokenSource cancellationTokenSource = new();
    private bool isDisposed;

    public NamedPipeServer()
    {
        serverStream = new NamedPipeServerStream(
            PrivateNamedPipe.FullTrustName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.WriteThrough);
    }

    public void Start()
    {
        Task.Run(async () => await RunServerAsync(cancellationTokenSource.Token));
    }

    public void Stop()
    {
        cancellationTokenSource.Cancel();
    }

    public void Dispose()
    {
        if (!isDisposed)
        {
            isDisposed = true;
            cancellationTokenSource.Cancel();
            serverStream.Dispose();
            cancellationTokenSource.Dispose();
        }
    }

    private async Task RunServerAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await serverStream.WaitForConnectionAsync(cancellationToken);
                await HandleClientAsync(cancellationToken);
                serverStream.Disconnect();
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error in pipe server: {ex.Message}");
            }
        }
    }

    private async Task HandleClientAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && serverStream.IsConnected)
        {
            try
            {
                serverStream.ReadPacket(out PipePacketHeader header);

                switch (header.Type)
                {
                    case PipePacketType.Request:
                        await HandleRequestAsync(header);
                        break;
                    case PipePacketType.SessionTermination:
                        return;
                    default:
                        Console.Error.WriteLine($"Unknown packet type: {header.Type}");
                        break;
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error handling client: {ex.Message}");
                break;
            }
        }
    }

    private async Task HandleRequestAsync(PipePacketHeader header)
    {
        switch (header.Command)
        {
            case PipePacketCommand.Create:
                await HandleCreateRequestAsync(header);
                break;
            case PipePacketCommand.StartProcess:
                await HandleStartProcessRequestAsync(header);
                break;
            case PipePacketCommand.LoadLibrary:
                await HandleLoadLibraryRequestAsync(header);
                break;
            case PipePacketCommand.ResumeMainThread:
                await HandleResumeMainThreadRequestAsync(header);
                break;
            default:
                Console.Error.WriteLine($"Unknown command: {header.Command}");
                break;
        }
    }

    private async Task HandleCreateRequestAsync(PipePacketHeader header)
    {
        var request = serverStream.ReadJsonContent<FullTrustProcessStartInfoRequest>(in header);
        if (request != null)
        {
            // 存储请求以供后续使用
            ProcessManager.StoreRequest(request);
            
            var responseHeader = new PipePacketHeader
            {
                Version = PrivateNamedPipe.FullTrustVersion,
                Type = PipePacketType.Response,
                Command = PipePacketCommand.Create,
                ContentType = PipePacketContentType.None
            };
            serverStream.WritePacket(in responseHeader);
        }
    }

    private async Task HandleStartProcessRequestAsync(PipePacketHeader header)
    {
        var result = ProcessManager.StartStoredProcess();
        
        var responseHeader = new PipePacketHeader
        {
            Version = PrivateNamedPipe.FullTrustVersion,
            Type = PipePacketType.Response,
            Command = PipePacketCommand.StartProcess,
            ContentType = PipePacketContentType.Json
        };
        
        serverStream.WritePacket(ref responseHeader, System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(result));
    }

    private async Task HandleLoadLibraryRequestAsync(PipePacketHeader header)
    {
        var request = serverStream.ReadJsonContent<FullTrustLoadLibraryRequest>(in header);
        var result = new FullTrustGenericResult { Succeeded = true };
        
        if (request != null)
        {
            // 这里实现加载库的逻辑
            result.Succeeded = ProcessManager.LoadLibrary(request);
            if (!result.Succeeded)
            {
                result.ErrorMessage = "Failed to load library";
            }
        }
        else
        {
            result.Succeeded = false;
            result.ErrorMessage = "Invalid request";
        }

        var responseHeader = new PipePacketHeader
        {
            Version = PrivateNamedPipe.FullTrustVersion,
            Type = PipePacketType.Response,
            Command = PipePacketCommand.LoadLibrary,
            ContentType = PipePacketContentType.Json
        };
        
        serverStream.WritePacket(ref responseHeader, System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(result));
    }

    private async Task HandleResumeMainThreadRequestAsync(PipePacketHeader header)
    {
        var result = new FullTrustGenericResult { Succeeded = ProcessManager.ResumeMainThread() };
        if (!result.Succeeded)
        {
            result.ErrorMessage = "Failed to resume main thread";
        }

        var responseHeader = new PipePacketHeader
        {
            Version = PrivateNamedPipe.FullTrustVersion,
            Type = PipePacketType.Response,
            Command = PipePacketCommand.ResumeMainThread,
            ContentType = PipePacketContentType.Json
        };
        
        serverStream.WritePacket(ref responseHeader, System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(result));
    }
}
