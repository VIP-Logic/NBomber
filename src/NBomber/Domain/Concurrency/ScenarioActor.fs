module internal NBomber.Domain.Concurrency.ScenarioActor

open System.Diagnostics
open System.Threading
open System.Threading.Tasks

open Nessos.Streams
open Serilog
open FSharp.Control.Tasks.NonAffine

open NBomber.Contracts
open NBomber.Domain
open NBomber.Domain.DomainTypes
open NBomber.Domain.Step
open NBomber.Domain.Statistics

type ActorDep = {
    Logger: ILogger
    CancellationToken: CancellationToken
    GlobalTimer: Stopwatch
    Scenario: Scenario
    ExecStopCommand: StopCommand -> unit
}

type ScenarioActor(dep: ActorDep, correlationId: CorrelationId) =

    let _allScnResponses = Array.init<ResizeArray<StepResponse>> dep.Scenario.Steps.Length (fun _ -> ResizeArray())

    let _stepDep = { Logger = dep.Logger; CancellationToken = dep.CancellationToken
                     GlobalTimer = dep.GlobalTimer; CorrelationId = correlationId
                     ExecStopCommand = dep.ExecStopCommand }

    let _steps = dep.Scenario.Steps
                 |> List.map RunningStep.init
                 |> List.toArray

    let mutable _working = false
    let mutable _reserved = false
    let mutable _currentTask = Unchecked.defaultof<Task>

    member _.CorrelationId = correlationId
    member _.Working = _working
    member _.Reserved = _reserved
    member _.CurrentTask = _currentTask

    member _.ReserveForScheduler() =
        _reserved <- true

    member _.LeaveScheduler() =
        _reserved <- false

    member _.ExecSteps() = task {
        if _reserved then
            _working <- true
            _currentTask <- Step.execSteps(_stepDep, _steps, _allScnResponses)
            do! _currentTask
            _working <- false
    }

    member _.GetStepResults(duration) =
        let filteredResponses = _allScnResponses |> Array.map(Stream.ofResizeArray >> Step.filterByDuration duration)

        dep.Scenario.Steps
        |> Stream.ofList
        |> Stream.mapi(fun i step -> step, StepResults.create(step.StepName, filteredResponses.[i]))
        |> Stream.choose(fun (step, results) -> if step.DoNotTrack then None else Some results)
