module FSharp.HelloWorld.HelloWorldExample

open System.Threading.Tasks

open FSharp.Control.Tasks.NonAffine

open NBomber.Contracts
open NBomber.FSharp

let run () =

    let step1 = Step.create("step_1", fun context -> task {

        // you can do any logic here: go to http, websocket etc
        do! Task.Delay(seconds 1)
        return Response.Ok(42) // this value will be passed as response for the next step
    })

    let pause = Step.createPause(milliseconds 100)

    let step2 = Step.create("step_2", fun context -> task {
        let value = context.GetPreviousStepResponse<int>(); // 42
        return Response.Ok();
    })

    Scenario.create "hello_world_scenario" [step1; pause; step2]
    |> Scenario.withoutWarmUp
    |> Scenario.withLoadSimulations [KeepConstant(copies = 1, during = seconds 30)]
    |> NBomberRunner.registerScenario
    |> NBomberRunner.withTestSuite "example"
    |> NBomberRunner.withTestName "hello_world_test"
    |> NBomberRunner.run
    |> ignore
