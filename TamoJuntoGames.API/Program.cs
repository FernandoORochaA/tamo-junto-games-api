using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using TamoJuntoGames.API.Data;
using TamoJuntoGames.API.Options;
using TamoJuntoGames.API.RateLimiting;
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
builder.Services.AddProblemDetails();
builder.Services.AddRateLimiter(LoginRateLimiting.Configure);

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

// Também em Development: detalhes internos ficam nos logs, não na resposta HTTP.
app.UseExceptionHandler(errorApp => errorApp.Run(async context =>
{
    context.Response.StatusCode = StatusCodes.Status500InternalServerError;
    var problemDetails = context.RequestServices.GetRequiredService<IProblemDetailsService>();
    // Se Accept não for suportado, mantemos 500 sem forçar serialização JSON.
    await problemDetails.TryWriteAsync(new ProblemDetailsContext
    {
        HttpContext = context,
        ProblemDetails = new Microsoft.AspNetCore.Mvc.ProblemDetails
        {
            Status = StatusCodes.Status500InternalServerError,
            Title = "Ocorreu um erro inesperado."
        }
    });
}));
app.UseStatusCodePages();

app.UseHttpsRedirection();

app.UseRouting();
app.UseRateLimiter();

app.UseAuthentication();

app.UseAuthorization();

app.MapControllers();

app.Run();

public partial class Program
{
}
