# Installed contract tests

> This project is disabled in `Okf-Todo.slnx` by default because it requires a Windows installation of OKF-Todo. Normal solution builds and test runs must not depend on which product version happens to be installed on the developer's computer.

These Windows-only black-box tests exercise only an installed OKF-Todo product:

- `Okf-Todo.exe --okf-command`
- `mcp\Okf-Todo.Mcp.exe`
- `okf\todo-database\index.md` and its installed context files
- disposable SQLite databases created under the test runner's temporary directory

The project has no references to OKF-Todo application projects. It must not use repository documentation, publish output, installer staging, or the user's normal database as product context.

The suite contains three tracks:

1. MCP task insertion and replacement updates through the official .NET MCP client.
2. Supported OKF command insertion and replacement updates, including adding an attachment.
3. OKF-guided direct SQLite insertion and updates, including a BLOB attachment. These tests explicitly prove that direct database writes bypass automatic application history.

## Run against the default installation

```cmd
dotnet test .\Okf-Todo.InstalledContractTests\Okf-Todo.InstalledContractTests.csproj -c Release
```

The default installation root is `%LOCALAPPDATA%\Programs\Okf-Todo`.

## Run against another installation directory

```cmd
set OKF_TODO_INSTALL_DIR=C:\Path\To\Installed\Okf-Todo
dotnet test .\Okf-Todo.InstalledContractTests\Okf-Todo.InstalledContractTests.csproj -c Release
```

The MCP installer component is mandatory. Missing installed files fail environment validation rather than skipping tests.

See [Installed contract test user stories](../docs/testing/installed-contract-test-user-stories.md) for the business scenarios covered by the suite.

See [Enable or disable the installed contract tests](../docs/testing/installed-contract-tests.md) for the solution commands and the difference between normal and installed-product test runs.
