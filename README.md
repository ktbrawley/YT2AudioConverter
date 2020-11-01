## INSTRUCTIONS

In order to publish a new version of the class lib to nuget, the following command must be executed:

```dotnet nuget push ./bin/Debug/YT2AudioConverter.<VERSION_NUMBER>.nupkg --api-key ygtf3uvbllttihbjzypfys7t3j6ejypg7ta4lg4m3vexau5bjx2a --source "BrawlNuget" --interactive```

Subsitute <VERSION_NUMBER> with the appropriate revision (e.g. 1.0.0)