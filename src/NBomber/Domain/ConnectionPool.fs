module internal NBomber.Domain.ConnectionPool

open System
open System.Threading
open System.Threading.Tasks

open FsToolkit.ErrorHandling
open FSharp.Control.Reactive
open FSharp.Control.Tasks.NonAffine

open NBomber
open NBomber.Extensions.InternalExtensions
open NBomber.Contracts

type ConnectionPoolArgs<'TConnection>(name: string, connectionCount: int,
                                      openConnection: int * CancellationToken -> Task<'TConnection>,
                                      closeConnection: 'TConnection * CancellationToken -> Task) =
    let untypedArgs = lazy (
        ConnectionPoolArgs<obj>(
            name, connectionCount,
            openConnection = (fun (number, token) -> task {
                let! connection = openConnection(number, token)
                return connection :> obj
            }),
            closeConnection = (fun (connection, token) -> closeConnection(connection :?> 'TConnection, token))
        )
    )

    member _.PoolName = name
    member _.ConnectionCount = connectionCount
    member _.OpenConnection(number, token) = openConnection(number, token)
    member _.CloseConnection(connection, token) = closeConnection(connection, token)
    member _.GetUntyped() = untypedArgs
    member _.Clone(newName: string) = ConnectionPoolArgs<'TConnection>(newName, connectionCount, openConnection, closeConnection)
    member _.Clone(newConnectionCount: int) = ConnectionPoolArgs<'TConnection>(name, newConnectionCount, openConnection, closeConnection)

    interface IConnectionPoolArgs<'TConnection> with
        member _.PoolName = name
        member _.ConnectionCount = connectionCount
        member _.OpenConnection(number, token) = openConnection(number, token)
        member _.CloseConnection(connection, token) = closeConnection(connection, token)

type ConnectionPoolEvent =
    | StartedInit           of poolName:string
    | StartedStop           of poolName:string
    | ConnectionOpened      of poolName:string * connectionNumber:int
    | ConnectionClosed      of error:exn option
    | InitFinished
    | InitFailed

type ConnectionPool(args: ConnectionPoolArgs<obj>) =

    let mutable _disposed = false
    let mutable _aliveConnections = Array.empty
    let _eventStream = Subject.broadcast

    let initPool (token: CancellationToken) =

        let rec retryOpenConnection (connectionNumber, tryCount, token) =
            try
                let connection = args.OpenConnection(connectionNumber, token).Result
                Ok connection
            with
            | ex ->
                if tryCount >= Constants.TryCount then Error ex
                else retryOpenConnection(connectionNumber, tryCount + 1, token)

        let rec openConnections (connectionNumber, connectionCount, token) = seq {
            if connectionNumber < connectionCount then
                match retryOpenConnection(connectionNumber, 0, token) with
                | Ok connection ->
                    yield Ok connection
                    let displayNumber = connectionNumber + 1
                    _eventStream.OnNext(ConnectionOpened(args.PoolName, displayNumber))
                    yield! openConnections(connectionNumber + 1, connectionCount, token)

                | Error ex -> yield Error ex
        }

        _eventStream.OnNext(StartedInit(args.PoolName))

        let result = openConnections(0, args.ConnectionCount, token) |> Result.sequence
        match result with
        | Ok connections ->
            _aliveConnections <- connections |> List.toArray
            _eventStream.OnNext(InitFinished)
            Ok() |> Task.singleton

        | Error exs ->
            _eventStream.OnNext(InitFailed)
            exs.Head |> Error |> Task.singleton

    let closeAllConnections (token: CancellationToken) =
        _eventStream.OnNext(StartedStop args.PoolName)

        for connection in _aliveConnections do
            try
                args.CloseConnection(connection, token).Wait(Constants.OperationTimeOut) |> ignore
                _eventStream.OnNext(ConnectionClosed(error = None))
            with
            | ex ->
                _eventStream.OnNext(ConnectionClosed(error = Some ex))

    let destroy (token: CancellationToken) =
        if not _disposed then
            _disposed <- true
            closeAllConnections(token)
            _eventStream.OnCompleted()
            use e = _eventStream
            e |> ignore

    member _.PoolName = args.PoolName
    member _.ConnectionCount = args.ConnectionCount
    member _.AliveConnections = _aliveConnections
    member _.EventStream = _eventStream :> IObservable<_>
    member _.Init(token) = initPool(token)
    member _.Destroy(token) = destroy(token)

    interface IDisposable with
        member _.Dispose() = destroy(CancellationToken.None)
