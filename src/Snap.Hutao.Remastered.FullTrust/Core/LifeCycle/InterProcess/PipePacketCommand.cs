namespace Snap.Hutao.Remastered.FullTrust.Core.LifeCycle.InterProcess;

internal enum PipePacketCommand : byte
{
    None = 0,
    Create = 1,
    StartProcess = 2,
    LoadLibrary = 3,
    ResumeMainThread = 4,
}
