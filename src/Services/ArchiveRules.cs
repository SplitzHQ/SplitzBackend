using System.Linq.Expressions;
using SplitzBackend.Models;

namespace SplitzBackend.Services;

/// <summary>
///     Rules for archived invoices, groups and expenses.
///     An archived item is read-only. This keeps an archived invoice fully paid and keeps an archived group free of
///     unpaid expenses until a member unarchives it.
/// </summary>
public static class ArchiveRules
{
    public const string InvoiceNotSettledMessage = "Only fully paid invoices can be archived";

    public const string GroupHasUnpaidExpensesMessage = "Only groups with no unpaid expenses can be archived";

    public const string ArchivedInvoiceMessage = "Archived invoices can't be changed. Unarchive the invoice first";

    public const string ArchivedGroupMessage = "Archived groups can't be changed. Unarchive the group first";

    public const string ArchivedTransactionMessage =
        "Archived expenses can't be changed. Unarchive the invoice first";

    /// <summary>
    ///     Matches an expense that is not paid. An expense is paid only when it belongs to a settled invoice.
    /// </summary>
    public static readonly Expression<Func<Transaction, bool>> IsUnpaid =
        t => t.InvoiceId == null || t.Invoice!.Status != InvoiceStatus.Settled;

    /// <summary>Returns why the invoice can't be changed, or null when changes are allowed. Needs the group loaded.</summary>
    public static string? GetInvoiceLockReason(Invoice invoice)
    {
        if (invoice.IsArchived)
            return ArchivedInvoiceMessage;
        return invoice.Group.IsArchived ? ArchivedGroupMessage : null;
    }

    /// <summary>Returns why the expense can't be changed, or null when changes are allowed.</summary>
    public static string? GetTransactionLockReason(Transaction transaction, Group group)
    {
        if (transaction.IsArchived)
            return ArchivedTransactionMessage;
        return group.IsArchived ? ArchivedGroupMessage : null;
    }

    /// <summary>Archives or unarchives the invoice and every expense in it. Needs the transactions loaded.</summary>
    public static void SetInvoiceArchived(Invoice invoice, bool archived)
    {
        invoice.IsArchived = archived;
        invoice.ArchiveTime = archived ? DateTime.UtcNow : null;
        foreach (var transaction in invoice.Transactions)
            transaction.IsArchived = archived;
    }
}