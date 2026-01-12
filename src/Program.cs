using AutoMapper;
using AutoMapper.EquivalencyExpression;
using FluentStorage;
using FluentStorage.Blobs;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;
using Scalar.AspNetCore;
using SplitzBackend.Models;
using SplitzBackend.Services;

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

        // configure openapi
        builder.Services.AddOpenApi(options =>
        {
            options.AddDocumentTransformer((document, _, _) =>
            {
                document.Components ??= new OpenApiComponents();
                document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
                document.Components.SecuritySchemes.Add("Bearer", new OpenApiSecurityScheme
                {
                    Type = SecuritySchemeType.Http,
                    Scheme = "bearer",
                    Description = "Bearer token authentication",
                    In = ParameterLocation.Header
                });
                document.SetReferenceHostDocument();
                return Task.CompletedTask;
            });
            options.AddOperationTransformer((operation, context, _) =>
            {
                operation.Security ??= new List<OpenApiSecurityRequirement>();
                var bearerRequirement = new OpenApiSecuritySchemeReference("Bearer");
                operation.Security.Add(new OpenApiSecurityRequirement
                {
                    [bearerRequirement] = []
                });
                return Task.CompletedTask;
            });
        });

        // configure object storage (S3 via FluentStorage.AWS)
        builder.Services.AddOptions<StorageOptions>()
            .Bind(builder.Configuration.GetSection(StorageOptions.SectionName))
            .Validate(o => !string.IsNullOrWhiteSpace(o.Bucket), "Storage:Bucket is required")
            .Validate(o => !string.IsNullOrWhiteSpace(o.Region), "Storage:Region is required")
            .ValidateOnStart();

        builder.Services.AddSingleton<IBlobStorage>(sp =>
        {
            StorageFactory.Modules.UseAwsStorage();

            var options = sp.GetRequiredService<IOptions<StorageOptions>>().Value;
            if (!options.Provider.Equals("S3", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Unsupported storage provider '{options.Provider}'.");
            if (string.IsNullOrWhiteSpace(options.Endpoint))
                throw new InvalidOperationException("Storage:Endpoint is required for S3.");

            return StorageFactory.Blobs.AwsS3(
                options.AccessKeyId,
                options.SecretAccessKey,
                null,
                options.Bucket,
                options.Region,
                options.Endpoint);
        });

        builder.Services.AddSingleton<IObjectStorage, S3ObjectStorage>();

        // configure automapper
        builder.Services.AddAutoMapper((serviceProvider, cfg) =>
        {
            cfg.AddCollectionMappers();
            cfg.UseEntityFrameworkCoreModel<SplitzDbContext>(serviceProvider);

            // Allow AutoMapper to construct profiles/value resolvers via DI.
            cfg.ConstructServicesUsing(serviceProvider.GetRequiredService);
        }, typeof(SplitzDbContext), typeof(MapperProfile));

        builder.Services.AddSingleton<IImageProcessingService, NetVipsImageProcessingService>();
        builder.Services.AddSingleton<IImageStorageService, ImageStorageService>();

        var app = builder.Build();

        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SplitzDbContext>();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<SplitzUser>>();

            // Ensure database is created
            await db.Database.EnsureCreatedAsync();

            // Seed test data in development environment
            if (app.Environment.IsDevelopment()) await SeedTestData(db, userManager);
        }

        // Configure the HTTP request pipeline.
        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi();
            app.MapScalarApiReference();
            //app.UseSwaggerUI(options =>
            //{
            //    options.SwaggerEndpoint("/openapi/v1.json", "v1");
            //});
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
        if (await db.Users.AnyAsync()) return; // Test data already seeded

        // Create test users
        var testUsers = new List<SplitzUser>
        {
            new()
            {
                Id = Guid.NewGuid().ToString(),
                UserName = "alice@example.com",
                Email = "alice@example.com",
                EmailConfirmed = true,
                Photo = "https://i.pravatar.cc/150?img=1"
            },
            new()
            {
                Id = Guid.NewGuid().ToString(),
                UserName = "bob@example.com",
                Email = "bob@example.com",
                EmailConfirmed = true,
                Photo = "https://i.pravatar.cc/150?img=2"
            },
            new()
            {
                Id = Guid.NewGuid().ToString(),
                UserName = "charlie@example.com",
                Email = "charlie@example.com",
                EmailConfirmed = true,
                Photo = "https://i.pravatar.cc/150?img=3"
            },
            new()
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
                throw new Exception(
                    $"Failed to create user {user.UserName}: {string.Join(", ", result.Errors.Select(e => e.Description))}");
        }

        // Refresh users from database to get the created IDs
        var alice = await userManager.FindByEmailAsync("alice@example.com");
        var bob = await userManager.FindByEmailAsync("bob@example.com");
        var charlie = await userManager.FindByEmailAsync("charlie@example.com");
        var diana = await userManager.FindByEmailAsync("diana@example.com");

        if (alice == null || bob == null || charlie == null || diana == null)
            throw new Exception("Failed to retrieve created test users");

        // Create test groups
        var testGroups = new List<Group>
        {
            new()
            {
                GroupId = Guid.NewGuid(),
                Name = "Weekend Trip",
                Photo = "https://picsum.photos/200/200?random=1",
                Members = new List<SplitzUser> { alice, bob, charlie },
                MembersIdHash = "",
                TransactionCount = 0,
                LastActivityTime = DateTime.Now.AddDays(-1)
            },
            new()
            {
                GroupId = Guid.NewGuid(),
                Name = "House Expenses",
                Photo = "https://picsum.photos/200/200?random=2",
                Members = new List<SplitzUser> { alice, bob },
                MembersIdHash = "",
                TransactionCount = 0,
                LastActivityTime = DateTime.Now.AddDays(-3)
            },
            new()
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
        foreach (var group in testGroups) group.UpdateMembersIdHash();

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