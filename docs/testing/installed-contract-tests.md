# Enable or Disable the Installed Contract Tests

`Okf-Todo.InstalledContractTests` is disabled in `Okf-Todo.slnx` by default because it is a Windows-only black-box suite that requires OKF-Todo to be installed. A normal solution test run must remain independent of the product version installed on the developer's computer.

Disabling the project only removes it from the main solution. It does not delete the project or prevent an explicit test run.

## Default: disabled

With the project disabled, this command builds and tests the source projects without running installed-product tests:

```cmd
dotnet test .\Okf-Todo.slnx
```

The disabled solution contains:

- `Okf-Todo`
- `Okf-Todo.Mcp`
- `Okf-Todo.Tests`

## Run once without enabling it

This is the recommended way to verify the current Windows installation without changing the solution:

```cmd
dotnet test .\Okf-Todo.InstalledContractTests\Okf-Todo.InstalledContractTests.csproj -c Release
```

The default installation directory is:

```text
%LOCALAPPDATA%\Programs\Okf-Todo
```

The MCP installer component must be installed. Missing installed executables or OKF files cause the tests to fail rather than skip.

## Enable in the main solution

Enable the project when you deliberately want `dotnet test .\Okf-Todo.slnx` to include the installed-product contract tests:

```cmd
dotnet sln .\Okf-Todo.slnx add .\Okf-Todo.InstalledContractTests\Okf-Todo.InstalledContractTests.csproj
```

Confirm the solution membership:

```cmd
dotnet sln .\Okf-Todo.slnx list
```

The enabled solution test run now requires a compatible Windows installation:

```cmd
dotnet test .\Okf-Todo.slnx
```

## Disable again

Return the repository to its default configuration with:

```cmd
dotnet sln .\Okf-Todo.slnx remove .\Okf-Todo.InstalledContractTests\Okf-Todo.InstalledContractTests.csproj
```

Confirm that the installed-contract project is no longer listed:

```cmd
dotnet sln .\Okf-Todo.slnx list
```

## Why it is disabled by default

The suite tests an installation rather than the source tree. Its result depends on which installer version is currently installed, whether the MCP component was selected, and whether the installed OKF documentation matches the installed executables. Including it in every solution test run would therefore make ordinary development builds fail because of external machine state rather than a source-code defect.

Run the project explicitly after installing or upgrading OKF-Todo, when validating an installer, or when verifying the compatibility of the installed MCP and OKF contracts.
