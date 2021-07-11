module internal NBomber.Domain.HintsAnalyzer

open NBomber.Contracts
open NBomber.Contracts.Stats
open NBomber.Domain.DomainTypes

let private analyzeDataTransfer (scnStats: ScenarioStats) =

    let printHint (scnName, stepName) =
        $"Step '{stepName}' in scenario '{scnName}' didn't track data transfer." +
        " In order to track data transfer, you should use Response.Ok(sizeInBytes: value)"

    scnStats.StepStats
    |> Seq.filter(fun step -> step.Ok.DataTransfer.MinBytes + step.Fail.DataTransfer.MinBytes = 0)
    |> Seq.map(fun step -> {
        SourceName = scnStats.ScenarioName
        SourceType = HintSourceType.Scenario
        Hint = printHint(scnStats.ScenarioName, step.StepName)
    })

let private analyzeStatusCodes (scnStats: ScenarioStats) =

    let printHint (scnName, stepName) =
        $"Step '{stepName}' in scenario '{scnName}' didn't track status code." +
        " In order to track status code, you should use Response.Ok(statusCode: value)"

    scnStats.StepStats
    |> Seq.filter(fun step -> (step.Ok.Request.Count > 0 && Array.isEmpty step.Ok.StatusCodes) ||
                              (step.Fail.Request.Count > 0 && Array.isEmpty step.Fail.StatusCodes))
    |> Seq.map(fun step -> {
        SourceName = scnStats.ScenarioName
        SourceType = HintSourceType.Scenario
        Hint = printHint(scnStats.ScenarioName, step.StepName)
    })


let private checkDuplicateStepName (scenario: Scenario) =

    let printHint (scnName, stepName) =
        $"Scenario: '{scnName}' contains duplicate step names: '{stepName}'. You registered several steps with the same name but different implementations. Statistics results show it as one single step."

    scenario.Steps
    |> Seq.cast<IStep>
    |> Seq.distinct
    |> Seq.groupBy(fun x -> x.StepName)
    |> Seq.choose(fun (name, steps) -> if Seq.length(steps) > 1 then Some name else None)
    |> Seq.map(fun stepName -> {
        SourceName = scenario.ScenarioName
        SourceType = HintSourceType.Scenario
        Hint = printHint(scenario.ScenarioName, stepName)
    })

let analyzeNodeStats (stats: NodeStats) =
    stats.ScenarioStats
    |> Seq.collect(fun scnStats -> analyzeDataTransfer(scnStats) |> Seq.append(analyzeStatusCodes(scnStats)))
    |> Seq.toList

let analyzeScenarios (scenarios: Scenario list) =
    scenarios
    |> Seq.collect(checkDuplicateStepName)
    |> Seq.toList
