\---

name: photino-feature

description: Use when adding or changing a Photino.NET desktop feature involving C# host code, HTML/CSS/JavaScript UI, local assets, or JavaScript-to-.NET messaging.

\---



When working on a Photino feature:



1\. First identify:

&#x20;  - the Photino startup/window configuration

&#x20;  - where local web assets are loaded from

&#x20;  - any JavaScript-to-.NET message bridge code

&#x20;  - the page/component/script affected by the feature



2\. Prefer small changes:

&#x20;  - keep host/window setup separate from feature logic

&#x20;  - keep JavaScript UI behavior close to the relevant HTML/assets

&#x20;  - avoid mixing packaging/build fixes with feature changes



3\. For .NET code:

&#x20;  - use dependency injection assumptions

&#x20;  - use ILogger logging, not Console.WriteLine

&#x20;  - do not introduce unnecessary classes or methods unless already consistent with the project



4\. After changes, run:

&#x20;  - dotnet restore

&#x20;  - dotnet build

&#x20;  - dotnet run --project <main Photino project>



5\. Report:

&#x20;  - files changed

&#x20;  - how to test manually

&#x20;  - any known limitation in the Photino/webview runtime

