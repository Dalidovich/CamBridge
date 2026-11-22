namespace CamBridge.Exceptions;

public sealed class PhoneTimeoutException : Exception
{
    public PhoneTimeoutException(string operation, TimeSpan timeout)
        : base($"The phone did not answer the {operation} request within {timeout.TotalSeconds:0} s.")
    {
    }
}
