using System.ComponentModel.DataAnnotations;

namespace SplitzBackend.Models;

public class Group
{
    public Guid GroupId { get; set; }

    [MaxLength(256)] public required string Name { get; set; }

    [Url] [MaxLength(256)] public string? Photo { get; set; }

    public List<SplitzUser> Members { get; set; } = new();

    public List<Transaction> Transactions { get; set; } = new();

    public List<GroupBalance> Balances { get; set; } = new();
}

public class GroupDto
{
    public required Guid GroupId { get; set; }

    [MaxLength(256)] public required string Name { get; set; }

    [Url] [MaxLength(256)] public string? Photo { get; set; }

    public List<SplitzUserReducedDto> Members { get; set; } = new();
    public List<GroupBalanceDto> Balances { get; set; } = new();
}

public class GroupReducedDto
{
    public required Guid GroupId { get; set; }

    [MaxLength(256)] public required string Name { get; set; }

    [Url] [MaxLength(256)] public string? Photo { get; set; }
}

public class GroupInputDto
{
    [MaxLength(256)] public required string Name { get; set; }

    [Url] [MaxLength(256)] public string? Photo { get; set; }
}