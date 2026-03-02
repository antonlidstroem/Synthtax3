using Microsoft.AspNetCore.Http; // För PathString och HttpContext
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using System.Text;

// ... inuti InvokeAsync ...
public async Task InvokeAsync(HttpContext ctx)
{
    if (ctx.User.Identity?.IsAuthenticated != true || ShouldBypass(ctx.Request.Path))
    {
        await _next(ctx); // FIX: _next är en RequestDelegate, anropas med (ctx)
        return;
    }
    // ...
}