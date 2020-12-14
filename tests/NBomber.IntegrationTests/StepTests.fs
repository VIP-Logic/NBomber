module Tests.Step

open System
open System.Threading.Tasks

open Xunit
open Swensen.Unquote
open FSharp.Control.Tasks.NonAffine

open NBomber.Contracts
open NBomber.Errors
open NBomber.FSharp
open NBomber.Extensions.InternalExtensions

[<Fact>]
let ``Response Ok and Fail should be properly count`` () =

    let mutable okCnt = 0
    let mutable failCnt = 0

    let okStep = Step.create("ok step", fun _ -> task {
        do! Task.Delay(TimeSpan.FromSeconds(0.1))
        okCnt <- okCnt + 1
        return Response.Ok()
    })

    let failStep = Step.create("fail step", fun _ -> task {
        do! Task.Delay(TimeSpan.FromSeconds(0.1))
        failCnt <- failCnt + 1
        return Response.Fail()
    })

    let scenario =
        Scenario.create "count test" [okStep; failStep]
        |> Scenario.withoutWarmUp
        |> Scenario.withLoadSimulations [
            KeepConstant(copies = 1, during = seconds 2)
        ]

    let result = NBomberRunner.registerScenarios [scenario]
                 |> NBomberRunner.run

    match result with
    | Ok nodeStats ->
        let allStepStats = nodeStats.ScenarioStats |> Seq.collect(fun x -> x.StepStats)
        let okStats = allStepStats |> Seq.find(fun x -> x.StepName = "ok step")
        let failStats = allStepStats |> Seq.find(fun x -> x.StepName = "fail step")

        test <@ okStats.OkCount >= 5 && okStats.OkCount <= 10 @>
        test <@ okStats.FailCount = 0 @>
        test <@ failStats.OkCount = 0 @>
        test <@ failStats.FailCount > 5 && failStats.FailCount <= 10 @>

    | Error msg -> failwith msg

[<Fact>]
let ``Min/Mean/Max/RPS/DataTransfer should be properly count`` () =

    let pullStep = Step.create("pull step", fun _ -> task {
        do! Task.Delay(TimeSpan.FromSeconds(0.1))
        return Response.Ok(sizeBytes = 100)
    })

    let scenario =
        Scenario.create "latency count test" [pullStep]
        |> Scenario.withWarmUpDuration(TimeSpan.FromSeconds 1.0)
        |> Scenario.withLoadSimulations [
            KeepConstant(copies = 1, during = TimeSpan.FromSeconds 3.0)
        ]

    let result = NBomberRunner.registerScenarios [scenario]
                 |> NBomberRunner.run

    match result with
    | Ok nodeStats ->
        let stats = nodeStats.ScenarioStats
                    |> Seq.collect(fun x -> x.StepStats)
                    |> Seq.find(fun x -> x.StepName = "pull step")

        test <@ stats.RPS >= 5 @>
        test <@ stats.RPS <= 10 @>
        test <@ stats.Min <= 110 @>
        test <@ stats.Mean <= 120 @>
        test <@ stats.Max <= 150 @>
        test <@ stats.MinDataKb = 0.1 @>
        test <@ stats.AllDataMB >= 0.0015 @>

    | Error msg -> failwith msg

[<Fact>]
let ``can be duplicated to introduce repeatable behaviour`` () =

    let mutable repeatCounter = 0

    let repeatStep = Step.create("repeat_step", fun context -> task {
        do! Task.Delay(TimeSpan.FromSeconds(0.1))
        let number = context.GetPreviousStepResponse<int>()

        if number = 1 then repeatCounter <- repeatCounter + 1

        return Response.Ok(number + 1)
    })

    let scenario =
        Scenario.create "latency count test" [repeatStep; repeatStep]
        |> Scenario.withoutWarmUp
        |> Scenario.withLoadSimulations [
            KeepConstant(copies = 1, during = TimeSpan.FromSeconds 3.0)
        ]

    NBomberRunner.registerScenarios [scenario]
    |> NBomberRunner.run
    |> ignore

    test <@ repeatCounter > 5 @>

[<Fact>]
let ``StepContext Data should store any payload data from latest step.Response`` () =

    let mutable counter = 0
    let mutable step2Counter = 0
    let mutable counterFromStep1 = 0

    let step1 = Step.create("step 1", fun context -> task {
        counter <- counter + 1
        do! Task.Delay(TimeSpan.FromSeconds(0.1))
        return Response.Ok(counter)
    })

    let step2 = Step.create("step 2", fun context -> task {
        step2Counter <- counter
        counterFromStep1 <- context.GetPreviousStepResponse<int>()
        do! Task.Delay(TimeSpan.FromSeconds(0.1))
        return Response.Ok()
    })

    let scenario =
        Scenario.create "test context.Data" [step1; step2]
        |> Scenario.withoutWarmUp
        |> Scenario.withLoadSimulations [
            KeepConstant(copies = 1, during = TimeSpan.FromSeconds 3.0)
        ]

    NBomberRunner.registerScenarios [scenario]
    |> NBomberRunner.run
    |> ignore

    test <@ counterFromStep1 = step2Counter @>

[<Fact>]
let ``Step with DoNotTrack = true should has empty stats and not be printed`` () =

    let step1 = Step.create("step 1", fun context -> task {
        do! Task.Delay(TimeSpan.FromSeconds(0.1))
        return Response.Ok()
    })

    let step2 = Step.create("step 2", fun context -> task {
        do! Task.Delay(TimeSpan.FromSeconds(0.1))
        return Response.Ok()
    }, doNotTrack = true)

    let scenario =
        Scenario.create "test context.Data" [step1; step2]
        |> Scenario.withoutWarmUp
        |> Scenario.withLoadSimulations [
            KeepConstant(copies = 1, during = TimeSpan.FromSeconds 3.0)
        ]

    let result =
        NBomberRunner.registerScenarios [scenario]
        |> NBomberRunner.runWithResult Array.empty
        |> Result.getOk

    test <@ result.ScenarioStats.Length = 1 @>
    test <@ result.ScenarioStats
            |> Seq.collect(fun x -> x.StepStats)
            |> Seq.tryFind(fun x -> x.StepName = "step 2")
            |> Option.isNone @>

[<Fact>]
let ``createPause should work correctly and not printed in statistics`` () =

    let step1 = Step.create("step 1", fun context -> task {
        do! Task.Delay(TimeSpan.FromSeconds(0.1))
        return Response.Ok()
    })

    let pause4sec = Step.createPause(TimeSpan.FromSeconds 4.0)

    let scenario =
        Scenario.create "test context.Data" [pause4sec; step1]
        |> Scenario.withoutWarmUp
        |> Scenario.withLoadSimulations [
            KeepConstant(copies = 1, during = TimeSpan.FromSeconds 3.0)
        ]

    let result =
        NBomberRunner.registerScenarios [scenario]
        |> NBomberRunner.runWithResult Array.empty
        |> Result.getOk

    test <@ result.ScenarioStats.Length = 1 @>

[<Fact>]
let ``NBomber should support to run and share the same step within one scenario and within several scenarios`` () =

    let step1 = Step.create("step 1", fun context -> task {
        do! Task.Delay(TimeSpan.FromSeconds(0.1))
        return Response.Ok()
    })

    let step2 = Step.create("step 2", fun context -> task {
        do! Task.Delay(TimeSpan.FromSeconds(0.5))
        return Response.Ok()
    })

    let scenario1 =
        Scenario.create "scenario 1" [step1; step2]
        |> Scenario.withoutWarmUp
        |> Scenario.withLoadSimulations [
            KeepConstant(copies = 1, during = TimeSpan.FromSeconds 3.0)
        ]

    let scenario2 =
        Scenario.create "scenario 2" [step2; step1]
        |> Scenario.withoutWarmUp
        |> Scenario.withLoadSimulations [
            KeepConstant(copies = 1, during = TimeSpan.FromSeconds 3.0)
        ]

    let result =
        NBomberRunner.registerScenarios [scenario1; scenario2]
        |> NBomberRunner.runWithResult Array.empty
        |> Result.getOk

    test <@ result.ScenarioStats.[0].StepStats.Length = 2 @>
    test <@ result.ScenarioStats.[1].StepStats.Length = 2 @>

[<Fact>]
let ``NBomber should stop execution scenario if too many failed results on a warm-up`` () =

    let step = Step.create("step", fun context -> task {
        do! Task.Delay(milliseconds 100)
        return Response.Fail()
    })

    let scenario =
        Scenario.create "scenario" [step]
        |> Scenario.withWarmUpDuration(seconds 5)
        |> Scenario.withLoadSimulations [
            KeepConstant(copies = 1, during = seconds 10)
        ]

    let result =
        NBomberRunner.registerScenarios [scenario]
        |> NBomberRunner.runWithResult Array.empty
        |> Result.getError

    let warmUpErrorFound =
        match result with
        | Domain error -> match error with
                          | WarmUpErrorWithManyFailedSteps _ -> true
                          | _ -> false
        | _ -> false

    test <@ warmUpErrorFound = true @>

[<Fact>]
let ``NBomber should allow to set custom response latency and handle it properly`` () =

    let step = Step.create("step", fun context -> task {
        do! Task.Delay(TimeSpan.FromSeconds(0.1))
        return Response.Ok(latencyMs = 2_000) // set custom latency
    })

    let scenario =
        Scenario.create "scenario" [step]
        |> Scenario.withoutWarmUp
        |> Scenario.withLoadSimulations [
            KeepConstant(copies = 1, during = TimeSpan.FromSeconds 3.0)
        ]

    let nodeStats =
        NBomberRunner.registerScenarios [scenario]
        |> NBomberRunner.run
        |> Result.getOk

    let stepStats = nodeStats.ScenarioStats
                    |> Seq.collect(fun x -> x.StepStats)
                    |> Seq.find(fun x -> x.StepName = "step")

    test <@ stepStats.OkCount > 5 @>
    test <@ stepStats.RPS = 0 @>
    test <@ stepStats.Min = 2_000 @>

[<Fact>]
let ``context StopTest should stop all scenarios`` () =

    let mutable counter = 0
    let duration = TimeSpan.FromSeconds(42.0)

    let okStep = Step.create("ok step", fun context -> task {
        do! Task.Delay(TimeSpan.FromSeconds(0.1))
        counter <- counter + 1

        if counter >= 30 then
            context.StopCurrentTest(reason = "custom reason")

        return Response.Ok()
    })

    let scenario1 =
        Scenario.create "test_youtube_1" [okStep]
        |> Scenario.withoutWarmUp
        |> Scenario.withLoadSimulations [
            KeepConstant(10, duration)
        ]

    let scenario2 =
        Scenario.create "test_youtube_2" [okStep]
        |> Scenario.withoutWarmUp
        |> Scenario.withLoadSimulations [
            KeepConstant(10, duration)
        ]

    NBomberRunner.registerScenarios [scenario1; scenario2]
    |> NBomberRunner.run
    |> Result.getOk
    |> fun nodeStats ->
        nodeStats.ScenarioStats
        |> Seq.find(fun x -> x.ScenarioName = "test_youtube_1")
        |> fun x -> test <@ x.Duration < duration @>

[<Fact>]
let ``NBomber should reset step invocation number after warm-up`` () =

    let mutable counter = 0

    let step = Step.create("step", fun context -> task {
        do! Task.Delay(seconds 1)
        counter <- context.InvocationCount
        return Response.Ok()
    })

    let scenario =
        Scenario.create "scenario" [step]
        |> Scenario.withWarmUpDuration(seconds 5)
        |> Scenario.withLoadSimulations [KeepConstant(copies = 1, during = seconds 5)]

    NBomberRunner.registerScenarios [scenario]
    |> NBomberRunner.run
    |> ignore

    test <@ counter > 0 && counter <= 6 @>

[<Fact>]
let ``NBomber should handle invocation number per step following shared-nothing approach`` () =

    let mutable counter = 0

    let step = Step.create("step", fun context -> task {
        do! Task.Delay(seconds 1)
        counter <- context.InvocationCount
        return Response.Ok()
    })

    let scenario =
        Scenario.create "scenario" [step]
        |> Scenario.withoutWarmUp
        |> Scenario.withLoadSimulations [KeepConstant(copies = 10, during = seconds 5)]

    NBomberRunner.registerScenarios [scenario]
    |> NBomberRunner.run
    |> ignore

    test <@ counter > 0 && counter <= 6 @>
