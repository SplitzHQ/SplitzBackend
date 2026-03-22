using AutoMapper;
using SplitzBackend.Models;
using SplitzBackend.Services;

namespace SplitzBackend;

public class MapperProfile : Profile
{
    public MapperProfile()
    {
        CreateMap<SplitzUser, SplitzUserDto>()
            .ForMember(d => d.Photo, opt => opt.MapFrom<SignedPhotoUrlResolver, string?>(s => s.Photo));

        CreateMap<SplitzUser, SplitzUserReducedDto>()
            .ForMember(d => d.Photo, opt => opt.MapFrom<SignedPhotoUrlResolver, string?>(s => s.Photo));

        CreateMap<Friend, FriendDto>();
        CreateMap<Group, GroupDto>()
            .ForMember(d => d.Photo, opt => opt.MapFrom<SignedPhotoUrlResolver, string?>(s => s.Photo));
        CreateMap<Group, GroupReducedDto>()
            .ForMember(d => d.Photo, opt => opt.MapFrom<SignedPhotoUrlResolver, string?>(s => s.Photo));
        CreateMap<GroupBalance, GroupBalanceDto>();
        CreateMap<GroupJoinLink, GroupJoinLinkDto>();

        CreateMap<Transaction, TransactionDto>()
            .ForMember(d => d.Photo, opt => opt.MapFrom<SignedPhotoUrlResolver, string?>(s => s.Photo));

        CreateMap<TransactionBalance, TransactionBalanceDto>();

        CreateMap<TransactionDraft, TransactionDraftDto>()
            .ForMember(d => d.Photo, opt => opt.MapFrom<SignedPhotoUrlResolver, string?>(s => s.Photo));

        CreateMap<TransactionDraftBalance, TransactionDraftBalanceDto>();

        CreateMap<Invoice, InvoiceDto>();
        CreateMap<Invoice, InvoiceReducedDto>();
        CreateMap<InvoiceDebt, InvoiceDebtDto>();
        CreateMap<InvoiceSettlement, InvoiceSettlementDto>();
        CreateMap<Notification, NotificationDto>()
            .ForMember(d => d.Data, opt => opt.MapFrom(s => s.GetTypedData() ?? (object)s.Data));

        CreateMap<GroupInputDto, Group>();
        CreateMap<TransactionInputDto, Transaction>();
        CreateMap<TransactionBalanceInputDto, TransactionBalance>();
        CreateMap<TransactionDraftInputDto, TransactionDraft>();
        CreateMap<TransactionDraftBalanceInputDto, TransactionDraftBalance>();
    }
}

public sealed class SignedPhotoUrlResolver(IObjectStorage objectStorage)
    : IMemberValueResolver<object, object, string?, string?>
{
    private static readonly TimeSpan PublicRounding = TimeSpan.FromHours(1);
    private const string PublicCacheControl = "public, max-age=3600";

    private static readonly TimeSpan PrivateRounding = TimeSpan.FromMinutes(15);
    private const string PrivateCacheControl = "private, max-age=900";

    public string? Resolve(object source, object destination, string? sourceMember, string? destMember,
        ResolutionContext context)
    {
        if (string.IsNullOrWhiteSpace(sourceMember))
            return sourceMember;

        if (!objectStorage.TryParseObjectKey(sourceMember, out var objectKey))
            return sourceMember;

        var isPublic = objectKey.StartsWith("users/", StringComparison.OrdinalIgnoreCase) ||
                       objectKey.StartsWith("groups/", StringComparison.OrdinalIgnoreCase);

        return isPublic
            ? objectStorage.BuildPublicUrl(objectKey, PublicRounding, PublicCacheControl)
            : objectStorage.BuildPublicUrl(objectKey, PrivateRounding, PrivateCacheControl);
    }
}