module Tests.Logging

open System
open System.Threading.Tasks

open Serilog
open Serilog.Events
open Serilog.Sinks.InMemory
open Xunit
open FsCheck
open FsCheck.Xunit
open Swensen.Unquote
open FSharp.Control.Tasks.NonAffine

open NBomber.Contracts
open NBomber.FSharp

[<Fact>]
let ``set min logger level should work correctly`` () =

    let step = Step.create("step", fun context -> task {
        do! Task.Delay(TimeSpan.FromSeconds 0.1)
        context.Logger.Information("this message should not be printed")
        return Response.Ok()
    })

    let inMemorySink1 = InMemorySink()
    let inMemorySink2 = InMemorySink()

    let createLoggerConfig1 = fun () ->
        LoggerConfiguration()
            .MinimumLevel.Is(LogEventLevel.Error)
            .WriteTo.Sink(inMemorySink1)

    let createLoggerConfig2 = fun () ->
        LoggerConfiguration()
            .MinimumLevel.Is(LogEventLevel.Verbose)
            .WriteTo.Sink(inMemorySink2)

    Scenario.create "scenario1" [step]
    |> Scenario.withLoadSimulations [KeepConstant(1, TimeSpan.FromSeconds 3.0)]
    |> NBomberRunner.registerScenario
    |> NBomberRunner.withLoggerConfig createLoggerConfig1
    |> NBomberRunner.run
    |> ignore

    Scenario.create "scenario2" [step]
    |> Scenario.withLoadSimulations [KeepConstant(1, TimeSpan.FromSeconds 3.0)]
    |> NBomberRunner.registerScenario
    |> NBomberRunner.withLoggerConfig createLoggerConfig2
    |> NBomberRunner.run
    |> ignore

    let scenario1LogPrinted =
        inMemorySink1.LogEvents
        |> Seq.exists(fun x -> x.MessageTemplate.Text = "this message should not be printed")

    let scenario2LogPrinted =
        inMemorySink2.LogEvents
        |> Seq.exists(fun x -> x.MessageTemplate.Text = "this message should not be printed")

    test <@ scenario1LogPrinted = false @>
    test <@ scenario2LogPrinted = true @>
