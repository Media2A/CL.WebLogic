# Starter Website

This sample is the polished demo website for `CL.WebLogic`.

It shows:

- a minimal ASP.NET Core host that hands requests to `CL.WebLogic`
- a CodeLogic application that registers the core website routes
- in-process plugins that register extra pages and APIs
- a request-aware theme with Bootstrap 5.3.8 and jQuery 4.0.0 bundled locally
- a live demo dashboard that refreshes its route and plugin data from the runtime

## Routes

- `/` application-owned themed homepage with live dashboard panels
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
- `theme/assets/site.js` the progressive-enhancement dashboard script
- `theme/templates/` the shared starter pages

## Notes

- The starter keeps the demo theme local so it can run without CDNs.
- Bootstrap provides the layout system, components, and interaction polish.
- jQuery powers the dashboard refresh flow and the runtime data panels.
- `CL.GitHelper`, `CL.StorageS3`, `CL.NetUtils`, and `CL.MySQL2` are still part of the runtime shape even though the theme itself stays focused on presentation.
- The theme is ready for a future SignalR live feed panel once the runtime hub lands.
