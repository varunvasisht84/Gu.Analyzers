# Gu.Analyzers

[![License](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

<!-- start generated table -->
<table>
<tr>
  <td><a href="https://github.com/JohanLarsson/Gu.Analyzers/Gu.Analyzers/tree/master/documentation/GU0001.md">GU0001</a></td>
  <td>Name the arguments.</td>
</tr>
<table>
<!-- end generated table -->

## Using Gu.Analyzers

The preferable way to use the analyzers is to add the nuget package [Gu.Analyzers](https://www.nuget.org/packages/Gu.Analyzers/)
to the project(s).

The severity of individual rules may be configured using [rule set files](https://msdn.microsoft.com/en-us/library/dd264996.aspx)
in Visual Studio 2015.

## Installation

Gu.Analyzers can be installed using [Paket](https://fsprojects.github.io/Paket/) or the NuGet command line or the NuGet Package Manager in Visual Studio 2015.


**Install using the command line:**
```bash
Install-Package Gu.Analyzers
```

## Updating

The ruleset editor does not handle changes IDs well, if things get out of sync you can try:

1) Close visual studio.
2) Edit the ProjectName.rulset file and remove the Gu.Analyzers element.
3) Start visual studio and add back the desired configuration.

Above is not ideal, sorry about this. Not sure this is our bug.


## Current status

Early alpha, names and IDs may change.