using System.Net;

namespace CamBridge.Infrastructure;

public sealed record NetworkSelection(IPAddress PrimaryAddress, IReadOnlyList<IPAddress> OtherAddresses, IReadOnlyList<IPAddress> AllAddresses);
