namespace LiveAudioBoard.Core.Downloads;

public sealed class FreesoundAuthorizationRequiredException : InvalidOperationException
{
    public FreesoundAuthorizationRequiredException(string message)
        : base(message)
    {
    }
}
