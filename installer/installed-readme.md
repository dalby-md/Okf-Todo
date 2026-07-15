# OKF-Todo installed integration

The desktop application, command adapter, MCP server, and OKF context use the same personal SQLite database:

```text
%LOCALAPPDATA%\Okf-Todo\okf-todo.db
```

## OKF context

The installed Open Knowledge Format context graph starts at:

```text
..\okf\todo-database\index.md
```

The graph describes the SQLite schema, relationships, integrity rules, lifecycle rules, and supported command interface. SQLite remains the source of task data.

## Application command adapter

Send one JSON command on standard input to:

```text
..\Okf-Todo.exe --okf-command
```

Application logs are written to standard error and the JSON response is written to standard output.

## MCP server

When the optional MCP component is installed, `mcp-config.json` in this directory contains a ready-to-copy MCP client configuration using the absolute installed path of `..\mcp\Okf-Todo.Mcp.exe`. The server is a stdio process started on demand by the MCP client; it is not a Windows service.

To install or remove the MCP component later, run the same OKF-Todo installer again and change the **Install MCP server** selection.
