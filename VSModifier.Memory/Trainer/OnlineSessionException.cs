namespace VSModifier.Memory.Trainer;

public sealed class OnlineSessionException(string message) : InvalidOperationException(message);
