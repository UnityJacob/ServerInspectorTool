# About Multiplay Matchmaking Connector
This package provides a bridge between various packages and the `Multiplay Matchmaking` (com.unity.ucg.matchmaking) package, allowing attribute/property data to automatically be appended to matchmaking tickets if the appropriate package is installed alongside the matchmaking package.

If the matchmaking package is not installed, or the matchmaking package is installed but no Matchmaking Connector compatible packages are installed, the Matchmaking Connector does nothing.

Packages that currently use the Matchmaking Connector to append data to matchmaking tickets:
- `Multiplay QoS Client` (com.unity.ucg.qos)

# Installing Multiplay Matchmaking Connector
To install this package, follow the instructions in the [Package Manager documentation](https://docs.unity3d.com/Packages/com.unity.package-manager-ui@latest/index.html).

In general, it will be auto-installed as a base depenency of other packages.

# Using Multiplay Matchmaking Connector
At the moment, the package is designed for internal use and does not have a public API that should be used.

# Technical details
## Requirements
This version of Matchmaking Connector is compatible with the following versions of the Unity Editor:
- 2019.1 and later (recommended)

## Document revision history
|Date|Reason|
|---|---|
|Oct 4, 2019|Document created. Matches package version 0.1.0-preview.1|
