﻿using System.ComponentModel.DataAnnotations;
using System.IO.Hashing;
using System.Text;

namespace SplitzBackend.Models;

public class Group
{
    public Guid GroupId { get; set; }

    [MaxLength(256)] public required string Name { get; set; }

    [Url][MaxLength(256)] public string? Photo { get; set; }

    public List<SplitzUser> Members { get; set; } = new();

    [MaxLength(16)] public required string MembersIdHash { get; set; }

    public List<Transaction> Transactions { get; set; } = new();

    public List<GroupBalance> Balances { get; set; } = new();

    /// <summary>
    ///     The number of transactions of the group.
    ///     This number is only for searching purposes and may not be accurate if transactions are deleted or modified.
    /// </summary>
    public required int TransactionCount { get; set; } = 0;

    /// <summary>
    ///     The date of the last transaction of the group.
    ///     This is only for searching purposes and may not be accurate if transactions are deleted or modified.
    /// </summary>
    public required DateTime LastActivityTime { get; set; } = DateTime.Now;

    public void UpdateMembersIdHash()
    {
        // use xxhash3 to create membersIdHash
        var membersId = string.Join(",", Members.Select(m => m.Id).OrderBy(m => m));
        var xxhash3 = XxHash3.Hash(Encoding.UTF8.GetBytes(membersId));
        MembersIdHash = Convert.ToHexString(xxhash3).ToLower();
    }
}

public class GroupDto
{
    public required Guid GroupId { get; set; }

    [MaxLength(256)] public required string Name { get; set; }

    [Url][MaxLength(256)] public string? Photo { get; set; }

    public required List<SplitzUserReducedDto> Members { get; set; } = new();

    [MaxLength(16)] public required string MembersIdHash { get; set; }

    public required List<GroupBalanceDto> Balances { get; set; } = new();

    /// <summary>
    ///     The number of transactions between the user and their friend.
    ///     This number is only for searching purposes and may not be accurate if transactions are deleted or modified.
    /// </summary>
    public required int TransactionCount { get; set; } = 0;

    /// <summary>
    ///     The date of the last transaction between the user and their friend.
    ///     This is only for searching purposes and may not be accurate if transactions are deleted or modified.
    /// </summary>
    public required DateTime LastActivityTime { get; set; } = DateTime.Now;
}

public class GroupReducedDto
{
    public required Guid GroupId { get; set; }

    [MaxLength(256)] public required string Name { get; set; }

    [Url][MaxLength(256)] public string? Photo { get; set; }
}

public class GroupInputDto
{
    [MaxLength(256)] public required string Name { get; set; }

    [Url][MaxLength(256)] public string? Photo { get; set; }

    public required List<string> MembersId { get; set; }
}