# Replier.Sdk (.NET)

.NET SDK for the [Replier.net](https://www.replier.net) WhatsApp API.
Targets .NET Standard 2.0 and .NET 6+, fully async with `CancellationToken` support.

## Install

```bash
dotnet add package Replier.Sdk
```

## Usage

```csharp
using Replier;

using var replier = new ReplierClient(new ReplierOptions
{
    InstanceId = "12345",
    Token = "your-instance-token",
    // only for ListInstances / NewInstance / RenewInstance:
    Email = "you@example.com",
    ApiKey = "your-account-api-key",
});

try
{
    var sent = await replier.SendMessageAsync("15551234567", "Hello!");
    Console.WriteLine(sent.GetProperty("id").GetString());

    await replier.SendFileAsync("15551234567", "https://example.com/invoice.pdf", "invoice.pdf", "Your invoice");
    await replier.CreateGroupAsync("Team", new[] { "15551234567", "15557654321" });
    await replier.SetWebhookAsync("https://example.com/replier/webhook");
}
catch (ReplierException e)
{
    Console.WriteLine($"{e.ErrorCode} {e.Message}");
}
```

Responses are `System.Text.Json.JsonElement`. In ASP.NET Core, pass an `HttpClient`
from `IHttpClientFactory` as the second constructor argument.
Call an endpoint that is not wrapped yet with
`CallAsync("someNewEndpoint", new Dictionary<string, object?> { ["param"] = "value" })`.
See the [method reference](../README.md#method-reference).

## Build & publish

```bash
dotnet pack src/Replier -c Release
dotnet nuget push src/Replier/bin/Release/Replier.Sdk.1.0.0.nupkg --api-key <NUGET_KEY> --source https://api.nuget.org/v3/index.json
```

## License

MIT
