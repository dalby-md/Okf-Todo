# Browser UI tests

The browser UI tests exercise the real files from `Okf-Todo/wwwroot` in Microsoft Edge through Playwright. The browser bridge is connected to the application's real `BridgeMessageHandler`, services, EF Core model, and a temporary SQLite database.

The tests never open the user's OKF-Todo database. Each run creates an isolated database under the system temporary directory and deletes it afterwards.

Run the UI tests on Windows with Microsoft Edge installed:

```cmd
dotnet test .\Okf-Todo.UiTests\Okf-Todo.UiTests.csproj -c Release
```

`SaveNewTask_WithoutMainSave_PersistsTaskEnablesControlsAndFocusesEditor` verifies the complete New task workflow in both HTML and Markdown modes:

1. Open the New task dialog.
2. Enter a title and select **Save**.
3. Verify that the modal closes only after the task has been persisted and read back.
4. Verify that checklist, attachment, comment, relationship, Complete, and Cancel controls are enabled without another save.
5. Add a checklist item, attachment, and comment.
6. Query the isolated SQLite database and verify that the task and added content were persisted.
7. Verify that the browser loaded the startup-versioned `app.js` and that no `task.update` message was sent through the main **Save** action.
8. Verify that keyboard focus moves into the active body editor when the modal closes.
