module Tests.ReportingConfiguration

open System
open System.IO
open System.Threading.Tasks

open NBomber.Configuration
open Xunit
open Swensen.Unquote
open FSharp.Control.Tasks.NonAffine

open NBomber.Contracts
open NBomber.Domain
open NBomber.FSharp

[<Fact>]
let ``settings for ReportFileName and ReportFolder should be properly handled`` () =

    // test_config.json contains
    // "ReportFileName": "custom_report_name",
    // "ReportFolder": "./my_reports",
    // "ReportFormats": ["Html", "Txt"],

    // delete all directories with all files
    if Directory.Exists "./my_reports" then
        Directory.Delete("./my_reports", recursive = true)

    let okStep = Step.create("ok step", fun _ -> task {
        do! Task.Delay(seconds 1)
        return Response.Ok()
    })

    let scenario =
        Scenario.create "test" [okStep]
        |> Scenario.withoutWarmUp
        |> Scenario.withLoadSimulations [
            KeepConstant(copies = 5, during = TimeSpan.FromSeconds 5.0)
        ]

    NBomberRunner.registerScenarios [scenario]
    |> NBomberRunner.loadConfig("./Configuration/test_config.json")
    |> NBomberRunner.run
    |> ignore

    let dirExist = Directory.Exists "./my_reports"
    let files = Directory.GetFiles("./my_reports", searchPattern = "*.*", searchOption = SearchOption.AllDirectories)

    test <@ dirExist @>
    test <@ files.Length = 2 @> // here we check that only 2 report formats were generated

    files
    |> Seq.map(FileInfo)
    |> Seq.iter(fun file ->
        test <@ file.Name.Contains "custom_report_name" @>
        test <@ [".html"; ".txt"] |> Seq.contains file.Extension  @>
        test <@ file.Length > 0L  @>
    )

[<Fact>]
let ``withReportFileName and withReportFolder should be properly handled`` () =

    // delete all directories with all files
    if Directory.Exists "./my_reports" then
        Directory.Delete("./my_reports", recursive = true)

    let okStep = Step.create("ok step", fun _ -> task {
        do! Task.Delay(seconds 1)
        return Response.Ok()
    })

    let scenario =
        Scenario.create "test" [okStep]
        |> Scenario.withoutWarmUp
        |> Scenario.withLoadSimulations [
            KeepConstant(copies = 5, during = TimeSpan.FromSeconds 5.0)
        ]

    NBomberRunner.registerScenarios [scenario]
    |> NBomberRunner.withReportFileName "custom_report_name"
    |> NBomberRunner.withReportFolder "./my_reports"
    |> NBomberRunner.run
    |> ignore

    let dirExist = Directory.Exists "./my_reports"
    let files = Directory.GetFiles("./my_reports", searchPattern = "*.*", searchOption = SearchOption.AllDirectories)

    test <@ dirExist @>
    test <@ files.Length = 4 @> // here we check that all report formats were generated

    files
    |> Seq.map(FileInfo)
    |> Seq.iter(fun file ->
        test <@ file.Name.Contains "custom_report_name" @>
        test <@ [".html"; ".csv"; ".txt"; ".md"] |> Seq.contains file.Extension  @>
        test <@ file.Length > 0L  @>
    )
