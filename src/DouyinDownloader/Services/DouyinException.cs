namespace DouyinDownloader.Services;

public class DouyinException : Exception
{
    public DouyinException(string message) : base(message)
    {
    }

    public DouyinException(string message, Exception inner) : base(message, inner)
    {
    }
}

public sealed class DouyinUnavailableException : DouyinException
{
    public DouyinUnavailableException(string message) : base(message)
    {
    }
}

public sealed class DouyinBlockedException : DouyinException
{
    public DouyinBlockedException(string message) : base(message)
    {
    }
}
