module FSharp.Logging.ElasticSearchLogging

open System.Threading.Tasks
open FSharp.Control.Tasks.NonAffine

open NBomber.Contracts
open NBomber.FSharp

// in this example we use:
// - Serilog.Sinks.Elasticsearch (https://github.com/serilog/serilog-sinks-elasticsearch)
// to get more info about logging, please visit: (https://nbomber.com/docs/logging)

let run () =

    let step = Step.create("step_1", fun context -> task {

        do! Task.Delay(seconds 1)

        // this message will be saved to elastic search
        context.Logger.Debug("hello from NBomber")

        return Response.Ok()
    })

    Scenario.create "hello_world_scenario" [step]
    |> Scenario.withoutWarmUp
    |> Scenario.withLoadSimulations [KeepConstant(copies = 1, during = seconds 30)]
    |> NBomberRunner.registerScenario
    |> NBomberRunner.withTestSuite "logging"
    |> NBomberRunner.withTestName "elastic_search"
    |> NBomberRunner.loadInfraConfig "./Logging/infra-config.json"
    |> NBomberRunner.run
    |> ignore
