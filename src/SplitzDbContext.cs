using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using SplitzBackend.Models;

namespace SplitzBackend
{
    public class SplitzDbContext(DbContextOptions<SplitzDbContext> options) : IdentityDbContext<SplitzUser>(options);
}
