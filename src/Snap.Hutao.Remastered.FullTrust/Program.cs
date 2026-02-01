using Snap.Hutao.Remastered.FullTrust.Services;

namespace Snap.Hutao.Remastered.FullTrust;

public static class Program
{
    private static NamedPipeServer? pipeServer;

    public static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--test")
        {
            TestClient.Test();
            return 0;
        }

        Console.WriteLine("Snap.Hutao.Remastered.FullTrust starting...");

        try
        {
            // 启动命名管道服务器
            pipeServer = new NamedPipeServer();
            pipeServer.Start();

            Console.WriteLine($"Named pipe server started on: {Core.LifeCycle.InterProcess.PrivateNamedPipe.FullTrustName}");
            Console.WriteLine("Press Ctrl+C to exit...");

            // 等待退出信号
            var exitEvent = new ManualResetEvent(false);
            Console.CancelKeyPress += (sender, e) =>
            {
                e.Cancel = true;
                exitEvent.Set();
            };

            exitEvent.WaitOne();

            Console.WriteLine("Shutting down...");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Fatal error: {ex.Message}");
            Console.Error.WriteLine(ex.StackTrace);
            return 1;
        }
        finally
        {
            pipeServer?.Stop();
            pipeServer?.Dispose();
            ProcessManager.Cleanup();
        }
    }
}
