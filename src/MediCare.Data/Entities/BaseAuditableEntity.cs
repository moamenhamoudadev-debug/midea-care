namespace MediCare.Data.Entities;

public abstract class BaseAuditableEntity
{
    public int Id { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
