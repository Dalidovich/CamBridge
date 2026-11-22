namespace CamBridge.Models;

public sealed record PhoneReply(bool Ok, string? Error, MediaPayload? Media);
