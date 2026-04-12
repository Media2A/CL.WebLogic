# CL.WebLogic Guidance

## Purpose

`CL.WebLogic` is a toolkit and foundation for building web applications on top of CodeLogic.

It should provide strong runtime primitives and reusable helpers, not force one storage model,
one database, one CMS shape, or one application policy.

## Core Design Rule

Keep the toolkit generic.

That means `CL.WebLogic` should contain:

- startup and ASP.NET handoff/runtime ownership
- request handling and page context
- routing and API registration
- template rendering
- widgets and widget areas
- forms, validation, and mapping helpers
- metadata/head rendering
- realtime/event helpers
- plugin contribution contracts
- dashboard/layout models and helper methods
- client-side toolkit/runtime

That also means `CL.WebLogic` should avoid owning:

- MySQL-specific persistence policy
- app-specific dashboard save strategy
- app-specific auth/user storage assumptions
- app-specific CMS/content storage assumptions
- hardcoded user-bound dashboard behavior

## Persistence Rule

The toolkit may define contracts and helper methods for persistence-backed features, but the
actual persistence implementation should belong to the application or an optional provider layer.

Examples:

- dashboard layout sorting/order:
  keep the models and helper methods in the toolkit
  save/load the layout in the app
- widget settings:
  generic store contract is fine
  app decides file/json/database strategy
- auth identity store:
  toolkit exposes interfaces and auth flow helpers
  app provides the actual store implementation
- audit/request logging:
  toolkit supports the hook
  app decides whether to save anything and where

## Database Rule

Do not tightly couple `CL.WebLogic` core to MySQL or any other single database.

Use this principle:

- toolkit core = generic contracts + helpers
- sample/reference apps = concrete persistence choices
- optional provider layers are acceptable only when they stay clearly outside the core toolkit

`MiniBlog` is allowed to use `CL.MySQL2` heavily because it is a sample application.
`StarterWebsite` should stay lighter and demonstrate app-owned/local persistence where that makes sense.

## Dashboard Rule

Dashboard support in the toolkit should be about tools, not policy.

Keep in the toolkit:

- dashboard layout models
- sorting/order helpers
- render helpers
- client-side dashboard helpers
- get/save helper methods that use an app-supplied store

Do not assume in the toolkit:

- every dashboard is per-user
- every dashboard is stored in MySQL
- every app needs a dashboard studio

If an app wants per-user dashboard persistence, it should implement that itself.
If an app wants local JSON persistence, that is also valid.

## Plugin Rule

Plugins should be able to contribute web features automatically through generic contracts.

Examples of valid plugin contribution areas:

- routes
- APIs
- widgets
- widget areas
- page scripts
- form option/search providers
- realtime/event contributors

The toolkit should support auto-registration of loaded web-capable plugins, but should not make
app-specific assumptions about what those plugins store or how they persist state.

## File Organization

Prefer small focused files organized by concern.

Toolkit folders should stay grouped by domain, for example:

- `src/Runtime`
- `src/Routing`
- `src/Theming`
- `src/Forms`
- `src/Security`
- `src/Realtime`
- `src/AspNetCore`

Sample apps should keep their own persistence code in app/shared layers, not in toolkit core.

## Decision Reminder

When adding new features, ask:

1. Is this a generic web/runtime tool?
   If yes, it likely belongs in `CL.WebLogic`.
2. Is this a persistence policy or app-specific behavior?
   If yes, it likely belongs in the sample app or app layer.
3. Is the toolkit starting to assume a specific database or product shape?
   If yes, stop and move that decision outward.
