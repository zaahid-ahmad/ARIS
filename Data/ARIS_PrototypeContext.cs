using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using ARIS1.Models;

namespace ARIS1.Data
{
    public class ARIS_PrototypeContext(DbContextOptions<ARIS_PrototypeContext> options) : IdentityDbContext<User>(options)
    {
    }
}
