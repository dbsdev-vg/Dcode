using DCode.Server.Projects;
using DCode.Server.Runs;
using DCode.Server.Storage;
using DCode.Server.Tools;
using Microsoft.Extensions.Configuration;

namespace DCode.Server.Tests;

public sealed class RunExecutionServiceTests:IDisposable
{
    private readonly string _directory=Path.Combine(Path.GetTempPath(),"dcode-runs",Guid.NewGuid().ToString("N"));

    [Fact] public void DetectsPackageScriptsAndPersistsCustomConfigurations(){var(service,project)=Create("{\"scripts\":{\"dev\":\"vite\",\"build\":\"tsc\"}}");var detected=service.ListConfigurations(project.Id);Assert.Contains(detected,item=>item.Name=="npm: dev"&&item.Arguments.SequenceEqual(["run","dev"]));var custom=service.Save(project.Id,null,new("Dotnet version","dotnet",["--version"],"."));var restored=service.ListConfigurations(project.Id);Assert.Contains(restored,item=>item.Id==custom.Id&&!item.IsDetected);}
    [Fact] public void RejectsWorkingDirectoryTraversal(){var(service,project)=Create();Assert.Throws<UnauthorizedAccessException>(()=>service.Save(project.Id,null,new("Escape","dotnet",["--version"],"..")));}
    [Fact] public async Task ExecutesCommandAndCapturesOutput(){var(service,project)=Create();var config=service.Save(project.Id,null,new("Dotnet version","dotnet",["--version"],"."));var started=service.Start(project.Id,config.Id);var finished=await WaitForTerminal(service,project.Id,started.Id);Assert.Equal("completed",finished.Status);Assert.Equal(0,finished.ExitCode);Assert.False(string.IsNullOrWhiteSpace(finished.Stdout));}
    [Fact] public void WindowsNpmLaunchBypassesCommandShim(){if(!OperatingSystem.IsWindows())return;var launch=ProcessLaunchResolver.Resolve("npm",["run","build"]);Assert.EndsWith("node.exe",launch.Executable,StringComparison.OrdinalIgnoreCase);Assert.EndsWith(Path.Combine("node_modules","npm","bin","npm-cli.js"),launch.Arguments[0],StringComparison.OrdinalIgnoreCase);Assert.Equal(["run","build"],launch.Arguments.Skip(1));}

    private(RunExecutionService,ProjectInfo)Create(string? package=null){Directory.CreateDirectory(_directory);if(package is not null)File.WriteAllText(Path.Combine(_directory,"package.json"),package);var configuration=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"Storage:DatabasePath",Path.Combine(_directory,"dcode.db")}}).Build();var database=new DCodeDatabase(configuration);database.Initialize();var projects=new ProjectService(new ProjectRepository(database));var project=projects.CreateProject(_directory);var repository=new RunRepository(database);return(new RunExecutionService(repository,projects),project);}
    private static async Task<RunExecution> WaitForTerminal(RunExecutionService service,string projectId,string id){for(var attempt=0;attempt<100;attempt++){var execution=service.ListExecutions(projectId).Single(item=>item.Id==id);if(execution.Status!="running")return execution;await Task.Delay(50);}throw new TimeoutException("Command did not finish.");}
    public void Dispose(){Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();try{if(Directory.Exists(_directory))Directory.Delete(_directory,true);}catch{}}
}
