# Terminal calls

Run any external tool from a step. The typed tool surfaces cover `dotnet`, `git`, and `gh`; use `IRunContext.Terminal` for anything else.

## Run a tool

Call `RunAsync` with a request, then check the result:

```csharp
using Plutonium.Core.Process;
using Plutonium.Helpers.Entries.Definitions;
using Plutonium.Helpers.Entries.Extensions;

public BuildEntry Compile => definition => definition
    .AppId("myapp")
    .Execute(async context =>
    {
        var result = await context.Terminal.RunAsync(new PlutoniumProcessRequest("npm", "ci"));
        result.EnsureSucceeded();
    });
```

On Windows, `.cmd` tool shims (for example `npm`) require a shell: use `new PlutoniumProcessRequest("cmd", "/c npm ci")`; the direct form above is the POSIX pattern.

`RunAsync` captures standard output and error with secrets masked, then forwards non-empty output to the run log after the tool exits — standard output at Information, standard error at Warning. Non-zero exit codes do not throw on their own; `EnsureSucceeded` throws with the captured standard error in the message. Forwarded output is passed through to the run log after masking; in CI, a tool's output becomes part of the job log and may be interpreted by the CI provider's own log processors. Sources: `src/Plutonium.Core/Process/PlutoniumTerminal.cs:61-78`, `src/Plutonium.Core/Process/PlutoniumProcessResult.cs:49-59`.

## Contract

| Aspect | Behavior |
| ------ | -------- |
| Request | `new PlutoniumProcessRequest(fileName, arguments, workingDirectory?, environmentVariables?, timeout?)` |
| Shell | None — call `sh -c` / `cmd /c` yourself when you need one. |
| Arguments | One raw string; quoting for your OS is your job. |
| Environment | Request variables merge over the inherited environment. |
| Working directory | `null` inherits the current process directory. |
| Timeout | Must be greater than zero. On timeout the process tree is killed and captured output is discarded; the `TimeoutException` carries the diagnostic. |
| Output | Captured after the tool exits, never streamed. Standard input is not redirected or managed; interactive tools are unsupported and may read the inherited console or block. |
| Result | `ExitCode`, `StandardOutput`, `StandardError`, and `Succeeded`; never null. |
| Failures | `EnsureSucceeded` throws when the exit code is non-zero. |

`Terminal` is never null on a run context: `IRunContext.Terminal`. Sources: `src/Plutonium.Core/Process/PlutoniumProcessRunner.cs:18-79` (request validation), `:113-200` (run, capture, mask, timeout), `src/Plutonium.Core/Process/PlutoniumProcessResult.cs:18-28` (result properties), `src/Plutonium.Helpers/Runtime/IRunContext.cs:34-35` (terminal property).

Output masking uses the values registered from your declared secrets — see [Secrets](secrets.md).
