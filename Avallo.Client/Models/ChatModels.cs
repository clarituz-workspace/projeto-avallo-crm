namespace Avallo.Client.Models;

public sealed record ChatMessageModel(string Role, string Content);
public sealed record ChatResponseModel(string Answer);
