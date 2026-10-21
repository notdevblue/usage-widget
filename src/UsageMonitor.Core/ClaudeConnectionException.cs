namespace UsageMonitor.Core;

public sealed class ClaudeConnectionException : Exception
{
    public ClaudeConnectionException() : this("Claude 사용량을 확인하지 못했습니다. Claude Code 로그인과 업데이트 상태를 확인해 주세요.")
    {
    }

    internal ClaudeConnectionException(string message) : base(message)
    {
    }
}
