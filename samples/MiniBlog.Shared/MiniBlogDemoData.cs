namespace MiniBlog.Shared;

public sealed record MiniBlogDemoUser(
    string UserId,
    string Handle,
    string DisplayName,
    string Email,
    string Password,
    IReadOnlyList<string> AccessGroups);

public static class MiniBlogDemoData
{
    public static readonly MiniBlogDemoUser Admin = new(
        "5b26db14-eb79-45ee-a49f-76f13a999fdc",
        "admin",
        "Claus Admin",
        "admin@miniblog.local",
        "admin123",
        ["admin", "editor", "member"]);

    public static readonly MiniBlogDemoUser Editor = new(
        "adf579b1-dae3-456d-95a6-87dbaf9c0b08",
        "editor",
        "Mia Editor",
        "editor@miniblog.local",
        "editor123",
        ["editor", "member"]);

    public static readonly IReadOnlyList<MiniBlogDemoUser> Users =
    [
        Admin,
        Editor
    ];

    public static readonly IReadOnlyList<MiniBlogSeedPost> Posts =
    [
        new(
            "e13ddce9-4066-44cb-90eb-a9f40ff20e7d",
            "building-a-web-toolkit-with-codelogic",
            "Building a web toolkit with CodeLogic",
            "What changed when the site runtime stopped being \"just ASP.NET pages\" and became its own toolkit.",
            """
            <p>MiniBlog exists to prove that <strong>CL.WebLogic</strong> can host a real application shape, not just a starter demo.</p>
            <p>The public blog stays lightweight, while the administration surface lives in a plugin and is auto-registered at startup.</p>
            <p>That split gives us a much more believable story for pages, metadata, RBAC, widgets, and forms.</p>
            """,
            "published",
            Admin.UserId,
            Admin.DisplayName,
            "Building a web toolkit with CodeLogic",
            "A realistic MiniBlog sample showing how CL.WebLogic can power a public site plus a plugin-owned admin area.",
            new DateTimeOffset(2026, 4, 1, 9, 30, 0, TimeSpan.Zero)),
        new(
            "858e3e9d-1e07-4ab0-bf09-85d06a7248d4",
            "why-the-admin-lives-in-a-plugin",
            "Why the admin lives in a plugin",
            "The editor area is not hardcoded into the app. It is contributed by a separate plugin DLL to prove the real integration story.",
            """
            <p>The administration site is intentionally <em>not</em> baked into the MiniBlog application assembly.</p>
            <p>Instead, a separate plugin registers its own routes, APIs, widgets, and editor form flow through the same WebLogic contracts.</p>
            <p>That gives the sample a real modular architecture instead of a fake plugin story.</p>
            """,
            "published",
            Editor.UserId,
            Editor.DisplayName,
            "Why the admin lives in a plugin",
            "A look at why the MiniBlog admin area is implemented as a CL.WebLogic plugin instead of app-only routes.",
            new DateTimeOffset(2026, 4, 4, 14, 15, 0, TimeSpan.Zero)),
        new(
            "43c66b54-b659-4e40-8f0f-6514f6ec3f76",
            "server-side-rendering-still-feels-fast",
            "Server-side rendering still feels fast",
            "Why this sample still feels snappy even while the runtime owns routing, metadata, widgets, and auth.",
            """
            <p>One of the nicest side effects of the MiniBlog architecture is how direct the rendering path feels.</p>
            <p>The app returns content through WebLogic templates without dragging a much larger page framework into every view.</p>
            <p>That keeps the sample readable while still leaving room for widgets, metadata, RBAC, and plugins.</p>
            """,
            "published",
            Admin.UserId,
            Admin.DisplayName,
            "Server-side rendering still feels fast",
            "A MiniBlog note on why the CL.WebLogic rendering path still feels direct and responsive.",
            new DateTimeOffset(2026, 4, 5, 8, 0, 0, TimeSpan.Zero)),
        new(
            "0cc6ac19-d702-4068-9cdf-c0f3bf80ad91",
            "themes-are-where-the-personality-shows-up",
            "Themes are where the personality shows up",
            "The public MiniBlog theme is intentionally editorial so the sample feels like a real site, not just a technical proof.",
            """
            <p>Reference apps matter more when they feel intentional.</p>
            <p>That is why the MiniBlog theme leans into warm neutrals, serif headlines, and a strong split between the public magazine feel and the plugin-owned admin surface.</p>
            <p>The toolkit should stay generic, but the samples should still have character.</p>
            """,
            "published",
            Editor.UserId,
            Editor.DisplayName,
            "Themes are where the personality shows up",
            "Why the MiniBlog sample uses a distinct editorial theme to prove that CL.WebLogic can feel intentional.",
            new DateTimeOffset(2026, 4, 6, 11, 45, 0, TimeSpan.Zero)),
        new(
            "3d5cd8ec-b920-4a4d-bb16-6b49fc4d4cd0",
            "draft-posts-and-editor-flow",
            "Draft posts and editor flow",
            "A preview of the editor flow that the admin plugin manages through CL.WebLogic forms and RBAC.",
            """
            <p>This post starts as a draft so the public site can prove that only published content is rendered outside the admin plugin.</p>
            <p>The editor plugin can still list, edit, and publish it through the shared blog data service.</p>
            """,
            "draft",
            Editor.UserId,
            Editor.DisplayName,
            "Draft posts and editor flow",
            "A draft-only MiniBlog post used to demonstrate the editor workflow in the admin plugin.",
            null)
    ];
}

public sealed record MiniBlogSeedPost(
    string Id,
    string Slug,
    string Title,
    string Summary,
    string BodyHtml,
    string Status,
    string AuthorUserId,
    string AuthorDisplayName,
    string MetaTitle,
    string MetaDescription,
    DateTimeOffset? PublishedUtc);
