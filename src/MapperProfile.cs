using AutoMapper;
using SplitzBackend.Models;
using SplitzBackend.Services;

namespace SplitzBackend;

public class MapperProfile : Profile
{
    public MapperProfile()
    {
        CreateMap<SplitzUser, SplitzUserDto>()
            .ForMember(d => d.Photo, opt => opt.MapFrom<PhotoResolver, string?>(s => s.Photo));

        CreateMap<SplitzUser, SplitzUserReducedDto>()
            .ForMember(d => d.Photo, opt => opt.MapFrom<PhotoResolver, string?>(s => s.Photo));

        CreateMap<Friend, FriendDto>();
        CreateMap<Group, GroupDto>()
            .ForMember(d => d.Photo, opt => opt.MapFrom<PhotoResolver, string?>(s => s.Photo));
        CreateMap<Group, GroupReducedDto>()
            .ForMember(d => d.Photo, opt => opt.MapFrom<PhotoResolver, string?>(s => s.Photo));
        CreateMap<GroupBalance, GroupBalanceDto>();
        CreateMap<GroupJoinLink, GroupJoinLinkDto>();

        CreateMap<Transaction, TransactionDto>()
            .ForMember(d => d.Photo, opt => opt.MapFrom<PhotoResolver, string?>(s => s.Photo));

        CreateMap<TransactionBalance, TransactionBalanceDto>();

        CreateMap<TransactionDraft, TransactionDraftDto>()
            .ForMember(d => d.Photo, opt => opt.MapFrom<PhotoResolver, string?>(s => s.Photo));

        CreateMap<TransactionDraftBalance, TransactionDraftBalanceDto>();

        CreateMap<Invoice, InvoiceDto>();
        CreateMap<Invoice, InvoiceReducedDto>();
        CreateMap<InvoiceDebt, InvoiceDebtDto>();
        CreateMap<InvoiceSettlement, InvoiceSettlementDto>();
        CreateMap<Notification, NotificationDto>()
            .ForMember(d => d.Data, opt => opt.MapFrom<NotificationDataResolver>());
        CreateMap<NotificationPreference, NotificationPreferenceDto>();
        CreateMap<NotificationTypeDefinition, NotificationTypeDto>();

        CreateMap<FriendRequest, FriendRequestDto>();
        CreateMap<GroupInvite, GroupInviteDto>();

        CreateMap<GroupInputDto, Group>();
        CreateMap<TransactionInputDto, Transaction>();
        CreateMap<TransactionBalanceInputDto, TransactionBalance>();
        CreateMap<TransactionDraftInputDto, TransactionDraft>();
        CreateMap<TransactionDraftBalanceInputDto, TransactionDraftBalance>();
    }
}

public sealed class PhotoResolver(IObjectStorage objectStorage)
    : IMemberValueResolver<object, object, string?, string?>
{
    public string? Resolve(object source, object destination, string? sourceMember, string? destMember,
        ResolutionContext context)
    {
        return ObjectStorageUrlBuilder.GenerateUrlFromKey(objectStorage, sourceMember);
    }
}

/// <summary>
///     Deserializes the notification payload. Falls back to the raw JSON string for unknown types.
///     Names and photos are not stored; call <see cref="INotificationService.LoadUserAndGroupReferencesAsync" /> to fill them.
/// </summary>
public sealed class NotificationDataResolver : IValueResolver<Notification, NotificationDto, object>
{
    public object Resolve(Notification source, NotificationDto destination, object destMember,
        ResolutionContext context)
    {
        return source.GetTypedData() ?? (object)source.Data;
    }
}

/// <summary>
///     Turns a stored object's key into a signed, cacheable relative URL.
///     Values that are not object keys (for example external URLs) pass through unchanged.
/// </summary>
public static class ObjectStorageUrlBuilder
{
    private static readonly TimeSpan PublicRounding = TimeSpan.FromHours(1);
    private const string PublicCacheControl = "public, max-age=3600";

    private static readonly TimeSpan PrivateRounding = TimeSpan.FromMinutes(15);
    private const string PrivateCacheControl = "private, max-age=900";

    public static string? GenerateUrlFromKey(IObjectStorage objectStorage, string? storedUrlOrKey)
    {
        if (string.IsNullOrWhiteSpace(storedUrlOrKey))
            return storedUrlOrKey;

        if (!objectStorage.TryParseObjectKey(storedUrlOrKey, out var objectKey))
            return storedUrlOrKey;

        var isPublic = objectKey.StartsWith("users/", StringComparison.OrdinalIgnoreCase) ||
                       objectKey.StartsWith("groups/", StringComparison.OrdinalIgnoreCase);

        return isPublic
            ? objectStorage.BuildRelativeUrl(objectKey, PublicRounding, PublicCacheControl)
            : objectStorage.BuildRelativeUrl(objectKey, PrivateRounding, PrivateCacheControl);
    }
}