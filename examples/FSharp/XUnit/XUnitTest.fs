module FSharp.DataFeed.XUnitTest

open System.Threading.Tasks

open FSharp.Control.Tasks.NonAffine
open Xunit
open Swensen.Unquote

open NBomber
open NBomber.Contracts
open NBomber.FSharp

// in this example we use:
// - XUnit (https://xunit.net/)
// - Unquote (https://github.com/SwensenSoftware/unquote)
// to get more info about test automation, please visit: (https://nbomber.com/docs/test-automation)

[<Fact>]
let ``XUnit test`` () =

    let step = Step.create("simple step", fun _ -> task {
        do! Task.Delay(milliseconds 100)
        return Response.Ok(sizeBytes = 1024)
    })

    let scenario =
        Scenario.create "xunit_hello_world" [step]
        |> Scenario.withoutWarmUp
        |> Scenario.withLoadSimulations [KeepConstant(copies = 1, during = seconds 2)]

    let result = NBomberRunner.registerScenarios [scenario]
                 |> NBomberRunner.run

    match result with
    | Ok nodeStats ->
        let stepStats = nodeStats.ScenarioStats.[0].StepStats.[0]
        test <@ stepStats.OkCount > 2 @>
        test <@ stepStats.RPS > 8 @>
        test <@ stepStats.Percent75 >= 100 @>
        test <@ stepStats.MinDataKb = 1.0 @>
        test <@ stepStats.AllDataMB >= 0.01 @>

    | Error msg -> failwith msg
