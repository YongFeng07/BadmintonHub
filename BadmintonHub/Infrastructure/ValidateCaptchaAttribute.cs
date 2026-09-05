using DNTCaptcha.Core;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BadmintonHub.Infrastructure;

/// <summary>
/// Validates the DNTCaptcha challenge on POST actions (3rd-party captcha
/// integration, revised spec). The check can be switched off with the
/// "Security:EnableCaptcha" setting so the automated e2e suite (tests/e2e.sh)
/// can drive the HTTP flows without solving a captcha — documented in
/// docs/TESTING.md. The UI always renders the captcha when enabled.
/// </summary>
public class ValidateCaptchaAttribute : ActionFilterAttribute
{
    public override async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var config = context.HttpContext.RequestServices.GetRequiredService<IConfiguration>();
        if (config.GetValue("Security:EnableCaptcha", true))
        {
            var validator = context.HttpContext.RequestServices.GetRequiredService<IDNTCaptchaValidatorService>();
            if (!validator.HasRequestValidCaptchaEntry())
            {
                context.ModelState.AddModelError("CaptchaInputText", "Please enter the correct security code.");

                // Re-show the same view with the posted model (ViewName null -> current action's view).
                var result = new ViewResult();
                foreach (var arg in context.ActionArguments.Values)
                {
                    if (arg is not null)
                    {
                        result.ViewData.Model = arg;
                        break;
                    }
                }
                context.Result = result;
                return;
            }
        }

        await next();
    }
}
