# CL.WebLogic

`CL.WebLogic` is a web application toolkit for CodeLogic3.

It is intentionally being shaped as a foundation, not a monolithic CMS product.

## Structure

The repo is moving toward three clear layers:

- `src/`
  The server-side `CL.WebLogic` toolkit:
  routing, request context, theming, widgets, forms, metadata, realtime, auth abstractions, and dashboard primitives.
- `src/Client/`
  The shared browser-side `CL.WebLogic.Client` runtime owned by the library itself.
- `samples/StarterWebsite/`
  A reference/demo site that consumes the toolkit and shows patterns for pages, plugins, forms, widgets, dashboards, auth, and realtime features.
- `tests/`
  Focused toolkit tests for stable seams such as forms and widget registries.

## Current Focus

The current cleanup/consolidation direction is:

- keep core toolkit code inside `src/`
- keep demo-specific behavior inside `samples/StarterWebsite/`
- reduce duplicate sample-only implementations
- make the client runtime a real toolkit surface instead of only a starter asset
- prepare the repo for better tests, docs, and packaging

## Starter Site

The starter demo is intentionally broad, but it is not the source of truth for the toolkit itself.

Use it as:

- a reference application
- a feature showcase
- a place to test integration patterns

Not as:

- the architectural definition of every `CL.WebLogic` feature
- the only location of reusable client/runtime code

The sample app is also being split by concern so it is easier to navigate:

- `StarterWebsiteApplication.Startup.cs`
- `StarterWebsiteApplication.Helpers.cs`
- `StarterWebsiteApplication.Widgets.cs`
- `StarterWebsiteApplication.Pages.cs`
- `StarterWebsiteApplication.Apis.cs`
- `StarterWebsiteApplication.Fallbacks.cs`

That layout is meant to keep demo code readable without turning the sample into the toolkit.

## Tests

The test suite is intentionally starting small and focused.

Current coverage includes:

- form definition, schema overrides, and rendering primitives
- widget descriptor and widget-area registry behavior

Run the current test project with:

```powershell
dotnet test .\tests\CL.WebLogic.Tests\CL.WebLogic.Tests.csproj -p:UseSharedCompilation=false
```

## Quick Commands

For local day-to-day work:

```powershell
.\scripts\Run-Starter.ps1
.\scripts\Test-Toolkit.ps1
```

## Snapshot

See `CHANGELOG.md` for the current toolkit snapshot before the cleanup pass.
