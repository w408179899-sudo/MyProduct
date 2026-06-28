namespace Hardware.KmBox;

public sealed class KmBoxException : Exception
{
    public KmBoxException(string message)
        : base(message)
    {
    }

    public KmBoxException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
