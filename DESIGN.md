# First-principles design

## Problem definition

“直连” is not a property of CokeCloud or any other proxy client. It is a routing decision for a request made by a target application:

```text
target application -> name resolution/connection -> proxy selection -> remote service -> observable evidence
```

The tool is responsible only for the proxy-selection seam and the evidence loop. It does not edit the proxy client's private configuration and does not edit the target application's database.

## Scope hierarchy

| Strategy | Control unit | Side effects | Default |
|---|---|---|---|
| Direct domain exception | Current-user WinINet domain bypass | Other WinINet apps may share it | Preferred |
| Broad fallback | Current-user WinINet `ProxyEnable=0` | All WinINet apps using system proxy | Manual fallback |
| Strict process routing | WFP/proxy backend/driver | Requires a separate privileged backend | Not claimed |

The distinction is deliberate. A normal EXE cannot safely promise strict process-level routing merely by editing `ProxyOverride`.

## Internal modules

- `TargetProfile`: app launch ID, package name, log directory, direct domains.
- `ProfileStore`: user-local configuration; never committed to GitHub.
- `RouteEngine`: WinINet read/write, domain exception merge, rollback, target restart, log observation.
- `MainForm`: presentation and explicit confirmation of policy scope.

The external interface stays small: diagnose, apply domain policy, apply broad fallback, save/restore rollback, restart target. The implementation can later swap in WFP or a proxy-backend adapter without changing the user-facing policy model.

## Verification contract

Authentication success is not sync proof. A successful diagnosis should prefer target-specific evidence such as connection-open and content-update events; remote multi-device content still requires a second-device or web check.
