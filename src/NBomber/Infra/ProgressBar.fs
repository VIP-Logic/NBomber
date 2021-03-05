module internal NBomber.Infra.ProgressBar

open System
open System.Threading.Tasks

open FSharp.Control.Tasks.NonAffine
open Spectre.Console
open Spectre.Console.Rendering

type MultilineColumn () =
    inherit ProgressColumn()

    static member val NewLine = "|" with get

    override _.NoWrap = false

    override _.Render(context: RenderContext, task: ProgressTask, deltaTime: TimeSpan) =
        let text = task.Description.Replace(MultilineColumn.NewLine, Environment.NewLine)
        Markup(text).RightAligned() :> IRenderable

let defaultColumns: ProgressColumn[] =
    [| MultilineColumn()
       ProgressBarColumn()
       PercentageColumn()
       ElapsedTimeColumn()
       SpinnerColumn() |]

type ProgressTaskConfig = {
    Description: string
    Ticks: float
}

let private createProgressTask (ctx: ProgressContext) (config: ProgressTaskConfig) =
    let task = ctx.AddTask(config.Description)

    if config.Ticks > 0.0 then
        task.MaxValue <- config.Ticks
        task.Increment(0.0)
    else
        // set 100% if number of ticks equal to 0
        task.MaxValue <- 1.0
        task.Increment(1.0)

    task

let create (created: ProgressTask list -> unit)
           (config: ProgressTaskConfig list) =

    AnsiConsole.Progress()
    |> fun progress -> ProgressExtensions.AutoRefresh(progress, true)
    |> fun progress -> ProgressExtensions.AutoClear(progress, false)
    |> fun progress -> ProgressExtensions.Columns(progress, defaultColumns)
    |> fun progress ->
        progress.StartAsync(fun ctx ->
            task {
                config |> List.map(createProgressTask ctx) |> created

                while not ctx.IsFinished do
                    do! Task.Delay(TimeSpan.FromMilliseconds 100.0)
            }
        )

let setDescription (task: ProgressTask) (description: string) =
    task.Description <- description
    task

let tick (task: ProgressTask) (progressTickInterval: float) =
    task.Increment(progressTickInterval)
    task

let getLeftNumberOfTicks (task: ProgressTask) (progressTickInterval: float) =
    (task.MaxValue - task.Value) * progressTickInterval
