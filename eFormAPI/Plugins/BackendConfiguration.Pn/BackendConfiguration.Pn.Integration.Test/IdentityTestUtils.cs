// csharp
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microting.eFormApi.BasePn.Infrastructure.Database.Entities;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microting.EformAngularFrontendBase.Infrastructure.Data;
using Microting.EformAngularFrontendBase.Infrastructure.Data.Entities.Permissions;

public static class IdentityTestUtils
{
    public static UserManager<EformUser> CreateRealUserManager(BaseDbContext baseDbContext)
    {
        // var store = new UserStore<EformUser>(baseDbContext);
        var store = new UserStore<EformUser,
            EformRole,
            BaseDbContext,
            int,
            IdentityUserClaim<int>,
            EformUserRole,
            IdentityUserLogin<int>,
            IdentityUserToken<int>,
            IdentityRoleClaim<int>>(baseDbContext);
        var options = Options.Create(new IdentityOptions());
        var passwordHasher = new PasswordHasher<EformUser>();
        var userValidators = new List<IUserValidator<EformUser>> { new UserValidator<EformUser>() };
        var passwordValidators = new List<IPasswordValidator<EformUser>> { new PasswordValidator<EformUser>() };
        var keyNormalizer = new UpperInvariantLookupNormalizer();
        var errors = new IdentityErrorDescriber();
        var services = null as IServiceProvider;
        var logger = new LoggerFactory().CreateLogger<UserManager<EformUser>>();

        var securityGroup = new SecurityGroup()
        {
            Name = "Kun tid"
        };
        if (!baseDbContext.SecurityGroups.Any(x => x.Name == securityGroup.Name))
        {
            baseDbContext.SecurityGroups.Add(securityGroup);
        }
        var securityGroup2 = new SecurityGroup()
        {
            Name = "Kun arkiv"
        };
        if (!baseDbContext.SecurityGroups.Any(x => x.Name == securityGroup2.Name))
        {
            baseDbContext.SecurityGroups.Add(securityGroup2);
        }
        var securityGroup3 = new SecurityGroup()
        {
            Name = "eForm users"
        };
        if (!baseDbContext.SecurityGroups.Any(x => x.Name == securityGroup3.Name))
        {
            baseDbContext.SecurityGroups.Add(securityGroup3);
        }

        baseDbContext.SaveChanges();

        var eFormAdminsRole = new EformRole()
        {
            Name = "admin",
            NormalizedName = "admin",
            Id = 1,
            ConcurrencyStamp = Guid.NewGuid().ToString()
        };
        if (!baseDbContext.Roles.Any(x => x.Id == eFormAdminsRole.Id))
        {
            baseDbContext.Roles.Add(eFormAdminsRole);
        }

        var eFormUsersRole = new EformRole()
        {
            Name = "user",
            NormalizedName = "user",
            Id = 2,
            ConcurrencyStamp = Guid.NewGuid().ToString()
        };
        if (!baseDbContext.Roles.Any(x => x.Id == eFormUsersRole.Id))
        {
            baseDbContext.Roles.Add(eFormUsersRole);
        }
        baseDbContext.SaveChanges();

        return new UserManager<EformUser>(
            store,
            options,
            passwordHasher,
            userValidators,
            passwordValidators,
            keyNormalizer,
            errors,
            services,
            logger
        );
    }
}