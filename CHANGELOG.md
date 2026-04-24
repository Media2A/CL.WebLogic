# Changelog

All notable changes to this project will be documented in this file. Going forward, versions follow [Semantic Versioning](https://semver.org/).

## [0.1.0] - 2026-04-24

First tagged release — packaging and CI groundwork.

### Added
- NuGet package metadata on `src/CL.WebLogic.csproj` (`PackageId`, `Version`, `Authors`, `Description`, `RepositoryUrl`, `License`, `PackageReadmeFile`).
- XML documentation generation (`GenerateDocumentationFile`), with CS1591 suppressed until public API is fully documented.
- Repo-root `.editorconfig` defining formatting, file-scoped namespaces, and private-field naming conventions.
- GitHub Actions `ci.yml`: builds + runs tests on push/PR to `main`; packs on `v*` tags and uploads the `.nupkg` as a build artifact. NuGet publish step is included but commented pending `NUGET_API_KEY`.
- `.gitignore` entries for `artifacts/`, `*.nupkg`, `*.snupkg`.

### Notes
- No source-code behavior changes in this release. Everything that worked before still works — this release exists to make the library packageable and give it a versioning baseline.

## 2026-04-07 Toolkit Snapshot

Current `CL.WebLogic` state before the cleanup/consolidation branch:

- Established `CL.WebLogic` as a standalone repo/library with CodeLogic lifecycle integration.
- Added ASP.NET Core handoff so WebLogic owns routing, request handling, and page rendering.
- Built the routing/contributor model for applications and plugins.
- Added plugin support for pages, APIs, widgets, widget areas, and theme-facing contributions.
- Added `WebRequestContext` / page context access for query, form, files, session, cookies, headers, auth state, and services.
- Added template engine v2 with layouts, sections, partials, conditionals, loops, widgets, and page scripts.
- Added `WebPageDocument` / metadata support for title, description, canonical, Open Graph, and Twitter tags.
- Added built-in explorer endpoints for routes, plugins, APIs, widgets, widget areas, and form providers.
- Added widget registry, widget areas, area targeting policies, persistent widget settings, widget data endpoints, and widget actions.
- Added dashboard/layout primitives plus demo dashboard composition and saved layouts.
- Added realtime/event infrastructure with SignalR integration and widget event publishing.
- Added auth/RBAC foundation with MySQL-backed identity support and access-group checks.
- Added starter login flow and RBAC demo pages.
- Added `CL.WebLogic.Client` starter runtime with navigation, head/meta syncing, AJAX forms, widget helpers, dashboard helpers, and realtime hooks.
- Added model-driven forms:
  - C# form definitions
  - generated HTML rendering
  - client schema generation
  - client/server validation
  - file and image validation
  - form-to-command/entity mapping
- Added provider-backed form fields for selects and dependent fields.
- Added autocomplete/search-backed form fields.
- Added generic HTTP/JSON lookup provider support for form options and autocomplete.
- Expanded the starter site into a broader reference/demo app for themes, plugins, widgets, dashboards, forms, auth, and realtime features.

Planned next direction after this snapshot:

- Consolidate `CL.WebLogic` as a cleaner toolkit/foundation.
- Separate core toolkit concerns from sample/demo behavior more clearly.
- Extract/package `CL.WebLogic.Client` more formally.
- Add tests and harden provider/runtime boundaries.
