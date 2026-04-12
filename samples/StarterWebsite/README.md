# Starter Website

This sample is the reference/demo website for `CL.WebLogic`.

It shows the toolkit through focused areas:

- a minimal ASP.NET Core host that hands requests to `CL.WebLogic`
- a CodeLogic application that registers the core website routes
- in-process plugins that register extra pages and APIs
- a request-aware theme with Bootstrap 5.3.8 and jQuery 4.0.0 bundled locally
- a live widget/dashboard area that refreshes its route and plugin data from the runtime
- a dedicated forms area for model-driven rendering, validation, upload rules, and lookups
- a dedicated templates/pages area for layouts, widgets, loops, page scripts, and metadata
- a dedicated auth/RBAC area for login and access checks

## App Layout

The starter application is now split by concern so the sample stays readable:

- `Application/StarterWebsiteApplication.cs`
  Small entry point that delegates route registration
- `Application/StarterWebsiteApplication.Startup.cs`
  startup hooks, seeding, and provider registration
- `Application/StarterWebsiteApplication.Helpers.cs`
  shared sample helpers and seed data helpers
- `Application/StarterWebsiteApplication.Widgets.cs`
  widget and widget-area registration
- `Application/StarterWebsiteApplication.Pages.cs`
  themed page registration
- `Application/StarterWebsiteApplication.Apis.cs`
  sample API registration
- `Application/StarterWebsiteApplication.Fallbacks.cs`
  fallback route registration

This is sample structure, not a required `CL.WebLogic` app shape.

## Demo Areas

- `Overview`
  `/` high-level entry page linking into the main toolkit slices
- `Request and page context`
  `/about`
- `Templates and page scripts`
  `/template-lab`
- `Forms and lookups`
  `/form-lab`
- `Widgets and areas`
  `/dashboard`
- `Saved dashboard layout`
  `/dashboard/studio`
- `Auth and RBAC`
  `/login`, `/rbac`, `/rbac/editor`, `/rbac/admin`
- `Plugins`
  `/plugins/theme-showcase`
- `Built-in WebLogic explorers`
  `/weblogic/apiexplorer`, `/weblogic/widgetareas`, related JSON APIs

## Routes

- `/` application-owned themed overview page
- `/about` application-owned themed page focused on request context
- `/plugins/theme-showcase` plugin-owned themed page with an RBAC demo block
- `/api/site` application-owned JSON API
- `/api/plugins/manifest` plugin-owned JSON API
- `/api/plugins/message` plugin-friendly message API that reads query and form values
- `/api/plugins/secure` RBAC-protected API
- `/weblogic/apiexplorer` built-in API explorer page
- `/api/weblogic/routes` built-in route registry API
- `/api/weblogic/plugins` built-in contributor registry API
- `/api/weblogic/apiexplorer` built-in API registry API

## Theme Layout

- `theme/assets/bootstrap/` local Bootstrap 5.3.8 CSS and JS bundles
- `theme/assets/jquery/` local jQuery 4.0.0 bundle
- `theme/assets/site.css` the custom demo styling layer
- `theme/assets/site.js` the starter-specific demo orchestration layer
- `/weblogic/client/weblogic.client.js` the shared `CL.WebLogic.Client` runtime served by the library
- `theme/templates/` the shared starter pages

## Notes

- The starter keeps the demo theme local so it can run without CDNs.
- Bootstrap provides the layout system, components, and interaction polish.
- jQuery powers the dashboard refresh flow and the runtime data panels.
- The shared browser toolkit now comes from the library itself instead of living only inside the sample theme.
- `CL.GitHelper`, `CL.StorageS3`, and `CL.NetUtils` are still part of the broader toolkit shape even though this starter stays focused on presentation and app-owned persistence.
- The theme is ready for a future SignalR live feed panel once the runtime hub lands.
- The starter still includes demo-only auth and dashboard choices on purpose. They are examples of using the toolkit, not hard requirements of `CL.WebLogic`.

## Quick Run

From the repo root:

```powershell
.\scripts\Run-Starter.ps1
```
