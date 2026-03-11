# Allow2 SDK for .NET

Official Allow2 Parental Freedom SDK for .NET platforms.

| | |
|---|---|
| **Package** | `Allow2` |
| **Targets** | .NET Standard 2.1 / .NET 6+ |
| **Dependencies** | None (uses `System.Text.Json`, `System.Net.Http`) |
| **Language** | C# 10 (file-scoped namespaces, records) |

## Quick Start

```csharp
using Allow2;
using Allow2.Models;

// 1. Create the daemon
var daemon = new Allow2Daemon(
    vid: 123,                                       // your Allow2 vendor ID
    deviceToken: "unique-device-token",
    deviceName: "Living Room PC",
    environment: Allow2Environment.Production,
    credentialStore: new FileCredentialStore()       // or your own IAllow2CredentialStore
);

// 2. Start the daemon (loads stored credentials)
await daemon.StartAsync();

// 3. If not yet paired, initiate pairing (parent scans QR or enters PIN)
var session = await daemon.InitPairingAsync("Living Room PC", PairingMethod.PINCode);
Console.WriteLine($"Enter this PIN in your Allow2 app: {session.PinCode}");

// 4. Wait for parent to confirm
var pairing = await daemon.WaitForPairingAsync(session.SessionId);

// 5. Select a child
await daemon.SelectChildAsync(pairing.Children[0].Id);

// 6. Check permissions
var result = await daemon.CheckAsync(new[] {
    new ActivityCheck(Activity.Gaming),
    new ActivityCheck(Activity.ScreenTime)
});

if (!result.Allowed) {
    Console.WriteLine("Time's up!");
}
```

## Modules

| Module | File | Purpose |
|--------|------|---------|
| **API Client** | `Allow2Api.cs` | HttpClient-based REST client for all Allow2 endpoints |
| **Daemon** | `Allow2Daemon.cs` | Main orchestrator managing the full device lifecycle |
| **Pairing** | `Allow2Pairing.cs` | QR code and PIN code device pairing flows |
| **Credentials** | `Allow2Credentials.cs` | `IAllow2CredentialStore` interface and `FileCredentialStore` default |
| **Check** | `Allow2Check.cs` | Permission checking with activity stacking |
| **Request** | `Allow2Request.cs` | "Request More Time" flow with polling |
| **Feedback** | `Allow2Feedback.cs` | Submit/load/reply to feedback discussions |
| **Updates** | `Allow2Updates.cs` | Poll for children, quota, ban, and day type changes |
| **Models** | `Models/` | Records for all API types (Child, CheckResult, Activity, etc.) |

## Permission Checks

```csharp
// Check with logging (records usage on the server)
var result = await daemon.CheckAsync(new[] {
    new ActivityCheck(Activity.Gaming, Log: true),
    new ActivityCheck(Activity.ScreenTime, Log: true)
});

// Inspect per-activity results
foreach (var activity in result.Activities) {
    Console.WriteLine($"{activity.Name}: allowed={activity.Allowed}, remaining={activity.Remaining}min");
}

// Day type info
Console.WriteLine($"Today: {result.DayTypeToday}, Tomorrow: {result.DayTypeTomorrow}");
```

## Request More Time

```csharp
// Child requests 30 more minutes of gaming
var (requestId, statusSecret) = await daemon.CreateRequestAsync(
    childId: 682,
    activityId: (ulong)Activity.Gaming,
    minutes: 30,
    message: "Can I please have more time? I'm almost done with this level."
);

// Poll until parent responds (or use CreateAndPollAsync for automatic polling)
var status = await daemon.PollRequestStatusAsync(requestId, statusSecret);

if (status.Status == "approved") {
    Console.WriteLine($"Approved! {status.ExtensionMinutes} extra minutes granted.");
} else if (status.Status == "denied") {
    Console.WriteLine("Request denied.");
}
```

## Feedback

```csharp
// Submit feedback
ulong discussionId = await daemon.SubmitFeedbackAsync(
    category: FeedbackCategory.Bug,
    message: "The block screen appears even when time is remaining.",
    childId: 682
);

// Load feedback threads
var threads = await daemon.LoadFeedbackAsync();
foreach (var thread in threads) {
    Console.WriteLine($"[{thread.Category}] {thread.Status} - {thread.MessageCount} messages");
}

// Reply to a thread
await daemon.ReplyToFeedbackAsync(discussionId, "This happens every Tuesday.");
```

## Updates Polling

```csharp
// Fetch latest changes (children list, quotas, bans, day types, extensions)
var updates = await daemon.GetUpdatesAsync();

foreach (var child in updates.Children) {
    Console.WriteLine($"Child: {child.Name} (ID: {child.Id})");
}

foreach (var ext in updates.Extensions) {
    Console.WriteLine($"Extension: child {ext.ChildId}, activity {ext.Activity}, +{ext.AdditionalMinutes}min");
}
```

## Credential Storage

The SDK uses `IAllow2CredentialStore` for persisting pairing credentials. The default `FileCredentialStore` writes to `~/.config/Allow2/credentials.json` (or equivalent).

For production, implement the interface with platform-specific secure storage:

```csharp
public class SecureCredentialStore : IAllow2CredentialStore
{
    public async Task<bool> SaveAsync(Allow2StoredCredentials credentials) {
        // Windows: DPAPI, macOS: Keychain, Linux: Secret Service
        ...
    }

    public async Task<Allow2StoredCredentials?> LoadAsync() { ... }
    public async Task<bool> ClearAsync() { ... }
}
```

## Target Platforms

| Platform | Notes |
|----------|-------|
| **Unity** | .NET Standard 2.1 compatible; use with Unity 2021.2+ |
| **.NET MAUI** | Cross-platform mobile/desktop apps |
| **Windows Desktop** | WPF, WinForms, WinUI 3 |
| **Xbox** | Via UWP / GDK (.NET 6+) |
| **ASP.NET** | Server-side service integrations |
| **Avalonia UI** | Cross-platform desktop (Linux, macOS, Windows) |

## Architecture

The SDK follows the Allow2 Device Operational Lifecycle:

1. **Pairing** (one-time) -- QR code or PIN, parent never enters credentials on device
2. **Child Identification** (every session) -- OS account mapping or child selector with PIN
3. **Permission Checks** (continuous) -- POST to service URL every 30-60s with `log: true`
4. **Warnings & Countdowns** -- progressive alerts before blocking
5. **Request More Time** -- child requests, parent approves/denies from their phone
6. **Feedback** -- in-app bug reports and feature requests

All API communication uses `System.Text.Json` with no external dependencies. The check endpoint POSTs to the **service URL** (`service.allow2.com`), while all other endpoints use the **API URL** (`api.allow2.com`).

## License

See [LICENSE](LICENSE) for details.
