namespace CamBridge.Exceptions;

public sealed class FrameLockedException : Exception
{
    public FrameLockedException(string operation)
        : base($"{operation} is unavailable: the frame is locked on the phone. Only the locked frame can be retrieved until it is unlocked on the phone.")
    {
    }
}
