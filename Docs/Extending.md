# Extending

## Adding an app

1. Create `Apps/<Name>/` with the six language folders (copy an existing app, keep `Arguments.rux` unchanged) and an `app.json` with `small` and `standard` arguments and empty `expected` values.
2. Add the name to `apps` in `bench.json`.
3. Store the expected outputs, first for the small profile, then for the standard one:

    ```bash
    dotnet run --project Runner -c Release -- all --app <Name> --profile small --runs 1 --warmups 0 --build-runs 1 --update-expected
    dotnet run --project Runner -c Release -- all --app <Name> --profile standard --runs 1 --warmups 0 --build-runs 1 --update-expected
    ```

4. Commit the app and the stored outputs.

Follow the [fairness rules](Apps.md#rules-that-keep-the-comparison-fair): the same algorithm, data and operation order in every language, standard library only, deterministic output.

## Adding a language

Add one new `Toolchain` class in `Runner/Toolchains.cs` (build command, output path, clean folders), add its name to `languages` in `bench.json`, and add a folder per app. For the compile benchmark, add an emitter in `Runner/Generator/` as well.

## Changing the generator

1. Edit `Runner/Generator/`.
2. Regenerate the projects:

    ```bash
    dotnet run --project Runner -c Release -- generate
    ```

3. If the program's output changed, clear `expected` in `Projects/*/project.json` and store the new output:

    ```bash
    dotnet run --project Runner -c Release -- compile --build-runs 1 --update-expected
    ```

4. Commit `Projects/Baseline` (and its `project.json`). `Projects/Large` is not committed.

`compile` and `doctor` refuse to run while the committed `Baseline` differs from the generator's output.
