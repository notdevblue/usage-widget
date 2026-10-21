namespace UsageMonitor.Core;

public sealed class CodexConnectionException : Exception
{
    public CodexConnectionException() : base("Codex 연결이 종료되었습니다. 다시 연결해 주세요.")
    {
    }
}

public sealed class CodexRequestException : Exception
{
    public int ErrorCode { get; }

    internal CodexRequestException(int errorCode) : base("Codex 요청을 처리하지 못했습니다. Codex를 업데이트한 후 다시 시도해 주세요.")
    {
        ErrorCode = errorCode;
    }
}
