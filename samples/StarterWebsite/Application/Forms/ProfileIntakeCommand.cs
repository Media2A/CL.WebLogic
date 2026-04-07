using CL.WebLogic.Forms;
using CL.WebLogic.Runtime;

namespace StarterWebsite.Application.Forms;

public sealed class ProfileIntakeCommand
{
    public string DisplayName { get; set; } = string.Empty;
    public string EmailAddress { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string LocalOffice { get; set; } = string.Empty;
    public string PreferredMentorId { get; set; } = string.Empty;
    public int? Age { get; set; }
    public string FavoriteColor { get; set; } = string.Empty;
    public string Bio { get; set; } = string.Empty;

    [WebFormMapFrom(nameof(ProfileIntakeForm.ProfileImage))]
    public string ProfileImageFileName { get; set; } = string.Empty;
}

public sealed class ProfileIntakeRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string DisplayName { get; set; } = string.Empty;
    public string EmailAddress { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string LocalOffice { get; set; } = string.Empty;
    public string PreferredMentorId { get; set; } = string.Empty;
    public int? Age { get; set; }
    public string FavoriteColor { get; set; } = string.Empty;
    public string Bio { get; set; } = string.Empty;
    public string ProfileImageFileName { get; set; } = string.Empty;
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}
