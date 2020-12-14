namespace NBomber.DomainServices.TestHost

open System
open System.Threading
open System.Threading.Tasks
open System.Diagnostics
open System.Runtime.InteropServices

open Nessos.Streams
open FSharp.Control.Tasks.NonAffine
open FsToolkit.ErrorHandling

open NBomber.Contracts
open NBomber.Errors
open NBomber.Domain
open NBomber.Domain.DomainTypes
open NBomber.Domain.Statistics
open NBomber.Domain.Concurrency.ScenarioActor
open NBomber.Domain.Concurrency.Scheduler.ScenarioScheduler
open NBomber.Infra.Dependency
open NBomber.DomainServices.NBomberContext

type internal TestHost(dep: IGlobalDependency, registeredScenarios: Scenario list, sessionArgs: SessionArgs) as this =

    let mutable _stopped = false
    let mutable _targetScenarios = List.empty<Scenario>
    let mutable _currentOperation = NodeOperationType.None
    let mutable _scnSchedulers = List.empty<ScenarioScheduler>
    let mutable _cancelToken = new CancellationTokenSource()
    let mutable _timeLineStats: (TimeSpan * NodeStats) list = List.empty
    let _defaultNodeInfo = NodeInfo.init()

    let getCurrentNodeInfo () =
        { _defaultNodeInfo with NodeType = dep.NodeType; CurrentOperation = _currentOperation }

    let getNodeStats (executionTime, nodeInfo, includeOnlyBombingScn) =
        let pluginStats = dep.WorkerPlugins |> Stream.ofList |> Stream.map(fun x -> x.GetStats())

        let scnStats =
            _scnSchedulers
            |> Stream.ofList
            |> Stream.filter(fun x -> x.Working = includeOnlyBombingScn)
            |> Stream.map(fun x -> x.GetScenarioStats executionTime)

        NodeStats.create sessionArgs.TestInfo nodeInfo scnStats pluginStats

    let getBombingOnlyNodeStats (executionTime, nodeInfo) = getNodeStats(executionTime, nodeInfo, true)
    let getFinalNodeStats (executionTime, nodeInfo) = getNodeStats(executionTime, nodeInfo, false)

    let execStopCommand (command: StopCommand) =
        match command with
        | StopScenario (scenarioName, reason) ->
            _scnSchedulers
            |> List.tryFind(fun sch -> sch.Scenario.ScenarioName = scenarioName)
            |> Option.iter(fun sch ->
                sch.Stop()
                dep.Logger.Warning("stopping scenario early: '{0}', reason: '{1}'", sch.Scenario.ScenarioName, reason)
            )

        | StopTest (reason) -> this.StopScenarios(reason) |> ignore

    let createScenarioSchedulers (targetScenarios: Scenario list) =

        let createScheduler (cancelToken: CancellationToken) (scn: Scenario) =
            let actorDep = {
                Logger = dep.Logger
                CancellationToken = cancelToken
                GlobalTimer = Stopwatch()
                Scenario = scn
                ExecStopCommand = execStopCommand
            }
            ScenarioScheduler(actorDep)

        _scnSchedulers |> List.iter(fun x -> x.Stop())
        _cancelToken.Dispose()
        _cancelToken <- new CancellationTokenSource()

        targetScenarios
        |> List.map(createScheduler _cancelToken.Token)

    let initScenarios () = taskResult {

        let defaultScnContext = Scenario.ScenarioContext.create(getCurrentNodeInfo(), _cancelToken.Token, dep.Logger)

        let! targetScenarios = registeredScenarios
                               |> TestHostScenario.filterTargetScenarios sessionArgs
                               |> TestHostScenario.initScenarios dep defaultScnContext sessionArgs

        TestHostPlugins.startPlugins(dep, sessionArgs.TestInfo)
        TestHostReporting.startReportingSinks(dep, sessionArgs.TestInfo)

        _targetScenarios <- targetScenarios
    }

    let stopScenarios () =
        if not _cancelToken.IsCancellationRequested then
            _cancelToken.Cancel()

    let cleanScenarios () =
        let defaultScnContext = Scenario.ScenarioContext.create(getCurrentNodeInfo(), _cancelToken.Token, dep.Logger)
        TestHostScenario.cleanScenarios(dep, defaultScnContext, _targetScenarios)

    let startBombing (isWarmUp) = task {
        _scnSchedulers <- createScenarioSchedulers(_targetScenarios)
        let progressBars = TestHostConsole.displayBombingProgress(dep, _scnSchedulers, isWarmUp)
        do! _scnSchedulers |> List.map(fun x -> x.Start(isWarmUp)) |> Task.WhenAll
        progressBars |> List.iter(fun x -> x.Dispose())
    }

    member _.TestInfo = sessionArgs.TestInfo
    member _.CurrentOperation = _currentOperation
    member _.CurrentNodeInfo = getCurrentNodeInfo()
    member _.RegisteredScenarios = registeredScenarios
    member _.TargetScenarios = _targetScenarios

    member _.StartInit() = task {
        _stopped <- false
        _currentOperation <- NodeOperationType.Init
        do! Task.Yield()

        TestHostConsole.printContextInfo(dep)

        dep.Logger.Information("starting init...")
        match! initScenarios() with
        | Ok _ ->
            dep.Logger.Information("init finished")
            _currentOperation <- NodeOperationType.None
            return Ok()

        | Error e ->
            dep.Logger.Information("init failed")
            _currentOperation <- NodeOperationType.Stop
            return AppError.createResult(InitScenarioError e)
    }

    member _.StartWarmUp() = task {
        _stopped <- false
        _currentOperation <- NodeOperationType.WarmUp
        do! Task.Yield()

        dep.Logger.Information("starting warm up...")
        let isWarmUp = true
        do! startBombing(isWarmUp)
        stopScenarios()

        _currentOperation <- NodeOperationType.None
    }

    member _.StartBombing() = task {
        _stopped <- false
        _currentOperation <- NodeOperationType.Bombing
        do! Task.Yield()

        dep.Logger.Information("starting bombing...")
        let isWarmUp = false
        do! startBombing(isWarmUp)
        do! this.StopScenarios()

        _currentOperation <- NodeOperationType.Complete
    }

    member _.StopScenarios([<Optional;DefaultParameterValue("":string)>]reason: string) = task {
        if _currentOperation <> NodeOperationType.Stop && not _stopped then
            _currentOperation <- NodeOperationType.Stop
            do! Task.Yield()

            if not(String.IsNullOrEmpty reason) then
                dep.Logger.Warning("stopping test early: '{0}'", reason)
            else
                dep.Logger.Information("stopping scenarios...")

            stopScenarios()
            cleanScenarios()
            TestHostPlugins.stopPlugins(dep)
            TestHostReporting.stopReportingSinks(dep)
            _stopped <- true

            _currentOperation <- NodeOperationType.None
    }

    member _.GetNodeStats(executionTime) = getNodeStats(executionTime)
    member _.GetTimeLineNodeStats() = List.rev _timeLineStats

    member _.RunSession() = taskResult {
        do! this.StartInit()

        let currentOperationTimer = Stopwatch()

        // warm-up
        currentOperationTimer.Restart()
        do! this.StartWarmUp()
        let nodeInfo = getCurrentNodeInfo()
        let warmUpStats = getFinalNodeStats(currentOperationTimer.Elapsed, nodeInfo)
        do! Scenario.Validation.validateWarmUpStats [warmUpStats]

        // we start real-time timer
        use reportingTimer =
            TestHostReporting.startReportingTimer(
                dep, sessionArgs.SendStatsInterval,
                (fun () ->
                    let operationTime = currentOperationTimer.Elapsed
                    let nodeInfo = getCurrentNodeInfo()
                    let nodeStats = getBombingOnlyNodeStats(operationTime, nodeInfo)
                    // prepend nodeStats to timeLineStats
                    _timeLineStats <- (operationTime, nodeStats) :: _timeLineStats
                    _currentOperation, nodeStats
                )
            )

        currentOperationTimer.Restart()

        // bombing
        do! this.StartBombing()
        reportingTimer.Stop()

        // saving final stats
        let nodeInfo = getCurrentNodeInfo()
        let finalStats = getFinalNodeStats(currentOperationTimer.Elapsed, nodeInfo)
        TestHostReporting.saveFinalStats dep [finalStats]

        return finalStats
    }

    interface IDisposable with
        member _.Dispose() =
            this.StopScenarios().Wait()
            dep.Dispose()
