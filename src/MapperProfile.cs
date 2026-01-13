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
        CreateMap<Group, GroupDto>();
        CreateMap<Group, GroupReducedDto>();
        CreateMap<GroupBalance, GroupBalanceDto>();
        CreateMap<GroupJoinLink, GroupJoinLinkDto>();

        CreateMap<Transaction, TransactionDto>()
            .ForMember(d => d.Photo, opt => opt.MapFrom<SignedPhotoUrlResolver, string?>(s => s.Photo));

        CreateMap<TransactionBalance, TransactionBalanceDto>();

        CreateMap<TransactionDraft, TransactionDraftDto>()
            .ForMember(d => d.Photo, opt => opt.MapFrom<SignedPhotoUrlResolver, string?>(s => s.Photo));

        CreateMap<TransactionDraftBalance, TransactionDraftBalanceDto>();

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
    public string? Resolve(object source, object destination, string? sourceMember, string? destMember,
        ResolutionContext context)
    {
        if (string.IsNullOrWhiteSpace(sourceMember))
            return sourceMember;

        return objectStorage.TryParseObjectKey(sourceMember, out var objectKey)
            ? objectStorage.BuildPublicUrl(objectKey)
            : sourceMember;
    }
}