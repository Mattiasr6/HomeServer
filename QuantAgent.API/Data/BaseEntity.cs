using System;

namespace QuantAgent.API.Data;

/// <summary>
/// Base auditable entity with UUID primary key.
/// All domain entities inherit from this to ensure consistent
/// tracking of creation/update timestamps and a stable identifier
/// safe for distributed generation.
/// </summary>
public abstract class BaseEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
