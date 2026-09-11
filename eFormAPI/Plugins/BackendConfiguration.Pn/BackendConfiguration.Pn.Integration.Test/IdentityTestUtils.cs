// csharp
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microting.eFormApi.BasePn.Abstractions;
using Microting.eFormApi.BasePn.Infrastructure.Database.Entities;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microting.EformAngularFrontendBase.Infrastructure.Data;
using Microting.EformAngularFrontendBase.Infrastructure.Data.Entities.Permissions;
using NSubstitute;

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
            services!,
            logger
        );
    }

    /// <summary>
    /// <see cref="CreateRealUserManager"/> with its <see cref="IdentityOptions"/>
    /// swapped for what production actually configures, instead of the bare
    /// Identity defaults. The store, validators, hasher and seeded groups/roles
    /// are exactly <see cref="CreateRealUserManager"/>'s; only the options differ.
    /// <para>
    /// Source of truth: eform-angular-frontend's
    /// eFormAPI/eFormAPI.Web/Hosting/Security/AuthServiceCollectionExtensions.cs,
    /// <c>AddEFormAuth</c>'s <c>services.Configure&lt;IdentityOptions&gt;(...)</c>
    /// block (lines 55-69 as read for this change). This repo cannot reference
    /// that file - it lives in a different repo entirely - so the values are
    /// copied by hand and MUST be kept in sync with it if that block ever
    /// changes.
    /// </para>
    /// <para>
    /// The property this exists for is <c>User.RequireUniqueEmail = true</c>:
    /// <see cref="CreateRealUserManager"/> leaves the Identity default (false),
    /// which makes Identity's duplicate-EMAIL refusal of a login
    /// structurally unreproducible against it - two AspNetUsers rows sharing an
    /// email but not a username never collide when this flag is off. Use this
    /// factory instead whenever a test needs that check to actually fire.
    /// <see cref="CreateRealUserManager"/> is left byte-identical so no test
    /// built against it changes behaviour.
    /// </para>
    /// </summary>
    public static UserManager<EformUser> CreateProductionLikeUserManager(BaseDbContext baseDbContext)
    {
        // UserManager reads Options at call time (validators, lockout, password
        // policy); with no IServiceProvider its constructor does nothing
        // options-dependent, so replacing them after construction is equivalent
        // to passing them in.
        var userManager = CreateRealUserManager(baseDbContext);
        userManager.Options = new IdentityOptions
        {
            Password =
            {
                RequireDigit = false,
                RequiredLength = 6,
                RequireNonAlphanumeric = false,
                RequireUppercase = false,
                RequireLowercase = false
            },
            Lockout =
            {
                DefaultLockoutTimeSpan = TimeSpan.FromMinutes(10),
                MaxFailedAccessAttempts = 10,
                AllowedForNewUsers = true
            },
            User =
            {
                RequireUniqueEmail = true
            }
        };
        return userManager;
    }

    /// <summary>
    /// A realistic <see cref="IUserService"/> substitute: only <c>GetByUsernameAsync</c>
    /// and <c>UserId</c> are wired (nothing else in this test suite calls the rest),
    /// but <c>GetByUsernameAsync</c> actually queries <paramref name="baseDbContext"/>
    /// instead of returning null unconditionally.
    /// <para>
    /// Mirrors eform-angular-frontend's
    /// eFormAPI/eFormAPI.Web/Services/UserService.cs, <c>GetByUsernameAsync</c>
    /// verbatim: look up by <c>UserName</c> first, then fall back to <c>Email</c> -
    /// and, on that fallback, rename the found user's <c>UserName</c> to match,
    /// exactly as production does. Keep the two in sync by hand.
    /// </para>
    /// <para>
    /// A bare <c>Substitute.For&lt;IUserService&gt;()</c> returns null from
    /// <c>GetByUsernameAsync</c> unconditionally regardless of its argument
    /// (confirmed against the real <c>Microting.eFormApi.BasePn</c> types -
    /// NSubstitute cannot construct a non-null <c>EformUser</c> for an
    /// unconfigured call). Against it, any code that still calls
    /// <c>IUserService.GetByUsernameAsync</c> directly would get a fresh,
    /// disconnected <c>EformUser</c> instead of finding the one that already
    /// exists for the worker being saved - unlike production, which uses the
    /// real <c>UserService</c> and finds it. <c>CreateDeviceUser</c> and
    /// <c>UpdateDeviceUser</c> no longer go through <c>IUserService</c> for
    /// this - they resolve the login via
    /// <c>FindLoginWithoutSideEffectsAsync</c> against the <c>UserManager</c>
    /// directly - so this factory matters to their tests only for
    /// <c>UserId</c>. Use it wherever a test's assertions depend on that
    /// lookup actually working.
    /// </para>
    /// </summary>
    public static IUserService CreateRealUserService(BaseDbContext baseDbContext, UserManager<EformUser> userManager)
    {
        var userService = Substitute.For<IUserService>();
        userService.UserId.Returns(1);
        userService.GetByUsernameAsync(Arg.Any<string>())
            .Returns(callInfo => LookUpByUsernameAsync(baseDbContext, userManager, callInfo.ArgAt<string>(0)));
        return userService;
    }

    private static async Task<EformUser> LookUpByUsernameAsync(
        BaseDbContext baseDbContext, UserManager<EformUser> userManager, string username)
    {
        var user = await baseDbContext.Users.FirstOrDefaultAsync(x => x.UserName == username);
        if (user == null)
        {
            user = await baseDbContext.Users.FirstOrDefaultAsync(x => x.Email == username);
            if (user != null)
            {
                user.UserName = username;
                await userManager.UpdateAsync(user);
                return user;
            }
        }

        // Matches production's own nullability here: GetByUsernameAsync legitimately
        // returns null when no account exists for either lookup.
        return user!;
    }

    /// <summary>
    /// Keeps every test subject off EformUser id 1. The primary admin is id 1,
    /// which the code under test never adopts, writes to or assigns a group to.
    /// AspNetUsers persists across tests and AUTO_INCREMENT never reuses an id,
    /// so only the first user created in a fresh container can land on id 1 by
    /// accident. While AspNetUsers is still empty this takes id 1 with a
    /// throwaway account; once it holds any row it does nothing. Call it from a
    /// fixture's <c>[SetUp]</c>. A test that needs a subject on id 1 puts it
    /// there with <see cref="ForceCreateAsEformUserId1Async"/>.
    /// </summary>
    public static async Task ReserveEformUserId1Async(BaseDbContext baseDbContext)
    {
        if (!await baseDbContext.Users.AnyAsync())
        {
            await ConsumeEformUserId1Async(CreateRealUserManager(baseDbContext));
        }
    }

    private static async Task ConsumeEformUserId1Async(UserManager<EformUser> userManager)
    {
        var placeholder = new EformUser
        {
            Email = $"{Guid.NewGuid()}@id1-placeholder.test",
            UserName = $"{Guid.NewGuid()}@id1-placeholder.test",
            FirstName = "Placeholder",
            LastName = "ConsumesId1",
            Locale = "da",
            EmailConfirmed = true,
            TimeZone = "Europe/Copenhagen",
            Formats = "de-DE"
        };
        var result = await userManager.CreateAsync(placeholder);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                "ConsumeEformUserId1Async: could not create the placeholder user - "
                + string.Join(",", result.Errors.Select(e => e.Description)));
        }
    }

    /// <summary>
    /// The opposite of <see cref="ReserveEformUserId1Async"/>: puts
    /// <paramref name="candidate"/> on EformUser id 1, for a test pinning the
    /// "resolved login IS id 1" half of an admin gate. Removes whatever row
    /// holds id 1 (with its group and role memberships), then inserts
    /// <paramref name="candidate"/> with <c>Id = 1</c> - MySQL accepts an
    /// explicit value for an AUTO_INCREMENT column without disturbing the
    /// counter for later inserts.
    /// </summary>
    public static async Task<EformUser> ForceCreateAsEformUserId1Async(
        BaseDbContext baseDbContext, UserManager<EformUser> userManager, EformUser candidate)
    {
        var existing = await baseDbContext.Users.FindAsync(1);
        if (existing != null)
        {
            baseDbContext.SecurityGroupUsers.RemoveRange(
                baseDbContext.SecurityGroupUsers.Where(x => x.EformUserId == 1));
            baseDbContext.UserRoles.RemoveRange(
                baseDbContext.UserRoles.Where(x => x.UserId == 1));
            await baseDbContext.SaveChangesAsync();

            baseDbContext.Users.Remove(existing);
            await baseDbContext.SaveChangesAsync();
        }

        candidate.Id = 1;
        var result = await userManager.CreateAsync(candidate);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                "ForceCreateAsEformUserId1Async: could not create the id-1 subject - "
                + string.Join(",", result.Errors.Select(e => e.Description)));
        }

        return candidate;
    }
}