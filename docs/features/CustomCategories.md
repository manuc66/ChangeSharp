# Custom Categories and SemVer Mappings

ChangeSharp allows users to define their own changelog categories and determine how each category affects the next version number.

## Configuration

Custom categories are defined in the `changesharp.json` configuration file under the `SemverPolicy` section.

### Example Configuration

The example below overrides the default `Changed → Minor` to `Changed → Major` (a common choice for strict SemVer projects) and adds two custom categories:

```json
{
  "SemverPolicy": {
    "Mappings": {
      "Breaking Changes": "Major",
      "Removed": "Major",
      "Changed": "Major",
      "Added": "Minor",
      "Deprecated": "Minor",
      "Fixed": "Patch",
      "Security": "Patch",
      "Maintenance": "None",
      "Documentation": "None"
    }
  }
}
```

## How it Works

1. **Fragment Creation**: When creating a new fragment (via `changesharp new`), you can specify any category.
2. **Validation**: The `changesharp validate` command checks that fragments use categories defined in your configuration.
3. **Version Computation**: During `changesharp release`, the tool aggregates all unreleased fragments. For each fragment, it looks up its category in the `SemverPolicy.Mappings` map:
   - `Major`: Triggers a Major version bump (e.g., 1.2.3 -> 2.0.0).
   - `Minor`: Triggers a Minor version bump (e.g., 1.2.3 -> 1.3.0).
   - `Patch`: Triggers a Patch version bump (e.g., 1.2.3 -> 1.2.4).
   - `None`: Does not affect the version number.

The highest impact from all fragments is selected to determine the final bump.

## Default Mappings

If no custom mappings are provided, ChangeSharp uses the following defaults (adhering to a conservative interpretation of SemVer):

- **Added**: Minor
- **Changed**: Minor
- **Deprecated**: Minor
- **Removed**: Major
- **Fixed**: Patch
- **Security**: Patch
