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
        CreateMap<GroupJoinLink, GroupJoinLinkDto>();
        CreateMap<Transaction, TransactionDto>();
        CreateMap<TransactionBalance, TransactionBalanceDto>();
        CreateMap<TransactionDraft, TransactionDraftDto>();
        CreateMap<TransactionDraftBalance, TransactionDraftBalanceDto>();

        CreateMap<GroupInputDto, Group>();
        CreateMap<TransactionInputDto, Transaction>();
        CreateMap<TransactionBalanceInputDto, TransactionBalance>();
        CreateMap<TransactionDraftInputDto, TransactionDraft>();
        CreateMap<TransactionDraftBalanceInputDto, TransactionDraftBalance>();
    }
}