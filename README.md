# Space Engineers Script Build System

Python based build system for Space Engineers. Builds scripts by utilizing `@` tags to signify special actions and on success will put the resulting script in your clipboard.

**NOTE:** requires `pyperclip` package

All common files can be placed inside `lib` folder. Naming of *'includes'* is comprised from relative filepath to the C# file, where all splashes `/` are replaced with dots `.` and file extension `.cs` is omitted *(e.g. `@include lib.gyroWrapper` is `<root_folder>/lib/gyroWrapper.cs`)*.

In `.vscode` folder are included vs code configurations that on ***F5*** will trigger script build on the current file and if successful a built version of the script will be in your clipboard.

**NOTE:** requires `F5 Anything` extension

## Including and excluding

You can **include**/**exclude** files int o your script code using `@include` and `@skip`, e.g.

```c#
@include lib.eps
```

extends to:

```c#
public const double dEPS = 0.001d;
public const float EPS = 0.01f;
```

Or in other case

```c#
@skip lib.eps

public const double dEPS = 0.00001d;
public const float EPS = 0.0001f;
```

extends to:

```c#

public const double dEPS = 0.00001d;
public const float EPS = 0.0001f;
```

## Type Definitions

By default type definitions are located in `<root_folder>/lib/__type_defs__.cs` (defined in build script).
They work by replacing not in-string types with another type, e.g.

```c#
public static readonly @Regex tagRegex = new @Regex(@"(\s|^)@some_tag(\s|$)");
```

extends to:

```c#
public static readonly System.Text.RegularExpressions.Regex tagRegex = new System.Text.RegularExpressions.Regex(@"(\s|^)@some_tag(\s|$)");
```

Type definition file has syntax as follows:

```txt
@<source_string> <replace_string>
@<source_string> <replace_string>
...
```

**NOTE:** `<source_string>` must be comprised from `[A-z0-9_]` and `<replace_string>` must not contain spaces.

## Running the script

Script requires two arguments: `root` and `file`, where first is `<root_folder>`, i.e. root folder there all scripts and/or script folders are contained.

Example of a proper script call:

```shell
python <path_to_build_script>/__build__script__.py --root="<root_folder>" --file="<path_to_file>/<your_file>.cs"
```
