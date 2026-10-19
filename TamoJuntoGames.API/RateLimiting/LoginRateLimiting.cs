using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace TamoJuntoGames.API.RateLimiting;

public static class LoginRateLimiting
{
    public const string PolicyName = "LoginPorIp";
    public const string MensagemPublica = "Muitas tentativas de login. Tente novamente mais tarde.";
    public static readonly EventId RejeicaoEvent = new(1002, "LoginRateLimitRejected");

    public static SlidingWindowRateLimiterOptions CriarOpcoes() => new()
    {
        PermitLimit = 10,
        Window = TimeSpan.FromMinutes(1),
        SegmentsPerWindow = 6,
        QueueLimit = 0,
        AutoReplenishment = true
    };

    public static void Configure(RateLimiterOptions options)
    {
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        options.AddPolicy(PolicyName, context =>
        {
            var ip = context.Connection.RemoteIpAddress;
            if (ip?.IsIPv4MappedToIPv6 == true)
                ip = ip.MapToIPv4();

            // Sem IP, todas as requisições usam a mesma partição, sem confiar em headers.
            var particao = ip?.ToString() ?? "sem-ip";
            return RateLimitPartition.GetSlidingWindowLimiter(particao, _ => CriarOpcoes());
        });

        options.OnRejected = async (context, cancellationToken) =>
        {
            var httpContext = context.HttpContext;
            var logger = httpContext.RequestServices.GetRequiredService<ILoggerFactory>()
                .CreateLogger(typeof(LoginRateLimiting).FullName!);
            logger.LogWarning(RejeicaoEvent, "Requisição rejeitada pela política {Politica}.", PolicyName);

            if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter) &&
                retryAfter >= TimeSpan.Zero)
            {
                httpContext.Response.Headers.RetryAfter = Math.Ceiling(retryAfter.TotalSeconds)
                    .ToString("0", CultureInfo.InvariantCulture);
            }

            httpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            await httpContext.RequestServices.GetRequiredService<IProblemDetailsService>()
                .TryWriteAsync(new ProblemDetailsContext
                {
                    HttpContext = httpContext,
                    ProblemDetails = new ProblemDetails
                    {
                        Status = StatusCodes.Status429TooManyRequests,
                        Type = "https://www.rfc-editor.org/rfc/rfc6585#section-4",
                        Title = "Muitas requisições.",
                        Detail = MensagemPublica
                    }
                });
        };
    }
}
