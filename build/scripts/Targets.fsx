// Learn more about F# at http://fsharp.net. See the 'F# Tutorial' project
// for more guidance on F# programming.
#I "../../packages/build/FAKE/tools"
#I "../../packages/build/System.Management.Automation/lib/net45"
#r "FakeLib.dll"
#r "System.Management.Automation.dll"
#load "Download.fsx"
#load "BuildConfig.fsx"

open System
open System.IO
open System.Management.Automation
open Fake
open Fake.FileHelper
open Scripts
open Scripts.Downloader
open Fake.Testing.XUnit2

//tracefn "last version is %s" Downloader.lastVersion

let version = getBuildParamOrDefault "version" Downloader.lastVersion
let integrationTestsTargets = getBuildParamOrDefault "testtargets" "*"

tracefn "Starting build for version %s" version

let buildDir = "./build/"
let toolsDir = buildDir @@ "tools/"
let inDir = buildDir @@ "in/"
let outDir = buildDir @@ "out/"

let msiDir = "./src/Elastic.Installer.Msi/"
let msiBuildDir = msiDir @@ "bin/Release/"

// TODO move these directory properties to Product
let esBinDir = inDir @@ "elasticsearch-" + version @@ "/bin/"
let esServiceDir = "./src/Elasticsearch/Elastic.Installer.Elasticsearch.Process/"
let esServiceBuildDir = esServiceDir @@ "bin/AnyCPU/Release/"

let kibanaBinDir = inDir @@ "kibana-" + version @@ "/bin/";
let kibanaServiceDir = "./src/Kibana/Elastic.Installer.Kibana.Process/"
let kibanaServiceBuildDir = kibanaServiceDir @@ "bin/AnyCPU/Release/"

let integrationTestsDir = FullName "./src/Tests/Elastic.Installer.Integration.Tests"

let unitTestsDir = "src/Tests/Elastic.Installer.Domain.Tests"

Target "Clean" (fun _ ->
    CleanDirs [
                msiBuildDir
                esServiceBuildDir
                outDir
              ]
)

Target "DownloadProducts" (fun () ->
  if (not (Directory.Exists (Product.Elasticsearch.ExtractedDirectory version)))
  then 
    Downloader.downloadProduct Product.Elasticsearch version
    Downloader.unzipProduct Product.Elasticsearch version

  if (not (Directory.Exists (Product.Kibana.ExtractedDirectory version)))
  then 
    Downloader.downloadProduct Product.Kibana version
    Downloader.unzipProduct Product.Kibana version
)

Target "PatchGuids" (fun () ->
    tracefn "Making sure a guid exists for v%s" version
    BuildConfig.versionGuid version |> ignore
)

Target "UnitTest" (fun () ->
    let unitTestBuildDir = unitTestsDir @@ "bin" @@ "Release"

    !! (unitTestsDir @@ "*.csproj")
    |> MSBuildRelease unitTestBuildDir "Build"
    |> Log "MsiBuild-Output: "

    !! (unitTestBuildDir @@ "*Tests.dll")
        |> xUnit2 (fun p -> { p with HtmlOutputPath = Some (outDir @@ "xunit.html") })
)

let prune files directory =
  let keep = files |> Seq.map (fun n -> directory @@ n)
  for file in System.IO.Directory.EnumerateFiles(directory) do
        if keep |> Seq.exists (fun n -> n <> file) then System.IO.File.Delete(file)
        
Target "PruneFiles" (fun () ->
    prune ["elasticsearch-plugin.bat"] esBinDir
    prune ["kibana-plugin.bat"] kibanaBinDir
)

let signFile file (product : Product) =
    let signToolExe = toolsDir @@ "signtool/signtool.exe"
    let certificate = Environment.GetEnvironmentVariable("ELASTIC_CERT_FILE", EnvironmentVariableTarget.Machine)
    let password = Environment.GetEnvironmentVariable("ELASTIC_CERT_PASSWORD", EnvironmentVariableTarget.Machine)
    let timestampServer = "http://timestamp.comodoca.com"
    let timeout = TimeSpan.FromMinutes 1.
    let description = System.Globalization.CultureInfo.GetCultureInfo(System.Threading.Thread.CurrentThread.CurrentCulture.Name).TextInfo.ToTitleCase product.Name

    if certificate <> null && password <> null
    then ExecProcess(fun info ->
            info.FileName <- signToolExe
            info.Arguments <- ["sign"; "/f"; certificate; "/p"; password; "/t"; timestampServer; "/d"; description; "/v"; file] |> String.concat " "
         ) <| timeout |> ignore
    else raise (Exception(sprintf "Failed to sign %s: Certificate not found." file))

let buildService (product : Product) sign serviceDir serviceBuildDir serviceBinDir =
    !! (serviceDir @@ "*.csproj")
    |> MSBuildRelease serviceBuildDir "Build"
    |> Log "ServiceBuild-Output: "
    let serviceAssembly = serviceBuildDir @@ (sprintf "Elastic.Installer.%s.Process.exe" product.Name)
    let service = serviceBinDir @@ (sprintf "%s.exe" product.Name)
    CopyFile service serviceAssembly
    if sign then signFile service product |> ignore

let buildMsi (product : Product) sign =
    !! (msiDir @@ "*.csproj")
    |> MSBuildRelease msiBuildDir "Build"
    |> Log "MsiBuild-Output: "

    let buildFailure errors =
          raise (BuildException("Building" + product.Name + " MSI failed.", errors |> List.ofSeq))

    let timeout = TimeSpan.FromMinutes 20.
    let result = ExecProcess (fun info ->
                    info.FileName <- sprintf "%sElastic.Installer.Msi" msiBuildDir
                    info.WorkingDirectory <- msiDir
                    info.Arguments <- [product.Name; version; Path.GetFullPath(inDir)] |> String.concat " "
                 ) <| timeout

    if result <> 0 then raise (Exception())
    let finalMsi = (outDir @@ (sprintf "%s-%s.msi" product.Name version))
    CopyFile finalMsi (msiDir @@ (sprintf "%s.msi" product.Name))

    if sign then signFile finalMsi product |> ignore

Target "BuildServices" (fun () ->
    buildService Product.Elasticsearch false esServiceDir esServiceBuildDir esBinDir
    buildService Product.Kibana false kibanaServiceDir kibanaServiceBuildDir kibanaBinDir
)

Target "BuildInstallers" (fun () ->
    buildMsi Product.Elasticsearch false
    buildMsi Product.Kibana false
)

Target "Sign" (fun () ->
    buildService Product.Elasticsearch true esServiceDir esServiceBuildDir esBinDir
    buildService Product.Kibana true kibanaServiceDir kibanaServiceBuildDir kibanaBinDir
    buildMsi Product.Elasticsearch true
    buildMsi Product.Kibana true
)

Target "Release" (fun () ->
    trace "Building in release mode.  All files will be signed."
)

Target "Integrate" (fun () ->
  let script = sprintf "cd '%s'; %s -Tests %s -Version %s" integrationTestsDir ".\Bootstrapper.ps1" integrationTestsTargets version
  trace (sprintf "Running Powershell script: \"%s\"" script)
  use p = PowerShell.Create()
  let output = new PSDataCollection<PSObject>()
  output.DataAdded.Add(fun data -> trace (sprintf "%O" output.[data.Index]))
  p.Streams.Verbose.DataAdded.Add(fun data -> trace (sprintf "%O" p.Streams.Verbose.[data.Index]))
  p.Streams.Debug.DataAdded.Add(fun data -> trace (sprintf "%O" p.Streams.Debug.[data.Index]))
  p.Streams.Progress.DataAdded.Add(fun data -> trace (sprintf "%O" p.Streams.Progress.[data.Index]))
  p.Streams.Warning.DataAdded.Add(fun data -> traceError (sprintf "%O" p.Streams.Warning.[data.Index]))
  p.Streams.Error.DataAdded.Add(fun data -> traceError (sprintf "%O" p.Streams.Error.[data.Index]))
  let async =
        p.AddScript(script).BeginInvoke(null, output)
              |> Async.AwaitIAsyncResult
              |> Async.Ignore
  Async.RunSynchronously async)

"Clean"
  =?> ("DownloadProducts", (not ((getBuildParam "release") = "1")))
  ==> "PatchGuids"
  ==> "PruneFiles"
  ==> "UnitTest"
  ==> "BuildServices"
  ==> "BuildInstallers"
  ==> "Integrate"

"UnitTest"
  ==> "Sign"
  ==> "Release"

RunTargetOrDefault "BuildInstallers"
