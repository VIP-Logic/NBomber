[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module internal NBomber.Domain.Step

open System
open System.Diagnostics
open System.Threading
open System.Threading.Tasks

open Nessos.Streams
open Serilog
open FSharp.Control.Tasks.NonAffine

open NBomber
open NBomber.Extensions.InternalExtensions
open NBomber.Contracts
open NBomber.Domain.DomainTypes
open NBomber.Domain.ConnectionPool

type StepDep = {
    Logger: ILogger
    CancellationToken: CancellationToken
    GlobalTimer: Stopwatch
    CorrelationId: CorrelationId
    ExecStopCommand: StopCommand -> unit
}

module RunningStep =

    let init (step: Step) =
        { Value = step; Context = Unchecked.defaultof<_>; InvocationCount = 1 }

let toUntypedExec (execute: StepContext<'TConnection,'TFeedItem> -> Task<Response>) =
    fun (context: StepContext<obj,obj>) ->
        let untyped = context :> IStepContext<obj,obj>
        let typed = StepContext(untyped.CorrelationId,
                                untyped.CancellationToken,
                                untyped.Connection :?> 'TConnection,
                                untyped.Data,
                                untyped.FeedItem :?> 'TFeedItem,
                                untyped.Logger,
                                untyped.InvocationCount,
                                context.ExecStopCommand)
        execute(typed)

let setStepContext (dep: StepDep, step: RunningStep, data: Dict<string,obj>) =

    let getConnection (pool: ConnectionPool option) =
        match pool with
        | Some v ->
            let index = dep.CorrelationId.CopyNumber % v.AliveConnections.Length
            v.AliveConnections.[index]

        | None -> () :> obj

    let connection = getConnection(step.Value.ConnectionPool)
    let context = StepContext(dep.CorrelationId,
                              dep.CancellationToken,
                              connection,
                              data,
                              step.Value.Feed.GetNextItem(dep.CorrelationId, data),
                              dep.Logger,
                              step.InvocationCount,
                              dep.ExecStopCommand)

    step.Context <- context
    step

let execStep (step: RunningStep, globalTimer: Stopwatch) = task {
    let startTime = globalTimer.Elapsed.TotalMilliseconds
    try
        let! resp = step.Value.Execute(step.Context)

        let latency =
            if resp.LatencyMs > 0 then resp.LatencyMs
            else
                let endTime = globalTimer.Elapsed.TotalMilliseconds
                int(endTime - startTime)

        return { Response = resp; StartTimeMs = startTime; LatencyMs = latency }
    with
    | :? TaskCanceledException
    | :? OperationCanceledException ->
        return { Response = Response.Ok(); StartTimeMs = -1.0; LatencyMs = -1 }

    | ex -> let endTime = globalTimer.Elapsed.TotalMilliseconds
            let latency = int(endTime - startTime)
            return { Response = Response.Fail(ex); StartTimeMs = startTime; LatencyMs = int latency }
}

let execSteps (dep: StepDep, steps: RunningStep[], allScnResponses: (ResizeArray<StepResponse>)[]) = task {

    let data = Dict.empty
    let mutable skipStep = false
    let mutable stepIndex = 0

    for s in steps do
        if not skipStep && not dep.CancellationToken.IsCancellationRequested then

            let step = setStepContext(dep, s, data)
            let! response = execStep(step, dep.GlobalTimer)
            s.InvocationCount <- s.InvocationCount + 1

            let payload = response.Response.Payload

            if not dep.CancellationToken.IsCancellationRequested && not step.Value.DoNotTrack then
                response.Response.Payload <- null
                allScnResponses.[stepIndex].Add(response)

            if response.Response.Exception.IsNone then
                stepIndex <- stepIndex + 1
                data.[Constants.StepResponseKey] <- payload
            else
                dep.Logger.Error(response.Response.Exception.Value, "step '{Step}' is failed. ", step.Value.StepName)
                skipStep <- true
}

let filterByDuration (duration: TimeSpan) (stepResponses: Stream<StepResponse>) =
    let validEndTime (endTime) = endTime <= duration.TotalMilliseconds
    let createEndTime (response) = response.StartTimeMs + float response.LatencyMs

    stepResponses
    |> Stream.filter(fun x -> x.StartTimeMs <> -1.0) // to filter out TaskCanceledException
    |> Stream.choose(fun x ->
        match x |> createEndTime |> validEndTime with
        | true  -> Some x
        | false -> None)
