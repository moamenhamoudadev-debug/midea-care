using Microsoft.AspNetCore.Identity;

namespace MediCare.Data.Entities;

public class ApplicationUser : IdentityUser
{
    public string FullName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }

    public virtual Doctor? Doctor { get; set; }
    public virtual Patient? Patient { get; set; }
    public virtual ICollection<Notification> Notifications { get; set; } = new List<Notification>();
}
