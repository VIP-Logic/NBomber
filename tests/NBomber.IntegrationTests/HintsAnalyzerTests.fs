module Tests.HintsAnalyzer

open System

open Xunit
open FsCheck.Xunit
open Swensen.Unquote
open FsToolkit.ErrorHandling
open FSharp.Control.Tasks.NonAffine

open NBomber
open NBomber.Contracts
open NBomber.Contracts.Stats
open NBomber.Domain
open NBomber.Infra.Dependency
open NBomber.Extensions.InternalExtensions
open NBomber.FSharp

let baseNodeStats = {
    RequestCount = 0
    OkCount = 0
    FailCount = 0
    AllBytes = 0L
    ScenarioStats = Array.empty
    PluginStats = Array.empty
    NodeInfo = NodeInfo.init()
    TestInfo = { SessionId = ""; TestSuite = ""; TestName = "" }
    ReportFiles = Array.empty
    Duration = TimeSpan.MinValue
}

let baseScnStats = {
    ScenarioName = "scenario"; RequestCount = 0; OkCount = 0; FailCount = 0;
    AllBytes = 0L; StepStats = Array.empty; LatencyCount = { LessOrEq800 = 0; More800Less1200 = 0; MoreOrEq1200 = 0 }
    LoadSimulationStats = { SimulationName = ""; Value = 0 }
    StatusCodes = Array.empty; CurrentOperation = OperationType.None; Duration = TimeSpan.MinValue
}

let baseStepStats = {
    StepName = "step"
    Ok = {
        Request = { Count = 0; RPS = 0.0 }
        Latency = { MinMs = 0.0; MeanMs = 0.0; MaxMs = 0.0
                    Percent50 = 0.0; Percent75 = 0.0; Percent95 = 0.0; Percent99 = 0.0; StdDev = 0.0
                    LatencyCount = { LessOrEq800 = 0; More800Less1200 = 0; MoreOrEq1200 = 0 } }
        DataTransfer = { MinBytes = 0; MeanBytes = 0; MaxBytes = 0
                         Percent50 = 0; Percent75 = 0; Percent95 = 0; Percent99 = 0; StdDev = 0.0; AllBytes = 0L }
        StatusCodes = Array.empty
    }
    Fail = {
        Request = { Count = 0; RPS = 0.0 }
        Latency = { MinMs = 0.0; MeanMs = 0.0; MaxMs = 0.0
                    Percent50 = 0.0; Percent75 = 0.0; Percent95 = 0.0; Percent99 = 0.0; StdDev = 0.0
                    LatencyCount = { LessOrEq800 = 0; More800Less1200 = 0; MoreOrEq1200 = 0 } }
        DataTransfer = { MinBytes = 0; MeanBytes = 0; MaxBytes = 0
                         Percent50 = 0; Percent75 = 0; Percent95 = 0; Percent99 = 0; StdDev = 0.0; AllBytes = 0L }
        StatusCodes = Array.empty
    }
}

[<Property>]
let ``analyzeNodeStats should return hint for case when DataTransfer.MinBytes = 0`` (minBytes: uint32) =

    let req = { baseStepStats.Ok.Request with RPS = 1.0 }
    let dt = { baseStepStats.Ok.DataTransfer with MinBytes = int minBytes }
    let sc = { StatusCode = 200
               IsError = false
               Message = "Success"
               Count = 1 }
    let stepStats = { baseStepStats with Ok = { Request = req; Latency = baseStepStats.Ok.Latency; DataTransfer = dt; StatusCodes = [| sc |] } }
    let scnStats = { baseScnStats with StepStats = [| stepStats |] }
    let nodeStats = { baseNodeStats with ScenarioStats = [| scnStats |] }

    match HintsAnalyzer.analyzeNodeStats nodeStats with
    | hint::tail when minBytes = 0u -> ()
    | [] when minBytes > 0u         -> ()
    | e -> failwith "analyzer finished with error"

[<Fact>]
let ``analyzeScenarios should return hint for case when Scenario has steps with the same name but different implementations`` () =

    let step1 = Step.create("same_name", fun ctx -> task { return Response.ok() })
    let step2 = Step.create("same_name", fun ctx -> task { return Response.ok() })

    let hints =
        [Scenario.create "test" [step1; step2]]
        |> Domain.Scenario.createScenarios
        |> Result.getOk
        |> HintsAnalyzer.analyzeScenarios

    test <@ hints |> Seq.exists(fun x -> x.Hint.Contains("Scenario: 'test' contains duplicate step names: 'same_name'")) @>

[<Fact>]
let ``analyzeScenarios should return no hint if Scenario contains duplicated steps`` () =

    let step1 = Step.create("same_name", fun ctx -> task { return Response.ok() })

    let hints =
        [Scenario.create "test" [step1; step1; step1]]
        |> Domain.Scenario.createScenarios
        |> Result.getOk
        |> HintsAnalyzer.analyzeScenarios

    test <@ hints.Length = 0 @>

[<Fact>]
let ``HintsAnalyzer should be enable by default`` () =

    let step1 = Step.create("same_name", fun ctx -> task { return Response.ok() })
    let step2 = Step.create("same_name", fun ctx -> task { return Response.ok() })

    Scenario.create "test" [step1; step2]
    |> Scenario.withLoadSimulations [LoadSimulation.KeepConstant(1, seconds 2)]
    |> NBomberRunner.registerScenario
    |> NBomberRunner.runWithResult Seq.empty
    |> Result.getOk
    |> fun result ->
        test <@ result.Hints |> Seq.exists(fun x -> x.Hint.Contains("Scenario: 'test' contains duplicate step names: 'same_name'")) @>

[<Fact>]
let ``disableHintsAnalyzer should disable hints`` () =

    let step1 = Step.create("same_name", fun ctx -> task { return Response.ok() })
    let step2 = Step.create("same_name", fun ctx -> task { return Response.ok() })

    Scenario.create "test" [step1; step2]
    |> Scenario.withLoadSimulations [LoadSimulation.KeepConstant(1, seconds 2)]
    |> NBomberRunner.registerScenario
    |> NBomberRunner.disableHintsAnalyzer
    |> NBomberRunner.runWithResult Seq.empty
    |> Result.getOk
    |> fun result ->
        test <@ result.Hints.Length = 0 @>

[<Fact>]
let ``analyzeNodeStats should return hints for case when StatusCodes are not provided`` () =
    let req = { baseStepStats.Ok.Request with RPS = 1.0 }
    let dt = { baseStepStats.Ok.DataTransfer with MinBytes = 100 }
    let stepStats = { baseStepStats with Ok = { StatusCodes = Array.empty; Request = req; Latency = baseStepStats.Ok.Latency; DataTransfer = dt} }
    let scnStats = { baseScnStats with StepStats = [| stepStats |] }
    let nodeStats = { baseNodeStats with ScenarioStats = [| scnStats |] }

    let hints = HintsAnalyzer.analyzeNodeStats nodeStats

    test<@ hints.Length > 0 @>
