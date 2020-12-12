﻿module FSharp.XUnit.PluginStatsTest
//
//open System
//open System.Data
//open System.Linq
//open System.Threading.Tasks
//
//open FSharp.Control.Tasks.V2.ContextInsensitive
//open Xunit
//open Swensen.Unquote
//
//open NBomber.Contracts
//open NBomber.FSharp
//
//// in this example we use:
//// - XUnit (https://xunit.net/)
//// - Unquote (https://github.com/SwensenSoftware/unquote)
//// to get more info about test automation, please visit: (https://nbomber.com/docs/test-automation)
//
//type Plugin1 () =
//
//    let createTableCols () =
//        let colKey = new DataColumn("Key", Type.GetType("System.String"))
//        colKey.Caption <- "Key"
//
//        let colValue = new DataColumn("Value", Type.GetType("System.String"))
//        colValue.Caption <- "Value"
//
//        [| colKey; colValue |]
//
//    let createTableRows (count: int) (table: DataTable) = [|
//        for i in 1 .. count do
//            let row = table.NewRow()
//            row.["Key"] <- sprintf "Key%i" i
//            row.["Value"] <- sprintf "Value%i" i
//            yield row
//       |]
//
//    let createTable (rowsCount) (tableName) =
//        let table = new DataTable(tableName)
//        table.Columns.AddRange(createTableCols())
//        table |> createTableRows rowsCount |> Array.iter(table.Rows.Add)
//        table
//
//    let createPluginStats (currentOperation) =
//        let pluginStats = new DataSet()
//        pluginStats.Tables.Add(createTable 5 "PluginStats")
//        pluginStats
//
//    static member TryGetValueForKey(key: string, pluginStats: DataSet) =
//        pluginStats.Tables.Item("PluginStats")
//        |> fun table -> table.Rows.Cast<DataRow>().ToArray()
//        |> Array.tryFind(fun row -> row.["Key"].ToString() = key)
//        |> Option.map(fun row -> row.["Value"].ToString())
//
//    interface IWorkerPlugin with
//        member _.PluginName = "Plugin1"
//        member _.Init(_, _) = ()
//        member _.Start(_) = Task.CompletedTask
//        member _.GetStats(currentOperation) = createPluginStats(currentOperation)
//        member _.Stop() = Task.CompletedTask
//        member _.Dispose() = ()
//
//[<Fact>]
//let ``Plugin stats test`` () =
//
//    let plugin = new Plugin1()
//
//    let step = Step.create("step_1", fun context -> task {
//        return Response.Ok()
//    })
//
//    let nodeStats =
//        Scenario.create "scenario_1" [step]
//        |> Scenario.withWarmUpDuration(seconds 5)
//        |> Scenario.withLoadSimulations [InjectPerSec(rate = 100, during = seconds 30)]
//        |> NBomberRunner.registerScenario
//        |> NBomberRunner.withWorkerPlugins [plugin]
//        |> NBomberRunner.withTestSuite "assert_plugin_stats"
//        |> NBomberRunner.withTestName "simple_test"
//        |> NBomberRunner.run
//        |> function
//            | Ok stats -> stats
//            | Error e -> failwith e
//
//    let pluginStatsValue =
//        nodeStats
//        |> PluginStats.tryFindPluginStatsByName "Plugin1"
//        |> Option.bind(fun pluginStats -> Plugin1.TryGetValueForKey("Key1", pluginStats))
//        |> function
//          | Some v -> v
//          | None   -> failwith "No value was found"
//
//   test <@ pluginStatsValue = "Value1" @>
