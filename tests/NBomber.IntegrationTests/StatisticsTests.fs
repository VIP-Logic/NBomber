module Tests.Statistics

open System
open System.Threading.Tasks

open Xunit
open FsCheck.Xunit
open Swensen.Unquote
open Nessos.Streams
open FSharp.Control.Tasks.NonAffine

open NBomber.Contracts
open NBomber.Domain
open NBomber.Domain.DomainTypes
open NBomber.FSharp
open NBomber.Extensions.InternalExtensions
open Tests.TestHelper

let private latencyCount = { Less800 = 1; More800Less1200 = 1; More1200 = 1 }

let private scenario = {
    ScenarioName = "Scenario1"
    Init = None
    Clean = None
    Steps = List.empty
    LoadSimulations = [KeepConstant(copies = 1, during = seconds 1)]
    WarmUpDuration = TimeSpan.FromSeconds(1.0)
}

[<Fact>]
let ``calcRPS() should not calculate latency which is bigger than 1 sec`` () =
    let latencies = [2_000; 3_000; 4_000]
    let scnDuration = TimeSpan.FromSeconds(5.0)

    let result = latencies |> Stream.ofList |> Statistics.calcRPS(scnDuration)

    test <@ result = 0 @>

[<Property>]
let ``calcRPS() should not fail and calculate correctly for any args values`` (latencies: Latency list, scnDuration: TimeSpan) =
    let result = latencies |> Stream.ofList |> Statistics.calcRPS(scnDuration)

    if latencies.Length = 0 then
        test <@ result = 0 @>

    elif latencies.Length <> 0 && scnDuration.TotalSeconds < 1.0 then
        test <@ result = latencies.Length @>

    else
        let allLatenciesIn1SecCount = latencies |> List.filter(fun x -> x <= 1_000) |> List.length
        let expected = allLatenciesIn1SecCount / int(scnDuration.TotalSeconds)
        test <@ result = expected @>

[<Property>]
let ``calcMin() should not fail and calculate correctly for any args values`` (latencies: Latency list) =
    let result   = latencies |> Stream.ofList |> Statistics.calcMin
    let expected = List.minOrDefault 0 latencies
    test <@ result = expected @>

[<Property>]
let ``calcMean() should not fail and calculate correctly for any args values`` (latencies: Latency list) =
    let result = latencies |> Stream.ofList |> Statistics.calcMean
    let expected = latencies |> List.map float |> List.averageOrDefault 0.0 |> int
    test <@ result = expected @>

[<Property>]
let ``calcMax() should not fail and calculate correctly for any args values`` (latencies: Latency list) =
    let result = latencies |> Stream.ofList |> Statistics.calcMax
    let expected = List.maxOrDefault 0 latencies
    test <@ result = expected @>

[<Fact>]
let ``ErrorStats should be calculated properly`` () =

    let okStep = Step.create("ok step", fun _ -> task {
        do! Task.Delay(milliseconds 100)
        return Response.Ok()
    })

    let failStep1 = Step.create("fail step 1", fun context -> task {
        do! Task.Delay(milliseconds 10)
        return if context.InvocationCount <= 10 then Response.Fail(reason = "reason 1", errorCode = 10)
               else Response.Ok()
    })

    let failStep2 = Step.create("fail step 2", fun context -> task {
        do! Task.Delay(milliseconds 10)
        return if context.InvocationCount <= 30 then Response.Fail(reason = "reason 2", errorCode = 20)
               else Response.Ok()
    })

    let scenario =
        Scenario.create "realtime stats scenario" [okStep; failStep1; failStep2]
        |> Scenario.withoutWarmUp
        |> Scenario.withLoadSimulations [
            KeepConstant(copies = 2, during = seconds 10)
        ]

    NBomberRunner.registerScenarios [scenario]
    |> NBomberRunner.run
    |> Result.getOk
    |> fun stats ->
        let allErrorStats = stats.ScenarioStats.[0].ErrorStats
        let fail1Stats = stats.ScenarioStats.[0].StepStats.[1].ErrorStats
        let fail2Stats = stats.ScenarioStats.[0].StepStats.[2].ErrorStats

        test <@ allErrorStats.Length = 2 @>
        test <@ fail1Stats.Length = 1 @>
        test <@ fail2Stats.Length = 1 @>

        test <@ fail1Stats
                |> Seq.find(fun x -> x.ErrorCode = 10)
                |> fun error -> error.Count = 20 @>

        test <@ fail2Stats
                |> Seq.find(fun x -> x.ErrorCode = 20)
                |> fun error -> error.Count = 60 @>
