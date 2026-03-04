using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using ARIS1.Models;

namespace ARIS1.Data
{
    public class ARIS1Context(DbContextOptions<ARIS1Context> options) : IdentityDbContext<User>(options)
    {
    }
}
