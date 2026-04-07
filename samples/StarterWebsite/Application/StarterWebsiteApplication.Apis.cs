using CL.WebLogic;
using CL.WebLogic.Forms;
using CL.WebLogic.Routing;
using CL.WebLogic.Runtime;
using StarterWebsite.Application.Forms;

namespace StarterWebsite.Application;

public sealed partial class StarterWebsiteApplication
{
    private void RegisterApis(WebRegistrationContext context)
    {
        context.RegisterApi("/api/site", new WebRouteOptions
        {
            Name = "Site Summary",
            Description = "Application API showing current site metadata and request context values.",
            Tags = ["starter", "api", "site"]
        }, _ =>
        {
            var pageContext = WebLogicRequest.GetPageContextFromRequest();
            return Task.FromResult(WebResult.Json(new
            {
                site = _config.SiteTitle,
                tagline = _config.Tagline,
                theme = _config.ThemeName,
                plugins = new[] { "ThemeShowcasePlugin", "PluginApiPlugin" },
                demoUsers = _config.DemoUsers.Select(static user => new
                {
                    user.UserId,
                    user.DisplayName,
                    accessGroups = user.AccessGroups
                }),
                pageContext = new
                {
                    pageContext.Path,
                    pageContext.Method,
                    pageContext.UserId,
                    accessGroups = pageContext.AccessGroups
                }
            }));
        }, "GET");

        context.RegisterApi("/api/demo/lookups/mentors", new WebRouteOptions
        {
            Name = "Starter Mentor Lookup",
            Description = "JSON lookup endpoint used by the generic HTTP-backed WebLogic form lookup provider.",
            Tags = ["starter", "api", "forms", "lookup"]
        }, request =>
        {
            var country = request.GetQuery(nameof(ProfileIntakeForm.Country)) ?? request.GetQuery("country") ?? string.Empty;
            var term = request.GetQuery("term") ?? string.Empty;
            var items = StarterMentorOption.All
                .Where(mentor => string.IsNullOrWhiteSpace(country) || string.Equals(mentor.Country, country, StringComparison.OrdinalIgnoreCase))
                .Where(mentor =>
                    string.IsNullOrWhiteSpace(term) ||
                    mentor.Value.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                    mentor.Label.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                    mentor.Specialty.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                    mentor.Office.Contains(term, StringComparison.OrdinalIgnoreCase))
                .Take(6)
                .Select(static mentor => new
                {
                    id = mentor.Value,
                    label = $"{mentor.Label} - {mentor.Office} - {mentor.Specialty}",
                    mentor.Country,
                    mentor.Office,
                    mentor.Specialty
                })
                .ToArray();

            return Task.FromResult(WebResult.Json(new { items }));
        }, "GET");

        context.RegisterApi("/api/forms/profile-intake", new WebRouteOptions
        {
            Name = "Profile Intake Submit",
            Description = "Binds and validates the starter profile intake form using request.Forms.",
            Tags = ["starter", "api", "forms", "validation"]
        }, async request =>
        {
            var submission = await request.Forms.BindAsync<ProfileIntakeForm>(ProfileFormSchemaOptions).ConfigureAwait(false);
            if (!submission.IsValid)
            {
                return WebResult.Json(new
                {
                    success = false,
                    message = "Validation failed. Fix the highlighted fields and try again.",
                    errors = submission.Errors,
                    values = submission.Values
                }, 400);
            }

            var file = submission.Files.GetValueOrDefault("ProfileImage");
            var command = submission.MapTo<ProfileIntakeCommand>();
            var record = WebFormMapper.Map<ProfileIntakeCommand, ProfileIntakeRecord>(command);
            return WebResult.Json(new
            {
                success = true,
                message = "The form passed both client and server validation.",
                normalized = new
                {
                    displayName = submission.Model.DisplayName,
                    emailAddress = submission.Model.EmailAddress,
                    country = submission.Model.Country,
                    localOffice = submission.Model.LocalOffice,
                    preferredMentorId = submission.Model.PreferredMentorId,
                    age = submission.Model.Age,
                    favoriteColor = submission.Model.FavoriteColor,
                    bio = submission.Model.Bio,
                    audienceSegment = submission.Model.AudienceSegment,
                    formIntent = submission.Model.FormIntent
                },
                mapped = new
                {
                    command,
                    record
                },
                upload = file is null
                    ? null
                    : new
                    {
                        file.FileName,
                        file.ContentType,
                        file.Length
                    }
            });
        }, "POST");
    }
}
