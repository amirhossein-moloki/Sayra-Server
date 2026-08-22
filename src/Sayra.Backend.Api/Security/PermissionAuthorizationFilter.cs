using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Sayra.Backend.Api.Models;
using Sayra.Backend.Application.Abstractions.Security;

namespace Sayra.Backend.Api.Security
{
    public class PermissionAuthorizationFilter : IAsyncActionFilter
    {
        private readonly string _permission;
        private readonly IAuthorizationService _authorizationService;

        public PermissionAuthorizationFilter(string permission, IAuthorizationService authorizationService)
        {
            _permission = permission ?? throw new ArgumentNullException(nameof(permission));
            _authorizationService = authorizationService ?? throw new ArgumentNullException(nameof(authorizationService));
        }

        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            var principal = context.HttpContext.Items["UserPrincipal"] as UserPrincipal ?? UserPrincipal.Anonymous;

            var authResult = await _authorizationService.AuthorizeAsync(principal, _permission, null, cancellationToken: context.HttpContext.RequestAborted);

            if (!authResult.IsAllowed)
            {
                if (string.Equals(authResult.ErrorCode, "UNAUTHORIZED", StringComparison.OrdinalIgnoreCase))
                {
                    context.Result = new UnauthorizedObjectResult(new
                    {
                        code = "UNAUTHORIZED",
                        message = authResult.FailureReason ?? "User is not authenticated.",
                        traceId = context.HttpContext.TraceIdentifier
                    });
                }
                else
                {
                    context.Result = new ObjectResult(new
                    {
                        code = authResult.ErrorCode ?? "FORBIDDEN",
                        message = authResult.FailureReason ?? "Permission denied.",
                        traceId = context.HttpContext.TraceIdentifier
                    })
                    {
                        StatusCode = 403
                    };
                }
                return;
            }

            await next();
        }
    }
}
