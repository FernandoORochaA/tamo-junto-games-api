using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using TamoJuntoGames.API.Data;
using TamoJuntoGames.API.Options;
using TamoJuntoGames.API.Services;

var builder = WebApplication.CreateBuilder(args);

// Banco
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection"))
);

// Services (DI)
builder.Services.AddScoped<IUsuarioService, UsuarioService>();
builder.Services.AddScoped<ITokenService, TokenService>();

// Controllers
builder.Services.AddControllers();

// JWT (Authentication)
builder.Services.AddOptions<JwtOptions>()
    .Bind(builder.Configuration.GetSection(JwtOptions.SectionName))
    .Validate(options => !string.IsNullOrWhiteSpace(options.Key),
        "Jwt:Key é obrigatória e não pode conter apenas espaços em branco.")
    .Validate(options => options.Key is not null &&
        System.Text.Encoding.UTF8.GetByteCount(options.Key) >= 32,
        "Jwt:Key deve possuir pelo menos 32 bytes UTF-8.")
    .Validate(options => !string.IsNullOrWhiteSpace(options.Issuer),
        "Jwt:Issuer é obrigatório e não pode conter apenas espaços em branco.")
    .Validate(options => !string.IsNullOrWhiteSpace(options.Audience),
        "Jwt:Audience é obrigatório e não pode conter apenas espaços em branco.")
    .Validate(options => options.ExpireMinutes is >= 1 and <= 1440,
        "Jwt:ExpireMinutes deve ser um número inteiro entre 1 e 1440.")
    .ValidateOnStart();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer();

builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IOptions<JwtOptions>>((options, jwtOptions) =>
    {
        var jwt = jwtOptions.Value;

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,

            ValidIssuer = jwt.Issuer,
            ValidAudience = jwt.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(
                System.Text.Encoding.UTF8.GetBytes(jwt.Key)
            ),

            ClockSkew = TimeSpan.Zero
        };

        options.Events = new JwtBearerEvents
        {
            OnAuthenticationFailed = context =>
            {
                Console.WriteLine("JWT Auth failed: " + context.Exception);
                return Task.CompletedTask;
            },
            OnChallenge = context =>
            {
                Console.WriteLine("JWT Challenge error: " + context.Error);
                Console.WriteLine("JWT Challenge desc: " + context.ErrorDescription);
                return Task.CompletedTask;
            }
        };
    });

var app = builder.Build();

app.UseHttpsRedirection();

app.UseAuthentication();

app.UseAuthorization();

app.MapControllers();

app.Run();

public partial class Program
{
}
