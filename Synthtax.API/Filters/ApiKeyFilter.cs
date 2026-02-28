using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Synthtax.API.Filters;

/// <summary>
/// Validerar att inkommande request har ett giltigt X-Api-Key header.
/// Används för CiCdController som är AllowAnonymous men behöver
/// autentisering för CI/CD-agenter som saknar JWT-tokens.
/// 
/// Konfiguration i appsettings.json:
///   "CiCd": { "ApiKey": "your-secret-key-here" }
/// 
/// Eller via miljövariabel: CiCd__ApiKey
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class ApiKeyAttribute : Attribute, IActionFilter
{
    private const string ApiKeyHeaderName = "X-Api-Key";

    public void OnActionExecuting(ActionExecutingContext context)
    {
        var config    = context.HttpContext.RequestServices.GetRequiredService<IConfiguration>();
        var logger    = context.HttpContext.RequestServices.GetRequiredService<ILogger<ApiKeyAttribute>>();
        var expectedKey = config["CiCd:ApiKey"];

        if (string.IsNullOrWhiteSpace(expectedKey))
        {
            logger.LogError("CiCd:ApiKey is not configured. Blocking request.");
            context.Result = new StatusCodeResult(StatusCodes.Status503ServiceUnavailable);
            return;
        }

        if (!context.HttpContext.Request.Headers.TryGetValue(ApiKeyHeaderName, out var providedKey)
            || !string.Equals(providedKey, expectedKey, StringComparison.Ordinal))
        {
            var ip = context.HttpContext.Connection.RemoteIpAddress?.ToString();
            logger.LogWarning("Invalid or missing API key in CI/CD request from {Ip}", ip);
            context.Result = new UnauthorizedObjectResult(new
            {
                Message = $"Valid '{ApiKeyHeaderName}' header required for CI/CD endpoints."
            });
        }
    }

    public void OnActionExecuted(ActionExecutedContext context) { }
}
