module internal NBomber.Domain.Concurrency.Scheduler.ConstantActorScheduler

open System.Collections.Generic
open System.Threading.Tasks

open FSharp.Control.Tasks.NonAffine

open NBomber.Extensions.InternalExtensions
open NBomber.Domain.Concurrency
open NBomber.Domain.Concurrency.ScenarioActor

type ActorTask = Task

[<Struct>]
type SchedulerCommand =
    | KeepWorking
    | ReleaseFinishedActor
    | AddActors of count:int
    | StopScheduler

let removeFromScheduler (scheduledActorsCount, removeCount) =
    let actorsCount = scheduledActorsCount - removeCount
    if actorsCount < 0 then 0
    else actorsCount

let schedule (workingActorCount, scheduledActorCount) =
    if scheduledActorCount = 0 then StopScheduler
    elif workingActorCount = scheduledActorCount then KeepWorking
    elif workingActorCount < scheduledActorCount then AddActors(scheduledActorCount - workingActorCount)
    else ReleaseFinishedActor

type ConstantActorScheduler(dep: ActorDep) =

    let mutable _actorPool = List.empty<ScenarioActor>
    let _workingActors = Dictionary<ActorTask,ScenarioActor>()
    let mutable _scheduledActorCount = 0
    let mutable _stop = true

    let stop () =
        _stop <- true
        ScenarioActorPool.releaseActors(_actorPool)

    let startScheduler () =

        _stop <- false

        let waitActors (workingActors: Dict<ActorTask,ScenarioActor>) = task {
            let! finishedTask = Task.WhenAny(workingActors.Keys)
            let finishedActor = workingActors.[finishedTask]
            workingActors.Remove(finishedTask) |> ignore
            return finishedActor
        }

        let addAndExecActor (actor: ScenarioActor) =
            let startedActorTask = actor.ExecSteps()
            _workingActors.[startedActorTask] <- actor

        task {
            do! Task.Yield()

            while not _stop || not dep.CancellationToken.IsCancellationRequested do

                try
                    match schedule(_workingActors.Count, _scheduledActorCount) with
                    | KeepWorking          ->
                        let! finishedActor = waitActors(_workingActors)
                        addAndExecActor(finishedActor)

                    | ReleaseFinishedActor ->
                        let! finishedActor = waitActors(_workingActors)
                        finishedActor.ReserveForScheduler()

                    | AddActors count ->
                        let result = ScenarioActorPool.rentActors(dep, _actorPool, count)
                        _actorPool <- ScenarioActorPool.updatePool(_actorPool, result)
                        result.ActorsFromPool @ result.NewActors |> List.iter(addAndExecActor)

                    | StopScheduler -> stop()
                with
                | ex -> dep.Logger.Error(ex.ToString())

            stop()
        }

    member _.AvailableActors = _actorPool
    member _.WorkingActorCount = _scheduledActorCount
    member _.Stop() = stop()

    member _.AddActors(count) =
        _scheduledActorCount <- _scheduledActorCount + count
        if _stop then startScheduler() |> ignore

    member _.RemoveActors(count) =
        _scheduledActorCount <- removeFromScheduler(_scheduledActorCount, count)
