module FSharp.HelloWorld.CustomSettingsExample

open System.Threading.Tasks

open Microsoft.Extensions.Configuration
open FSharp.Control.Tasks.NonAffine

open NBomber.Contracts
open NBomber.FSharp

[<CLIMutable>]
type CustomScenarioSettings = {
    TestField: int
    PauseMs: int
}

let run () =

    let mutable _customSettings = { TestField = 0; PauseMs = 0 }

    let scenarioInit (context: IScenarioContext) = task {
        _customSettings <- context.CustomSettings.Get<CustomScenarioSettings>()

        context.Logger.Information(
            "test init received CustomSettings.TestField '{TestField}'",
            _customSettings.TestField
        )
    }

    let step = Step.create("step", fun context -> task {

        do! Task.Delay(seconds 1)

        context.Logger.Debug(
            "step received CustomSettings.TestField '{TestField}'",
            _customSettings.TestField
        )

        return Response.Ok()
    })

    let customPause = Step.createPause(fun () -> _customSettings.PauseMs)

    Scenario.create "my_scenario" [step; customPause]
    |> Scenario.withInit scenarioInit
    |> NBomberRunner.registerScenario
    |> NBomberRunner.loadConfig "./HelloWorld/config.json"
    |> NBomberRunner.run
    |> ignore
