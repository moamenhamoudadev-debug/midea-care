using System.ComponentModel.DataAnnotations;

namespace MediCare.Web.ViewModels;

public class CreatePharmacistViewModel
{
    [Required(ErrorMessage = "Full name is required / الاسم بالكامل مطلوب.")]
    [StringLength(100, MinimumLength = 3, ErrorMessage = "Full name must be between 3 and 100 characters / الاسم يجب أن يكون بين 3 و100 حرف.")]
    [RegularExpression(@"^(?=.*[a-zA-Z\u0621-\u064A\u0671-\u06D3])[a-zA-Z\u0621-\u064A\u0671-\u06D3\u064B-\u065F\s.'\-]+$", ErrorMessage = "الاسم يجب أن يحتوي على حروف فقط وبدون أرقام / Full Name must contain only letters and cannot contain numbers.")]
    [Display(Name = "Full Name")]
    public string FullName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Email address is required.")]
    [EmailAddress(ErrorMessage = "Please provide a valid email address.")]
    [Display(Name = "Email Address")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Password is required.")]
    [StringLength(100, MinimumLength = 8, ErrorMessage = "Password must be at least 8 characters long.")]
    [DataType(DataType.Password)]
    [Display(Name = "Temporary Password")]
    public string Password { get; set; } = string.Empty;

    [Required(ErrorMessage = "Confirm password is required.")]
    [DataType(DataType.Password)]
    [Compare("Password", ErrorMessage = "The password and confirmation password do not match.")]
    [Display(Name = "Confirm Password")]
    public string ConfirmPassword { get; set; } = string.Empty;
}
