# Tests

The `tests/` folder is for focused toolkit verification, not end-to-end starter-site walkthroughs.

Current test areas:

- `CL.WebLogic.Tests/Forms`
  Form definition, schema override, and rendering behavior
- `CL.WebLogic.Tests/Theming`
  Widget and widget-area registry behavior

Current goal:

- cover stable toolkit seams first
- avoid coupling tests too tightly to the starter demo
- grow outward from low-friction unit-style coverage into broader integration tests

Run the current suite with:

```powershell
dotnet test .\tests\CL.WebLogic.Tests\CL.WebLogic.Tests.csproj -p:UseSharedCompilation=false
```

Or use:

```powershell
.\scripts\Test-Toolkit.ps1
```
