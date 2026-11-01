namespace SplitzBackend.Models;

/// <summary>The current user's net balance and suggested payments for one currency.</summary>
public sealed record GroupBalanceSummaryDto
{
    /// <summary>The ISO currency code. Amounts in different currencies remain separate.</summary>
    public required string Currency { get; init; }

    /// <summary>The user's net balance. Positive means the user is owed money.</summary>
    public required decimal TotalBalance { get; init; }

    /// <summary>Suggested payments involving the current user after group debt simplification.</summary>
    public required List<GroupMemberTransferDto> MemberBalances { get; init; }
}

/// <summary>A suggested payment involving the current user and another group member.</summary>
public sealed record GroupMemberTransferDto
{
    /// <summary>The other member's user ID.</summary>
    public required string UserId { get; init; }

    /// <summary>The other member's display name.</summary>
    public required string Name { get; init; }

    /// <summary>The payment amount. Negative means the member pays the current user; positive means the user pays the member.</summary>
    public required decimal Amount { get; init; }

    /// <summary>The ISO currency code for the payment.</summary>
    public required string Currency { get; init; }
}