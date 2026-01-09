using Microsoft.AspNetCore.Identity;
using System.ComponentModel.DataAnnotations;

namespace SplitzBackend.Models;

public class SplitzUser : IdentityUser
{
    [PersonalData][Url][MaxLength(256)] public string? Photo { get; set; }

    public List<Friend> Friends { get; set; } = new();
    public List<Group> Groups { get; set; } = new();
    public List<GroupBalance> Balances { get; set; } = new();

    public override bool Equals(object? obj)
    {
        if (obj is not SplitzUser user) return false;
        return Id == user.Id;
    }

    public override int GetHashCode()
    {
        return Id.GetHashCode();
    }
}

public class SplitzUserDto
{
    public required string Id { get; set; }

    [Required]
    [MinLength(1, ErrorMessage = "Username cannot be empty")]
    [MaxLength(256)]
    public required string UserName { get; set; }
    public required string Email { get; set; }
    [Url][MaxLength(256)] public string? Photo { get; set; }

    public List<FriendDto> Friends { get; set; } = new();
    public List<GroupReducedDto> Groups { get; set; } = new();
    public List<GroupBalanceDto> Balances { get; set; } = new();
}

public class SplitzUserReducedDto
{
    public required string Id { get; set; }

    [Required]
    [MinLength(1, ErrorMessage = "Username cannot be empty")]
    [MaxLength(256)]
    public required string UserName { get; set; }

    [Url][MaxLength(256)] public string? Photo { get; set; }
}