namespace Snap.Hutao.Remastered.FullTrust.Core.LifeCycle.InterProcess;

internal enum PipePacketType : byte
{
    None = 0,
    Request = 1,
    Response = 2,
    SessionTermination = 3,
}
