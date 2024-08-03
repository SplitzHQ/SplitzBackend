using AutoMapper;
using SplitzBackend.Models;

namespace SplitzBackend;

public class MapperProfile : Profile
{
    public MapperProfile()
    {
        CreateMap<SplitzUser, SplitzUserDto>();
        CreateMap<SplitzUser, SplitzUserReducedDto>();
        CreateMap<Friend, FriendDto>();
        CreateMap<Group, GroupDto>();
        CreateMap<Group, GroupReducedDto>();
        CreateMap<GroupBalance, GroupBalanceDto>();
        CreateMap<GroupJoinLink, GroupBalanceDto>();
        CreateMap<Tag, TagDto>();
        CreateMap<Transaction, TransactionDto>();
        CreateMap<TransactionBalance, TransactionBalanceDto>();
        CreateMap<TransactionDraft, TransactionDraftDto>();
        CreateMap<TransactionDraftBalance, TransactionDraftBalanceDto>();
    }
}