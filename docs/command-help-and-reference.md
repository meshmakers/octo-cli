---
description: Three-level help, command groups, GetDocumentation samples and notes, and the pipeline that publishes the command reference.
applies_to: src/CommandReferenceGenerator/**, tests/CommandReferenceGenerator.Tests/**, src/ManagementTool/Commands/*.cs
---

# Command help and generated command reference

## Help Options

The help flag is served by `CommandParser` / `ParserService` in
`Meshmakers.Common.CommandLineParser` (repo `mm-common`), not by octo-cli itself. It is
implicit: no command declares it, every command understands it.

> **Top-level arguments.** Besides `-c`/`--command` and the help flag, octo-cli declares one
> argument on the parser's top layer: `--context` (see
> [Per-invocation context override](#per-invocation-context-override---context)). It is understood
> by every command, in any position, and is listed in the full usage output. Commands themselves
> keep declaring their arguments via `CommandArgumentValue.AddArgument`; a top-level argument is
> added in `OctoCommandParser`, and only for something that genuinely applies to all commands.

The help has three levels, so nobody has to page through all 176 commands to find one:

```bash
# 1. Which groups are there? (~19 lines)
octo-cli -h

# 2. Which commands are in a group? Verb + description, no arguments (~94 lines for the largest group)
octo-cli -h "Identity Services"
octo-cli -h identity          # prefix match, case-insensitive
octo-cli -h Identity Services # quoting is optional, the words are joined
octo-cli -h General

# 3. What does one command take? Arguments, samples and notes (~15 lines)
octo-cli -c Create --help
octo-cli -h Create            # a topic that names a command goes straight to its help

# Everything at once, as before (~1210 lines)
octo-cli -h all
octo-cli                      # same dump on the error path: the mandatory -c is missing
```

| Input | Output | Exit |
|---|---|---|
| `-h` | group overview | 0 |
| `-h <group>` | commands of that group | 0 |
| `-h <command>` / `-c <command> -h` | help of that command | 0 |
| `-h all` | full usage listing | 0 |
| `-h <unknown>` | error naming the known groups | -1 |
| *(no arguments)* | full usage listing after the missing-argument error | -1 |

Accepted terms are `--help`, `-help`, `/help`, `-?`, `/?` and `-h` (case-insensitive). A
topic is resolved in this order: reserved `all` → exact group name → exact command name →
unique group prefix → unique group substring. Ambiguous shortenings resolve to nothing and
are reported, so `-h e` fails rather than guessing. Fuzzy matching applies to groups only —
`-h Crea` is an error, not `Create`.

**`-h` is claimed by the command first.** Declared arguments are matched before the help
flag, so a command owning `-h` keeps it — `octo-cli -c AddAdIdentityProvider -h myhost`
still binds `--host`. Use `--help` or `-?` for those commands. Give a new `AddArgument`
the short term `h` only when the command genuinely needs it; it costs the command its
`-h` shortcut for help.

Behaviour worth knowing:

- A help request suppresses validation of mandatory arguments and argument values, so
  `-c Create --help` prints help instead of complaining about the missing `-tid`.
- Help exits with code `0` and executes nothing — safe in scripts.
- Position does not matter: `--help -c Create` behaves like `-c Create --help`.
- Without help, everything is unchanged: missing mandatory arguments still fail with
  exit code `-1` followed by the full usage.
- The `NOTES` section comes from `GetDocumentation()`'s `Notes`; `SAMPLES` is filtered to
  the samples of that one command.

**Groups come from the `base(...)` call**, not from a registry: the first argument of the
`Command<T>` constructor, in practice one of the `Constants.*Group` values
(`Constants.cs:13-21`). A command using the constructor overload without a group lands in
`General`. A new group therefore exists as soon as one command names it — nothing else to
register, and it shows up in `-h` automatically.

Two names are worth avoiding for a new group: `all` is reserved for the full listing and
would make the group unreachable, and a group named exactly like a command loses nothing
but renders an extra pointer line (the group wins the name because a command is still
reachable via `-c <name> --help`, whereas a group has no second way in).

## Command Reference Documentation

All command documentation lives in the **command class itself** — there are no sidecar `.docs.md` files. `CommandReferenceGenerator` (Roslyn-based) parses each `.cs` file under `src/ManagementTool/Commands/Implementations/**`, extracts metadata from the constructor (`base(...)` call + `AddArgument(...)` assignments), and walks an optional `GetDocumentation()` override for richer per-command content. The rendered Markdown is published to `octo-documentation` via the `documentation-main-collect` release pipeline.

### What gets extracted automatically

- **Verb** — second-to-last constructor argument (or last, for the no-group ctor overload).
- **Description** — last constructor argument; the renderer uses it as the page's intro paragraph.
- **Group** — first constructor argument (when present); used as the sidebar category.
- **Arguments** — every `_field = CommandArgumentValue.AddArgument(...)` call in the ctor. The field name is captured so `GetDocumentation()` samples can refer back to it.
- **Inherited args** — for commands deriving from `JobWithWaitOctoCommand`, the `-w`/`--wait` flag is appended automatically (mirrored from the base class via `RoslynExtractor.InheritedArgsByBaseClass`).
- **Canonical example** — auto-built from the required arguments (`octo-cli -c <verb> -<short> <long>...`) when no `GetDocumentation()` override is present.

### `GetDocumentation()` override

When the auto-generated content isn't enough, override `GetDocumentation()` on `Command<T>` (defined in `Meshmakers.Common.CommandLineParser`):

```csharp
using Meshmakers.Common.CommandLineParser;

internal class FooCommand : ServiceClientOctoCommand<IFooClient>
{
    private readonly IArgument _targetArg;

    public FooCommand(/* ... */)
        : base(logger, Constants.SomeGroup, "Foo", "Does foo.", options, fooClient, authenticationService)
    {
        _targetArg = CommandArgumentValue.AddArgument("t", "target", ["Target id"], true, 1);
    }

    public override CommandDocumentation? GetDocumentation() =>
        new(
            Samples:
            [
                // Reference _targetArg by field — renaming "t"/"target" in AddArgument auto-updates the sample.
                new CodeSample(
                    arguments: [new CodeSampleArgument(_targetArg, "bar")],
                    description: "Basic usage"),
                new CodeSample(
                    arguments: [new CodeSampleArgument(_targetArg, "baz")],
                    description: "Verbose with expected output",
                    expectedOutput: """
                    ID    NAME
                    42    baz
                    """),
            ],
            Notes:
            [
                "Requires the caller to be in the `TenantOwners` group.",
                "Idempotent — re-running with the same arguments produces the same result.",
            ]);

    public override async Task Execute() { /* ... */ }
}
```

### Conventions

- Use **explicit type names** (`new CodeSample(...)`, `new CodeSampleArgument(...)`) and **named arguments** (`arguments:`, `description:`, `expectedOutput:`) inside the documentation tree. The top-level `new(Samples: …, Notes: …)` keeps the target-typed `new(` because the method return type makes it unambiguous; everything nested below should be explicit so the reader doesn't have to mentally type-check.
- `CodeSample(IEnumerable<CodeSampleArgument> arguments, string description, string? expectedOutput = null)` — arguments are typed bindings, not free-form strings. The renderer composes `octo-cli -c <verb> -<short> "value"...` at format time from the live `IArgument.ShortTerm`. Samples with three or more bindings render multi-line with PowerShell-7 backtick continuation; shorter invocations stay on a single line.
- `CodeSampleArgument(IArgument, string)` for arguments with values; `CodeSampleArgument(IArgument)` for flags. Constructor enforces the right shape against the argument's `MandatoryValuesCount`.
- `ExpectedOutput` is documentation-only — `--help` does not render it (consistent with `kubectl`-style CLIs). `Samples` and `Notes`, in contrast, are rendered by `octo-cli -c <verb> --help` (see [Help Options](#help-options)), so keep both terse enough to read in a terminal.
- Cross-references to non-command pages (concept docs, related sections) belong on handwritten `index.md` landing pages per command-reference section in `octo-documentation`, not on individual command pages — keeps generator output focused on the command itself.
- A note may start with a Markdown heading (`"### Title\n\nText"`); it renders as a subsection of `## Notes` and Docusaurus derives the anchor from the heading text. `ImportRt` uses this for `### Replaced create` (anchor `replaced-create`, linked from the CK methods docs).
- Skip the override entirely when the auto-canonical example suffices and there are no notes to add — keeps the class clean.

### Where output goes

The generator emits to `bin/Release/documentation/technologyGuide/tools/octo-cli/command-reference/` during CI builds. `octo-cli-pipeline.yml` invokes `createCommandReference.ps1`, and the resulting tree is published as the `_octo-cli` artifact via `handle-artifacts.yml`.

### End-to-end flow

```
   dev edits command class (.cs)              ← only step a developer ever does
            │
            ▼
   octo-cli-CI builds the branch
            │
            ▼
   _octo-cli artifact (contains the regenerated command-reference tree)
            │
            ▼
   Azure DevOps release: documentation-main-collect
   • Task Group UpdateDocumentationRepo:
       Delete files in octo-documentation's command-reference/
         (Contents `**`, excludes `_category_.json` and `**/_category_.json`)
       Copy Files from _octo-cli artifact into the same path
       Commit + push to octo-documentation main
            │
            ▼
   octo-documentation-CI rebuilds Docusaurus
            │
            ▼
   docs.meshmakers.cloud (auto-published)
```

Net effect: editing a `GetDocumentation()` override, an `AddArgument(...)`
help string, or a base() description in any command class is the **single**
step needed to update the public docs site. No manual edits in
`octo-documentation` — handwritten pages there are limited to `intro.md`
and `common-workflows.md`.
