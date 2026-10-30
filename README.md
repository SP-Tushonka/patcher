# Patcher

Generates and applies patches between two versions of a folder. Used to downgrade EFT to the client version SPT supports.

## Projects

| Project | What it is |
|---|---|
| `PatchGenerator` | Builds a patch from a source and a target folder, with a window or headless |
| `PatchClient` | `patcher.exe`, applies a patch to a copy of the game |
| `PatcherUtils` | The patching code both tools share. The SPT installer references it too. |
| `PatcherUtils.Tests` | xUnit tests for the patching round trip and the logger |
| `SPT.Theme` | The shared Avalonia theme and fonts, also used by the SPT installer |

## Requirements

- .NET 10 SDK
- Git LFS. `PatchClient.exe` is stored in LFS.

## Git LFS

LFS files are served from our own server, not GitHub. Every commit has a `.lfsconfig` that points at it:

```
https://lfs.sp-tushonka.com/sp-tushonka/patcher
```

A normal clone needs nothing extra:

```bash
git lfs install
git clone https://github.com/SP-Tushonka/patcher
```

Downloads need no login. Pushing LFS files asks for your GitHub username and a token with push access to this repository.

## PatchClient parameters

`patcher.exe` takes one optional parameter. It is not case sensitive.

```
patcher.exe autoclose
```

- `autoclose`: close when done and return an exit code instead of waiting for the user

## PatchGenerator parameters

Parameters are passed as `name::value`. Names are not case sensitive.

```
PatchGenerator.exe "SourceFolderPath::C:\EFT\old" "TargetFolderPath::C:\EFT\new" "OutputFolderName::patch" "Headless::true"
```

- `SourceFolderPath`: the folder the patch is applied to
- `TargetFolderPath`: the folder the patch turns it into
- `OutputFolderName`: the folder the patch is written to
- `AutoZip`: compress the output to a `.7z` archive named `Patcher_<source>_to_<target>`. Defaults to `true`.
- `AutoClose`: close when done and return an exit code. Defaults to `false`.
- `Headless`: run without a window and write progress to the console. Defaults to `false`.

## Exit codes

```cs
public enum PatcherExitCode
{
    ProgramClosed  = 0,  // closed by the user
    Success        = 10, // patching or patch generation succeeded
    EftExeNotFound = 11, // the EFT exe was not found (patch client only)
    NoPatchFolder  = 12, // no patch folder was found (patch client only)
    MissingFile    = 13, // a file to patch was not found (patch client only)
    MissingDir     = 14, // the source, target or output folder was not found (patch generator only)
    PatchFailed    = 15  // a patch failed to apply, or the output could not be compressed
}
```
