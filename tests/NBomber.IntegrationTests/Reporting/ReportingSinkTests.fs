module Tests.ReportingSink

open System
open System.Threading.Tasks

open Xunit
open Swensen.Unquote
open FSharp.Control.Tasks.NonAffine

open NBomber.Contracts
open NBomber.Domain
open NBomber.FSharp

//todo: test that multiply sink will be invoked correctly
//todo: test that stop timer stops sending metrics in case when stopping is still executing
//todo: test cluster stats

[<Fact>]
let ``SaveStats should be invoked many times during test execution to send realtime stats`` () =

    let okStep = Step.create("ok step", fun _ -> task {
        do! Task.Delay(milliseconds 100)
        return Response.Ok()
    })

    let scenario =
        Scenario.create "realtime stats scenario" [okStep]
        |> Scenario.withoutWarmUp
        |> Scenario.withLoadSimulations [
            KeepConstant(copies = 5, during = seconds 40)
        ]

    let mutable statsInvokedCounter = 0

    let reportingSink = {
        new IReportingSink with
            member _.SinkName = "TestSink"
            member _.Init(_, _) = ()
            member _.Start(_) = Task.CompletedTask
            member _.SaveStats(_) =
                // 1 invoke per 5 sec
                statsInvokedCounter <- statsInvokedCounter + 1
                Task.CompletedTask

            member _.Stop() = Task.CompletedTask
            member _.Dispose() = ()
    }

    NBomberRunner.registerScenarios [scenario]
    |> NBomberRunner.withReportingSinks [reportingSink] (seconds 10)
    |> NBomberRunner.run
    |> ignore

    test <@ statsInvokedCounter >= 3 @> // 1 invoke as realtime and 1 invoke at the end

[<Fact>]
let ``SaveStats should be invoked with OperationType = Complete only once`` () =

    let okStep = Step.create("ok step", fun _ -> task {
        do! Task.Delay(milliseconds 100)
        return Response.Ok()
    })

    let scenario =
        Scenario.create "realtime stats scenario" [okStep]
        |> Scenario.withoutWarmUp
        |> Scenario.withLoadSimulations [
            KeepConstant(copies = 5, during = seconds 30)
        ]

    let mutable bombingCounter = 0
    let mutable completeCounter = 0

    let reportingSink = {
        new IReportingSink with
            member _.SinkName = "TestSink"
            member _.Init(_, _) = ()
            member _.Start(_) = Task.CompletedTask

            member _.SaveStats(stats) =
                match stats.[0].NodeInfo.CurrentOperation with
                | NodeOperationType.Bombing  -> bombingCounter <- bombingCounter + 1
                | NodeOperationType.Complete -> completeCounter <- completeCounter + 1
                | _                          -> failwith "operation type is invalid for SaveStats"
                Task.CompletedTask

            member _.Stop() = Task.CompletedTask
            member _.Dispose() = ()
    }

    NBomberRunner.registerScenarios [scenario]
    |> NBomberRunner.withReportingSinks [reportingSink] (seconds 10)
    |> NBomberRunner.run
    |> ignore

    test <@ bombingCounter > 0 && completeCounter = 1 @>

[<Fact>]
let ``SaveStats for real-time reporting should contains only bombing stats`` () =

    let okStep = Step.create("ok step", fun _ -> task {
        do! Task.Delay(milliseconds 100)
        return Response.Ok()
    })

    let scenario1 =
        Scenario.create "scenario_1" [okStep]
        |> Scenario.withoutWarmUp
        |> Scenario.withLoadSimulations [KeepConstant(copies = 5, during = seconds 20)]

    let scenario2 =
        Scenario.create "scenario_2" [okStep]
        |> Scenario.withoutWarmUp
        |> Scenario.withLoadSimulations [KeepConstant(copies = 5, during = seconds 40)]

    let mutable scn1BombingInvokedCount = 0
    let mutable scn2BombingInvokedCount = 0
    let mutable scn1CompleteInvokedCount = 0
    let mutable scn2CompleteInvokedCount = 0

    let reportingSink = {
        new IReportingSink with
            member _.SinkName = "TestSink"
            member _.Init(_, _) = ()
            member _.Start(_) = Task.CompletedTask

            member _.SaveStats(stats) =
                match stats.[0].NodeInfo.CurrentOperation with
                | NodeOperationType.Bombing ->
                    stats.[0].ScenarioStats
                    |> Seq.filter(fun x -> x.ScenarioName = "scenario_1")
                    |> Seq.iter(fun _ -> scn1BombingInvokedCount <- scn1BombingInvokedCount + 1)

                    stats.[0].ScenarioStats
                    |> Seq.filter(fun x -> x.ScenarioName = "scenario_2")
                    |> Seq.iter(fun _ -> scn2BombingInvokedCount <- scn2BombingInvokedCount + 1)

                | NodeOperationType.Complete ->
                    stats.[0].ScenarioStats
                    |> Seq.filter(fun x -> x.ScenarioName = "scenario_1")
                    |> Seq.iter(fun _ -> scn1CompleteInvokedCount <- scn1CompleteInvokedCount + 1)

                    stats.[0].ScenarioStats
                    |> Seq.filter(fun x -> x.ScenarioName = "scenario_2")
                    |> Seq.iter(fun _ -> scn2CompleteInvokedCount <- scn2CompleteInvokedCount + 1)

                | _ -> failwith "operation type is invalid for SaveStats"

                Task.CompletedTask

            member _.Stop() = Task.CompletedTask
            member _.Dispose() = ()
    }

    NBomberRunner.registerScenarios [scenario1; scenario2]
    |> NBomberRunner.withReportingSinks [reportingSink] (seconds 10)
    |> NBomberRunner.run
    |> ignore

    test <@ scn1BombingInvokedCount < scn2BombingInvokedCount @> // since scenario_2 has bigger duration
    test <@ scn1CompleteInvokedCount = scn2CompleteInvokedCount && scn2CompleteInvokedCount = 1 @>
