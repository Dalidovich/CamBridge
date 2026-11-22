namespace CamBridge.Exceptions;

public sealed class PhoneNotConnectedException : Exception
{
    public const string DefaultMessage = "No phone connected. Scan the QR code shown in the CamBridge console.";

    public PhoneNotConnectedException()
        : base(DefaultMessage)
    {
    }

    public PhoneNotConnectedException(string message)
        : base(message)
    {
    }
}
