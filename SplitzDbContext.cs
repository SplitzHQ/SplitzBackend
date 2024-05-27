using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace SplitzBackend
{
    public class SplitzDbContext : IdentityDbContext<IdentityUser>
    {
        public SplitzDbContext(DbContextOptions<SplitzDbContext> options) :
            base(options)
        { }
    }
}
