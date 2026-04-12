# Authentication

CL.WebLogic provides pluggable authentication with session-based auth, RBAC (role-based access control), and permission resolution.

## Identity Store

Implement `IWebIdentityStore` to provide user lookup and credential validation:

```csharp
public class MyIdentityStore : IWebIdentityStore
{
    public Task<WebIdentityProfile?> GetIdentityAsync(string userId) { ... }
    public Task<WebIdentityProfile?> ValidateCredentialsAsync(string username, string password) { ... }
}
```

Register it during startup:

```csharp
WebLogicLibrary.GetRequired().IdentityStore = new MyIdentityStore();
```

## Session Auth

WebLogic uses ASP.NET Core sessions. Set session values to sign in:

```csharp
request.SetSessionValue("weblogic.user_id", profile.UserId);
request.SetSessionValue("weblogic.access_groups", "member,admin");
```

## Access Groups (RBAC)

Restrict routes to specific groups:

```csharp
context.RegisterPage("/admin", new WebRouteOptions
{
    AllowAnonymous = false,
    RequiredAccessGroups = ["admin"]
}, handler, "GET");
```

Check in templates:

```html
{if:auth}Logged in{/if}
{if:accessgroup:admin}Admin content{/if}
{if:permission:content.edit}Edit button{/if}
```

## Permission Resolver

For custom permission logic, set an external resolver:

```csharp
WebRequestIdentity.ExternalPermissionResolver = (userId, permission) =>
{
    return MyPermissionCache.HasPermission(userId, permission);
};
```

## Request Context

Access auth info in handlers:

```csharp
request.IsAuthenticated    // bool
request.UserId             // string
request.AccessGroups       // IEnumerable<string>
request.HasPermission("x") // bool
```
