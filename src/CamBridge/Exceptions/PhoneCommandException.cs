namespace CamBridge.Exceptions;

public sealed class PhoneCommandException : Exception
{
    public PhoneCommandException(string message)
        : base(message)
    {
    }
}
