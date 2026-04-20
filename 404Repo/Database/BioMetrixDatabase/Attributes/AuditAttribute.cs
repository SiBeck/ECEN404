using Microsoft.AspNetCore.Mvc.Filters;
using MedicalImagingAPI.Services;
using System.Security.Claims;

namespace MedicalImagingAPI.Attributes
{
    public class AuditAttribute : ActionFilterAttribute
    {
        private readonly string _action;
        private readonly string _resourceType;

        public AuditAttribute(string action, string resourceType)
        {
            _action = action;
            _resourceType = resourceType;
        }

        public override async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            var auditService = context.HttpContext.RequestServices.GetService<IAuditService>();
            var userId = context.HttpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            if (userId == null)
            {
                await next();
                return;
            }

            var resourceId = context.ActionArguments.ContainsKey("id")
                ? context.ActionArguments["id"]?.ToString()
                : "N/A";

            var executedContext = await next();

            var isSuccess = executedContext.Exception == null;
            var errorMessage = executedContext.Exception?.Message;

            await auditService.LogAsync(
                Guid.Parse(userId),
                _action,
                _resourceType,
                resourceId,
                isSuccess,
                errorMessage
            );
        }
    }
}