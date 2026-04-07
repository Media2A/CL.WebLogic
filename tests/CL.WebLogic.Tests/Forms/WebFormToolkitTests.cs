using CL.WebLogic.Forms;
using Xunit;

namespace CL.WebLogic.Tests.Forms;

[WebForm(Id = "tests.profile", Name = "Profile Intake", Description = "Toolkit test form.")]
public sealed class TestProfileForm
{
    [WebFormField(
        Label = "Display name",
        Required = true,
        MinLength = 3,
        MaxLength = 40,
        Placeholder = "Claus",
        Section = "Identity",
        HelpText = "Shown in the starter shell.")]
    public string DisplayName { get; set; } = string.Empty;

    [WebFormField(
        Label = "Favorite color",
        InputType = WebFormInputType.Select,
        SelectPrompt = "Pick a palette")]
    public string FavoriteColor { get; set; } = string.Empty;
}

public sealed class WebFormToolkitTests
{
    [Fact]
    public void GetDefinition_MapsAttributesIntoDefinition()
    {
        var definition = WebFormBinder.GetDefinition<TestProfileForm>();

        Assert.Equal("tests.profile", definition.Id);
        Assert.Equal("Profile Intake", definition.Name);

        var displayName = Assert.Single(definition.Fields, field => field.Name == nameof(TestProfileForm.DisplayName));
        Assert.True(displayName.Required);
        Assert.Equal(3, displayName.MinLength);
        Assert.Equal(40, displayName.MaxLength);
        Assert.Equal("Identity", displayName.Section);
        Assert.Equal("Shown in the starter shell.", displayName.HelpText);
    }

    [Fact]
    public void ResolveDefinition_AppliesSchemaOverrides()
    {
        var definition = WebFormBinder.GetDefinition<TestProfileForm>();
        var resolved = WebFormBinder.ResolveDefinition(definition, new WebFormSchemaOptions
        {
            FieldOverrides = new Dictionary<string, WebFormFieldOverride>(StringComparer.OrdinalIgnoreCase)
            {
                [nameof(TestProfileForm.FavoriteColor)] = new WebFormFieldOverride
                {
                    HelpText = "Resolved at render time.",
                    Options =
                    [
                        new WebFormSelectOption { Value = "amber", Label = "Amber Glow" },
                        new WebFormSelectOption { Value = "teal", Label = "Teal Current" }
                    ]
                }
            }
        });

        var favoriteColor = Assert.Single(resolved.Fields, field => field.Name == nameof(TestProfileForm.FavoriteColor));
        Assert.Equal("Resolved at render time.", favoriteColor.HelpText);
        Assert.Collection(
            favoriteColor.Options,
            option =>
            {
                Assert.Equal("amber", option.Value);
                Assert.Equal("Amber Glow", option.Label);
            },
            option =>
            {
                Assert.Equal("teal", option.Value);
                Assert.Equal("Teal Current", option.Label);
            });
    }

    [Fact]
    public void RenderForm_IncludesToolkitAttributesAndValidationOutput()
    {
        var definition = WebFormBinder.ResolveDefinition(
            WebFormBinder.GetDefinition<TestProfileForm>(),
            new WebFormSchemaOptions
            {
                FieldOverrides = new Dictionary<string, WebFormFieldOverride>(StringComparer.OrdinalIgnoreCase)
                {
                    [nameof(TestProfileForm.FavoriteColor)] = new WebFormFieldOverride
                    {
                        Options =
                        [
                            new WebFormSelectOption { Value = "amber", Label = "Amber Glow" }
                        ]
                    }
                }
            });

        var html = WebFormRenderer.RenderForm(
            definition,
            new WebFormRenderOptions
            {
                Action = "/api/forms/test-profile",
                SchemaId = "tests.profile.schema"
            },
            new WebFormRenderState
            {
                Values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
                {
                    [nameof(TestProfileForm.DisplayName)] = "Claus",
                    [nameof(TestProfileForm.FavoriteColor)] = "amber"
                },
                Errors =
                [
                    new WebFieldValidationError
                    {
                        FieldName = nameof(TestProfileForm.DisplayName),
                        Code = "required",
                        Message = "Display name is required."
                    }
                ]
            });

        Assert.Contains("data-weblogic-form=\"ajax\"", html);
        Assert.Contains("data-weblogic-form-id=\"tests.profile\"", html);
        Assert.Contains("data-weblogic-form-schema=\"tests.profile.schema\"", html);
        Assert.Contains("Amber Glow", html);
        Assert.Contains("Display name is required.", html);
        Assert.Contains("value=\"Claus\"", html);
    }
}
