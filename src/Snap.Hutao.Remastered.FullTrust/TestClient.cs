using System.IO.Pipes;
using System.Text.Json;
using Snap.Hutao.Remastered.FullTrust.Core.LifeCycle.InterProcess;
using Snap.Hutao.Remastered.FullTrust.Models;

namespace Snap.Hutao.Remastered.FullTrust;

public static class TestClient
{
    public static void Test()
    {
        Console.WriteLine("Testing FullTrust client...");
        
        try
        {
            using NamedPipeClientStream client = new NamedPipeClientStream(
                ".",
                PrivateNamedPipe.FullTrustName,
                PipeDirection.InOut,
                PipeOptions.Asynchronous);
            
            client.Connect(5000);
            Console.WriteLine("Connected to FullTrust server");
            
            // 测试Create命令
            FullTrustProcessStartInfoRequest request = new FullTrustProcessStartInfoRequest
            {
                ApplicationName = "notepad.exe",
                CommandLine = "",
                CurrentDirectory = Environment.CurrentDirectory,
                CreationFlags = 0
            };
            
            client.WritePacketWithJsonContent(
                PrivateNamedPipe.FullTrustVersion,
                PipePacketType.Request,
                PipePacketCommand.Create,
                request);
            
            Console.WriteLine("Sent Create request");
            
            // 读取响应
            client.ReadPacket(out PipePacketHeader responseHeader, out object? responseData);
            Console.WriteLine($"Received response: Type={responseHeader.Type}, Command={responseHeader.Command}");
            
            // 测试StartProcess命令
            client.WritePacket(
                PrivateNamedPipe.FullTrustVersion,
                PipePacketType.Request,
                PipePacketCommand.StartProcess);
            
            Console.WriteLine("Sent StartProcess request");
            
            client.ReadPacket(out responseHeader, out FullTrustStartProcessResult? startResult);
            if (startResult != null)
            {
                Console.WriteLine($"StartProcess result: Succeeded={startResult.Succeeded}, ProcessId={startResult.ProcessId}");
            }
            
            // 测试LoadLibrary命令（需要实际的DLL路径）
            FullTrustLoadLibraryRequest loadRequest = FullTrustLoadLibraryRequest.Create("test.dll", "C:\\test.dll");
            client.WritePacketWithJsonContent(
                PrivateNamedPipe.FullTrustVersion,
                PipePacketType.Request,
                PipePacketCommand.LoadLibrary,
                loadRequest);
            
            Console.WriteLine("Sent LoadLibrary request");
            
            client.ReadPacket(out responseHeader, out FullTrustGenericResult? loadResult);
            if (loadResult != null)
            {
                Console.WriteLine($"LoadLibrary result: Succeeded={loadResult.Succeeded}");
            }
            
            // 测试ResumeMainThread命令
            client.WritePacket(
                PrivateNamedPipe.FullTrustVersion,
                PipePacketType.Request,
                PipePacketCommand.ResumeMainThread);
            
            Console.WriteLine("Sent ResumeMainThread request");
            
            client.ReadPacket(out responseHeader, out FullTrustGenericResult? resumeResult);
            if (resumeResult != null)
            {
                Console.WriteLine($"ResumeMainThread result: Succeeded={resumeResult.Succeeded}");
            }
            
            Console.WriteLine("All tests completed successfully!");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Test failed: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
        }
    }
}
