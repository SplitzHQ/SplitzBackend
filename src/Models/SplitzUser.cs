using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;

namespace SplitzBackend.Models;

public class SplitzUser: IdentityUser
{
    [PersonalData]
    [Url]
    [MaxLength(255)]
    public string? Photo { get; set; }

    [PersonalData]
    public List<SplitzUser> Friends { get; set; } = new();
}

public class SplitzUserDto
{
    public required string Id { get; set; }
    public string? UserName { get; set; }
    public string? Email { get; set; }
    public string? Photo { get; set; }
    public List<SplitzUserDto> Friends { get; set; } = new();
}