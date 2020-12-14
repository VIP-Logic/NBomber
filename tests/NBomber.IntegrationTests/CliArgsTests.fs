module Tests.CliArgs

open System
open System.IO

open FSharp.Control.Tasks.NonAffine
open Swensen.Unquote
open Xunit

open NBomber.Contracts
open NBomber.Domain
open NBomber.FSharp

let okStep = Step.create("ok step", fun _ -> task {
    return Response.Ok()
})

let scenario = Scenario.create "scenario" [okStep] |> Scenario.withoutWarmUp

[<Theory>]
[<InlineData("-c")>]
[<InlineData("--config")>]
let ``correct CLI commands should load config`` (command) =
    let context =
        NBomberRunner.registerScenarios [scenario]
        |> NBomberRunner.executeCliArgs [command; "Configuration/test_config.json"]

    test <@ context.NBomberConfig.IsSome @>
    test <@ context.InfraConfig.IsNone @>

[<Theory>]
[<InlineData("-C")>]
[<InlineData("--Config")>]
[<InlineData("")>]
[<InlineData("-")>]
[<InlineData("--")>]
[<InlineData("-w")>]
let ``incorrect CLI command should not load config`` (command) =
    let context =
        NBomberRunner.registerScenarios [scenario]
        |> NBomberRunner.executeCliArgs [command; "Configuration/test_config.json"]

    test <@ context.NBomberConfig.IsNone @>
    test <@ context.InfraConfig.IsNone @>

[<Theory>]
[<InlineData("-i")>]
[<InlineData("--infra")>]
let ``correct CLI commands should load infra config`` (command) =
    let context =
        NBomberRunner.registerScenarios [scenario]
        |> NBomberRunner.executeCliArgs [command; "Configuration/infra_config.json"]

    test <@ context.NBomberConfig.IsNone @>
    test <@ context.InfraConfig.IsSome @>

[<Theory>]
[<InlineData("-I")>]
[<InlineData("--Infra")>]
[<InlineData("")>]
[<InlineData("-")>]
[<InlineData("--")>]
[<InlineData("-w")>]
let ``incorrect CLI command should not load infra config`` (command) =
    let context =
        NBomberRunner.registerScenarios [scenario]
        |> NBomberRunner.executeCliArgs [command; "Configuration/infra_config.json"]

    test <@ context.NBomberConfig.IsNone @>
    test <@ context.InfraConfig.IsNone @>

[<Theory>]
[<InlineData("-c")>]
[<InlineData("--config")>]
let ``CLI commands should throw ex if config file is not found`` (command) =
    Assert.Throws(typeof<FileNotFoundException>,
                  fun _ -> NBomberRunner.registerScenarios [scenario]
                           |> NBomberRunner.executeCliArgs [command; "not_found_config.json"]
                           |> ignore)

[<Theory>]
[<InlineData("-i")>]
[<InlineData("--infra")>]
let ``CLI commands should throw ex if infra config file is not found`` (command) =
    Assert.Throws(typeof<FileNotFoundException>,
                  fun _ -> NBomberRunner.registerScenarios [scenario]
                           |> NBomberRunner.executeCliArgs [command; "not_found_infra_config.json"]
                           |> ignore)

[<Theory>]
[<InlineData("-c")>]
[<InlineData("--config")>]
let ``CLI commands should throw ex if config file is empty`` (command) =
    Assert.Throws(typeof<Exception>,
                  fun _ -> NBomberRunner.registerScenarios [scenario]
                           |> NBomberRunner.executeCliArgs [command; ""]
                           |> ignore)

[<Theory>]
[<InlineData("-i")>]
[<InlineData("--infra")>]
let ``CLI commands should throw ex if infra config file is empty`` (command) =
    Assert.Throws(typeof<Exception>,
                  fun _ -> NBomberRunner.registerScenarios [scenario]
                           |> NBomberRunner.executeCliArgs [command; ""]
                           |> ignore)
