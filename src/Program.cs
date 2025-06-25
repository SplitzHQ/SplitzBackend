using AutoMapper;
using AutoMapper.EquivalencyExpression;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using SplitzBackend.Models;
using System.Reflection;

namespace SplitzBackend;

public class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // configure database
        builder.Services.AddDbContext<SplitzDbContext>(options =>
        {
            options.UseSqlite(builder.Configuration.GetConnectionString("Sqlite"));
        });

        // configure identity
        builder.Services.AddAuthorization();
        builder.Services.AddIdentityApiEndpoints<SplitzUser>(option =>
            {
                option.User.RequireUniqueEmail = true;
                option.Password.RequiredLength = 12;
                option.Password.RequireDigit = true;
                option.Password.RequireLowercase = true;
                option.Password.RequireUppercase = false;
                option.Password.RequireNonAlphanumeric = false;
            })
            .AddEntityFrameworkStores<SplitzDbContext>();

        // configure routing and controllers
        builder.Services.AddControllers();
        builder.Services.Configure<RouteOptions>(options => options.LowercaseUrls = true);

        // configure default cors policy to allow all origins
        builder.Services.AddCors(options =>
        {
            options.AddDefaultPolicy(policy =>
            {
                policy.AllowAnyOrigin();
                policy.AllowAnyMethod();
                policy.AllowAnyHeader();
            });
        });

        // configure swagger
        // Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen(options =>
        {
            var xmlFilename = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
            options.IncludeXmlComments(Path.Combine(AppContext.BaseDirectory, xmlFilename));
            options.SupportNonNullableReferenceTypes();
            options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                Name = "Authorization",
                Type = SecuritySchemeType.ApiKey,
                In = ParameterLocation.Header,
                Scheme = "Bearer"
            });
            options.OperationFilter<SwaggerSecurityOperationFilter>();
        });

        // configure automapper
        builder.Services.AddAutoMapper((serviceProvider, automapper) =>
        {
            automapper.AddCollectionMappers();
            automapper.UseEntityFrameworkCoreModel<SplitzDbContext>(serviceProvider);
        }, typeof(SplitzDbContext), typeof(MapperProfile));

        var app = builder.Build();

        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SplitzDbContext>();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<SplitzUser>>();

            // Ensure database is created
            await db.Database.EnsureCreatedAsync();

            // Seed test data in development environment
            if (app.Environment.IsDevelopment())
            {
                await SeedTestData(db, userManager);
            }
        }

        // Configure the HTTP request pipeline.
        if (app.Environment.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI();
        }

        app.UseCors();

        app.UseAuthorization();

        app.MapGroup("/account").MapIdentityApi<SplitzUser>();
        app.MapControllers();

        app.Run();
    }

    private static async Task SeedTestData(SplitzDbContext db, UserManager<SplitzUser> userManager)
    {
        // Check if test data already exists
        if (await db.Users.AnyAsync())
        {
            return; // Test data already seeded
        }

        // Create test users
        var testUsers = new List<SplitzUser>
        {
            new SplitzUser
            {
                Id = Guid.NewGuid().ToString(),
                UserName = "alice@example.com",
                Email = "alice@example.com",
                EmailConfirmed = true,
                Photo = "https://i.pravatar.cc/150?img=1"
            },
            new SplitzUser
            {
                Id = Guid.NewGuid().ToString(),
                UserName = "bob@example.com",
                Email = "bob@example.com",
                EmailConfirmed = true,
                Photo = "https://i.pravatar.cc/150?img=2"
            },
            new SplitzUser
            {
                Id = Guid.NewGuid().ToString(),
                UserName = "charlie@example.com",
                Email = "charlie@example.com",
                EmailConfirmed = true,
                Photo = "https://i.pravatar.cc/150?img=3"
            },
            new SplitzUser
            {
                Id = Guid.NewGuid().ToString(),
                UserName = "diana@example.com",
                Email = "diana@example.com",
                EmailConfirmed = true,
                Photo = "https://i.pravatar.cc/150?img=4"
            }
        };

        // Create users with password
        const string defaultPassword = "TestPassword123!";
        foreach (var user in testUsers)
        {
            var result = await userManager.CreateAsync(user, defaultPassword);
            if (!result.Succeeded)
            {
                throw new Exception($"Failed to create user {user.UserName}: {string.Join(", ", result.Errors.Select(e => e.Description))}");
            }
        }

        // Refresh users from database to get the created IDs
        var alice = await userManager.FindByEmailAsync("alice@example.com");
        var bob = await userManager.FindByEmailAsync("bob@example.com");
        var charlie = await userManager.FindByEmailAsync("charlie@example.com");
        var diana = await userManager.FindByEmailAsync("diana@example.com");

        if (alice == null || bob == null || charlie == null || diana == null)
        {
            throw new Exception("Failed to retrieve created test users");
        }

        // Create test groups
        var testGroups = new List<Group>
        {
            new Group
            {
                GroupId = Guid.NewGuid(),
                Name = "Weekend Trip",
                Photo = "https://picsum.photos/200/200?random=1",
                Members = new List<SplitzUser> { alice, bob, charlie },
                MembersIdHash = "",
                TransactionCount = 0,
                LastActivityTime = DateTime.Now.AddDays(-1)
            },
            new Group
            {
                GroupId = Guid.NewGuid(),
                Name = "House Expenses",
                Photo = "https://picsum.photos/200/200?random=2",
                Members = new List<SplitzUser> { alice, bob },
                MembersIdHash = "",
                TransactionCount = 0,
                LastActivityTime = DateTime.Now.AddDays(-3)
            },
            new Group
            {
                GroupId = Guid.NewGuid(),
                Name = "Dinner Club",
                Photo = "https://picsum.photos/200/200?random=3",
                Members = new List<SplitzUser> { alice, bob, charlie, diana },
                MembersIdHash = "",
                TransactionCount = 0,
                LastActivityTime = DateTime.Now.AddDays(-7)
            }
        };

        // Update members hash for each group
        foreach (var group in testGroups)
        {
            group.UpdateMembersIdHash();
        }

        // Add groups to database
        db.Groups.AddRange(testGroups);
        await db.SaveChangesAsync();

        Console.WriteLine("Test data seeded successfully!");
        Console.WriteLine($"Created {testUsers.Count} test users and {testGroups.Count} test groups");
        Console.WriteLine("Test user credentials:");
        Console.WriteLine($"  Email: alice@example.com, Password: {defaultPassword}");
        Console.WriteLine($"  Email: bob@example.com, Password: {defaultPassword}");
        Console.WriteLine($"  Email: charlie@example.com, Password: {defaultPassword}");
        Console.WriteLine($"  Email: diana@example.com, Password: {defaultPassword}");
    }
}