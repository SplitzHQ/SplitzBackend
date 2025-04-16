using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;

namespace SplitzBackend.Models;

public class SplitzUser : IdentityUser
{
    [PersonalData] [Url] [MaxLength(256)] public string? Photo { get; set; }

    public List<Friend> Friends { get; set; } = new();
    public List<Group> Groups { get; set; } = new();

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

    [MaxLength(256)] public string? UserName { get; set; }

    [Url] [MaxLength(256)] public string? Photo { get; set; }

    public List<FriendDto> Friends { get; set; } = new();
    public List<Group> Groups { get; set; } = new();
    public List<GroupBalance> Balances { get; set; } = new();
}

public class SplitzUserReducedDto
{
    public required string Id { get; set; }

    [MaxLength(256)] public string? UserName { get; set; }

    [Url] [MaxLength(256)] public string? Photo { get; set; }
}