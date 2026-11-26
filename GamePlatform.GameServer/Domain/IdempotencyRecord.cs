namespace GamePlatform.GameServer.Domain;

public sealed class IdempotencyRecord
{
    public Guid Id { get; set; }

    public string IdempotencyKey { get; set; } = null!;

    public string OperationId { get; set; } = null!;

    public string Status { get; set; } = null!;

    public string ResponsePayload { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    public DateTime? CompletedAt { get; set; }
}
