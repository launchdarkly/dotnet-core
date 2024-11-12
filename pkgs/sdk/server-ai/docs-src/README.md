# Notes on in-code documentation for this project

All public types, methods, and properties should have documentation comments in the standard C# XML comment format.
These will be automatically included in the [HTML documentation](https://launchdarkly.github.io/dotnet-core/pkgs/sdk/server) that is generated on release.

Non-public items may have documentation comments as well, since those may be helpful to other developers working on this
project, but they will not be included in the HTML documentation.
