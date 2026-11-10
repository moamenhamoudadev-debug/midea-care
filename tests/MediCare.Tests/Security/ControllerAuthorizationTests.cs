using System.Reflection;
using FluentAssertions;
using MediCare.Web.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace MediCare.Tests.Security;

public class ControllerAuthorizationTests
{
    /// <summary>
    /// Explicit, strictly reviewed and documented allowlist of actions that are publicly accessible without authentication.
    /// Format: "ControllerName.ActionName"
    /// Any action not in this allowlist that is anonymous will fail the security test suite.
    /// </summary>
    private static readonly HashSet<string> DocumentedAnonymousAllowlist = new(StringComparer.OrdinalIgnoreCase)
    {
        // Public marketing and informational views
        "HomeController.Index",
        "HomeController.Privacy",
        "HomeController.Terms",
        "HomeController.Error",

        // Public patient authentication & onboarding
        "AccountController.Login",
        "AccountController.RegisterPatient",
        "AccountController.RegisterDoctor",
        "AccountController.AccessDenied",
        "AccountController.ForgotPassword",
        "AccountController.ForgotPasswordConfirmation",
        "AccountController.ResetPassword",
        "AccountController.ResetPasswordConfirmation",

        // Public doctor directory & search catalog
        "DoctorsController.Index",
        "DoctorsController.Details",

        // Public digital prescription QR verification portal (read-only verification)
        "PrescriptionsController.Verify",

        // Public appointment booking slots computation for candidate dates
        "CalendarApiController.GetSlots",

        // Public interactive AI medical assistance chatbot
        "ChatbotApiController.SendMessage",

        // Paymob provider callback (anonymous transport, authenticated by signed HMAC validation)
        "PaymentApiController.PaymobWebhook",

        // Paymob browser return is informational and does not confirm payment
        "PaymentApiController.PaymobReturn",

        // Public doctor reviews catalog view
        "DoctorReviewsController.ListPartial"
    };

    private static IEnumerable<Type> GetControllerTypes()
    {
        var assembly = typeof(AccountController).Assembly;
        return assembly.GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract && t.IsPublic);
    }

    private static IEnumerable<MethodInfo> GetActionMethods(Type controllerType)
    {
        return controllerType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName && !m.GetCustomAttributes<NonActionAttribute>().Any());
    }

    [Fact]
    public void AllControllers_MustHaveExplicitSecurityPosture()
    {
        var controllers = GetControllerTypes().ToList();
        controllers.Should().NotBeEmpty("At least one controller should be present in MediCare.Web");

        var violations = new List<string>();

        foreach (var controller in controllers)
        {
            var actions = GetActionMethods(controller);
            bool classHasAuthorize = controller.GetCustomAttributes<AuthorizeAttribute>(inherit: true).Any();
            bool classHasAllowAnonymous = controller.GetCustomAttributes<AllowAnonymousAttribute>(inherit: true).Any();

            foreach (var action in actions)
            {
                bool actionHasAuthorize = action.GetCustomAttributes<AuthorizeAttribute>(inherit: true).Any();
                bool actionHasAllowAnonymous = action.GetCustomAttributes<AllowAnonymousAttribute>(inherit: true).Any();

                bool isProtected = actionHasAuthorize || (classHasAuthorize && !actionHasAllowAnonymous);
                bool isAnonymous = actionHasAllowAnonymous || (classHasAllowAnonymous && !actionHasAuthorize);

                if (!isProtected && !isAnonymous)
                {
                    violations.Add($"{controller.Name}.{action.Name} has neither [Authorize] nor [AllowAnonymous].");
                }
            }
        }

        violations.Should().BeEmpty(
            "Every controller action must explicitly declare its security policy ([Authorize] or [AllowAnonymous]).\n" +
            string.Join("\n", violations));
    }

    [Fact]
    public void AllAnonymousActions_MustBeInDocumentedAllowlist()
    {
        var controllers = GetControllerTypes().ToList();
        var unauthorizedExposures = new List<string>();

        foreach (var controller in controllers)
        {
            var actions = GetActionMethods(controller);
            bool classHasAuthorize = controller.GetCustomAttributes<AuthorizeAttribute>(inherit: true).Any();
            bool classHasAllowAnonymous = controller.GetCustomAttributes<AllowAnonymousAttribute>(inherit: true).Any();

            foreach (var action in actions)
            {
                bool actionHasAuthorize = action.GetCustomAttributes<AuthorizeAttribute>(inherit: true).Any();
                bool actionHasAllowAnonymous = action.GetCustomAttributes<AllowAnonymousAttribute>(inherit: true).Any();

                bool isAnonymous = actionHasAllowAnonymous || (classHasAllowAnonymous && !actionHasAuthorize);

                if (isAnonymous)
                {
                    string actionKey = $"{controller.Name}.{action.Name}";
                    if (!DocumentedAnonymousAllowlist.Contains(actionKey))
                    {
                        unauthorizedExposures.Add(
                            $"Unapproved public exposure: '{actionKey}' is accessible anonymously but NOT in the documented allowlist!");
                    }
                }
            }
        }

        unauthorizedExposures.Should().BeEmpty(
            "No action may be publicly exposed without being explicitly catalogued in the security allowlist:\n" +
            string.Join("\n", unauthorizedExposures));
    }

    [Fact]
    public void DocumentedAllowlist_MustNotContainDeadEntries()
    {
        var controllers = GetControllerTypes().ToList();
        var actualAnonymousActions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var controller in controllers)
        {
            var actions = GetActionMethods(controller);
            bool classHasAuthorize = controller.GetCustomAttributes<AuthorizeAttribute>(inherit: true).Any();
            bool classHasAllowAnonymous = controller.GetCustomAttributes<AllowAnonymousAttribute>(inherit: true).Any();

            foreach (var action in actions)
            {
                bool actionHasAuthorize = action.GetCustomAttributes<AuthorizeAttribute>(inherit: true).Any();
                bool actionHasAllowAnonymous = action.GetCustomAttributes<AllowAnonymousAttribute>(inherit: true).Any();

                bool isAnonymous = actionHasAllowAnonymous || (classHasAllowAnonymous && !actionHasAuthorize);
                if (isAnonymous)
                {
                    actualAnonymousActions.Add($"{controller.Name}.{action.Name}");
                }
            }
        }

        var deadEntries = DocumentedAnonymousAllowlist
            .Where(entry => !actualAnonymousActions.Contains(entry))
            .ToList();

        deadEntries.Should().BeEmpty(
            "Allowlist must remain synchronized with real codebase. Found obsolete allowlist entries:\n" +
            string.Join("\n", deadEntries));
    }

    [Fact]
    public void SensitiveClinicalControllers_MustNeverExposeAnonymousActions()
    {
        var sensitiveControllers = new[]
        {
            typeof(AdminController),
            typeof(DoctorController),
            typeof(MedicalRecordsController),
            typeof(AppointmentsController),
            typeof(PaymentController)
        };

        foreach (var controller in sensitiveControllers)
        {
            var actions = GetActionMethods(controller);
            foreach (var action in actions)
            {
                action.GetCustomAttributes<AllowAnonymousAttribute>(inherit: true)
                    .Should().BeEmpty($"Sensitive clinical action '{controller.Name}.{action.Name}' MUST NEVER be decorated with [AllowAnonymous].");
            }
        }
    }

    [Theory]
    [InlineData(typeof(AdminController), "Admin")]
    [InlineData(typeof(DoctorController), "Doctor")]
    public void RoleRestrictedControllers_MustEnforceDesignatedRoles(Type controllerType, string expectedRole)
    {
        var authAttr = controllerType.GetCustomAttributes<AuthorizeAttribute>(inherit: true).FirstOrDefault();
        authAttr.Should().NotBeNull($"{controllerType.Name} must have [Authorize] attribute at class level.");
        authAttr!.Roles.Should().NotBeNull();
        authAttr.Roles!.Split(',').Select(r => r.Trim()).Should().Contain(expectedRole);
    }

    [Fact]
    public void PrescriptionDispense_MustRequirePharmacistOrAdminRole()
    {
        var method = typeof(PrescriptionsController).GetMethods()
            .FirstOrDefault(m => m.Name == "Dispense" && m.GetCustomAttributes<HttpPostAttribute>().Any());

        method.Should().NotBeNull("PrescriptionsController must have a POST Dispense action.");
        var authAttr = method!.GetCustomAttributes<AuthorizeAttribute>().FirstOrDefault();
        authAttr.Should().NotBeNull("POST Dispense must have an [Authorize] attribute.");
        authAttr!.Roles.Should().NotBeNull();

        var roles = authAttr.Roles!.Split(',').Select(r => r.Trim()).ToList();
        roles.Should().Contain("Pharmacist");
        roles.Should().Contain("Admin");
    }
}
