using AutoMapper;
using AutoMapper.EquivalencyExpression;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using SplitzBackend.Models;
using System.Reflection;

namespace SplitzBackend;

public class Program
{
    public static void Main(string[] args)
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
            //db.Database.EnsureDeleted();
            db.Database.EnsureCreated();
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
}