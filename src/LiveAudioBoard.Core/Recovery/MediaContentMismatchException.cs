namespace LiveAudioBoard.Core.Recovery;

public sealed class MediaContentMismatchException : Exception
{
    public MediaContentMismatchException(string expectedHash, string actualHash)
        : base("所选文件与原音频内容不一致。请选择原文件或它的完整副本。")
    {
        ExpectedHash = expectedHash;
        ActualHash = actualHash;
    }

    public string ExpectedHash { get; }

    public string ActualHash { get; }
}
