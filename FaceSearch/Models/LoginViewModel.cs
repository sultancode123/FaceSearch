using System.ComponentModel.DataAnnotations;

namespace IFNRCONFaceSearch.Models
{
    public class LoginViewModel
    {
        [Required(ErrorMessage = "Registration ID is required")]
        [Display(Name = "Registration ID")]
        public string RegistrationId { get; set; } = string.Empty;

        [Required(ErrorMessage = "Email is required")]
        [EmailAddress(ErrorMessage = "Enter a valid email")]
        [Display(Name = "Email Address")]
        public string Email { get; set; } = string.Empty;
    }
}