using AutoMapper;
using AutoMapper.QueryableExtensions;
using Microsoft.EntityFrameworkCore;
using SplitzBackend.Models;

namespace SplitzBackend;

public class MapperProfile : Profile
{
    public MapperProfile()
    {
        CreateMap<SplitzUser, SplitzUserDto>();
        CreateMap<SplitzUser, SplitzUserReducedDto>();
        CreateMap<Friend, FriendDto>();
        CreateMap<Group, GroupDto>()
            .ForMember(dest => dest.Balances, opt => opt.Ignore());
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