namespace UsageMonitor.Core;

public class GrokConnectionException : Exception
{
    public GrokConnectionException() : this("Grok 사용량을 확인하지 못했습니다. Grok 로그인과 네트워크를 확인해 주세요.")
    {
    }

    internal GrokConnectionException(string message) : base(message)
    {
    }
}

internal sealed class GrokAuthenticationRequiredException : GrokConnectionException
{
    internal GrokAuthenticationRequiredException() : base("Grok 로그인이 필요해요. 설정에서 Grok 로그인을 진행해 주세요.")
    {
    }
}
